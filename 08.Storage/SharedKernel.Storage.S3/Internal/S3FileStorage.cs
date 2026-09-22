using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using S3ByteRange = Amazon.S3.Model.ByteRange;

namespace SharedKernel.Storage.S3.Internal;

/// <summary>
/// The provider's store over one bucket (and optional key prefix). Only reached through the registry's
/// wrapper, which validates requests and applies tenant prefixes; keys arriving here may include a tenant prefix.
/// </summary>
internal sealed class S3FileStorage : IFileStorage
{
    private const string DefaultContentType = "application/octet-stream";
    private const string OperationUpload = "upload";
    private const string OperationDownload = "download";
    private const string OperationProperties = "get_properties";
    private const string OperationExists = "exists";
    private const string OperationDelete = "delete";
    private const string OperationDeleteMany = "delete_many";
    private const string OperationCopy = "copy";
    private const string OperationList = "list";
    private const string OperationPresign = "presign";
    private const string OperationMultipart = "multipart";
    private const string OperationProbe = "probe";
    private const int MaxDeleteBatch = 1000;

    private static readonly Dictionary<string, string> NoMetadata = [];

    private readonly S3Connection _connection;
    private readonly S3StoreOptions _options;
    private readonly IClock _clock;
    private readonly ILogger _logger;
    private readonly string _keyPrefix;

    public S3FileStorage(string storeName, S3StoreOptions options, S3Connection connection, IClock clock, ILogger<S3FileStorage> logger)
    {
        StoreName = storeName;
        _options = options;
        _connection = connection;
        _clock = clock;
        _logger = logger;
        _keyPrefix = options.KeyPrefix ?? string.Empty;
    }

    public string StoreName { get; }

    public string? TenantId => null;

    internal string Bucket => _options.Bucket;

    private IAmazonS3 Client => _connection.Client;

    private S3Compatibility Features => _connection.Compatibility;

    public Task<Result<FileReference>> UploadAsync(
        string key,
        Stream content,
        FileUploadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        options ??= new FileUploadOptions();

        if ((CheckKey(key)
            ?? StorageValidation.ValidateUpload(options)
            ?? RequireCondition(options.Condition)
            ?? Require(options.ChecksumSha256 is null || Features.Sha256Checksums, "SHA-256 checksums")
            ?? Require(options.Tags is not { Count: > 0 } || Features.ObjectTags, "object tags")
            ?? RequireEncryption()
            ?? (options.ChecksumSha256 is not null && !content.CanSeek && options.ContentLength is null
                ? StorageErrors.InvalidRequest(
                    "ChecksumSha256 on a stream that cannot report its length also needs ContentLength (for a request body, Request.ContentLength).")
                : null)) is Error invalid)
        {
            return Task.FromResult(Result<FileReference>.Failure(invalid));
        }

        long? knownLength = options.ContentLength ?? (content.CanSeek ? content.Length - content.Position : null);

        return RunAsync(
            OperationUpload,
            async () =>
            {
                // A caller-supplied SHA-256 covers the whole object, which only a single PUT can verify:
                // a multipart upload's checksum is a checksum of part checksums.
                // Content of known length that fits in one part goes in a single PUT as well.
                bool singleRequest = options.ChecksumSha256 is not null
                    || (options.ContentLength is { } length && length <= _options.MultipartPartSize);
                (string? eTag, string? versionId) = singleRequest
                    ? await PutAsync(key, content, options, knownLength, cancellationToken).ConfigureAwait(false)
                    : await TransferAsync(key, content, options, cancellationToken).ConfigureAwait(false);

                StorageTelemetry.RecordBytes(StoreName, _connection.Name, StorageTelemetry.Upload, knownLength ?? 0);
                return Result<FileReference>.Success(Reference(this, key, eTag, versionId));
            },
            ex => MapServiceError(ex, OperationUpload, key, options.Condition),
            cancellationToken);
    }

    public Task<Result<FileDownload>> DownloadAsync(
        string key,
        FileDownloadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new FileDownloadOptions();
        if (CheckKey(key) is Error invalid)
        {
            return Task.FromResult(Result<FileDownload>.Failure(invalid));
        }

        var request = new GetObjectRequest
        {
            BucketName = Bucket,
            Key = Full(key),
            VersionId = options.VersionId,
            EtagToMatch = Quote(options.IfMatch),
            ExpectedBucketOwner = _options.ExpectedBucketOwner,
        };

        // Where an ETag is not the content's MD5 (OBS with encryption), the SDK's legacy MD5 check fails every full
        // download; a ranged request for the whole object returns the same bytes without that check.
        bool wholeObjectAsRange = options.Range is null && !Features.ETagIsContentMd5;
        if (options.Range is { } range)
        {
            request.ByteRange = range.To is { } to ? new S3ByteRange(range.From, to) : new S3ByteRange(range.ToString());
        }
        else if (wholeObjectAsRange)
        {
            request.ByteRange = new S3ByteRange(new ByteRange(0).ToString());
        }
        else if (Features.Sha256Checksums)
        {
            // Lets the SDK verify a stored full-object checksum while the stream is read.
            request.ChecksumMode = ChecksumMode.ENABLED;
        }

        return RunAsync(
            OperationDownload,
            async () =>
            {
                GetObjectResponse response = await Client.GetObjectAsync(request, cancellationToken).ConfigureAwait(false);
                (ByteRange? returned, long? total) = ParseContentRange(response.ContentRange);

                var properties = new FileProperties
                {
                    Key = Relative(response.Key ?? request.Key),
                    ContentLength = total ?? response.ContentLength,
                    ContentType = NullIfEmpty(response.Headers.ContentType),
                    LastModified = ToUtc(response.LastModified),
                    ETag = response.ETag,
                    VersionId = response.VersionId,
                    CacheControl = NullIfEmpty(response.Headers.CacheControl),
                    ContentDisposition = NullIfEmpty(response.Headers.ContentDisposition),
                    ContentEncoding = NullIfEmpty(response.Headers.ContentEncoding),
                    ChecksumSha256 = NullIfEmpty(response.ChecksumSHA256),
                    Tier = ToTier(response.StorageClass),
                    Metadata = ToMetadata(response.Metadata),
                };

                StorageTelemetry.RecordBytes(StoreName, _connection.Name, StorageTelemetry.Download, response.ContentLength);
                return Result<FileDownload>.Success(
                    new FileDownload(response.ResponseStream, properties, response.ContentLength, wholeObjectAsRange ? null : returned));
            },
            ex => wholeObjectAsRange && ex.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable
                ? null
                : MapServiceError(ex, OperationDownload, key, options.IfMatch is null ? null : WriteCondition.IfMatch(options.IfMatch)),
            cancellationToken,
            // "bytes=0-" of an empty object is 416: it exists and has no content.
            onNotMapped: () => Result<FileDownload>.Success(
                new FileDownload(Stream.Null, new FileProperties { Key = key, ContentLength = 0 }, 0)));
    }

