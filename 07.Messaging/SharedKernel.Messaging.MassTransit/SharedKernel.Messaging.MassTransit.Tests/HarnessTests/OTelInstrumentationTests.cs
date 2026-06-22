#pragma warning disable CS8602 // MassTransit harness IPublishedMessage/IReceivedMessage nullable context
using System.Diagnostics;
using FluentAssertions;
using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Contracts.Events;
using SharedKernel.Domain.Events;
using SharedKernel.Messaging.Abstractions.EventPublisher;
using SharedKernel.Messaging.Abstractions.Options;
using SharedKernel.Messaging.MassTransit.Consumers;
using SharedKernel.Messaging.MassTransit.EventPublisher;

namespace SharedKernel.Messaging.MassTransit.Tests.HarnessTests;

/// <summary>
/// OT-05/OT-06/OT-07: <c>MessagingDiagnostics.ActivitySource</c> instrumentation tests.
/// Verifies <see cref="ConsumerBase{TMessage}.Consume"/> and
/// <see cref="MassTransitEventPublisher.PublishAsync{TEvent}(TEvent, CancellationToken)"/>
/// each produce an <see cref="Activity"/> from the <c>"SharedKernel.Messaging"</c> source,
/// and that the consumer log scope is enriched with <c>messaging.destination</c> and
/// <c>messaging.message_type</c>.
/// </summary>
public sealed class OTelInstrumentationTests
{
    private const string SourceName = "SharedKernel.Messaging";

    // -------------------------------------------------------------------------
    // OT-05: ConsumerBase.Consume produces a "Consumer.Consume" activity
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Consume_ProducesConsumerConsumeActivity_WithMessageTypeTag()
    {
        var capturedActivities = new List<Activity>();
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

        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddConsumer<OTelRecordingConsumer>();
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(new OTelTestMessage("activity-check"));

        (await harness.Consumed.Any<OTelTestMessage>()).Should().BeTrue();
        await activityStopped.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await harness.Stop();

        // Filter by the message-type tag rather than asserting global singularity — the
        // ActivityListener is process-wide and other test classes running concurrently
        // also drive ConsumerBase<T>.Consume(), emitting their own "Consumer.Consume" activities.
        var consumeActivity = capturedActivities.Should().ContainSingle(a =>
                a.OperationName == "Consumer.Consume" &&
                Equals(a.GetTagItem("messaging.message_type"), nameof(OTelTestMessage)))
            .Subject;
        consumeActivity.GetTagItem("messaging.message_type").Should().Be(nameof(OTelTestMessage));
    }

    // -------------------------------------------------------------------------
    // OT-06: MassTransitEventPublisher.PublishAsync produces an "EventPublisher.Publish" activity
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PublishAsync_ProducesEventPublisherPublishActivity_WithEventTypeTag()
    {
        var capturedActivities = new List<Activity>();
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

        var services = new ServiceCollection();
        services.AddMassTransitTestHarness();
        services.Configure<MessagingOptions>(o => o.ServiceName = "otel-publish-service");
        services.AddScoped<IEventPublisher, MassTransitEventPublisher>();

        await using var provider = services.BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        using var scope = provider.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
        var evt = new OTelTestDomainEvent { OccurredOn = DateTimeOffset.UtcNow, OrderId = Guid.NewGuid() };

        await publisher.PublishAsync(evt, CancellationToken.None);

        await activityStopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await harness.Stop();

        // Filter by the event-type tag rather than asserting global singularity — see note above.
        var publishActivity = capturedActivities
            .Should().ContainSingle(a =>
                a.OperationName == "EventPublisher.Publish" &&
                Equals(a.GetTagItem("messaging.event_type"), nameof(OTelTestDomainEvent)))
            .Subject;
        publishActivity.GetTagItem("messaging.event_type").Should().Be(nameof(OTelTestDomainEvent));
    }

