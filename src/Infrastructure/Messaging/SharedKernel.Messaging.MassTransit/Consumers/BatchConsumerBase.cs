using System.Diagnostics;
using MassTransit;
using Microsoft.Extensions.Logging;
using SharedKernel.Messaging.MassTransit.Diagnostics;
using SharedKernel.Messaging.MassTransit.Logging;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Primitives.Telemetry;

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
        ArgumentNullException.ThrowIfNull(logger);

        Logger = logger;
    }

    /// <summary>
    /// MassTransit entry point for batch delivery. Starts a <c>messaging consume_batch</c> span, counts the batch
    /// (its size, tagged with the outcome) and times it in seconds, forwards to <see cref="ConsumeAsync"/>, and
    /// rethrows any unhandled exception after structured logging.
    /// Do not override — override <see cref="ConsumeAsync"/> instead.
    /// </summary>
    /// <param name="context">The MassTransit batch consume context.</param>
    public async Task Consume(ConsumeContext<Batch<TMessage>> context)
    {
        var messages = context.Message.Select(c => c.Message).ToList().AsReadOnly();
        var messageTypeName = typeof(TMessage).Name;

        using var activity = MessagingDiagnostics.ActivitySource.StartActivity("messaging consume_batch", ActivityKind.Consumer);
        activity?.SetTag(MessagingTagKeys.MessageType, messageTypeName);
        activity?.SetTag(MessagingTagKeys.BatchMessageCount, messages.Count);

        var destination = context.DestinationAddress?.AbsolutePath;
        if (destination is not null)
            activity?.SetTag(MessagingTagKeys.Destination, destination);

        var scopeState = MessagingLogScope.Create(context.CorrelationId);
        scopeState["MessageType"] = messageTypeName;
        scopeState["BatchSize"] = messages.Count;

        using var scope = Logger.BeginScope(scopeState);

        MessagingLog.BatchProcessing(Logger, messages.Count, messageTypeName);

        // One outcome feeds the span's result and the metric tags. The counter adds the batch size, so it counts
        // messages like ConsumerBase does.
        var outcome = WellKnownOutcomes.Success;
        var started = Stopwatch.GetTimestamp();
        try
        {
            await ConsumeAsync(messages, context.CancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            outcome = activity.RecordException(ex);

            // Cancellation (a host shutting down) is not an error and is not logged as one.
            if (outcome != WellKnownOutcomes.Canceled)
                MessagingLog.BatchFailed(Logger, messageTypeName, ex);

            throw; // Never swallow: activates MassTransit retry/fault policies.
        }
        finally
        {
            var tags = MessagingDiagnostics.OutcomeTags(MessagingTagKeys.MessageType, messageTypeName, outcome);
            MessagingDiagnostics.ConsumeCounter.Add(messages.Count, tags);
            MessagingDiagnostics.ConsumeDurationHistogram.Record(Stopwatch.GetElapsedTime(started).TotalSeconds, tags);
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
