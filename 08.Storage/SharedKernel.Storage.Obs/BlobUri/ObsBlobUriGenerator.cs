using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Results;
using SharedKernel.Storage.Abstractions.Abstractions;
using SharedKernel.Storage.Abstractions.Errors;
using SharedKernel.Storage.Abstractions.Models;
using SharedKernel.Storage.Obs.Logging;

namespace SharedKernel.Storage.Obs.BlobUri;

/// <summary>
/// Huawei Cloud OBS implementation of <see cref="IBlobUriGenerator"/>, delegating to
/// <see cref="IAmazonS3"/>'s request presigning against the OBS S3-compatible endpoint.
/// </summary>
/// <remarks>Clamps <see cref="PresignedUrlRequest.Expiry"/> to the S3-family 7-day signature maximum.</remarks>
public sealed class ObsBlobUriGenerator : IBlobUriGenerator
{
    private static readonly TimeSpan MaxExpiry = TimeSpan.FromDays(7);

    private readonly IAmazonS3 _obs;
    private readonly IClock _clock;
    private readonly ILogger<ObsBlobUriGenerator> _logger;

    /// <summary>Initializes a new instance backed by <paramref name="obs"/>.</summary>
    /// <param name="obs">The shared, singleton-lifetime OBS-endpoint S3 client.</param>
    /// <param name="clock">Source of the current time used to derive the absolute expiry.</param>
    /// <param name="logger">Logger for structured provider-rejection recording.</param>
    public ObsBlobUriGenerator(IAmazonS3 obs, IClock clock, ILogger<ObsBlobUriGenerator> logger)
    {
        ArgumentNullException.ThrowIfNull(obs);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        _obs = obs;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public Result<PresignedUrl> GeneratePresignedUploadUrl(PresignedUrlRequest request) => Generate(request, HttpVerb.PUT);

    /// <inheritdoc />
    public Result<PresignedUrl> GeneratePresignedDownloadUrl(PresignedUrlRequest request) => Generate(request, HttpVerb.GET);

    private Result<PresignedUrl> Generate(PresignedUrlRequest request, HttpVerb verb)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Bucket))
        {
            return Result<PresignedUrl>.Failure(StorageErrors.InvalidBucket(request.Bucket));
        }

        if (string.IsNullOrWhiteSpace(request.Key))
        {
            return Result<PresignedUrl>.Failure(StorageErrors.InvalidKey(request.Key));
        }

        if (request.Expiry > MaxExpiry)
        {
            ObsStorageLog.ExpiryTooLong(_logger, request.Bucket, request.Key, request.Expiry, MaxExpiry);
            return Result<PresignedUrl>.Failure(StorageErrors.ExpiryTooLong(request.Expiry, MaxExpiry));
        }

        var expiresAt = _clock.UtcNow.Add(request.Expiry);

        var presignRequest = new GetPreSignedUrlRequest
        {
            BucketName = request.Bucket,
            Key = request.Key,
            Verb = verb,
            Expires = expiresAt.UtcDateTime,
            // GetPreSignedUrlRequest.Protocol defaults to HTTPS unconditionally — it does NOT derive
            // from the client's own ServiceURL/UseHttp configuration. Left unset, a plain-HTTP OBS
            // (or MinIO stand-in) deployment would receive an https:// presigned URL that fails the
            // TLS handshake against an HTTP-only listener. Deriving it from the client's own config
            // keeps this transparent to callers and correct for both HTTPS OBS endpoints and HTTP
            // test endpoints alike.
            Protocol = _obs.Config.UseHttp ? Protocol.HTTP : Protocol.HTTPS,
        };

        var url = _obs.GetPreSignedURL(presignRequest);

        return Result<PresignedUrl>.Success(new PresignedUrl { Url = new Uri(url), ExpiresAt = expiresAt });
    }
}
