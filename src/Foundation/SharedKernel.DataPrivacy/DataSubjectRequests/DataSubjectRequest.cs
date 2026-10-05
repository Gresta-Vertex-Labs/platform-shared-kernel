namespace SharedKernel.DataPrivacy.DataSubjectRequests;

/// <summary>
/// One data subject's request to see or erase their data (GDPR Articles 15, 17 and 20; KVKK
/// Article 11), as passed to each service's <see cref="IDataSubjectRequestHandler"/>.
/// </summary>
/// <remarks>
/// <see cref="RequestId"/> identifies the request across retries and across services. A handler
/// that receives the same id again returns the outcome of the first run instead of repeating it,
/// so an orchestrator can retry safely.
/// </remarks>
public sealed record DataSubjectRequest
{
    /// <summary>Initializes a new instance of the <see cref="DataSubjectRequest"/> class.</summary>
    /// <param name="requestId">The request's id, the same in every service and on every retry.</param>
    /// <param name="subjectId">The data subject's id, as every receiving service knows it.</param>
    /// <param name="requestedAt">When the data subject made the request; the statutory deadline runs from here.</param>
    /// <param name="tenantId">The tenant the subject belongs to, or <see langword="null"/> in a single-tenant service.</param>
    /// <exception cref="ArgumentException"><paramref name="requestId"/> or <paramref name="subjectId"/> is empty or whitespace.</exception>
    public DataSubjectRequest(string requestId, string subjectId, DateTimeOffset requestedAt, string? tenantId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestId);
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);

        RequestId = requestId;
        SubjectId = subjectId;
        RequestedAt = requestedAt;
        TenantId = tenantId;
    }

    /// <summary>Gets the request's id, the same in every service and on every retry.</summary>
    public string RequestId { get; }

    /// <summary>Gets the data subject's id.</summary>
    public string SubjectId { get; }

    /// <summary>Gets when the data subject made the request.</summary>
    public DateTimeOffset RequestedAt { get; }

    /// <summary>Gets the tenant the subject belongs to, or <see langword="null"/>.</summary>
    public string? TenantId { get; }
}
