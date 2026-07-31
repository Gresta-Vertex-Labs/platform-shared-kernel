namespace SharedKernel.Persistence.EfCore.Diagnostics;

/// <summary>
/// Domain-local <see cref="System.Diagnostics.Activity.SetTag(string, object?)"/> attribute-key
/// names for repository-operation tracing spans (WO-051/P-319).
/// </summary>
/// <remarks>
/// Colocated per the platform's Magic String Convention (mirrors <c>SecurityClaimTypes</c>,
/// <c>WebhookSignatureHeaders</c>, <c>HubGroupNaming</c>). The exception-type tag on failure reuses
/// <c>01.Core</c>'s cross-domain <c>WellKnownTagKeys.ErrorType</c> ("error.type") rather than
/// duplicating a domain-local key — see
/// <see cref="SharedKernel.Primitives.Propagation.WellKnownTagKeys"/>.
/// </remarks>
internal static class PersistenceTagKeys
{
    /// <summary>The traced aggregate's CLR type name (e.g. "Order").</summary>
    public const string AggregateType = "persistence.aggregate_type";

    /// <summary>The repository operation name (e.g. "AddAsync", "ListAsync").</summary>
    public const string Operation = "persistence.operation";

    /// <summary>The operation outcome — <c>"success"</c> or <c>"failure"</c>.</summary>
    public const string Outcome = "persistence.outcome";
}
