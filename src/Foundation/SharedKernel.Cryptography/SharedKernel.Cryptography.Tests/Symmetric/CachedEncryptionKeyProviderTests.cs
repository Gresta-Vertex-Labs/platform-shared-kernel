using System.Globalization;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Cryptography.Tests.TestDoubles;

namespace SharedKernel.Cryptography.Tests.Symmetric;

public sealed class CachedEncryptionKeyProviderTests
{
    private static readonly TimeSpan TimeToLive = TimeSpan.FromMinutes(5);

    private readonly ManualTimeProvider _time = new();
    private readonly ScriptedKeyProvider _inner = new();

    [Fact]
    public async Task GetCurrentKeyAsync_WithinTimeToLive_CallsInnerOnce()
    {
        CryptographicKey key = TestKeys.Create("current");
        _inner.OnGetCurrentKey = _ => new(key);
        CachedEncryptionKeyProvider cache = CreateCache();

        CryptographicKey first = await cache.GetCurrentKeyAsync();
        _time.Advance(TimeToLive - TimeSpan.FromSeconds(1));
        CryptographicKey second = await cache.GetCurrentKeyAsync();

        Assert.Same(key, first);
        Assert.Same(key, second);
        Assert.Equal(1, _inner.CurrentKeyCalls);
    }

    [Fact]
    public async Task GetCurrentKeyAsync_AfterTimeToLive_CallsInnerAgain()
    {
        _inner.OnGetCurrentKey = _ => new(TestKeys.Create("current"));
        CachedEncryptionKeyProvider cache = CreateCache();

        await cache.GetCurrentKeyAsync();
        _time.Advance(TimeToLive);
        await cache.GetCurrentKeyAsync();

        Assert.Equal(2, _inner.CurrentKeyCalls);
    }

    [Fact]
    public async Task GetCurrentKeyAsync_RotationBecomesVisibleAfterTimeToLive()
    {
        CryptographicKey oldKey = TestKeys.Create("2026-03");
        CryptographicKey newKey = TestKeys.Create("2026-09");
        CryptographicKey current = oldKey;
        _inner.OnGetCurrentKey = _ => new(current);
        CachedEncryptionKeyProvider cache = CreateCache();

        Assert.Same(oldKey, await cache.GetCurrentKeyAsync());
        current = newKey;
        _time.Advance(TimeSpan.FromMinutes(4));
        Assert.Same(oldKey, await cache.GetCurrentKeyAsync());
        _time.Advance(TimeSpan.FromMinutes(1));
        Assert.Same(newKey, await cache.GetCurrentKeyAsync());
    }

    [Fact]
    public async Task GetKeyAsync_WithinTimeToLive_CallsInnerOncePerId()
    {
        CryptographicKey a = TestKeys.Create("a");
        CryptographicKey b = TestKeys.Create("b");
        _inner.OnGetKey = (id, _) => new(id == "a" ? a : b);
        CachedEncryptionKeyProvider cache = CreateCache();

        Assert.Same(a, await cache.GetKeyAsync("a"));
        Assert.Same(b, await cache.GetKeyAsync("b"));
        Assert.Same(a, await cache.GetKeyAsync("a"));
        Assert.Same(b, await cache.GetKeyAsync("b"));

        Assert.Equal(2, _inner.KeyCalls);
    }

    [Fact]
    public async Task GetKeyAsync_AfterTimeToLive_CallsInnerAgain()
    {
        _inner.OnGetKey = (id, _) => new(TestKeys.Create(id));
        CachedEncryptionKeyProvider cache = CreateCache();

        await cache.GetKeyAsync("a");
        _time.Advance(TimeToLive + TimeSpan.FromTicks(1));
        await cache.GetKeyAsync("a");

        Assert.Equal(2, _inner.KeyCalls);
    }

    [Fact]
    public async Task GetKeyAsync_KeyIdsAreCaseSensitive()
    {
        _inner.OnGetKey = (id, _) => new(TestKeys.Create(id));
        CachedEncryptionKeyProvider cache = CreateCache();

        CryptographicKey? upper = await cache.GetKeyAsync("Key");
        CryptographicKey? lower = await cache.GetKeyAsync("key");

        Assert.Equal("Key", upper!.Id);
        Assert.Equal("key", lower!.Id);
        Assert.Equal(2, _inner.KeyCalls);
    }

