using System.Diagnostics;
using System.Runtime.CompilerServices;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.Application.Authorization;
using SharedKernel.Application.Pipeline.Metrics;
using SharedKernel.Application.Pipeline.Streaming;
using SharedKernel.Application.Pipeline.Tests.Support;
using SharedKernel.Application.Streaming;
using SharedKernel.Core.Exceptions;
using SharedKernel.Execution.Context;
using SharedKernel.Primitives.Errors;
using SharedKernel.Testing.Logging;
using SharedKernel.Testing.Telemetry;

namespace SharedKernel.Application.Pipeline.Tests.Streaming;

public sealed class StreamObservabilityBehaviorTests
{
    private const string SentinelFaultText = "sentinel-fault-text-4f1b";
    private const string SentinelPermission = "sentinel.permission.7c2d";

    private sealed record Numbers(int Count) : IStreamQuery<int>;

    private sealed record Faulting(int ItemsBeforeTheFault) : IStreamQuery<int>;

    private sealed record Hanging : IStreamQuery<int>;

    private sealed record Canceling : IStreamQuery<int>;

    [RequirePermission(SentinelPermission)]
    private sealed record Protected(int Count) : IStreamQuery<int>;

    private sealed class Journal
    {
        public Exception? Thrown { get; set; }

        public int ProtectedHandlerCalls { get; set; }
    }

    private sealed class SentinelFault() : InvalidOperationException(SentinelFaultText);

    private sealed class NumbersHandler : IStreamQueryHandler<Numbers, int>
    {
        public async IAsyncEnumerable<int> Handle(Numbers request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            for (var index = 0; index < request.Count; index++)
            {
                await Task.Yield();
                yield return index;
            }
        }
    }

    private sealed class FaultingHandler(Journal journal) : IStreamQueryHandler<Faulting, int>
    {
        public async IAsyncEnumerable<int> Handle(Faulting request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            for (var index = 0; index < request.ItemsBeforeTheFault; index++)
            {
                await Task.Yield();
                yield return index;
            }

            journal.Thrown = new SentinelFault();
            throw journal.Thrown;
        }
    }

    private sealed class HangingHandler : IStreamQueryHandler<Hanging, int>
    {
        public async IAsyncEnumerable<int> Handle(Hanging request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return 0;
            await Task.Delay(Timeout.Infinite, cancellationToken);
            yield return 1;
        }
    }

    private sealed class CancelingHandler : IStreamQueryHandler<Canceling, int>
    {
        public async IAsyncEnumerable<int> Handle(Canceling request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            yield return 0;
            await Task.Yield();
            throw new OperationCanceledException();
        }
    }

    private sealed class ProtectedHandler(Journal journal) : IStreamQueryHandler<Protected, int>
    {
        public async IAsyncEnumerable<int> Handle(Protected request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            journal.ProtectedHandlerCalls++;
            for (var index = 0; index < request.Count; index++)
            {
                await Task.Yield();
                yield return index;
            }
        }
    }

    private static (ServiceProvider Provider, Journal Journal) Build(IRequestContext? caller = null)
    {
        var journal = new Journal();
        var services = new ServiceCollection();
        services.AddInMemoryLoggerFactory();
        services.AddSingleton(journal);
        if (caller is not null)
            services.AddSingleton(caller);
        services.AddSharedKernelApplication(typeof(StreamObservabilityBehaviorTests).Assembly, app => app.UseMediatR());
        return (services.BuildServiceProvider(), journal);
    }

    private static StreamRequestPipeline<TRequest, int> Pipeline<TRequest>(IServiceProvider provider)
        where TRequest : IStreamQuery<int>
        => provider.GetRequiredService<StreamRequestPipeline<TRequest, int>>();

    private static KeyValuePair<string, object?>[] MetricTags<TRequest>(string? outcome = null)
        => outcome is null
            ? [TelemetryProbe.Tag("application.request_type", typeof(TRequest).FullName), TelemetryProbe.Tag("application.request_kind", "stream")]
            :
            [
                TelemetryProbe.Tag("application.request_type", typeof(TRequest).FullName),
                TelemetryProbe.Tag("application.request_kind", "stream"),
                TelemetryProbe.Tag("outcome", outcome),
            ];

    private static KeyValuePair<string, object?>[] ItemTags<TRequest>(string outcome)
        =>
        [
            TelemetryProbe.Tag("application.request_type", typeof(TRequest).FullName),
            TelemetryProbe.Tag("outcome", outcome),
        ];

