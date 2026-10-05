using Xunit;

namespace SharedKernel.Caching.Redis.PubSub.Tests;

/// <summary>Collects the messages a handler receives and lets a test wait for them.</summary>
internal sealed class MessageRecorder<T>
{
    private readonly object _gate = new();
    private readonly List<T> _messages = [];

    public IReadOnlyList<T> Messages
    {
        get
        {
            lock (_gate)
                return _messages.ToArray();
        }
    }

    public void Add(T message)
    {
        lock (_gate)
            _messages.Add(message);
    }

    /// <summary>A handler that records each message.</summary>
    public ValueTask Handle(T message, CancellationToken ct)
    {
        Add(message);
        return ValueTask.CompletedTask;
    }

    /// <summary>Waits until at least <paramref name="count"/> messages have arrived.</summary>
    public async Task WaitForCountAsync(int count, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (Messages.Count < count)
        {
            if (DateTime.UtcNow > deadline)
                Assert.Fail($"Expected at least {count} messages; received {Messages.Count}: [{string.Join(", ", Messages)}].");

            await Task.Delay(10);
        }
    }

    /// <summary>Waits until a message matching <paramref name="predicate"/> has arrived.</summary>
    public async Task WaitForAsync(Func<T, bool> predicate, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (!Messages.Any(predicate))
        {
            if (DateTime.UtcNow > deadline)
                Assert.Fail($"The expected message did not arrive; received [{string.Join(", ", Messages)}].");

            await Task.Delay(10);
        }
    }
}
