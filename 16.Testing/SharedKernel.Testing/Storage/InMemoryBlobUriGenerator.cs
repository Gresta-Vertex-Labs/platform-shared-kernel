using System.Collections.Concurrent;
using SharedKernel.Primitives.Results;
using SharedKernel.Storage.Abstractions.Abstractions;
using SharedKernel.Storage.Abstractions.Errors;
using SharedKernel.Storage.Abstractions.Models;

namespace SharedKernel.Testing.Storage;

/// <summary>
/// In-memory test double for <see cref="IBlobUriGenerator"/>. Returns a deterministic, inspectable
/// URL encoding bucket/key/mode/expiry directly in the URL string, so a test can assert on URL
/// content without needing the recorded-list accessors.
/// </summary>
/// <remarks>
/// <see cref="PresignedUrl.ExpiresAt"/> is computed as a fixed internal non-real baseline instant
/// plus <see cref="PresignedUrlRequest.Expiry"/> — never <see cref="DateTimeOffset.UtcNow"/>-derived,
/// mirroring <see cref="InMemoryFileStorage"/>'s "never real time" philosophy, independently declared
/// (no <see cref="SharedKernel.Testing.Clocks"/> dependency, per the sibling-isolation rule). Honors
/// the same <see cref="StorageErrors.ExpiryTooLong"/> validation as the real provider contract (the
/// documented 7-day provider maximum) so a test asserting this error path behaves identically against
/// the fake and a real provider.
/// </remarks>
public sealed class InMemoryBlobUriGenerator : IBlobUriGenerator
{
    private static readonly DateTimeOffset FixedBaseline = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan MaxExpiry = TimeSpan.FromDays(7);

    private readonly ConcurrentQueue<PresignedUrlRequest> _uploadRequests = new();
    private readonly ConcurrentQueue<PresignedUrlRequest> _downloadRequests = new();

    /// <summary>Every request ever passed to <see cref="GeneratePresignedUploadUrl"/>.</summary>
    public IReadOnlyList<PresignedUrlRequest> GeneratedUploadUrls => _uploadRequests.ToArray();

    /// <summary>Every request ever passed to <see cref="GeneratePresignedDownloadUrl"/>.</summary>
    public IReadOnlyList<PresignedUrlRequest> GeneratedDownloadUrls => _downloadRequests.ToArray();

    /// <inheritdoc />
    public SharedKernel.Primitives.Results.Result<PresignedUrl> GeneratePresignedUploadUrl(PresignedUrlRequest request) =>
        Generate(request, "upload", _uploadRequests);

    /// <inheritdoc />
    public SharedKernel.Primitives.Results.Result<PresignedUrl> GeneratePresignedDownloadUrl(PresignedUrlRequest request) =>
        Generate(request, "download", _downloadRequests);

    private static SharedKernel.Primitives.Results.Result<PresignedUrl> Generate(
        PresignedUrlRequest request,
        string mode,
        ConcurrentQueue<PresignedUrlRequest> recorded)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Expiry > MaxExpiry)
        {
            return SharedKernel.Primitives.Results.Result<PresignedUrl>.Failure(StorageErrors.ExpiryTooLong(request.Expiry, MaxExpiry));
        }

        recorded.Enqueue(request);

        var url = new Uri(
            $"https://fake-storage.test/{request.Bucket}/{request.Key}?mode={mode}&expirySeconds={(int)request.Expiry.TotalSeconds}");

        return SharedKernel.Primitives.Results.Result<PresignedUrl>.Success(new PresignedUrl
        {
            Url = url,
            ExpiresAt = FixedBaseline + request.Expiry,
        });
    }
}
