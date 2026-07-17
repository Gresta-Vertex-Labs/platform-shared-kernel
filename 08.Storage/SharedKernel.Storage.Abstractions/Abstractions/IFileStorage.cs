using SharedKernel.Primitives.Results;
using SharedKernel.Storage.Abstractions.Models;

namespace SharedKernel.Storage.Abstractions.Abstractions;

/// <summary>
/// Provider-agnostic contract for uploading, downloading, deleting, copying, batch-deleting, and
/// stream-listing binary blobs (documents, images, exports, attachments) in object storage, plus a
/// lightweight connectivity probe.
/// </summary>
/// <remarks>
/// <para>
/// Stream-first, always: payloads flow as <see cref="Stream"/> from caller to provider and back —
/// large objects never fully materialize in managed memory.
/// </para>
/// <para>
/// Expected failures (not-found, access-denied, validation, provider-rejection) are returned as
/// <see cref="Result"/>/<see cref="Result{T}"/> failures via <see cref="Errors.StorageErrors"/> —
/// never thrown exceptions. Only genuinely exceptional transport faults (socket reset, DNS failure)
/// propagate as thrown exceptions.
/// </para>
/// <para>
/// <see cref="ListAsync"/> is a deliberate, documented exception to that Result-first convention: it
/// returns <see cref="IAsyncEnumerable{T}"/> of <see cref="FileMetadata"/> directly for constant-memory
/// streaming enumeration, mirroring <c>06.Persistence</c>'s <c>IReadRepository.StreamAsync</c>
/// precedent. A provider fault mid-enumeration propagates as a thrown exception from
/// <c>MoveNextAsync</c>, not an <see cref="Primitives.Errors.Error"/> value.
/// </para>
/// </remarks>
public interface IFileStorage
{
    /// <summary>
    /// Uploads <paramref name="request"/>'s <see cref="FileUploadRequest.Content"/> stream to the
    /// object identified by <see cref="FileUploadRequest.Bucket"/>/<see cref="FileUploadRequest.Key"/>.
    /// </summary>
    /// <param name="request">
    /// The upload request. <see cref="FileUploadRequest.Content"/> is caller-owned — this method
    /// never disposes it and reads from the stream's current position.
    /// </param>
    /// <param name="cancellationToken">Token used to cancel the upload.</param>
    /// <returns>
    /// The <see cref="FileReference"/> handle to the stored object on success, or a
    /// <see cref="Errors.StorageErrors"/> failure.
    /// </returns>
    Task<Result<FileReference>> UploadAsync(FileUploadRequest request, CancellationToken cancellationToken);

