using System.Runtime.CompilerServices;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Storage;

/// <summary>
/// The <see cref="IFileStorage"/> application code receives: validates every request, then forwards it to
/// the provider's store with the tenant prefix applied (empty for a shared store), and maps keys in results
/// back to relative keys.
/// </summary>
/// <remarks>
/// Every store handed out goes through this type, so the provider's store — which sees the whole bucket — is
/// never reachable directly, and copies between any two handed-out stores can be unwrapped to the providers'
/// stores, letting a provider copy server-side.
/// </remarks>
internal sealed class ScopedFileStorage : IFileStorage
{
    private readonly IFileStorage _inner;
    private readonly string _prefix;

    public ScopedFileStorage(IFileStorage inner, TenantId? tenantId)
    {
        _inner = inner;
        TenantId = tenantId;
        _prefix = tenantId is null ? string.Empty : $"{TenantFileStorage.TenantsFolder}{tenantId.Value}/";
    }

    public string StoreName => _inner.StoreName;

    public TenantId? TenantId { get; }

    public async Task<Result<FileReference>> UploadAsync(
        string key,
        Stream content,
        FileUploadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        if ((StorageValidation.ValidateKey(key) ?? StorageValidation.ValidateUpload(options)) is Error error)
        {
            return error;
        }

        Result<FileReference> result = await _inner
            .UploadAsync(ToInner(key), content, options, cancellationToken)
            .ConfigureAwait(false);
        return Map(result, ToCaller);
    }

    public async Task<Result<FileDownload>> DownloadAsync(
        string key,
        FileDownloadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (StorageValidation.ValidateKey(key) is Error error)
        {
            return error;
        }

        Result<FileDownload> result = await _inner
            .DownloadAsync(ToInner(key), options, cancellationToken)
            .ConfigureAwait(false);
        return Map(
            result,
            download => _prefix.Length == 0
                ? download
                : new FileDownload(download.Content, ToCaller(download.Properties), download.Length, download.Range));
    }

    public async Task<Result<FileProperties>> GetPropertiesAsync(string key, CancellationToken cancellationToken = default)
    {
        if (StorageValidation.ValidateKey(key) is Error error)
        {
            return error;
        }

        Result<FileProperties> result = await _inner.GetPropertiesAsync(ToInner(key), cancellationToken).ConfigureAwait(false);
        return Map(result, ToCaller);
    }

    public async Task<Result<bool>> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        if (StorageValidation.ValidateKey(key) is Error error)
        {
            return error;
        }

