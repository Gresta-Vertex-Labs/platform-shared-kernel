namespace SharedKernel.Persistence.EfCore.Auditing;

/// <summary>
/// Maximum lengths, in characters, the writer accepts for each audit field.
/// </summary>
/// <remarks>
/// Checked before any SQL is sent, so an oversized value is reported as an
/// <see cref="ArgumentException"/> naming the field and never as a database error that would abort the
/// caller's business transaction (A19). The columns themselves are unbounded <c>text</c>. The snapshots
/// are not limited.
/// </remarks>
public static class AuditFieldLimits
{
    /// <summary>Limit for <c>Action</c>.</summary>
    public const int Action = 200;

    /// <summary>Limit for <c>ResourceType</c>.</summary>
    public const int ResourceType = 200;

    /// <summary>Limit for <c>ResourceId</c>.</summary>
    public const int ResourceId = 200;

    /// <summary>Limit for <c>ErrorCode</c>.</summary>
    public const int ErrorCode = 500;

    /// <summary>Limit for identity fields: actor, client, session, impersonator, source service, correlation, approval and idempotency key.</summary>
    public const int Identifier = 256;
}

/// <summary>
/// The action codes and resource type the ledger itself writes when it audits its own sensitive
/// operations (exports, cross-tenant reads, payload erasure, forced resealing).
/// </summary>
public static class AuditLedgerActions
{
    /// <summary>The resource type of ledger-maintenance records whose chain is the ledger itself.</summary>
    public const string LedgerResourceType = "AuditLedger";

    /// <summary>A range of a chain was exported (<see cref="IAuditQueryService.ExportRangeAsync"/>).</summary>
    public const string Exported = "audit.ledger.exported";

    /// <summary>A cross-tenant query ran (<see cref="IAuditQueryService.QueryAcrossTenantsAsync"/>).</summary>
    public const string CrossTenantQueried = "audit.ledger.cross_tenant_queried";

    /// <summary>A record's payload was erased (<see cref="IAuditLedgerMaintenance.ErasePayloadAsync"/>).</summary>
    public const string PayloadErased = "audit.ledger.payload_erased";

    /// <summary>A chain received a marker sealed under the current key (<see cref="IAuditLedgerMaintenance.SealAllChainsAsync"/>).</summary>
    public const string ChainResealed = "audit.ledger.chain_resealed";
}
