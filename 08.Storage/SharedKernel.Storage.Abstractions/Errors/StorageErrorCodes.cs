namespace SharedKernel.Storage;

/// <summary>
/// The stable <c>Error.Code</c> values storage operations return. Match on these, never on error messages, which
/// may change. The <c>ErrorType</c> of each code is given in parentheses; it drives the HTTP status when an
/// error is turned into a ProblemDetails response.
/// </summary>
/// <remarks>
/// Codes marked "before any I/O" are returned by request validation (<see cref="StorageValidation"/>) and
/// <see cref="NotSupported"/> checks, before anything is sent to the provider, identically for every provider.
/// </remarks>
public static class StorageErrorCodes
{
    /// <summary>
    /// <c>storage.not_found</c> (<c>NotFound</c>): the object, version or multipart upload does not exist. Not
    /// returned by <see cref="IFileStorage.ExistsAsync"/> (which answers <see langword="false"/>) or by deletes.
    /// </summary>
    public const string NotFound = "storage.not_found";

    /// <summary>
    /// <c>storage.access_denied</c> (<c>Forbidden</c>): the credentials may not perform the operation, the bucket
    /// belongs to another account than the store's expected owner, or — on S3-family providers without list
    /// permission — the object is missing.
    /// </summary>
    public const string AccessDenied = "storage.access_denied";

    /// <summary>
    /// <c>storage.invalid_key</c> (<c>Validation</c>, before any I/O): a key or listing prefix breaks the key rules
    /// of <see cref="StorageValidation.ValidateKey(string)"/>.
    /// </summary>
    public const string InvalidKey = "storage.invalid_key";

    /// <summary>
    /// <c>storage.invalid_tenant</c> (<c>Validation</c>, before any I/O): a tenant id is empty, longer than
    /// <see cref="StorageValidation.MaxTenantIdLength"/>, <c>.</c>/<c>..</c>, or contains characters outside
    /// <c>A-Z a-z 0-9 . _ -</c>. <see cref="ITenantFileStorage.ForTenant(string)"/> throws
    /// <see cref="ArgumentException"/> with this error's message instead of returning it.
    /// </summary>
    public const string InvalidTenant = "storage.invalid_tenant";

    /// <summary>
    /// <c>storage.invalid_request</c> (<c>Validation</c>): an option is invalid — content type, header value,
    /// metadata, tags, tier, checksum format, length, page size, part number, upload id, presigned-form sizes —
    /// or the parts passed to <see cref="IFileStorage.CompleteMultipartUploadAsync"/> do not complete the upload.
    /// </summary>
    public const string InvalidRequest = "storage.invalid_request";

    /// <summary>
    /// <c>storage.expiry_too_long</c> (<c>Validation</c>, before any I/O): a presign expiry is zero, negative or
    /// longer than the store's maximum presign expiry.
    /// </summary>
    public const string ExpiryTooLong = "storage.expiry_too_long";

    /// <summary>
    /// <c>storage.already_exists</c> (<c>Conflict</c>): a write with <see cref="WriteCondition.IfNotExists"/> found
    /// an existing object.
    /// </summary>
    public const string AlreadyExists = "storage.already_exists";

    /// <summary>
    /// <c>storage.precondition_failed</c> (<c>Conflict</c>): an <c>If-Match</c> ETag condition failed — the object
    /// changed or is gone. Read it again and retry with its new ETag.
    /// </summary>
    public const string PreconditionFailed = "storage.precondition_failed";

    /// <summary>
    /// <c>storage.checksum_mismatch</c> (<c>Validation</c>): the provider received bytes that do not match the
    /// supplied <see cref="FileUploadOptions.ChecksumSha256"/>; nothing was stored.
    /// </summary>
    public const string ChecksumMismatch = "storage.checksum_mismatch";

    /// <summary>
    /// <c>storage.invalid_range</c> (<c>Validation</c>): the requested <see cref="ByteRange"/> starts at or beyond
    /// the end of the object.
    /// </summary>
    public const string InvalidRange = "storage.invalid_range";

    /// <summary>
    /// <c>storage.not_supported</c> (<c>Unexpected</c>, before any I/O): the store's provider does not support a
    /// requested feature — for example conditional writes or SHA-256 checksums on Huawei Cloud OBS — so the
    /// request is refused rather than silently degraded. Also returned when the provider answers 501.
    /// </summary>
    public const string NotSupported = "storage.not_supported";

    /// <summary>
    /// <c>storage.unavailable</c> (<c>Unavailable</c>): the provider is unreachable, throttling, timing out or
    /// failing (5xx), after the provider client's own retries. Retrying later may succeed.
    /// </summary>
    public const string Unavailable = "storage.unavailable";

    /// <summary>
    /// <c>storage.provider_error</c> (<c>Unexpected</c>): the provider rejected the request for another reason —
    /// for example a missing bucket, a bucket in another region, or a server-side copy over 5 GiB. The details
    /// are logged with the provider's request id, never returned.
    /// </summary>
    public const string ProviderError = "storage.provider_error";
}
