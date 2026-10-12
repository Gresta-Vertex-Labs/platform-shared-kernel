using System.Diagnostics;
using FluentAssertions;
using SharedKernel.Core.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.Application.DomainEvents;
using SharedKernel.Application.Pipeline.DomainEvents;
using SharedKernel.Application.Pipeline.Tests.Support;
using SharedKernel.Domain.Abstractions;
using SharedKernel.Domain.Events;
using SharedKernel.Primitives.Errors;
using SharedKernel.Testing.Logging;
using SharedKernel.Testing.Telemetry;

namespace SharedKernel.Application.Pipeline.Tests.DomainEvents;

public sealed class DomainEventDispatcherTelemetryTests
{
    private const string SentinelFaultText = "sentinel-handler-fault-9a3e";
    private const string SentinelPayload = "sentinel-event-payload-5d71";

    private sealed record AlphaEvent(string Payload = SentinelPayload) : DomainEvent;

    private sealed record BetaEvent : DomainEvent;

    private sealed record UnhandledEvent : DomainEvent;

    private sealed class SentinelFault() : InvalidOperationException(SentinelFaultText);

    private sealed class Journal
    {
        public List<string> Entries { get; } = [];

        public Exception? ThrowFromFirstAlpha { get; set; }
    }

    private sealed class AlphaOne(Journal journal) : IDomainEventHandler<AlphaEvent>
    {
        public Task Handle(AlphaEvent domainEvent, CancellationToken cancellationToken)
        {
            journal.Entries.Add("alpha:1");
            return journal.ThrowFromFirstAlpha is { } exception ? Task.FromException(exception) : Task.CompletedTask;
        }
    }

    private sealed class AlphaTwo(Journal journal) : IDomainEventHandler<AlphaEvent>
    {
        public Task Handle(AlphaEvent domainEvent, CancellationToken cancellationToken)
        {
            journal.Entries.Add("alpha:2");
            return Task.CompletedTask;
        }
    }

    private sealed class BetaOne(Journal journal) : IDomainEventHandler<BetaEvent>
    {
        public Task Handle(BetaEvent domainEvent, CancellationToken cancellationToken)
        {
            journal.Entries.Add("beta:1");
            return Task.CompletedTask;
        }
    }

    private sealed class BetaTwo(Journal journal) : IDomainEventHandler<BetaEvent>
    {
        public Task Handle(BetaEvent domainEvent, CancellationToken cancellationToken)
        {
            journal.Entries.Add("beta:2");
            return Task.CompletedTask;
        }
    }

    private static (ServiceProvider Provider, Journal Journal) Build()
    {
        var journal = new Journal();
        var services = new ServiceCollection();
        services.AddInMemoryLoggerFactory();
        services.AddSingleton(journal);
        services.AddSharedKernelApplication(typeof(SharedKernel.Application.Commands.ICommandScope).Assembly);
        services.AddDomainEventHandler<AlphaEvent, AlphaOne>();
        services.AddDomainEventHandler<AlphaEvent, AlphaTwo>();
        services.AddDomainEventHandler<BetaEvent, BetaOne>();
        services.AddDomainEventHandler<BetaEvent, BetaTwo>();
        return (services.BuildServiceProvider(), journal);
    }

    [Fact]
    public async Task Dispatch_TwoEventsWithTwoHandlers_RecordsOneParentSpanAndOneChildPerEventTypeInOrder()
    {
        using var probe = new TelemetryProbe();
        var (provider, journal) = Build();
        using var host = provider;
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>()
            .DispatchAsync([new AlphaEvent { OccurredOn = DateTimeOffset.UtcNow }, new BetaEvent { OccurredOn = DateTimeOffset.UtcNow }], CancellationToken.None);

        journal.Entries.Should().Equal("alpha:1", "alpha:2", "beta:1", "beta:2");
        var dispatch = probe.Spans.ShouldHaveSingleSpan("application dispatch_events")
            .ShouldHaveKind(ActivityKind.Internal)
            .ShouldHaveSource("SharedKernel.Application")
            .ShouldHaveTag("application.event_count", 2)
            .ShouldHaveOutcome("success")
            .ShouldBeRecordedSuccess();
        var handled = probe.Spans.Where(span => span.OperationName == "application handle_event").ToList();
        handled.Should().HaveCount(2);
        handled[0].ShouldHaveTag("application.event_type", typeof(AlphaEvent).FullName).ShouldHaveParent(dispatch)
            .ShouldHaveKind(ActivityKind.Internal).ShouldBeRecordedSuccess();
        handled[1].ShouldHaveTag("application.event_type", typeof(BetaEvent).FullName).ShouldHaveParent(dispatch)
            .ShouldBeRecordedSuccess();
        probe.Spans.Where(span => span.OperationName.StartsWith("application ", StringComparison.Ordinal)).ShouldFollowSpanConvention();
        probe.Records(provider).Should().BeEmpty("a successful dispatch logs nothing");
    }

    [Fact]
    public async Task Dispatch_EventWithoutHandler_HasNoChildSpan()
    {
        using var probe = new TelemetryProbe();
        var (provider, _) = Build();
        using var host = provider;
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>()
            .DispatchAsync([new UnhandledEvent { OccurredOn = DateTimeOffset.UtcNow }, new BetaEvent { OccurredOn = DateTimeOffset.UtcNow }], CancellationToken.None);

        probe.Spans.ShouldHaveSingleSpan("application dispatch_events").ShouldHaveTag("application.event_count", 2);
        var handled = probe.Spans.Where(span => span.OperationName == "application handle_event").ToList();
        handled.Should().ContainSingle().Which.ShouldHaveTag("application.event_type", typeof(BetaEvent).FullName);
    }

