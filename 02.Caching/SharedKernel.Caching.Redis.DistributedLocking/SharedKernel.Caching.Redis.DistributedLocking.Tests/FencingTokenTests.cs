using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.Redis.DistributedLocking.Extensions;
using Testcontainers.Redis;
using Xunit;

namespace SharedKernel.Caching.Redis.DistributedLocking.Tests;

/// <summary>
/// Integration tests for the Phase 43 fencing-token feature: <see cref="IFencedLock"/> and
/// its wiring into <see cref="IDistributedLockService.AcquireAsync"/> and
/// <see cref="IDistributedLockService.AcquireRenewableAsync"/>. Uses Testcontainers to spin
/// up a real Redis instance, since the fencing counter is an atomic server-side <c>INCR</c>.
/// </summary>
[Collection("Redis")]
public sealed class FencingTokenTests : IAsyncLifetime
{
    private readonly RedisContainer _redisContainer = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();

    private ServiceProvider? _provider;

    public async Task InitializeAsync()
    {
        await _redisContainer.StartAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRedisDistributedLocking(_redisContainer.GetConnectionString());
        _provider = services.BuildServiceProvider();
    }

    public async Task DisposeAsync()
    {
        if (_provider is not null)
            await _provider.DisposeAsync();

        await _redisContainer.DisposeAsync();
    }

    private IDistributedLockService LockService =>
        _provider!.GetRequiredService<IDistributedLockService>();

    // -------------------------------------------------------------------------
    // FT-06 — two sequential acquisitions of the same resource (after release)
    // produce strictly increasing tokens.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AcquireAsync_HandleImplementsIFencedLock_WithPositiveToken()
    {
        var resource = "fencing:acquire-implements-" + Guid.NewGuid();

        await using var handle = await LockService.AcquireAsync(
            resource,
            expiry: TimeSpan.FromSeconds(30),
            wait: TimeSpan.FromSeconds(5),
            retry: TimeSpan.FromMilliseconds(200));

        Assert.NotNull(handle);
        var fenced = Assert.IsAssignableFrom<IFencedLock>(handle);
        Assert.True(fenced.FencingToken > 0);
    }

    [Fact]
    public async Task AcquireAsync_SequentialAcquisitionsAfterRelease_ProduceStrictlyIncreasingTokens()
    {
        var resource = "fencing:sequential-" + Guid.NewGuid();

        var firstHandle = await LockService.AcquireAsync(
            resource,
            expiry: TimeSpan.FromSeconds(30),
            wait: TimeSpan.FromSeconds(5),
            retry: TimeSpan.FromMilliseconds(200));

        Assert.NotNull(firstHandle);
        var firstToken = ((IFencedLock)firstHandle!).FencingToken;
        await firstHandle.DisposeAsync();

        await using var secondHandle = await LockService.AcquireAsync(
            resource,
            expiry: TimeSpan.FromSeconds(30),
            wait: TimeSpan.FromSeconds(5),
            retry: TimeSpan.FromMilliseconds(200));

        Assert.NotNull(secondHandle);
        var secondToken = ((IFencedLock)secondHandle!).FencingToken;

        Assert.True(secondToken > firstToken,
            $"Second acquisition's token ({secondToken}) must be strictly greater than the first ({firstToken}).");
    }

