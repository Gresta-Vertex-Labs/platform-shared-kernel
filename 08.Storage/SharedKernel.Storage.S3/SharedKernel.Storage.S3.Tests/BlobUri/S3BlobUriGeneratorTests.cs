using Amazon.S3;
using Amazon.S3.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharedKernel.Storage.Abstractions.Errors;
using SharedKernel.Storage.Abstractions.Models;
using SharedKernel.Storage.S3.BlobUri;
using SharedKernel.Testing.Clocks;

namespace SharedKernel.Storage.S3.Tests.BlobUri;

/// <summary>
/// T-11: <see cref="S3BlobUriGenerator"/> expiry-clamping tests. Presigning is a local
/// cryptographic operation with no network round-trip (per 08.Storage/CLAUDE.md), so these tests
/// never require a live S3-compatible endpoint — <see cref="IAmazonS3"/> is substituted purely to
/// avoid constructing a real client, and <see cref="IAmazonS3.GetPreSignedURL"/> is only invoked
/// (and only needs configuring) on the success path.
/// </summary>
public sealed class S3BlobUriGeneratorTests
{
    private static readonly TimeSpan MaxExpiry = TimeSpan.FromDays(7);

    [Fact]
    public void GeneratePresignedUploadUrl_ExpiryOverSevenDays_ReturnsExpiryTooLong()
    {
        var generator = CreateGenerator(out _);
        var requested = TimeSpan.FromDays(8);
        var request = new PresignedUrlRequest { Bucket = "bucket", Key = "key", Expiry = requested };

        var result = generator.GeneratePresignedUploadUrl(request);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StorageErrors.ExpiryTooLong(requested, MaxExpiry));
    }

    [Fact]
    public void GeneratePresignedDownloadUrl_ExpiryOverSevenDays_ReturnsExpiryTooLong()
    {
        var generator = CreateGenerator(out _);
        var requested = TimeSpan.FromDays(30);
        var request = new PresignedUrlRequest { Bucket = "bucket", Key = "key", Expiry = requested };

        var result = generator.GeneratePresignedDownloadUrl(request);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StorageErrors.ExpiryTooLong(requested, MaxExpiry));
    }

    [Fact]
    public void GeneratePresignedUploadUrl_ExpiryExactlySevenDays_Succeeds()
    {
        var generator = CreateGenerator(out var s3);
        s3.GetPreSignedURL(Arg.Any<GetPreSignedUrlRequest>()).Returns("https://example.com/bucket/key?sig=abc");

        var request = new PresignedUrlRequest { Bucket = "bucket", Key = "key", Expiry = MaxExpiry };

        var result = generator.GeneratePresignedUploadUrl(request);

        result.IsSuccess.Should().BeTrue("the 7-day expiry itself is the boundary, not an over-long value");
    }

    [Fact]
    public void GeneratePresignedUploadUrl_ValidRequest_DerivesAbsoluteExpiresAt_FromClock()
    {
        var clock = new FakeClock(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var generator = CreateGenerator(out var s3, clock);
        s3.GetPreSignedURL(Arg.Any<GetPreSignedUrlRequest>()).Returns("https://example.com/bucket/key?sig=abc");

        var expiry = TimeSpan.FromMinutes(15);
        var request = new PresignedUrlRequest { Bucket = "bucket", Key = "key", Expiry = expiry };

        var result = generator.GeneratePresignedUploadUrl(request);

        result.IsSuccess.Should().BeTrue();
        result.Value.ExpiresAt.Should().Be(clock.UtcNow.Add(expiry));
        result.Value.Url.Should().Be(new Uri("https://example.com/bucket/key?sig=abc"));
    }

    [Fact]
    public void GeneratePresignedUploadUrl_EmptyBucket_ReturnsInvalidBucket()
    {
        var generator = CreateGenerator(out _);
        var request = new PresignedUrlRequest { Bucket = string.Empty, Key = "key", Expiry = TimeSpan.FromMinutes(5) };

        var result = generator.GeneratePresignedUploadUrl(request);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StorageErrors.InvalidBucket(string.Empty));
    }

    [Fact]
    public void GeneratePresignedUploadUrl_EmptyKey_ReturnsInvalidKey()
    {
        var generator = CreateGenerator(out _);
        var request = new PresignedUrlRequest { Bucket = "bucket", Key = string.Empty, Expiry = TimeSpan.FromMinutes(5) };

        var result = generator.GeneratePresignedUploadUrl(request);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StorageErrors.InvalidKey(string.Empty));
    }

    private static S3BlobUriGenerator CreateGenerator(out IAmazonS3 s3, FakeClock? clock = null)
    {
        s3 = Substitute.For<IAmazonS3>();
        return new S3BlobUriGenerator(s3, clock ?? new FakeClock(), NullLogger<S3BlobUriGenerator>.Instance);
    }
}
