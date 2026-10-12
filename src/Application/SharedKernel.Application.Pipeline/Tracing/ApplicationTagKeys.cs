namespace SharedKernel.Application.Pipeline.Tracing;

/// <summary>
/// The tag keys this package puts on its spans and metric measurements, so no behavior writes a raw literal.
/// </summary>
/// <remarks>
/// <see cref="RequestType"/> and <see cref="RequestKind"/> keep their current values so the shared metric dimension
/// does not split between stream and unary records. The outcome, error code and error type tags come from
/// <c>WellKnownTagKeys</c> in <c>SharedKernel.Primitives</c>.
/// </remarks>
internal static class ApplicationTagKeys
{
    /// <summary>The full name of the request type.</summary>
    internal const string RequestType = "request.type";

    /// <summary>The request kind: <c>command</c>, <c>query</c>, <c>stream</c> or <c>request</c>.</summary>
    internal const string RequestKind = "request.kind";

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
}

/// <summary>The span names this package starts on <see cref="ApplicationDiagnostics.ActivitySource"/>.</summary>
internal static class ApplicationSpanNames
{
    /// <summary>One streaming query, from the first read to completion, fault or dispose.</summary>
    internal const string Stream = "application stream";

    /// <summary>One non-empty domain-event dispatch.</summary>
    internal const string DispatchEvents = "application dispatch_events";

    /// <summary>One domain event type that has at least one handler.</summary>
    internal const string HandleEvent = "application handle_event";

    /// <summary>The transaction around one command.</summary>
    internal const string Transaction = "application transaction";
}