    [Fact]
    public async Task GetKeyAsync_UnknownId_IsNotCached()
    {
        _inner.OnGetKey = (_, _) => new((CryptographicKey?)null);
        CachedEncryptionKeyProvider cache = CreateCache();

        Assert.Null(await cache.GetKeyAsync("missing"));
        Assert.Null(await cache.GetKeyAsync("missing"));
        Assert.Null(await cache.GetKeyAsync("missing"));

        Assert.Equal(3, _inner.KeyCalls);
    }

    [Fact]
    public async Task GetKeyAsync_ManyUnknownIds_DoNotOccupyCacheCapacity()
    {
        _inner.OnGetKey = (id, _) => new(id.StartsWith("known-", StringComparison.Ordinal) ? TestKeys.Create(id) : null);
        CachedEncryptionKeyProvider cache = CreateCache(maxEntries: 10);

        for (int i = 0; i < 5000; i++)
        {
            Assert.Null(await cache.GetKeyAsync(string.Create(CultureInfo.InvariantCulture, $"unknown-{i}")));
        }

        for (int round = 0; round < 2; round++)
        {
            for (int i = 0; i < 10; i++)
            {
                string id = string.Create(CultureInfo.InvariantCulture, $"known-{i}");
                Assert.Equal(id, (await cache.GetKeyAsync(id))!.Id);
            }
        }

        Assert.Equal(5000 + 10, _inner.KeyCalls);
    }

    [Fact]
    public async Task GetKeyAsync_MoreKnownIdsThanCapacity_StillReturnsCorrectKeys()
    {
        var keys = Enumerable.Range(0, 50)
            .Select(i => TestKeys.Create(string.Create(CultureInfo.InvariantCulture, $"key-{i}")))
            .ToDictionary(k => k.Id, StringComparer.Ordinal);
        _inner.OnGetKey = (id, _) => new(keys.GetValueOrDefault(id));
        CachedEncryptionKeyProvider cache = CreateCache(maxEntries: 10);

        for (int round = 0; round < 3; round++)
        {
            foreach ((string id, CryptographicKey key) in keys)
            {
                Assert.Same(key, await cache.GetKeyAsync(id));
            }
        }

        Assert.Equal(10 + (40 * 3), _inner.KeyCalls);
    }

    [Fact]
    public async Task GetKeyAsync_ConcurrentRequestsForSameId_ShareOneInnerCall()
    {
        CryptographicKey key = TestKeys.Create("k");
        var release = new TaskCompletionSource<CryptographicKey?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _inner.OnGetKey = (_, _) => new(release.Task);
        CachedEncryptionKeyProvider cache = CreateCache();

        Task<CryptographicKey?>[] callers = [.. Enumerable.Range(0, 20).Select(_ => Task.Run(async () => await cache.GetKeyAsync("k")))];
        await WaitUntilAsync(() => _inner.KeyCalls >= 1);
        await Task.Delay(50);
        release.SetResult(key);
        CryptographicKey?[] results = await Task.WhenAll(callers);

        Assert.Equal(1, _inner.KeyCalls);
        Assert.All(results, result => Assert.Same(key, result));
    }

    [Fact]
    public async Task GetCurrentKeyAsync_SequentialRequestsWhileFetchPending_ShareOneInnerCall()
    {
        CryptographicKey key = TestKeys.Create("current");
        var release = new TaskCompletionSource<CryptographicKey>(TaskCreationOptions.RunContinuationsAsynchronously);
        _inner.OnGetCurrentKey = _ => new(release.Task);
        CachedEncryptionKeyProvider cache = CreateCache();

        Task<CryptographicKey>[] callers = [.. Enumerable.Range(0, 20).Select(_ => cache.GetCurrentKeyAsync().AsTask())];
        release.SetResult(key);
        CryptographicKey[] results = await Task.WhenAll(callers);

        Assert.Equal(1, _inner.CurrentKeyCalls);
        Assert.All(results, result => Assert.Same(key, result));
    }

