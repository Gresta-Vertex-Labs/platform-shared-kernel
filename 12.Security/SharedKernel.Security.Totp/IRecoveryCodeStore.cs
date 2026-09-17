namespace SharedKernel.Security.Totp;

/// <summary>Stores a user's hashed recovery codes and marks them used.</summary>
/// <remarks>
/// Implemented by the consuming service. Save <see cref="TotpEnrollment.StoredRecoveryCodes"/> when the enrollment is
/// confirmed, replacing any earlier codes.
/// </remarks>
public interface IRecoveryCodeStore
{
    /// <summary>Returns the user's recovery codes that were not used yet.</summary>
    /// <param name="subjectId">The user's subject id.</param>
    /// <param name="cancellationToken">A token to cancel the read.</param>
    /// <returns>The unused codes; empty when there are none.</returns>
    ValueTask<IReadOnlyList<StoredRecoveryCode>> GetUnusedAsync(string subjectId, CancellationToken cancellationToken);

    /// <summary>Marks a code used, unless it already is. The check and the update must be one atomic operation.</summary>
    /// <param name="subjectId">The user's subject id.</param>
    /// <param name="codeId">The code's <see cref="StoredRecoveryCode.Id"/>.</param>
    /// <param name="usedAt">When the code was used.</param>
    /// <param name="cancellationToken">A token to cancel the update.</param>
    /// <returns><see langword="true"/> when this call marked the code; <see langword="false"/> when it was already used.</returns>
    ValueTask<bool> TryMarkUsedAsync(string subjectId, string codeId, DateTimeOffset usedAt, CancellationToken cancellationToken);
}
