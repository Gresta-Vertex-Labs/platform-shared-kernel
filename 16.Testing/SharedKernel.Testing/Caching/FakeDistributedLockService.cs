using System.Collections.Concurrent;
using SharedKernel.Caching.Abstractions;

namespace SharedKernel.Testing.Caching;

/// <summary>
/// In-memory fake implementation of <see cref="IDistributedLockService"/> for use in unit tests.
/// </summary>
/// <remarks>
/// <para>
/// Enforces real exclusivity within the fake: a resource held by a lock or an unexpired lease
/// makes further acquisitions return <see langword="null"/>. Fencing tokens increase per resource.
/// Wait time is not simulated; every acquisition is a single attempt.
/// </para>
/// <para>
/// Lease expiry uses the <see cref="TimeProvider"/> passed to the constructor, so a test can
/// advance a <c>FakeTimeProvider</c> to expire a lease.
/// </para>
/// </remarks>
public sealed class FakeDistributedLockService : IDistributedLockService
{
    private readonly TimeProvider _timeProvider;
    private readonly object _gate = new();
    private readonly Dictionary<string, DateTimeOffset?> _held = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _fencingTokens = new(StringComparer.Ordinal);

    /// <summary>Creates a fake that uses <see cref="TimeProvider.System"/> for lease expiry.</summary>
    public FakeDistributedLockService()
        : this(TimeProvider.System)
    {
    }

    /// <summary>Creates a fake that uses <paramref name="timeProvider"/> for lease expiry.</summary>
    /// <param name="timeProvider">The time source.</param>
    public FakeDistributedLockService(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Gets or sets a value indicating whether every acquisition returns <see langword="null"/>, as if
    /// another holder had the resource.
    /// </summary>
    public bool SimulateContention { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether every acquisition throws
    /// <see cref="DistributedLockUnavailableException"/>, as if the lock store were unreachable.
    /// </summary>
    public bool SimulateUnavailable { get; set; }

    /// <summary>Gets every lock handed out, in acquisition order.</summary>
    public IReadOnlyList<FakeDistributedLock> AcquiredLocks
    {
        get
        {
            lock (_gate)
            {
                return [.. _acquiredLocks];
            }
        }
    }

    /// <summary>Gets every lease handed out, in acquisition order.</summary>
    public IReadOnlyList<DistributedLease> AcquiredLeases
    {
        get
        {
            lock (_gate)
            {
                return [.. _acquiredLeases];
            }
        }
    }

    private readonly List<FakeDistributedLock> _acquiredLocks = [];
    private readonly List<DistributedLease> _acquiredLeases = [];

    /// <inheritdoc />
    public ValueTask<IDistributedLock?> TryAcquireAsync(
        string resource,
        DistributedLockOptions? options = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            if (!TryClaim(resource, expiresAt: null))
                return ValueTask.FromResult<IDistributedLock?>(null);

            var handle = new FakeDistributedLock(resource, NextToken(resource), () => ReleaseLock(resource));
            _acquiredLocks.Add(handle);
            return ValueTask.FromResult<IDistributedLock?>(handle);
        }
    }

    /// <inheritdoc />
    public ValueTask<DistributedLease?> TryAcquireLeaseAsync(
        string resource,
        TimeSpan duration,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, TimeSpan.Zero);
        ct.ThrowIfCancellationRequested();

        lock (_gate)
        {
            DateTimeOffset expiresAt = _timeProvider.GetUtcNow() + duration;
            if (!TryClaim(resource, expiresAt))
                return ValueTask.FromResult<DistributedLease?>(null);

            var lease = new DistributedLease(resource, NextToken(resource), expiresAt);
            _acquiredLeases.Add(lease);
            return ValueTask.FromResult<DistributedLease?>(lease);
        }
    }

    // Caller holds _gate. A null expiry marks a lock, which is held until released.
    private bool TryClaim(string resource, DateTimeOffset? expiresAt)
    {
        if (SimulateUnavailable)
            throw DistributedLockUnavailableException.ForResource(resource);

        if (SimulateContention)
            return false;

        if (_held.TryGetValue(resource, out var existing)
            && (existing is null || existing > _timeProvider.GetUtcNow()))
        {
            return false;
        }

        _held[resource] = expiresAt;
        return true;
    }

    private long NextToken(string resource) => _fencingTokens.AddOrUpdate(resource, 1, static (_, token) => token + 1);

    private void ReleaseLock(string resource)
    {
        lock (_gate)
        {
            if (_held.TryGetValue(resource, out var expiresAt) && expiresAt is null)
                _held.Remove(resource);
        }
    }
}

/// <summary>A lock handed out by <see cref="FakeDistributedLockService"/>.</summary>
public sealed class FakeDistributedLock : IDistributedLock
{
    private readonly Action _release;
    private readonly CancellationTokenSource _lost = new();
    private int _state;

    internal FakeDistributedLock(string resource, long fencingToken, Action release)
    {
        Resource = resource;
        FencingToken = fencingToken;
        _release = release;
    }

    /// <inheritdoc />
    public string Resource { get; }

    /// <inheritdoc />
    public long FencingToken { get; }

    /// <inheritdoc />
    public bool IsHeld => Volatile.Read(ref _state) == 0;

    /// <summary>Gets a value indicating whether the lock was released by disposal.</summary>
    public bool IsReleased => Volatile.Read(ref _state) == 2;

    /// <inheritdoc />
    public CancellationToken LostToken => _lost.Token;

    /// <summary>Simulates losing the lock: <see cref="IsHeld"/> turns false and <see cref="LostToken"/> is cancelled.</summary>
    public void SimulateLoss()
    {
        if (Interlocked.CompareExchange(ref _state, 1, 0) == 0)
        {
            _release();
            _lost.Cancel();
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _state, 2, 0) == 0)
            _release();

        _lost.Cancel();
        return ValueTask.CompletedTask;
    }
}
