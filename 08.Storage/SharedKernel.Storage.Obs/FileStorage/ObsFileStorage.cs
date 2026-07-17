using System.Net;
using System.Runtime.CompilerServices;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Storage.Abstractions.Abstractions;
using SharedKernel.Storage.Abstractions.Errors;
using SharedKernel.Storage.Abstractions.Models;
using SharedKernel.Storage.Obs.Constants;
using SharedKernel.Storage.Obs.Logging;

namespace SharedKernel.Storage.Obs.FileStorage;

/// <summary>
/// Huawei Cloud OBS implementation of <see cref="IFileStorage"/>, consumed over OBS's
/// S3-compatible endpoint via <see cref="IAmazonS3"/>.
/// </summary>
/// <remarks>
/// <para>
/// Independent type from <c>SharedKernel.Storage.S3</c>'s <c>S3FileStorage</c> — not a shared base —
/// so the two providers stay independently swappable per the sibling-package rule.
/// </para>
/// <para>
/// <see cref="UploadAsync"/> streams through <see cref="ITransferUtility"/> for multipart-aware
/// uploads. Every member maps <see cref="AmazonS3Exception"/> status codes onto
/// <see cref="StorageErrors"/> — expected failures never propagate as exceptions.
/// </para>
/// </remarks>
public sealed class ObsFileStorage : IFileStorage
{
    private const string UploadOperation = "Upload";
    private const string DownloadOperation = "Download";
    private const string DeleteOperation = "Delete";
    private const string ExistsOperation = "Exists";
    private const string MetadataOperation = "GetMetadata";
    private const string CopyOperation = "Copy";
    private const string NoSuchKeyErrorCode = "NoSuchKey";

    private readonly IAmazonS3 _obs;
    private readonly ITransferUtility _transferUtility;
    private readonly ILogger<ObsFileStorage> _logger;

    /// <summary>Initializes a new instance backed by <paramref name="obs"/>.</summary>
    /// <param name="obs">The shared, singleton-lifetime OBS-endpoint S3 client.</param>
    /// <param name="logger">Logger for structured provider-rejection recording.</param>
    public ObsFileStorage(IAmazonS3 obs, ILogger<ObsFileStorage> logger)
    {
        ArgumentNullException.ThrowIfNull(obs);
        ArgumentNullException.ThrowIfNull(logger);

        _obs = obs;
        _logger = logger;
        _transferUtility = new TransferUtility(obs);
    }

