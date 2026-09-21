namespace SharedKernel.Persistence.EfCore.Auditing;

/// <summary>
/// Explicit table/column/index names for <see cref="Abstractions.Auditing.AuditRecord"/> — pinned
/// rather than left to whichever naming convention (or none) the consuming service's <c>DbContext</c>
/// happens to apply.
/// </summary>
/// <remarks>
/// <para>
/// <c>EfAuditTrailWriter</c> appends via raw ADO.NET, deliberately bypassing EF Core's
/// change tracker/<c>SaveChanges</c> pipeline entirely (see its own remarks for why) — which means it
/// cannot ask the running <c>DbContext</c>'s model "what did the active naming convention call this
/// column." Pinning explicit, migration-controlled names — the same practice most hand-maintained
/// ledger/financial schemas already follow — makes the writer's raw SQL and
/// <see cref="AuditRecordEntityConfiguration"/>'s EF mapping provably agree, independent of whether
/// snake_case naming (<c>SharedKernel.Persistence.EfCore</c>'s <c>UsePostgreSQL()</c>) is even in use.
/// </para>
/// </remarks>
internal static class AuditSchema
{
    public const string TableName = "audit_records";

    public const string Id = "id";
    public const string TenantId = "tenant_id";
    public const string ActorId = "actor_id";
    public const string ActorKind = "actor_kind";
    public const string Action = "action";
    public const string ResourceType = "resource_type";
    public const string ResourceId = "resource_id";
    public const string Sequence = "sequence";
    public const string OccurredOn = "occurred_on";
    public const string BeforeSnapshot = "before_snapshot";
    public const string AfterSnapshot = "after_snapshot";
    public const string CorrelationId = "correlation_id";
    public const string ApprovalId = "approval_id";
    public const string Outcome = "outcome";
    public const string ErrorCode = "error_code";
    public const string ClientId = "client_id";
    public const string SessionId = "session_id";
    public const string ImpersonatorId = "impersonator_id";
    public const string SourceService = "source_service";
    public const string IdempotencyKey = "idempotency_key";
    public const string HashAlgorithm = "hash_algorithm";
    public const string SchemaVersion = "schema_version";
    public const string KeyId = "key_id";
    public const string RecordHash = "record_hash";
    public const string PreviousRecordHash = "previous_record_hash";

    /// <summary>
    /// The database-generated (never application-written), non-nullable chain-partition key column —
    /// see <c>AuditChainKeyFormat</c>'s remarks for why the domain's nullable <see cref="TenantId"/>
    /// cannot itself be the uniqueness/lookup key.
    /// </summary>
    public const string ChainKey = "chain_key";

    public const string IndexChainSequence = "ux_audit_records_chain_sequence";
    public const string IndexChainIdempotencyKey = "ux_audit_records_chain_idempotency_key";
    public const string IndexActorActions = "ix_audit_records_tenant_actor_occurred_on";
    public const string IndexChainResource = "ix_audit_records_chain_resource_sequence";
}
