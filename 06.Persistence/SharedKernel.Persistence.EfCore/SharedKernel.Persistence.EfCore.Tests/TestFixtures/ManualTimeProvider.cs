namespace SharedKernel.Persistence.EfCore.Tests.TestFixtures;

/// <summary>
/// A <see cref="TimeProvider"/> whose clock and timers move only when a test calls <see cref="Advance"/>, so
/// <see cref="PeriodicTimer"/>-driven code can be tested without real delays.
/// </summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<ManualTimer> _timers = [];
    private DateTimeOffset _utcNow = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _utcNow;
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        lock (_gate)
        {
            _timers.Add(timer);
        }

        return timer;
    }

    /// <summary>Moves the clock forward and fires every timer that has come due, in order.</summary>
    public void Advance(TimeSpan by)
    {
        DateTimeOffset now;
        ManualTimer[] timers;
        lock (_gate)
        {
            _utcNow += by;
            now = _utcNow;
            timers = [.. _timers];
        }

        foreach (var timer in timers)
        {
            timer.FireIfDue(now);
        }
    }

    private void Remove(ManualTimer timer)
    {
        lock (_gate)
        {
            _timers.Remove(timer);
        }
    }

    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        private DateTimeOffset? _next;
        private TimeSpan _period;

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (this)
            {
                _next = dueTime == Timeout.InfiniteTimeSpan ? null : owner.GetUtcNow() + dueTime;
                _period = period;
            }

            return true;
        }

        public void FireIfDue(DateTimeOffset now)
        {
            while (true)
            {
                lock (this)
                {
                    if (_next is null || _next > now)
                    {
                        return;
                    }

                    _next = _period > TimeSpan.Zero && _period != Timeout.InfiniteTimeSpan ? _next + _period : null;
                }

                callback(state);
            }
        }

        public void Dispose()
        {
            lock (this)
            {
                _next = null;
            }

            owner.Remove(this);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
