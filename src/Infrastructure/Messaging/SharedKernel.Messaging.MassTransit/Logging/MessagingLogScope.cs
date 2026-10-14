using SharedKernel.Primitives.Propagation;

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
/// (e.g. <c>MessageType</c>, <c>BatchSize</c>, <c>FaultId</c>).
/// </para>
/// <para>
/// Consumed by <see cref="Consumers.ConsumerBase{TMessage}"/>.Consume(),
/// <see cref="Consumers.BatchConsumerBase{TMessage}"/>.Consume() and
/// <c>FaultConsumerAdapter&lt;TMessage,TFaultConsumer&gt;.Consume()</c> — the three consumer base
/// types in this package that build a structured log scope. Guarantees an identical
/// <see cref="CorrelationIdKey"/> key name and identical null-handling across all three, instead
/// of three independently hand-rolled dictionary literals drifting out of sync with each other
/// (the exact defect P-254 fixed). A fourth consumer of it, <c>RoutingSlipActivityBase</c>, went
/// with routing slips when P-560 removed them from this domain.
/// </para>
/// <para>
/// <c>VersionTranslatingConsumer</c> and <c>TranslatorRegistrationValidator</c> do not use
/// <c>BeginScope</c> and are out of scope for this helper.
/// </para>
/// </remarks>
internal static class MessagingLogScope
{
    /// <summary>
    /// The dictionary key under which the correlation identifier is recorded in the log scope
    /// returned by <see cref="Create"/>.
    /// </summary>
    /// <remarks>
    /// Package-local: this key names a structured-log-scope entry that is this domain's own
    /// logging contract, not a cross-service wire format — it is deliberately NOT promoted to
    /// <c>01.Core</c>'s <c>EventId</c>/logging registry (P-263).
    /// </remarks>
    internal const string CorrelationIdKey = "CorrelationId";

    /// <summary>
    /// Creates a new mutable log scope dictionary seeded with a single
    /// <see cref="CorrelationIdKey"/> entry derived from <paramref name="correlationId"/>.
    /// </summary>
    /// <param name="correlationId">
    /// The correlation identifier for the current consume/execute/compensate operation, or
    /// <see langword="null"/> when unavailable.
    /// </param>
    /// <returns>
    /// A new <see cref="Dictionary{TKey, TValue}"/> containing
    /// <c>[CorrelationIdKey] = correlationId?.ToString("D") ?? string.Empty</c>. Callers must add
    /// their own additional entries before passing the dictionary to
    /// <see cref="Microsoft.Extensions.Logging.ILogger.BeginScope{TState}"/>.
    /// </returns>
    public static Dictionary<string, object?> Create(Guid? correlationId) =>
        new() { [CorrelationIdKey] = correlationId?.ToString("D") ?? string.Empty };

    /// <summary>
    /// The scope key under which the caller kind (<see cref="WellKnownHeaders.ActorKind"/>) is recorded.
    /// </summary>
    internal const string ActorKindKey = "ActorKind";

    /// <summary>
    /// The closed set of message headers a consumer log scope may carry, mapped to the scope key each is
    /// recorded under (compared case-insensitively on the header name).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only <see cref="WellKnownHeaders.ActorKind"/> qualifies: a coarse enum, not an identifier. The rest of
    /// the well-known headers stay out on purpose (root rule L5): <see cref="WellKnownHeaders.ActorId"/> and
    /// <see cref="WellKnownHeaders.ClientId"/> identify a person or a client, <see cref="WellKnownHeaders.TenantId"/>
    /// and <see cref="WellKnownHeaders.IdempotencyKey"/> are on the never-log list, and
    /// <see cref="WellKnownHeaders.CorrelationId"/> is already the scope's own first entry.
    /// </para>
    /// <para>
    /// The set is closed: a header joins it only through a reviewed change, and a test pins its content.
    /// </para>
    /// </remarks>
    internal static readonly IReadOnlyDictionary<string, string> AllowedHeaders =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [WellKnownHeaders.ActorKind] = ActorKindKey,
        };

    /// <summary>
    /// Copies the allow-listed headers (<see cref="AllowedHeaders"/>) from <paramref name="headers"/> into
    /// <paramref name="scope"/>; every other header is ignored.
    /// </summary>
    /// <param name="scope">The scope dictionary built by <see cref="Create"/>.</param>
    /// <param name="headers">The headers of the message being consumed.</param>
    public static void AddAllowedHeaders(
        Dictionary<string, object?> scope,
        IEnumerable<KeyValuePair<string, object>> headers)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(headers);

        foreach (var header in headers)
        {
            if (header.Value is not null && AllowedHeaders.TryGetValue(header.Key, out var scopeKey))
                scope[scopeKey] = header.Value.ToString();
        }
    }
}
