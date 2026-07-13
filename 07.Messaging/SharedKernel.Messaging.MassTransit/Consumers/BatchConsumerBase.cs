using MassTransit;
using Microsoft.Extensions.Logging;
using SharedKernel.Messaging.MassTransit.Logging;

namespace SharedKernel.Messaging.MassTransit.Consumers;

/// <summary>
/// Base class for MassTransit batch consumers in the platform.
/// Handles structured batch-level logging and exception rethrow semantics.
/// </summary>
/// <typeparam name="TMessage">The message type processed in batches by this consumer.</typeparam>
/// <remarks>
/// <para>
/// Override <see cref="ConsumeAsync"/> with business logic only.
/// Do not override or call <see cref="Consume"/> directly.
/// </para>
/// <para>
/// <strong>Registration rule:</strong> Subclasses must be registered via
/// <c>MessagingBusBuilder.AddBatchConsumer&lt;TConsumer&gt;()</c>.
/// Do <strong>not</strong> use <c>AddConsumer&lt;T&gt;()</c> for batch consumers —
/// batch endpoint configuration (<c>MessageLimit</c>, <c>TimeLimit</c>, <c>ConcurrencyLimit</c>)
/// will not be applied if <c>AddConsumer</c> is used.
/// </para>
/// </remarks>
public abstract partial class BatchConsumerBase<TMessage> : IConsumer<Batch<TMessage>>
    where TMessage : class
{
    /// <summary>
    /// Gets the logger for this batch consumer.
    /// </summary>
    protected ILogger Logger { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="BatchConsumerBase{TMessage}"/> with the provided logger.
    /// </summary>
    /// <param name="logger">The logger for this batch consumer type.</param>
    protected BatchConsumerBase(ILogger logger)
    {
        Logger = logger;
    }

    /// <summary>
    /// MassTransit entry point for batch delivery. Logs batch entry, forwards to
    /// <see cref="ConsumeAsync"/>, and rethrows any unhandled exception after structured logging.
    /// Do not override — override <see cref="ConsumeAsync"/> instead.
    /// </summary>
    /// <param name="context">The MassTransit batch consume context.</param>
    public async Task Consume(ConsumeContext<Batch<TMessage>> context)
    {
        var messages = context.Message.Select(c => c.Message).ToList().AsReadOnly();

        var scopeState = MessagingLogScope.Create(context.CorrelationId);
        scopeState["MessageType"] = typeof(TMessage).Name;
        scopeState["BatchSize"] = messages.Count;

        using var scope = Logger.BeginScope(scopeState);

        LogBatchEntry(Logger, messages.Count, typeof(TMessage).Name);

        try
        {
            await ConsumeAsync(messages, context.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogBatchError(Logger, typeof(TMessage).Name, ex);
            throw; // Never swallow — activates MassTransit retry/fault policies.
        }
    }

    /// <summary>
    /// Override this method with the batch consumer's business logic.
    /// </summary>
    /// <param name="messages">
    /// The read-only list of deserialized message payloads in this batch.
    /// Batch size is bounded by <c>BatchOptions.MessageLimit</c>.
    /// Partial batches are delivered when <c>BatchOptions.TimeLimit</c> elapses.
    /// </param>
    /// <param name="ct">The cancellation token forwarded from the MassTransit <c>ConsumeContext.CancellationToken</c>.</param>
    /// <returns>A task representing the asynchronous batch processing operation.</returns>
    /// <remarks>
    /// <para>
    /// <strong>Exceptions are logged at Error level and rethrown.</strong>
    /// Never swallow exceptions — unhandled exceptions propagate to MassTransit and trigger
    /// the configured retry and fault policies, preventing silent batch loss.
    /// </para>
    /// </remarks>
    protected abstract Task ConsumeAsync(IReadOnlyList<TMessage> messages, CancellationToken ct);

    /// <summary>
    /// Logs structured entry into batch processing with the batch size and message type.
    /// </summary>
    [LoggerMessage(
        EventId = 7002,
        Level = LogLevel.Information,
        Message = "Processing batch of {BatchSize} messages for {MessageType}.")]
    private static partial void LogBatchEntry(ILogger logger, int batchSize, string messageType);

    /// <summary>
    /// Logs an unhandled exception raised from <see cref="ConsumeAsync"/>. See CorrelationId
    /// in the structured log scope established by <see cref="Consume"/>.
    /// </summary>
    [LoggerMessage(
        EventId = 7003,
        Level = LogLevel.Error,
        Message = "Unhandled exception processing batch for {MessageType}. See CorrelationId in scope.")]
    private static partial void LogBatchError(ILogger logger, string messageType, Exception exception);
}
