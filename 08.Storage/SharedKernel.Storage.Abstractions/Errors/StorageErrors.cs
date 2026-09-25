using SharedKernel.Primitives.Errors;

namespace SharedKernel.Storage;

/// <summary>
/// Builds the <see cref="Error"/> values storage operations return, one factory per <see cref="StorageErrorCodes"/>
/// code. For provider packages and test doubles; application code matches on <see cref="Error.Code"/> instead.
/// </summary>
/// <remarks>
/// Providers build every storage error through these factories, never inline, so codes, error types and message
/// shapes stay identical across providers. Messages name the store and key the caller supplied, never the bucket,
/// endpoint or provider request id, because they can reach an HTTP response; providers log those details instead.
/// A tenant view removes its <c>tenants/{id}/</c> prefix from keys quoted in messages.
/// </remarks>
public static class StorageErrors
{
    /// <summary>
    /// Builds <see cref="StorageErrorCodes.NotFound"/>: the object, version or upload does not exist.
    /// </summary>
    /// <param name="store">The store name.</param>
    /// <param name="key">The object key.</param>
    /// <returns>A <see cref="ErrorType.NotFound"/> error.</returns>
    public static Error NotFound(string store, string key) =>
        Error.NotFound(StorageErrorCodes.NotFound, $"Object '{key}' was not found in store '{store}'.");

    /// <summary>
    /// Builds <see cref="StorageErrorCodes.AccessDenied"/>: the credentials may not perform the operation.
    /// </summary>
    /// <param name="store">The store name.</param>
    /// <param name="key">The object key, or <see langword="null"/> for a store-wide operation.</param>
    /// <returns>A <see cref="ErrorType.Forbidden"/> error.</returns>
    public static Error AccessDenied(string store, string? key = null) =>
        Error.Forbidden(
            StorageErrorCodes.AccessDenied,
            key is null ? $"Access to store '{store}' was denied." : $"Access to object '{key}' in store '{store}' was denied.");

    /// <summary>Builds <see cref="StorageErrorCodes.InvalidKey"/>: a key or prefix breaks the key rules.</summary>
    /// <param name="key">The rejected key, or <see langword="null"/>.</param>
    /// <param name="reason">Which rule it breaks, as a sentence ending with a period, e.g. <c>it is empty.</c></param>
    /// <returns>A <see cref="ErrorType.Validation"/> error.</returns>
    public static Error InvalidKey(string? key, string reason) =>
        Error.Validation(StorageErrorCodes.InvalidKey, $"Object key '{key}' is invalid: {reason}");

    /// <summary>Builds <see cref="StorageErrorCodes.InvalidRequest"/>: an option of the request is invalid.</summary>
    /// <param name="message">The whole error message: what is wrong, naming the option.</param>
    /// <returns>A <see cref="ErrorType.Validation"/> error.</returns>
    public static Error InvalidRequest(string message) =>
        Error.Validation(StorageErrorCodes.InvalidRequest, message);

    /// <summary>
    /// Builds <see cref="StorageErrorCodes.ExpiryTooLong"/>: a presign expiry is not positive or exceeds the store's
    /// maximum.
    /// </summary>
    /// <param name="requested">The requested expiry.</param>
    /// <param name="max">The store's maximum.</param>
    /// <returns>A <see cref="ErrorType.Validation"/> error naming both values.</returns>
    public static Error ExpiryTooLong(TimeSpan requested, TimeSpan max) =>
        Error.Validation(
            StorageErrorCodes.ExpiryTooLong,
            $"Presign expiry '{requested}' must be positive and at most '{max}'.");

    /// <summary>
    /// Builds <see cref="StorageErrorCodes.AlreadyExists"/>: a create-only write found an existing object.
    /// </summary>
    /// <param name="store">The store name.</param>
    /// <param name="key">The object key.</param>
    /// <returns>A <see cref="ErrorType.Conflict"/> error.</returns>
    public static Error AlreadyExists(string store, string key) =>
        Error.Conflict(StorageErrorCodes.AlreadyExists, $"Object '{key}' already exists in store '{store}'.");

    /// <summary>
    /// Builds <see cref="StorageErrorCodes.PreconditionFailed"/>: an <c>If-Match</c> condition failed.
    /// </summary>
    /// <param name="store">The store name.</param>
    /// <param name="key">The object key.</param>
    /// <returns>A <see cref="ErrorType.Conflict"/> error.</returns>
    public static Error PreconditionFailed(string store, string key) =>
        Error.Conflict(
            StorageErrorCodes.PreconditionFailed,
            $"Object '{key}' in store '{store}' no longer matches the expected version.");

    /// <summary>
    /// Builds <see cref="StorageErrorCodes.ChecksumMismatch"/>: the received bytes do not match the supplied
    /// checksum.
    /// </summary>
    /// <param name="store">The store name.</param>
    /// <param name="key">The object key.</param>
    /// <returns>A <see cref="ErrorType.Validation"/> error.</returns>
    public static Error ChecksumMismatch(string store, string key) =>
        Error.Validation(
            StorageErrorCodes.ChecksumMismatch,
            $"The content uploaded to '{key}' in store '{store}' does not match its checksum.");

    /// <summary>
    /// Builds <see cref="StorageErrorCodes.InvalidRange"/>: the requested range starts beyond the end of the
    /// object.
    /// </summary>
    /// <param name="store">The store name.</param>
    /// <param name="key">The object key.</param>
    /// <returns>A <see cref="ErrorType.Validation"/> error.</returns>
    public static Error InvalidRange(string store, string key) =>
        Error.Validation(
            StorageErrorCodes.InvalidRange,
            $"The requested range of object '{key}' in store '{store}' cannot be satisfied.");

    /// <summary>
    /// Builds <see cref="StorageErrorCodes.NotSupported"/>: the store's provider does not support a feature.
    /// </summary>
    /// <param name="store">The store name.</param>
    /// <param name="feature">The feature, completing "does not support ...", e.g. <c>conditional writes</c>.</param>
    /// <returns>An <see cref="ErrorType.Unexpected"/> error.</returns>
    public static Error NotSupported(string store, string feature) =>
        Error.Unexpected(StorageErrorCodes.NotSupported, $"Store '{store}' does not support {feature}.");

    /// <summary>
    /// Builds <see cref="StorageErrorCodes.Unavailable"/>: the provider is unreachable, throttling or failing.
    /// </summary>
    /// <param name="store">The store name.</param>
    /// <param name="operation">The operation, e.g. <c>upload</c>; the message says it can be retried later.</param>
    /// <returns>An <see cref="ErrorType.Unexpected"/> error.</returns>
    public static Error Unavailable(string store, string operation) =>
        Error.Unexpected(
            StorageErrorCodes.Unavailable,
            $"Store '{store}' is unavailable; the {operation} can be retried later.");

    /// <summary>
    /// Builds <see cref="StorageErrorCodes.ProviderError"/>: the provider rejected the request for another reason.
    /// </summary>
    /// <param name="store">The store name.</param>
    /// <param name="operation">The operation, e.g. <c>copy</c>.</param>
    /// <returns>An <see cref="ErrorType.Unexpected"/> error.</returns>
    public static Error ProviderError(string store, string operation) =>
        Error.Unexpected(StorageErrorCodes.ProviderError, $"Store '{store}' rejected the {operation}.");
}
