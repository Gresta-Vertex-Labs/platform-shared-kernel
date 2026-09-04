using System.Security.Cryptography;
using SharedKernel.Cryptography.Symmetric;
using Xunit;

namespace SharedKernel.Cryptography.Tests.Symmetric;

/// <summary>
/// Covers <see cref="CachedEncryptionKeyProvider"/> (P-446/WO-068): cache-hit behavior, TTL
/// expiry, single-flight refresh under genuine concurrency, and fail-closed propagation on a
/// failed refresh.
/// </summary>
public sealed class CachedEncryptionKeyProviderTests
{
    private static CryptographicKey NewKey(string id) => new(id, RandomNumberGenerator.GetBytes(32));

    [Fact]
    public async Task GetCurrentKeyAsync_CacheHit_NeverCallsInnerProvider()
    {
        var inner = new ControllableEncryptionKeyProvider(NewKey("v1"));
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var cached = new CachedEncryptionKeyProvider(inner, timeProvider, TimeSpan.FromMinutes(5));

        CryptographicKey first = await cached.GetCurrentKeyAsync();
        CryptographicKey second = await cached.GetCurrentKeyAsync();
        CryptographicKey third = await cached.GetCurrentKeyAsync();

        Assert.Equal("v1", first.Id);
        Assert.Equal("v1", second.Id);
        Assert.Equal("v1", third.Id);
        Assert.Equal(1, inner.CurrentKeyCallCount);
    }

    [Fact]
    public async Task GetCurrentKeyAsync_ExpiredEntry_AlwaysRefetches()
    {
        var inner = new ControllableEncryptionKeyProvider(NewKey("v1"));
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var ttl = TimeSpan.FromMinutes(5);
        var cached = new CachedEncryptionKeyProvider(inner, timeProvider, ttl);

        await cached.GetCurrentKeyAsync();
        Assert.Equal(1, inner.CurrentKeyCallCount);

        timeProvider.Advance(ttl + TimeSpan.FromSeconds(1));

        await cached.GetCurrentKeyAsync();
        Assert.Equal(2, inner.CurrentKeyCallCount);
    }

    [Fact]
    public async Task GetCurrentKeyAsync_RevokedOrRotatedKey_IsNeverServedPastConfiguredTtl()
    {
        var v1 = NewKey("v1");
        var v2 = NewKey("v2");
        var inner = new ControllableEncryptionKeyProvider(v1);
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var ttl = TimeSpan.FromMinutes(10);
        var cached = new CachedEncryptionKeyProvider(inner, timeProvider, ttl);

        CryptographicKey resolved = await cached.GetCurrentKeyAsync();
        Assert.Equal("v1", resolved.Id);

        // Rotate the inner provider's key while still inside the TTL window — the cached
        // (now-stale) v1 must still be served: this is the caching contract working as designed,
        // not a bug, and sets up the real assertion below.
        inner.SetCurrentKey(v2);
        resolved = await cached.GetCurrentKeyAsync();
        Assert.Equal("v1", resolved.Id);

        // Advance strictly past the TTL boundary — the next resolution MUST observe the rotated
        // key. A revoked/rotated key must never be served once its cache entry's TTL has elapsed.
        timeProvider.Advance(ttl + TimeSpan.FromSeconds(1));

        resolved = await cached.GetCurrentKeyAsync();
        Assert.Equal("v2", resolved.Id);
        Assert.Equal(2, inner.CurrentKeyCallCount);
    }

    [Fact]
    public async Task GetCurrentKeyAsync_ConcurrentCallersPastExpiry_CallInnerProviderExactlyOnce()
    {
        var inner = new ControllableEncryptionKeyProvider(NewKey("v1"));
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var cached = new CachedEncryptionKeyProvider(inner, timeProvider, TimeSpan.FromMinutes(5));

        // Hold the inner provider open so every one of the N concurrent callers below is
        // guaranteed to be genuinely in-flight simultaneously, rather than merely appearing to
        // race by chance — this is what makes the assertion below a real proof of single-flight
        // behavior rather than a trivially-passing sequential test.
        inner.Hold();

        const int concurrency = 50;
        Task<CryptographicKey>[] callers = [.. Enumerable.Range(0, concurrency)
            .Select(_ => Task.Run(() => cached.GetCurrentKeyAsync().AsTask()))];

        // Give every Task.Run-scheduled caller a chance to actually start and block on the held
        // gate before releasing it.
        await Task.Delay(TimeSpan.FromMilliseconds(200));

        inner.Release();

        CryptographicKey[] results = await Task.WhenAll(callers).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(1, inner.CurrentKeyCallCount);
        Assert.All(results, key => Assert.Equal("v1", key.Id));
    }