    [Fact]
    public async Task PublishAsync_WhenThrowsForNonDomainEvent_StillDisposesActivity()
    {
        var capturedActivities = new List<Activity>();
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

        var services = new ServiceCollection();
        services.AddMassTransitTestHarness();
        services.Configure<MessagingOptions>(o => o.ServiceName = "otel-publish-throw-service");
        services.AddScoped<IEventPublisher, MassTransitEventPublisher>();

        await using var provider = services.BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        using var scope = provider.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
        var plainObject = new OTelPlainMessage("not-a-domain-event");

        var act = async () => await publisher.PublishAsync(plainObject, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await activityStopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await harness.Stop();

        // The activity must still be started (and disposed) even though the publish call throws.
        // Filter by the event-type tag (set before the IDomainEvent guard runs) rather than
        // asserting global singularity — see note in PublishAsync_ProducesEventPublisherPublishActivity...
        capturedActivities.Should().ContainSingle(a =>
            a.OperationName == "EventPublisher.Publish" &&
            Equals(a.GetTagItem("messaging.event_type"), nameof(OTelPlainMessage)));
    }

    // -------------------------------------------------------------------------
    // OT-07: Consumer log scope contains messaging.destination / messaging.message_type
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Consume_LogScope_ContainsDestinationAndMessageType_WhenDestinationAddressSet()
    {
        OTelLogScopeCaptureStore.Reset();
        var capturingLogger = new OTelCapturingLogger();

        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddConsumer<OTelLogScopeCapturingConsumer>();
                cfg.UsingInMemory((ctx, busCfg) => busCfg.ConfigureEndpoints(ctx));
            })
            .AddSingleton(capturingLogger)
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        await harness.Bus.Publish(new OTelLogScopeTestMessage("destination-check"));

        (await harness.Consumed.Any<OTelLogScopeTestMessage>()).Should().BeTrue();

        await harness.Stop();

        OTelLogScopeCaptureStore.CapturedKeys.Should().Contain("messaging.message_type",
            "messaging.message_type must always be present in the consumer log scope");
        OTelLogScopeCaptureStore.CapturedKeys.Should().Contain("messaging.destination",
            "messaging.destination must be present when ConsumeContext.DestinationAddress is set " +
            "(MassTransit TestHarness always assigns a receive endpoint address)");
    }

    [Fact]
    public async Task Consume_LogScope_NoException_WhenLoggerHasNoDestination()
    {
        // Sanity coverage for the null-DestinationAddress branch: a NullLogger-backed
        // consumer must not throw even though we cannot directly null out
        // ConsumeContext.DestinationAddress via TestHarness (MassTransit always assigns one).
        // This exercises the full Consume() pipeline end-to-end without raising.
        await using var provider = new ServiceCollection()
            .AddMassTransitTestHarness(cfg =>
            {
                cfg.AddConsumer<OTelNullLoggerConsumer>();
            })
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var act = async () => await harness.Bus.Publish(new OTelLogScopeTestMessage("no-destination-null-check"));

        await act.Should().NotThrowAsync();
        (await harness.Consumed.Any<OTelLogScopeTestMessage>()).Should().BeTrue();

        await harness.Stop();
    }
}

// ---------------------------------------------------------------------------
// Message / event types — internal, no 'file' modifier (MassTransit type matching)
// ---------------------------------------------------------------------------

internal sealed record OTelTestMessage(string Text);
internal sealed record OTelLogScopeTestMessage(string Text);
internal sealed record OTelPlainMessage(string Text);

internal sealed record OTelTestDomainEvent : DomainEvent
{
    public Guid OrderId { get; init; }
}

// ---------------------------------------------------------------------------
// Consumers
// ---------------------------------------------------------------------------

internal sealed class OTelRecordingConsumer : ConsumerBase<OTelTestMessage>
{
    public OTelRecordingConsumer() : base(NullLogger.Instance) { }

    protected override Task ConsumeAsync(OTelTestMessage message, CancellationToken ct) => Task.CompletedTask;
}

internal sealed class OTelNullLoggerConsumer : ConsumerBase<OTelLogScopeTestMessage>
{
    public OTelNullLoggerConsumer() : base(NullLogger.Instance) { }

    protected override Task ConsumeAsync(OTelLogScopeTestMessage message, CancellationToken ct) => Task.CompletedTask;
}

internal sealed class OTelLogScopeCapturingConsumer : ConsumerBase<OTelLogScopeTestMessage>
{
    public OTelLogScopeCapturingConsumer(OTelCapturingLogger logger) : base(logger) { }

    protected override Task ConsumeAsync(OTelLogScopeTestMessage message, CancellationToken ct) => Task.CompletedTask;
}

// ---------------------------------------------------------------------------
// Static store for log-scope key capture
// ---------------------------------------------------------------------------

internal static class OTelLogScopeCaptureStore
{
    private static readonly System.Collections.Concurrent.ConcurrentBag<string> _keys = [];
    public static IEnumerable<string> CapturedKeys => _keys;
    public static void AddKey(string key) => _keys.Add(key);

    public static void Reset()
    {
        while (_keys.TryTake(out _)) { }
    }
}

// ---------------------------------------------------------------------------
// Logger that captures BeginScope keys for assertions
// ---------------------------------------------------------------------------

internal sealed class OTelCapturingLogger : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        if (state is IDictionary<string, object?> dict)
        {
            foreach (var key in dict.Keys)
                OTelLogScopeCaptureStore.AddKey(key);
        }
        return NullScope.Instance;
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter) { }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
    }
}
