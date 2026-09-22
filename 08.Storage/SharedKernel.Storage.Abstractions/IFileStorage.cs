using SharedKernel.Primitives.Results;

namespace SharedKernel.Storage;

/// <summary>
/// One named object store — a bucket, optionally narrowed to a key prefix, on one storage provider — or a
/// tenant view of one. Uploads, downloads, lists, copies and deletes objects, and creates presigned requests
/// so clients can transfer bytes directly with the provider.
/// </summary>
/// <remarks>
/// <para>
/// <b>Resolving.</b> Every store is a keyed singleton under its registered name:
/// <c>[FromKeyedServices("invoices")] IFileStorage</c>. The unkeyed <see cref="IFileStorage"/> resolves only
/// when exactly one shared store is registered; with none or several, resolving it throws
/// <see cref="InvalidOperationException"/> naming the stores. For a store named in data use
/// <see cref="IFileStorageFactory"/>. A store registered as tenant-scoped is never an <see cref="IFileStorage"/>
/// directly: resolve <see cref="ITenantFileStorage"/> and call <see cref="ITenantFileStorage.ForTenant(string)"/>.
/// Instances are safe for concurrent use.
/// </para>
/// <para>
/// <b>Keys</b> are relative to the store, and to the tenant for a tenant view; the bucket, the store's key
/// prefix and the tenant prefix never appear in keys, results or error messages. Before any I/O every key is
/// checked by <see cref="StorageValidation.ValidateKey(string)"/>: not empty, no leading <c>/</c>, no <c>\</c>, no
/// empty, <c>.</c> or <c>..</c> segment, no control character, at most <see cref="StorageValidation.MaxKeyBytes"/>
/// UTF-8 bytes including the prefixes. A trailing <c>/</c> is allowed. A <see langword="null"/> or invalid key
/// returns <see cref="StorageErrorCodes.InvalidKey"/>; it does not throw.
/// </para>
/// <para>
/// <b>Failures</b> are <see cref="Result"/> values whose <c>Error.Code</c> is a <see cref="StorageErrorCodes"/>
/// constant; match on the code, never on the message. Besides the codes listed per member, every member that
/// reaches the provider can return <see cref="StorageErrorCodes.AccessDenied"/>,
/// <see cref="StorageErrorCodes.Unavailable"/> (unreachable, throttled, timed out or failing after the provider
/// client's own retries — retry later) and <see cref="StorageErrorCodes.ProviderError"/> (any other rejection;
/// details are logged, never returned). A feature the store's provider lacks returns
/// <see cref="StorageErrorCodes.NotSupported"/> before the request is sent. Only three things throw:
/// <see cref="ArgumentNullException"/> for a <see langword="null"/> argument other than a key, a prefix or
/// optional options, <see cref="OperationCanceledException"/> when the caller's token is cancelled, and
/// <see cref="StorageException"/> from <see cref="ListAsync"/>, which cannot return a <see cref="Result"/>.
/// </para>
/// <para>
/// <b>Streams</b> are never buffered whole: uploads read the caller's stream as they send it, downloads return
/// the provider's response stream. Object keys are never written to logs, spans or metrics.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class InvoiceArchive([FromKeyedServices("invoices")] IFileStorage invoices)
/// {
///     public Task&lt;Result&lt;FileReference&gt;&gt; SaveAsync(Guid id, Stream pdf, CancellationToken ct) =>
///         invoices.UploadAsync($"{id}.pdf", pdf, new FileUploadOptions
///         {
///             ContentType = "application/pdf",
///             Condition = WriteCondition.IfNotExists,   // never overwrite an issued invoice
///         }, ct);
///
///     public async Task&lt;Result&lt;byte[]&gt;&gt; ReadAsync(string key, CancellationToken ct)
///     {
///         Result&lt;FileDownload&gt; opened = await invoices.DownloadAsync(key, cancellationToken: ct);
///         if (opened.IsFailure)
///         {
///             return opened.Error;                       // e.g. StorageErrorCodes.NotFound
///         }
///
///         await using FileDownload download = opened.Value;
///         using var buffer = new MemoryStream();
///         await download.Content.CopyToAsync(buffer, ct);
///         return buffer.ToArray();
///     }
/// }
/// </code>
/// </example>
public interface IFileStorage
{
    /// <summary>Gets the name this store was registered with, in its registered spelling.</summary>
    /// <value>The store name; also <see cref="FileReference.Store"/> of every reference this store returns.</value>
    string StoreName { get; }

