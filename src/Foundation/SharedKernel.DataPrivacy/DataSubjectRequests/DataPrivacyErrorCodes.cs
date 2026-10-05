namespace SharedKernel.DataPrivacy.DataSubjectRequests;

/// <summary>The error codes an <see cref="IDataSubjectRequestHandler"/> fails with.</summary>
/// <remarks>
/// An unknown subject is not an error, and neither is data kept for a legal reason; see
/// <see cref="IDataSubjectRequestHandler"/>. Any other failure is an
/// <c>Error.Unexpected</c> with your own code.
/// </remarks>
public static class DataPrivacyErrorCodes
{
    /// <summary>
    /// The request id was already used for a different subject or tenant; return it as
    /// <c>Error.Conflict</c>. It is never retried.
    /// </summary>
    public const string RequestIdConflict = "data_privacy.request_id_conflict";

    /// <summary>
    /// The request cannot be carried out yet, for example because a migration or a backup restore
    /// is running; return it as <c>Error.Unexpected</c>. The orchestrator retries later.
    /// </summary>
    public const string TemporarilyUnavailable = "data_privacy.temporarily_unavailable";
}
