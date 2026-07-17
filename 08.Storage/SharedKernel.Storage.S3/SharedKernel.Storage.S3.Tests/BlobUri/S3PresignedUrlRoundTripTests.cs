using System.Net.Http.Headers;
using System.Text;
using FluentAssertions;
using SharedKernel.Storage.Abstractions.Models;
using SharedKernel.Storage.S3.Tests.Containers;
using SharedKernel.Testing.Containers;

namespace SharedKernel.Storage.S3.Tests.BlobUri;

/// <summary>
/// T-08: <c>S3BlobUriGenerator</c> presigned-URL round-trip coverage — a presigned upload URL
/// accepts a direct client <c>PUT</c> with no <c>Amazon.S3.IAmazonS3</c> call involved, and a
/// presigned download URL returns the uploaded object via a direct <c>GET</c>.
/// </summary>
[Collection(MinioCollection.Name)]
public sealed class S3PresignedUrlRoundTripTests(MinioContainerFixture fixture)
{
    [Fact]
    public async Task PresignedUploadUrl_AcceptsDirectClientPut_ThenObjectExists()
    {
        var blobUriGenerator = MinioProviderFactory.CreateBlobUriGenerator(fixture);
        var storage = MinioProviderFactory.CreateFileStorage(fixture);
        var bucket = fixture.DefaultBucket;
        var key = $"presigned/upload-{Guid.NewGuid():N}.txt";
        var content = Encoding.UTF8.GetBytes("uploaded via presigned url");

        var urlResult = blobUriGenerator.GeneratePresignedUploadUrl(
            new PresignedUrlRequest { Bucket = bucket, Key = key, Expiry = TimeSpan.FromMinutes(5) });

        urlResult.IsSuccess.Should().BeTrue();
        urlResult.Value.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow);

        using var httpClient = new HttpClient();
        using var putContent = new ByteArrayContent(content);
        putContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");

        using var putResponse = await httpClient.PutAsync(urlResult.Value.Url, putContent);
        putResponse.IsSuccessStatusCode.Should().BeTrue(
            $"presigned PUT must succeed with no IAmazonS3 call involved (got {(int)putResponse.StatusCode})");

        var existsResult = await storage.ExistsAsync(bucket, key, CancellationToken.None);
        existsResult.IsSuccess.Should().BeTrue();
        existsResult.Value.Should().BeTrue();
    }

    [Fact]
    public async Task PresignedDownloadUrl_ReturnsUploadedObject_ViaDirectGet()
    {
        var blobUriGenerator = MinioProviderFactory.CreateBlobUriGenerator(fixture);
        var storage = MinioProviderFactory.CreateFileStorage(fixture);
        var bucket = fixture.DefaultBucket;
        var key = $"presigned/download-{Guid.NewGuid():N}.txt";
        var content = Encoding.UTF8.GetBytes("downloaded via presigned url");

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
            uploadResult.IsSuccess.Should().BeTrue();
        }

        var urlResult = blobUriGenerator.GeneratePresignedDownloadUrl(
            new PresignedUrlRequest { Bucket = bucket, Key = key, Expiry = TimeSpan.FromMinutes(5) });

        urlResult.IsSuccess.Should().BeTrue();

        using var httpClient = new HttpClient();
        using var getResponse = await httpClient.GetAsync(urlResult.Value.Url);

        getResponse.IsSuccessStatusCode.Should().BeTrue(
            $"presigned GET must succeed with no IAmazonS3 call involved (got {(int)getResponse.StatusCode})");

        var downloadedBytes = await getResponse.Content.ReadAsByteArrayAsync();
        downloadedBytes.Should().BeEquivalentTo(content);
    }
}
