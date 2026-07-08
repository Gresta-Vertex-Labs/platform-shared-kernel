using SharedKernel.Application.Behaviors.Idempotency;
using SharedKernel.Testing.Application;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Application;

public sealed class FakeIdempotencyResponseStoreTests
{
    [Fact]
    public async Task HasProcessedAsync_UnseenKey_ReturnsFalse()
    {
        var store = new FakeIdempotencyResponseStore();

        var result = await store.HasProcessedAsync("key-1", CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task MarkProcessedAsync_ThenHasProcessedAsync_ReturnsTrue()
    {
        var store = new FakeIdempotencyResponseStore();

        await store.MarkProcessedAsync("key-1", CancellationToken.None);
        var result = await store.HasProcessedAsync("key-1", CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task MarkProcessedAsync_RecordsKeyInProcessedKeys()
    {
        var store = new FakeIdempotencyResponseStore();

        await store.MarkProcessedAsync("key-1", CancellationToken.None);

        Assert.Contains("key-1", store.ProcessedKeys);
    }

    [Fact]
    public async Task MarkAsProcessed_PreSeedsKey_WithoutGoingThroughMarkProcessedAsync()
    {
        var store = new FakeIdempotencyResponseStore();

        store.MarkAsProcessed("key-1");

        Assert.Contains("key-1", store.ProcessedKeys);
        var result = await store.HasProcessedAsync("key-1", CancellationToken.None);
        Assert.True(result);
    }

    [Fact]
    public async Task TryGetStoredResponseAsync_UnseededKey_ReturnsNull()
    {
        var store = new FakeIdempotencyResponseStore();

        var result = await store.TryGetStoredResponseAsync("key-1", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task StoreResponseAsync_ThenTryGetStoredResponseAsync_RoundTrips()
    {
        var store = new FakeIdempotencyResponseStore();

        await store.StoreResponseAsync("key-1", "{\"value\":42}", CancellationToken.None);
        var result = await store.TryGetStoredResponseAsync("key-1", CancellationToken.None);

        Assert.Equal("{\"value\":42}", result);
    }

    [Fact]
    public async Task StoreResponseAsync_RecordsInStoredResponses()
    {
        var store = new FakeIdempotencyResponseStore();

        await store.StoreResponseAsync("key-1", "payload", CancellationToken.None);

        Assert.Equal("payload", store.StoredResponses["key-1"]);
    }

    [Fact]
    public void FakeIdempotencyResponseStore_ImplementsIIdempotencyResponseStore()
    {
        var store = new FakeIdempotencyResponseStore();

        Assert.True(store is IIdempotencyResponseStore);
    }
}
