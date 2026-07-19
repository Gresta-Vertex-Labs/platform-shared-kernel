using System.Text;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Storage.Abstractions.Models;
using SharedKernel.Testing.Storage;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Storage;

/// <summary>
/// Proves <see cref="InMemoryFileStorage"/> against <c>IFileStorage</c>'s documented contract — no
/// consuming domain has adopted this fake yet (see <c>16.Testing/state-map.md</c> T-47), so this
/// self-test is the only behavioral proof today, per the SelfTests routing rule.
/// </summary>
public sealed class InMemoryFileStorageTests
{
    [Fact]
    public async Task RoundTrip_UploadDownloadExistsMetadataCopyDelete_ThenNotFound()
    {
        var storage = new InMemoryFileStorage();
        var payload = Encoding.UTF8.GetBytes("hello world");

        var uploadResult = await storage.UploadAsync(
            new FileUploadRequest
            {
                Bucket = "bucket",
                Key = "a.txt",
                Content = new MemoryStream(payload),
                ContentType = "text/plain",
            },
            CancellationToken.None);

        Assert.True(uploadResult.IsSuccess);
        Assert.Equal("bucket", uploadResult.Value.Bucket);
        Assert.Equal("a.txt", uploadResult.Value.Key);
        Assert.False(string.IsNullOrWhiteSpace(uploadResult.Value.ETag));
        Assert.True(storage.WasUploaded("bucket", "a.txt"));

        var downloadResult = await storage.DownloadAsync("bucket", "a.txt", CancellationToken.None);
        Assert.True(downloadResult.IsSuccess);
        await using (downloadResult.Value)
        {
            Assert.Equal("text/plain", downloadResult.Value.ContentType);
            Assert.Equal(payload.Length, downloadResult.Value.ContentLength);
            using var reader = new StreamReader(downloadResult.Value.Content);
            Assert.Equal("hello world", await reader.ReadToEndAsync());
        }

        var existsResult = await storage.ExistsAsync("bucket", "a.txt", CancellationToken.None);
        Assert.True(existsResult.IsSuccess);
        Assert.True(existsResult.Value);

        var metadataResult = await storage.GetMetadataAsync("bucket", "a.txt", CancellationToken.None);
        Assert.True(metadataResult.IsSuccess);
        Assert.Equal(payload.Length, metadataResult.Value.ContentLength);
        Assert.Equal("text/plain", metadataResult.Value.ContentType);

        var copyResult = await storage.CopyAsync("bucket", "a.txt", "bucket", "b.txt", CancellationToken.None);
        Assert.True(copyResult.IsSuccess);
        Assert.Equal("bucket", copyResult.Value.Bucket);
        Assert.Equal("b.txt", copyResult.Value.Key);
        Assert.True(storage.WasCopied("bucket", "a.txt", "bucket", "b.txt"));

        var copyExistsResult = await storage.ExistsAsync("bucket", "b.txt", CancellationToken.None);
        Assert.True(copyExistsResult.Value);

        var deleteResult = await storage.DeleteAsync("bucket", "a.txt", CancellationToken.None);
        Assert.True(deleteResult.IsSuccess);
        Assert.True(storage.WasDeleted("bucket", "a.txt"));

        var batchDeleteResult = await storage.DeleteManyAsync("bucket", ["b.txt"], CancellationToken.None);
        Assert.True(batchDeleteResult.IsSuccess);
        Assert.All(batchDeleteResult.Value, outcome => Assert.True(outcome.Succeeded));
        Assert.True(storage.WasDeleted("bucket", "b.txt"));

        var notFoundResult = await storage.DownloadAsync("bucket", "a.txt", CancellationToken.None);
        Assert.True(notFoundResult.IsFailure);
        Assert.Equal(ErrorType.NotFound, notFoundResult.Error.Type);
    }

