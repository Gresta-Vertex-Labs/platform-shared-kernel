using Microsoft.Extensions.Options;
using SharedKernel.Security.Oidc.Revocation;
using Xunit;

namespace SharedKernel.Security.Oidc.Tests.Revocation;

/// <summary>
/// Proves <see cref="CachingTokenRevocationCheck"/>'s cache-first / call-through-and-populate-on-miss
/// behavior, and — the invariant that matters most in this seam — that a genuine revocation is never
/// masked by a stale cached "not revoked" entry beyond <see cref="RevocationCheckCacheOptions.RevokedTtl"/>
/// (WO-060, P-388, T-36).
/// </summary>
/// <remarks>
/// Time is driven explicitly via <see cref="ControllableClockCache"/>'s injectable "now" delegate — never
/// <see cref="Task.Delay(TimeSpan)"/> against a real clock, and never unseeded randomness.
/// </remarks>
public sealed class CachingTokenRevocationCheckTests
{
    private static readonly RevocationCheckCacheOptions DefaultOptions = new();

    [Fact]
    public async Task NotRevokedResult_CachedWithinNotRevokedTtl_AvoidsSecondInnerCall()
    {
        var currentTime = DateTimeOffset.UtcNow;
        var cache = new ControllableClockCache(() => currentTime);
        var inner = new CountingRevocationCheck(revoked: false);
        var sut = new CachingTokenRevocationCheck(inner, cache, Microsoft.Extensions.Options.Options.Create(new RevocationCheckCacheOptions()));

        var first = await sut.IsRevokedAsync("token", CancellationToken.None);

        // Well within NotRevokedTtl's default 30s window.
        currentTime = currentTime.AddSeconds(10);
        var second = await sut.IsRevokedAsync("token", CancellationToken.None);

        Assert.False(first);
        Assert.False(second);
        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public async Task RevokedOutcome_CachedWithShorterRevokedTtl_SurfacesAgain_SoonerThanNotRevokedWould()
    {
        // Sequence: a "not revoked" result is cached first (moments before, as in real traffic against
        // a token that later gets revoked) — then, once the underlying check reports "revoked", that
        // outcome must be cached under the DELIBERATELY SHORT RevokedTtl (5s default), not the longer
        // NotRevokedTtl (30s) — so a revocation is re-verified soon rather than trusted for as long as a
        // "not revoked" verdict would be. This is the invariant CachingTokenRevocationCheck must uphold:
        // a "revoked" outcome is never allowed to go as stale as a "not revoked" one.
        var currentTime = DateTimeOffset.UtcNow;
        var cache = new ControllableClockCache(() => currentTime);
        var inner = new ScriptedRevocationCheck(revoked: false);
        var sut = new CachingTokenRevocationCheck(inner, cache, Microsoft.Extensions.Options.Options.Create(new RevocationCheckCacheOptions()));

        // A "not revoked" result is cached moments before...
        var first = await sut.IsRevokedAsync("token", CancellationToken.None);
        Assert.False(first);
        Assert.Equal(1, inner.CallCount);

        // ...the NotRevokedTtl window fully elapses, so the decorator calls through again — and this
        // time the token has been revoked.
        inner.NextResult = true;
        currentTime += DefaultOptions.NotRevokedTtl + TimeSpan.FromSeconds(1);
        var second = await sut.IsRevokedAsync("token", CancellationToken.None);
        Assert.True(second);
        Assert.Equal(2, inner.CallCount);

        // The revoked outcome must be reflected/re-checked within RevokedTtl (5s) — NOT held onto for
        // the longer NotRevokedTtl (30s) — proving the cache write used RevokedTtl for the revoked case.
        currentTime += DefaultOptions.RevokedTtl + TimeSpan.FromSeconds(1);
        var third = await sut.IsRevokedAsync("token", CancellationToken.None);
        Assert.Equal(3, inner.CallCount);
        Assert.True(third);
    }

    [Fact]
    public async Task CacheReadFailure_FallsThroughToInnerCheck_NeverTreatedAsNotRevoked()
    {
        var cache = new ThrowingCache();
        var inner = new CountingRevocationCheck(revoked: true);
        var sut = new CachingTokenRevocationCheck(inner, cache, Microsoft.Extensions.Options.Options.Create(new RevocationCheckCacheOptions()));

        var result = await sut.IsRevokedAsync("token", CancellationToken.None);

        Assert.True(result);
        Assert.Equal(1, inner.CallCount);
    }

    private sealed class CountingRevocationCheck(bool revoked) : ITokenRevocationCheck
    {
        public int CallCount { get; private set; }

        public Task<bool> IsRevokedAsync(string tokenIdentifier, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(revoked);
        }
    }

    private sealed class ScriptedRevocationCheck(bool revoked) : ITokenRevocationCheck
    {
        public bool NextResult { get; set; } = revoked;

        public int CallCount { get; private set; }

        public Task<bool> IsRevokedAsync(string tokenIdentifier, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(NextResult);
        }
    }

    /// <summary>
    /// A cache double backed by an injectable "now" delegate so tests can drive time explicitly, never
    /// via a real wall-clock wait.
    /// </summary>
    private sealed class ControllableClockCache(Func<DateTimeOffset> now) : IRevocationCheckCache
    {
        private readonly Dictionary<string, (bool IsRevoked, DateTimeOffset ExpiresAt)> _entries = [];

        public Task<bool?> TryGetAsync(string tokenIdentifier, CancellationToken ct)
        {
            if (_entries.TryGetValue(tokenIdentifier, out var entry) && entry.ExpiresAt > now())
            {
                return Task.FromResult<bool?>(entry.IsRevoked);
            }

            return Task.FromResult<bool?>(null);
        }

        public Task SetAsync(string tokenIdentifier, bool isRevoked, TimeSpan ttl, CancellationToken ct)
        {
            _entries[tokenIdentifier] = (isRevoked, now() + ttl);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingCache : IRevocationCheckCache
    {
        public Task<bool?> TryGetAsync(string tokenIdentifier, CancellationToken ct) =>
            throw new InvalidOperationException("Simulated unreadable cache.");

        public Task SetAsync(string tokenIdentifier, bool isRevoked, TimeSpan ttl, CancellationToken ct) =>
            throw new InvalidOperationException("Simulated unwritable cache.");
    }
}