    private static async Task<List<int>> ReadAll(IAsyncEnumerable<int> stream)
    {
        var items = new List<int>();
        await foreach (var item in stream)
            items.Add(item);
        return items;
    }

    [Fact]
    public async Task Stream_RunToTheEnd_RecordsSpanDurationItemsAndDebugLog()
    {
        using var probe = new TelemetryProbe();
        var (provider, _) = Build();
        using var host = provider;

        var items = await ReadAll(Pipeline<Numbers>(provider).Handle(new Numbers(3)));

        items.Should().Equal(0, 1, 2);
        probe.Spans.ShouldHaveSingleSpan("application stream")
            .ShouldHaveKind(ActivityKind.Internal)
            .ShouldHaveSource("SharedKernel.Application")
            .ShouldHaveTag("application.request_type", typeof(Numbers).FullName)
            .ShouldHaveTag("application.request_kind", "stream")
            .ShouldHaveOutcome("success")
            .ShouldBeRecordedSuccess();
        probe.Spans.ShouldFollowSpanConvention();
        probe.Metrics
            .ShouldHaveInstrument(ApplicationMetrics.RequestDurationName, "s", InstrumentKind.Histogram)
            .ShouldHaveInstrument(ApplicationMetrics.StreamItemsName, "{item}", InstrumentKind.Counter)
            .ShouldHaveMeasurement(ApplicationMetrics.RequestDurationName, null, MetricTags<Numbers>("success"))
            .ShouldHaveSum(ApplicationMetrics.StreamItemsName, 3, ItemTags<Numbers>("success"));
        probe.Metrics.Count(ApplicationMetrics.RequestDurationName, MetricTags<Numbers>()).Should().Be(1);
        probe.Records(provider).ShouldHaveLogged(new EventId(5140), LogLevel.Debug)
            .TryGetProperty("ItemCount", out var count).Should().BeTrue();
        count.Should().Be(3L);
    }

    [Fact]
    public async Task Stream_Empty_RecordsTheSameShapeWithZeroItems()
    {
        using var probe = new TelemetryProbe();
        var (provider, _) = Build();
        using var host = provider;

        var items = await ReadAll(Pipeline<Numbers>(provider).Handle(new Numbers(0)));

        items.Should().BeEmpty();
        probe.Spans.ShouldHaveSingleSpan("application stream").ShouldHaveOutcome("success");
        probe.Metrics
            .ShouldHaveMeasurement(ApplicationMetrics.RequestDurationName, null, MetricTags<Numbers>("success"))
            .ShouldHaveSum(ApplicationMetrics.StreamItemsName, 0, ItemTags<Numbers>("success"));
        probe.Metrics.Count(ApplicationMetrics.StreamItemsName, ItemTags<Numbers>("success")).Should().Be(1);
    }

    [Fact]
    public async Task Stream_NeverEnumerated_RecordsNothing()
    {
        using var probe = new TelemetryProbe();
        var (provider, _) = Build();
        using var host = provider;

        var stream = Pipeline<Numbers>(provider).Handle(new Numbers(3));
        await using (stream.GetAsyncEnumerator())
        {
            // Opened but never advanced: the iterator body, and so the span, never starts.
        }

        probe.Spans.ShouldNotHaveSpan("application stream");
        probe.Metrics.Count(ApplicationMetrics.RequestDurationName, MetricTags<Numbers>()).Should().Be(0);
        probe.Metrics.Count(ApplicationMetrics.StreamItemsName, TelemetryProbe.Tag("application.request_type", typeof(Numbers).FullName)).Should().Be(0);
        probe.Records(provider).Where(record => record.EventId.Id is >= 5140 and <= 5143).Should().BeEmpty();
    }

    [Fact]
    public async Task Stream_ConsumerBreaksEarly_IsCanceledNotFailed()
    {
        using var probe = new TelemetryProbe();
        var (provider, _) = Build();
        using var host = provider;

        await foreach (var item in Pipeline<Numbers>(provider).Handle(new Numbers(10)))
        {
            if (item == 1)
                break;
        }

        probe.Spans.ShouldHaveSingleSpan("application stream")
            .ShouldBeRecordedCancellation()
            .ShouldHaveOutcome("canceled");
        probe.Metrics
            .ShouldHaveMeasurement(ApplicationMetrics.RequestDurationName, null, MetricTags<Numbers>("canceled"))
            .ShouldHaveSum(ApplicationMetrics.StreamItemsName, 2, ItemTags<Numbers>("canceled"))
            .ShouldNotCarryErrorTypeFor(typeof(Numbers));
        var records = probe.Records(provider);
        records.ShouldHaveLogged(new EventId(5141), LogLevel.Debug);
        records.Should().NotContain(record => record.LogLevel >= LogLevel.Warning);
    }

