using System.Collections;
using System.Diagnostics;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SharedKernel.Messaging.Abstractions.Faults;
using SharedKernel.Messaging.MassTransit.Consumers;
using SharedKernel.Messaging.MassTransit.Diagnostics;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Testing.Logging;
using SharedKernel.Testing.Telemetry;
using MtHeaders = MassTransit.Serialization.DictionarySendHeaders;

namespace SharedKernel.Messaging.MassTransit.Tests.ObservabilityTests;

/// <summary>
/// The telemetry and logs of the consumer bases and the fault adapter: span name, kind and tags, the failure shapes, the
/// <c>outcome</c> vocabulary, the duration in seconds, and each consume log by <c>EventId</c>, level, template and
/// exception. Every test uses a message type of its own and filters by it, because the source and meter are process-wide.
/// </summary>
public sealed class ConsumeTelemetryTests
{
    private const string Consume = "sharedkernel.messaging.consume.count";
    private const string Duration = "sharedkernel.messaging.consume.duration";

    [Fact]
    public async Task Consume_Success_RecordsAConsumerSpanTaggedWithTheDestination_AndCountsSuccess()
    {
        using var spans = ActivityRecorder.StartRecording(MessagingTelemetry.SourceName);
        using var metrics = MetricRecorder.StartRecording(MessagingTelemetry.MeterName);
        var consumer = new OkConsumer(new InMemoryLogger());

        await consumer.Consume(ContextFor(new ObsConsumeOkMessage("a")));

        SpanFor(spans, "messaging consume", nameof(ObsConsumeOkMessage))
            .ShouldHaveKind(ActivityKind.Consumer)
            .ShouldHaveSource(MessagingTelemetry.SourceName)
            .ShouldHaveTag(MessagingTagKeys.Destination, "/obs-queue")
            .ShouldHaveTagKeys(MessagingTagKeys.MessageType, MessagingTagKeys.Destination)
            .ShouldBeRecordedSuccess()
            .ShouldFollowSpanConvention();
        metrics.Sum(Consume, Tag(nameof(ObsConsumeOkMessage)), Outcome(WellKnownOutcomes.Success)).Should().Be(1);
        metrics.ShouldHaveInstrument(Consume, "{message}", InstrumentKind.Counter);
        metrics.ShouldHaveInstrument(Duration, "s", InstrumentKind.Histogram);
        metrics.ShouldHaveInstrument("sharedkernel.messaging.retry.count", "{retry}", InstrumentKind.Counter);
        metrics.ShouldHaveInstrument("sharedkernel.messaging.fault.count", "{fault}", InstrumentKind.Counter);
        metrics.ShouldHaveInstrument("sharedkernel.messaging.publish.count", "{message}", InstrumentKind.Counter);
        metrics.ShouldHaveInstrument("sharedkernel.messaging.send.count", "{message}", InstrumentKind.Counter);
        metrics.ShouldUseOutcomeVocabulary(Consume);
        metrics.ShouldUseOutcomeVocabulary(Duration);
    }

    [Fact]
    public async Task Consume_RecordsTheDurationInSeconds_NotMilliseconds()
    {
        using var metrics = MetricRecorder.StartRecording(MessagingTelemetry.MeterName);
        var consumer = new SlowConsumer(new InMemoryLogger());

        await consumer.Consume(ContextFor(new ObsConsumeSlowMessage("a")));

        var recorded = metrics.MeasurementsOf(Duration)
            .Single(m => Equals(m.Tags.Single(t => t.Key == MessagingTagKeys.MessageType).Value, nameof(ObsConsumeSlowMessage)));
        recorded.Value.Should().BeInRange(0.14, 5, "a 150 ms consumer records about 0.15 seconds, not 150");
    }

