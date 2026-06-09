using MassTransit;
using Microsoft.Extensions.Logging;

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
public abstract class BatchConsumerBase<TMessage> : IConsumer<Batch<TMessage>>
    where TMessage : class
{
    private static readonly Action<ILogger, int, string, Exception?> LogBatchEntry =
        LoggerMessage.Define<int, string>(
            LogLevel.Information,
            new EventId(2, "BatchConsumeEntry"),
            "Processing batch of {BatchSize} messages for {MessageType}.");

    private static readonly Action<ILogger, string, Exception?> LogBatchError =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(3, "BatchConsumeError"),
            "Unhandled exception processing batch for {MessageType}. See CorrelationId in scope.");

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
        var correlationId = context.CorrelationId?.ToString("D") ?? string.Empty;

        using var scope = Logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = correlationId,
            ["MessageType"] = typeof(TMessage).Name,
            ["BatchSize"] = messages.Count,
        });

        LogBatchEntry(Logger, messages.Count, typeof(TMessage).Name, null);

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
}
