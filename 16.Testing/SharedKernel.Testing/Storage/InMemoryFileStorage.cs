using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using SharedKernel.Primitives.Results;
using SharedKernel.Storage.Abstractions.Abstractions;
using SharedKernel.Storage.Abstractions.Errors;
using SharedKernel.Storage.Abstractions.Models;

namespace SharedKernel.Testing.Storage;

/// <summary>
/// In-memory test double for <see cref="IFileStorage"/>. Simulates behavioral correctness (what was
/// uploaded/deleted/copied, under what bucket/key) — not provider timing or transport faults.
/// </summary>
/// <remarks>
/// <para>
/// Backing store is a <see cref="ConcurrentDictionary{TKey,TValue}"/> holding fully-buffered content
/// bytes, read once from the caller's <see cref="Stream"/> during <see cref="UploadAsync"/>/
/// <see cref="Seed"/> — <see cref="FileUploadRequest.Content"/> is never disposed by this fake, per
/// its documented caller-owned contract.
/// </para>
/// <para>
/// ETags are a deterministic, internally-incrementing sequence (never a random <see cref="Guid"/>),
/// and <c>LastModified</c> is a fixed, non-real instant — never <see cref="DateTimeOffset.UtcNow"/>.
/// This fake takes no <see cref="SharedKernel.Testing.Clocks"/> dependency: sibling capability
/// folders must never reference each other, so "never real time" is achieved here via an
/// independently-declared fixed baseline.
/// </para>
/// </remarks>
public sealed class InMemoryFileStorage : IFileStorage
{
    private static readonly DateTimeOffset FixedLastModified = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly ConcurrentDictionary<(string Bucket, string Key), StoredObject> _store = new();
    private readonly ConcurrentQueue<(string Bucket, string Key)> _uploadedKeys = new();
    private readonly ConcurrentQueue<(string Bucket, string Key)> _deletedKeys = new();
    private readonly ConcurrentQueue<(string SourceBucket, string SourceKey, string DestinationBucket, string DestinationKey)> _copiedPairs = new();
    private long _etagSequence;

    /// <summary>
    /// Gets or sets a value indicating whether write-path operations should simulate a provider
    /// failure. When <see langword="true"/>, <see cref="UploadAsync"/>, <see cref="CopyAsync"/>,
    /// <see cref="DeleteAsync"/>, and <see cref="DeleteManyAsync"/> all return the matching
    /// <see cref="StorageErrors"/> failure instead of performing the operation. Read-path members
    /// (<see cref="DownloadAsync"/>/<see cref="ExistsAsync"/>/<see cref="GetMetadataAsync"/>/
    /// <see cref="ListAsync"/>) and <see cref="CheckHealthAsync"/> are unaffected by this toggle.
    /// </summary>
    public bool SimulateFailure { get; set; }

    /// <summary>Every key ever successfully uploaded, thread-safe, append-only — never pruned on delete.</summary>
    public IReadOnlyList<(string Bucket, string Key)> UploadedKeys => _uploadedKeys.ToArray();

    /// <summary>Every key ever successfully deleted via <see cref="DeleteAsync"/> or <see cref="DeleteManyAsync"/>.</summary>
    public IReadOnlyList<(string Bucket, string Key)> DeletedKeys => _deletedKeys.ToArray();

    /// <summary>Every source/destination pair ever successfully copied via <see cref="CopyAsync"/>.</summary>
    public IReadOnlyList<(string SourceBucket, string SourceKey, string DestinationBucket, string DestinationKey)> CopiedPairs =>
        _copiedPairs.ToArray();

    /// <inheritdoc />
    public async Task<SharedKernel.Primitives.Results.Result<FileReference>> UploadAsync(FileUploadRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (SimulateFailure)
        {
            return SharedKernel.Primitives.Results.Result<FileReference>.Failure(StorageErrors.UploadFailed(request.Bucket, request.Key));
        }

        var content = await ReadAllBytesAsync(request.Content, cancellationToken).ConfigureAwait(false);
        var etag = NextETag();

        _store[(request.Bucket, request.Key)] = new StoredObject(
            content,
            request.ContentType,
            request.Metadata ?? new Dictionary<string, string>(),
            etag,
            VersionId: null,
            FixedLastModified);

        _uploadedKeys.Enqueue((request.Bucket, request.Key));

        return SharedKernel.Primitives.Results.Result<FileReference>.Success(new FileReference
        {
            Bucket = request.Bucket,
            Key = request.Key,
            ETag = etag,
            VersionId = null,
        });
    }

