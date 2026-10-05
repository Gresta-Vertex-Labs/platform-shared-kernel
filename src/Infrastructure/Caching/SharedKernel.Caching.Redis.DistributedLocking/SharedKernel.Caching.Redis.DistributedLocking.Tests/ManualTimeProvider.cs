namespace SharedKernel.Caching.Redis.DistributedLocking.Tests;

/// <summary>
/// A <see cref="TimeProvider"/> whose clock moves only through <see cref="Advance"/>. Timers fire on the
/// thread that advances the clock, in due-time order.
/// </summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<ManualTimer> _timers = [];
    private readonly DateTimeOffset _epoch = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private long _now;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp()
    {
        lock (_gate)
            return _now;
    }

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
            return _epoch.AddTicks(_now);
    }

    /// <summary>Gets the elapsed time since the provider was created.</summary>
    public TimeSpan Elapsed
    {
        get
        {
            lock (_gate)
                return TimeSpan.FromTicks(_now);
        }
    }

    /// <summary>Gets the number of timers that are not disposed.</summary>
    public int ActiveTimers
    {
        get
        {
            lock (_gate)
                return _timers.Count;
        }
    }

    /// <summary>Gets the due times, as elapsed time, of every scheduled timer.</summary>
    public IReadOnlyList<TimeSpan> DueTimes
    {
        get
        {
            lock (_gate)
                return _timers.Where(t => t.DueAt is not null).Select(t => TimeSpan.FromTicks(t.DueAt!.Value)).ToList();
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        lock (_gate)
            _timers.Add(timer);
        timer.Change(dueTime, period);
        return timer;
    }

    /// <summary>Moves the clock forward, firing every timer that falls due on the way.</summary>
    public void Advance(TimeSpan by)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(by, TimeSpan.Zero);

        long target;
        lock (_gate)
            target = _now + by.Ticks;

        while (true)
        {
            ManualTimer? due;
            lock (_gate)
            {
                due = _timers
                    .Where(t => t.DueAt is { } at && at <= target)
                    .MinBy(t => t.DueAt);

                if (due is null)
                {
                    _now = target;
                    return;
                }

                _now = Math.Max(_now, due.DueAt!.Value);
                due.DueAt = due.Period > 0 ? due.DueAt + due.Period : null;
            }

            due.Callback(due.State);
        }
    }

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        public TimerCallback Callback { get; } = callback;

        public object? State { get; } = state;

        public long? DueAt { get; set; }

        public long Period { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner._gate)
            {
                if (!owner._timers.Contains(this))
                    throw new ObjectDisposedException(nameof(ManualTimer));

                DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : owner._now + dueTime.Ticks;
                Period = period == Timeout.InfiniteTimeSpan ? 0 : period.Ticks;
                return true;
            }
        }

        public void Dispose()
        {
            lock (owner._gate)
                owner._timers.Remove(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
