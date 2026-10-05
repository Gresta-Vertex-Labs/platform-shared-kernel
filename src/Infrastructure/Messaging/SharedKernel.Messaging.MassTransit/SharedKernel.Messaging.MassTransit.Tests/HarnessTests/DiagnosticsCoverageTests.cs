#pragma warning disable CS8602 // MassTransit harness IPublishedMessage/IReceivedMessage nullable context
using System.Diagnostics;
using System.Diagnostics.Metrics;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharedKernel.Contracts.Events;
using SharedKernel.Messaging.Abstractions.EventPublisher;
using SharedKernel.Messaging.Abstractions.MessageBus;
using SharedKernel.Messaging.Abstractions.Options;
using SharedKernel.Messaging.MassTransit.Consumers;
using SharedKernel.Messaging.MassTransit.EventPublisher;
using SharedKernel.Messaging.MassTransit.Extensions;
using SharedKernel.Messaging.MassTransit.MessageBus;
using Activity = System.Diagnostics.Activity;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace SharedKernel.Messaging.MassTransit.Tests.HarnessTests;

/// <summary>
/// DC-06/DC-07: full-dispatch-surface diagnostics coverage tests (P-348/WO-054).
/// DC-06 proves <c>MassTransitMessageBus.SendAsync</c> emits an <see cref="Activity"/> with the correct
/// <see cref="Activity.OperationName"/> and tags — the three verbs that previously produced
/// no activity at all. DC-07 proves each of <see cref="MessagingDiagnostics.Meter"/>'s five
/// instruments records on its corresponding operation (publish, consume, duration, retry, fault).
/// </summary>
public sealed class DiagnosticsCoverageTests
{
    private const string SourceName = "SharedKernel.Messaging";

