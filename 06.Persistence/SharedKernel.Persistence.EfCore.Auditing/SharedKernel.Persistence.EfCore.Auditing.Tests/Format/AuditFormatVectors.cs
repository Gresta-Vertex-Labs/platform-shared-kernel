using SharedKernel.Execution.Auditing;
using SharedKernel.Execution.Context;
using SharedKernel.Persistence.EfCore.Auditing.Format;

namespace SharedKernel.Persistence.EfCore.Auditing.Tests.Format;

/// <summary>The fixed inputs of the test vectors published in AUDIT-FORMAT.md.</summary>
internal static class AuditFormatVectors
{
    public static readonly byte[] Key = Enumerable.Repeat((byte)0x42, 32).ToArray();

    public static readonly byte[] Salt = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();

    public const string KeyId = "k1";

    /// <summary>Vector 1: a tenant record, genesis link (sequence 1), minimal optional fields.</summary>
    public static readonly LedgerRecordFields Record1 = new()
    {
        Id = new Guid("0190a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b"),
        TenantId = new Guid("11111111-2222-3333-4444-555555555555"),
        ResourceType = "Order",
        ResourceId = "order-42",
        Action = "OrderApproved",
        Outcome = AuditOutcome.Succeeded,
        ActorId = "user-7",
        ActorKind = ActorKind.User,
        SourceService = "orders",
        OccurredOn = DateTimeOffset.FromUnixTimeMilliseconds(1_767_225_600_000).AddTicks(1_234_560), // 2026-01-01T00:00:00.123456Z
        PayloadHash = AuditV3Format.ComputePayloadCommitment(Salt, "{\"status\":\"pending\"}", "{\"status\":\"approved\"}"),
    };

    /// <summary>Vector 2: a system-chain record (no tenant), sequence 2 after vector 1's MAC, every optional field present, no snapshots.</summary>
    public static readonly LedgerRecordFields Record2 = new()
    {
        Id = new Guid("0190a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5c"),
        TenantId = null,
        ResourceType = "Order",
        ResourceId = "order-43",
        Action = "OrderRejected",
        Outcome = AuditOutcome.Failed,
        ErrorCode = "order.limit_exceeded",
        ActorId = "svc-batch",
        ActorKind = ActorKind.System,
        ClientId = "client-1",
        SessionId = "session-1",
        ImpersonatorId = "admin-1",
        SourceService = "orders",
        CorrelationId = "corr-1",
        TraceId = "0af7651916cd43dd8448eb211c80319c",
        ApprovalId = "approval-1",
        IdempotencyKey = "idem-1",
        OccurredOn = DateTimeOffset.FromUnixTimeMilliseconds(1_767_225_601_000),
        PayloadHash = AuditV3Format.ComputePayloadCommitment(Salt, null, null),
    };

    public static readonly Guid CheckpointId = new("0190a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a60");

    public static readonly DateTimeOffset CheckpointCreatedOn = DateTimeOffset.FromUnixTimeMilliseconds(1_767_229_200_000);
}
