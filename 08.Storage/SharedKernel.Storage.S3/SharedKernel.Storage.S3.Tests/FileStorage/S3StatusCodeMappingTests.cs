using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SharedKernel.Storage.Abstractions.Errors;
using SharedKernel.Storage.S3.FileStorage;

namespace SharedKernel.Storage.S3.Tests.FileStorage;

/// <summary>
/// T-09: <see cref="AmazonS3Exception"/> status-code → <see cref="StorageErrors"/> mapping tests.
/// </summary>
/// <remarks>
/// Per 08.Storage/CLAUDE.md's Test Rules, behavioral/round-trip coverage must never mock
/// <see cref="IAmazonS3"/> — but a "narrow error-mapping/unit assertion where a real failure is
/// hard to induce" (a 403 with no real IAM/bucket-policy setup; a 500 with no way to force a real
/// provider fault) is explicitly the sanctioned exception. This file substitutes
/// <see cref="IAmazonS3"/> purely to inject specific <see cref="AmazonS3Exception.StatusCode"/>
/// values at each call site and asserts <c>S3FileStorage</c>'s mapping logic — it does not assert
/// any other provider behavior.
/// </remarks>
public sealed class S3StatusCodeMappingTests
{
    private const string Bucket = "bucket";
    private const string Key = "key";

    // ---------------------------------------------------------------------------
    // DownloadAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task DownloadAsync_404_ReturnsNotFound()
    {
        var s3 = Substitute.For<IAmazonS3>();
        s3.GetObjectAsync(Arg.Any<GetObjectRequest>(), Arg.Any<CancellationToken>())
            .Throws(NotFoundException());

        var storage = CreateStorage(s3);

        var result = await storage.DownloadAsync(Bucket, Key, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StorageErrors.NotFound(Bucket, Key));
    }

    [Fact]
    public async Task DownloadAsync_403_ReturnsAccessDenied()
    {
        var s3 = Substitute.For<IAmazonS3>();
        s3.GetObjectAsync(Arg.Any<GetObjectRequest>(), Arg.Any<CancellationToken>())
            .Throws(ForbiddenException());

        var storage = CreateStorage(s3);

        var result = await storage.DownloadAsync(Bucket, Key, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StorageErrors.AccessDenied(Bucket, Key));
    }

    // ---------------------------------------------------------------------------
    // GetMetadataAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetMetadataAsync_404_ReturnsNotFound()
    {
        var s3 = Substitute.For<IAmazonS3>();
        s3.GetObjectMetadataAsync(Bucket, Key, Arg.Any<CancellationToken>())
            .Throws(NotFoundException());

        var storage = CreateStorage(s3);

        var result = await storage.GetMetadataAsync(Bucket, Key, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StorageErrors.NotFound(Bucket, Key));
    }

    [Fact]
    public async Task GetMetadataAsync_403_ReturnsAccessDenied()
    {
        var s3 = Substitute.For<IAmazonS3>();
        s3.GetObjectMetadataAsync(Bucket, Key, Arg.Any<CancellationToken>())
            .Throws(ForbiddenException());

        var storage = CreateStorage(s3);

        var result = await storage.GetMetadataAsync(Bucket, Key, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StorageErrors.AccessDenied(Bucket, Key));
    }

