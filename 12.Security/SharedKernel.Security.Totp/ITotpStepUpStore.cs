namespace SharedKernel.Security.Totp;

/// <summary>Records when a sign-in session last completed a TOTP step-up.</summary>
/// <remarks>
/// Implemented by the consuming service over storage shared by every replica. Entries are keyed by subject and
/// session, so a step-up never carries over to the user's other sessions. The store is read on every authenticated
/// request of a user with a session id; keep it fast.
/// </remarks>
public interface ITotpStepUpStore
{
    /// <summary>Records a step-up, replacing any earlier one for the session.</summary>
    /// <param name="subjectId">The user's subject id.</param>
    /// <param name="sessionId">The session id.</param>
    /// <param name="verifiedAt">When the step-up succeeded.</param>
    /// <param name="expiresAt">When the entry may be deleted.</param>
    /// <param name="cancellationToken">A token to cancel the write.</param>
    /// <returns>A task that completes when the step-up is recorded.</returns>
    ValueTask RecordAsync(string subjectId, string sessionId, DateTimeOffset verifiedAt, DateTimeOffset expiresAt, CancellationToken cancellationToken);

    /// <summary>Returns when the session last completed a step-up.</summary>
    /// <param name="subjectId">The user's subject id.</param>
    /// <param name="sessionId">The session id.</param>
    /// <param name="cancellationToken">A token to cancel the read.</param>
    /// <returns>The time of the last step-up, or <see langword="null"/> when there is none.</returns>
    ValueTask<DateTimeOffset?> GetLastVerifiedAsync(string subjectId, string sessionId, CancellationToken cancellationToken);
}
