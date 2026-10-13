using System.Diagnostics;
using FluentAssertions;
using MassTransit;
using Microsoft.Extensions.Logging;
using NSubstitute;
using SharedKernel.Contracts.Events;
using SharedKernel.Messaging.Abstractions.Errors;
using SharedKernel.Messaging.Abstractions.Options;
using SharedKernel.Messaging.MassTransit.Diagnostics;
using SharedKernel.Messaging.MassTransit.EventPublisher;
using SharedKernel.Messaging.MassTransit.MessageBus;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Testing.Logging;
using SharedKernel.Testing.Telemetry;
using MsOptions = Microsoft.Extensions.Options.Options;

namespace SharedKernel.Messaging.MassTransit.Tests.ObservabilityTests;

/// <summary>
/// The telemetry and logs of the dispatch verbs: span name, kind and tags, the failure shapes recorded through the shared
/// helpers, the <c>outcome</c> vocabulary on the counters, and the dispatch-failure log. Every test uses a message type
/// of its own and filters by that type, because the source and the meter are process-wide.
/// </summary>
public sealed class DispatchTelemetryTests
{
    private static readonly string[] SentinelValues = ["tenant-sentinel-7f3a", "actor-sentinel-9c1e", "broker.sentinel-host.internal"];

    // ---- IMessageBus.PublishAsync ------------------------------------------------------------------------

