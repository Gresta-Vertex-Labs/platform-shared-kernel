namespace SharedKernel.Persistence.EfCore.Encryption.Rotation;

/// <summary>Result of one <see cref="IEncryptionRotationJob.RotateAsync"/> call.</summary>
/// <param name="RowsProcessed">Total rows examined across every encrypted property in the model (or resumed range).</param>
/// <param name="RowsRotated">Rows whose ciphertext was successfully re-encrypted to the expected current key.</param>
/// <param name="RowsFailed">
/// Rows a compare-and-swap update did not apply to because a concurrent writer changed them between read and
/// re-encrypt — left for a later rotation pass, never overwritten.
/// </param>
/// <param name="RowsSkippedUnparseable">
/// Rows whose stored value was not <see langword="null"/> but did not parse as an encrypted payload at all — never
/// re-encrypted, since there was no valid ciphertext to re-encrypt. This can mean the column still holds
/// unencrypted data from an in-progress migration (see <c>EncryptionOptions.AllowUnencryptedValues</c>), or that
/// the stored value was truncated or otherwise corrupted. Distinct from <paramref name="RowsFailed"/> (a
/// concurrent-write collision, always safely retryable) — a nonzero count here needs a human to look at the data,
/// not just a second rotation pass. A <see langword="null"/> stored value (a nullable encrypted property never
/// populated) is never counted here — there was nothing to encrypt in the first place, so it is not a skip.
/// </param>
/// <param name="Completed">
/// <see langword="true"/> when every encrypted property across the whole model was scanned this call;
/// <see langword="false"/> when the call stopped early (cancellation) with rows still to examine —
/// <paramref name="CheckpointToken"/> resumes from there. Note this reflects only whether every row was SCANNED —
/// a <see langword="true"/> value with a nonzero <paramref name="RowsSkippedUnparseable"/> still means some rows
/// were left un-rotated and need attention.
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
    long RowsSkippedUnparseable,
    bool Completed,
    string? CheckpointToken,
    IReadOnlyDictionary<string, long> RowsRemainingByKeyId);
