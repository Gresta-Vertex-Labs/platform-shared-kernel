using System.Diagnostics;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.Messaging.Abstractions.Faults;
using SharedKernel.Messaging.MassTransit.Diagnostics;
using SharedKernel.Messaging.MassTransit.Logging;

namespace SharedKernel.Messaging.MassTransit.Consumers;

/// <summary>
/// Internal MassTransit consumer adapter that bridges <c>Fault&lt;TMessage&gt;</c> messages
/// to <see cref="IFaultConsumer{TMessage}.HandleAsync"/>.
/// </summary>
/// <typeparam name="TMessage">The original message type that faulted.</typeparam>
/// <typeparam name="TFaultConsumer">
/// The fault consumer implementation type. Must implement <see cref="IFaultConsumer{TMessage}"/>.
/// </typeparam>
/// <remarks>
/// NOT a subclass of <see cref="ConsumerBase{TMessage}"/>. Implements <c>IConsumer&lt;Fault&lt;TMessage&gt;&gt;</c>
/// directly and applies equivalent log-then-rethrow semantics. Registered by
/// <c>MessagingBusBuilder.AddFaultConsumer&lt;TMessage, TFaultConsumer&gt;()</c> — never register directly.
/// Increments <see cref="MessagingDiagnostics.FaultCounter"/> unconditionally for every delivered
/// fault (P-348/WO-054).
/// </remarks>
internal sealed class FaultConsumerAdapter<TMessage, TFaultConsumer> : IConsumer<Fault<TMessage>>
    where TMessage : class
    where TFaultConsumer : class, IFaultConsumer<TMessage>
{
    private readonly TFaultConsumer _faultConsumer;
    private readonly ILogger<FaultConsumerAdapter<TMessage, TFaultConsumer>> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="FaultConsumerAdapter{TMessage, TFaultConsumer}"/>
    /// with the resolved fault consumer and logger.
    /// </summary>
    /// <param name="faultConsumer">The fault consumer resolved from DI.</param>
    /// <param name="logger">Logger for structured error logging.</param>
    public FaultConsumerAdapter(
        TFaultConsumer faultConsumer,
        ILogger<FaultConsumerAdapter<TMessage, TFaultConsumer>> logger)
    {
        _faultConsumer = faultConsumer;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task Consume(ConsumeContext<Fault<TMessage>> context)
    {
        var fault = context.Message;
        var faultId = fault.FaultId;
        var messageTypeName = typeof(TMessage).Name;

        // P-348/WO-054: incremented unconditionally for every delivered fault, regardless of
        // whether the registered IFaultConsumer<TMessage> below then succeeds or throws.
        MessagingDiagnostics.FaultCounter.Add(1, new KeyValuePair<string, object?>(MessagingTagKeys.MessageType, messageTypeName));

        // Propagate CorrelationId from headers to Activity when no active span.
        var correlationId = context.CorrelationId;
        if (correlationId.HasValue && Activity.Current is null)
        {
            // Attach correlation to structured logging scope for downstream context.
        }

        var scopeState = MessagingLogScope.Create(correlationId);
        scopeState["FaultId"] = faultId;
        scopeState["MessageType"] = messageTypeName;

        using var scope = _logger.BeginScope(scopeState);

        // Map MassTransit ExceptionInfo[] to the read-only FaultExceptionInfo list the contract takes.
        var exceptions = fault.Exceptions
            .Select(e => new FaultExceptionInfo(
                ExceptionType: e.ExceptionType ?? string.Empty,
                Message: e.Message ?? string.Empty))
            .ToArray();

        MessagingLog.FaultHandling(_logger, faultId, messageTypeName);

        try
        {
            await _faultConsumer.HandleAsync(
                faultId: faultId,
                faultTimestamp: fault.Timestamp,
                faultedMessage: fault.Message,
                exceptions: exceptions,
                ct: context.CancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            MessagingLog.FaultConsumerFailed(_logger, typeof(TFaultConsumer).Name, faultId, ex);
            throw; // Never swallow — activates MassTransit fault tracking.
        }
    }
}
