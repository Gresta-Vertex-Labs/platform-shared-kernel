using System.Text;
using FluentAssertions;
using SharedKernel.Storage.Abstractions.Models;
using SharedKernel.Storage.S3.Tests.Containers;
using SharedKernel.Testing.Containers;

namespace SharedKernel.Storage.S3.Tests.FileStorage;

/// <summary>
/// T-12: MinIO-path confirmation. Every real-backend test in this suite (T-05–T-08, above) already
/// runs against <see cref="MinioContainerFixture"/> — i.e. against
/// <c>S3StorageOptions.ServiceUrl</c> + <c>ForcePathStyle</c> configured, never against real AWS S3 —
/// which is itself continuous proof that no MinIO-specific branch exists anywhere in
/// <c>S3FileStorage</c>/<c>S3BlobUriGenerator</c>: the identical, unmodified provider code is what
/// every other test in this project already exercises. This class makes that fact explicit and
/// independently traceable as its own test artifact, asserting the fixture's path-style addressing
/// is genuinely in effect and a full upload → download → delete round trip succeeds through it.
/// </summary>
[Collection(MinioCollection.Name)]
public sealed class S3MinioPathConfirmationTests(MinioContainerFixture fixture)
{
    [Fact]
    public void Fixture_IsConfiguredForPathStyleAddressing()
    {
        // MinIO requires path-style bucket addressing (https://host/bucket/key) rather than
        // subdomain addressing — confirming this is set is what proves the round-trip tests below
        // (and T-05–T-08 in the sibling test classes) exercise the MinIO configuration path, not a
        // hidden default that happens to coincide with real AWS S3's virtual-hosted-style default.
        fixture.ForcePathStyle.Should().BeTrue();
    }

    [Fact]
    public async Task UploadDownloadDelete_RoundTrip_SucceedsThroughServiceUrlAndForcePathStyle()
    {
        var storage = MinioProviderFactory.CreateFileStorage(fixture);
        var bucket = fixture.DefaultBucket;
        var key = $"minio-path-confirmation/{Guid.NewGuid():N}.txt";
        var content = Encoding.UTF8.GetBytes("minio path-style round trip");

        using (var uploadStream = new MemoryStream(content))
        {
            var uploadResult = await storage.UploadAsync(
                new FileUploadRequest
                {
                    Bucket = bucket,
                    Key = key,
                    Content = uploadStream,
                    ContentType = "text/plain",
                },
                CancellationToken.None);

            uploadResult.IsSuccess.Should().BeTrue(
                "the same S3FileStorage code path must work identically against MinIO's ServiceUrl+ForcePathStyle configuration");
        }

        var downloadResult = await storage.DownloadAsync(bucket, key, CancellationToken.None);
        downloadResult.IsSuccess.Should().BeTrue();

        await using (var download = downloadResult.Value)
        {
            using var memoryStream = new MemoryStream();
            await download.Content.CopyToAsync(memoryStream);
            memoryStream.ToArray().Should().BeEquivalentTo(content);
        }

        var deleteResult = await storage.DeleteAsync(bucket, key, CancellationToken.None);
        deleteResult.IsSuccess.Should().BeTrue();

        var existsResult = await storage.ExistsAsync(bucket, key, CancellationToken.None);
        existsResult.IsSuccess.Should().BeTrue();
        existsResult.Value.Should().BeFalse();
    }
}
