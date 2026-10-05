namespace SharedKernel.Caching.Abstractions;

/// <summary>Settings for acquiring a distributed lock.</summary>
/// <remarks>Every property validates on assignment, so an invalid instance cannot exist.</remarks>
public sealed record DistributedLockOptions
{
    private readonly TimeSpan _expiry = TimeSpan.FromSeconds(30);
    private readonly TimeSpan _waitTime = TimeSpan.Zero;
    private readonly TimeSpan _retryInterval = TimeSpan.FromMilliseconds(200);

    /// <summary>Gets the default settings: 30-second expiry, a single attempt, 200 ms retry interval.</summary>
    public static DistributedLockOptions Default { get; } = new();

    /// <summary>
    /// Gets how long the lock survives in the store if its holder stops keeping it alive, for
    /// example after a crash. Must be positive. Defaults to 30 seconds.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public TimeSpan Expiry
    {
        get => _expiry;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero, nameof(Expiry));
            _expiry = value;
        }
    }

    /// <summary>
    /// Gets how long to keep retrying while another holder has the resource. Zero means a single
    /// attempt. Must not be negative. Defaults to zero.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public TimeSpan WaitTime
    {
        get => _waitTime;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, TimeSpan.Zero, nameof(WaitTime));
            _waitTime = value;
        }
    }

    /// <summary>Gets the pause between attempts during <see cref="WaitTime"/>. Must be positive. Defaults to 200 ms.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not positive.</exception>
    public TimeSpan RetryInterval
    {
        get => _retryInterval;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero, nameof(RetryInterval));
            _retryInterval = value;
        }
    }
}
