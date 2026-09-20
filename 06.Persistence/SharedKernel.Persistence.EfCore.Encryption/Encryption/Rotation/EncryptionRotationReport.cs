namespace SharedKernel.Persistence.EfCore.Encryption.Rotation;

/// <summary>Result of one <see cref="IEncryptionRotationJob.RotateAsync"/> call.</summary>
/// <param name="RowsProcessed">Total rows examined across every encrypted property in the model (or resumed range).</param>
/// <param name="RowsRotated">Rows whose ciphertext was successfully re-encrypted to the expected current key.</param>
/// <param name="RowsFailed">
/// Rows a compare-and-swap update did not apply to because a concurrent writer changed them between read and
/// re-encrypt — left for a later rotation pass, never overwritten.
/// </param>
/// <param name="Completed">
/// <see langword="true"/> when every encrypted property across the whole model was scanned this call;
/// <see langword="false"/> when the call stopped early (cancellation) with rows still to examine —
/// <paramref name="CheckpointToken"/> resumes from there.
/// </param>
/// <param name="CheckpointToken">
/// An opaque token to pass back into the next <see cref="IEncryptionRotationJob.RotateAsync"/> call to resume
/// exactly where this call left off. <see langword="null"/> when <paramref name="Completed"/> is
/// <see langword="true"/>.
/// </param>
/// <param name="RowsRemainingByKeyId">
/// Rows observed THIS call whose ciphertext key id was not the expected current key, grouped by that key id —
/// rows still needing rotation, as seen so far. Calling <see cref="IEncryptionRotationJob.RotateAsync"/> to
/// completion (<paramref name="Completed"/> <see langword="true"/>) makes this the exact remaining count
/// model-wide; a resumed, still-incomplete run only reflects the range scanned so far.
/// </param>
public sealed record EncryptionRotationReport(
    long RowsProcessed,
    long RowsRotated,
    long RowsFailed,
    bool Completed,
    string? CheckpointToken,
    IReadOnlyDictionary<string, long> RowsRemainingByKeyId);