    /// <summary>Gets the tenant this view is bound to.</summary>
    /// <value>
    /// The tenant id passed to <see cref="ITenantFileStorage.ForTenant(string)"/>, or <see langword="null"/> for a
    /// store shared by all tenants.
    /// </value>
    string? TenantId { get; }

    /// <summary>
    /// Uploads <paramref name="content"/> to <paramref name="key"/>, replacing an existing object unless
    /// <see cref="FileUploadOptions.Condition"/> says otherwise.
    /// </summary>
    /// <param name="key">The object key, relative to the store (and tenant).</param>
    /// <param name="content">
    /// The payload, read from its current position to its end. Owned by the caller: never disposed, never
    /// rewound. May be non-seekable (a pipe, a network stream, a request body); pass
    /// <see cref="FileUploadOptions.ContentLength"/> when it cannot report its length.
    /// </param>
    /// <param name="options">
    /// Content type, stored headers, metadata, tags, tier, write condition, length and checksum;
    /// <see langword="null"/> uses the defaults (<c>application/octet-stream</c>, the store's tier, no condition).
    /// </param>
    /// <param name="cancellationToken">Cancels the upload; a cancelled multipart upload is aborted.</param>
    /// <returns>
    /// The reference of the stored object (with its new <see cref="FileReference.ETag"/>), or a failure:
    /// <see cref="StorageErrorCodes.InvalidKey"/>; <see cref="StorageErrorCodes.InvalidRequest"/> for invalid
    /// options, or a <see cref="FileUploadOptions.ChecksumSha256"/> on a non-seekable stream without
    /// <see cref="FileUploadOptions.ContentLength"/>; <see cref="StorageErrorCodes.AlreadyExists"/> or
    /// <see cref="StorageErrorCodes.PreconditionFailed"/> when the condition fails;
    /// <see cref="StorageErrorCodes.ChecksumMismatch"/> when the received bytes do not match the checksum;
    /// <see cref="StorageErrorCodes.NotSupported"/> when the provider lacks conditional writes, SHA-256
    /// checksums, tags or the store's encryption.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// On S3-family providers, content with a known <see cref="FileUploadOptions.ContentLength"/> up to the
    /// store's part size, and every upload with a <see cref="FileUploadOptions.ChecksumSha256"/>, is sent as one
    /// request; anything else goes through a multipart upload that holds at most one part in memory.
    /// </remarks>
    Task<Result<FileReference>> UploadAsync(
        string key,
        Stream content,
        FileUploadOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>Opens <paramref name="key"/> for reading, optionally only a byte range of it.</summary>
    /// <param name="key">The object key, relative to the store (and tenant).</param>
    /// <param name="options">
    /// Byte range, version and <c>If-Match</c> ETag; <see langword="null"/> reads the current version whole.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancels opening the object. Reading <see cref="FileDownload.Content"/> takes its own token.
    /// </param>
    /// <returns>
    /// A <see cref="FileDownload"/> the caller must dispose, or a failure: <see cref="StorageErrorCodes.NotFound"/>
    /// (no such object or version), <see cref="StorageErrorCodes.PreconditionFailed"/> (the ETag no longer
    /// matches <see cref="FileDownloadOptions.IfMatch"/>), <see cref="StorageErrorCodes.InvalidRange"/> (the range
    /// starts at or beyond the end of the object), <see cref="StorageErrorCodes.InvalidKey"/>.
    /// </returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// Failures while reading <see cref="FileDownload.Content"/> — a dropped connection, for example — surface as
    /// exceptions from the stream, not as <see cref="Result"/> values.
    /// </remarks>
    Task<Result<FileDownload>> DownloadAsync(
        string key,
        FileDownloadOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>Reads the properties and user metadata of <paramref name="key"/> without its content.</summary>
    /// <param name="key">The object key, relative to the store (and tenant).</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// The properties of the current version, or <see cref="StorageErrorCodes.NotFound"/> /
    /// <see cref="StorageErrorCodes.InvalidKey"/>.
    /// </returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<Result<FileProperties>> GetPropertiesAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Reports whether <paramref name="key"/> exists, without reading its content.</summary>
    /// <param name="key">The object key, relative to the store (and tenant).</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// <see langword="true"/> or <see langword="false"/>; a missing object is not a failure. Without list
    /// permission on the bucket, S3-family providers answer a missing key with 403, which is returned as
    /// <see cref="StorageErrorCodes.AccessDenied"/> rather than <see langword="false"/>.
    /// </returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// The answer can be stale by the time you act on it; to create an object only if it is missing, upload with
    /// <see cref="WriteCondition.IfNotExists"/> instead. S3-family providers answer this metadata request without an
    /// error code, so a store whose bucket does not exist also answers <see langword="false"/> (and
    /// <see cref="GetPropertiesAsync"/> <see cref="StorageErrorCodes.NotFound"/>); the readiness probe
    /// (<see cref="IFileStorageHealthProbe"/>) is what reports a missing bucket.
    /// </remarks>
    Task<Result<bool>> ExistsAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes <paramref name="key"/>. Deleting a missing object succeeds, unless an ETag condition is set.
    /// </summary>
    /// <param name="key">The object key, relative to the store (and tenant).</param>
    /// <param name="options">
    /// An <c>If-Match</c> ETag or a specific version to delete; <see langword="null"/> deletes the current object
    /// unconditionally.
    /// </param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// Success, or <see cref="StorageErrorCodes.PreconditionFailed"/> when <see cref="FileDeleteOptions.IfMatch"/>
    /// no longer matches or the object is gone, <see cref="StorageErrorCodes.NotSupported"/> when the provider
    /// lacks conditional deletes, <see cref="StorageErrorCodes.InvalidKey"/>.
    /// </returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<Result> DeleteAsync(string key, FileDeleteOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes several objects in as few provider calls as possible (1,000 keys per request on S3-family
    /// providers), reporting the outcome per key.
    /// </summary>
    /// <param name="keys">
    /// The keys, relative to the store (and tenant). Duplicates are removed (ordinal comparison). Every key is
    /// validated before the first request.
    /// </param>
    /// <param name="cancellationToken">Cancels the remaining requests.</param>
    /// <returns>
    /// <see cref="StorageErrorCodes.InvalidKey"/> for the first invalid key, with nothing deleted. Otherwise
    /// success, with every distinct key in exactly one of <see cref="BatchDeleteResult.Deleted"/> or
    /// <see cref="BatchDeleteResult.Failed"/> — including the keys of a request the provider rejected as a whole,
    /// and of requests not sent after the provider became unavailable. Missing keys count as deleted. An empty
    /// input succeeds with nothing deleted.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="keys"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// To delete everything under a prefix — for example a tenant's data on its view — list it with
    /// <see cref="ListAsync"/> and pass the keys in chunks.
    /// </remarks>
    Task<Result<BatchDeleteResult>> DeleteManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default);

    /// <summary>
    /// Copies <paramref name="sourceKey"/> to <paramref name="destinationKey"/> within this store (and tenant).
    /// Equivalent to <see cref="CopyToAsync"/> with this store as the destination.
    /// </summary>
    /// <param name="sourceKey">The object to copy; left unchanged.</param>
    /// <param name="destinationKey">
    /// The key to write; an existing object is replaced unless a condition says otherwise.
    /// </param>
    /// <param name="options">
    /// Source <c>If-Match</c>, destination condition, replacement content type and metadata, and tier;
    /// <see langword="null"/> copies content, headers and metadata as they are.
    /// </param>
    /// <param name="cancellationToken">Cancels the copy.</param>
    /// <returns>The destination reference, or a failure as for <see cref="CopyToAsync"/>.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<Result<FileReference>> CopyAsync(
        string sourceKey,
        string destinationKey,
        FileCopyOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Copies <paramref name="sourceKey"/> into another store or tenant view — for example from a quarantine
    /// store to its final one, or from a shared store into a tenant's view.
    /// </summary>
    /// <param name="sourceKey">The object to copy, relative to this store (and tenant); left unchanged.</param>
    /// <param name="destination">
    /// The destination: any store or tenant view resolved from the container or
    /// <see cref="IFileStorageFactory"/>, including this one.
    /// </param>
    /// <param name="destinationKey">The key to write, relative to <paramref name="destination"/>.</param>
    /// <param name="options">
    /// Source <c>If-Match</c>, destination condition, replacement content type and metadata, and tier;
    /// <see langword="null"/> copies content, headers and metadata as they are.
    /// </param>
    /// <param name="cancellationToken">Cancels the copy.</param>
    /// <returns>
    /// The destination reference (store, tenant and key of <paramref name="destination"/>), or a failure:
    /// <see cref="StorageErrorCodes.NotFound"/> for a missing source;
    /// <see cref="StorageErrorCodes.PreconditionFailed"/> when the source no longer matches
    /// <see cref="FileCopyOptions.SourceIfMatch"/> or the destination fails an <c>If-Match</c> condition;
    /// <see cref="StorageErrorCodes.AlreadyExists"/> when the destination fails
    /// <see cref="WriteCondition.IfNotExists"/>; <see cref="StorageErrorCodes.NotSupported"/> when the destination's
    /// provider lacks conditional writes or its store's encryption; <see cref="StorageErrorCodes.InvalidKey"/> /
    /// <see cref="StorageErrorCodes.InvalidRequest"/>; <see cref="StorageErrorCodes.ProviderError"/> for a server-side
    /// copy the provider refuses, such as one over 5 GiB on S3.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// <para>
    /// The copy runs on the provider (S3 <c>CopyObject</c>, no bytes through this process) when both stores share
    /// a provider connection and <see cref="FileCopyOptions.Condition"/> is <see langword="null"/>. Otherwise the
    /// content is streamed through this process without being buffered and written with a normal upload, which
    /// enforces the condition atomically — several S3-compatible services ignore conditions on a server-side copy.
    /// </para>
    /// <para>
    /// <c>Content-Type</c>, <c>Cache-Control</c>, <c>Content-Disposition</c>, <c>Content-Encoding</c> and user
    /// metadata are carried over unless replaced. The destination store's encryption applies to the copy.
    /// </para>
    /// </remarks>
    Task<Result<FileReference>> CopyToAsync(
        string sourceKey,
        IFileStorage destination,
        string destinationKey,
        FileCopyOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams every object whose key starts with <paramref name="prefix"/>, in key order, including objects in
    /// sub-folders, reading one page from the provider at a time.
    /// </summary>
    /// <param name="prefix">
    /// The key prefix, relative to the store (and tenant); empty lists the whole store (or the tenant's objects).
    /// For a folder, end it with <c>/</c>. Must be empty or a valid key.
    /// </param>
    /// <param name="cancellationToken">
    /// Stops paging; also honoured when passed through <c>WithCancellation</c>.
    /// </param>
    /// <returns>The objects, with keys relative to the store (and tenant).</returns>
    /// <exception cref="StorageException">
    /// Thrown by this call itself when <paramref name="prefix"/> is <see langword="null"/> or invalid
    /// (<see cref="StorageErrorCodes.InvalidKey"/>), and during enumeration when a page cannot be read; its
    /// <see cref="StorageException.Error"/> carries the storage error.
    /// </exception>
    /// <exception cref="OperationCanceledException">The token was cancelled during enumeration.</exception>
    /// <remarks>
    /// Use <see cref="ListPageAsync"/> for paging in an API, for folder listings, or to receive failures as
    /// <see cref="Result"/> values.
    /// </remarks>
    IAsyncEnumerable<FileListItem> ListAsync(string prefix = "", CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns one page of objects and, for a non-recursive listing, the folders directly under the prefix.
    /// </summary>
    /// <param name="request">Prefix, recursion, page size and the continuation token of the previous page.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// The page — call again with its <see cref="FileListPage.ContinuationToken"/> while
    /// <see cref="FileListPage.HasMore"/> — or <see cref="StorageErrorCodes.InvalidKey"/> for an invalid prefix,
    /// <see cref="StorageErrorCodes.InvalidRequest"/> for a page size outside 1 to
    /// <see cref="FileListRequest.MaxPageSize"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<Result<FileListPage>> ListPageAsync(FileListRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a URL a client can <c>GET</c> to download <paramref name="key"/> directly from the provider.
    /// </summary>
    /// <param name="key">The object key, relative to the store (and tenant). Its existence is not checked.</param>
    /// <param name="options">
    /// Expiry (at most the store's maximum presign expiry), version, and the <c>Content-Type</c> and
    /// <c>Content-Disposition</c> the response should carry.
    /// </param>
    /// <param name="cancellationToken">Cancels credential resolution.</param>
    /// <returns>
    /// A <c>GET</c> <see cref="PresignedRequest"/> with no required headers, or a failure:
    /// <see cref="StorageErrorCodes.ExpiryTooLong"/> (expiry not positive or above the store's maximum),
    /// <see cref="StorageErrorCodes.InvalidRequest"/> (invalid header value),
    /// <see cref="StorageErrorCodes.InvalidKey"/>, <see cref="StorageErrorCodes.Unavailable"/> (credentials could not
    /// be resolved).
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// Signing happens locally; the only I/O is resolving credentials. Anyone holding the URL can download the
    /// object until it expires — keep expiries short and never persist the URL; persist a
    /// <see cref="FileReference"/> and sign again.
    /// </remarks>
    Task<Result<PresignedRequest>> CreateDownloadUrlAsync(
        string key,
        PresignedDownloadOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a URL a client can <c>PUT</c> one file to. The client must send every header in
    /// <see cref="PresignedRequest.Headers"/> with exactly those values; the signature covers them.
    /// </summary>
    /// <param name="key">The object key the client will write, relative to the store (and tenant).</param>
    /// <param name="options">
    /// Expiry (at most the store's maximum presign expiry), the content type the client must send, and an
    /// optional create-only condition, checksum and metadata.
    /// </param>
    /// <param name="cancellationToken">Cancels credential resolution.</param>
    /// <returns>
    /// A <c>PUT</c> <see cref="PresignedRequest"/> whose headers include <c>Content-Type</c> and, as applicable,
    /// <c>If-None-Match</c>, <c>x-amz-checksum-sha256</c>, <c>x-amz-meta-*</c>, the store's encryption and storage
    /// class headers; or a failure: <see cref="StorageErrorCodes.ExpiryTooLong"/>,
    /// <see cref="StorageErrorCodes.InvalidRequest"/> (missing or invalid content type, checksum or metadata),
    /// <see cref="StorageErrorCodes.NotSupported"/> (the provider lacks create-only writes, SHA-256 checksums or
    /// the store's encryption), <see cref="StorageErrorCodes.InvalidKey"/>,
    /// <see cref="StorageErrorCodes.Unavailable"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// A presigned <c>PUT</c> cannot limit the size of the upload and overwrites an existing object unless
    /// <see cref="PresignedUploadOptions.CreateOnly"/> is set. For an untrusted client such as a browser, use
    /// <see cref="CreateUploadFormAsync"/>, which enforces a size range and content type.
    /// </remarks>
    Task<Result<PresignedRequest>> CreateUploadUrlAsync(
        string key,
        PresignedUploadOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an HTML-form (<c>POST</c>) upload a browser can submit directly to the provider, limited to a
    /// size range and a content type. The provider rejects anything outside the signed policy before storing it.
    /// </summary>
    /// <param name="key">The object key the client will write, relative to the store (and tenant).</param>
    /// <param name="options">
    /// Expiry (at most the store's maximum presign expiry), the maximum and minimum size, the allowed content type
    /// and optional metadata.
    /// </param>
    /// <param name="cancellationToken">Cancels credential resolution.</param>
    /// <returns>
    /// The form (see <see cref="PresignedPost"/> for how to submit it), or a failure:
    /// <see cref="StorageErrorCodes.ExpiryTooLong"/>, <see cref="StorageErrorCodes.InvalidRequest"/> (invalid
    /// sizes, content type or metadata), <see cref="StorageErrorCodes.NotSupported"/> (the provider lacks
    /// presigned POST or the store's encryption), <see cref="StorageErrorCodes.InvalidKey"/>,
    /// <see cref="StorageErrorCodes.Unavailable"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>The form overwrites an existing object at <paramref name="key"/>; use a new key per upload.</remarks>
    /// <example>
    /// <code>
    /// Result&lt;PresignedPost&gt; form = await uploads.CreateUploadFormAsync($"avatars/{userId}", new()
    /// {
    ///     Expiry = TimeSpan.FromMinutes(10),
    ///     MaxSize = 5 * 1024 * 1024,
    ///     ContentType = "image/",        // any image type; the browser sends its own in a Content-Type field
    /// }, ct);
    /// // Return form.Value.Url and form.Value.Fields to the browser.
    /// </code>
    /// </example>
    Task<Result<PresignedPost>> CreateUploadFormAsync(
        string key,
        PresignedPostOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a multipart upload that a client completes with presigned part URLs — for files too large for one
    /// request (up to 10,000 parts).
    /// </summary>
    /// <param name="key">The object key the upload will create, relative to the store (and tenant).</param>
    /// <param name="options">
    /// Content type, stored headers, metadata, tags and tier of the resulting object; <see langword="null"/> uses
    /// the defaults.
    /// </param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// The <see cref="MultipartUpload"/> to pass to the other multipart members, or a failure:
    /// <see cref="StorageErrorCodes.InvalidKey"/>, <see cref="StorageErrorCodes.InvalidRequest"/> (invalid options),
    /// <see cref="StorageErrorCodes.NotSupported"/> (the provider lacks tags or the store's encryption).
    /// </returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    /// <remarks>
    /// <para>
    /// The flow: start the upload; for each part call <see cref="CreateUploadPartUrlAsync"/> and let the client
    /// <c>PUT</c> the part and return the response's <c>ETag</c> header; then call
    /// <see cref="CompleteMultipartUploadAsync"/> with every part, or <see cref="AbortMultipartUploadAsync"/>.
    /// Every part but the last must be at least 5 MiB.
    /// </para>
    /// <para>
    /// Parts of an upload that is never completed or aborted keep occupying (and costing) storage; configure a
    /// bucket lifecycle rule that aborts incomplete multipart uploads.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// MultipartUpload upload = (await store.StartMultipartUploadAsync(key, new MultipartUploadOptions
    /// {
    ///     ContentType = "video/mp4",
    /// }, ct)).Value;
    ///
    /// // For part numbers 1..n: the client PUTs the part to the URL and reports back the ETag response header.
    /// PresignedRequest partUrl =
    ///     (await store.CreateUploadPartUrlAsync(upload, 1, TimeSpan.FromMinutes(30), ct)).Value;
    ///
    /// // With every UploadedPart(partNumber, eTag) the client reported:
    /// Result&lt;FileReference&gt; stored =
    ///     await store.CompleteMultipartUploadAsync(upload, parts, cancellationToken: ct);
    /// </code>
    /// </example>
    Task<Result<MultipartUpload>> StartMultipartUploadAsync(
        string key,
        MultipartUploadOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a URL a client can <c>PUT</c> one part of a multipart upload to. The <c>ETag</c> header of the
    /// provider's response identifies the uploaded part; the client must report it back for
    /// <see cref="CompleteMultipartUploadAsync"/>.
    /// </summary>
    /// <param name="upload">
    /// The upload returned by <see cref="StartMultipartUploadAsync"/> on this store (and tenant).
    /// </param>
    /// <param name="partNumber">
    /// The part number, 1 to <see cref="StorageValidation.MaxPartNumber"/>; parts are assembled in part-number
    /// order. Uploading the same number again replaces that part. Every part but the last must be at least 5 MiB.
    /// </param>
    /// <param name="expiry">
    /// How long the URL stays valid; positive and at most the store's maximum presign expiry.
    /// </param>
    /// <param name="cancellationToken">Cancels credential resolution.</param>
    /// <returns>
    /// A <c>PUT</c> <see cref="PresignedRequest"/> with no required headers, or a failure:
    /// <see cref="StorageErrorCodes.InvalidRequest"/> (part number out of range, empty upload id),
    /// <see cref="StorageErrorCodes.ExpiryTooLong"/>, <see cref="StorageErrorCodes.InvalidKey"/>,
    /// <see cref="StorageErrorCodes.Unavailable"/>. Whether the upload still exists is not checked.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="upload"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<Result<PresignedRequest>> CreateUploadPartUrlAsync(
        MultipartUpload upload,
        int partNumber,
        TimeSpan expiry,
        CancellationToken cancellationToken = default);

    /// <summary>Assembles the uploaded parts into the final object, which then becomes visible at its key.</summary>
    /// <param name="upload">
    /// The upload returned by <see cref="StartMultipartUploadAsync"/> on this store (and tenant).
    /// </param>
    /// <param name="parts">
    /// Every uploaded part with the ETag its upload returned, in any order; each part number at most once.
    /// </param>
    /// <param name="condition">
    /// An optional <see cref="WriteCondition.IfNotExists"/> or <see cref="WriteCondition.IfMatch(string)"/>
    /// condition on the final object, checked when the upload completes.
    /// </param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// The stored object's reference, or a failure: <see cref="StorageErrorCodes.InvalidRequest"/> (no parts, a
    /// duplicate or out-of-range part number, an empty ETag, or parts the provider rejects — a missing part, a
    /// wrong ETag, a part other than the last under 5 MiB); <see cref="StorageErrorCodes.NotFound"/> (the upload
    /// was completed, aborted or never existed); <see cref="StorageErrorCodes.AlreadyExists"/> /
    /// <see cref="StorageErrorCodes.PreconditionFailed"/> (the condition failed);
    /// <see cref="StorageErrorCodes.NotSupported"/> (the provider lacks conditional writes);
    /// <see cref="StorageErrorCodes.InvalidKey"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="upload"/> or <paramref name="parts"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<Result<FileReference>> CompleteMultipartUploadAsync(
        MultipartUpload upload,
        IReadOnlyCollection<UploadedPart> parts,
        WriteCondition? condition = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Discards a multipart upload and every part uploaded to it. Aborting an upload that is unknown, already
    /// completed or already aborted succeeds.
    /// </summary>
    /// <param name="upload">
    /// The upload returned by <see cref="StartMultipartUploadAsync"/> on this store (and tenant).
    /// </param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>
    /// Success, or <see cref="StorageErrorCodes.InvalidKey"/> / <see cref="StorageErrorCodes.InvalidRequest"/>
    /// (empty upload id), or a provider failure.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="upload"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<Result> AbortMultipartUploadAsync(MultipartUpload upload, CancellationToken cancellationToken = default);
}
