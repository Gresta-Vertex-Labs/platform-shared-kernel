namespace SharedKernel.Persistence.Abstractions.Auditing;

/// <summary>
/// Hard limits shared by every audit-trail keyset query.
/// </summary>
public static class AuditQueryLimits
{
    /// <summary>
    /// The maximum number of records a single <see cref="AuditResourceHistorySpecification"/>/
    /// <see cref="AuditActorActionsSpecification"/> page may request. A caller-supplied <c>take</c>
    /// above this value is rejected, never silently clamped — a silent clamp would make a caller's
    /// own pagination-loop math (page count, "did I get everything") quietly wrong.
    /// </summary>
    public const int MaxPageSize = 1000;
}