    [Fact]
    public async Task Consume_Exception_IsRecordedAsAnException_CountsException_LogsError_AndRethrows()
    {
        using var spans = ActivityRecorder.StartRecording(MessagingTelemetry.SourceName);
        using var metrics = MetricRecorder.StartRecording(MessagingTelemetry.MeterName);
        var logger = new InMemoryLogger();
        var consumer = new ThrowingConsumer(logger, new InvalidOperationException("defect in broker.sentinel-host.internal"));

        var act = async () => await consumer.Consume(ContextFor(new ObsConsumeBugMessage("a")));

        await act.Should().ThrowAsync<InvalidOperationException>();
        SpanFor(spans, "messaging consume", nameof(ObsConsumeBugMessage)).ShouldBeRecordedException(typeof(InvalidOperationException));
        metrics.Sum(Consume, Tag(nameof(ObsConsumeBugMessage)), Outcome(WellKnownOutcomes.Exception)).Should().Be(1);
        metrics.Sum(Consume, Tag(nameof(ObsConsumeBugMessage)), Outcome(WellKnownOutcomes.Success)).Should().Be(0);

        var records = logger.Records;
        records.ShouldHaveLogged(new EventId(MessagingLoggingEventIds.ConsumeFailed), LogLevel.Error);
        records.ShouldHaveLoggedWithException(new EventId(MessagingLoggingEventIds.ConsumeFailed), typeof(InvalidOperationException));
        records.ShouldHaveLoggedTemplate(new EventId(MessagingLoggingEventIds.ConsumeFailed), "Consuming a message of type '{MessageType}' failed.");
        records.ShouldFollowLogConvention();
        records.Should().OnlyContain(r => !r.Message.Contains("broker.sentinel-host.internal", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Consume_Cancellation_IsRecordedAsCanceled_AndIsNotLoggedAsAnError()
    {
        using var spans = ActivityRecorder.StartRecording(MessagingTelemetry.SourceName);
        using var metrics = MetricRecorder.StartRecording(MessagingTelemetry.MeterName);
        var logger = new InMemoryLogger();
        var consumer = new ThrowingConsumer(logger, new OperationCanceledException());

        var act = async () => await consumer.Consume(ContextFor(new ObsConsumeBugMessage("cancel")));

        await act.Should().ThrowAsync<OperationCanceledException>();
        spans.RecordedActivities
            .Where(a => a.OperationName == "messaging consume" && Equals(a.GetTagItem(MessagingTagKeys.MessageType), nameof(ObsConsumeBugMessage)))
            .Should().Contain(a => a.Status == ActivityStatusCode.Unset && a.GetTagItem(WellKnownTagKeys.ErrorType) == null);
        metrics.Sum(Consume, Tag(nameof(ObsConsumeBugMessage)), Outcome(WellKnownOutcomes.Canceled)).Should().BeGreaterThanOrEqualTo(1);
        logger.Records.ShouldNotHaveLogged(new EventId(MessagingLoggingEventIds.ConsumeFailed));
    }

    [Fact]
    public async Task Consume_WithSentinelIdentityHeaders_NeverLeaksIntoSpansMetricsOrLogs()
    {
        string[] sentinels = ["tenant-sentinel-7f3a", "actor-sentinel-9c1e", "client-sentinel-44aa"];
        using var spans = ActivityRecorder.StartRecording(MessagingTelemetry.SourceName);
        using var metrics = MetricRecorder.StartRecording(MessagingTelemetry.MeterName);
        var logger = new InMemoryLogger();
        var consumer = new OkConsumer(logger);
        var headers = new MtHeaders();
        headers.Set(WellKnownHeaders.TenantId, sentinels[0]);
        headers.Set(WellKnownHeaders.ActorId, sentinels[1]);
        headers.Set(WellKnownHeaders.ClientId, sentinels[2]);

        await consumer.Consume(ContextFor(new ObsConsumeOkMessage("leak"), headers));

        SpanFor(spans, "messaging consume", nameof(ObsConsumeOkMessage)).ShouldNotLeak(sentinels);
        metrics.ShouldNotLeak(sentinels);
        logger.Records.ShouldNotLeak(sentinels);
    }

    // ---- BatchConsumerBase -------------------------------------------------------------------------------

    [Fact]
    public async Task ConsumeBatch_Success_RecordsABatchSpan_AndCountsTheBatchSize()
    {
        using var spans = ActivityRecorder.StartRecording(MessagingTelemetry.SourceName);
        using var metrics = MetricRecorder.StartRecording(MessagingTelemetry.MeterName);
        var logger = new InMemoryLogger();
        var consumer = new OkBatchConsumer(logger);

        await consumer.Consume(BatchContext<ObsBatchOkMessage>(3));

        SpanFor(spans, "messaging consume_batch", nameof(ObsBatchOkMessage))
            .ShouldHaveKind(ActivityKind.Consumer)
            .ShouldHaveTag(MessagingTagKeys.BatchMessageCount, 3)
            .ShouldBeRecordedSuccess()
            .ShouldFollowSpanConvention();
        metrics.Sum(Consume, Tag(nameof(ObsBatchOkMessage)), Outcome(WellKnownOutcomes.Success)).Should().Be(3);
        logger.Records.ShouldHaveLogged(new EventId(MessagingLoggingEventIds.BatchProcessing), LogLevel.Debug);
        logger.Records.ShouldHaveLoggedTemplate(
            new EventId(MessagingLoggingEventIds.BatchProcessing),
            "Processing a batch of {BatchSize} messages of type '{MessageType}'.");
        logger.Records.ShouldFollowLogConvention();
    }

    [Fact]
    public async Task ConsumeBatch_Exception_IsRecordedLoggedAndRethrown()
    {
        using var spans = ActivityRecorder.StartRecording(MessagingTelemetry.SourceName);
        using var metrics = MetricRecorder.StartRecording(MessagingTelemetry.MeterName);
        var logger = new InMemoryLogger();
        var consumer = new ThrowingBatchConsumer(logger);

        var act = async () => await consumer.Consume(BatchContext<ObsBatchBugMessage>(2));

        await act.Should().ThrowAsync<InvalidOperationException>();
        SpanFor(spans, "messaging consume_batch", nameof(ObsBatchBugMessage)).ShouldBeRecordedException(typeof(InvalidOperationException));
        metrics.Sum(Consume, Tag(nameof(ObsBatchBugMessage)), Outcome(WellKnownOutcomes.Exception)).Should().Be(2);
        logger.Records.ShouldHaveLogged(new EventId(MessagingLoggingEventIds.BatchFailed), LogLevel.Error);
        logger.Records.ShouldHaveLoggedWithException(new EventId(MessagingLoggingEventIds.BatchFailed), typeof(InvalidOperationException));
        logger.Records.ShouldHaveLoggedTemplate(
            new EventId(MessagingLoggingEventIds.BatchFailed),
            "Processing a batch of messages of type '{MessageType}' failed.");
        logger.Records.ShouldFollowLogConvention();
    }

    // ---- FaultConsumerAdapter ----------------------------------------------------------------------------

    [Fact]
    public async Task FaultAdapter_LogsTheObservationAsAWarning_AndAFailingFaultConsumerAsAnErrorWithTheException()
    {
        using var metrics = MetricRecorder.StartRecording(MessagingTelemetry.MeterName);
        var logger = new InMemoryLogger<FaultConsumerAdapter<ObsFaultedMessage, ThrowingFaultConsumer>>();
        var adapter = new FaultConsumerAdapter<ObsFaultedMessage, ThrowingFaultConsumer>(new ThrowingFaultConsumer(), logger);
        var fault = Substitute.For<Fault<ObsFaultedMessage>>();
        fault.FaultId.Returns(Guid.NewGuid());
        fault.Timestamp.Returns(DateTime.UtcNow);
        fault.Message.Returns(new ObsFaultedMessage("a"));
        var context = Substitute.For<ConsumeContext<Fault<ObsFaultedMessage>>>();
        context.Message.Returns(fault);
        context.CancellationToken.Returns(CancellationToken.None);

        var act = async () => await adapter.Consume(context);

        await act.Should().ThrowAsync<InvalidOperationException>();
        logger.Records.ShouldHaveLogged(new EventId(MessagingLoggingEventIds.FaultHandling), LogLevel.Warning);
        logger.Records.ShouldHaveLoggedWithoutException(new EventId(MessagingLoggingEventIds.FaultHandling));
        logger.Records.ShouldHaveLogged(new EventId(MessagingLoggingEventIds.FaultConsumerFailed), LogLevel.Error);
        logger.Records.ShouldHaveLoggedWithException(new EventId(MessagingLoggingEventIds.FaultConsumerFailed), typeof(InvalidOperationException));
        logger.Records.ShouldHaveLoggedTemplate(
            new EventId(MessagingLoggingEventIds.FaultConsumerFailed),
            "Fault consumer '{FaultConsumerType}' failed while handling fault '{FaultId}'.");
        logger.Records.ShouldFollowLogConvention();
        metrics.Sum("sharedkernel.messaging.fault.count", Tag(nameof(ObsFaultedMessage))).Should().Be(1);
    }

    // ---- Helpers ---------------------------------------------------------------------------------------------

    private static KeyValuePair<string, object?> Tag(string messageType) => new(MessagingTagKeys.MessageType, messageType);

    private static KeyValuePair<string, object?> Outcome(string value) => new(WellKnownTagKeys.Outcome, value);

    private static Activity SpanFor(ActivityRecorder recorder, string name, string messageType) =>
        recorder.RecordedActivities
            .Where(a => a.OperationName == name && Equals(a.GetTagItem(MessagingTagKeys.MessageType), messageType))
            .Should().ContainSingle().Subject;

    private static ConsumeContext<T> ContextFor<T>(T message, MtHeaders? headers = null)
        where T : class
    {
        var context = Substitute.For<ConsumeContext<T>>();
        context.Message.Returns(message);
        context.CancellationToken.Returns(CancellationToken.None);
        context.CorrelationId.Returns(Guid.NewGuid());
        context.DestinationAddress.Returns(new Uri("rabbitmq://localhost/obs-queue"));
        context.Headers.Returns(headers ?? new MtHeaders());
        return context;
    }

    private static ConsumeContext<Batch<T>> BatchContext<T>(int size)
        where T : class
    {
        var items = Enumerable.Range(0, size)
            .Select(i => ContextFor((T)Activator.CreateInstance(typeof(T), $"m{i}")!))
            .ToArray();
        var context = Substitute.For<ConsumeContext<Batch<T>>>();
        context.Message.Returns(new FakeBatch<T>(items));
        context.CancellationToken.Returns(CancellationToken.None);
        context.CorrelationId.Returns(Guid.NewGuid());
        context.DestinationAddress.Returns(new Uri("rabbitmq://localhost/obs-batch-queue"));
        return context;
    }

    private sealed class FakeBatch<T>(ConsumeContext<T>[] items) : Batch<T>
        where T : class
    {
        public BatchCompletionMode Mode => BatchCompletionMode.Size;

        public DateTime FirstMessageReceived => DateTime.UtcNow;

        public DateTime LastMessageReceived => DateTime.UtcNow;

        public int Length => items.Length;

        public ConsumeContext<T> this[int index] => items[index];

        public IEnumerator<ConsumeContext<T>> GetEnumerator() => ((IEnumerable<ConsumeContext<T>>)items).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class OkConsumer(ILogger logger) : ConsumerBase<ObsConsumeOkMessage>(logger)
    {
        protected override Task ConsumeAsync(ObsConsumeOkMessage message, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class SlowConsumer(ILogger logger) : ConsumerBase<ObsConsumeSlowMessage>(logger)
    {
        protected override Task ConsumeAsync(ObsConsumeSlowMessage message, CancellationToken ct) => Task.Delay(150, ct);
    }

    private sealed class ThrowingConsumer(ILogger logger, Exception exception) : ConsumerBase<ObsConsumeBugMessage>(logger)
    {
        protected override Task ConsumeAsync(ObsConsumeBugMessage message, CancellationToken ct) => throw exception;
    }

    private sealed class OkBatchConsumer(ILogger logger) : BatchConsumerBase<ObsBatchOkMessage>(logger)
    {
        protected override Task ConsumeAsync(IReadOnlyList<ObsBatchOkMessage> messages, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class ThrowingBatchConsumer(ILogger logger) : BatchConsumerBase<ObsBatchBugMessage>(logger)
    {
        protected override Task ConsumeAsync(IReadOnlyList<ObsBatchBugMessage> messages, CancellationToken ct) =>
            throw new InvalidOperationException("batch defect");
    }

    public sealed class ThrowingFaultConsumer : IFaultConsumer<ObsFaultedMessage>
    {
        public Task HandleAsync(
            Guid faultId,
            DateTimeOffset faultTimestamp,
            ObsFaultedMessage faultedMessage,
            IReadOnlyList<FaultExceptionInfo> exceptions,
            CancellationToken ct) => throw new InvalidOperationException("fault consumer defect");
    }
}

public sealed record ObsConsumeOkMessage(string Text);

public sealed record ObsConsumeSlowMessage(string Text);

public sealed record ObsConsumeBugMessage(string Text);

public sealed record ObsBatchOkMessage(string Text);

public sealed record ObsBatchBugMessage(string Text);

public sealed record ObsFaultedMessage(string Text);
