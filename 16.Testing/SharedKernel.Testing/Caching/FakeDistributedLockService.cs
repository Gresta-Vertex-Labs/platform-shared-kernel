using SharedKernel.Caching.Abstractions;

namespace SharedKernel.Testing.Caching;

/// <summary>
/// In-memory fake implementation of <see cref="IDistributedLockService"/> for use in unit tests.
/// Thread-safe, no external dependencies.
/// </summary>
/// <remarks>
/// <para>
/// Locks are always granted immediately — there is no contention simulation.
/// Pass a <c>simulateFailure</c> flag to constructors or use the
/// <see cref="SimulateFailure"/> property to make all subsequent acquire calls return
/// <see langword="null"/>.
/// </para>
/// <para>
/// <see cref="AcquireRenewableAsync"/> returns a <see cref="FakeRenewableLock"/> that
/// tracks how many times <see cref="IRenewableLock.RenewAsync"/> has been called via
/// <see cref="FakeRenewableLock.RenewalCount"/>.
/// </para>
/// </remarks>
public sealed class FakeDistributedLockService : IDistributedLockService
{
    /// <summary>
    /// When <see langword="true"/>, both <see cref="AcquireAsync"/> and
    /// <see cref="AcquireRenewableAsync"/> return <see langword="null"/> (simulating
    /// a contended or unavailable lock).
    /// </summary>
    public bool SimulateFailure { get; set; }

    /// <inheritdoc />
    public Task<IAsyncDisposable?> AcquireAsync(
        string resource,
        TimeSpan expiry,
        TimeSpan wait,
        TimeSpan retry,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);

        if (SimulateFailure)
            return Task.FromResult<IAsyncDisposable?>(null);

        return Task.FromResult<IAsyncDisposable?>(new FakeLockHandle());
    }

    /// <inheritdoc />
    public ValueTask<IRenewableLock?> AcquireRenewableAsync(
        string resource,
        TimeSpan expiry,
        TimeSpan wait,
        TimeSpan retry,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);

        if (SimulateFailure)
            return ValueTask.FromResult<IRenewableLock?>(null);

        return ValueTask.FromResult<IRenewableLock?>(new FakeRenewableLock());
    }

    // ----- nested types -----

    /// <summary>
    /// Minimal <see cref="IAsyncDisposable"/> returned by <see cref="AcquireAsync"/>.
    /// </summary>
    private sealed class FakeLockHandle : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

/// <summary>
/// Fake <see cref="IRenewableLock"/> for use in unit tests.
/// Tracks how many times <see cref="RenewAsync"/> has been called via
/// <see cref="RenewalCount"/>.
/// </summary>
/// <remarks>
/// <see cref="IsAcquired"/> defaults to <see langword="true"/> and transitions to
/// <see langword="false"/> after <see cref="IAsyncDisposable.DisposeAsync"/> is called
/// or when <see cref="SimulateRenewalFailure"/> is <see langword="true"/>.
/// </remarks>
public sealed class FakeRenewableLock : IRenewableLock
{
    private bool _disposed;

    /// <summary>
    /// Gets the number of times <see cref="RenewAsync"/> has been called.
    /// </summary>
    public int RenewalCount { get; private set; }

    /// <summary>
    /// Gets or sets the fencing token reported by this fake lock.
    /// </summary>
    /// <remarks>
    /// Minimal compile-time-only implementation of <c>02.Caching</c> Phase 43's
    /// <c>IFencedLock.FencingToken</c> requirement (added to <c>IRenewableLock</c>).
    /// Defaults to <c>1</c> and does not auto-increment on <see cref="RenewAsync"/> —
    /// a caller-driven, behaviorally-faithful fake (auto-incrementing on renewal, with
    /// dedicated test coverage) remains a queued follow-up for this domain, out of
    /// <c>02.Caching</c>'s jurisdiction (see <c>02.Caching/CLAUDE.md</c>, Phase 43, FT-10).
    /// </remarks>
    public long FencingToken { get; set; } = 1;

    /// <summary>
    /// When <see langword="true"/>, <see cref="RenewAsync"/> returns
    /// <see langword="false"/> and sets <see cref="IsAcquired"/> to
    /// <see langword="false"/>, simulating a lost lock.
    /// </summary>
    public bool SimulateRenewalFailure { get; set; }

    /// <inheritdoc />
    public bool IsAcquired => !_disposed && !SimulateRenewalFailure;

    /// <inheritdoc />
    public ValueTask<bool> RenewAsync(CancellationToken ct = default)
    {
        if (_disposed || SimulateRenewalFailure)
            return ValueTask.FromResult(false);

        RenewalCount++;
        return ValueTask.FromResult(true);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _disposed = true;
        return ValueTask.CompletedTask;
    }
}