        Result<bool> result = await _inner.ExistsAsync(ToInner(key), cancellationToken).ConfigureAwait(false);
        return Map(result, exists => exists);
    }

    public async Task<Result> DeleteAsync(string key, FileDeleteOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (StorageValidation.ValidateKey(key) is Error error)
        {
            return error;
        }

        Result result = await _inner.DeleteAsync(ToInner(key), options, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? result : Result.Failure(ToCaller(result.Error));
    }

    public async Task<Result<BatchDeleteResult>> DeleteManyAsync(
        IEnumerable<string> keys,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);

        var distinct = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string key in keys)
        {
            if (StorageValidation.ValidateKey(key) is Error error)
            {
                return error;
            }

            if (seen.Add(key))
            {
                distinct.Add(ToInner(key));
            }
        }

        if (distinct.Count == 0)
        {
            return new BatchDeleteResult { Deleted = [], Failed = [] };
        }

        Result<BatchDeleteResult> result = await _inner.DeleteManyAsync(distinct, cancellationToken).ConfigureAwait(false);
        return Map(result, batch => _prefix.Length == 0
            ? batch
            : new BatchDeleteResult
            {
                Deleted = [.. batch.Deleted.Select(ToCallerKey)],
                Failed = [.. batch.Failed.Select(f => new FileDeleteFailure(ToCallerKey(f.Key), ToCaller(f.Error)))],
            });
    }

    public async Task<Result<FileReference>> CopyAsync(
        string sourceKey,
        string destinationKey,
        FileCopyOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if ((StorageValidation.ValidateKey(sourceKey)
            ?? StorageValidation.ValidateKey(destinationKey)
            ?? StorageValidation.ValidateCopy(options)) is Error error)
        {
            return error;
        }

        Result<FileReference> result = await _inner
            .CopyAsync(ToInner(sourceKey), ToInner(destinationKey), options, cancellationToken)
            .ConfigureAwait(false);
        return Map(result, ToCaller);
    }

    public async Task<Result<FileReference>> CopyToAsync(
        string sourceKey,
        IFileStorage destination,
        string destinationKey,
        FileCopyOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if ((StorageValidation.ValidateKey(sourceKey)
            ?? StorageValidation.ValidateKey(destinationKey)
            ?? StorageValidation.ValidateCopy(options)) is Error error)
        {
            return error;
        }

        if (destination is not ScopedFileStorage target)
        {
            // A store not handed out by the registry (a test double, a hand-built store): let the provider
            // stream into it through its public members.
            return Map(
                await _inner.CopyToAsync(ToInner(sourceKey), destination, destinationKey, options, cancellationToken)
                    .ConfigureAwait(false),
                reference => reference);
        }

        Result<FileReference> result = await _inner
            .CopyToAsync(ToInner(sourceKey), target._inner, target.ToInner(destinationKey), options, cancellationToken)
            .ConfigureAwait(false);
        return result.IsSuccess
            ? target.ToCaller(result.Value)
            : Result<FileReference>.Failure(ToCaller(target.ToCaller(result.Error)));
    }

    public IAsyncEnumerable<FileListItem> ListAsync(string prefix = "", CancellationToken cancellationToken = default)
    {
        if (StorageValidation.ValidatePrefix(prefix) is Error error)
        {
            throw new StorageException(error);
        }

        return ListCoreAsync(prefix, cancellationToken);
    }

    public async Task<Result<FileListPage>> ListPageAsync(FileListRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (StorageValidation.ValidateList(request) is Error error)
        {
            return error;
        }

        Result<FileListPage> result = await _inner
            .ListPageAsync(request with { Prefix = _prefix + request.Prefix }, cancellationToken)
            .ConfigureAwait(false);
        return Map(result, page => _prefix.Length == 0
            ? page
            : page with
            {
                Items = [.. page.Items.Select(ToCaller)],
                Folders = [.. page.Folders.Select(ToCallerKey)],
            });
    }

    public async Task<Result<PresignedRequest>> CreateDownloadUrlAsync(
        string key,
        PresignedDownloadOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (StorageValidation.ValidateKey(key) is Error error)
        {
            return error;
        }

        return Map(
            await _inner.CreateDownloadUrlAsync(ToInner(key), options, cancellationToken).ConfigureAwait(false),
            request => request);
    }

    public async Task<Result<PresignedRequest>> CreateUploadUrlAsync(
        string key,
        PresignedUploadOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (StorageValidation.ValidateKey(key) is Error error)
        {
            return error;
        }

        return Map(
            await _inner.CreateUploadUrlAsync(ToInner(key), options, cancellationToken).ConfigureAwait(false),
            request => request);
    }

    public async Task<Result<PresignedPost>> CreateUploadFormAsync(
        string key,
        PresignedPostOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (StorageValidation.ValidateKey(key) is Error error)
        {
            return error;
        }

        return Map(
            await _inner.CreateUploadFormAsync(ToInner(key), options, cancellationToken).ConfigureAwait(false),
            post => post);
    }

    public async Task<Result<MultipartUpload>> StartMultipartUploadAsync(
        string key,
        MultipartUploadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if ((StorageValidation.ValidateKey(key) ?? StorageValidation.ValidateMultipart(options)) is Error error)
        {
            return error;
        }

        Result<MultipartUpload> result = await _inner
            .StartMultipartUploadAsync(ToInner(key), options, cancellationToken)
            .ConfigureAwait(false);
        return Map(result, upload => upload with { Key = ToCallerKey(upload.Key) });
    }

    public async Task<Result<PresignedRequest>> CreateUploadPartUrlAsync(
        MultipartUpload upload,
        int partNumber,
        TimeSpan expiry,
        CancellationToken cancellationToken = default)
    {
        if ((ValidateUpload(upload) ?? StorageValidation.ValidatePartNumber(partNumber)) is Error error)
        {
            return error;
        }

        return Map(
            await _inner.CreateUploadPartUrlAsync(ToInner(upload), partNumber, expiry, cancellationToken).ConfigureAwait(false),
            request => request);
    }

    public async Task<Result<FileReference>> CompleteMultipartUploadAsync(
        MultipartUpload upload,
        IReadOnlyCollection<UploadedPart> parts,
        WriteCondition? condition = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parts);

        if (ValidateUpload(upload) is Error error)
        {
            return error;
        }

        if (parts.Count == 0 || parts.Any(p => p is null || StorageValidation.ValidatePartNumber(p.PartNumber) is not null || string.IsNullOrWhiteSpace(p.ETag)))
        {
            return StorageErrors.InvalidRequest("Parts must be non-empty, each with a part number from 1 to 10000 and an ETag.");
        }

        if (parts.Select(p => p.PartNumber).Distinct().Count() != parts.Count)
        {
            return StorageErrors.InvalidRequest("Each part number may appear only once.");
        }

        Result<FileReference> result = await _inner
            .CompleteMultipartUploadAsync(ToInner(upload), parts, condition, cancellationToken)
            .ConfigureAwait(false);
        return Map(result, ToCaller);
    }

    public async Task<Result> AbortMultipartUploadAsync(MultipartUpload upload, CancellationToken cancellationToken = default)
    {
        if (ValidateUpload(upload) is Error error)
        {
            return error;
        }

        Result result = await _inner.AbortMultipartUploadAsync(ToInner(upload), cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? result : Result.Failure(ToCaller(result.Error));
    }

    private static Error? ValidateUpload(MultipartUpload upload)
    {
        ArgumentNullException.ThrowIfNull(upload);

        return StorageValidation.ValidateKey(upload.Key)
            ?? (string.IsNullOrWhiteSpace(upload.UploadId) ? StorageErrors.InvalidRequest("UploadId is required.") : null);
    }

    private async IAsyncEnumerable<FileListItem> ListCoreAsync(
        string prefix,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (FileListItem item in _inner.ListAsync(_prefix + prefix, cancellationToken).ConfigureAwait(false))
        {
            yield return ToCaller(item);
        }
    }

    private string ToInner(string key) => _prefix + key;

    private MultipartUpload ToInner(MultipartUpload upload) => upload with { Key = ToInner(upload.Key) };

    private string ToCallerKey(string innerKey) =>
        _prefix.Length != 0 && innerKey.StartsWith(_prefix, StringComparison.Ordinal) ? innerKey[_prefix.Length..] : innerKey;

    private FileReference ToCaller(FileReference reference) =>
        reference with { Key = ToCallerKey(reference.Key), TenantId = TenantId };

    private FileProperties ToCaller(FileProperties properties) =>
        _prefix.Length == 0 ? properties : properties with { Key = ToCallerKey(properties.Key) };

    private FileListItem ToCaller(FileListItem item) =>
        _prefix.Length == 0 ? item : item with { Key = ToCallerKey(item.Key) };

    /// <summary>Removes this view's tenant prefix from an error message, so errors name the key the caller passed.</summary>
    private Error ToCaller(Error error) =>
        _prefix.Length == 0 ? error : error with { Message = error.Message.Replace($"'{_prefix}", "'", StringComparison.Ordinal) };

    private Result<TOut> Map<TIn, TOut>(Result<TIn> result, Func<TIn, TOut> map) =>
        result.IsSuccess ? Result<TOut>.Success(map(result.Value)) : Result<TOut>.Failure(ToCaller(result.Error));
}
