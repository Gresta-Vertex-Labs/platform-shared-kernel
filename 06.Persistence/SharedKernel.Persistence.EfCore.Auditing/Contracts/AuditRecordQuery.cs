namespace SharedKernel.Persistence.EfCore.Auditing;

/// <summary>
/// A keyset-paginated ledger query, ordered by <see cref="AuditRecord.OccurredOn"/> then
/// <see cref="AuditRecord.Id"/>.
/// </summary>
/// <remarks>
/// <para>
/// Carries no tenant: <see cref="IAuditQueryService.QueryAsync"/> always applies the caller's own tenant,
/// and <see cref="IAuditQueryService.QueryAcrossTenantsAsync"/> is the separately named, scope-gated and
/// itself audited alternative.
/// </para>
/// <para>
/// Every accepted shape is served by an index (A17): <see cref="ResourceType"/> (optionally with
/// <see cref="ResourceId"/>) or <see cref="ActorId"/> is required, and a cross-tenant query requires
/// <see cref="ResourceType"/> and <see cref="ResourceId"/>.
/// </para>
/// </remarks>
public sealed record AuditRecordQuery
{
    /// <summary>Gets the resource type to filter by.</summary>
    public string? ResourceType { get; init; }

    /// <summary>Gets the resource instance to filter by; requires <see cref="ResourceType"/>.</summary>
    public string? ResourceId { get; init; }

    /// <summary>Gets the actor to filter by.</summary>
    public string? ActorId { get; init; }

    /// <summary>Gets the inclusive lower bound on <see cref="AuditRecord.OccurredOn"/>.</summary>
    public DateTimeOffset? From { get; init; }

    /// <summary>Gets the inclusive upper bound on <see cref="AuditRecord.OccurredOn"/>.</summary>
    public DateTimeOffset? To { get; init; }

    /// <summary>Gets a value indicating whether the newest records come first.</summary>
    public bool Descending { get; init; }

    /// <summary>Gets the page size, between 1 and <see cref="AuditQueryLimits.MaxPageSize"/>. Defaults to 100.</summary>
    public int Limit { get; init; } = 100;

    /// <summary>Gets the opaque cursor returned as <c>NextCursor</c> by the previous page, or <see langword="null"/> for the first page.</summary>
    public string? Cursor { get; init; }
}

/// <summary>Hard limits shared by every ledger query.</summary>
public static class AuditQueryLimits
{
    /// <summary>The largest page a query may request. A larger <see cref="AuditRecordQuery.Limit"/> is rejected, never clamped.</summary>
    public const int MaxPageSize = 1000;
}