    [Fact]
    public async Task Dispatch_EmptyList_HasNoSpan()
    {
        using var probe = new TelemetryProbe();
        var (provider, _) = Build();
        using var host = provider;
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>()
            .DispatchAsync([], CancellationToken.None);

        probe.Spans.Should().BeEmpty();
    }

    [Fact]
    public async Task Dispatch_HandlerFaults_RecordsBothSpansLogsOnceAndRethrowsTheSameInstance()
    {
        using var probe = new TelemetryProbe();
        var (provider, journal) = Build();
        using var host = provider;
        var fault = new SentinelFault();
        journal.ThrowFromFirstAlpha = fault;
        using var scope = provider.CreateScope();

        var act = () => scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>()
            .DispatchAsync([new AlphaEvent { OccurredOn = DateTimeOffset.UtcNow }, new BetaEvent { OccurredOn = DateTimeOffset.UtcNow }], CancellationToken.None);

        (await act.Should().ThrowAsync<SentinelFault>()).Which.Should().BeSameAs(fault);
        journal.Entries.Should().Equal("alpha:1");

        var dispatch = probe.Spans.ShouldHaveSingleSpan("application dispatch_events")
            .ShouldBeRecordedException(typeof(SentinelFault))
            .ShouldHaveOutcome("exception");
        var handled = probe.Spans.Where(span => span.OperationName == "application handle_event").ToList();
        handled.Should().ContainSingle().Which
            .ShouldBeRecordedException(typeof(SentinelFault))
            .ShouldHaveTag("application.handler_type", typeof(AlphaOne).FullName)
            .ShouldHaveParent(dispatch);

        var records = probe.Records(provider);
        var log = records.ShouldHaveLogged(new EventId(5150), LogLevel.Error);
        log.Exception.Should().BeSameAs(fault);
        log.TryGetProperty("EventType", out var eventType).Should().BeTrue();
        eventType.Should().Be(typeof(AlphaEvent).FullName);
        log.TryGetProperty("HandlerType", out var handlerType).Should().BeTrue();
        handlerType.Should().Be(typeof(AlphaOne).FullName);
        records.Should().ContainSingle();
        foreach (var record in records)
        {
            record.Message.Should().NotContain(SentinelFaultText).And.NotContain(SentinelPayload);
            (record.State ?? []).Select(property => property.Value?.ToString())
                .Should().NotContain(value => value != null && (value.Contains(SentinelFaultText) || value.Contains(SentinelPayload)));
        }

        ((IReadOnlyList<LogRecord>)records).ShouldFollowLogConvention()
            .ShouldOnlyLogEventIdsInSubBlock(SharedKernel.Primitives.Logging.LoggingEventIdRanges.Application, 1);
        probe.Spans.ShouldNotLeak(SentinelPayload);
    }

    [Fact]
    public async Task Dispatch_ClassifiedHandlerFault_CarriesTheErrorCodeAndLogsAWarning()
    {
        using var probe = new TelemetryProbe();
        var (provider, journal) = Build();
        using var host = provider;
        var fault = Error.Conflict("test.handler_conflict", "A conflict.").ToException();
        journal.ThrowFromFirstAlpha = fault;
        using var scope = provider.CreateScope();

        var act = () => scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>()
            .DispatchAsync([new AlphaEvent { OccurredOn = DateTimeOffset.UtcNow }], CancellationToken.None);

        (await act.Should().ThrowAsync<Exception>()).Which.Should().BeSameAs(fault);
        probe.Spans.ShouldHaveSingleSpan("application dispatch_events")
            .ShouldBeRecordedException(fault.GetType(), "test.handler_conflict");
        probe.Spans.ShouldHaveSingleSpan("application handle_event")
            .ShouldBeRecordedException(fault.GetType(), "test.handler_conflict");
        probe.Records(provider).ShouldHaveLogged(new EventId(5150), LogLevel.Warning).Exception.Should().BeSameAs(fault);
    }

    [Fact]
    public async Task Dispatch_HandlerCanceled_IsNotAnErrorAndIsNotLogged()
    {
        using var probe = new TelemetryProbe();
        var (provider, journal) = Build();
        using var host = provider;
        journal.ThrowFromFirstAlpha = new OperationCanceledException();
        using var scope = provider.CreateScope();

        var act = () => scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>()
            .DispatchAsync([new AlphaEvent { OccurredOn = DateTimeOffset.UtcNow }], CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
        probe.Spans.ShouldHaveSingleSpan("application dispatch_events").ShouldBeRecordedCancellation().ShouldHaveOutcome("canceled");
        probe.Spans.ShouldHaveSingleSpan("application handle_event").ShouldBeRecordedCancellation().ShouldHaveOutcome("canceled");
        probe.Records(provider).Should().BeEmpty();
    }

    [Fact]
    public void Dispatcher_ResolvesItsLoggerFromDependencyInjection()
    {
        var (provider, _) = Build();
        using var host = provider;
        using var scope = provider.CreateScope();

        _ = scope.ServiceProvider.GetRequiredService<IDomainEventDispatcher>();

        ((InMemoryLoggerFactory)provider.GetRequiredService<ILoggerFactory>())
            .ShouldHaveCategory(typeof(DomainEventDispatcher));
    }
}
