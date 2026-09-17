using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using SharedKernel.Caching.Redis.PubSub;

namespace SharedKernel.Testing.Caching;

/// <summary>
/// In-memory fake implementation of <see cref="IRedisChannelService"/> for use in unit tests. Delivers
/// published messages to in-process subscriptions; thread-safe, no Redis needed.
/// </summary>
/// <remarks>
/// <para>
/// <b>Delivery.</b> <see cref="PublishAsync(string, string, CancellationToken)"/> records the message, hands it
/// to every subscription on the channel and returns how many there were. When no delivery is already running
/// for a subscription, the publisher delivers the message itself before returning, so in a test that publishes
/// from one flow every handler has run by the time the <c>await</c> completes. A message published from inside
/// a handler, or while another caller is delivering to the same subscription, is queued and delivered by that
/// running delivery after the current handler returns.
/// </para>
/// <para>
/// <b>Subscriptions.</b> As in production, every <c>SubscribeAsync</c> call creates an independent
/// subscription that handles its messages one at a time, in publish order, until disposed. Disposing waits for
/// a running handler unless called from inside it, and cancels the token passed to the handler. A handler
/// exception is recorded in <see cref="HandlerExceptions"/> and delivery continues; a typed message that does
/// not deserialize is recorded in <see cref="SkippedMessages"/> and skipped.
/// </para>
/// <para>
/// <b>Differences from Redis.</b> The receiver count is the number of subscriptions on this fake, whereas Redis
/// counts connections. There is no connection to lose, so nothing is ever missed or replayed.
/// </para>
/// <para>
/// <b>TEST-ONLY.</b> Never register it in a production container; <see cref="PublishedMessages"/> grows without
/// bound.
/// </para>
/// </remarks>
public sealed class FakeRedisChannelService : IRedisChannelService
{
    private readonly object _gate = new();
    private readonly Dictionary<string, List<Subscription>> _subscriptions = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<(string Channel, string Message)> _published = new();
    private readonly ConcurrentQueue<Exception> _handlerExceptions = new();
    private readonly ConcurrentQueue<(string Channel, string Message)> _skipped = new();

    /// <summary>
    /// Gets or sets a value indicating whether publishing and subscribing throw <see cref="TimeoutException"/>
    /// after argument validation, as if Redis did not answer.
    /// </summary>
    public bool SimulateFailure { get; set; }

    /// <summary>Gets every (channel, message) pair published so far, in publish order; typed messages as JSON.</summary>
    public IReadOnlyList<(string Channel, string Message)> PublishedMessages => [.. _published];

    /// <summary>Gets every channel that has at least one active subscription.</summary>
    public IReadOnlyList<string> SubscribedChannels
    {
        get
        {
            lock (_gate)
            {
                return [.. _subscriptions.Keys];
            }
        }
    }

    /// <summary>Gets every exception a handler threw, in the order they were caught.</summary>
    public IReadOnlyList<Exception> HandlerExceptions => [.. _handlerExceptions];

    /// <summary>Gets every (channel, message) pair a typed subscription skipped because it did not deserialize.</summary>
    public IReadOnlyList<(string Channel, string Message)> SkippedMessages => [.. _skipped];

    /// <inheritdoc />
    public async ValueTask<long> PublishAsync(string channel, string message, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        ArgumentNullException.ThrowIfNull(message);
        ct.ThrowIfCancellationRequested();
        ThrowIfFailing();

        Subscription[] receivers;
        lock (_gate)
        {
            _published.Enqueue((channel, message));
            receivers = _subscriptions.TryGetValue(channel, out var list) ? [.. list] : [];
            foreach (var receiver in receivers)
                receiver.Enqueue(message);
        }

        foreach (var receiver in receivers)
            await receiver.DeliverPendingAsync().ConfigureAwait(false);

        return receivers.Length;
    }

    /// <inheritdoc />
    public ValueTask<long> PublishAsync<T>(string channel, T message, JsonTypeInfo<T> typeInfo, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        return PublishAsync(channel, JsonSerializer.Serialize(message, typeInfo), ct);
    }

    /// <inheritdoc />
    public ValueTask<IAsyncDisposable> SubscribeAsync(
        string channel,
        Func<string, CancellationToken, ValueTask> handler,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return SubscribeCore(channel, handler, ct);
    }

    /// <inheritdoc />
    public ValueTask<IAsyncDisposable> SubscribeAsync<T>(
        string channel,
        JsonTypeInfo<T> typeInfo,
        Func<T, CancellationToken, ValueTask> handler,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        ArgumentNullException.ThrowIfNull(handler);

        return SubscribeCore(
            channel,
            (message, token) =>
            {
                T value;
                try
                {
                    value = JsonSerializer.Deserialize(message, typeInfo)!;
                }
                catch (JsonException)
                {
                    _skipped.Enqueue((channel, message));
                    return ValueTask.CompletedTask;
                }

                return handler(value, token);
            },
            ct);
    }

    /// <summary>Gets whether <paramref name="channel"/> has at least one active subscription.</summary>
    /// <param name="channel">The channel name.</param>
    /// <returns><see langword="true"/> when at least one subscription is active.</returns>
    public bool IsSubscribed(string channel) => GetSubscriptionCount(channel) > 0;

