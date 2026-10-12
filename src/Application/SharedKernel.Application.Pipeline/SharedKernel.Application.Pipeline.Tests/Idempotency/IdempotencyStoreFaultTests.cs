using System.Diagnostics;
using FluentAssertions;
using SharedKernel.Core.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.Application.Idempotency;
using SharedKernel.Application.Messaging;
using SharedKernel.Application.Pipeline.Tests.Support;
using SharedKernel.Execution.Context;
using SharedKernel.Idempotency.Abstractions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Testing.Logging;
using SharedKernel.Testing.Telemetry;

namespace SharedKernel.Application.Pipeline.Tests.Idempotency;

/// <summary>
/// The idempotency store call is the fault boundary: a begin fault fails closed as Unavailable, a complete or release
/// fault is logged and never replaces the handler's outcome.
/// </summary>
public sealed class IdempotencyStoreFaultTests
{
    private const string SentinelFaultText = "sentinel-store-fault-b41f";
    private const string SecretKey = "secret-idempotency-key-77d2";

    private sealed record Charge(string IdempotencyKey, bool Fails = false, bool Throws = false)
        : ICommand<string>, IIdempotentRequest;

    private sealed class StoreFault() : InvalidOperationException(SentinelFaultText);

    private sealed class Journal
    {
        public int HandlerRuns { get; set; }

        public Exception? Thrown { get; set; }
    }

    private sealed class ChargeHandler(Journal journal) : ICommandHandler<Charge, string>
    {
        public Task<Result<string>> Handle(Charge request, CancellationToken cancellationToken)
        {
            journal.HandlerRuns++;
            if (request.Throws)
            {
                journal.Thrown = new InvalidOperationException("handler-fault");
                throw journal.Thrown;
            }

            return Task.FromResult(request.Fails
                ? Result<string>.Failure(Error.BusinessRule("test.declined", "Declined."))
                : Result<string>.Success("charged"));
        }
    }

    private static (ServiceProvider Provider, FakeIdempotencyStore Store, Journal Journal) Build()
    {
        var journal = new Journal();
        var store = new FakeIdempotencyStore();
        var services = new ServiceCollection();
        services.AddInMemoryLoggerFactory();
        services.AddSingleton(journal);
        services.AddSingleton<IRequestContext>(new FakeRequestContext(isAuthenticated: true));
        services.AddKeyedSingleton<IIdempotencyStore>(IdempotencyPurpose.Request, store);
        services.AddSharedKernelApplication(typeof(IdempotencyStoreFaultTests).Assembly, app => app.UseMediatR().WithIdempotency());
        return (services.BuildServiceProvider(), store, journal);
    }

    private static Task<Result<string>> Send(IServiceProvider provider, Charge command)
        => provider.GetRequiredService<ISender>().Send(command);

    [Fact]
    public async Task Begin_RawStoreException_FailsClosedAsUnavailableAndTheHandlerDoesNotRun()
    {
        using var probe = new TelemetryProbe();
        var (provider, store, journal) = Build();
        using var host = provider;
        var fault = new StoreFault();
        store.BeginFault = fault;

        var result = await Send(provider, new Charge(SecretKey));

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Unavailable);
        result.Error.Code.Should().Be(ErrorCodes.Idempotency.StoreUnavailable).And.Be("idempotency.store_unavailable");
        result.Error.Message.Should().NotContain(SentinelFaultText).And.NotContain(SecretKey);
        journal.HandlerRuns.Should().Be(0);

        var records = probe.Records(provider);
        var log = records.ShouldHaveLogged(new EventId(5121), LogLevel.Error);
        log.Exception.Should().BeSameAs(fault);
        foreach (var record in records)
        {
            record.Message.Should().NotContain(SentinelFaultText).And.NotContain(SecretKey);
            record.Template.Should().NotContain(SentinelFaultText).And.NotContain(SecretKey);
            (record.State ?? []).Select(property => property.Value?.ToString())
                .Should().NotContain(value => value != null && (value.Contains(SentinelFaultText) || value.Contains(SecretKey)));
        }

