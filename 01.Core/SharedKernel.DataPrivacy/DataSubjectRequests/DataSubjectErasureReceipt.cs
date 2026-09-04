namespace SharedKernel.DataPrivacy.DataSubjectRequests;

/// <summary>
/// The result of a successful <see cref="IDataSubjectRequestHandler.RequestErasureAsync"/> call —
/// confirmation of what one service erased about a data subject.
/// </summary>
/// <param name="SubjectId">The data subject's identifier, as known to the erasing service.</param>
/// <param name="ErasedAtUtc">The instant the erasure completed.</param>
/// <param name="RecordsAffected">The number of records erased (or anonymized) by this call.</param>
public sealed record DataSubjectErasureReceipt(
    string SubjectId,
    DateTimeOffset ErasedAtUtc,
    int RecordsAffected);