    /// <inheritdoc />
    public Task<SharedKernel.Primitives.Results.Result<FileDownload>> DownloadAsync(string bucket, string key, CancellationToken cancellationToken)
    {
        if (!_store.TryGetValue((bucket, key), out var stored))
        {
            return Task.FromResult(SharedKernel.Primitives.Results.Result<FileDownload>.Failure(StorageErrors.NotFound(bucket, key)));
        }

        var download = new FileDownload
        {
            Content = new MemoryStream(stored.Content, writable: false),
            ContentType = stored.ContentType,
            ContentLength = stored.Content.Length,
            Metadata = stored.Metadata,
        };

        return Task.FromResult(SharedKernel.Primitives.Results.Result<FileDownload>.Success(download));
    }

    /// <inheritdoc />
    public Task<Result> DeleteAsync(string bucket, string key, CancellationToken cancellationToken)
    {
        if (SimulateFailure)
        {
            // AccessDenied is the only failure StorageErrors defines for a single-key delete —
            // mirrors the real providers' own DeleteAsync failure mapping (no dedicated
            // "DeleteFailed" factory exists).
            return Task.FromResult(Result.Failure(StorageErrors.AccessDenied(bucket, key)));
        }

        // Idempotent — deleting an absent key still succeeds, matching the real contract exactly.
        if (_store.TryRemove((bucket, key), out _))
        {
            _deletedKeys.Enqueue((bucket, key));
        }

        return Task.FromResult(Result.Success());
    }

    /// <inheritdoc />
    public Task<SharedKernel.Primitives.Results.Result<bool>> ExistsAsync(string bucket, string key, CancellationToken cancellationToken) =>
        Task.FromResult(SharedKernel.Primitives.Results.Result<bool>.Success(_store.ContainsKey((bucket, key))));

    /// <inheritdoc />
    public Task<SharedKernel.Primitives.Results.Result<FileMetadata>> GetMetadataAsync(string bucket, string key, CancellationToken cancellationToken)
    {
        if (!_store.TryGetValue((bucket, key), out var stored))
        {
            return Task.FromResult(SharedKernel.Primitives.Results.Result<FileMetadata>.Failure(StorageErrors.NotFound(bucket, key)));
        }

        return Task.FromResult(SharedKernel.Primitives.Results.Result<FileMetadata>.Success(new FileMetadata
        {
            Bucket = bucket,
            Key = key,
            ContentType = stored.ContentType,
            ContentLength = stored.Content.Length,
            LastModified = stored.LastModified,
            ETag = stored.ETag,
        }));
    }

    /// <inheritdoc />
    public Task<SharedKernel.Primitives.Results.Result<FileReference>> CopyAsync(
        string sourceBucket,
        string sourceKey,
        string destinationBucket,
        string destinationKey,
        CancellationToken cancellationToken)
    {
        if (SimulateFailure)
        {
            return Task.FromResult(
                SharedKernel.Primitives.Results.Result<FileReference>.Failure(StorageErrors.CopyFailed(sourceBucket, sourceKey, destinationBucket, destinationKey)));
        }

        if (!_store.TryGetValue((sourceBucket, sourceKey), out var source))
        {
            return Task.FromResult(SharedKernel.Primitives.Results.Result<FileReference>.Failure(StorageErrors.NotFound(sourceBucket, sourceKey)));
        }

        var etag = NextETag();
        var copiedContent = new byte[source.Content.Length];
        Array.Copy(source.Content, copiedContent, source.Content.Length);

        _store[(destinationBucket, destinationKey)] = new StoredObject(
            copiedContent,
            source.ContentType,
            source.Metadata,
            etag,
            VersionId: null,
            FixedLastModified);

        _copiedPairs.Enqueue((sourceBucket, sourceKey, destinationBucket, destinationKey));

        return Task.FromResult(SharedKernel.Primitives.Results.Result<FileReference>.Success(new FileReference
        {
            Bucket = destinationBucket,
            Key = destinationKey,
            ETag = etag,
            VersionId = null,
        }));
    }