        // The request span (opened by the tracing behavior) carries the exception event; its status comes from the Result.
        var request = probe.Spans.Single(span => span.OperationName == nameof(Charge));
        request.ShouldHaveEvent("exception");
        request.ShouldHaveTag("error.code", "idempotency.store_unavailable");
        request.Tags.Select(tag => tag.Value).Should().NotContain(value => value != null && value.Contains(SentinelFaultText));
    }

    [Fact]
    public async Task Begin_ClassifiedStoreException_ReturnsItsOwnErrorUnchanged()
    {
        using var probe = new TelemetryProbe();
        var (provider, store, journal) = Build();
        using var host = provider;
        var own = Error.Timeout("idempotency.store_timeout", "The store timed out.");
        store.BeginFault = own.ToException();

        var result = await Send(provider, new Charge(SecretKey));

        result.Error.Should().Be(own);
        journal.HandlerRuns.Should().Be(0);
        probe.Records(provider).ShouldHaveLogged(new EventId(5121), LogLevel.Warning);
    }

    [Fact]
    public async Task Begin_CallerCancellation_Propagates()
    {
        var (provider, store, journal) = Build();
        using var host = provider;
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        store.BeginFault = new OperationCanceledException(cts.Token);

        var act = () => provider.GetRequiredService<ISender>().Send(new Charge(SecretKey), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        journal.HandlerRuns.Should().Be(0);
    }

    [Fact]
    public async Task Begin_CancellationNotRequestedByTheCaller_IsAStoreFault()
    {
        var (provider, store, journal) = Build();
        using var host = provider;
        store.BeginFault = new TaskCanceledException("store call timed out");

        var result = await Send(provider, new Charge(SecretKey));

        result.Error.Code.Should().Be(ErrorCodes.Idempotency.StoreUnavailable);
        journal.HandlerRuns.Should().Be(0);
    }

    [Fact]
    public async Task Complete_Fault_ReturnsTheResponseLogsAndRunsTheHandlerOnce()
    {
        using var probe = new TelemetryProbe();
        var (provider, store, journal) = Build();
        using var host = provider;
        var fault = new StoreFault();
        store.CompleteFault = fault;

        var result = await Send(provider, new Charge(SecretKey));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("charged");
        journal.HandlerRuns.Should().Be(1);
        probe.Records(provider).ShouldHaveLogged(new EventId(5122), LogLevel.Error).Exception.Should().BeSameAs(fault);
        probe.Records(provider).Where(record => record.EventId.Id == 5122).Should().ContainSingle();
    }

    [Fact]
    public async Task Release_FaultAfterAFailedResult_ReturnsTheOriginalFailure()
    {
        using var probe = new TelemetryProbe();
        var (provider, store, _) = Build();
        using var host = provider;
        var fault = new StoreFault();
        store.ReleaseFault = fault;

        var result = await Send(provider, new Charge(SecretKey, Fails: true));

        result.Error.Code.Should().Be("test.declined");
        store.ReleaseCallCount.Should().Be(1);
        probe.Records(provider).ShouldHaveLogged(new EventId(5123), LogLevel.Warning).Exception.Should().BeSameAs(fault);
    }

    [Fact]
    public async Task Release_FaultAfterAHandlerException_PropagatesTheHandlersOwnException()
    {
        using var probe = new TelemetryProbe();
        var (provider, store, journal) = Build();
        using var host = provider;
        var fault = new StoreFault();
        store.ReleaseFault = fault;

        var act = () => Send(provider, new Charge(SecretKey, Throws: true));

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(journal.Thrown);
        probe.Records(provider).ShouldHaveLogged(new EventId(5123), LogLevel.Warning).Exception.Should().BeSameAs(fault);
    }

    [Fact]
    public async Task HandlerException_IsNotCaughtAsAStoreFault()
    {
        using var probe = new TelemetryProbe();
        var (provider, store, journal) = Build();
        using var host = provider;

        var act = () => Send(provider, new Charge(SecretKey, Throws: true));

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(journal.Thrown);
        store.ReleaseCallCount.Should().Be(1);
        probe.Records(provider).Where(record => record.EventId.Id is 5121 or 5122 or 5123).Should().BeEmpty();
    }

    [Fact]
    public async Task StoreFaultLogEvents_FollowTheLogConventionsInThePipelineSubBlock_AndNeverCarryTheKey()
    {
        using var probe = new TelemetryProbe();
        var (provider, store, _) = Build();
        using var host = provider;

        store.BeginFault = new StoreFault();
        await Send(provider, new Charge(SecretKey));
        store.BeginFault = null;
        store.CompleteFault = new StoreFault();
        await Send(provider, new Charge(SecretKey + "-2"));
        store.CompleteFault = null;
        store.ReleaseFault = new StoreFault();
        await Send(provider, new Charge(SecretKey + "-3", Fails: true));

        IReadOnlyList<LogRecord> records =
            [.. probe.Records(provider).Where(record => record.EventId.Id is >= 5121 and <= 5123)];
        records.Select(record => record.EventId.Id).Should().BeEquivalentTo([5121, 5122, 5123]);
        records.ShouldFollowLogConvention()
            .ShouldOnlyLogEventIdsInSubBlock(SharedKernel.Primitives.Logging.LoggingEventIdRanges.Application, 1);
        foreach (var record in records)
        {
            record.Message.Should().NotContain(SecretKey);
            (record.State ?? []).Select(property => property.Value?.ToString())
                .Should().NotContain(value => value != null && value.Contains(SecretKey));
        }
    }
}