    // -------------------------------------------------------------------------
    // DC-06a: SendAsync produces a "MessageBus.Send" activity
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SendAsync_ProducesMessageBusSendActivity_WithMessageTypeTag()
    {
        // ConcurrentBag throughout this class, never List: ActivityListener and MeterListener callbacks
        // are process-wide, so parallel test classes call them concurrently. A List throws "Collection
        // was modified", and an exception from ActivityStopped faults another test's consume pipeline.
        var capturedActivities = new System.Collections.Concurrent.ConcurrentBag<Activity>();
        var activityStopped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                capturedActivities.Add(activity);
                activityStopped.TrySetResult(true);
            },
        };
        ActivitySource.AddActivityListener(listener);

        var sendEndpointProvider = Substitute.For<ISendEndpointProvider>();
        var sendEndpoint = Substitute.For<ISendEndpoint>();
        sendEndpointProvider.GetSendEndpoint(Arg.Any<Uri>()).Returns(Task.FromResult(sendEndpoint));
        sendEndpoint.Send(Arg.Any<DcSendMessage>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var options = MsOptions.Create(new MessagingOptions { ServiceName = "dc-send-service" });
        var bus = new MassTransitMessageBus(
            publishEndpoint: Substitute.For<IPublishEndpoint>(),
            sendEndpointProvider: sendEndpointProvider,
            serviceProvider: Substitute.For<IServiceProvider>(),
            routeMap: new Dictionary<Type, string>(),
            resolver: new ConventionSendEndpointResolver(options));

        await bus.SendAsync(new DcSendMessage("send-activity-check"), CancellationToken.None);

        await activityStopped.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Filter by the message-type tag rather than global singularity — see OTelInstrumentationTests
        // for the documented parallel-test-isolation hazard (the ActivityListener is process-wide).
        var sendActivity = capturedActivities.Should().ContainSingle(a =>
                a.OperationName == "MessageBus.Send" &&
                Equals(a.GetTagItem("messaging.message_type"), nameof(DcSendMessage)))
            .Subject;
        sendActivity.GetTagItem("messaging.message_type").Should().Be(nameof(DcSendMessage));
    }


    // -------------------------------------------------------------------------
    // DC-07a: messaging.publish.count records on IEventPublisher.PublishAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task EventPublisher_PublishAsync_RecordsPublishCounter_TaggedWithEventType()
    {
        var measurements = new System.Collections.Concurrent.ConcurrentBag<(string InstrumentName, long Value, KeyValuePair<string, object?>[] Tags)>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == SourceName)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
            measurements.Add((instrument.Name, measurement, tags.ToArray())));
        listener.Start();

        var services = new ServiceCollection();
        services.AddMassTransitTestHarness();
        services.Configure<MessagingOptions>(o => o.ServiceName = "dc-meter-publish-service");
        services.AddScoped<IEventPublisher, MassTransitEventPublisher>();

        await using var provider = services.BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        using var scope = provider.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
        var evt = new DcPublishIntegrationEvent(Guid.NewGuid(), DateTimeOffset.UtcNow, Guid.NewGuid());

        await publisher.PublishAsync(evt, CancellationToken.None);
        await harness.Stop();

        measurements.Should().Contain(m =>
                m.InstrumentName == "messaging.publish.count" &&
                m.Value == 1 &&
                m.Tags.Any(t => t.Key == "messaging.event_type" && Equals(t.Value, DcPublishIntegrationEvent.EventName)),
            "messaging.publish.count must record exactly one measurement tagged with the event's " +
            "[IntegrationEvent] name (never the CLR class name) " +
            "after a successful IEventPublisher.PublishAsync call");
    }

    // -------------------------------------------------------------------------
    // DC-07b: messaging.publish.count records on IMessageBus.PublishAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task MessageBus_PublishAsync_RecordsPublishCounter_TaggedWithMessageType()
    {
        var measurements = new System.Collections.Concurrent.ConcurrentBag<(string InstrumentName, long Value, KeyValuePair<string, object?>[] Tags)>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == SourceName)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
            measurements.Add((instrument.Name, measurement, tags.ToArray())));
        listener.Start();

        var publishEndpoint = Substitute.For<IPublishEndpoint>();
        publishEndpoint.Publish(Arg.Any<DcMessageBusPublishMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var options = MsOptions.Create(new MessagingOptions { ServiceName = "dc-meter-messagebus-publish" });
        var bus = new MassTransitMessageBus(
            publishEndpoint: publishEndpoint,
            sendEndpointProvider: Substitute.For<ISendEndpointProvider>(),
            serviceProvider: Substitute.For<IServiceProvider>(),
            routeMap: new Dictionary<Type, string>(),
            resolver: new ConventionSendEndpointResolver(options));

        await bus.PublishAsync(new DcMessageBusPublishMessage("publish-counter-check"), CancellationToken.None);

        measurements.Should().Contain(m =>
                m.InstrumentName == "messaging.publish.count" &&
                m.Value == 1 &&
                m.Tags.Any(t => t.Key == "messaging.message_type" && Equals(t.Value, nameof(DcMessageBusPublishMessage))),
            "messaging.publish.count must record exactly one measurement tagged with the message type " +
            "after a successful IMessageBus.PublishAsync call");
    }

    // -------------------------------------------------------------------------
    // DC-07c/DC-07d: messaging.consume.count and messaging.consume.duration record on
    // ConsumerBase<TMessage>.Consume success
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConsumerBase_Consume_RecordsConsumeCounterAndDuration_TaggedWithMessageType()
    {
        var longMeasurements = new System.Collections.Concurrent.ConcurrentBag<(string InstrumentName, long Value, KeyValuePair<string, object?>[] Tags)>();
        var doubleMeasurements = new System.Collections.Concurrent.ConcurrentBag<(string InstrumentName, double Value, KeyValuePair<string, object?>[] Tags)>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == SourceName)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
            longMeasurements.Add((instrument.Name, measurement, tags.ToArray())));
        listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, _) =>
            doubleMeasurements.Add((instrument.Name, measurement, tags.ToArray())));
        listener.Start();

        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(cfg => cfg.AddConsumer<DcConsumeMessageConsumer>())
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(new DcConsumeMessage("consume-metric-check"));
        (await harness.Consumed.Any<DcConsumeMessage>()).Should().BeTrue();

        await harness.Stop();

        longMeasurements.Should().Contain(m =>
                m.InstrumentName == "messaging.consume.count" &&
                m.Value == 1 &&
                m.Tags.Any(t => t.Key == "messaging.message_type" && Equals(t.Value, nameof(DcConsumeMessage))),
            "messaging.consume.count must record exactly one measurement after a successful ConsumeAsync");

        doubleMeasurements.Should().Contain(m =>
                m.InstrumentName == "messaging.consume.duration" &&
                m.Value >= 0 &&
                m.Tags.Any(t => t.Key == "messaging.message_type" && Equals(t.Value, nameof(DcConsumeMessage))),
            "messaging.consume.duration must record a non-negative duration for the same consume operation");
    }

    // -------------------------------------------------------------------------
    // DC-07e: messaging.retry.count records when the retry filter re-delivers a message
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConsumerBase_Consume_RecordsRetryCounter_WhenRetryFilterRedeliversMessage()
    {
        var measurements = new System.Collections.Concurrent.ConcurrentBag<(string InstrumentName, long Value, KeyValuePair<string, object?>[] Tags)>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == SourceName)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
            measurements.Add((instrument.Name, measurement, tags.ToArray())));
        listener.Start();

        DcRetryTracker.Reset();

        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddConsumer<DcRetryMessageConsumer>();
                cfg.UsingInMemory((ctx, busCfg) =>
                {
                    busCfg.UseMessageRetry(r => r.Immediate(2));
                    busCfg.ConfigureEndpoints(ctx);
                });
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(new DcRetryMessage("retry-metric-check"));
        (await harness.Consumed.Any<DcRetryMessage>()).Should().BeTrue(
            "the consumer fails once then succeeds on the retry-filter re-delivery");

        await harness.Stop();

        measurements.Should().Contain(m =>
                m.InstrumentName == "messaging.retry.count" &&
                m.Tags.Any(t => t.Key == "messaging.message_type" && Equals(t.Value, nameof(DcRetryMessage))),
            "messaging.retry.count must record at least one measurement when ConsumeContext.GetRetryAttempt() > 0");
    }

    // -------------------------------------------------------------------------
    // DC-07f: messaging.fault.count records when FaultConsumerAdapter observes a fault
    // -------------------------------------------------------------------------

    [Fact]
    public async Task FaultConsumerAdapter_Consume_RecordsFaultCounter_TaggedWithMessageType()
    {
        var measurements = new System.Collections.Concurrent.ConcurrentBag<(string InstrumentName, long Value, KeyValuePair<string, object?>[] Tags)>();

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == SourceName)
                l.EnableMeasurementEvents(instrument);
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
            measurements.Add((instrument.Name, measurement, tags.ToArray())));
        listener.Start();

        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddConsumer<DcFaultMessageConsumer>();
                cfg.AddConsumer<FaultConsumerAdapter<DcFaultMessage, DcNoOpFaultConsumer>>();
                cfg.UsingInMemory((ctx, busCfg) =>
                {
                    busCfg.UseMessageRetry(r => r.Immediate(0));
                    busCfg.ConfigureEndpoints(ctx);
                });
            })
            .AddScoped<DcNoOpFaultConsumer>()
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(new DcFaultMessage("fault-metric-check"));
        (await harness.Consumed.Any<Fault<DcFaultMessage>>()).Should().BeTrue(
            "FaultConsumerAdapter must consume the Fault<TMessage> envelope");

        await harness.Stop();

        measurements.Should().Contain(m =>
                m.InstrumentName == "messaging.fault.count" &&
                m.Value == 1 &&
                m.Tags.Any(t => t.Key == "messaging.message_type" && Equals(t.Value, nameof(DcFaultMessage))),
            "messaging.fault.count must record exactly one measurement when FaultConsumerAdapter observes a fault");
    }
}