    [Fact]
    public async Task Stream_CallerTokenCanceledMidEnumeration_IsCanceledNotFailed()
    {
        using var probe = new TelemetryProbe();
        var (provider, _) = Build();
        using var host = provider;
        using var cts = new CancellationTokenSource();

        var act = async () =>
        {
            await foreach (var item in Pipeline<Hanging>(provider).Handle(new Hanging(), cts.Token))
            {
                item.Should().Be(0);
                await cts.CancelAsync();
            }
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
        probe.Spans.ShouldHaveSingleSpan("application stream")
            .ShouldBeRecordedCancellation()
            .ShouldHaveOutcome("canceled");
        probe.Metrics
            .ShouldHaveMeasurement(ApplicationMetrics.RequestDurationName, null, MetricTags<Hanging>("canceled"))
            .ShouldHaveSum(ApplicationMetrics.StreamItemsName, 1, ItemTags<Hanging>("canceled"))
            .ShouldNotCarryErrorTypeFor(typeof(Hanging));
        probe.Records(provider).ShouldHaveLogged(new EventId(5141), LogLevel.Debug);
        probe.Records(provider).Should().NotContain(record => record.LogLevel >= LogLevel.Warning);
    }

    [Fact]
    public async Task Stream_HandlerThrowsOperationCanceled_IsCanceledNotFailed()
    {
        using var probe = new TelemetryProbe();
        var (provider, _) = Build();
        using var host = provider;

        var act = () => ReadAll(Pipeline<Canceling>(provider).Handle(new Canceling()));

        await act.Should().ThrowAsync<OperationCanceledException>();
        probe.Spans.ShouldHaveSingleSpan("application stream")
            .ShouldBeRecordedCancellation()
            .ShouldHaveOutcome("canceled");
        probe.Metrics
            .ShouldHaveMeasurement(ApplicationMetrics.RequestDurationName, null, MetricTags<Canceling>("canceled"))
            .ShouldHaveSum(ApplicationMetrics.StreamItemsName, 1, ItemTags<Canceling>("canceled"))
            .ShouldNotCarryErrorTypeFor(typeof(Canceling));
        probe.Records(provider).Should().NotContain(record => record.LogLevel >= LogLevel.Warning);
    }

    [Fact]
    public async Task Stream_HandlerFaultsAfterItems_RecordsTheExceptionShapeAndRethrowsTheSameInstance()
    {
        using var probe = new TelemetryProbe();
        var (provider, journal) = Build();
        using var host = provider;
        var seen = new List<int>();

        var act = async () =>
        {
            await foreach (var item in Pipeline<Faulting>(provider).Handle(new Faulting(2)))
                seen.Add(item);
        };

        var thrown = (await act.Should().ThrowAsync<SentinelFault>()).Which;
        thrown.Should().BeSameAs(journal.Thrown);
        seen.Should().Equal(0, 1);

        probe.Spans.ShouldHaveSingleSpan("application stream")
            .ShouldBeRecordedException(typeof(SentinelFault))
            .ShouldHaveOutcome("exception");
        probe.Metrics
            .ShouldHaveMeasurement(
                ApplicationMetrics.RequestDurationName,
                null,
                [.. MetricTags<Faulting>("exception"), TelemetryProbe.Tag("error.type", typeof(SentinelFault).FullName)])
            .ShouldHaveSum(ApplicationMetrics.StreamItemsName, 2, ItemTags<Faulting>("exception"));

        var records = probe.Records(provider);
        var log = records.ShouldHaveLogged(new EventId(5143), LogLevel.Error);
        log.Exception.Should().BeSameAs(journal.Thrown);
        records.Where(record => record.EventId.Id is >= 5140 and <= 5143).Should().ContainSingle();
        ShouldNotMentionSentinel(records);
        probe.Metrics.ShouldNotLeak(SentinelFaultText);
    }

    [Fact]
    public async Task Stream_DeniedUnauthenticated_IsObservedAsAClassifiedFailureAndNeverRunsTheHandler()
    {
        using var probe = new TelemetryProbe();
        var (provider, journal) = Build(new FakeRequestContext(isAuthenticated: false));
        using var host = provider;

        var act = () => ReadAll(Pipeline<Protected>(provider).Handle(new Protected(2)));

        await act.Should().ThrowAsync<UnauthorizedException>();
        journal.ProtectedHandlerCalls.Should().Be(0);
        probe.Spans.ShouldHaveSingleSpan("application stream")
            .ShouldBeRecordedException(typeof(UnauthorizedException), ErrorCodes.Unauthorized.Default)
            .ShouldHaveOutcome("exception");
        var records = probe.Records(provider);
        records.ShouldHaveLogged(new EventId(5142), LogLevel.Warning)
            .TryGetProperty("ErrorCode", out var code).Should().BeTrue();
        code.Should().Be(ErrorCodes.Unauthorized.Default);
        records.ShouldNotLeak(SentinelPermission);
        probe.Spans.ShouldNotLeak(SentinelPermission);
        probe.Metrics.ShouldNotLeak(SentinelPermission);
    }

    [Fact]
    public async Task Stream_DeniedForbidden_IsObservedAsAClassifiedFailure()
    {
        using var probe = new TelemetryProbe();
        var (provider, journal) = Build(new FakeRequestContext(isAuthenticated: true, new HashSet<string>()));
        using var host = provider;

        var act = () => ReadAll(Pipeline<Protected>(provider).Handle(new Protected(2)));

        await act.Should().ThrowAsync<ForbiddenException>();
        journal.ProtectedHandlerCalls.Should().Be(0);
        probe.Spans.ShouldHaveSingleSpan("application stream")
            .ShouldBeRecordedException(typeof(ForbiddenException), ErrorCodes.Forbidden.InsufficientPermission);
        probe.Records(provider).ShouldHaveLogged(new EventId(5142), LogLevel.Warning);
        probe.Records(provider).ShouldNotLeak(SentinelPermission);
    }

    [Fact]
    public async Task Stream_Allowed_DoesNotLeakThePermissionAndIsASuccess()
    {
        using var probe = new TelemetryProbe();
        var (provider, journal) = Build(new FakeRequestContext(isAuthenticated: true, new HashSet<string> { SentinelPermission }));
        using var host = provider;

        var items = await ReadAll(Pipeline<Protected>(provider).Handle(new Protected(2)));

        items.Should().Equal(0, 1);
        journal.ProtectedHandlerCalls.Should().Be(1);
        probe.Spans.ShouldHaveSingleSpan("application stream").ShouldBeRecordedSuccess();
        probe.Spans.ShouldNotLeak(SentinelPermission);
        probe.Records(provider).ShouldNotLeak(SentinelPermission);
        probe.Metrics.ShouldNotLeak(SentinelPermission);
    }

    [Fact]
    public async Task Stream_LogEvents_FollowTheLogConventionsInThePipelineSubBlock()
    {
        using var probe = new TelemetryProbe();
        var (provider, _) = Build(new FakeRequestContext(isAuthenticated: false));
        using var host = provider;

        await ReadAll(Pipeline<Numbers>(provider).Handle(new Numbers(1)));
        await FluentActions.Awaiting(() => ReadAll(Pipeline<Faulting>(provider).Handle(new Faulting(1))))
            .Should().ThrowAsync<SentinelFault>();
        await FluentActions.Awaiting(() => ReadAll(Pipeline<Protected>(provider).Handle(new Protected(1))))
            .Should().ThrowAsync<UnauthorizedException>();
        await using (var enumerator = Pipeline<Numbers>(provider).Handle(new Numbers(5)).GetAsyncEnumerator())
        {
            (await enumerator.MoveNextAsync()).Should().BeTrue();
        }

        IReadOnlyList<LogRecord> records =
            [.. probe.Records(provider).Where(record => record.EventId.Id is >= 5140 and <= 5143)];
        records.Select(record => record.EventId.Id).Should().BeEquivalentTo([5140, 5141, 5142, 5143]);
        records.ShouldFollowLogConvention()
            .ShouldOnlyLogEventIdsInSubBlock(SharedKernel.Primitives.Logging.LoggingEventIdRanges.Application, 1);
    }

    private static void ShouldNotMentionSentinel(IReadOnlyList<LogRecord> records)
    {
        // The exception object carries its own message by design; nothing else may.
        foreach (var record in records)
        {
            record.Message.Should().NotContain(SentinelFaultText);
            record.Template.Should().NotContain(SentinelFaultText);
            (record.State ?? []).Select(property => property.Value?.ToString())
                .Should().NotContain(value => value != null && value.Contains(SentinelFaultText));
        }
    }
}
