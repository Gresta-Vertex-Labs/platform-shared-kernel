using System.Collections.Concurrent;
using SharedKernel.Caching.Abstractions;

namespace SharedKernel.Testing.Caching;

/// <summary>
/// In-memory fake implementation of <see cref="ICacheWarmupStrategy"/> for use in unit tests.
/// </summary>
/// <remarks>
/// <para>
/// A test proves <c>CacheWarmupHostedService</c>'s documented "sorts/orders strategies, catches
/// per-strategy exceptions, logs at Error, and continues" contract by constructing several
/// <see cref="FakeCacheWarmupStrategy"/> instances that all share the <b>same</b>
/// <see cref="ConcurrentQueue{T}"/> passed to each constructor, mixing <see cref="SimulateFailure"/>
/// across them, running them through the consumer's own ordering/dispatch loop, then asserting the
/// queue's contents reflect every strategy having run — in <see cref="Order"/> — including the ones
/// that threw. The shared queue is an explicit, caller-supplied instance, never a static field —
/// this package permits only two documented static-mutable-state exceptions, and this is not a
/// third.
/// </para>
/// </remarks>
public sealed class FakeCacheWarmupStrategy : ICacheWarmupStrategy
{
    private readonly ConcurrentQueue<string>? _executionLog;
    private int _callCount;

    /// <summary>Initializes a new instance of <see cref="FakeCacheWarmupStrategy"/>.</summary>
    /// <param name="name">The fixed, human-readable strategy name.</param>
    /// <param name="order">The fixed execution order relative to other registered strategies.</param>
    /// <param name="executionLog">
    /// An optional shared queue that <see cref="WarmupAsync"/> appends <paramref name="name"/> to on
    /// every call — pass the same instance to multiple strategies to prove cross-strategy execution
    /// order.
    /// </param>
    public FakeCacheWarmupStrategy(string name, int order = 0, ConcurrentQueue<string>? executionLog = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name;
        Order = order;
        _executionLog = executionLog;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public int Order { get; }

    /// <summary>Gets the number of times <see cref="WarmupAsync"/> has been called.</summary>
    public int CallCount => Volatile.Read(ref _callCount);

    /// <summary>
    /// When <see langword="true"/>, <see cref="WarmupAsync"/> throws
    /// <see cref="InvalidOperationException"/> after recording the call.
    /// </summary>
    public bool SimulateFailure { get; set; }

    /// <summary>
    /// Optional callback invoked synchronously with the <see cref="ICacheService"/> instance
    /// <see cref="WarmupAsync"/> received, before the <see cref="SimulateFailure"/> check.
    /// </summary>
    public Action<ICacheService>? OnWarmup { get; set; }

    /// <inheritdoc />
    public ValueTask WarmupAsync(ICacheService cache, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(cache);

        Interlocked.Increment(ref _callCount);
        OnWarmup?.Invoke(cache);

        // Recorded unconditionally — before evaluating SimulateFailure — so a failing strategy's
        // execution is still visible in the shared ordering log.
        _executionLog?.Enqueue(Name);

        if (SimulateFailure)
            throw new InvalidOperationException($"Simulated warmup failure for strategy '{Name}'.");

        return ValueTask.CompletedTask;
    }
}
