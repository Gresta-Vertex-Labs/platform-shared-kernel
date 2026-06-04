namespace SharedKernel.Persistence.EfCore.Encryption;

/// <summary>
/// Result record returned by <see cref="IEncryptionRotationJob.RotateAsync"/> after a
/// field-level encryption key rotation pass.
/// </summary>
/// <param name="RowsProcessed">Total number of rows examined across all encrypted entity types.</param>
/// <param name="RowsRotated">Number of rows that were successfully re-encrypted to the new key version.</param>
/// <param name="RowsFailed">Number of rows that encountered errors during rotation.</param>
/// <param name="Errors">Human-readable error messages from failed rows, one entry per failure.</param>
public sealed record EncryptionRotationResult(
    int RowsProcessed,
    int RowsRotated,
    int RowsFailed,
    IReadOnlyList<string> Errors);
