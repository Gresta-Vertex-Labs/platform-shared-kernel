namespace SharedKernel.Persistence.EfCore.Diagnostics;

/// <summary>
/// Domain-local <see cref="System.Diagnostics.Activity.SetTag(string, object?)"/> attribute-key
/// names for repository-operation tracing spans.
/// </summary>
/// <remarks>
/// Colocated per the platform's Magic String Convention (mirrors <c>SecurityClaimTypes</c>,
/// <c>WebhookSignatureHeaders</c>, <c>HubGroupNaming</c>). The exception-type tag on failure reuses
/// <c>01.Core</c>'s cross-domain <c>WellKnownTagKeys.ErrorType</c> ("error.type") rather than
/// duplicating a domain-local key — see
/// <see cref="SharedKernel.Primitives.Propagation.WellKnownTagKeys"/>. <see cref="DbCollectionName"/>/
/// <see cref="DbOperationName"/> mirror the OpenTelemetry semantic-conventions
/// <c>db.collection.name</c>/<c>db.operation.name</c> attribute names so a generic OTel-aware
/// backend can group/query these spans the same way it does any other database client span,
/// alongside — never instead of — the domain-local <see cref="AggregateType"/>/<see cref="Operation"/>
/// tags this package has always emitted.
/// </remarks>
internal static class PersistenceTagKeys
{
    /// <summary>The traced aggregate's CLR type name (e.g. "Order").</summary>
    public const string AggregateType = "persistence.aggregate_type";

    /// <summary>The repository operation name (e.g. "AddAsync", "ListAsync").</summary>
    public const string Operation = "persistence.operation";

    /// <summary>The operation outcome — <c>"success"</c> or <c>"failure"</c>.</summary>
    public const string Outcome = "persistence.outcome";

    /// <summary>
    /// OpenTelemetry semantic-conventions <c>db.collection.name</c> — set to the same value as
    /// <see cref="AggregateType"/>.
    /// </summary>
    public const string DbCollectionName = "db.collection.name";

    /// <summary>
    /// OpenTelemetry semantic-conventions <c>db.operation.name</c> — set to the same value as
    /// <see cref="Operation"/>.
    /// </summary>
    public const string DbOperationName = "db.operation.name";
}
