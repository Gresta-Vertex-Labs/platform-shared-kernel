using System.Text;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Storage;

/// <summary>
/// The request rules every store enforces before any I/O, identically for every provider. Stores apply them
/// already; call them yourself to validate user input early (for example a key taken from a route),
/// or when implementing a provider or test double.
/// </summary>
/// <remarks>
/// Each method returns <see langword="null"/> when the input is valid, otherwise the <see cref="Error"/> a store
/// would return, with a <see cref="StorageErrorCodes"/> code and a message naming the rule. Methods are pure and
/// thread-safe.
/// </remarks>
/// <example>
/// <code>
/// if (StorageValidation.ValidateKey(request.Key) is Error invalid)
/// {
///     return invalid;   // storage.invalid_key, before touching the store
/// }
/// </code>
/// </example>
public static class StorageValidation
{
    /// <summary>
    /// The longest key, in UTF-8 bytes: 1024, the S3 limit. Providers apply it to the full key, including the
    /// store key prefix and the <c>tenants/{id}/</c> prefix, so the usable length of a tenant key is shorter.
    /// </summary>
    public const int MaxKeyBytes = 1024;

    /// <summary>The largest total length of user metadata keys and values: 2048 bytes (2 KiB), the S3 limit.</summary>
    public const int MaxMetadataBytes = 2048;

    /// <summary>The most tags an object can carry: 10, the S3 limit.</summary>
    public const int MaxTags = 10;

    /// <summary>The highest multipart part number: 10,000, the S3 limit.</summary>
    public const int MaxPartNumber = 10_000;

    private const int MaxTagKeyLength = 128;
    private const int MaxTagValueLength = 256;
    private const int MaxHeaderValueLength = 1024;
    private const int Sha256Bytes = 32;

