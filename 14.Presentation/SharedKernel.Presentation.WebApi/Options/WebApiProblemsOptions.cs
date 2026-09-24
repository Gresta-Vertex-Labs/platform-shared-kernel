namespace SharedKernel.Presentation.WebApi;

/// <summary>Settings for RFC 9457 <c>application/problem+json</c> error responses.</summary>
public sealed class WebApiProblemsOptions
{
    // The codes of the platform's version conflicts. They are owned by 06.Persistence (ConcurrencyVersion) and
    // 08.Storage (StorageErrorCodes), which this package may not reference; 00.Governance pins these literals to
    // the owning constants.
    private const string PersistenceConcurrencyConflict = "persistence.concurrency_conflict";

    private const string StoragePreconditionFailed = "storage.precondition_failed";

    private const string StorageAlreadyExists = "storage.already_exists";

    private Uri? _typeBaseUri;

    /// <summary>
    /// Gets or sets an absolute base URI for the <c>type</c> member. When set, every problem's <c>type</c> is this URI
    /// followed by its <c>errorCode</c>, such as <c>https://errors.example.com/not_found.default</c>, so a service
    /// can document each code at a stable address. When <see langword="null"/> (the default), <c>type</c> is the
    /// RFC section of the status code.
    /// </summary>
    /// <remarks>
    /// A trailing <c>/</c> is added when the path lacks one, so <c>https://errors.example.com/problems</c> gives
    /// <c>https://errors.example.com/problems/not_found.default</c>. The URI must not have a query or a fragment.
    /// </remarks>
    public Uri? TypeBaseUri
    {
        get => _typeBaseUri;
        set => _typeBaseUri = WithTrailingSlash(value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether an unhandled exception's type, message and stack trace are returned
    /// in the <c>exception</c> member. <see langword="null"/> (the default) returns them in the Development
    /// environment only. Never enable this in production: stack traces reveal internals, and the host logs a
    /// warning at startup when it is enabled outside Development.
    /// </summary>
    public bool? IncludeExceptionDetails { get; set; }

    /// <summary>
    /// Gets or sets the <c>Retry-After</c> sent with a 503 Service Unavailable, rounded up to whole seconds.
    /// <see langword="null"/> (the default) sends none.
    /// </summary>
    public TimeSpan? UnavailableRetryAfter { get; set; }

    /// <summary>
    /// Gets the codes of <see cref="Primitives.Errors.ErrorType.Conflict"/> errors that mean "the version the client
    /// named is not current". A request carrying <c>If-Match</c> or <c>If-None-Match</c> that fails with one of them
    /// is answered 412 Precondition Failed (RFC 9110 section 13.1) instead of 409, keeping the error's code.
    /// </summary>
    /// <remarks>
    /// Defaults to <c>persistence.concurrency_conflict</c> (a stale row version), <c>storage.precondition_failed</c>
    /// (a stale object ETag) and <c>storage.already_exists</c> (a create-only write, <c>If-None-Match: *</c>, found
    /// an object). Every other conflict — a duplicate name, a state that forbids the change — stays 409, whatever
    /// headers the request carries. Configured values are added to the defaults; clear the list in code to replace
    /// them. Codes compare ordinally.
    /// </remarks>
    public IList<string> PreconditionFailedErrorCodes { get; } =
    [
        PersistenceConcurrencyConflict,
        StoragePreconditionFailed,
        StorageAlreadyExists,
    ];

    // Only an absolute URI without query and fragment is changed: a relative one (Query throws for it) or one with a
    // query is left for validation to refuse.
    private static Uri? WithTrailingSlash(Uri? uri)
    {
        if (uri is null || !uri.IsAbsoluteUri || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.AbsolutePath.EndsWith('/'))
        {
            return uri;
        }

        return new Uri(uri.AbsoluteUri + "/", UriKind.Absolute);
    }
}
