using System.Diagnostics;
using SharedKernel.Caching.Abstractions;
using Xunit;

namespace SharedKernel.Caching.Redis.DistributedLocking.Tests.Abstractions;

/// <summary>
/// Behavioural contract every <see cref="IDistributedLockService"/> implementation must satisfy.
/// Derive once per implementation and supply the service under test.
/// </summary>
public abstract class IDistributedLockServiceContractTests
{
    private static readonly DistributedLockOptions ShortLock = new() { Expiry = TimeSpan.FromSeconds(5) };

    /// <summary>Gets the implementation under test.</summary>
    protected abstract IDistributedLockService Service { get; }

    /// <summary>Returns a resource name no other test uses.</summary>
    protected virtual string NewResource(string prefix) => $"contract:{prefix}:{Guid.NewGuid():N}";

    // -------------------------------------------------------------------------
    // Locks
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TryAcquireAsync_FreeResource_ReturnsHeldHandle()
    {
        var resource = NewResource("free");

        await using var handle = await Service.TryAcquireAsync(resource, ShortLock);

        Assert.NotNull(handle);
        Assert.Equal(resource, handle.Resource);
        Assert.True(handle.FencingToken > 0);
        Assert.True(handle.IsHeld);
        Assert.False(handle.LostToken.IsCancellationRequested);
    }

    [Fact]
    public async Task TryAcquireAsync_WhileHeld_ReturnsNull()
    {
        var resource = NewResource("contended");

        await using var holder = await Service.TryAcquireAsync(resource, ShortLock);
        Assert.NotNull(holder);

        var contender = await Service.TryAcquireAsync(resource, ShortLock);

        Assert.Null(contender);
        Assert.True(holder.IsHeld);
    }

    [Fact]
    public async Task TryAcquireAsync_AfterRelease_SucceedsWithGreaterFencingToken()
    {
        var resource = NewResource("reacquire");

        var first = await Service.TryAcquireAsync(resource, ShortLock);
        Assert.NotNull(first);
        await first.DisposeAsync();

        await using var second = await Service.TryAcquireAsync(resource, ShortLock);

        Assert.NotNull(second);
        Assert.True(second.FencingToken > first.FencingToken,
            $"Token after release ({second.FencingToken}) must exceed the released token ({first.FencingToken}).");
    }

    [Fact]
    public async Task DisposeAsync_IsIdempotent_AndEndsTheLock()
    {
        var resource = NewResource("dispose");

        var handle = await Service.TryAcquireAsync(resource, ShortLock);
        Assert.NotNull(handle);

        await handle.DisposeAsync();
        await handle.DisposeAsync();

        Assert.False(handle.IsHeld);
        Assert.True(handle.LostToken.IsCancellationRequested);

        await using var next = await Service.TryAcquireAsync(resource, ShortLock);
        Assert.NotNull(next);
    }