    public Task<Result<FileProperties>> GetPropertiesAsync(string key, CancellationToken cancellationToken = default)
    {
        if (CheckKey(key) is Error invalid)
        {
            return Task.FromResult(Result<FileProperties>.Failure(invalid));
        }

        return RunAsync(
            OperationProperties,
            async () => Result<FileProperties>.Success(await HeadAsync(key, eTag: null, cancellationToken).ConfigureAwait(false)),
            ex => MapServiceError(ex, OperationProperties, key),
            cancellationToken);
    }

    public Task<Result<bool>> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        if (CheckKey(key) is Error invalid)
        {
            return Task.FromResult(Result<bool>.Failure(invalid));
        }

        return RunAsync(
            OperationExists,
            async () =>
            {
                await Client.GetObjectMetadataAsync(
                        new GetObjectMetadataRequest { BucketName = Bucket, Key = Full(key), ExpectedBucketOwner = _options.ExpectedBucketOwner },
                        cancellationToken)
                    .ConfigureAwait(false);
                return Result<bool>.Success(true);
            },
            ex => IsMissingObject(ex) ? null : MapServiceError(ex, OperationExists, key),
            cancellationToken,
            onNotMapped: () => Result<bool>.Success(false));
    }

    public async Task<Result> DeleteAsync(string key, FileDeleteOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new FileDeleteOptions();
        if ((CheckKey(key) ?? Require(options.IfMatch is null || Features.ConditionalWrites, "conditional deletes")) is Error invalid)
        {
            return invalid;
        }

        bool conditional = options.IfMatch is not null;
        Result<bool> result = await RunAsync(
                OperationDelete,
                async () =>
                {
                    await Client.DeleteObjectAsync(
                            new DeleteObjectRequest
                            {
                                BucketName = Bucket,
                                Key = Full(key),
                                VersionId = options.VersionId,
                                IfMatch = Quote(options.IfMatch),
                                ExpectedBucketOwner = _options.ExpectedBucketOwner,
                            },
                            cancellationToken)
                        .ConfigureAwait(false);
                    return Result<bool>.Success(true);
                },
                // Deleting a missing object succeeds, unless the caller required a specific version of it.
                ex => IsMissingObject(ex)
                    ? conditional ? StorageErrors.PreconditionFailed(StoreName, key) : null
                    : MapServiceError(ex, OperationDelete, key, conditional ? WriteCondition.IfMatch(options.IfMatch!) : null),
                cancellationToken,
                onNotMapped: () => Result<bool>.Success(true))
            .ConfigureAwait(false);

        return result.IsSuccess ? Result.Success() : Result.Failure(result.Error);
    }

    public async Task<Result<BatchDeleteResult>> DeleteManyAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);

        string[] distinct = [.. keys.Distinct(StringComparer.Ordinal)];
        foreach (string key in distinct)
        {
            if (CheckKey(key) is Error invalid)
            {
                return invalid;
            }
        }

        var deleted = new List<string>(distinct.Length);
        var failed = new List<FileDeleteFailure>();

        foreach (string[] batch in distinct.Chunk(MaxDeleteBatch))
        {
            if (failed.Count > 0 && failed[^1].Error.Code == StorageErrorCodes.Unavailable)
            {
                // The provider is down: report the rest without sending more requests.
                failed.AddRange(batch.Select(k => new FileDeleteFailure(k, failed[^1].Error)));
                continue;
            }

            Result<bool> result = await RunAsync(
                    OperationDeleteMany,
                    async () =>
                    {
                        DeleteObjectsResponse response;
                        try
                        {
                            response = await Client.DeleteObjectsAsync(
                                    new DeleteObjectsRequest
                                    {
                                        BucketName = Bucket,
                                        Objects = [.. batch.Select(k => new KeyVersion { Key = Full(k) })],
                                        ExpectedBucketOwner = _options.ExpectedBucketOwner,
                                    },
                                    cancellationToken)
                                .ConfigureAwait(false);
                        }
                        catch (DeleteObjectsException partial)
                        {
                            response = partial.Response;
                        }

                        // Some S3-compatible services return null lists instead of empty ones.
                        deleted.AddRange((response.DeletedObjects ?? []).Select(d => Relative(d.Key)));
                        foreach (DeleteError error in response.DeleteErrors ?? [])
                        {
                            string errorKey = Relative(error.Key);
                            if (string.Equals(error.Code, S3ErrorCodes.NoSuchKey, StringComparison.Ordinal))
                            {
                                deleted.Add(errorKey);
                            }
                            else
                            {
                                failed.Add(new FileDeleteFailure(errorKey, MapDeleteError(error, errorKey)));
                            }
                        }

                        return Result<bool>.Success(true);
                    },
                    ex => MapServiceError(ex, OperationDeleteMany, key: batch[0]),
                    cancellationToken)
                .ConfigureAwait(false);

            if (result.IsFailure)
            {
                Error batchError = result.Error.Code == StorageErrorCodes.NotFound
                    ? StorageErrors.ProviderError(StoreName, OperationDeleteMany)
                    : result.Error;
                failed.AddRange(batch.Select(k => new FileDeleteFailure(k, batchError)));
            }
        }

        return new BatchDeleteResult { Deleted = deleted, Failed = failed };
    }

    public Task<Result<FileReference>> CopyAsync(
        string sourceKey,
        string destinationKey,
        FileCopyOptions? options = null,
        CancellationToken cancellationToken = default) =>
        CopyToAsync(sourceKey, this, destinationKey, options, cancellationToken);

    public async Task<Result<FileReference>> CopyToAsync(
        string sourceKey,
        IFileStorage destination,
        string destinationKey,
        FileCopyOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        options ??= new FileCopyOptions();

        if ((CheckKey(sourceKey)
            ?? StorageValidation.ValidateCopy(options)
            ?? (destination as S3FileStorage)?.RequireCondition(options.Condition)) is Error invalid)
        {
            return invalid;
        }

        // A destination condition is enforced through a conditional PUT: several S3-compatible services
        // (MinIO among them) ignore If-None-Match on a server-side copy and silently overwrite.
        return destination is S3FileStorage target && ReferenceEquals(target._connection, _connection) && options.Condition is null
            ? await ServerSideCopyAsync(sourceKey, target, destinationKey, options, cancellationToken).ConfigureAwait(false)
            : await StreamCopyAsync(sourceKey, destination, destinationKey, options, cancellationToken).ConfigureAwait(false);
    }

    public IAsyncEnumerable<FileListItem> ListAsync(string prefix = "", CancellationToken cancellationToken = default)
    {
        if (StorageValidation.ValidatePrefix(prefix) is Error invalid)
        {
            throw new StorageException(invalid);
        }

        return ListCoreAsync(prefix, cancellationToken);
    }

    public Task<Result<FileListPage>> ListPageAsync(FileListRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (StorageValidation.ValidateList(request) is Error invalid)
        {
            return Task.FromResult(Result<FileListPage>.Failure(invalid));
        }

        return RunAsync(
            OperationList,
            async () =>
            {
                ListObjectsV2Response response = await Client.ListObjectsV2Async(
                        new ListObjectsV2Request
                        {
                            BucketName = Bucket,
                            Prefix = Full(request.Prefix),
                            Delimiter = request.Recursive ? null : "/",
                            MaxKeys = request.PageSize,
                            ContinuationToken = request.ContinuationToken,
                            ExpectedBucketOwner = _options.ExpectedBucketOwner,
                        },
                        cancellationToken)
                    .ConfigureAwait(false);

                return Result<FileListPage>.Success(new FileListPage
                {
                    Items = [.. (response.S3Objects ?? []).Select(ToListItem)],
                    Folders = [.. (response.CommonPrefixes ?? []).Select(Relative)],
                    ContinuationToken = response.IsTruncated == true ? response.NextContinuationToken : null,
                });
            },
            ex => MapServiceError(ex, OperationList, request.Prefix),
            cancellationToken);
    }

    public Task<Result<PresignedRequest>> CreateDownloadUrlAsync(
        string key,
        PresignedDownloadOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if ((CheckKey(key) ?? StorageValidation.ValidatePresignedDownload(options, _options.MaxPresignExpiry)) is Error invalid)
        {
            return Task.FromResult(Result<PresignedRequest>.Failure(invalid));
        }

        DateTimeOffset expiresAt = _clock.UtcNow + options.Expiry;
        var request = new GetPreSignedUrlRequest
        {
            BucketName = Bucket,
            Key = Full(key),
            Verb = HttpVerb.GET,
            Expires = expiresAt.UtcDateTime,
            Protocol = Protocol,
            VersionId = options.VersionId,
        };
        request.ResponseHeaderOverrides.ContentDisposition = options.ContentDisposition;
        request.ResponseHeaderOverrides.ContentType = options.ContentType;

        return PresignAsync(request, key, expiresAt, new Dictionary<string, string>(), cancellationToken);
    }

    public Task<Result<PresignedRequest>> CreateUploadUrlAsync(
        string key,
        PresignedUploadOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if ((CheckKey(key)
            ?? StorageValidation.ValidatePresignedUpload(options, _options.MaxPresignExpiry)
            ?? Require(!options.CreateOnly || Features.ConditionalWrites, "create-only uploads")
            ?? Require(options.ChecksumSha256 is null || Features.Sha256Checksums, "SHA-256 checksums")
            ?? RequireEncryption()) is Error invalid)
        {
            return Task.FromResult(Result<PresignedRequest>.Failure(invalid));
        }

        DateTimeOffset expiresAt = _clock.UtcNow + options.Expiry;
        var request = new GetPreSignedUrlRequest
        {
            BucketName = Bucket,
            Key = Full(key),
            Verb = HttpVerb.PUT,
            Expires = expiresAt.UtcDateTime,
            Protocol = Protocol,
            ContentType = options.ContentType,
        };

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [S3HeaderNames.ContentType] = options.ContentType,
        };

        if (options.CreateOnly)
        {
            request.Headers[S3HeaderNames.IfNoneMatch] = S3HeaderNames.AnyETag;
            headers[S3HeaderNames.IfNoneMatch] = S3HeaderNames.AnyETag;
        }

        if (options.ChecksumSha256 is { } checksum)
        {
            request.Headers[S3HeaderNames.ChecksumSha256] = checksum;
            headers[S3HeaderNames.ChecksumSha256] = checksum;
        }

        foreach ((string name, string value) in options.Metadata ?? NoMetadata)
        {
            request.Metadata.Add(name, value);
            headers[S3HeaderNames.MetadataPrefix + name.ToLowerInvariant()] = value;
        }

        foreach ((string name, string value) in EncryptionHeaders())
        {
            request.Headers[name] = value;
            headers[name] = value;
        }

        if (StorageClassFor(StorageTier.Default) is { } storageClass)
        {
            request.Headers[S3HeaderNames.StorageClass] = storageClass.Value;
            headers[S3HeaderNames.StorageClass] = storageClass.Value;
        }

        return PresignAsync(request, key, expiresAt, headers, cancellationToken);
    }

    public Task<Result<PresignedPost>> CreateUploadFormAsync(
        string key,
        PresignedPostOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if ((CheckKey(key)
            ?? StorageValidation.ValidatePresignedPost(options, _options.MaxPresignExpiry)
            ?? Require(Features.PresignedPost, "presigned POST uploads")
            ?? RequireEncryption()) is Error invalid)
        {
            return Task.FromResult(Result<PresignedPost>.Failure(invalid));
        }

        DateTimeOffset expiresAt = _clock.UtcNow + options.Expiry;
        var request = new CreatePresignedPostRequest
        {
            BucketName = Bucket,
            Key = Full(key),
            Expires = expiresAt.UtcDateTime,
            Fields = [],
            Conditions = [S3PostCondition.ContentLengthRange(options.MinSize, options.MaxSize)],
        };

        if (options.ContentType.EndsWith('/'))
        {
            request.Conditions.Add(S3PostCondition.StartsWith(S3HeaderNames.ContentType, options.ContentType));
        }
        else
        {
            AddPostField(request, S3HeaderNames.ContentType, options.ContentType);
        }

        foreach ((string name, string value) in options.Metadata ?? NoMetadata)
        {
            AddPostField(request, S3HeaderNames.MetadataPrefix + name.ToLowerInvariant(), value);
        }

        foreach ((string name, string value) in EncryptionHeaders())
        {
            AddPostField(request, name, value);
        }

        if (StorageClassFor(StorageTier.Default) is { } storageClass)
        {
            AddPostField(request, S3HeaderNames.StorageClass, storageClass.Value);
        }

        return RunAsync(
            OperationPresign,
            async () =>
            {
                CreatePresignedPostResponse response = await Client.CreatePresignedPostAsync(request).ConfigureAwait(false);
                return Result<PresignedPost>.Success(new PresignedPost
                {
                    Url = new Uri(response.Url),
                    Fields = new Dictionary<string, string>(response.Fields, StringComparer.Ordinal),
                    ExpiresAt = expiresAt,
                });
            },
            ex => MapServiceError(ex, OperationPresign, key),
            cancellationToken);
    }

    public Task<Result<MultipartUpload>> StartMultipartUploadAsync(
        string key,
        MultipartUploadOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new MultipartUploadOptions();
        if ((CheckKey(key)
            ?? StorageValidation.ValidateMultipart(options)
            ?? Require(options.Tags is not { Count: > 0 } || Features.ObjectTags, "object tags")
            ?? RequireEncryption()) is Error invalid)
        {
            return Task.FromResult(Result<MultipartUpload>.Failure(invalid));
        }

        var request = new InitiateMultipartUploadRequest
        {
            BucketName = Bucket,
            Key = Full(key),
            ContentType = options.ContentType ?? DefaultContentType,
            StorageClass = StorageClassFor(options.Tier),
            TagSet = ToTags(options.Tags),
            ExpectedBucketOwner = _options.ExpectedBucketOwner,
        };
        request.Headers.CacheControl = options.CacheControl;
        request.Headers.ContentDisposition = options.ContentDisposition;
        AddMetadata(request.Metadata, options.Metadata);
        (request.ServerSideEncryptionMethod, request.ServerSideEncryptionKeyManagementServiceKeyId) = Encryption();

        return RunAsync(
            OperationMultipart,
            async () =>
            {
                InitiateMultipartUploadResponse response = await Client
                    .InitiateMultipartUploadAsync(request, cancellationToken)
                    .ConfigureAwait(false);
                return Result<MultipartUpload>.Success(new MultipartUpload(key, response.UploadId));
            },
            ex => MapServiceError(ex, OperationMultipart, key),
            cancellationToken);
    }

    public Task<Result<PresignedRequest>> CreateUploadPartUrlAsync(
        MultipartUpload upload,
        int partNumber,
        TimeSpan expiry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(upload);
        if ((CheckKey(upload.Key)
            ?? StorageValidation.ValidatePartNumber(partNumber)
            ?? StorageValidation.ValidateExpiry(expiry, _options.MaxPresignExpiry)) is Error invalid)
        {
            return Task.FromResult(Result<PresignedRequest>.Failure(invalid));
        }

        DateTimeOffset expiresAt = _clock.UtcNow + expiry;
        var request = new GetPreSignedUrlRequest
        {
            BucketName = Bucket,
            Key = Full(upload.Key),
            Verb = HttpVerb.PUT,
            Expires = expiresAt.UtcDateTime,
            Protocol = Protocol,
            UploadId = upload.UploadId,
            PartNumber = partNumber,
        };

        return PresignAsync(request, upload.Key, expiresAt, new Dictionary<string, string>(), cancellationToken);
    }

    public Task<Result<FileReference>> CompleteMultipartUploadAsync(
        MultipartUpload upload,
        IReadOnlyCollection<UploadedPart> parts,
        WriteCondition? condition = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(upload);
        ArgumentNullException.ThrowIfNull(parts);
        if ((CheckKey(upload.Key) ?? RequireCondition(condition)) is Error invalid)
        {
            return Task.FromResult(Result<FileReference>.Failure(invalid));
        }

        var request = new CompleteMultipartUploadRequest
        {
            BucketName = Bucket,
            Key = Full(upload.Key),
            UploadId = upload.UploadId,
            PartETags = [.. parts.OrderBy(p => p.PartNumber).Select(p => new PartETag(p.PartNumber, Quote(p.ETag)))],
            ExpectedBucketOwner = _options.ExpectedBucketOwner,
        };
        ApplyCondition(condition, eTag => request.IfMatch = eTag, () => request.IfNoneMatch = S3HeaderNames.AnyETag);

        return RunAsync(
            OperationMultipart,
            async () =>
            {
                CompleteMultipartUploadResponse response = await Client
                    .CompleteMultipartUploadAsync(request, cancellationToken)
                    .ConfigureAwait(false);
                return Result<FileReference>.Success(Reference(this, upload.Key, response.ETag, response.VersionId));
            },
            ex => ex.ErrorCode is S3ErrorCodes.InvalidPart or S3ErrorCodes.InvalidPartOrder or S3ErrorCodes.EntityTooSmall
                ? StorageErrors.InvalidRequest(
                    "The parts do not complete the upload: every part must be uploaded with the ETag given, and every part but the last must be at least 5 MiB.")
                : MapServiceError(ex, OperationMultipart, upload.Key, condition),
            cancellationToken);
    }

    public async Task<Result> AbortMultipartUploadAsync(MultipartUpload upload, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(upload);
        if (CheckKey(upload.Key) is Error invalid)
        {
            return invalid;
        }

        Result<bool> result = await RunAsync(
                OperationMultipart,
                async () =>
                {
                    await Client.AbortMultipartUploadAsync(
                            new AbortMultipartUploadRequest
                            {
                                BucketName = Bucket,
                                Key = Full(upload.Key),
                                UploadId = upload.UploadId,
                                ExpectedBucketOwner = _options.ExpectedBucketOwner,
                            },
                            cancellationToken)
                        .ConfigureAwait(false);
                    return Result<bool>.Success(true);
                },
                // An upload that is already gone needs no aborting.
                ex => IsMissingObject(ex) ? null : MapServiceError(ex, OperationMultipart, upload.Key),
                cancellationToken,
                onNotMapped: () => Result<bool>.Success(true))
            .ConfigureAwait(false);

        return result.IsSuccess ? Result.Success() : Result.Failure(result.Error);
    }

    /// <summary>Checks the bucket is reachable with the configured credentials: one <c>HEAD</c> on the bucket.</summary>
    internal async Task<Result> ProbeAsync(CancellationToken cancellationToken)
    {
        long start = Stopwatch.GetTimestamp();
        using Activity? activity = StorageTelemetry.StartActivity(OperationProbe, StoreName, _connection.Name);
        Error? error = null;
        try
        {
            await Client.HeadBucketAsync(
                    new HeadBucketRequest { BucketName = Bucket, ExpectedBucketOwner = _options.ExpectedBucketOwner },
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            S3StorageLog.ProbeFailed(_logger, ex, StoreName, Bucket);
            if (ex is AmazonServiceException redirect && IsRedirect(redirect.StatusCode))
            {
                S3StorageLog.WrongRegion(_logger, StoreName, Bucket, redirect.ErrorCode, redirect.RequestId);
            }

            error = ex is AmazonServiceException { StatusCode: HttpStatusCode.Forbidden }
                ? StorageErrors.AccessDenied(StoreName)
                : StorageErrors.Unavailable(StoreName, OperationProbe);
        }

        StorageTelemetry.Complete(activity, OperationProbe, StoreName, _connection.Name, start, error?.Code);
        return error is null ? Result.Success() : Result.Failure(error);
    }

    private async Task<(string? ETag, string? VersionId)> TransferAsync(
        string key,
        Stream content,
        FileUploadOptions options,
        CancellationToken cancellationToken)
    {
        var request = new TransferUtilityUploadRequest
        {
            BucketName = Bucket,
            Key = Full(key),
            InputStream = content,
            AutoCloseStream = false,
            AutoResetStreamPosition = false,
            PartSize = _options.MultipartPartSize,
            ContentType = options.ContentType ?? DefaultContentType,
            StorageClass = StorageClassFor(options.Tier),
            TagSet = ToTags(options.Tags),
            ExpectedBucketOwner = _options.ExpectedBucketOwner,
        };

        // The SDK cannot attach part checksums to a multipart upload of unknown length, so a stored SHA-256
        // is only requested when the length is known.
        if (Features.Sha256Checksums && content.CanSeek)
        {
            request.ChecksumAlgorithm = ChecksumAlgorithm.SHA256;
        }

        request.Headers.CacheControl = options.CacheControl;
        request.Headers.ContentDisposition = options.ContentDisposition;
        request.Headers.ContentEncoding = options.ContentEncoding;
        AddMetadata(request.Metadata, options.Metadata);
        (request.ServerSideEncryptionMethod, request.ServerSideEncryptionKeyManagementServiceKeyId) = Encryption();
        ApplyCondition(options.Condition, eTag => request.IfMatch = eTag, () => request.IfNoneMatch = S3HeaderNames.AnyETag);

        TransferUtilityUploadResponse response = await _connection.TransferUtility
            .UploadWithResponseAsync(request, cancellationToken)
            .ConfigureAwait(false);
        return (response.ETag, response.VersionId);
    }

    private async Task<(string? ETag, string? VersionId)> PutAsync(
        string key,
        Stream content,
        FileUploadOptions options,
        long? contentLength,
        CancellationToken cancellationToken)
    {
        var request = new PutObjectRequest
        {
            BucketName = Bucket,
            Key = Full(key),
            InputStream = content,
            AutoCloseStream = false,
            AutoResetStreamPosition = false,
            ContentType = options.ContentType ?? DefaultContentType,
            ChecksumSHA256 = options.ChecksumSha256,
            StorageClass = StorageClassFor(options.Tier),
            TagSet = ToTags(options.Tags),
            ExpectedBucketOwner = _options.ExpectedBucketOwner,
        };

        if (!content.CanSeek && contentLength is { } length)
        {
            request.Headers.ContentLength = length;
        }

        if (options.ChecksumSha256 is null && Features.Sha256Checksums && content.CanSeek)
        {
            request.ChecksumAlgorithm = ChecksumAlgorithm.SHA256;
        }

        request.Headers.CacheControl = options.CacheControl;
        request.Headers.ContentDisposition = options.ContentDisposition;
        request.Headers.ContentEncoding = options.ContentEncoding;
        AddMetadata(request.Metadata, options.Metadata);
        (request.ServerSideEncryptionMethod, request.ServerSideEncryptionKeyManagementServiceKeyId) = Encryption();
        ApplyCondition(options.Condition, eTag => request.IfMatch = eTag, () => request.IfNoneMatch = S3HeaderNames.AnyETag);

        PutObjectResponse response = await Client.PutObjectAsync(request, cancellationToken).ConfigureAwait(false);
        return (response.ETag, response.VersionId);
    }

    private async Task<FileProperties> HeadAsync(string key, string? eTag, CancellationToken cancellationToken)
    {
        var request = new GetObjectMetadataRequest
        {
            BucketName = Bucket,
            Key = Full(key),
            EtagToMatch = Quote(eTag),
            ExpectedBucketOwner = _options.ExpectedBucketOwner,
        };

        if (Features.Sha256Checksums)
        {
            request.ChecksumMode = ChecksumMode.ENABLED;
        }

        GetObjectMetadataResponse response = await Client.GetObjectMetadataAsync(request, cancellationToken).ConfigureAwait(false);
        return new FileProperties
        {
            Key = key,
            ContentLength = response.ContentLength,
            ContentType = NullIfEmpty(response.Headers.ContentType),
            LastModified = ToUtc(response.LastModified),
            ETag = response.ETag,
            VersionId = response.VersionId,
            CacheControl = NullIfEmpty(response.Headers.CacheControl),
            ContentDisposition = NullIfEmpty(response.Headers.ContentDisposition),
            ContentEncoding = NullIfEmpty(response.Headers.ContentEncoding),
            ChecksumSha256 = NullIfEmpty(response.ChecksumSHA256),
            Tier = ToTier(response.StorageClass),
            Metadata = ToMetadata(response.Metadata),
        };
    }

    private Task<Result<FileReference>> ServerSideCopyAsync(
        string sourceKey,
        S3FileStorage target,
        string destinationKey,
        FileCopyOptions options,
        CancellationToken cancellationToken)
    {
        if ((target.CheckKey(destinationKey) ?? target.RequireEncryption()) is Error invalid)
        {
            return Task.FromResult(Result<FileReference>.Failure(invalid));
        }

        bool replace = options.ContentType is not null || options.Metadata is not null;
        return RunAsync(
            OperationCopy,
            async () =>
            {
                var request = new CopyObjectRequest
                {
                    SourceBucket = Bucket,
                    SourceKey = Full(sourceKey),
                    DestinationBucket = target.Bucket,
                    DestinationKey = target.Full(destinationKey),
                    ETagToMatch = Quote(options.SourceIfMatch),
                    StorageClass = target.StorageClassFor(options.Tier),
                    ExpectedSourceBucketOwner = _options.ExpectedBucketOwner,
                    ExpectedBucketOwner = target._options.ExpectedBucketOwner,
                };
                (request.ServerSideEncryptionMethod, request.ServerSideEncryptionKeyManagementServiceKeyId) = target.Encryption();

                if (replace)
                {
                    // REPLACE drops every stored header and metadata entry not sent again: carry the source's
                    // values over for whatever the caller did not replace.
                    FileProperties source = await HeadAsync(sourceKey, options.SourceIfMatch, cancellationToken).ConfigureAwait(false);
                    request.MetadataDirective = S3MetadataDirective.REPLACE;
                    request.ContentType = options.ContentType ?? source.ContentType ?? DefaultContentType;
                    request.Headers.CacheControl = source.CacheControl;
                    request.Headers.ContentDisposition = source.ContentDisposition;
                    request.Headers.ContentEncoding = source.ContentEncoding;
                    AddMetadata(request.Metadata, options.Metadata ?? source.Metadata);
                }

                CopyObjectResponse response = await Client.CopyObjectAsync(request, cancellationToken).ConfigureAwait(false);
                return Result<FileReference>.Success(Reference(target, destinationKey, response.ETag, response.VersionId));
            },
            ex => MapServiceError(ex, OperationCopy, sourceKey),
            cancellationToken);
    }

    private async Task<Result<FileReference>> StreamCopyAsync(
        string sourceKey,
        IFileStorage destination,
        string destinationKey,
        FileCopyOptions options,
        CancellationToken cancellationToken)
    {
        Result<FileDownload> download = await DownloadAsync(
                sourceKey,
                new FileDownloadOptions { IfMatch = options.SourceIfMatch },
                cancellationToken)
            .ConfigureAwait(false);
        if (download.IsFailure)
        {
            return download.Error;
        }

        await using FileDownload source = download.Value;
        FileProperties properties = source.Properties;
        return await destination.UploadAsync(
                destinationKey,
                source.Content,
                new FileUploadOptions
                {
                    ContentType = options.ContentType ?? properties.ContentType,
                    CacheControl = properties.CacheControl,
                    ContentDisposition = properties.ContentDisposition,
                    ContentEncoding = properties.ContentEncoding,
                    Metadata = options.Metadata ?? properties.Metadata,
                    Tier = options.Tier,
                    Condition = options.Condition,
                },
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async IAsyncEnumerable<FileListItem> ListCoreAsync(string prefix, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string? continuationToken = null;
        do
        {
            Result<FileListPage> page = await ListPageAsync(
                    new FileListRequest { Prefix = prefix, ContinuationToken = continuationToken },
                    cancellationToken)
                .ConfigureAwait(false);
            if (page.IsFailure)
            {
                throw new StorageException(page.Error);
            }

            foreach (FileListItem item in page.Value.Items)
            {
                yield return item;
            }

            continuationToken = page.Value.ContinuationToken;
        }
        while (continuationToken is not null);
    }

    private Task<Result<PresignedRequest>> PresignAsync(
        GetPreSignedUrlRequest request,
        string key,
        DateTimeOffset expiresAt,
        Dictionary<string, string> headers,
        CancellationToken cancellationToken) =>
        RunAsync(
            OperationPresign,
            async () =>
            {
                string url = await Client.GetPreSignedURLAsync(request).ConfigureAwait(false);
                return Result<PresignedRequest>.Success(new PresignedRequest
                {
                    Url = new Uri(url),
                    Method = request.Verb.ToString(),
                    Headers = headers,
                    ExpiresAt = expiresAt,
                });
            },
            ex => MapServiceError(ex, OperationPresign, key),
            cancellationToken);

    /// <summary>
    /// Runs one provider call: records a span and a duration, and turns provider exceptions into storage
    /// errors. Cancellation requested by the caller propagates.
    /// </summary>
    /// A mapping that returns <see langword="null"/> means the rejection is an expected outcome: <c>onNotMapped</c> supplies the result.
    private async Task<Result<T>> RunAsync<T>(
        string operation,
        Func<Task<Result<T>>> action,
        Func<AmazonServiceException, Error?> mapServiceError,
        CancellationToken cancellationToken,
        Func<Result<T>>? onNotMapped = null)
    {
        long start = Stopwatch.GetTimestamp();
        using Activity? activity = StorageTelemetry.StartActivity(operation, StoreName, _connection.Name);

        Result<T> result;
        try
        {
            result = await action().ConfigureAwait(false);
        }
        catch (AmazonServiceException ex)
        {
            result = mapServiceError(ex) is Error error ? Result<T>.Failure(error) : onNotMapped!();
        }
        catch (Exception ex) when (IsTransportFailure(ex) && !cancellationToken.IsCancellationRequested)
        {
            S3StorageLog.Unavailable(_logger, ex, StoreName, Bucket, operation, statusCode: null, errorCode: null, requestId: null);
            result = StorageErrors.Unavailable(StoreName, operation);
        }

        StorageTelemetry.Complete(activity, operation, StoreName, _connection.Name, start, result.IsFailure ? result.Error.Code : null);
        return result;
    }

    /// <summary>A 404 for the object (or upload) itself — never for a missing bucket, which is a configuration error.</summary>
    private static bool IsMissingObject(AmazonServiceException ex) =>
        ex.StatusCode == HttpStatusCode.NotFound && ex.ErrorCode != S3ErrorCodes.NoSuchBucket;

    private static bool IsRedirect(HttpStatusCode status) =>
        status is HttpStatusCode.MovedPermanently or HttpStatusCode.Redirect or HttpStatusCode.TemporaryRedirect;

    private static bool IsTransportFailure(Exception ex) =>
        ex is AmazonClientException or HttpRequestException or IOException or TimeoutException or OperationCanceledException;

    private Error MapServiceError(AmazonServiceException ex, string operation, string key, WriteCondition? condition = null)
    {
        HttpStatusCode status = ex.StatusCode;
        string? code = ex.ErrorCode;

        Error? expected = status switch
        {
            HttpStatusCode.NotFound when code != S3ErrorCodes.NoSuchBucket => StorageErrors.NotFound(StoreName, key),
            HttpStatusCode.PreconditionFailed => condition?.MustNotExist == true
                ? StorageErrors.AlreadyExists(StoreName, key)
                : StorageErrors.PreconditionFailed(StoreName, key),
            HttpStatusCode.Conflict when code == S3ErrorCodes.ConditionalRequestConflict => condition?.MustNotExist == true
                ? StorageErrors.AlreadyExists(StoreName, key)
                : StorageErrors.PreconditionFailed(StoreName, key),
            HttpStatusCode.RequestedRangeNotSatisfiable => StorageErrors.InvalidRange(StoreName, key),
            HttpStatusCode.BadRequest when code is S3ErrorCodes.BadDigest or S3ErrorCodes.InvalidDigest
                or S3ErrorCodes.ChecksumMismatch or S3ErrorCodes.Sha256Mismatch => StorageErrors.ChecksumMismatch(StoreName, key),
            _ => null,
        };

        if (expected is not null)
        {
            S3StorageLog.ExpectedFailure(_logger, StoreName, Bucket, operation, status, code);
            return expected;
        }

        if (IsRedirect(status) || code is S3ErrorCodes.PermanentRedirect or S3ErrorCodes.AuthorizationHeaderMalformed)
        {
            S3StorageLog.WrongRegion(_logger, StoreName, Bucket, code, ex.RequestId);
            return StorageErrors.ProviderError(StoreName, operation);
        }

        if (status == HttpStatusCode.Forbidden)
        {
            S3StorageLog.AccessDenied(_logger, StoreName, Bucket, operation, status, code, ex.RequestId);
            return StorageErrors.AccessDenied(StoreName, key);
        }

        if (status == HttpStatusCode.NotImplemented)
        {
            S3StorageLog.ProviderError(_logger, ex, StoreName, Bucket, operation, status, code, ex.RequestId);
            return StorageErrors.NotSupported(StoreName, $"this {operation} request");
        }

        if ((int)status >= 500 || status == HttpStatusCode.TooManyRequests || code is S3ErrorCodes.SlowDown or S3ErrorCodes.RequestTimeout)
        {
            S3StorageLog.Unavailable(_logger, ex, StoreName, Bucket, operation, status, code, ex.RequestId);
            return StorageErrors.Unavailable(StoreName, operation);
        }

        S3StorageLog.ProviderError(_logger, ex, StoreName, Bucket, operation, status, code, ex.RequestId);
        return StorageErrors.ProviderError(StoreName, operation);
    }

    private Error MapDeleteError(DeleteError error, string key) => error.Code switch
    {
        S3ErrorCodes.AccessDenied => StorageErrors.AccessDenied(StoreName, key),
        S3ErrorCodes.SlowDown or "InternalError" or "ServiceUnavailable" => StorageErrors.Unavailable(StoreName, OperationDeleteMany),
        _ => StorageErrors.ProviderError(StoreName, OperationDeleteMany),
    };

    private Error? CheckKey(string key) => StorageValidation.ValidateKey(key) ?? StorageValidation.ValidateKey(Full(key));

    private Error? Require(bool supported, string feature) => supported ? null : StorageErrors.NotSupported(StoreName, feature);

    private Error? RequireCondition(WriteCondition? condition) =>
        Require(condition is null || Features.ConditionalWrites, "conditional writes");

    private Error? RequireEncryption() =>
        Require(_options.Encryption != S3Encryption.Kms || Features.KmsEncryption, "SSE-KMS encryption");

    private (ServerSideEncryptionMethod? Method, string? KeyId) Encryption() => _options.Encryption switch
    {
        S3Encryption.S3Managed => (ServerSideEncryptionMethod.AES256, null),
        S3Encryption.Kms => (ServerSideEncryptionMethod.AWSKMS, _options.KmsKeyId),
        _ => (null, null),
    };

    private IEnumerable<KeyValuePair<string, string>> EncryptionHeaders()
    {
        (ServerSideEncryptionMethod? method, string? keyId) = Encryption();
        if (method is not null)
        {
            yield return new(S3HeaderNames.ServerSideEncryption, method.Value);
        }

        if (keyId is not null)
        {
            yield return new(S3HeaderNames.KmsKeyId, keyId);
        }
    }

    private S3StorageClass? StorageClassFor(StorageTier tier) =>
        (tier == StorageTier.Default ? _options.DefaultTier : tier) == StorageTier.InfrequentAccess
            ? S3StorageClass.StandardInfrequentAccess
            : null;

    private Protocol Protocol =>
        Client.Config.UseHttp || (Client.Config.ServiceURL?.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ?? false)
            ? Protocol.HTTP
            : Protocol.HTTPS;

    private string Full(string key) => _keyPrefix + key;

    private string Relative(string fullKey) =>
        _keyPrefix.Length != 0 && fullKey.StartsWith(_keyPrefix, StringComparison.Ordinal) ? fullKey[_keyPrefix.Length..] : fullKey;

    private FileListItem ToListItem(S3Object item) => new()
    {
        Key = Relative(item.Key),
        ContentLength = item.Size ?? 0,
        LastModified = ToUtc(item.LastModified),
        ETag = item.ETag,
    };

    private static FileReference Reference(S3FileStorage store, string key, string? eTag, string? versionId) => new()
    {
        Store = store.StoreName,
        Key = key,
        ETag = eTag,
        VersionId = versionId,
    };

    private static void ApplyCondition(WriteCondition? condition, Action<string> ifMatch, Action ifNoneMatch)
    {
        if (condition is null)
        {
            return;
        }

        if (condition.MustNotExist)
        {
            ifNoneMatch();
        }
        else
        {
            ifMatch(Quote(condition.ETag)!);
        }
    }

    private static void AddPostField(CreatePresignedPostRequest request, string name, string value)
    {
        request.Fields[name] = value;
        request.Conditions.Add(S3PostCondition.ExactMatch(name, value));
    }

    private static void AddMetadata(MetadataCollection target, IReadOnlyDictionary<string, string>? metadata)
    {
        foreach ((string name, string value) in metadata ?? NoMetadata)
        {
            target.Add(name.ToLowerInvariant(), value);
        }
    }

    private static List<Tag>? ToTags(IReadOnlyDictionary<string, string>? tags) =>
        tags is { Count: > 0 } ? [.. tags.Select(t => new Tag { Key = t.Key, Value = t.Value })] : null;

    private static IReadOnlyDictionary<string, string> ToMetadata(MetadataCollection metadata)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in metadata.Keys)
        {
            string key = name.StartsWith(S3HeaderNames.MetadataPrefix, StringComparison.OrdinalIgnoreCase)
                ? name[S3HeaderNames.MetadataPrefix.Length..]
                : name;
            result[key.ToLowerInvariant()] = metadata[name];
        }

        return result;
    }

    private static StorageTier ToTier(S3StorageClass? storageClass) =>
        storageClass == S3StorageClass.StandardInfrequentAccess ? StorageTier.InfrequentAccess : StorageTier.Default;

    private static string? Quote(string? eTag) =>
        eTag is null ? null : eTag.StartsWith('"') ? eTag : $"\"{eTag}\"";

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private static DateTimeOffset? ToUtc(DateTime? value) => value switch
    {
        null => null,
        { Kind: DateTimeKind.Local } local => new DateTimeOffset(local.ToUniversalTime(), TimeSpan.Zero),
        { } other => new DateTimeOffset(DateTime.SpecifyKind(other, DateTimeKind.Utc), TimeSpan.Zero),
    };

    /// <summary>Parses <c>bytes 0-99/1234</c> into the returned range and the object's total size.</summary>
    private static (ByteRange? Range, long? Total) ParseContentRange(string? contentRange)
    {
        if (string.IsNullOrEmpty(contentRange) || !contentRange.StartsWith("bytes ", StringComparison.OrdinalIgnoreCase))
        {
            return (null, null);
        }

        ReadOnlySpan<char> value = contentRange.AsSpan(6);
        int dash = value.IndexOf('-');
        int slash = value.IndexOf('/');
        if (dash <= 0 || slash <= dash
            || !long.TryParse(value[..dash], NumberStyles.None, CultureInfo.InvariantCulture, out long from)
            || !long.TryParse(value[(dash + 1)..slash], NumberStyles.None, CultureInfo.InvariantCulture, out long to))
        {
            return (null, null);
        }

        long? total = long.TryParse(value[(slash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out long size) ? size : null;
        return (new ByteRange(from, to), total);
    }
}
