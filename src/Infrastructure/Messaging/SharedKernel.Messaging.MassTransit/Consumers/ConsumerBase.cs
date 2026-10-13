using System.Diagnostics;
using MassTransit;
using Microsoft.Extensions.Logging;
using SharedKernel.Messaging.MassTransit.Diagnostics;
using SharedKernel.Messaging.MassTransit.Logging;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Primitives.Telemetry;

namespace SharedKernel.Messaging.MassTransit.Consumers;

/// <summary>
/// The base class every consumer in the platform derives from: write
/// <see cref="ConsumeAsync"/> and get correlation, structured logging, tracing and metrics without
/// writing any of them.
/// </summary>
/// <typeparam name="TMessage">The message type consumed by this consumer.</typeparam>
/// <remarks>
/// <para>
/// <strong>Override <see cref="ConsumeAsync"/> and nothing else.</strong> It receives the
/// deserialized message and a cancellation token — no <c>ConsumeContext</c>, no MassTransit types.
/// That is deliberate: a consumer body that cannot reach the transport cannot accidentally depend
/// on it, and the same body is testable by calling it directly.
/// </para>
/// <para>
/// <strong>Never swallow an exception.</strong> Letting it propagate is how a consumer asks for a
/// retry. A caught-and-logged exception acknowledges the message, which means the work is lost and
/// the queue looks healthy — the failure mode this platform cares most about avoiding. If a failure
/// is genuinely not worth retrying, declare its type in
/// <c>ConsumerDefinitionBase.NonRetryableExceptions</c> so it goes straight to the error queue
/// rather than being hidden.
/// </para>
/// <para>
/// <strong>Assume at-least-once delivery.</strong> Every broker can deliver the same message twice;
/// a consumer whose work is not naturally idempotent needs
/// <c>MessagingBusBuilder.WithIdempotency()</c>.
/// </para>
/// <para>
/// With <c>WithInboundRequestContext()</c> enabled, injecting <c>IRequestContext</c> into a
/// consumer resolves to the caller that published the message, so tenant-scoped persistence works
/// from a consumer exactly as it does from an HTTP handler.
/// </para>
/// </remarks>
public abstract class ConsumerBase<TMessage> : IConsumer<TMessage>
    where TMessage : class
{
    /// <summary>
    /// Gets the logger for this consumer. Additional dependencies are constructor-injected by subclasses.
    /// </summary>
    protected ILogger Logger { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="ConsumerBase{TMessage}"/> with the provided logger.
    /// </summary>
    /// <param name="logger">The logger for this consumer type.</param>
    protected ConsumerBase(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        Logger = logger;
    }

    /// <summary>
    /// MassTransit entry point. Starts a consume <see cref="Activity"/>, propagates
    /// CorrelationId, opens a structured log scope (allow-listed headers only),
    /// forwards to <see cref="ConsumeAsync"/>, and rethrows any unhandled exception
    /// after structured logging.
    /// Do not override — override <see cref="ConsumeAsync"/> instead.
    /// </summary>
    /// <param name="context">The MassTransit consume context providing the message and metadata.</param>
    /// <remarks>
    /// <para>
    /// A <c>messaging consume</c> span (kind <c>Consumer</c>) is started for the duration of the consume
    /// operation, tagged with <c>messaging.message_type</c> and <c>messaging.destination.name</c> (when known). A
    /// throwing <see cref="ConsumeAsync"/> is recorded through the shared Primitives recorder; cancellation is not an
    /// error. The span is disposed after <see cref="ConsumeAsync"/> completes, whether it succeeds or throws.
    /// </para>
    /// <para>
    /// The structured log scope carries <c>CorrelationId</c>, <c>MessageType</c>,
    /// <c>messaging.message_type</c>, <c>messaging.destination.name</c> (the consume endpoint's
    /// queue/topic path, omitted when unavailable) and <c>ActorKind</c> (from the
    /// <c>x-sk-actor-kind</c> header, when present).
    /// </para>
    /// <para>
    /// Headers reach the scope only through a closed allow-list of values that are safe to log
    /// (today the actor kind alone). The actor id, the client id, the tenant and the idempotency key
    /// never enter a scope, and every other header is ignored, so identity does not land on every
    /// log line of a consume. Tenant and correlation already flow ambiently from the request context.
    /// </para>
    /// <para>
    /// Also increments the retry counter when <c>context.GetRetryAttempt() &gt; 0</c> (this invocation is a
    /// retry-filter re-delivery), and counts every invocation on the consume counter and records its duration in
    /// seconds, both tagged with <c>messaging.message_type</c> and <c>outcome</c>.
    /// </para>
    /// </remarks>
    public async Task Consume(ConsumeContext<TMessage> context)
    {
        using var activity = MessagingDiagnostics.ActivitySource.StartActivity("messaging consume", ActivityKind.Consumer);
        var messageTypeName = typeof(TMessage).Name;
        activity?.SetTag(MessagingTagKeys.MessageType, messageTypeName);

        var destination = context.DestinationAddress?.AbsolutePath;
        if (destination is not null)
            activity?.SetTag(MessagingTagKeys.Destination, destination);

        // GetRetryAttempt() > 0 identifies this invocation as a retry-filter re-delivery (0 on the original
        // delivery, and safely 0, never throwing, when no retry middleware is configured). See
        // MessagingDiagnostics.RetryCounter for why this is the observation point.
        if (context.GetRetryAttempt() > 0)
        {
            MessagingDiagnostics.RetryCounter.Add(
                1, new KeyValuePair<string, object?>(MessagingTagKeys.MessageType, messageTypeName));
        }

        // Build the log scope with the correlation id, the message type and the allow-listed headers.
        var scopeState = MessagingLogScope.Create(context.CorrelationId);
        scopeState["MessageType"] = messageTypeName;
        scopeState[MessagingTagKeys.MessageType] = messageTypeName;

        if (destination is not null)
            scopeState[MessagingTagKeys.Destination] = destination;

        // Only the closed allow-list of headers safe to log (MessagingLogScope.AllowedHeaders) enters the
        // scope; actor id, client id, tenant and idempotency key never do.
        MessagingLogScope.AddAllowedHeaders(scopeState, context.Headers.GetAll());

        using var scope = Logger.BeginScope(scopeState);

        // One outcome feeds the span's result and the metric tags.
        var outcome = WellKnownOutcomes.Success;
        var started = Stopwatch.GetTimestamp();
        try
        {
            await ConsumeAsync(context.Message, context.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            outcome = activity.RecordException(ex);

            // Cancellation (a host shutting down) is not an error and is not logged as one.
            if (outcome != WellKnownOutcomes.Canceled)
                MessagingLog.ConsumeFailed(Logger, messageTypeName, ex);

            throw; // Never swallow: activates MassTransit retry/fault policies.
        }
        finally
        {
            // Recorded on success and failure alike, so the histogram reflects true end-to-end consume latency.
            var tags = MessagingDiagnostics.OutcomeTags(MessagingTagKeys.MessageType, messageTypeName, outcome);
            MessagingDiagnostics.ConsumeCounter.Add(1, tags);
            MessagingDiagnostics.ConsumeDurationHistogram.Record(Stopwatch.GetElapsedTime(started).TotalSeconds, tags);
        }
    }

    /// <summary>
    /// Override this method with the consumer's business logic.
    /// </summary>
    /// <param name="message">The deserialized message payload.</param>
    /// <param name="ct">The cancellation token forwarded from the MassTransit <c>ConsumeContext.CancellationToken</c>.</param>
    /// <returns>A task representing the asynchronous consumer operation.</returns>
    /// <remarks>
    /// <strong>Do not swallow exceptions.</strong> Unhandled exceptions propagate to MassTransit
    /// and trigger the configured retry and fault policies.
    /// </remarks>
    protected abstract Task ConsumeAsync(TMessage message, CancellationToken ct);
}