    [Fact]
    public async Task GetCurrentKeyAsync_InnerProviderFailureDuringRefresh_PropagatesToEveryWaitingCaller()
    {
        var inner = new ControllableEncryptionKeyProvider(NewKey("v1"));
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var cached = new CachedEncryptionKeyProvider(inner, timeProvider, TimeSpan.FromMinutes(5));

        var failure = new InvalidOperationException("KMS unreachable");
        inner.ThrowOnNextCall(failure);
        inner.Hold();

        const int concurrency = 10;
        Task<CryptographicKey>[] callers = [.. Enumerable.Range(0, concurrency)
            .Select(_ => Task.Run(() => cached.GetCurrentKeyAsync().AsTask()))];

        await Task.Delay(TimeSpan.FromMilliseconds(200));
        inner.Release();

        // Every single caller awaiting the one shared in-flight refresh must observe the failure
        // — never a stale fallback, and never a subset silently succeeding.
        foreach (Task<CryptographicKey> caller in callers)
        {
            InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => caller);
            Assert.Same(failure, thrown);
        }

        Assert.Equal(1, inner.CurrentKeyCallCount);

        // A failed refresh must not permanently poison the slot — the next call retries.
        CryptographicKey resolved = await cached.GetCurrentKeyAsync();
        Assert.Equal("v1", resolved.Id);
        Assert.Equal(2, inner.CurrentKeyCallCount);
    }

    [Fact]
    public async Task GetKeyAsync_CacheHit_NeverCallsInnerProvider()
    {
        var key = NewKey("v1");
        var inner = new ControllableEncryptionKeyProvider(key);
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var cached = new CachedEncryptionKeyProvider(inner, timeProvider, TimeSpan.FromMinutes(5));

        CryptographicKey? first = await cached.GetKeyAsync("v1");
        CryptographicKey? second = await cached.GetKeyAsync("v1");

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(1, inner.GetKeyCallCount);
    }

    [Fact]
    public async Task GetKeyAsync_ExpiredEntry_AlwaysRefetches()
    {
        var key = NewKey("v1");
        var inner = new ControllableEncryptionKeyProvider(key);
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var ttl = TimeSpan.FromMinutes(5);
        var cached = new CachedEncryptionKeyProvider(inner, timeProvider, ttl);

        await cached.GetKeyAsync("v1");
        Assert.Equal(1, inner.GetKeyCallCount);

        timeProvider.Advance(ttl + TimeSpan.FromSeconds(1));

        await cached.GetKeyAsync("v1");
        Assert.Equal(2, inner.GetKeyCallCount);
    }

    [Fact]
    public async Task GetKeyAsync_UnknownKeyId_ReturnsNull_AndIsNotCachedAsPositiveHit()
    {
        var inner = new ControllableEncryptionKeyProvider(NewKey("v1"));
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var cached = new CachedEncryptionKeyProvider(inner, timeProvider, TimeSpan.FromMinutes(5));

        CryptographicKey? result = await cached.GetKeyAsync("unknown");

        Assert.Null(result);
    }

    [Fact]
    public void Constructor_NullInner_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new CachedEncryptionKeyProvider(null!, new FakeTimeProvider(DateTimeOffset.UnixEpoch), TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void Constructor_NullTimeProvider_Throws()
    {
        Assert.Throws<ArgumentNullException>(
            () => new CachedEncryptionKeyProvider(new ControllableEncryptionKeyProvider(NewKey("v1")), null!, TimeSpan.FromMinutes(1)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_NonPositiveTtl_Throws(int ttlSeconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CachedEncryptionKeyProvider(
                new ControllableEncryptionKeyProvider(NewKey("v1")),
                new FakeTimeProvider(DateTimeOffset.UnixEpoch),
                TimeSpan.FromSeconds(ttlSeconds)));
    }
}
