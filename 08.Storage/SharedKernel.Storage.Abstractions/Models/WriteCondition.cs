namespace SharedKernel.Storage;

/// <summary>
/// A condition a write must meet, checked atomically by the provider — no other writer can slip in between the
/// check and the write. Used by <see cref="FileUploadOptions.Condition"/>, <see cref="FileCopyOptions.Condition"/>
/// and <see cref="IFileStorage.CompleteMultipartUploadAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// Use <see cref="IfNotExists"/> to create an object without overwriting one written concurrently, and
/// <see cref="IfMatch(string)"/> for optimistic concurrency: the write succeeds only while the object still has
/// the ETag you read.
/// </para>
/// <para>
/// Requires conditional-write support: a provider without it (Huawei Cloud OBS, whose S3 API silently ignores the
/// headers) fails the write with <see cref="StorageErrorCodes.NotSupported"/> rather than overwriting unchecked.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// FileProperties current = (await store.GetPropertiesAsync(key, ct)).Value;
/// Result&lt;FileReference&gt; saved = await store.UploadAsync(key, updated,
///     new FileUploadOptions { Condition = WriteCondition.IfMatch(current.ETag!) }, ct);
/// if (saved.IsFailure &amp;&amp; saved.Error.Code == StorageErrorCodes.PreconditionFailed)
/// {
///     // Someone else changed it since it was read: reload and retry, or report a conflict.
/// }
/// </code>
/// </example>
public sealed record WriteCondition
{
    private WriteCondition(bool mustNotExist, string? eTag)
    {
        MustNotExist = mustNotExist;
        ETag = eTag;
    }

    /// <summary>
    /// Gets the condition that the object must not exist yet (<c>If-None-Match: *</c>); a failed check is
    /// <see cref="StorageErrorCodes.AlreadyExists"/>.
    /// </summary>
    public static WriteCondition IfNotExists { get; } = new(mustNotExist: true, eTag: null);

    /// <summary>
    /// Gets a value indicating whether the object must not exist (<see langword="true"/> only for
    /// <see cref="IfNotExists"/>).
    /// </summary>
    public bool MustNotExist { get; }

    /// <summary>
    /// Gets the ETag the object must currently have, as passed to <see cref="IfMatch(string)"/>, or
    /// <see langword="null"/>.
    /// </summary>
    public string? ETag { get; }

    /// <summary>
    /// Returns a condition that the object must currently have <paramref name="eTag"/> (<c>If-Match</c>); a failed
    /// check — the object changed or is gone — is <see cref="StorageErrorCodes.PreconditionFailed"/>.
    /// </summary>
    /// <param name="eTag">
    /// The ETag read earlier (<see cref="FileProperties.ETag"/>, <see cref="FileReference.ETag"/>), with or without
    /// quotes.
    /// </param>
    /// <returns>The condition.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="eTag"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="eTag"/> is empty or whitespace.</exception>
    public static WriteCondition IfMatch(string eTag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eTag);
        return new WriteCondition(mustNotExist: false, eTag);
    }
}
