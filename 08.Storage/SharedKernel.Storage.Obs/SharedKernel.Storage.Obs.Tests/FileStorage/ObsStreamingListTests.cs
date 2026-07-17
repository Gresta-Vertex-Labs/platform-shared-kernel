using System.Text;
using FluentAssertions;
using SharedKernel.Storage.Abstractions.Models;
using SharedKernel.Storage.Obs.Tests.Containers;
using SharedKernel.Testing.Containers;

namespace SharedKernel.Storage.Obs.Tests.FileStorage;

/// <summary>
/// T-14: <c>ObsFileStorage.ListAsync</c> streaming coverage — mirrors
/// <c>SharedKernel.Storage.S3.Tests</c>' <c>S3StreamingListTests</c> one-for-one against the Obs
/// provider types.
/// </summary>
[Collection(MinioCollection.Name)]
public sealed class ObsStreamingListTests(MinioContainerFixture fixture)
{
    [Fact]
    public async Task ListAsync_YieldsAllSeededObjects_UnderPrefix()
    {
        var storage = MinioProviderFactory.CreateFileStorage(fixture);
        var bucket = fixture.DefaultBucket;
        var prefix = $"streaming-list/{Guid.NewGuid():N}/";
        const int seededCount = 12;
        var seededKeys = new HashSet<string>();

        for (var i = 0; i < seededCount; i++)
        {
            var key = $"{prefix}item-{i:D3}.txt";
            seededKeys.Add(key);

            using var uploadStream = new MemoryStream(Encoding.UTF8.GetBytes($"payload-{i}"));
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

        var listedKeys = new List<string>();
        await foreach (var metadata in storage.ListAsync(bucket, prefix, CancellationToken.None))
        {
            listedKeys.Add(metadata.Key);
        }

        listedKeys.Should().HaveCount(seededCount);
        listedKeys.Should().BeEquivalentTo(seededKeys);
    }

    [Fact]
    public async Task ListAsync_CancellationRequestedBeforeFirstPage_ThrowsWithoutYieldingAnyItem()
    {
        // See S3StreamingListTests' identically-named test for the full rationale on why this is
        // the provably-correct, fast assertion available without seeding 1000+ objects to force a
        // real second-page fetch.
        var storage = MinioProviderFactory.CreateFileStorage(fixture);
        var bucket = fixture.DefaultBucket;
        var prefix = $"streaming-list-cancel/{Guid.NewGuid():N}/";

        using var uploadStream = new MemoryStream(Encoding.UTF8.GetBytes("payload"));
        var uploadResult = await storage.UploadAsync(
            new FileUploadRequest
            {
                Bucket = bucket,
                Key = $"{prefix}item-000.txt",
                Content = uploadStream,
                ContentType = "text/plain",
            },
            CancellationToken.None);
        uploadResult.IsSuccess.Should().BeTrue();

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var seen = 0;

        var act = async () =>
        {
            await foreach (var metadata in storage.ListAsync(bucket, prefix, cts.Token))
            {
                seen++;
            }
        };

        await act.Should().ThrowAsync<OperationCanceledException>();
        seen.Should().Be(0, "a cancellation observed by the underlying page fetch must stop enumeration before any item is yielded");
    }
}
