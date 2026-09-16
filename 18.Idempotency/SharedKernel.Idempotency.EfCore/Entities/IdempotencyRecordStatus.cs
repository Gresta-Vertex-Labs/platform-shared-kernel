namespace SharedKernel.Idempotency.EfCore.Entities;

/// <summary>The lifecycle state of an <see cref="IdempotencyKeyRecord"/> row.</summary>
internal enum IdempotencyRecordStatus
{
    /// <summary>Reserved, not yet completed.</summary>
    InProgress,

    /// <summary>Completed — <see cref="IdempotencyKeyRecord.Response"/> holds the stored response.</summary>
    Completed,
}