    /// <inheritdoc />
    public Task<SharedKernel.Primitives.Results.Result<IReadOnlyList<FileDeleteOutcome>>> DeleteManyAsync(
        string bucket,
        IReadOnlyCollection<string> keys,
        CancellationToken cancellationToken)
    {
        if (SimulateFailure)
        {
            return Task.FromResult(
                SharedKernel.Primitives.Results.Result<IReadOnlyList<FileDeleteOutcome>>.Failure(StorageErrors.BatchDeleteFailed(bucket)));
        }

        if (keys is null || keys.Count == 0)
        {
            return Task.FromResult(
                SharedKernel.Primitives.Results.Result<IReadOnlyList<FileDeleteOutcome>>.Failure(StorageErrors.BatchDeleteFailed(bucket)));
        }

        var outcomes = new List<FileDeleteOutcome>(keys.Count);
        foreach (var key in keys)
        {
            // Idempotent per-key — an absent key still reports success, matching DeleteAsync's contract.
            if (_store.TryRemove((bucket, key), out _))
            {
                _deletedKeys.Enqueue((bucket, key));
            }

            outcomes.Add(FileDeleteOutcome.Success(key));
        }

        return Task.FromResult(SharedKernel.Primitives.Results.Result<IReadOnlyList<FileDeleteOutcome>>.Success(outcomes));
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<FileMetadata> ListAsync(
        string bucket,
        string prefix,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var entry in _store)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (entryBucket, entryKey) = entry.Key;
            if (entryBucket != bucket || !entryKey.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            yield return new FileMetadata
            {
                Bucket = entryBucket,
                Key = entryKey,
                ContentType = entry.Value.ContentType,
                ContentLength = entry.Value.Content.Length,
                LastModified = entry.Value.LastModified,
                ETag = entry.Value.ETag,
            };

            await Task.Yield();
        }
    }

    /// <inheritdoc />
    public Task<Result> CheckHealthAsync(string bucket, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success());

    /// <summary>Returns whether <paramref name="bucket"/>/<paramref name="key"/> was ever successfully uploaded.</summary>
    public bool WasUploaded(string bucket, string key) => _uploadedKeys.Any(k => k.Bucket == bucket && k.Key == key);

    /// <summary>Returns whether <paramref name="bucket"/>/<paramref name="key"/> was ever successfully deleted.</summary>
    public bool WasDeleted(string bucket, string key) => _deletedKeys.Any(k => k.Bucket == bucket && k.Key == key);

    /// <summary>Returns whether the given source/destination pair was ever successfully copied.</summary>
    public bool WasCopied(string sourceBucket, string sourceKey, string destinationBucket, string destinationKey) =>
        _copiedPairs.Any(p =>
            p.SourceBucket == sourceBucket
            && p.SourceKey == sourceKey
            && p.DestinationBucket == destinationBucket
            && p.DestinationKey == destinationKey);

    /// <summary>
    /// Pre-populates storage without going through <see cref="UploadAsync"/> — a test-setup helper.
    /// Reads <paramref name="content"/> fully but does not dispose the caller's stream, the same
    /// ownership contract as <see cref="UploadAsync"/>.
    /// </summary>
    /// <param name="bucket">The bucket to seed the object into.</param>
    /// <param name="key">The object key.</param>
    /// <param name="content">The object payload, read from its current position.</param>
    /// <param name="contentType">The object's MIME type.</param>
    /// <param name="metadata">Optional user metadata. <see langword="null"/> means none.</param>
    public void Seed(
        string bucket,
        string key,
        Stream content,
        string contentType,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(content);

        using var buffer = new MemoryStream();
        content.CopyTo(buffer);

        _store[(bucket, key)] = new StoredObject(
            buffer.ToArray(),
            contentType,
            metadata ?? new Dictionary<string, string>(),
            NextETag(),
            VersionId: null,
            FixedLastModified);
    }

    /// <summary>Clears all stored objects and the <see cref="UploadedKeys"/>/<see cref="DeletedKeys"/>/<see cref="CopiedPairs"/> recordings.</summary>
    public void Reset()
    {
        _store.Clear();
        _uploadedKeys.Clear();
        _deletedKeys.Clear();
        _copiedPairs.Clear();
    }

    private string NextETag() => $"etag-{Interlocked.Increment(ref _etagSequence)}";

    private static async Task<byte[]> ReadAllBytesAsync(Stream content, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return buffer.ToArray();
    }

    private sealed record StoredObject(
        byte[] Content,
        string ContentType,
        IReadOnlyDictionary<string, string> Metadata,
        string ETag,
        string? VersionId,
        DateTimeOffset LastModified);
}
