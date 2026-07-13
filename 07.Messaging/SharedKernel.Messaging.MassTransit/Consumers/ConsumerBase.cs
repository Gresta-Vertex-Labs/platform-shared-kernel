using System.Diagnostics;
using MassTransit;
using Microsoft.Extensions.Logging;
using SharedKernel.Messaging.MassTransit.Diagnostics;
using SharedKernel.Messaging.MassTransit.Logging;

namespace SharedKernel.Messaging.MassTransit.Consumers;

/// <summary>
/// Base class for all MassTransit consumers in the platform.
/// Handles CorrelationId propagation, structured error logging, and exception rethrow semantics.
/// </summary>
/// <typeparam name="TMessage">The message type consumed by this consumer.</typeparam>
/// <remarks>
/// <para>
/// Override <see cref="ConsumeAsync"/> with business logic only — no MassTransit concerns.
/// Do not override or call <see cref="Consume"/> directly.
/// </para>
/// <para>
/// <strong>Exception policy:</strong> Unhandled exceptions from <see cref="ConsumeAsync"/> trigger
/// MassTransit retry and fault policies. Never swallow exceptions inside <see cref="ConsumeAsync"/>.
/// </para>
/// </remarks>
public abstract partial class ConsumerBase<TMessage> : IConsumer<TMessage>
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
        Logger = logger;
    }

    /// <summary>
    /// MassTransit entry point. Starts a consume <see cref="Activity"/>, propagates
    /// CorrelationId, extracts <c>x-sk-*</c> headers into the structured log scope,
    /// forwards to <see cref="ConsumeAsync"/>, and rethrows any unhandled exception
    /// after structured logging.
    /// Do not override — override <see cref="ConsumeAsync"/> instead.
    /// </summary>
    /// <param name="context">The MassTransit consume context providing the message and metadata.</param>
    /// <remarks>
    /// <para>
    /// A child <see cref="Activity"/> named <c>"Consumer.Consume"</c> is started via
    /// <see cref="MessagingDiagnostics.ActivitySource"/> for the duration of the consume
    /// operation, tagged with <c>messaging.message_type</c>. The activity is disposed
    /// after <see cref="ConsumeAsync"/> completes, whether it succeeds or throws.
    /// </para>
    /// <para>
    /// Any header whose key starts with <c>"x-sk-"</c> (case-insensitive) is added to the
    /// structured log scope automatically. This enriches downstream log entries with propagated
    /// cross-cutting headers such as <c>x-sk-tenant-id</c> without manual extraction in each consumer.
    /// The log scope is additionally enriched with <c>messaging.destination</c> (the consume
    /// endpoint's queue/topic path, omitted when unavailable) and <c>messaging.message_type</c>.
    /// </para>
    /// <para>
    /// Headers whose keys do <em>not</em> start with <c>"x-sk-"</c> are ignored and not added
    /// to the log scope to avoid leaking unrelated transport metadata.
    /// </para>
    /// </remarks>
    public async Task Consume(ConsumeContext<TMessage> context)
    {
        using var activity = MessagingDiagnostics.ActivitySource.StartActivity("Consumer.Consume");
        activity?.SetTag("messaging.message_type", typeof(TMessage).Name);

        // HP-05: Build log scope with CorrelationId, MessageType, and any x-sk-* headers.
        // OT-03: additive messaging.destination / messaging.message_type entries.
        var scopeState = MessagingLogScope.Create(context.CorrelationId);
        scopeState["MessageType"] = typeof(TMessage).Name;
        scopeState["messaging.message_type"] = typeof(TMessage).Name;

        var destination = context.DestinationAddress?.AbsolutePath;
        if (destination is not null)
            scopeState["messaging.destination"] = destination;

        // Extract headers whose key starts with "x-sk-" (case-insensitive) into the log scope.
        foreach (var header in context.Headers.GetAll())
        {
            if (header.Key.StartsWith("x-sk-", StringComparison.OrdinalIgnoreCase) &&
                header.Value is not null)
            {
                scopeState[header.Key] = header.Value.ToString();
            }
        }

        using var scope = Logger.BeginScope(scopeState);

        try
        {
            await ConsumeAsync(context.Message, context.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogConsumeError(Logger, typeof(TMessage).Name, ex);
            throw; // Never swallow — activates MassTransit retry/fault policies.
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

    /// <summary>
    /// Logs an unhandled exception raised from <see cref="ConsumeAsync"/>. See CorrelationId
    /// in the structured log scope established by <see cref="Consume"/>.
    /// </summary>
    [LoggerMessage(
        EventId = 7001,
        Level = LogLevel.Error,
        Message = "Unhandled exception consuming message {MessageType}. See CorrelationId in scope.")]
    private static partial void LogConsumeError(ILogger logger, string messageType, Exception exception);
}
