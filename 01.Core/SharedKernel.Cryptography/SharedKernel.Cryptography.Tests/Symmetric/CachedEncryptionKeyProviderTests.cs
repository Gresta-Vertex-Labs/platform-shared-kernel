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
        using var allAttached = new CountdownEvent(concurrency);
        Task<CryptographicKey>[] callers = [.. Enumerable.Range(0, concurrency)
            .Select(_ => Task.Run(() =>
            {
                // cached.GetCurrentKeyAsync() always runs synchronously up to the point where it
                // has registered itself as a waiter on the shared in-flight slot (or created it)
                // before it can suspend — so signalling right after the call returns is a
                // deterministic proof of attachment, unlike a fixed Task.Delay, which is only a
                // guess about thread-pool scheduling and can under-wait on a contended CI runner.
                Task<CryptographicKey> task = cached.GetCurrentKeyAsync().AsTask();
                allAttached.Signal();
                return task;
            }))];

        Assert.True(
            allAttached.Wait(TimeSpan.FromSeconds(10)),
            "Not every concurrent caller attached to the shared in-flight resolution within the timeout.");

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
        using var allAttached = new CountdownEvent(concurrency);
        Task<CryptographicKey>[] callers = [.. Enumerable.Range(0, concurrency)
            .Select(_ => Task.Run(() =>
            {
                // See the identical barrier in GetCurrentKeyAsync_ConcurrentCallersPastExpiry_
                // CallInnerProviderExactlyOnce above: this deterministically proves every caller
                // has attached to the shared slot as a waiter before Release() runs. Without it,
                // a caller whose Task.Run body is slow to be dispatched under CI contention could
                // still be queued when the other waiters observe the fault, decrement the
                // slot's waiter count to zero, and evict it (the documented, correct P-511
                // behavior for a failed refresh) — so the late caller would start a brand new
                // resolution against ThrowOnNextCall's already-consumed one-shot exception and
                // spuriously succeed instead of observing the failure.
                Task<CryptographicKey> task = cached.GetCurrentKeyAsync().AsTask();
                allAttached.Signal();
                return task;
            }))];

        Assert.True(
            allAttached.Wait(TimeSpan.FromSeconds(10)),
            "Not every concurrent caller attached to the shared in-flight resolution within the timeout.");

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

    // ---- P-511/WO-083: cross-caller cancellation safety ----

    [Fact]
    public async Task GetCurrentKeyAsync_OneCallerCancels_NeverCancelsOrFaultsAnotherConcurrentCaller()
    {
        var inner = new ControllableEncryptionKeyProvider(NewKey("v1"));
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var cached = new CachedEncryptionKeyProvider(inner, timeProvider, TimeSpan.FromMinutes(5));

        inner.Hold();

        using var callerACts = new CancellationTokenSource();
        Task<CryptographicKey> callerA = cached.GetCurrentKeyAsync(callerACts.Token).AsTask();
        Task<CryptographicKey> callerB = cached.GetCurrentKeyAsync().AsTask();

        // Let both callers genuinely start and block on the held gate before staggering A's
        // cancellation — this is what makes the assertions below a real proof rather than a
        // trivially-passing sequential test.
        await Task.Delay(TimeSpan.FromMilliseconds(200));

        callerACts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => callerA);

        // Caller B must be entirely undisturbed by A's cancellation: still legitimately pending,
        // not faulted, not cancelled — the shared in-flight resolution is untouched because at
        // least one caller (B) is still waiting.
        await Task.Delay(TimeSpan.FromMilliseconds(100));
        Assert.False(callerB.IsCompleted);
        Assert.Equal(0, inner.CanceledCallCount);

        inner.Release();

        CryptographicKey resolved = await callerB.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("v1", resolved.Id);
        Assert.Equal(1, inner.CurrentKeyCallCount);
        Assert.Equal(0, inner.CanceledCallCount);
    }

    [Fact]
    public async Task GetCurrentKeyAsync_LastCallerCancels_GenuinelyAbandonsInnerCallAndEvictsSlot()
    {
        var inner = new ControllableEncryptionKeyProvider(NewKey("v1"));
        var timeProvider = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var cached = new CachedEncryptionKeyProvider(inner, timeProvider, TimeSpan.FromMinutes(5));

        inner.Hold();

        using var soleCallerCts = new CancellationTokenSource();
        Task<CryptographicKey> soleCaller = cached.GetCurrentKeyAsync(soleCallerCts.Token).AsTask();

        await Task.Delay(TimeSpan.FromMilliseconds(200));

        // This caller is the ONLY one currently awaiting the shared slot — cancelling it must
        // bring the per-slot waiter count to zero, genuinely abandoning the inner factory call
        // (as opposed to the previous test, where a second caller was still legitimately waiting).
        soleCallerCts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => soleCaller);

        // The inner call's own await must observe cancellation directly — proving the slot's
        // owned CancellationTokenSource, not merely Task.WaitAsync's caller-side surfacing, is
        // what tore down the abandoned in-flight resolution.
        await WaitUntilAsync(() => inner.CanceledCallCount == 1, TimeSpan.FromSeconds(10));

        // A subsequent call inside the same still-unexpired TTL window must NOT reuse the
        // abandoned slot (which would otherwise poison every future caller with a spurious
        // cancellation) — it must evict and start a genuinely fresh resolution.
        Task<CryptographicKey> nextCaller = cached.GetCurrentKeyAsync().AsTask();
        await WaitUntilAsync(() => inner.CurrentKeyCallCount == 2, TimeSpan.FromSeconds(10));

        inner.Release();
        CryptographicKey resolved = await nextCaller.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("v1", resolved.Id);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Condition was not met within the allotted timeout.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }
    }
}