    /// <summary>Gets how many active subscriptions <paramref name="channel"/> has.</summary>
    /// <param name="channel">The channel name.</param>
    /// <returns>The number of subscriptions not yet disposed.</returns>
    public int GetSubscriptionCount(string channel)
    {
        lock (_gate)
        {
            return _subscriptions.TryGetValue(channel, out var list) ? list.Count : 0;
        }
    }

    /// <summary>Gets the messages published to one channel, in publish order.</summary>
    /// <param name="channel">The channel name.</param>
    /// <returns>The messages; typed messages as JSON.</returns>
    public IReadOnlyList<string> GetPublishedMessages(string channel) =>
        [.. _published.Where(entry => string.Equals(entry.Channel, channel, StringComparison.Ordinal)).Select(entry => entry.Message)];

    /// <summary>Gets the messages published to one channel, deserialized.</summary>
    /// <typeparam name="T">The message type.</typeparam>
    /// <param name="channel">The channel name.</param>
    /// <param name="typeInfo">The JSON contract for <typeparamref name="T"/>.</param>
    /// <returns>The messages, in publish order.</returns>
    /// <exception cref="JsonException">A message on the channel is not a valid <typeparamref name="T"/>.</exception>
    public IReadOnlyList<T> GetPublishedMessages<T>(string channel, JsonTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        return [.. GetPublishedMessages(channel).Select(message => JsonSerializer.Deserialize(message, typeInfo)!)];
    }

    /// <summary>
    /// Clears the recorded messages and exceptions and ends every subscription without waiting for running handlers.
    /// </summary>
    public void Reset()
    {
        Subscription[] all;
        lock (_gate)
        {
            all = [.. _subscriptions.Values.SelectMany(list => list)];
            _subscriptions.Clear();
        }

        foreach (var subscription in all)
            subscription.Stop();

        _published.Clear();
        _handlerExceptions.Clear();
        _skipped.Clear();
    }

    private ValueTask<IAsyncDisposable> SubscribeCore(
        string channel,
        Func<string, CancellationToken, ValueTask> handler,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        ct.ThrowIfCancellationRequested();
        ThrowIfFailing();

        var subscription = new Subscription(this, channel, handler);
        lock (_gate)
        {
            if (!_subscriptions.TryGetValue(channel, out var list))
            {
                list = [];
                _subscriptions[channel] = list;
            }

            list.Add(subscription);
        }

        return ValueTask.FromResult<IAsyncDisposable>(subscription);
    }

    private void Remove(Subscription subscription)
    {
        lock (_gate)
        {
            if (_subscriptions.TryGetValue(subscription.Channel, out var list) && list.Remove(subscription) && list.Count == 0)
                _subscriptions.Remove(subscription.Channel);
        }
    }

    private void ThrowIfFailing()
    {
        if (SimulateFailure)
            throw new TimeoutException("Simulated Redis pub/sub failure.");
    }

    private sealed class Subscription(
        FakeRedisChannelService owner,
        string channel,
        Func<string, CancellationToken, ValueTask> handler) : IAsyncDisposable
    {
        // True on the flow running this subscription's handler, so disposing from inside it does not wait for itself.
        private readonly AsyncLocal<bool> _inHandler = new();

        private readonly ConcurrentQueue<string> _pending = new();
        private readonly CancellationTokenSource _stopping = new();
        private readonly SemaphoreSlim _delivering = new(1, 1);
        private int _stopped;

        public string Channel { get; } = channel;

        public void Enqueue(string message)
        {
            if (Volatile.Read(ref _stopped) == 0)
                _pending.Enqueue(message);
        }

        public async ValueTask DeliverPendingAsync()
        {
            // Re-entrant publish from inside this subscription's handler: the running delivery picks it up.
            if (_inHandler.Value)
                return;

            while (!_pending.IsEmpty && Volatile.Read(ref _stopped) == 0)
            {
                // Another caller is delivering; it drains the queue, including this message.
                if (!_delivering.Wait(0))
                    return;

                try
                {
                    while (Volatile.Read(ref _stopped) == 0 && _pending.TryDequeue(out var message))
                        await InvokeAsync(message).ConfigureAwait(false);
                }
                finally
                {
                    _delivering.Release();
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (!Stop())
                return;

            if (_inHandler.Value)
                return;

            // Waits for a handler that is running on another flow.
            await _delivering.WaitAsync().ConfigureAwait(false);
            _delivering.Release();
        }

        public bool Stop()
        {
            if (Interlocked.Exchange(ref _stopped, 1) == 1)
                return false;

            owner.Remove(this);
            _pending.Clear();
            _stopping.Cancel();
            return true;
        }

        private async ValueTask InvokeAsync(string message)
        {
            var token = _stopping.Token;
            _inHandler.Value = true;
            try
            {
                await handler(message, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // Disposed while the handler ran.
            }
            catch (Exception ex)
            {
                owner._handlerExceptions.Enqueue(ex);
            }
            finally
            {
                _inHandler.Value = false;
            }
        }
    }
}