    [Fact]
    public async Task AcquireAsync_FailedContendedAttempt_DoesNotAdvanceCounter()
    {
        // Rule 5 (Ph43): the INCR fires only after a *successful* acquisition — never on a
        // failed/contended attempt. Prove this by holding the lock, failing a contended
        // acquire, then releasing and re-acquiring: the re-acquisition's token must be
        // exactly one greater than the original — not bumped by the failed attempt.
        var resource = "fencing:contended-" + Guid.NewGuid();

        var firstHandle = await LockService.AcquireAsync(
            resource,
            expiry: TimeSpan.FromSeconds(30),
            wait: TimeSpan.FromSeconds(5),
            retry: TimeSpan.FromMilliseconds(200));

        Assert.NotNull(firstHandle);
        var firstToken = ((IFencedLock)firstHandle!).FencingToken;

        // Contended, non-blocking attempt — must fail while firstHandle is held.
        var contendedAttempt = await LockService.AcquireAsync(
            resource,
            expiry: TimeSpan.FromSeconds(30),
            wait: TimeSpan.Zero,
            retry: TimeSpan.FromMilliseconds(50));
        Assert.Null(contendedAttempt);

        await firstHandle.DisposeAsync();

        await using var secondHandle = await LockService.AcquireAsync(
            resource,
            expiry: TimeSpan.FromSeconds(30),
            wait: TimeSpan.FromSeconds(5),
            retry: TimeSpan.FromMilliseconds(200));

        Assert.NotNull(secondHandle);
        var secondToken = ((IFencedLock)secondHandle!).FencingToken;

        Assert.Equal(firstToken + 1, secondToken);
    }

    // -------------------------------------------------------------------------
    // FT-07 — renewal produces a strictly greater token than pre-renewal, and a
    // downstream "reject non-increasing token" guard correctly rejects the stale
    // pre-renewal token once the post-renewal token has already been accepted.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AcquireRenewableAsync_HandleExposesPositiveFencingToken()
    {
        var resource = "fencing:renewable-initial-" + Guid.NewGuid();

        await using var handle = await LockService.AcquireRenewableAsync(
            resource,
            expiry: TimeSpan.FromSeconds(30),
            wait: TimeSpan.FromSeconds(5),
            retry: TimeSpan.FromMilliseconds(200));

        Assert.NotNull(handle);
        Assert.True(handle!.FencingToken > 0);
    }

    [Fact]
    public async Task RenewAsync_Succeeds_ProducesStrictlyGreaterFencingToken()
    {
        var resource = "fencing:renewal-" + Guid.NewGuid();

        await using var handle = await LockService.AcquireRenewableAsync(
            resource,
            expiry: TimeSpan.FromSeconds(30),
            wait: TimeSpan.FromSeconds(5),
            retry: TimeSpan.FromMilliseconds(200));

        Assert.NotNull(handle);
        var preRenewalToken = handle!.FencingToken;

        var renewed = await handle.RenewAsync();

        Assert.True(renewed);
        var postRenewalToken = handle.FencingToken;

        Assert.True(postRenewalToken > preRenewalToken,
            $"Post-renewal token ({postRenewalToken}) must be strictly greater than " +
            $"pre-renewal token ({preRenewalToken}).");
    }

    [Fact]
    public async Task RenewAsync_MultipleRenewals_EachProducesAStrictlyGreaterToken()
    {
        var resource = "fencing:renewal-multi-" + Guid.NewGuid();

        await using var handle = await LockService.AcquireRenewableAsync(
            resource,
            expiry: TimeSpan.FromSeconds(30),
            wait: TimeSpan.FromSeconds(5),
            retry: TimeSpan.FromMilliseconds(200));

        Assert.NotNull(handle);
        var previousToken = handle!.FencingToken;

        for (var i = 0; i < 3; i++)
        {
            var renewed = await handle.RenewAsync();
            Assert.True(renewed, $"Renewal {i + 1} should succeed");

            var currentToken = handle.FencingToken;
            Assert.True(currentToken > previousToken,
                $"Renewal {i + 1}: token ({currentToken}) must exceed the previous token ({previousToken}).");
            previousToken = currentToken;
        }
    }