    /// <summary>
    /// Validates an object key. Keys are case-sensitive; <c>/</c> separates folders, and a single trailing
    /// <c>/</c> is allowed (a folder marker). Any other Unicode character is allowed.
    /// </summary>
    /// <param name="key">The key, relative to the store (and tenant), or <see langword="null"/>.</param>
    /// <returns>
    /// <see langword="null"/>, or <see cref="StorageErrorCodes.InvalidKey"/> for a <see langword="null"/>, empty or
    /// whitespace-only key, a leading <c>/</c>, a backslash, an empty (<c>a//b</c>), <c>.</c> or <c>..</c> segment,
    /// a control character, or more than <see cref="MaxKeyBytes"/> UTF-8 bytes.
    /// </returns>
    public static Error? ValidateKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return StorageErrors.InvalidKey(key, "it is empty.");
        }

        if (Encoding.UTF8.GetByteCount(key) > MaxKeyBytes)
        {
            return StorageErrors.InvalidKey(key, $"it is longer than {MaxKeyBytes} UTF-8 bytes.");
        }

        if (key[0] == '/')
        {
            return StorageErrors.InvalidKey(key, "it starts with '/'.");
        }

        foreach (char c in key)
        {
            if (char.IsControl(c))
            {
                return StorageErrors.InvalidKey(key, "it contains a control character.");
            }

            if (c == '\\')
            {
                return StorageErrors.InvalidKey(key, "it contains '\\'; separate folders with '/'.");
            }
        }

        string[] segments = key.Split('/');
        for (int i = 0; i < segments.Length; i++)
        {
            string segment = segments[i];
            bool isTrailingFolderMarker = i == segments.Length - 1 && segment.Length == 0;
            if (isTrailingFolderMarker)
            {
                continue;
            }

            if (segment.Length == 0 || segment is "." or "..")
            {
                return StorageErrors.InvalidKey(key, "it contains an empty, '.' or '..' segment.");
            }
        }

        return null;
    }

    /// <summary>Validates a listing prefix: empty, or a valid key (<see cref="ValidateKey(string)"/>).</summary>
    /// <param name="prefix">The prefix, or <see langword="null"/>.</param>
    /// <returns>
    /// <see langword="null"/>, or <see cref="StorageErrorCodes.InvalidKey"/>, also for <see langword="null"/>.
    /// </returns>
    public static Error? ValidatePrefix(string? prefix) =>
        prefix is null ? StorageErrors.InvalidKey(prefix, "it is null.")
        : prefix.Length == 0 ? null
        : ValidateKey(prefix);

    /// <summary>
    /// Validates upload options: content type, header values, metadata, tags, tier, checksum format and a
    /// non-negative length. Whether the provider supports each feature is checked by the provider, not here.
    /// </summary>
    /// <param name="options">The options, or <see langword="null"/> (valid).</param>
    /// <returns>
    /// <see langword="null"/>, or <see cref="StorageErrorCodes.InvalidRequest"/> naming the first invalid option.
    /// </returns>
    public static Error? ValidateUpload(FileUploadOptions? options) =>
        options is null
            ? null
            : ValidateContentType(options.ContentType, nameof(FileUploadOptions.ContentType))
                ?? ValidateHeaderValue(options.ContentDisposition, nameof(FileUploadOptions.ContentDisposition))
                ?? ValidateHeaderValue(options.CacheControl, nameof(FileUploadOptions.CacheControl))
                ?? ValidateHeaderValue(options.ContentEncoding, nameof(FileUploadOptions.ContentEncoding))
                ?? ValidateMetadata(options.Metadata)
                ?? ValidateTags(options.Tags)
                ?? ValidateTier(options.Tier)
                ?? ValidateChecksum(options.ChecksumSha256, nameof(FileUploadOptions.ChecksumSha256))
                ?? (options.ContentLength < 0
                    ? StorageErrors.InvalidRequest($"{nameof(FileUploadOptions.ContentLength)} cannot be negative.")
                    : null);

    /// <summary>Validates copy options: content type, source ETag, metadata and tier.</summary>
    /// <param name="options">The options, or <see langword="null"/> (valid).</param>
    /// <returns>
    /// <see langword="null"/>, or <see cref="StorageErrorCodes.InvalidRequest"/> naming the first invalid option.
    /// </returns>
    public static Error? ValidateCopy(FileCopyOptions? options) =>
        options is null
            ? null
            : ValidateContentType(options.ContentType, nameof(FileCopyOptions.ContentType))
                ?? ValidateHeaderValue(options.SourceIfMatch, nameof(FileCopyOptions.SourceIfMatch))
                ?? ValidateMetadata(options.Metadata)
                ?? ValidateTier(options.Tier);

    /// <summary>Validates multipart upload options: content type, header values, metadata, tags and tier.</summary>
    /// <param name="options">The options, or <see langword="null"/> (valid).</param>
    /// <returns>
    /// <see langword="null"/>, or <see cref="StorageErrorCodes.InvalidRequest"/> naming the first invalid option.
    /// </returns>
    public static Error? ValidateMultipart(MultipartUploadOptions? options) =>
        options is null
            ? null
            : ValidateContentType(options.ContentType, nameof(MultipartUploadOptions.ContentType))
                ?? ValidateHeaderValue(options.ContentDisposition, nameof(MultipartUploadOptions.ContentDisposition))
                ?? ValidateHeaderValue(options.CacheControl, nameof(MultipartUploadOptions.CacheControl))
                ?? ValidateMetadata(options.Metadata)
                ?? ValidateTags(options.Tags)
                ?? ValidateTier(options.Tier);

    /// <summary>
    /// Validates a listing request: its prefix and a page size of 1 to <see cref="FileListRequest.MaxPageSize"/>.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <returns>
    /// <see langword="null"/>, <see cref="StorageErrorCodes.InvalidKey"/> for the prefix, or
    /// <see cref="StorageErrorCodes.InvalidRequest"/> for the page size.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    public static Error? ValidateList(FileListRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return ValidatePrefix(request.Prefix)
            ?? (request.PageSize is < 1 or > FileListRequest.MaxPageSize
                ? StorageErrors.InvalidRequest($"PageSize must be between 1 and {FileListRequest.MaxPageSize}.")
                : null);
    }

    /// <summary>Validates a presign expiry against the store's maximum.</summary>
    /// <param name="expiry">The requested expiry.</param>
    /// <param name="max">The store's maximum presign expiry.</param>
    /// <returns>
    /// <see langword="null"/>, or <see cref="StorageErrorCodes.ExpiryTooLong"/> when <paramref name="expiry"/> is
    /// zero, negative or greater than <paramref name="max"/>.
    /// </returns>
    public static Error? ValidateExpiry(TimeSpan expiry, TimeSpan max) =>
        expiry <= TimeSpan.Zero || expiry > max ? StorageErrors.ExpiryTooLong(expiry, max) : null;

    /// <summary>Validates presigned download options: expiry, and the response content type and disposition.</summary>
    /// <param name="options">The options.</param>
    /// <param name="maxExpiry">The store's maximum presign expiry.</param>
    /// <returns>
    /// <see langword="null"/>, <see cref="StorageErrorCodes.ExpiryTooLong"/> or
    /// <see cref="StorageErrorCodes.InvalidRequest"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    public static Error? ValidatePresignedDownload(PresignedDownloadOptions options, TimeSpan maxExpiry)
    {
        ArgumentNullException.ThrowIfNull(options);

        return ValidateExpiry(options.Expiry, maxExpiry)
            ?? ValidateHeaderValue(options.ContentDisposition, nameof(PresignedDownloadOptions.ContentDisposition))
            ?? ValidateContentType(options.ContentType, nameof(PresignedDownloadOptions.ContentType));
    }

    /// <summary>
    /// Validates presigned upload options: expiry, the required content type, checksum format and metadata.
    /// </summary>
    /// <param name="options">The options.</param>
    /// <param name="maxExpiry">The store's maximum presign expiry.</param>
    /// <returns>
    /// <see langword="null"/>, <see cref="StorageErrorCodes.ExpiryTooLong"/> or
    /// <see cref="StorageErrorCodes.InvalidRequest"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    public static Error? ValidatePresignedUpload(PresignedUploadOptions options, TimeSpan maxExpiry)
    {
        ArgumentNullException.ThrowIfNull(options);

        return ValidateExpiry(options.Expiry, maxExpiry)
            ?? RequireContentType(options.ContentType, nameof(PresignedUploadOptions.ContentType))
            ?? ValidateChecksum(options.ChecksumSha256, nameof(PresignedUploadOptions.ChecksumSha256))
            ?? ValidateMetadata(options.Metadata);
    }

    /// <summary>
    /// Validates presigned form upload options: sizes (<c>MaxSize</c> at least 1, <c>MinSize</c> from 0 to
    /// <c>MaxSize</c>), the content type (a MIME type, or a prefix of at least two characters ending with
    /// <c>/</c>), expiry and metadata.
    /// </summary>
    /// <param name="options">The options.</param>
    /// <param name="maxExpiry">The store's maximum presign expiry.</param>
    /// <returns>
    /// <see langword="null"/>, <see cref="StorageErrorCodes.InvalidRequest"/> or
    /// <see cref="StorageErrorCodes.ExpiryTooLong"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    public static Error? ValidatePresignedPost(PresignedPostOptions options, TimeSpan maxExpiry)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.MinSize < 0 || options.MaxSize < 1 || options.MinSize > options.MaxSize)
        {
            return StorageErrors.InvalidRequest("MaxSize must be at least 1 and MinSize between 0 and MaxSize.");
        }

        string contentType = options.ContentType ?? string.Empty;
        Error? contentTypeError = contentType.EndsWith('/')
            ? ValidateHeaderValue(contentType, nameof(PresignedPostOptions.ContentType))
                ?? (contentType.Length < 2
                    ? StorageErrors.InvalidRequest("ContentType prefix must name a type, such as 'image/'.")
                    : null)
            : RequireContentType(contentType, nameof(PresignedPostOptions.ContentType));

        return ValidateExpiry(options.Expiry, maxExpiry) ?? contentTypeError ?? ValidateMetadata(options.Metadata);
    }

    /// <summary>Validates a multipart part number.</summary>
    /// <param name="partNumber">The part number.</param>
    /// <returns>
    /// <see langword="null"/>, or <see cref="StorageErrorCodes.InvalidRequest"/> outside 1 to
    /// <see cref="MaxPartNumber"/>.
    /// </returns>
    public static Error? ValidatePartNumber(int partNumber) =>
        partNumber is < 1 or > MaxPartNumber
            ? StorageErrors.InvalidRequest($"Part number must be between 1 and {MaxPartNumber}.")
            : null;

    /// <summary>
    /// Validates user metadata: keys of ASCII letters, digits, <c>-</c> and <c>_</c>, unique ignoring case; values
    /// of printable ASCII (space to <c>~</c>), possibly empty; keys and values together at most
    /// <see cref="MaxMetadataBytes"/> characters.
    /// </summary>
    /// <param name="metadata">The metadata, or <see langword="null"/> (valid).</param>
    /// <returns>
    /// <see langword="null"/>, or <see cref="StorageErrorCodes.InvalidRequest"/> naming the offending key.
    /// </returns>
    public static Error? ValidateMetadata(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null)
        {
            return null;
        }

        int totalBytes = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string key, string value) in metadata)
        {
            if (string.IsNullOrEmpty(key) || !key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            {
                return StorageErrors.InvalidRequest(
                    $"Metadata key '{key}' is invalid: use letters, digits, '-' and '_'.");
            }

            if (!seen.Add(key))
            {
                return StorageErrors.InvalidRequest($"Metadata key '{key}' appears twice, ignoring case.");
            }

            if (value is null || !IsPrintableAscii(value))
            {
                return StorageErrors.InvalidRequest($"Metadata value of '{key}' must be printable ASCII.");
            }

            totalBytes += key.Length + value.Length;
        }

        return totalBytes > MaxMetadataBytes
            ? StorageErrors.InvalidRequest($"Metadata keys and values exceed {MaxMetadataBytes} bytes.")
            : null;
    }

    /// <summary>
    /// Validates object tags: at most <see cref="MaxTags"/>; keys of 1 to 128 and values of 0 to 256 characters,
    /// without control characters.
    /// </summary>
    /// <param name="tags">The tags, or <see langword="null"/> (valid).</param>
    /// <returns><see langword="null"/>, or <see cref="StorageErrorCodes.InvalidRequest"/>.</returns>
    public static Error? ValidateTags(IReadOnlyDictionary<string, string>? tags)
    {
        if (tags is null)
        {
            return null;
        }

        if (tags.Count > MaxTags)
        {
            return StorageErrors.InvalidRequest($"An object can carry at most {MaxTags} tags.");
        }

        foreach ((string key, string value) in tags)
        {
            if (string.IsNullOrEmpty(key) || key.Length > MaxTagKeyLength || (value?.Length ?? 0) > MaxTagValueLength
                || value is null || key.Any(char.IsControl) || value.Any(char.IsControl))
            {
                return StorageErrors.InvalidRequest(
                    $"Tag '{key}' is invalid: keys are 1 to {MaxTagKeyLength} and values 0 to {MaxTagValueLength} characters, without control characters.");
            }
        }

        return null;
    }

    private static Error? ValidateTier(StorageTier tier) =>
        Enum.IsDefined(tier) ? null : StorageErrors.InvalidRequest($"Storage tier '{tier}' is not defined.");

    private static Error? RequireContentType(string? contentType, string name) =>
        string.IsNullOrWhiteSpace(contentType)
            ? StorageErrors.InvalidRequest($"{name} is required.")
            : ValidateContentType(contentType, name);

    private static Error? ValidateContentType(string? contentType, string name)
    {
        if (contentType is null)
        {
            return null;
        }

        int slash = contentType.IndexOf('/', StringComparison.Ordinal);
        return slash <= 0 || slash == contentType.Length - 1
            ? StorageErrors.InvalidRequest($"{name} '{contentType}' is not a MIME type such as 'application/pdf'.")
            : ValidateHeaderValue(contentType, name);
    }

    private static Error? ValidateHeaderValue(string? value, string name) =>
        value is null ? null
        : value.Length == 0 || value.Length > MaxHeaderValueLength || !IsPrintableAscii(value)
            ? StorageErrors.InvalidRequest($"{name} must be 1 to {MaxHeaderValueLength} printable ASCII characters.")
            : null;

    private static Error? ValidateChecksum(string? checksum, string name)
    {
        if (checksum is null)
        {
            return null;
        }

        Span<byte> buffer = stackalloc byte[Sha256Bytes + 3];
        return Convert.TryFromBase64String(checksum, buffer, out int written) && written == Sha256Bytes
            ? null
            : StorageErrors.InvalidRequest($"{name} must be the base64 encoding of a 32-byte SHA-256 hash.");
    }

    private static bool IsPrintableAscii(string value)
    {
        foreach (char c in value)
        {
            if (c is < ' ' or > '~')
            {
                return false;
            }
        }

        return true;
    }
}
