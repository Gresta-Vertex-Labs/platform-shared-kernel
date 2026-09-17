using SharedKernel.Testing.Security;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Security;

public sealed class InMemoryDpopReplayCacheTests
{
    private static readonly DateTimeOffset ExpiresAt = new(2026, 9, 16, 12, 5, 0, TimeSpan.Zero);

    [Fact]
    public void Count_New_IsZero()
    {
        Assert.Equal(0, new InMemoryDpopReplayCache().Count);
    }

    [Fact]
    public async Task TryAddAsync_FirstProof_ReturnsTrueAndCounts()
    {
        var cache = new InMemoryDpopReplayCache();

        Assert.True(await cache.TryAddAsync("proof-1", ExpiresAt, CancellationToken.None));
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public async Task TryAddAsync_SameProofAgain_ReturnsFalseAndCountUnchanged()
    {
        var cache = new InMemoryDpopReplayCache();
        await cache.TryAddAsync("proof-1", ExpiresAt, CancellationToken.None);

        Assert.False(await cache.TryAddAsync("proof-1", ExpiresAt.AddHours(1), CancellationToken.None));
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public async Task TryAddAsync_IdsDifferingOnlyInCase_AreDistinct()
    {
        var cache = new InMemoryDpopReplayCache();

        Assert.True(await cache.TryAddAsync("proof-a", ExpiresAt, CancellationToken.None));
        Assert.True(await cache.TryAddAsync("PROOF-A", ExpiresAt, CancellationToken.None));
        Assert.Equal(2, cache.Count);
    }

    [Fact]
    public async Task TryAddAsync_ConcurrentSameProof_ExactlyOneSucceeds()
    {
        var cache = new InMemoryDpopReplayCache();

        var results = await Task.WhenAll(Enumerable.Range(0, 64)
            .Select(_ => Task.Run(() => cache.TryAddAsync("proof-1", ExpiresAt, CancellationToken.None).AsTask())));

        Assert.Single(results, added => added);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public async Task TryAddAsync_NullProofId_ThrowsArgumentNull()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => new InMemoryDpopReplayCache().TryAddAsync(null!, ExpiresAt, CancellationToken.None).AsTask());
    }
}