    [Fact]
    public async Task TryAcquireAsync_WaitTimeLongerThanRemainingHold_AcquiresAfterRelease()
    {
        var resource = NewResource("wait-acquires");

        var holder = await Service.TryAcquireAsync(resource, ShortLock);
        Assert.NotNull(holder);

        var release = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(400));
            await holder.DisposeAsync();
        });

        var waiterOptions = ShortLock with
        {
            WaitTime = TimeSpan.FromSeconds(5),
            RetryInterval = TimeSpan.FromMilliseconds(50),
        };
        await using var waiter = await Service.TryAcquireAsync(resource, waiterOptions);
        await release;

        Assert.NotNull(waiter);
        Assert.True(waiter.FencingToken > holder.FencingToken);
    }

    [Fact]
    public async Task TryAcquireAsync_WaitTimeZero_ReturnsNullImmediately()
    {
        var resource = NewResource("wait-zero");

        await using var holder = await Service.TryAcquireAsync(resource, ShortLock);
        Assert.NotNull(holder);

        var stopwatch = Stopwatch.StartNew();
        var contender = await Service.TryAcquireAsync(resource, ShortLock with { WaitTime = TimeSpan.Zero });
        stopwatch.Stop();

        Assert.Null(contender);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1),
            $"A single attempt should not wait; took {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task TryAcquireAsync_WaitTimeShorterThanHold_ReturnsNullAfterWaiting()
    {
        var resource = NewResource("wait-expires");

        await using var holder = await Service.TryAcquireAsync(resource, ShortLock);
        Assert.NotNull(holder);

        var waitTime = TimeSpan.FromMilliseconds(300);
        var stopwatch = Stopwatch.StartNew();
        var contender = await Service.TryAcquireAsync(
            resource,
            ShortLock with { WaitTime = waitTime, RetryInterval = TimeSpan.FromMilliseconds(50) });
        stopwatch.Stop();

        Assert.Null(contender);
        Assert.True(stopwatch.Elapsed >= waitTime - TimeSpan.FromMilliseconds(50),
            $"The attempt should keep retrying for the wait time; gave up after {stopwatch.Elapsed}.");
    }

    // -------------------------------------------------------------------------
    // Leases
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TryAcquireLeaseAsync_FreeResource_ReturnsLease()
    {
        var resource = NewResource("lease");
        var duration = TimeSpan.FromSeconds(10);

        var before = DateTimeOffset.UtcNow;
        var lease = await Service.TryAcquireLeaseAsync(resource, duration);
        var after = DateTimeOffset.UtcNow;

        Assert.NotNull(lease);
        Assert.Equal(resource, lease.Resource);
        Assert.True(lease.FencingToken > 0);
        Assert.InRange(lease.ExpiresAt, before + duration - TimeSpan.FromSeconds(1), after + duration + TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task TryAcquireLeaseAsync_WhileLeaseLives_BlocksLeasesAndLocks()
    {
        var resource = NewResource("lease-blocks");

        var lease = await Service.TryAcquireLeaseAsync(resource, TimeSpan.FromSeconds(10));
        Assert.NotNull(lease);

        Assert.Null(await Service.TryAcquireLeaseAsync(resource, TimeSpan.FromSeconds(10)));
        Assert.Null(await Service.TryAcquireAsync(resource, ShortLock));
    }

    [Fact]
    public async Task TryAcquireLeaseAsync_WhileLockHeld_ReturnsNull()
    {
        var resource = NewResource("lock-blocks-lease");

        await using var holder = await Service.TryAcquireAsync(resource, ShortLock);
        Assert.NotNull(holder);

        Assert.Null(await Service.TryAcquireLeaseAsync(resource, TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task TryAcquireLeaseAsync_AfterDurationElapses_SucceedsWithGreaterFencingToken()
    {
        var resource = NewResource("lease-expires");
        var duration = TimeSpan.FromMilliseconds(300);

        var first = await Service.TryAcquireLeaseAsync(resource, duration);
        Assert.NotNull(first);

        await Task.Delay(duration + TimeSpan.FromMilliseconds(300));

        var second = await Service.TryAcquireLeaseAsync(resource, duration);

        Assert.NotNull(second);
        Assert.True(second.FencingToken > first.FencingToken);
    }

    // -------------------------------------------------------------------------
    // Fencing
    // -------------------------------------------------------------------------

    [Fact]
    public async Task FencingTokens_StrictlyIncreaseAcrossLocksAndLeases()
    {
        var resource = NewResource("mixed-fencing");
        var leaseDuration = TimeSpan.FromMilliseconds(200);
        var tokens = new List<long>();

        for (var i = 0; i < 3; i++)
        {
            var handle = await Service.TryAcquireAsync(resource, ShortLock);
            Assert.NotNull(handle);
            tokens.Add(handle.FencingToken);
            await handle.DisposeAsync();

            var lease = await Service.TryAcquireLeaseAsync(resource, leaseDuration);
            Assert.NotNull(lease);
            tokens.Add(lease.FencingToken);
            await Task.Delay(leaseDuration + TimeSpan.FromMilliseconds(200));
        }

        for (var i = 1; i < tokens.Count; i++)
        {
            Assert.True(tokens[i] > tokens[i - 1],
                $"Token {i} ({tokens[i]}) must exceed token {i - 1} ({tokens[i - 1]}): [{string.Join(", ", tokens)}].");
        }
    }

    [Fact]
    public async Task FencingToken_StaleHolderIsRejectedByAGuardThatAcceptedANewerToken()
    {
        var resource = NewResource("stale-guard");

        var stale = await Service.TryAcquireAsync(resource, ShortLock);
        Assert.NotNull(stale);
        await stale.DisposeAsync();

        await using var current = await Service.TryAcquireAsync(resource, ShortLock);
        Assert.NotNull(current);

        // A minimal stand-in for the protected resource's write path.
        var lastAccepted = 0L;
        Assert.True(TryAcceptWrite(current.FencingToken, ref lastAccepted));
        Assert.False(TryAcceptWrite(stale.FencingToken, ref lastAccepted));
        Assert.Equal(current.FencingToken, lastAccepted);
    }

    // -------------------------------------------------------------------------
    // Concurrency
    // -------------------------------------------------------------------------

    [Fact]
    public async Task TryAcquireAsync_ParallelAttempts_ExactlyOneWins()
    {
        var resource = NewResource("parallel-lock");

        var attempts = Enumerable.Range(0, 20)
            .Select(_ => Task.Run(() => Service.TryAcquireAsync(resource, ShortLock).AsTask()));
        var results = await Task.WhenAll(attempts);

        try
        {
            Assert.Single(results, r => r is not null);
        }
        finally
        {
            foreach (var handle in results.OfType<IDistributedLock>())
                await handle.DisposeAsync();
        }
    }

    [Fact]
    public async Task TryAcquireLeaseAsync_ParallelAttempts_ExactlyOneWins()
    {
        var resource = NewResource("parallel-lease");

        var attempts = Enumerable.Range(0, 20)
            .Select(_ => Task.Run(() => Service.TryAcquireLeaseAsync(resource, TimeSpan.FromSeconds(10)).AsTask()));
        var results = await Task.WhenAll(attempts);

        Assert.Single(results, r => r is not null);
    }

    // -------------------------------------------------------------------------
    // Argument validation and cancellation
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task TryAcquireAsync_BlankResource_ThrowsArgumentException(string resource)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Service.TryAcquireAsync(resource).AsTask());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task TryAcquireLeaseAsync_BlankResource_ThrowsArgumentException(string resource)
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => Service.TryAcquireLeaseAsync(resource, TimeSpan.FromSeconds(1)).AsTask());
    }

    [Fact]
    public async Task NullResource_ThrowsArgumentException()
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(() => Service.TryAcquireAsync(null!).AsTask());
        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => Service.TryAcquireLeaseAsync(null!, TimeSpan.FromSeconds(1)).AsTask());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task TryAcquireLeaseAsync_NonPositiveDuration_ThrowsArgumentOutOfRangeException(int milliseconds)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => Service.TryAcquireLeaseAsync(NewResource("bad-duration"), TimeSpan.FromMilliseconds(milliseconds)).AsTask());
    }

    [Fact]
    public async Task CancelledToken_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Service.TryAcquireAsync(NewResource("cancelled"), ShortLock, cts.Token).AsTask());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Service.TryAcquireLeaseAsync(NewResource("cancelled"), TimeSpan.FromSeconds(1), cts.Token).AsTask());
    }

    private static bool TryAcceptWrite(long candidateToken, ref long lastAcceptedToken)
    {
        if (candidateToken <= lastAcceptedToken)
            return false;

        lastAcceptedToken = candidateToken;
        return true;
    }
}