    [Fact]
    public async Task DeleteAsync_AbsentKey_IsIdempotent_ReturnsSuccess()
    {
        var storage = new InMemoryFileStorage();

        var result = await storage.DeleteAsync("bucket", "never-existed.txt", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(storage.WasDeleted("bucket", "never-existed.txt"));
    }

    [Fact]
    public async Task ListAsync_YieldsAllSeededItems_UnderMatchingPrefix()
    {
        var storage = new InMemoryFileStorage();
        storage.Seed("bucket", "docs/a.txt", new MemoryStream(), "text/plain");
        storage.Seed("bucket", "docs/b.txt", new MemoryStream(), "text/plain");
        storage.Seed("bucket", "other/c.txt", new MemoryStream(), "text/plain");
        storage.Seed("other-bucket", "docs/d.txt", new MemoryStream(), "text/plain");

        var keys = new List<string>();
        await foreach (var item in storage.ListAsync("bucket", "docs/", CancellationToken.None))
        {
            keys.Add(item.Key);
        }

        Assert.Equal(["docs/a.txt", "docs/b.txt"], keys.OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public async Task ListAsync_CancellationMidEnumeration_StopsPaging()
    {
        var storage = new InMemoryFileStorage();
        for (var i = 0; i < 5; i++)
        {
            storage.Seed("bucket", $"key-{i}", new MemoryStream(), "text/plain");
        }

        using var cts = new CancellationTokenSource();
        var seen = new List<string>();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var item in storage.ListAsync("bucket", string.Empty, cts.Token))
            {
                seen.Add(item.Key);
                if (seen.Count == 1)
                {
                    cts.Cancel();
                }
            }
        });

        Assert.True(seen.Count < 5);
    }

    [Fact]
    public async Task CheckHealthAsync_AlwaysSucceeds()
    {
        var storage = new InMemoryFileStorage();

        var result = await storage.CheckHealthAsync("any-bucket", CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task SimulateFailure_ForcesWriteFailures_ButLeavesReadPathAndHealthUnaffected()
    {
        var storage = new InMemoryFileStorage();
        storage.Seed("bucket", "seeded.txt", new MemoryStream("data"u8.ToArray()), "text/plain");
        storage.SimulateFailure = true;

        var uploadResult = await storage.UploadAsync(
            new FileUploadRequest { Bucket = "bucket", Key = "new.txt", Content = new MemoryStream(), ContentType = "text/plain" },
            CancellationToken.None);
        Assert.True(uploadResult.IsFailure);

        var copyResult = await storage.CopyAsync("bucket", "seeded.txt", "bucket", "copy.txt", CancellationToken.None);
        Assert.True(copyResult.IsFailure);

        var deleteResult = await storage.DeleteAsync("bucket", "seeded.txt", CancellationToken.None);
        Assert.True(deleteResult.IsFailure);

        var deleteManyResult = await storage.DeleteManyAsync("bucket", ["seeded.txt"], CancellationToken.None);
        Assert.True(deleteManyResult.IsFailure);

        // None of the simulated write failures actually mutated state.
        Assert.False(storage.WasUploaded("bucket", "new.txt"));
        Assert.False(storage.WasCopied("bucket", "seeded.txt", "bucket", "copy.txt"));
        Assert.False(storage.WasDeleted("bucket", "seeded.txt"));

        // Read-path members remain unaffected by SimulateFailure.
        var downloadResult = await storage.DownloadAsync("bucket", "seeded.txt", CancellationToken.None);
        Assert.True(downloadResult.IsSuccess);
        await downloadResult.Value.DisposeAsync();

        var existsResult = await storage.ExistsAsync("bucket", "seeded.txt", CancellationToken.None);
        Assert.True(existsResult.IsSuccess);
        Assert.True(existsResult.Value);

        var metadataResult = await storage.GetMetadataAsync("bucket", "seeded.txt", CancellationToken.None);
        Assert.True(metadataResult.IsSuccess);

        var listedKeys = new List<string>();
        await foreach (var item in storage.ListAsync("bucket", string.Empty, CancellationToken.None))
        {
            listedKeys.Add(item.Key);
        }

        Assert.Contains("seeded.txt", listedKeys);

        // CheckHealthAsync is unconditional — SimulateFailure never affects it either.
        var healthResult = await storage.CheckHealthAsync("bucket", CancellationToken.None);
        Assert.True(healthResult.IsSuccess);
    }

    [Fact]
    public void Seed_PopulatesStoreWithoutRecordingAsUploaded()
    {
        var storage = new InMemoryFileStorage();

        storage.Seed("bucket", "seeded.txt", new MemoryStream("data"u8.ToArray()), "text/plain");

        Assert.False(storage.WasUploaded("bucket", "seeded.txt"));
    }

    [Fact]
    public async Task Reset_ClearsStoreAndAllRecordedHistory()
    {
        var storage = new InMemoryFileStorage();
        await storage.UploadAsync(
            new FileUploadRequest { Bucket = "bucket", Key = "a.txt", Content = new MemoryStream(), ContentType = "text/plain" },
            CancellationToken.None);
        await storage.DeleteAsync("bucket", "a.txt", CancellationToken.None);

        storage.Reset();

        Assert.False(storage.WasUploaded("bucket", "a.txt"));
        Assert.False(storage.WasDeleted("bucket", "a.txt"));

        var existsResult = await storage.ExistsAsync("bucket", "a.txt", CancellationToken.None);
        Assert.False(existsResult.Value);
    }
}
