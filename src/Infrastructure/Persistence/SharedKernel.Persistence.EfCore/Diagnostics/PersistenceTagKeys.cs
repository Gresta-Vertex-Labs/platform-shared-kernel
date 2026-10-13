namespace SharedKernel.Persistence.EfCore.Diagnostics;

/// <summary>
/// The tag keys of the persistence spans and instruments: the repository keys, the audit ledger keys and the field
/// encryption keys, shared with the sibling packages through <c>InternalsVisibleTo</c>.
/// </summary>
/// <remarks>
/// Package keys are <c>persistence.{snake_name}</c> (<c>persistence.audit.*</c> and <c>persistence.encryption.*</c> for
/// the siblings); <see cref="DbCollectionName"/> and <see cref="DbOperationName"/> mirror the OpenTelemetry
/// database semantic conventions so a generic backend can group the spans like any other database span. The outcome of a
/// span is the shared <c>WellKnownTagKeys.Outcome</c>, and an error is recorded by the Primitives recorder. A tag
/// never carries a tenant, an actor, a key or a value.
/// </remarks>
internal static class PersistenceTagKeys
{
    /// <summary>The traced aggregate's CLR type name (for example <c>Order</c>).</summary>
    public const string AggregateType = "persistence.aggregate_type";

    /// <summary>The repository operation name (for example <c>GetByIdAsync</c>).</summary>
    public const string Operation = "persistence.operation";

    /// <summary>OpenTelemetry <c>db.collection.name</c>, set to the value of <see cref="AggregateType"/>.</summary>
    public const string DbCollectionName = "db.collection.name";

    /// <summary>OpenTelemetry <c>db.operation.name</c>, set to the value of <see cref="Operation"/>.</summary>
    public const string DbOperationName = "db.operation.name";

    /// <summary>The resource type of an audit chain.</summary>
    public const string AuditResourceType = "persistence.audit.resource_type";

    /// <summary>The number of records a sealing pass sealed.</summary>
    public const string AuditSealedRecords = "persistence.audit.sealed_records";

    /// <summary>The failure kind of a chain verification.</summary>
    public const string AuditFailureKind = "persistence.audit.failure_kind";

    /// <summary>The reason a field-encryption failure or skip happened.</summary>
    public const string EncryptionReason = "persistence.encryption.reason";

    /// <summary>The operation an encryption maintenance row was written by.</summary>
    public const string EncryptionMaintenanceOperation = "persistence.encryption.maintenance_operation";
}
