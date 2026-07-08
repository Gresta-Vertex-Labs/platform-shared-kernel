using SharedKernel.Application.Behaviors.Idempotency;
using SharedKernel.Testing.Application;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Application;

public sealed class FakeIdempotencyKeyStoreTests
{
    [Fact]
    public async Task HasProcessedAsync_UnseenKey_ReturnsFalse()
    {
        var store = new FakeIdempotencyKeyStore();

        var result = await store.HasProcessedAsync("key-1", CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task MarkProcessedAsync_ThenHasProcessedAsync_ReturnsTrue()
    {
        var store = new FakeIdempotencyKeyStore();

        await store.MarkProcessedAsync("key-1", CancellationToken.None);
        var result = await store.HasProcessedAsync("key-1", CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task MarkProcessedAsync_RecordsKeyInProcessedKeys()
    {
        var store = new FakeIdempotencyKeyStore();

        await store.MarkProcessedAsync("key-1", CancellationToken.None);

        Assert.Contains("key-1", store.ProcessedKeys);
    }

    [Fact]
    public async Task MarkAsProcessed_PreSeedsKey_WithoutGoingThroughMarkProcessedAsync()
    {
        var store = new FakeIdempotencyKeyStore();

        store.MarkAsProcessed("key-1");

        Assert.Contains("key-1", store.ProcessedKeys);
        var result = await store.HasProcessedAsync("key-1", CancellationToken.None);
        Assert.True(result);
    }

    [Fact]
    public void FakeIdempotencyKeyStore_DoesNotImplementIIdempotencyResponseStore()
    {
        object store = new FakeIdempotencyKeyStore();

        Assert.False(store is IIdempotencyResponseStore);
    }
}
