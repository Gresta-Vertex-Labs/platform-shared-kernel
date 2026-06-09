using System.Diagnostics;
using MassTransit;
using Microsoft.Extensions.Logging;

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
public abstract class ConsumerBase<TMessage> : IConsumer<TMessage>
    where TMessage : class
{
    private static readonly Action<ILogger, string, Exception?> LogConsumeError =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(1, "ConsumerError"),
            "Unhandled exception consuming message {MessageType}. See CorrelationId in scope.");

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
    /// MassTransit entry point. Propagates CorrelationId, forwards to <see cref="ConsumeAsync"/>,
    /// and rethrows any unhandled exception after structured logging.
    /// Do not override — override <see cref="ConsumeAsync"/> instead.
    /// </summary>
    /// <param name="context">The MassTransit consume context providing the message and metadata.</param>
    public async Task Consume(ConsumeContext<TMessage> context)
    {
        var correlationIdStr = context.CorrelationId?.ToString("D") ?? string.Empty;

        using var scope = Logger.BeginScope(new Dictionary<string, object?>
        {
            ["CorrelationId"] = correlationIdStr,
            ["MessageType"] = typeof(TMessage).Name,
        });

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
}
