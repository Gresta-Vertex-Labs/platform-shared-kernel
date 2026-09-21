using System.Data.Common;
using SharedKernel.Persistence.Abstractions.Auditing;
using SharedKernel.Application.Auditing;
using SharedKernel.Application.Context;

namespace SharedKernel.Persistence.EfCore.Auditing;

/// <summary>
/// The column list and row-mapping shared by every raw-ADO.NET read of <see cref="AuditRecord"/> in
/// this package (<c>EfAuditTrailWriter</c>'s idempotency-key lookup, <c>EfAuditQueryService</c>'s
/// checkpoint/export/verification paths) — ONE place declaring the column order, so a reader's
/// ordinal-based <see cref="Map"/> can never drift from what <see cref="SelectColumns"/> selects.
/// </summary>
internal static class AuditRecordSql
{
    /// <summary>
    /// The <c>SELECT</c> clause (including the trailing space) selecting every <see cref="AuditRecord"/>
    /// column, in the exact ordinal order <see cref="Map"/> reads them back in.
    /// </summary>
    public const string SelectColumns =
        $"""
        SELECT "{AuditSchema.Id}", "{AuditSchema.TenantId}", "{AuditSchema.ActorId}", "{AuditSchema.ActorKind}",
               "{AuditSchema.Action}", "{AuditSchema.ResourceType}", "{AuditSchema.ResourceId}", "{AuditSchema.Sequence}",
               "{AuditSchema.OccurredOn}", "{AuditSchema.BeforeSnapshot}", "{AuditSchema.AfterSnapshot}",
               "{AuditSchema.CorrelationId}", "{AuditSchema.ApprovalId}", "{AuditSchema.Outcome}", "{AuditSchema.ErrorCode}",
               "{AuditSchema.ClientId}", "{AuditSchema.SessionId}", "{AuditSchema.ImpersonatorId}", "{AuditSchema.SourceService}",
               "{AuditSchema.IdempotencyKey}", "{AuditSchema.HashAlgorithm}", "{AuditSchema.SchemaVersion}", "{AuditSchema.KeyId}",
               "{AuditSchema.RecordHash}", "{AuditSchema.PreviousRecordHash}"

        """;

    /// <summary>Maps the current row of <paramref name="reader"/> to an <see cref="AuditRecord"/>, using <see cref="SelectColumns"/>'s ordinal order.</summary>
    public static AuditRecord Map(DbDataReader reader) => new()
    {
        Id = reader.GetGuid(0),
        TenantId = reader.IsDBNull(1) ? null : reader.GetGuid(1),
        ActorId = reader.GetString(2),
        ActorKind = (ActorKind)reader.GetInt32(3),
        Action = reader.GetString(4),
        ResourceType = reader.GetString(5),
        ResourceId = reader.GetString(6),
        Sequence = reader.GetInt64(7),
        OccurredOn = reader.GetFieldValue<DateTimeOffset>(8),
        BeforeSnapshot = reader.IsDBNull(9) ? null : reader.GetString(9),
        AfterSnapshot = reader.IsDBNull(10) ? null : reader.GetString(10),
        CorrelationId = reader.IsDBNull(11) ? null : reader.GetString(11),
        ApprovalId = reader.IsDBNull(12) ? null : reader.GetString(12),
        Outcome = (AuditOutcome)reader.GetInt32(13),
        ErrorCode = reader.IsDBNull(14) ? null : reader.GetString(14),
        ClientId = reader.IsDBNull(15) ? null : reader.GetString(15),
        SessionId = reader.IsDBNull(16) ? null : reader.GetString(16),
        ImpersonatorId = reader.IsDBNull(17) ? null : reader.GetString(17),
        SourceService = reader.IsDBNull(18) ? null : reader.GetString(18),
        IdempotencyKey = reader.IsDBNull(19) ? null : reader.GetString(19),
        HashAlgorithm = reader.GetString(20),
        SchemaVersion = reader.GetInt32(21),
        KeyId = reader.GetString(22),
        RecordHash = reader.GetString(23),
        PreviousRecordHash = reader.IsDBNull(24) ? null : reader.GetString(24),
    };
}
