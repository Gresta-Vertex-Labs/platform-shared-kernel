namespace SharedKernel.Application.Pipeline.Tracing;

/// <summary>
/// The tag keys the Application packages put on their spans and metric measurements, so no behavior writes a raw
/// literal.
/// </summary>
/// <remarks>
/// <para>
/// Package keys are <c>application.{snake_name}</c>. The outcome, error code and error type tags come from
/// <c>WellKnownTagKeys</c> in <c>SharedKernel.Primitives</c>, and the <c>outcome</c> tag carries only
/// <c>WellKnownOutcomes</c>. The cache lookup result is deliberately a separate key (<see cref="CacheResult"/>), because
/// <c>outcome</c> is reserved for the four-value vocabulary.
/// </para>
/// <para>
/// Shared with <c>SharedKernel.Application.Pipeline.Caching</c> through <c>InternalsVisibleTo</c>, so the request span,
/// the duration histogram and the cache instruments agree on one spelling.
/// </para>
/// </remarks>
internal static class ApplicationTagKeys
{
    /// <summary>The full name of the request type.</summary>
    internal const string RequestType = "application.request_type";

    /// <summary>The request kind: <c>command</c>, <c>query</c>, <c>stream</c> or <c>request</c>.</summary>
    internal const string RequestKind = "application.request_kind";

    /// <summary>The full name of a domain event type.</summary>
    internal const string EventType = "application.event_type";

    /// <summary>The number of domain events in one dispatch.</summary>
    internal const string EventCount = "application.event_count";

    /// <summary>The full name of the domain-event handler that failed.</summary>
    internal const string HandlerType = "application.handler_type";

    /// <summary>Whether a command joined a transaction that was already active.</summary>
    internal const string TransactionJoined = "application.transaction_joined";

    /// <summary>How many times the unit of work ran the transaction delegate.</summary>
    internal const string Attempts = "application.attempts";

    /// <summary>The cache entity name of a cacheable query type.</summary>
    internal const string QueryType = "application.query_type";

    /// <summary>The cache entity name of an invalidating command type.</summary>
    internal const string CommandType = "application.command_type";

    /// <summary>The declared cache scope: <c>global</c>, <c>tenant</c> or <c>user</c>.</summary>
    internal const string CacheScope = "application.cache_scope";

    /// <summary>What the caching behavior did with one query: a lowercase snake name such as <c>hit</c> or <c>miss</c>.</summary>
    internal const string CacheResult = "application.cache_result";

    /// <summary>What one eviction addressed: <c>key</c> or <c>tag</c>.</summary>
    internal const string CacheTarget = "application.cache_target";

    /// <summary>Whether one eviction succeeded.</summary>
    internal const string CacheEvicted = "application.cache_evicted";
}

/// <summary>The span names the Application packages start on <see cref="ApplicationDiagnostics.ActivitySourceName"/>.</summary>
internal static class ApplicationSpanNames
{
    /// <summary>One unary request (a command or a query), from the first behavior to the last.</summary>
    internal const string Send = "application send";

    /// <summary>One streaming query, from the first read to completion, fault or dispose.</summary>
    internal const string Stream = "application stream";

    /// <summary>One non-empty domain-event dispatch.</summary>
    internal const string DispatchEvents = "application dispatch_events";

    /// <summary>One domain event type that has at least one handler.</summary>
    internal const string HandleEvent = "application handle_event";

    /// <summary>The transaction around one command.</summary>
    internal const string Transaction = "application transaction";
}
