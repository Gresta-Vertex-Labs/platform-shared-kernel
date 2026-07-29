using System.Collections.Concurrent;
using SharedKernel.Caching.Abstractions;

namespace SharedKernel.Testing.Caching;

/// <summary>
/// In-memory fake implementation of <see cref="IRedisChannelService"/> for use in unit tests.
/// Performs genuine in-process publish/subscribe fan-out — thread-safe, zero Redis dependency.
/// </summary>
/// <remarks>
/// <para>
/// Unlike most fakes in this package, <see cref="PublishAsync"/> does not merely record the
/// call — it synchronously invokes every handler currently subscribed to the published channel,
/// in subscription order, so a publish/subscribe/unsubscribe round trip can be proven within a
/// single process with no real Redis. A handler exception is caught and swallowed, never
/// propagated to the caller, mirroring the production <c>RedisChannelService</c>'s documented
/// "handler exceptions must never propagate to the Redis subscriber thread" rule.
/// </para>
/// <para>
/// <see cref="ConnectionHealth"/> is a plain settable property — it is never driven by a real
/// connection event, unlike the production implementation's automatic
/// <c>ConnectionRestored</c>/<c>ConnectionFailed</c> tracking. Set it directly to simulate what a
/// health-check consumer would observe.
/// </para>
/// <para>
/// This fake also deliberately does <b>not</b> replicate the production <c>RedisChannelService</c>'s
/// own reconnect/replay machinery — subscribing to <c>IConnectionMultiplexer.ConnectionRestored</c>/
/// <c>ConnectionFailed</c> and resubscribing every channel after a reconnect. There is no real
/// connection to lose, so there is nothing to replay.
/// </para>
/// <para>
/// <b>TEST-ONLY — NEVER PRODUCTION-SAFE.</b> This type must never be wired into a production DI
/// container — <c>16.Testing</c> packages are never referenced by production code (root
/// <c>CLAUDE.md</c> hard rule). Its unbounded <see cref="PublishedMessages"/> list and its total lack
/// of reconnect/replay behavior make it unsuitable for anything but a short-lived test process.
/// </para>
/// </remarks>
public sealed class FakeRedisChannelService : IRedisChannelService
{
    private readonly ConcurrentDictionary<string, ConcurrentBag<Func<string, ValueTask>>> _subscriptions = new();
    private readonly ConcurrentQueue<(string Channel, string Message)> _published = new();

    /// <summary>
    /// Gets or sets the simulated Redis connection health. Defaults to
    /// <see cref="ConnectionHealthState.Connected"/>.
    /// </summary>
    public ConnectionHealthState ConnectionHealth { get; set; } = ConnectionHealthState.Connected;

    /// <summary>
    /// When <see langword="true"/>, <see cref="PublishAsync"/>, <see cref="SubscribeAsync"/>, and
    /// <see cref="UnsubscribeAsync"/> all throw <see cref="InvalidOperationException"/> instead of
    /// performing the operation.
    /// </summary>
    public bool SimulateFailure { get; set; }

    /// <summary>Gets every (channel, message) pair successfully published so far, in publish order.</summary>
    public IReadOnlyList<(string Channel, string Message)> PublishedMessages => [.. _published];

    /// <summary>Gets every channel that currently has at least one active (not yet unsubscribed) handler.</summary>
    public IReadOnlyList<string> SubscribedChannels => [.. _subscriptions.Keys];

    /// <inheritdoc />
    public async ValueTask PublishAsync(string channel, string message, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        ArgumentNullException.ThrowIfNull(message);

        if (SimulateFailure)
            throw new InvalidOperationException("Simulated Redis publish failure.");

        _published.Enqueue((channel, message));

        if (!_subscriptions.TryGetValue(channel, out var handlers))
            return;

        foreach (var handler in handlers)
        {
            try
            {
                await handler(message).ConfigureAwait(false);
            }
            catch
            {
                // Handler exceptions are caught and swallowed here, mirroring RedisChannelService's
                // own "must never propagate to the Redis subscriber thread" rule.
            }
        }
    }

    /// <inheritdoc />
    public ValueTask SubscribeAsync(string channel, Func<string, ValueTask> handler, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        ArgumentNullException.ThrowIfNull(handler);

        if (SimulateFailure)
            throw new InvalidOperationException("Simulated Redis subscribe failure.");

        _subscriptions.AddOrUpdate(
            channel,
            static (_, h) => [h],
            static (_, existing, h) =>
            {
                existing.Add(h);
                return existing;
            },
            handler);

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask UnsubscribeAsync(string channel, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);

        if (SimulateFailure)
            throw new InvalidOperationException("Simulated Redis unsubscribe failure.");

        // Wholesale removal (never a per-item bag mutation) to avoid a torn-bag read racing a
        // concurrent PublishAsync enumeration of the same channel's handler list.
        _subscriptions.TryRemove(channel, out _);

        return ValueTask.CompletedTask;
    }

    /// <summary>Gets whether <paramref name="channel"/> currently has at least one active subscription.</summary>
    /// <param name="channel">The channel name to check.</param>
    public bool IsSubscribed(string channel) => _subscriptions.ContainsKey(channel);

    /// <summary>Clears every recorded published message and every subscription.</summary>
    public void Reset()
    {
        _published.Clear();
        _subscriptions.Clear();
    }
}
