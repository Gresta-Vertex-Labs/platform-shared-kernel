using System.Runtime.CompilerServices;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Storage.Abstractions.Tests;

/// <summary>
/// A provider store that records the keys it receives and echoes them back in its results, so tests can see
/// exactly what the registry's wrapper sends to a provider and what it hands back to the caller.
/// </summary>
internal sealed class RecordingFileStorage(string storeName) : IFileStorage
{
    public string StoreName { get; } = storeName;

    public TenantId? TenantId => null;

    public List<string> Keys { get; } = [];

    public int Calls { get; private set; }

    /// <summary>When set, every Result-returning member fails with this error.</summary>
    public Error? FailWith { get; set; }

    /// <summary>Keys <see cref="ListAsync"/> and <see cref="ListPageAsync"/> return when they match the prefix.</summary>
    public List<string> StoredKeys { get; } = [];

    public (IFileStorage Destination, string DestinationKey)? LastCopyTarget { get; private set; }

    public Task<Result<FileReference>> UploadAsync(string key, Stream content, FileUploadOptions? options = null, CancellationToken cancellationToken = default) =>
        Reply(key, () => new FileReference { Store = StoreName, Key = key, ETag = "\"e\"" });

    public Task<Result<FileDownload>> DownloadAsync(string key, FileDownloadOptions? options = null, CancellationToken cancellationToken = default) =>
        Reply(key, () => new FileDownload(new MemoryStream([1, 2, 3]), new FileProperties { Key = key, ContentLength = 3 }, 3));

    public Task<Result<FileProperties>> GetPropertiesAsync(string key, CancellationToken cancellationToken = default) =>
        Reply(key, () => new FileProperties { Key = key, ContentLength = 3 });

    public Task<Result<bool>> ExistsAsync(string key, CancellationToken cancellationToken = default) => Reply(key, () => true);

    public async Task<Result> DeleteAsync(string key, FileDeleteOptions? options = null, CancellationToken cancellationToken = default)
    {
        Result<bool> result = await Reply(key, () => true);
        return result.IsSuccess ? Result.Success() : Result.Failure(result.Error);
    }

    public Task<Result<BatchDeleteResult>> DeleteManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        string[] all = [.. keys];
        Keys.AddRange(all);
        Calls++;
        return Task.FromResult(Result<BatchDeleteResult>.Success(new BatchDeleteResult
        {
            Deleted = all[..^1],
            Failed = [new FileDeleteFailure(all[^1], StorageErrors.AccessDenied(StoreName, all[^1]))],
        }));
    }

    public Task<Result<FileReference>> CopyAsync(string sourceKey, string destinationKey, FileCopyOptions? options = null, CancellationToken cancellationToken = default)
    {
        Keys.Add(sourceKey);
        return Reply(destinationKey, () => new FileReference { Store = StoreName, Key = destinationKey });
    }

    public Task<Result<FileReference>> CopyToAsync(string sourceKey, IFileStorage destination, string destinationKey, FileCopyOptions? options = null, CancellationToken cancellationToken = default)
    {
        Keys.Add(sourceKey);
        LastCopyTarget = (destination, destinationKey);
        return Reply(destinationKey, () => new FileReference { Store = destination.StoreName, Key = destinationKey });
    }

    public async IAsyncEnumerable<FileListItem> ListAsync(string prefix = "", [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Keys.Add(prefix);
        Calls++;
        foreach (string key in StoredKeys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)))
        {
            await Task.Yield();
            yield return new FileListItem { Key = key, ContentLength = 1 };
        }
    }

    public Task<Result<FileListPage>> ListPageAsync(FileListRequest request, CancellationToken cancellationToken = default) =>
        Reply(request.Prefix, () => new FileListPage
        {
            Items = [.. StoredKeys.Where(k => k.StartsWith(request.Prefix, StringComparison.Ordinal)).Select(k => new FileListItem { Key = k, ContentLength = 1 })],
            Folders = [request.Prefix + "sub/"],
        });

    public Task<Result<PresignedRequest>> CreateDownloadUrlAsync(string key, PresignedDownloadOptions options, CancellationToken cancellationToken = default) =>
        Reply(key, () => Presigned(key));

    public Task<Result<PresignedRequest>> CreateUploadUrlAsync(string key, PresignedUploadOptions options, CancellationToken cancellationToken = default) =>
        Reply(key, () => Presigned(key));

    public Task<Result<PresignedPost>> CreateUploadFormAsync(string key, PresignedPostOptions options, CancellationToken cancellationToken = default) =>
        Reply(key, () => new PresignedPost { Url = new Uri("https://example.test/"), Fields = new Dictionary<string, string> { ["key"] = key }, ExpiresAt = DateTimeOffset.UnixEpoch });

    public Task<Result<MultipartUpload>> StartMultipartUploadAsync(string key, MultipartUploadOptions? options = null, CancellationToken cancellationToken = default) =>
        Reply(key, () => new MultipartUpload(key, "upload-1"));

    public Task<Result<PresignedRequest>> CreateUploadPartUrlAsync(MultipartUpload upload, int partNumber, TimeSpan expiry, CancellationToken cancellationToken = default) =>
        Reply(upload.Key, () => Presigned(upload.Key));

    public Task<Result<FileReference>> CompleteMultipartUploadAsync(MultipartUpload upload, IReadOnlyCollection<UploadedPart> parts, WriteCondition? condition = null, CancellationToken cancellationToken = default) =>
        Reply(upload.Key, () => new FileReference { Store = StoreName, Key = upload.Key });

    public async Task<Result> AbortMultipartUploadAsync(MultipartUpload upload, CancellationToken cancellationToken = default)
    {
        Result<bool> result = await Reply(upload.Key, () => true);
        return result.IsSuccess ? Result.Success() : Result.Failure(result.Error);
    }

    private static PresignedRequest Presigned(string key) => new()
    {
        Url = new Uri($"https://example.test/{key}"),
        Method = "GET",
        Headers = new Dictionary<string, string>(),
        ExpiresAt = DateTimeOffset.UnixEpoch,
    };

    private Task<Result<T>> Reply<T>(string key, Func<T> value)
    {
        Keys.Add(key);
        Calls++;
        return Task.FromResult(FailWith is { } error ? Result<T>.Failure(error) : Result<T>.Success(value()));
    }
}