    [Fact]
    public async Task Publish_Success_RecordsAProducerSpanAndASuccessCount()
    {
        using var spans = ActivityRecorder.StartRecording(MessagingTelemetry.SourceName);
        using var metrics = MetricRecorder.StartRecording(MessagingTelemetry.MeterName);
        var bus = CreateBus(Substitute.For<IPublishEndpoint>());

        var result = await bus.PublishAsync(new ObsPublishOkMessage("a"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        var span = SpanFor(spans, "messaging publish", nameof(ObsPublishOkMessage));
        span.ShouldHaveKind(ActivityKind.Producer)
            .ShouldHaveSource(MessagingTelemetry.SourceName)
            .ShouldHaveTagKeys(MessagingTagKeys.MessageType)
            .ShouldBeRecordedSuccess()
            .ShouldFollowSpanConvention();
        metrics.Sum(Publish, Tag(MessagingTagKeys.MessageType, nameof(ObsPublishOkMessage)), Outcome(WellKnownOutcomes.Success))
            .Should().Be(1);
        metrics.ShouldHaveInstrument(Publish, "{message}", InstrumentKind.Counter);
    }

    [Fact]
    public async Task Publish_RecognisedFault_RecordsAFailureWithTheExceptionEvent_CountsFailure_AndLogsWithTheException()
    {
        using var spans = ActivityRecorder.StartRecording(MessagingTelemetry.SourceName);
        using var metrics = MetricRecorder.StartRecording(MessagingTelemetry.MeterName);
        var fault = new ConnectionException("connection to broker.sentinel-host.internal refused");
        var logger = new InMemoryLogger<MassTransitMessageBus>();
        var bus = CreateBus(Throwing(fault), logger);

        var result = await bus.PublishAsync(new ObsPublishFaultMessage("a"), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(MessagingErrorCodes.Unavailable);
        var span = SpanFor(spans, "messaging publish", nameof(ObsPublishFaultMessage));
        span.ShouldHaveStatus(ActivityStatusCode.Error, MessagingErrorCodes.Unavailable)
            .ShouldHaveTag(WellKnownTagKeys.ErrorCode, MessagingErrorCodes.Unavailable)
            .ShouldHaveTag(WellKnownTagKeys.ErrorType, "Unavailable")
            .ShouldHaveEvent("exception");
        metrics.Sum(Publish, Tag(MessagingTagKeys.MessageType, nameof(ObsPublishFaultMessage)), Outcome(WellKnownOutcomes.Failure))
            .Should().Be(1);
        metrics.Sum(Publish, Tag(MessagingTagKeys.MessageType, nameof(ObsPublishFaultMessage)), Outcome(WellKnownOutcomes.Success))
            .Should().Be(0, "a failed publish is never counted as published");

        var record = logger.Records.ShouldHaveLogged(new EventId(MessagingLoggingEventIds.DispatchFailed), LogLevel.Warning);
        record.Exception.Should().BeSameAs(fault);
        logger.Records.ShouldHaveLoggedTemplate(
            new EventId(MessagingLoggingEventIds.DispatchFailed),
            "Dispatching a message of type '{MessageType}' failed during '{Operation}' with error '{ErrorCode}'.");
        logger.Records.ShouldFollowLogConvention();
        logger.Records.Should().OnlyContain(r => !r.Message.Contains("broker.sentinel-host.internal", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Publish_UnrecognisedException_IsRecordedAsAnExceptionAndRethrown()
    {
        using var spans = ActivityRecorder.StartRecording(MessagingTelemetry.SourceName);
        using var metrics = MetricRecorder.StartRecording(MessagingTelemetry.MeterName);
        var bus = CreateBus(Throwing(new InvalidOperationException("defect")));

        var act = async () => await bus.PublishAsync(new ObsPublishBugMessage("a"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        SpanFor(spans, "messaging publish", nameof(ObsPublishBugMessage))
            .ShouldBeRecordedException(typeof(InvalidOperationException));
        metrics.Sum(Publish, Tag(MessagingTagKeys.MessageType, nameof(ObsPublishBugMessage)), Outcome(WellKnownOutcomes.Exception))
            .Should().Be(1);
    }

    [Fact]
    public async Task Publish_Cancellation_IsRecordedAsCanceledNotAnError()
    {
        using var spans = ActivityRecorder.StartRecording(MessagingTelemetry.SourceName);
        using var metrics = MetricRecorder.StartRecording(MessagingTelemetry.MeterName);
        var bus = CreateBus(Throwing(new OperationCanceledException()));

        var act = async () => await bus.PublishAsync(new ObsPublishCancelMessage("a"), CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
        SpanFor(spans, "messaging publish", nameof(ObsPublishCancelMessage)).ShouldBeRecordedCancellation();
        metrics.Sum(Publish, Tag(MessagingTagKeys.MessageType, nameof(ObsPublishCancelMessage)), Outcome(WellKnownOutcomes.Canceled))
            .Should().Be(1);
    }

    // ---- IMessageBus.SendAsync ---------------------------------------------------------------------------

    [Fact]
    public async Task Send_Success_RecordsAProducerSpanAndASuccessCount()
    {
        using var spans = ActivityRecorder.StartRecording(MessagingTelemetry.SourceName);
        using var metrics = MetricRecorder.StartRecording(MessagingTelemetry.MeterName);
        var bus = CreateBus(Substitute.For<IPublishEndpoint>(), sendEndpoint: Substitute.For<ISendEndpoint>());

        var result = await bus.SendAsync(new ObsSendOkMessage("a"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        SpanFor(spans, "messaging send", nameof(ObsSendOkMessage))
            .ShouldHaveKind(ActivityKind.Producer)
            .ShouldBeRecordedSuccess()
            .ShouldFollowSpanConvention();
        metrics.Sum(Send, Tag(MessagingTagKeys.MessageType, nameof(ObsSendOkMessage)), Outcome(WellKnownOutcomes.Success))
            .Should().Be(1);
        metrics.ShouldHaveInstrument(Send, "{message}", InstrumentKind.Counter);
    }

    [Fact]
    public async Task Send_EndpointNotFound_RecordsAFailureResult_AndLogsWithTheException()
    {
        using var spans = ActivityRecorder.StartRecording(MessagingTelemetry.SourceName);
        using var metrics = MetricRecorder.StartRecording(MessagingTelemetry.MeterName);
        var logger = new InMemoryLogger<MassTransitMessageBus>();
        var endpoint = Substitute.For<ISendEndpoint>();
        var missing = new EndpointNotFoundException("no such queue");
        endpoint.Send(Arg.Any<ObsSendMissingMessage>(), Arg.Any<IPipe<SendContext<ObsSendMissingMessage>>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(missing));
        var bus = CreateBus(Substitute.For<IPublishEndpoint>(), logger, endpoint);

        var result = await bus.SendAsync(new ObsSendMissingMessage("a"), CancellationToken.None);

        result.Error.Code.Should().Be(MessagingErrorCodes.EndpointNotFound);
        SpanFor(spans, "messaging send", nameof(ObsSendMissingMessage))
            .ShouldHaveStatus(ActivityStatusCode.Error, MessagingErrorCodes.EndpointNotFound)
            .ShouldHaveTag(WellKnownTagKeys.ErrorType, "NotFound");
        metrics.Sum(Send, Tag(MessagingTagKeys.MessageType, nameof(ObsSendMissingMessage)), Outcome(WellKnownOutcomes.Failure))
            .Should().Be(1);
        logger.Records.ShouldHaveLoggedWithException(new EventId(MessagingLoggingEventIds.DispatchFailed), typeof(EndpointNotFoundException));
        logger.Records.ShouldFollowLogConvention();
    }

    [Fact]
    public async Task Send_RecognisedFault_RecordsAFailureWithTheExceptionEvent()
    {
        using var spans = ActivityRecorder.StartRecording(MessagingTelemetry.SourceName);
        using var metrics = MetricRecorder.StartRecording(MessagingTelemetry.MeterName);
        var endpoint = Substitute.For<ISendEndpoint>();
        endpoint.Send(Arg.Any<ObsSendFaultMessage>(), Arg.Any<IPipe<SendContext<ObsSendFaultMessage>>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new ConnectionException("down")));
        var bus = CreateBus(Substitute.For<IPublishEndpoint>(), sendEndpoint: endpoint);

        var result = await bus.SendAsync(new ObsSendFaultMessage("a"), CancellationToken.None);

        result.Error.Code.Should().Be(MessagingErrorCodes.Unavailable);
        SpanFor(spans, "messaging send", nameof(ObsSendFaultMessage))
            .ShouldHaveStatus(ActivityStatusCode.Error, MessagingErrorCodes.Unavailable)
            .ShouldHaveEvent("exception");
        metrics.Sum(Send, Tag(MessagingTagKeys.MessageType, nameof(ObsSendFaultMessage)), Outcome(WellKnownOutcomes.Failure))
            .Should().Be(1);
    }

    [Fact]
    public async Task Send_UnrecognisedException_IsRecordedAsAnExceptionAndRethrown()
    {
        using var spans = ActivityRecorder.StartRecording(MessagingTelemetry.SourceName);
        using var metrics = MetricRecorder.StartRecording(MessagingTelemetry.MeterName);
        var endpoint = Substitute.For<ISendEndpoint>();
        endpoint.Send(Arg.Any<ObsSendBugMessage>(), Arg.Any<IPipe<SendContext<ObsSendBugMessage>>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("defect")));
        var bus = CreateBus(Substitute.For<IPublishEndpoint>(), sendEndpoint: endpoint);

        var act = async () => await bus.SendAsync(new ObsSendBugMessage("a"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        SpanFor(spans, "messaging send", nameof(ObsSendBugMessage)).ShouldBeRecordedException(typeof(InvalidOperationException));
        metrics.Sum(Send, Tag(MessagingTagKeys.MessageType, nameof(ObsSendBugMessage)), Outcome(WellKnownOutcomes.Exception))
            .Should().Be(1);
    }

    // ---- IEventPublisher.PublishAsync ---------------------------------------------------------------------

    [Fact]
    public async Task PublishEvent_Success_RecordsAProducerSpanTaggedWithTheWireName()
    {
        using var spans = ActivityRecorder.StartRecording(MessagingTelemetry.SourceName);
        using var metrics = MetricRecorder.StartRecording(MessagingTelemetry.MeterName);
        var publisher = CreatePublisher(Substitute.For<IPublishEndpoint>());

        var result = await publisher.PublishAsync(NewEvent<ObsOkEvent>(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        SpanFor(spans, "messaging publish_event", ObsOkEvent.WireName, MessagingTagKeys.EventType)
            .ShouldHaveKind(ActivityKind.Producer)
            .ShouldHaveTagKeys(MessagingTagKeys.EventType)
            .ShouldBeRecordedSuccess()
            .ShouldFollowSpanConvention();
        metrics.Sum(Publish, Tag(MessagingTagKeys.EventType, ObsOkEvent.WireName), Outcome(WellKnownOutcomes.Success))
            .Should().Be(1);
    }

    [Fact]
    public async Task PublishEvent_RecognisedFault_RecordsAFailureWithTheExceptionEvent()
    {
        using var spans = ActivityRecorder.StartRecording(MessagingTelemetry.SourceName);
        using var metrics = MetricRecorder.StartRecording(MessagingTelemetry.MeterName);
        var logger = new InMemoryLogger<MassTransitEventPublisher>();
        var publisher = CreatePublisher(ThrowingEnvelope<ObsFaultEvent>(new ConnectionException("down")), logger);

        var result = await publisher.PublishAsync(NewEvent<ObsFaultEvent>(), CancellationToken.None);

        result.Error.Code.Should().Be(MessagingErrorCodes.Unavailable);
        SpanFor(spans, "messaging publish_event", ObsFaultEvent.WireName, MessagingTagKeys.EventType)
            .ShouldHaveStatus(ActivityStatusCode.Error, MessagingErrorCodes.Unavailable)
            .ShouldHaveEvent("exception");
        metrics.Sum(Publish, Tag(MessagingTagKeys.EventType, ObsFaultEvent.WireName), Outcome(WellKnownOutcomes.Failure))
            .Should().Be(1);
        logger.Records.ShouldHaveLoggedWithException(new EventId(MessagingLoggingEventIds.DispatchFailed), typeof(ConnectionException));
    }

    [Fact]
    public async Task PublishEvent_UnrecognisedException_IsRecordedAsAnExceptionAndRethrown()
    {
        using var spans = ActivityRecorder.StartRecording(MessagingTelemetry.SourceName);
        using var metrics = MetricRecorder.StartRecording(MessagingTelemetry.MeterName);
        var publisher = CreatePublisher(ThrowingEnvelope<ObsBugEvent>(new InvalidOperationException("defect")));

        var act = async () => await publisher.PublishAsync(NewEvent<ObsBugEvent>(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        SpanFor(spans, "messaging publish_event", ObsBugEvent.WireName, MessagingTagKeys.EventType)
            .ShouldBeRecordedException(typeof(InvalidOperationException));
        metrics.Sum(Publish, Tag(MessagingTagKeys.EventType, ObsBugEvent.WireName), Outcome(WellKnownOutcomes.Exception))
            .Should().Be(1);
    }

    [Fact]
    public async Task PublishEvent_Cancellation_IsRecordedAsCanceled()
    {
        using var spans = ActivityRecorder.StartRecording(MessagingTelemetry.SourceName);
        var publisher = CreatePublisher(ThrowingEnvelope<ObsCancelEvent>(new OperationCanceledException()));

        var act = async () => await publisher.PublishAsync(NewEvent<ObsCancelEvent>(), CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
        SpanFor(spans, "messaging publish_event", ObsCancelEvent.WireName, MessagingTagKeys.EventType).ShouldBeRecordedCancellation();
    }

    [Fact]
    public async Task PublishEvent_InvalidEvent_IsAValidationFailureWithAClosedReason_AndNoExceptionEvent()
    {

        using var spans = ActivityRecorder.StartRecording(MessagingTelemetry.SourceName);
        using var metrics = MetricRecorder.StartRecording(MessagingTelemetry.MeterName);
        var logger = new InMemoryLogger<MassTransitEventPublisher>();
        var publisher = CreatePublisher(Substitute.For<IPublishEndpoint>(), logger);
        var invalid = new ObsInvalidEvent(Guid.Empty, DateTimeOffset.UtcNow);

        var result = await publisher.PublishAsync(invalid, CancellationToken.None);

        result.Error.Code.Should().Be(MessagingErrorCodes.InvalidMessage);
        result.Error.Message.Should().NotContain("Guid.Empty", "the inner ArgumentException text never reaches the error");
        SpanFor(spans, "messaging publish_event", ObsInvalidEvent.WireName, MessagingTagKeys.EventType)
            .ShouldBeRecordedFailure(result.Error);
        metrics.Sum(Publish, Tag(MessagingTagKeys.EventType, ObsInvalidEvent.WireName), Outcome(WellKnownOutcomes.Failure))
            .Should().Be(1);
        logger.Records.ShouldHaveLoggedWithException(new EventId(MessagingLoggingEventIds.DispatchFailed), typeof(ArgumentException));
    }

    [Fact]
    public async Task PublishEvent_NoIntegrationEventAttribute_IsAContractViolationRecordedAsAFailure()
    {
        using var spans = ActivityRecorder.StartRecording(MessagingTelemetry.SourceName);
        using var metrics = MetricRecorder.StartRecording(MessagingTelemetry.MeterName);
        var publisher = CreatePublisher(Substitute.For<IPublishEndpoint>());

        var result = await publisher.PublishAsync(
            new ObsUnattributedEvent(Guid.NewGuid(), DateTimeOffset.UtcNow), CancellationToken.None);

        result.Error.Code.Should().Be(MessagingErrorCodes.ContractViolation);
        SpanFor(spans, "messaging publish_event", nameof(ObsUnattributedEvent), MessagingTagKeys.MessageType)
            .ShouldBeRecordedFailure(result.Error);
        metrics.Sum(Publish, Tag(MessagingTagKeys.MessageType, nameof(ObsUnattributedEvent)), Outcome(WellKnownOutcomes.Failure))
            .Should().Be(1);
    }

    // ---- No identity leaves the process through telemetry ----------------------------------------------------

    [Fact]
    public async Task Publish_WithSentinelIdentity_NeverLeaksIntoSpansOrMetricTags()
    {
        using var spans = ActivityRecorder.StartRecording(MessagingTelemetry.SourceName);
        using var metrics = MetricRecorder.StartRecording(MessagingTelemetry.MeterName);
        var bus = CreateBus(Substitute.For<IPublishEndpoint>());

        await bus.PublishAsync(
            new ObsPublishLeakMessage("a"),
            ctx =>
            {
                ctx.WithHeader("x-sk-actor-id", SentinelValues[1]);
                ctx.WithHeader("x-sk-tenant-id", SentinelValues[0]);
            },
            CancellationToken.None);

        SpanFor(spans, "messaging publish", nameof(ObsPublishLeakMessage)).ShouldNotLeak(SentinelValues);
        metrics.ShouldNotLeak(SentinelValues);
    }

    // ---- Helpers ---------------------------------------------------------------------------------------------

    private const string Publish = "sharedkernel.messaging.publish.count";
    private const string Send = "sharedkernel.messaging.send.count";

    private static KeyValuePair<string, object?> Tag(string key, string value) => new(key, value);

    private static KeyValuePair<string, object?> Outcome(string value) => new(WellKnownTagKeys.Outcome, value);

    private static Activity SpanFor(ActivityRecorder recorder, string name, string typeTag, string tagKey = MessagingTagKeys.MessageType) =>
        recorder.RecordedActivities
            .Where(a => a.OperationName == name && Equals(a.GetTagItem(tagKey), typeTag))
            .Should().ContainSingle().Subject;

    private static TEvent NewEvent<TEvent>() where TEvent : class, IIntegrationEvent =>
        (TEvent)Activator.CreateInstance(typeof(TEvent), Guid.NewGuid(), DateTimeOffset.UtcNow)!;

    private static MassTransitMessageBus CreateBus(
        IPublishEndpoint publishEndpoint,
        ILogger<MassTransitMessageBus>? logger = null,
        ISendEndpoint? sendEndpoint = null)
    {
        var provider = Substitute.For<ISendEndpointProvider>();
        provider.GetSendEndpoint(Arg.Any<Uri>()).Returns(Task.FromResult(sendEndpoint ?? Substitute.For<ISendEndpoint>()));
        return new MassTransitMessageBus(
            publishEndpoint,
            provider,
            Substitute.For<IServiceProvider>(),
            new Dictionary<Type, string>(),
            new ConventionSendEndpointResolver(MsOptions.Create(new MessagingOptions { ServiceName = "obs-service" })),
            logger ?? new InMemoryLogger<MassTransitMessageBus>());
    }

    private static MassTransitEventPublisher CreatePublisher(
        IPublishEndpoint publishEndpoint,
        ILogger<MassTransitEventPublisher>? logger = null) =>
        new(
            publishEndpoint,
            MsOptions.Create(new MessagingOptions { ServiceName = "obs-service" }),
            [],
            logger ?? new InMemoryLogger<MassTransitEventPublisher>());

    private static IPublishEndpoint Throwing(Exception exception)
    {
        var endpoint = Substitute.For<IPublishEndpoint>();
        endpoint
            .Publish(Arg.Any<ObsPublishFaultMessage>(), Arg.Any<IPipe<PublishContext<ObsPublishFaultMessage>>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(exception));
        endpoint
            .Publish(Arg.Any<ObsPublishBugMessage>(), Arg.Any<IPipe<PublishContext<ObsPublishBugMessage>>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(exception));
        endpoint
            .Publish(Arg.Any<ObsPublishCancelMessage>(), Arg.Any<IPipe<PublishContext<ObsPublishCancelMessage>>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(exception));
        return endpoint;
    }

    private static IPublishEndpoint ThrowingEnvelope<TEvent>(Exception exception) where TEvent : class, IIntegrationEvent
    {
        var endpoint = Substitute.For<IPublishEndpoint>();
        endpoint
            .Publish(Arg.Any<EventEnvelope<TEvent>>(), Arg.Any<IPipe<PublishContext<EventEnvelope<TEvent>>>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(exception));
        return endpoint;
    }
}

public sealed record ObsPublishOkMessage(string Text);

public sealed record ObsPublishFaultMessage(string Text);

public sealed record ObsPublishBugMessage(string Text);

public sealed record ObsPublishCancelMessage(string Text);

public sealed record ObsPublishLeakMessage(string Text);

public sealed record ObsSendOkMessage(string Text);

public sealed record ObsSendMissingMessage(string Text);

public sealed record ObsSendFaultMessage(string Text);

public sealed record ObsSendBugMessage(string Text);

[IntegrationEvent(WireName)]
public sealed record ObsOkEvent(Guid EventId, DateTimeOffset OccurredOn) : IIntegrationEvent
{
    public const string WireName = "tests.messaging.obs.ok";
}

[IntegrationEvent(WireName)]
public sealed record ObsFaultEvent(Guid EventId, DateTimeOffset OccurredOn) : IIntegrationEvent
{
    public const string WireName = "tests.messaging.obs.fault";
}

[IntegrationEvent(WireName)]
public sealed record ObsBugEvent(Guid EventId, DateTimeOffset OccurredOn) : IIntegrationEvent
{
    public const string WireName = "tests.messaging.obs.bug";
}

[IntegrationEvent(WireName)]
public sealed record ObsCancelEvent(Guid EventId, DateTimeOffset OccurredOn) : IIntegrationEvent
{
    public const string WireName = "tests.messaging.obs.cancel";
}

[IntegrationEvent(WireName)]
public sealed record ObsInvalidEvent(Guid EventId, DateTimeOffset OccurredOn) : IIntegrationEvent
{
    public const string WireName = "tests.messaging.obs.invalid";
}

public sealed record ObsUnattributedEvent(Guid EventId, DateTimeOffset OccurredOn) : IIntegrationEvent;
