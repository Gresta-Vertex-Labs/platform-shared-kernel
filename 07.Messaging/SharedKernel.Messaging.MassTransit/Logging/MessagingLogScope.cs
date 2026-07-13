namespace SharedKernel.Messaging.MassTransit.Logging;

/// <summary>
/// Shared construction path for the base <see cref="Microsoft.Extensions.Logging.ILogger.BeginScope{TState}"/>
/// dictionary used by the consumer and routing-slip activity base types in this package.
/// </summary>
/// <remarks>
/// <para>
/// This is the ONLY approved construction path for the base entry of an
/// <c>ILogger.BeginScope(...)</c> dictionary anywhere in this package. Callers add their own
/// type-specific entries to the returned dictionary before passing it to <c>BeginScope</c>
/// (e.g. <c>MessageType</c>, <c>BatchSize</c>, <c>FaultId</c>, <c>routing_slip.tracking_number</c>).
/// </para>
/// <para>
/// Consumed by <see cref="Consumers.ConsumerBase{TMessage}"/>.Consume(),
/// <see cref="Consumers.BatchConsumerBase{TMessage}"/>.Consume(),
/// <c>FaultConsumerAdapter&lt;TMessage,TFaultConsumer&gt;.Consume()</c>, and
/// <see cref="RoutingSlips.RoutingSlipActivityBase{TArguments, TLog}"/>.Execute()/Compensate() —
/// the four consumer/activity base types in this package that build a structured log scope.
/// Guarantees an identical <c>"CorrelationId"</c> key name and identical null-handling across
/// all four, instead of four independently hand-rolled dictionary literals drifting out of sync
/// with each other (the exact defect P-254 fixed).
/// </para>
/// <para>
/// <c>VersionTranslatingConsumer</c> and <c>TranslatorRegistrationValidator</c> do not use
/// <c>BeginScope</c> and are out of scope for this helper.
/// </para>
/// </remarks>
internal static class MessagingLogScope
{
    /// <summary>
    /// Creates a new mutable log scope dictionary seeded with a single
    /// <c>"CorrelationId"</c> entry derived from <paramref name="correlationId"/>.
    /// </summary>
    /// <param name="correlationId">
    /// The correlation identifier for the current consume/execute/compensate operation, or
    /// <see langword="null"/> when unavailable.
    /// </param>
    /// <returns>
    /// A new <see cref="Dictionary{TKey, TValue}"/> containing
    /// <c>["CorrelationId"] = correlationId?.ToString("D") ?? string.Empty</c>. Callers must add
    /// their own additional entries before passing the dictionary to
    /// <see cref="Microsoft.Extensions.Logging.ILogger.BeginScope{TState}"/>.
    /// </returns>
    public static Dictionary<string, object?> Create(Guid? correlationId) =>
        new() { ["CorrelationId"] = correlationId?.ToString("D") ?? string.Empty };
}