// ---------------------------------------------------------------------------
// Message / event / argument types — internal, no 'file' modifier (MassTransit type matching)
// ---------------------------------------------------------------------------

internal sealed record DcSendMessage(string Text);
internal sealed record DcMessageBusPublishMessage(string Text);
internal sealed record DcConsumeMessage(string Text);
internal sealed record DcRetryMessage(string Text);
internal sealed record DcFaultMessage(string Text);

[IntegrationEvent(DcPublishIntegrationEvent.EventName)]
internal sealed record DcPublishIntegrationEvent(Guid EventId, DateTimeOffset OccurredOn, Guid OrderId) : IIntegrationEvent
{
    public const string EventName = "tests.messaging.diagnostics.publish-counted";
}

// ---------------------------------------------------------------------------
// Consumers
// ---------------------------------------------------------------------------

internal sealed class DcConsumeMessageConsumer : ConsumerBase<DcConsumeMessage>
{
    public DcConsumeMessageConsumer() : base(NullLogger.Instance) { }

    protected override Task ConsumeAsync(DcConsumeMessage message, CancellationToken ct) => Task.CompletedTask;
}

/// <summary>Fails on the first delivery, succeeds on the retry-filter re-delivery.</summary>
internal sealed class DcRetryMessageConsumer : ConsumerBase<DcRetryMessage>
{
    public DcRetryMessageConsumer() : base(NullLogger.Instance) { }

    protected override Task ConsumeAsync(DcRetryMessage message, CancellationToken ct)
    {
        if (!DcRetryTracker.HasFailedOnce)
        {
            DcRetryTracker.HasFailedOnce = true;
            throw new InvalidOperationException("simulated first-attempt failure");
        }

        return Task.CompletedTask;
    }
}

internal static class DcRetryTracker
{
    public static bool HasFailedOnce { get; set; }

    public static void Reset() => HasFailedOnce = false;
}

internal sealed class DcFaultMessageConsumer : ConsumerBase<DcFaultMessage>
{
    public DcFaultMessageConsumer() : base(NullLogger.Instance) { }

    protected override Task ConsumeAsync(DcFaultMessage message, CancellationToken ct) =>
        throw new InvalidOperationException("dc fault metric test failure");
}

internal sealed class DcNoOpFaultConsumer : SharedKernel.Messaging.Abstractions.Faults.IFaultConsumer<DcFaultMessage>
{
    public Task HandleAsync(
        Guid faultId,
        DateTimeOffset faultTimestamp,
        DcFaultMessage faultedMessage,
        IReadOnlyList<SharedKernel.Messaging.Abstractions.Faults.FaultExceptionInfo> exceptions,
        CancellationToken ct) => Task.CompletedTask;
}
