namespace SharedKernel.Storage;

/// <summary>
/// Options for <see cref="IFileStorage.CreateUploadFormAsync"/>: the policy the provider enforces on a browser
/// upload. Invalid values fail with <see cref="StorageErrorCodes.InvalidRequest"/> before any I/O.
/// </summary>
/// <example>
/// <code>
/// new PresignedPostOptions
/// {
///     Expiry = TimeSpan.FromMinutes(10),
///     MaxSize = 10 * 1024 * 1024,   // 10 MiB
///     ContentType = "image/",        // image/png, image/jpeg, ...; "application/pdf" for exactly one type
///     Metadata = new Dictionary&lt;string, string&gt; { ["uploaded-by"] = userId },
/// }
/// </code>
/// </example>
public sealed record PresignedPostOptions
{
    /// <summary>
    /// Gets how long the form stays valid. Required; positive and at most the store's maximum presign expiry
    /// (1 hour unless configured), otherwise <see cref="StorageErrorCodes.ExpiryTooLong"/>.
    /// </summary>
    public required TimeSpan Expiry { get; init; }

    /// <summary>
    /// Gets the largest file accepted, in bytes; at least 1 and at least <see cref="MinSize"/>. Required: an
    /// unbounded browser upload is never offered.
    /// </summary>
    public required long MaxSize { get; init; }

    /// <summary>
    /// Gets the smallest file accepted, in bytes; 0 to <see cref="MaxSize"/>. Defaults to 1, rejecting empty files.
    /// </summary>
    public long MinSize { get; init; } = 1;

    /// <summary>
    /// Gets the allowed content type. Required. Either an exact MIME type (<c>application/pdf</c>), which is added
    /// to <see cref="PresignedPost.Fields"/>, or a prefix ending with <c>/</c> (<c>image/</c>), in which case the
    /// browser must send its file's type as the form's <c>Content-Type</c> field and the provider checks it
    /// starts with the prefix. The accepted value is stored as the object's content type.
    /// </summary>
    public required string ContentType { get; init; }

    /// <summary>
    /// Gets user metadata stored with the object, added to <see cref="PresignedPost.Fields"/> and pinned by the
    /// policy so the browser cannot change it; same rules as <see cref="FileUploadOptions.Metadata"/>.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}