    /// <inheritdoc />
    public async Task<Result<FileReference>> UploadAsync(FileUploadRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (ValidateBucketAndKey(request.Bucket, request.Key) is { } validationError)
        {
            return Result<FileReference>.Failure(validationError);
        }

        var uploadRequest = new TransferUtilityUploadRequest
        {
            BucketName = request.Bucket,
            Key = request.Key,
            InputStream = request.Content,
            ContentType = request.ContentType,
            AutoCloseStream = false,
            AutoResetStreamPosition = false,
        };

        if (request.Metadata is not null)
        {
            foreach (var (metadataKey, metadataValue) in request.Metadata)
            {
                uploadRequest.Metadata.Add(metadataKey, metadataValue);
            }
        }

        try
        {
            var response = await _transferUtility
                .UploadWithResponseAsync(uploadRequest, cancellationToken)
                .ConfigureAwait(false);

            return Result<FileReference>.Success(new FileReference
            {
                Bucket = request.Bucket,
                Key = request.Key,
                ETag = response.ETag,
                VersionId = response.VersionId,
            });
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            ObsStorageLog.ObjectNotFound(_logger, ex, request.Bucket, request.Key, UploadOperation);
            return Result<FileReference>.Failure(StorageErrors.NotFound(request.Bucket, request.Key));
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
        {
            ObsStorageLog.AccessDenied(_logger, ex, request.Bucket, request.Key, UploadOperation);
            return Result<FileReference>.Failure(StorageErrors.AccessDenied(request.Bucket, request.Key));
        }
        catch (AmazonS3Exception ex)
        {
            ObsStorageLog.UploadFailed(_logger, ex, request.Bucket, request.Key);
            return Result<FileReference>.Failure(StorageErrors.UploadFailed(request.Bucket, request.Key));
        }
    }

    /// <inheritdoc />
    public async Task<Result<FileDownload>> DownloadAsync(string bucket, string key, CancellationToken cancellationToken)
    {
        if (ValidateBucketAndKey(bucket, key) is { } validationError)
        {
            return Result<FileDownload>.Failure(validationError);
        }

        try
        {
            var response = await _obs
                .GetObjectAsync(new GetObjectRequest { BucketName = bucket, Key = key }, cancellationToken)
                .ConfigureAwait(false);

            return Result<FileDownload>.Success(new FileDownload
            {
                Content = response.ResponseStream,
                ContentType = response.Headers.ContentType ?? string.Empty,
                ContentLength = response.ContentLength,
                Metadata = ToMetadataDictionary(response.Metadata),
            });
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            ObsStorageLog.ObjectNotFound(_logger, ex, bucket, key, DownloadOperation);
            return Result<FileDownload>.Failure(StorageErrors.NotFound(bucket, key));
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
        {
            ObsStorageLog.AccessDenied(_logger, ex, bucket, key, DownloadOperation);
            return Result<FileDownload>.Failure(StorageErrors.AccessDenied(bucket, key));
        }
    }

    /// <inheritdoc />
    public async Task<Result> DeleteAsync(string bucket, string key, CancellationToken cancellationToken)
    {
        if (ValidateBucketAndKey(bucket, key) is { } validationError)
        {
            return Result.Failure(validationError);
        }

        try
        {
            await _obs.DeleteObjectAsync(bucket, key, cancellationToken).ConfigureAwait(false);
            return Result.Success();
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            // Idempotent: deleting an absent key succeeds.
            return Result.Success();
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
        {
            ObsStorageLog.AccessDenied(_logger, ex, bucket, key, DeleteOperation);
            return Result.Failure(StorageErrors.AccessDenied(bucket, key));
        }
    }

    /// <inheritdoc />
    public async Task<Result<bool>> ExistsAsync(string bucket, string key, CancellationToken cancellationToken)
    {
        if (ValidateBucketAndKey(bucket, key) is { } validationError)
        {
            return Result<bool>.Failure(validationError);
        }

        try
        {
            await _obs.GetObjectMetadataAsync(bucket, key, cancellationToken).ConfigureAwait(false);
            return Result<bool>.Success(true);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return Result<bool>.Success(false);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
        {
            ObsStorageLog.AccessDenied(_logger, ex, bucket, key, ExistsOperation);
            return Result<bool>.Failure(StorageErrors.AccessDenied(bucket, key));
        }
    }

    /// <inheritdoc />
    public async Task<Result<FileMetadata>> GetMetadataAsync(string bucket, string key, CancellationToken cancellationToken)
    {
        if (ValidateBucketAndKey(bucket, key) is { } validationError)
        {
            return Result<FileMetadata>.Failure(validationError);
        }

        try
        {
            var response = await _obs.GetObjectMetadataAsync(bucket, key, cancellationToken).ConfigureAwait(false);

            return Result<FileMetadata>.Success(new FileMetadata
            {
                Bucket = bucket,
                Key = key,
                ContentType = response.Headers.ContentType ?? string.Empty,
                ContentLength = response.ContentLength,
                LastModified = ToUtcDateTimeOffset(response.LastModified),
                ETag = response.ETag,
            });
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            ObsStorageLog.ObjectNotFound(_logger, ex, bucket, key, MetadataOperation);
            return Result<FileMetadata>.Failure(StorageErrors.NotFound(bucket, key));
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
        {
            ObsStorageLog.AccessDenied(_logger, ex, bucket, key, MetadataOperation);
            return Result<FileMetadata>.Failure(StorageErrors.AccessDenied(bucket, key));
        }
    }

    /// <inheritdoc />
    public async Task<Result<FileReference>> CopyAsync(
        string sourceBucket,
        string sourceKey,
        string destinationBucket,
        string destinationKey,
        CancellationToken cancellationToken)
    {
        if (ValidateBucketAndKey(sourceBucket, sourceKey) is { } sourceValidationError)
        {
            return Result<FileReference>.Failure(sourceValidationError);
        }

        if (ValidateBucketAndKey(destinationBucket, destinationKey) is { } destinationValidationError)
        {
            return Result<FileReference>.Failure(destinationValidationError);
        }

        try
        {
            var request = new CopyObjectRequest
            {
                SourceBucket = sourceBucket,
                SourceKey = sourceKey,
                DestinationBucket = destinationBucket,
                DestinationKey = destinationKey,
            };

            var response = await _obs.CopyObjectAsync(request, cancellationToken).ConfigureAwait(false);

            return Result<FileReference>.Success(new FileReference
            {
                Bucket = destinationBucket,
                Key = destinationKey,
                ETag = response.ETag,
                VersionId = response.VersionId,
            });
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            ObsStorageLog.ObjectNotFound(_logger, ex, sourceBucket, sourceKey, CopyOperation);
            return Result<FileReference>.Failure(StorageErrors.NotFound(sourceBucket, sourceKey));
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.Forbidden)
        {
            ObsStorageLog.AccessDenied(_logger, ex, sourceBucket, sourceKey, CopyOperation);
            return Result<FileReference>.Failure(StorageErrors.AccessDenied(sourceBucket, sourceKey));
        }
        catch (AmazonS3Exception ex)
        {
            ObsStorageLog.CopyFailed(_logger, ex, sourceBucket, sourceKey, destinationBucket, destinationKey);
            return Result<FileReference>.Failure(
                StorageErrors.CopyFailed(sourceBucket, sourceKey, destinationBucket, destinationKey));
        }
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<FileDeleteOutcome>>> DeleteManyAsync(
        string bucket,
        IReadOnlyCollection<string> keys,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(bucket))
        {
            return Result<IReadOnlyList<FileDeleteOutcome>>.Failure(StorageErrors.InvalidBucket(bucket));
        }

        if (keys is null || keys.Count == 0)
        {
            return Result<IReadOnlyList<FileDeleteOutcome>>.Failure(StorageErrors.BatchDeleteFailed(bucket));
        }

        var outcomes = new List<FileDeleteOutcome>(keys.Count);

        foreach (var chunk in keys.Chunk(ObsStorageConstants.MaxBatchDeleteKeys))
        {
            var request = new DeleteObjectsRequest
            {
                BucketName = bucket,
                Objects = [.. chunk.Select(key => new KeyVersion { Key = key })],
            };

            try
            {
                var response = await _obs.DeleteObjectsAsync(request, cancellationToken).ConfigureAwait(false);
                outcomes.AddRange(response.DeletedObjects.Select(deleted => FileDeleteOutcome.Success(deleted.Key)));
            }
            catch (DeleteObjectsException ex)
            {
                outcomes.AddRange(ex.Response.DeletedObjects.Select(deleted => FileDeleteOutcome.Success(deleted.Key)));
                outcomes.AddRange(ex.Response.DeleteErrors.Select(error => MapDeleteError(bucket, error)));
            }
            catch (AmazonS3Exception ex)
            {
                ObsStorageLog.BatchDeleteFailed(_logger, ex, bucket);
                return Result<IReadOnlyList<FileDeleteOutcome>>.Failure(StorageErrors.BatchDeleteFailed(bucket));
            }
        }

        return Result<IReadOnlyList<FileDeleteOutcome>>.Success(outcomes);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<FileMetadata> ListAsync(string bucket, string prefix, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(bucket))
        {
            throw new ArgumentException("Bucket must not be empty.", nameof(bucket));
        }

        return ListAsyncCore(bucket, prefix ?? string.Empty, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result> CheckHealthAsync(string bucket, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(bucket))
        {
            return Result.Failure(StorageErrors.InvalidBucket(bucket));
        }

        try
        {
            await _obs.HeadBucketAsync(new HeadBucketRequest { BucketName = bucket }, cancellationToken)
                .ConfigureAwait(false);
            return Result.Success();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ObsStorageLog.ConnectivityCheckFailed(_logger, ex, bucket);
            return Result.Failure(StorageErrors.ConnectivityFailure(bucket));
        }
    }

    private async IAsyncEnumerable<FileMetadata> ListAsyncCore(
        string bucket,
        string prefix,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string? continuationToken = null;

        do
        {
            var request = new ListObjectsV2Request
            {
                BucketName = bucket,
                Prefix = prefix,
                ContinuationToken = continuationToken,
            };

            var response = await _obs.ListObjectsV2Async(request, cancellationToken).ConfigureAwait(false);

            foreach (var s3Object in response.S3Objects)
            {
                // ListObjectsV2 does not return per-object Content-Type — retrieving it would require
                // a HEAD call per key, defeating the constant-memory streaming purpose of this method.
                yield return new FileMetadata
                {
                    Bucket = bucket,
                    Key = s3Object.Key,
                    ContentType = string.Empty,
                    ContentLength = s3Object.Size ?? 0,
                    LastModified = ToUtcDateTimeOffset(s3Object.LastModified),
                    ETag = s3Object.ETag,
                };
            }

            continuationToken = response.IsTruncated == true ? response.NextContinuationToken : null;
        }
        while (continuationToken is not null);
    }

    private static FileDeleteOutcome MapDeleteError(string bucket, DeleteError error)
    {
        var mappedError = string.Equals(error.Code, NoSuchKeyErrorCode, StringComparison.OrdinalIgnoreCase)
            ? StorageErrors.NotFound(bucket, error.Key)
            : StorageErrors.AccessDenied(bucket, error.Key);

        return FileDeleteOutcome.Failure(error.Key, mappedError);
    }

    private static IReadOnlyDictionary<string, string> ToMetadataDictionary(MetadataCollection metadata)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var key in metadata.Keys)
        {
            result[key] = metadata[key];
        }

        return result;
    }

    private static DateTimeOffset ToUtcDateTimeOffset(DateTime? value) =>
        value.HasValue
            ? new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc))
            : DateTimeOffset.MinValue;

    private static Error? ValidateBucketAndKey(string bucket, string key)
    {
        if (string.IsNullOrWhiteSpace(bucket))
        {
            return StorageErrors.InvalidBucket(bucket);
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            return StorageErrors.InvalidKey(key);
        }

        return null;
    }
}