    [Fact]
    public async Task GetCurrentKeyAsync_ConcurrentColdStartRequests_ShareOneInnerCall()
    {
        for (int round = 0; round < 150; round++)
        {
            var inner = new ScriptedKeyProvider();
            var release = new TaskCompletionSource<CryptographicKey>(TaskCreationOptions.RunContinuationsAsynchronously);
            inner.OnGetCurrentKey = _ => new(release.Task);
            var cache = new CachedEncryptionKeyProvider(inner, _time, TimeToLive);
            using var barrier = new Barrier(8);

            Task<CryptographicKey>[] callers =
            [
                .. Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
                {
                    barrier.SignalAndWait();
                    return await cache.GetCurrentKeyAsync();
                })),
            ];
            await WaitUntilAsync(() => inner.CurrentKeyCalls >= 1);
            await Task.Delay(5);
            release.SetResult(TestKeys.Create("current"));
            await Task.WhenAll(callers);

            Assert.True(inner.CurrentKeyCalls == 1, $"Round {round}: the inner provider was called {inner.CurrentKeyCalls} times.");
        }
    }

    [Fact]
    public async Task GetKeyAsync_CapacityOne_ConcurrentColdRequestsForSameIdShareOneInnerCall()
    {
        for (int round = 0; round < 150; round++)
        {
            var inner = new ScriptedKeyProvider();
            var release = new TaskCompletionSource<CryptographicKey?>(TaskCreationOptions.RunContinuationsAsynchronously);
            inner.OnGetKey = (_, _) => new(release.Task);
            var cache = new CachedEncryptionKeyProvider(inner, _time, TimeToLive, maxEntries: 1);
            using var barrier = new Barrier(8);
            CryptographicKey key = TestKeys.Create("k");

            Task<CryptographicKey?>[] callers =
            [
                .. Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
                {
                    barrier.SignalAndWait();
                    return await cache.GetKeyAsync("k");
                })),
            ];
            await WaitUntilAsync(() => inner.KeyCalls >= 1);
            await Task.Delay(5);
            release.SetResult(key);
            CryptographicKey?[] results = await Task.WhenAll(callers);

            Assert.True(inner.KeyCalls == 1, $"Round {round}: the inner provider was called {inner.KeyCalls} times.");
            Assert.All(results, result => Assert.Same(key, result));
        }
    }

    [Fact]
    public async Task GetKeyAsync_CapacityOneFull_DifferentIdResolvesCorrectlyWithoutCaching()
    {
        CryptographicKey cached = TestKeys.Create("k");
        CryptographicKey other = TestKeys.Create("other");
        var requestedIds = new List<string>();
        _inner.OnGetKey = (id, _) =>
        {
            requestedIds.Add(id);
            return new(id switch
            {
                "k" => cached,
                "other" => other,
                _ => null,
            });
        };
        CachedEncryptionKeyProvider cache = CreateCache(maxEntries: 1);

        Assert.Same(cached, await cache.GetKeyAsync("k"));
        Assert.Same(other, await cache.GetKeyAsync("other"));
        Assert.Same(other, await cache.GetKeyAsync("other"));
        Assert.Same(cached, await cache.GetKeyAsync("k"));

        Assert.Equal(["k", "other", "other"], requestedIds);
    }

    [Fact]
    public async Task GetKeyAsync_OneWaiterCancels_OtherWaiterStillCompletes()
    {
        CryptographicKey key = TestKeys.Create("k");
        var release = new TaskCompletionSource<CryptographicKey?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _inner.OnGetKey = (_, _) => new(release.Task);
        CachedEncryptionKeyProvider cache = CreateCache();
        using var cancelA = new CancellationTokenSource();

        Task<CryptographicKey?> callerA = cache.GetKeyAsync("k", cancelA.Token).AsTask();
        Task<CryptographicKey?> callerB = cache.GetKeyAsync("k").AsTask();
        await cancelA.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => callerA);
        Assert.False(callerB.IsCompleted);
        Assert.False(_inner.KeyTokens.Single().IsCancellationRequested);

        release.SetResult(key);

        Assert.Same(key, await callerB);
        Assert.Equal(1, _inner.KeyCalls);
    }

    [Fact]
    public async Task GetKeyAsync_LastWaiterCancels_CancelsInnerCallAndNextCallStartsFreshFetch()
    {
        CryptographicKey key = TestKeys.Create("k");
        var abandoned = new TaskCompletionSource<CryptographicKey?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _inner.OnGetKey = (_, _) => new(abandoned.Task);
        CachedEncryptionKeyProvider cache = CreateCache();
        using var cancel = new CancellationTokenSource();

        Task<CryptographicKey?> caller = cache.GetKeyAsync("k", cancel.Token).AsTask();
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => caller);

        Assert.True(_inner.KeyTokens.Single().IsCancellationRequested);

        _inner.OnGetKey = (_, _) => new(key);
        Assert.Same(key, await cache.GetKeyAsync("k"));
        Assert.Equal(2, _inner.KeyCalls);

        abandoned.SetResult(TestKeys.Create("k"));
        Assert.Same(key, await cache.GetKeyAsync("k"));
        Assert.Equal(2, _inner.KeyCalls);
    }

    [Fact]
    public async Task GetCurrentKeyAsync_LastWaiterCancels_CancelsInnerCall()
    {
        var never = new TaskCompletionSource<CryptographicKey>(TaskCreationOptions.RunContinuationsAsynchronously);
        _inner.OnGetCurrentKey = _ => new(never.Task);
        CachedEncryptionKeyProvider cache = CreateCache();
        using var cancel = new CancellationTokenSource();

        Task<CryptographicKey> caller = cache.GetCurrentKeyAsync(cancel.Token).AsTask();
        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => caller);
        Assert.True(_inner.CurrentKeyTokens.Single().IsCancellationRequested);
    }

    [Fact]
    public async Task GetKeyAsync_InnerFails_AllWaitersObserveFailureAndNextCallRetries()
    {
        CryptographicKey key = TestKeys.Create("k");
        var failing = new TaskCompletionSource<CryptographicKey?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _inner.OnGetKey = (_, _) => new(failing.Task);
        CachedEncryptionKeyProvider cache = CreateCache();

        Task<CryptographicKey?> first = cache.GetKeyAsync("k").AsTask();
        Task<CryptographicKey?> second = cache.GetKeyAsync("k").AsTask();
        failing.SetException(new TimeoutException("key service unreachable"));

        await Assert.ThrowsAsync<TimeoutException>(() => first);
        await Assert.ThrowsAsync<TimeoutException>(() => second);
        Assert.Equal(1, _inner.KeyCalls);

        _inner.OnGetKey = (_, _) => new(key);
        Assert.Same(key, await cache.GetKeyAsync("k"));
        Assert.Equal(2, _inner.KeyCalls);
    }

    [Fact]
    public async Task GetKeyAsync_InnerThrowsSynchronously_IsNotCached()
    {
        CryptographicKey key = TestKeys.Create("k");
        _inner.OnGetKey = (_, _) => throw new InvalidOperationException("denied");
        CachedEncryptionKeyProvider cache = CreateCache();

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await cache.GetKeyAsync("k"));

        _inner.OnGetKey = (_, _) => new(key);
        Assert.Same(key, await cache.GetKeyAsync("k"));
    }

    [Fact]
    public async Task GetCurrentKeyAsync_RefreshFailsAfterExpiry_ThrowsInsteadOfServingExpiredKey()
    {
        CryptographicKey key = TestKeys.Create("current");
        _inner.OnGetCurrentKey = _ => new(key);
        CachedEncryptionKeyProvider cache = CreateCache();
        await cache.GetCurrentKeyAsync();

        _time.Advance(TimeToLive);
        _inner.OnGetCurrentKey = _ => throw new TimeoutException("key service unreachable");

        await Assert.ThrowsAsync<TimeoutException>(async () => await cache.GetCurrentKeyAsync());
        await Assert.ThrowsAsync<TimeoutException>(async () => await cache.GetCurrentKeyAsync());

        _inner.OnGetCurrentKey = _ => new(key);
        Assert.Same(key, await cache.GetCurrentKeyAsync());
    }

    [Fact]
    public async Task GetKeyAsync_RefreshFailsAfterExpiry_ThrowsInsteadOfServingExpiredKey()
    {
        _inner.OnGetKey = (id, _) => new(TestKeys.Create(id));
        CachedEncryptionKeyProvider cache = CreateCache();
        await cache.GetKeyAsync("k");

        _time.Advance(TimeToLive);
        _inner.OnGetKey = (_, _) => throw new TimeoutException("key service unreachable");

        await Assert.ThrowsAsync<TimeoutException>(async () => await cache.GetKeyAsync("k"));
    }

    [Fact]
    public async Task GetCurrentKeyAsync_InnerReturnsNull_Throws()
    {
        _inner.OnGetCurrentKey = _ => new((CryptographicKey)null!);
        CachedEncryptionKeyProvider cache = CreateCache();

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await cache.GetCurrentKeyAsync());
    }

    [Theory]
    [InlineData("\0current")]
    [InlineData("True")]
    [InlineData("current")]
    public async Task GetKeyAsync_DoesNotReturnCachedCurrentKey(string keyId)
    {
        _inner.OnGetCurrentKey = _ => new(TestKeys.Create("current"));
        _inner.OnGetKey = (_, _) => new((CryptographicKey?)null);
        CachedEncryptionKeyProvider cache = CreateCache();

        await cache.GetCurrentKeyAsync();

        Assert.Null(await cache.GetKeyAsync(keyId));
        Assert.Equal(1, _inner.KeyCalls);
    }

    [Fact]
    public async Task GetCurrentKeyAsync_DoesNotReturnKeyCachedById()
    {
        CryptographicKey byId = TestKeys.Create("k");
        CryptographicKey current = TestKeys.Create("current");
        _inner.OnGetKey = (_, _) => new(byId);
        _inner.OnGetCurrentKey = _ => new(current);
        CachedEncryptionKeyProvider cache = CreateCache();

        await cache.GetKeyAsync("k");

        Assert.Same(current, await cache.GetCurrentKeyAsync());
    }

    [Fact]
    public async Task GetKeyAsync_NullId_Throws()
    {
        CachedEncryptionKeyProvider cache = CreateCache();

        await Assert.ThrowsAsync<ArgumentNullException>(async () => await cache.GetKeyAsync(null!));
    }

    [Fact]
    public async Task GetKeyAsync_PassesCallerTokenWhenCacheIsFull()
    {
        _inner.OnGetKey = (id, _) => new(TestKeys.Create(id));
        CachedEncryptionKeyProvider cache = CreateCache(maxEntries: 1);
        using var cts = new CancellationTokenSource();

        await cache.GetKeyAsync("a");
        await cache.GetKeyAsync("b", cts.Token);

        Assert.Equal(2, _inner.KeyCalls);
        Assert.Equal(cts.Token, _inner.KeyTokens.Last());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_NonPositiveTimeToLive_Throws(int seconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CachedEncryptionKeyProvider(_inner, _time, TimeSpan.FromSeconds(seconds)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_NonPositiveMaxEntries_Throws(int maxEntries)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CachedEncryptionKeyProvider(_inner, _time, TimeToLive, maxEntries));
    }

    [Fact]
    public void Constructor_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => new CachedEncryptionKeyProvider(null!, _time, TimeToLive));
        Assert.Throws<ArgumentNullException>(() => new CachedEncryptionKeyProvider(_inner, null!, TimeToLive));
    }

    [Fact]
    public async Task CachedProvider_WorksWithEncryptionService()
    {
        StaticEncryptionKeyProvider keys = TestKeys.SingleKeyProvider();
        var service = new AesGcmEncryptionService(new CachedEncryptionKeyProvider(keys, _time, TimeToLive));

        EncryptedPayload payload = await service.EncryptAsync(new byte[] { 1, 2, 3 }, "aad"u8.ToArray());

        Assert.Equal([1, 2, 3], (await service.DecryptAsync(payload, "aad"u8.ToArray())).Value);
    }

    private CachedEncryptionKeyProvider CreateCache(int maxEntries = CachedEncryptionKeyProvider.DefaultMaxEntries) =>
        new(_inner, _time, TimeToLive, maxEntries);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            await Task.Delay(5, timeout.Token);
        }
    }
}