    /// <summary>Downloads the object identified by <paramref name="bucket"/>/<paramref name="key"/>.</summary>
    /// <param name="bucket">The bucket name.</param>
    /// <param name="key">The object key.</param>
    /// <param name="cancellationToken">Token used to cancel the download.</param>
    /// <returns>
    /// A <see cref="FileDownload"/> wrapping the provider's network stream on success — the caller
    /// must dispose it via <see cref="FileDownload.DisposeAsync"/> — or a <see cref="Errors.StorageErrors"/>
    /// failure.
    /// </returns>
    Task<Result<FileDownload>> DownloadAsync(string bucket, string key, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the object identified by <paramref name="bucket"/>/<paramref name="key"/>. Idempotent —
    /// deleting an absent key succeeds.
    /// </summary>
    /// <param name="bucket">The bucket name.</param>
    /// <param name="key">The object key.</param>
    /// <param name="cancellationToken">Token used to cancel the delete.</param>
    Task<Result> DeleteAsync(string bucket, string key, CancellationToken cancellationToken);

    /// <summary>
    /// Checks whether the object identified by <paramref name="bucket"/>/<paramref name="key"/>
    /// exists, via a HEAD-style metadata probe — never downloads the object body.
    /// </summary>
    /// <param name="bucket">The bucket name.</param>
    /// <param name="key">The object key.</param>
    /// <param name="cancellationToken">Token used to cancel the probe.</param>
    Task<Result<bool>> ExistsAsync(string bucket, string key, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves metadata for the object identified by <paramref name="bucket"/>/<paramref name="key"/>
    /// without downloading its body.
    /// </summary>
    /// <param name="bucket">The bucket name.</param>
    /// <param name="key">The object key.</param>
    /// <param name="cancellationToken">Token used to cancel the probe.</param>
    Task<Result<FileMetadata>> GetMetadataAsync(string bucket, string key, CancellationToken cancellationToken);

    /// <summary>
    /// Performs a server-side copy of an object from <paramref name="sourceBucket"/>/<paramref name="sourceKey"/>
    /// to <paramref name="destinationBucket"/>/<paramref name="destinationKey"/>. The provider issues
    /// a native copy call so object bytes never flow through application memory. The source object is
    /// left untouched (copy, not move).
    /// </summary>
    /// <param name="sourceBucket">The bucket the source object is stored in.</param>
    /// <param name="sourceKey">The source object's key.</param>
    /// <param name="destinationBucket">The bucket the copy is written to.</param>
    /// <param name="destinationKey">The destination object's key.</param>
    /// <param name="cancellationToken">Token used to cancel the copy.</param>
    /// <returns>The destination <see cref="FileReference"/> (new ETag/VersionId) on success.</returns>
    Task<Result<FileReference>> CopyAsync(
        string sourceBucket,
        string sourceKey,
        string destinationBucket,
        string destinationKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// Deletes multiple objects from <paramref name="bucket"/> in a single batch operation, backed by
    /// the provider's native multi-object delete where available.
    /// </summary>
    /// <param name="bucket">The bucket the keys are deleted from.</param>
    /// <param name="keys">The object keys to delete.</param>
    /// <param name="cancellationToken">Token used to cancel the batch.</param>
    /// <returns>
    /// A <see cref="Result{T}"/> that fails only when the batch call itself cannot be attempted
    /// (empty/null keys, transport fault). On success, the inner <see cref="FileDeleteOutcome"/> list
    /// carries each key's individual success/failure, so a partial batch failure never masquerades as
    /// one opaque error. Providers chunk internally at their own native per-request key limit,
    /// invisible to the caller.
    /// </returns>
    Task<Result<IReadOnlyList<FileDeleteOutcome>>> DeleteManyAsync(
        string bucket,
        IReadOnlyCollection<string> keys,
        CancellationToken cancellationToken);

    /// <summary>
    /// Streams <see cref="FileMetadata"/> for every object under <paramref name="prefix"/> in
    /// <paramref name="bucket"/>, in constant memory.
    /// </summary>
    /// <param name="bucket">The bucket to list.</param>
    /// <param name="prefix">The key prefix to filter by. An empty string lists the whole bucket.</param>
    /// <param name="cancellationToken">
    /// Token used to stop paging mid-enumeration. Implementations apply
    /// <see cref="System.Runtime.CompilerServices.EnumeratorCancellationAttribute"/> to this parameter
    /// on their concrete async-iterator method — the attribute has no effect on an interface
    /// declaration, so it is intentionally omitted here.
    /// </param>
    /// <remarks>
    /// Deliberate, documented deviation from this interface's Result-first convention: this method is
    /// not <see cref="Result"/>-wrapped. A provider fault mid-enumeration propagates as a thrown
    /// exception from <c>MoveNextAsync</c>.
    /// </remarks>
    IAsyncEnumerable<FileMetadata> ListAsync(string bucket, string prefix, CancellationToken cancellationToken);

    /// <summary>
    /// Performs a lightweight connectivity/reachability probe scoped to <paramref name="bucket"/>.
    /// Never requires a specific object key to exist and never touches object bytes.
    /// </summary>
    /// <param name="bucket">The bucket to probe.</param>
    /// <param name="cancellationToken">Token used to cancel the probe.</param>
    Task<Result> CheckHealthAsync(string bucket, CancellationToken cancellationToken);
}
