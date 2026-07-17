using System.Net.Http.Headers;
using System.Text;
using FluentAssertions;
using SharedKernel.Storage.Abstractions.Models;
using SharedKernel.Storage.Obs.Tests.Containers;
using SharedKernel.Testing.Containers;

namespace SharedKernel.Storage.Obs.Tests.BlobUri;

/// <summary>
/// T-14: <c>ObsBlobUriGenerator</c> presigned-URL round-trip coverage — mirrors
/// <c>SharedKernel.Storage.S3.Tests</c>' <c>S3PresignedUrlRoundTripTests</c> one-for-one against the
/// Obs provider types.
/// </summary>
[Collection(MinioCollection.Name)]
public sealed class ObsPresignedUrlRoundTripTests(MinioContainerFixture fixture)
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