    [Fact]
    public async Task RenewAsync_StalePreRenewalToken_IsRejectedByDownstreamGuard_OncePostRenewalTokenAccepted()
    {
        var resource = "fencing:stale-guard-" + Guid.NewGuid();

        await using var handle = await LockService.AcquireRenewableAsync(
            resource,
            expiry: TimeSpan.FromSeconds(30),
            wait: TimeSpan.FromSeconds(5),
            retry: TimeSpan.FromMilliseconds(200));

        Assert.NotNull(handle);
        var preRenewalToken = handle!.FencingToken;

        var renewed = await handle.RenewAsync();
        Assert.True(renewed);
        var postRenewalToken = handle.FencingToken;

        // A minimal stand-in for "the protected resource's own write path" — the standard
        // fencing-token usage contract documented on IFencedLock.FencingToken.
        var lastAcceptedToken = 0L;

        // The (correctly ordered, in this scenario) write from the renewed handle is accepted.
        Assert.True(TryAcceptWrite(postRenewalToken, ref lastAcceptedToken));

        // A write attempted from the stale, pre-renewal handle arriving afterward — simulating
        // the documented "brief unprotected window" — must be rejected as non-increasing.
        Assert.False(TryAcceptWrite(preRenewalToken, ref lastAcceptedToken));

        // The guard's recorded state must be unaffected by the rejected stale write.
        Assert.Equal(postRenewalToken, lastAcceptedToken);
    }

    // Standard fencing-token consumer guard: reject any write whose token is not strictly
    // greater than the last-accepted token, per IFencedLock.FencingToken's documented
    // usage contract.
    private static bool TryAcceptWrite(long candidateToken, ref long lastAcceptedToken)
    {
        if (candidateToken <= lastAcceptedToken)
            return false;

        lastAcceptedToken = candidateToken;
        return true;
    }

    // -------------------------------------------------------------------------
    // FT-08 — fencing tokens are scoped per-resource: two different resources
    // acquired concurrently do not share or interfere with each other's sequence.
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DifferentResources_HaveIndependentFencingSequences()
    {
        var resourceA = "fencing:resource-a-" + Guid.NewGuid();
        var resourceB = "fencing:resource-b-" + Guid.NewGuid();

        // Each resource is fresh (GUID-scoped) — a first acquisition on a brand-new resource
        // must always yield token 1. If the counter were global rather than per-resource,
        // the second resource's first acquisition would observe a token > 1 (bumped by the
        // first resource's own acquisition).
        await using var firstOnA = await LockService.AcquireAsync(
            resourceA,
            expiry: TimeSpan.FromSeconds(30),
            wait: TimeSpan.FromSeconds(5),
            retry: TimeSpan.FromMilliseconds(200));
        Assert.NotNull(firstOnA);
        Assert.Equal(1L, ((IFencedLock)firstOnA!).FencingToken);

        await using var firstOnB = await LockService.AcquireAsync(
            resourceB,
            expiry: TimeSpan.FromSeconds(30),
            wait: TimeSpan.FromSeconds(5),
            retry: TimeSpan.FromMilliseconds(200));
        Assert.NotNull(firstOnB);
        Assert.Equal(1L, ((IFencedLock)firstOnB!).FencingToken);
    }

    [Fact]
    public async Task AdvancingOneResourcesSequence_DoesNotAffectAnothers()
    {
        var resourceA = "fencing:advance-a-" + Guid.NewGuid();
        var resourceB = "fencing:advance-b-" + Guid.NewGuid();

        // Acquire and release resource A three times, advancing its counter to 3.
        long lastTokenOnA = 0;
        for (var i = 0; i < 3; i++)
        {
            var handle = await LockService.AcquireAsync(
                resourceA,
                expiry: TimeSpan.FromSeconds(30),
                wait: TimeSpan.FromSeconds(5),
                retry: TimeSpan.FromMilliseconds(200));
            Assert.NotNull(handle);
            lastTokenOnA = ((IFencedLock)handle!).FencingToken;
            await handle.DisposeAsync();
        }

        Assert.Equal(3L, lastTokenOnA);

        // Resource B, untouched until now, must still start at 1 — proving A's three
        // acquisitions never advanced B's independent sequence.
        await using var firstOnB = await LockService.AcquireAsync(
            resourceB,
            expiry: TimeSpan.FromSeconds(30),
            wait: TimeSpan.FromSeconds(5),
            retry: TimeSpan.FromMilliseconds(200));
        Assert.NotNull(firstOnB);
        Assert.Equal(1L, ((IFencedLock)firstOnB!).FencingToken);
    }
}