    // ---------------------------------------------------------------------------
    // ExistsAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ExistsAsync_404_ReturnsSuccessFalse()
    {
        var s3 = Substitute.For<IAmazonS3>();
        s3.GetObjectMetadataAsync(Bucket, Key, Arg.Any<CancellationToken>())
            .Throws(NotFoundException());

        var storage = CreateStorage(s3);

        var result = await storage.ExistsAsync(Bucket, Key, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse();
    }

    [Fact]
    public async Task ExistsAsync_403_ReturnsAccessDenied()
    {
        var s3 = Substitute.For<IAmazonS3>();
        s3.GetObjectMetadataAsync(Bucket, Key, Arg.Any<CancellationToken>())
            .Throws(ForbiddenException());

        var storage = CreateStorage(s3);

        var result = await storage.ExistsAsync(Bucket, Key, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StorageErrors.AccessDenied(Bucket, Key));
    }

    // ---------------------------------------------------------------------------
    // DeleteAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task DeleteAsync_404_IsIdempotent_ReturnsSuccess()
    {
        var s3 = Substitute.For<IAmazonS3>();
        s3.DeleteObjectAsync(Bucket, Key, Arg.Any<CancellationToken>())
            .Throws(NotFoundException());

        var storage = CreateStorage(s3);

        var result = await storage.DeleteAsync(Bucket, Key, CancellationToken.None);

        result.IsSuccess.Should().BeTrue("deleting an absent key must succeed idempotently");
    }

    [Fact]
    public async Task DeleteAsync_403_ReturnsAccessDenied()
    {
        var s3 = Substitute.For<IAmazonS3>();
        s3.DeleteObjectAsync(Bucket, Key, Arg.Any<CancellationToken>())
            .Throws(ForbiddenException());

        var storage = CreateStorage(s3);

        var result = await storage.DeleteAsync(Bucket, Key, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StorageErrors.AccessDenied(Bucket, Key));
    }

    // ---------------------------------------------------------------------------
    // CopyAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CopyAsync_404_OnSource_ReturnsNotFound()
    {
        var s3 = Substitute.For<IAmazonS3>();
        s3.CopyObjectAsync(Arg.Any<CopyObjectRequest>(), Arg.Any<CancellationToken>())
            .Throws(NotFoundException());

        var storage = CreateStorage(s3);

        var result = await storage.CopyAsync("src-bucket", "src-key", "dst-bucket", "dst-key", CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StorageErrors.NotFound("src-bucket", "src-key"));
    }

    [Fact]
    public async Task CopyAsync_403_ReturnsAccessDenied()
    {
        var s3 = Substitute.For<IAmazonS3>();
        s3.CopyObjectAsync(Arg.Any<CopyObjectRequest>(), Arg.Any<CancellationToken>())
            .Throws(ForbiddenException());

        var storage = CreateStorage(s3);

        var result = await storage.CopyAsync("src-bucket", "src-key", "dst-bucket", "dst-key", CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StorageErrors.AccessDenied("src-bucket", "src-key"));
    }

    [Fact]
    public async Task CopyAsync_UnmappedProviderFailure_ReturnsCopyFailed()
    {
        var s3 = Substitute.For<IAmazonS3>();
        s3.CopyObjectAsync(Arg.Any<CopyObjectRequest>(), Arg.Any<CancellationToken>())
            .Throws(ServerErrorException());

        var storage = CreateStorage(s3);

        var result = await storage.CopyAsync("src-bucket", "src-key", "dst-bucket", "dst-key", CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StorageErrors.CopyFailed("src-bucket", "src-key", "dst-bucket", "dst-key"));
    }

    // ---------------------------------------------------------------------------
    // DeleteManyAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task DeleteManyAsync_WholeCallProviderFailure_ReturnsBatchDeleteFailed()
    {
        var s3 = Substitute.For<IAmazonS3>();
        s3.DeleteObjectsAsync(Arg.Any<DeleteObjectsRequest>(), Arg.Any<CancellationToken>())
            .Throws(ServerErrorException());

        var storage = CreateStorage(s3);

        var result = await storage.DeleteManyAsync(Bucket, ["key-1", "key-2"], CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StorageErrors.BatchDeleteFailed(Bucket));
    }

    // ---------------------------------------------------------------------------
    // CheckHealthAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CheckHealthAsync_ProviderFailure_ReturnsConnectivityFailure()
    {
        var s3 = Substitute.For<IAmazonS3>();
        s3.HeadBucketAsync(Arg.Any<HeadBucketRequest>(), Arg.Any<CancellationToken>())
            .Throws(ServerErrorException());

        var storage = CreateStorage(s3);

        var result = await storage.CheckHealthAsync(Bucket, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(StorageErrors.ConnectivityFailure(Bucket));
    }

    private static S3FileStorage CreateStorage(IAmazonS3 s3) =>
        new(s3, NullLogger<S3FileStorage>.Instance);

    private static AmazonS3Exception NotFoundException() =>
        new("Not found") { StatusCode = HttpStatusCode.NotFound };

    private static AmazonS3Exception ForbiddenException() =>
        new("Forbidden") { StatusCode = HttpStatusCode.Forbidden };

    private static AmazonS3Exception ServerErrorException() =>
        new("Internal error") { StatusCode = HttpStatusCode.InternalServerError };
}
