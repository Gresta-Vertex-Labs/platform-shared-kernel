using SharedKernel.Primitives.Results;
using SharedKernel.Storage.Abstractions.Models;

namespace SharedKernel.Storage.Abstractions.Abstractions;

/// <summary>
/// Provider-agnostic contract for generating presigned upload/download URLs, letting a
/// browser/client transfer bytes directly with storage, bypassing the service process for large
/// payloads.
/// </summary>
/// <remarks>
/// Both members are synchronous — presigning is a local cryptographic operation against provider
/// credentials, with no network round-trip. Expiry is bounded by the provider's maximum (7 days for
/// S3-family signatures); requests exceeding it return <see cref="Errors.StorageErrors.ExpiryTooLong"/>.
/// </remarks>
public interface IBlobUriGenerator
{
    /// <summary>Generates a presigned URL a client can issue an HTTP PUT against to upload directly to storage.</summary>
    /// <param name="request">The bucket/key/expiry describing the desired upload URL.</param>
    /// <returns>
    /// The <see cref="PresignedUrl"/> on success, carrying its absolute <see cref="PresignedUrl.ExpiresAt"/>,
    /// or a <see cref="Errors.StorageErrors"/> failure.
    /// </returns>
    Result<PresignedUrl> GeneratePresignedUploadUrl(PresignedUrlRequest request);

    /// <summary>Generates a presigned URL a client can issue an HTTP GET against to download directly from storage.</summary>
    /// <param name="request">The bucket/key/expiry describing the desired download URL.</param>
    /// <returns>
    /// The <see cref="PresignedUrl"/> on success, carrying its absolute <see cref="PresignedUrl.ExpiresAt"/>,
    /// or a <see cref="Errors.StorageErrors"/> failure.
    /// </returns>
    Result<PresignedUrl> GeneratePresignedDownloadUrl(PresignedUrlRequest request);
}
