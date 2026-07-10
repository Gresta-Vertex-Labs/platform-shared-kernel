using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Polly;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Primitives.Logging;
using StackExchange.Redis;

namespace SharedKernel.Caching.Redis.PubSub;

/// <summary>
/// <see cref="IRedisChannelService"/> implementation backed by StackExchange.Redis Pub/Sub.
/// Uses literal channel names exclusively — pattern subscriptions are not supported.
/// </summary>
/// <remarks>
/// <para>
/// All subscriber handler exceptions are caught, logged at <see cref="LogLevel.Error"/>, and
/// swallowed. The Redis subscriber thread is never exposed to handler failures.
/// </para>
/// <para>
/// When the Redis connection is lost, <see cref="ConnectionHealth"/> transitions to
/// <see cref="ConnectionHealthState.Reconnecting"/> (or <see cref="ConnectionHealthState.Disconnected"/>
/// when no reconnect attempt is in progress). On <c>ConnectionRestored</c>, all registered
/// channel subscriptions are atomically resubscribed and <see cref="ConnectionHealth"/> transitions
/// back to <see cref="ConnectionHealthState.Connected"/>.
/// </para>
/// </remarks>
internal sealed partial class RedisChannelService : IRedisChannelService
{
    private readonly IConnectionMultiplexer _multiplexer;
    private readonly ISubscriber _subscriber;
    private readonly ILogger<RedisChannelService> _logger;
    private readonly ResiliencePipeline? _pipeline;

    // Registry of active subscriptions: channel name → the Action wrapper registered with SE.Redis.
    // The lock object serialises both mutation of the registry and the reconnect replay loop,
    // so a concurrent SubscribeAsync cannot race with mid-replay resubscription.
    private readonly Dictionary<string, SubscriptionEntry> _handlers = new(StringComparer.Ordinal);
    private readonly object _registryLock = new();

    // Volatile field so reads do not require a lock. Interlocked is not needed because
    // ConnectionHealthState fits within a single word and volatile guarantees visibility.
    private volatile int _connectionHealth = (int)ConnectionHealthState.Connected;

    /// <summary>
    /// Initialises a new <see cref="RedisChannelService"/> using the supplied multiplexer.
    /// </summary>
    /// <param name="multiplexer">The singleton Redis connection multiplexer.</param>
    /// <param name="logger">Logger for structured error recording.</param>
    /// <param name="pipeline">
    /// Optional Polly resilience pipeline (circuit breaker). When <see langword="null"/>,
    /// publish operations are executed directly with no Polly overhead.
    /// </param>
    public RedisChannelService(
        IConnectionMultiplexer multiplexer,
        ILogger<RedisChannelService> logger,
        ResiliencePipeline? pipeline = null)
    {
        ArgumentNullException.ThrowIfNull(multiplexer);
        ArgumentNullException.ThrowIfNull(logger);
        _multiplexer = multiplexer;
        _subscriber = multiplexer.GetSubscriber();
        _logger = logger;
        _pipeline = pipeline;

        // Subscribe to multiplexer lifecycle events.
        _multiplexer.ConnectionRestored += OnConnectionRestored;
        _multiplexer.ConnectionFailed += OnConnectionFailed;
    }

    /// <summary>Returns the number of channels currently in the subscription registry (for testing).</summary>
    internal int SubscriptionCount
    {
        get { lock (_registryLock) { return _handlers.Count; } }
    }

    /// <inheritdoc />
    public ConnectionHealthState ConnectionHealth =>
        (ConnectionHealthState)_connectionHealth;

    /// <inheritdoc />
    public async ValueTask PublishAsync(string channel, string message, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        ArgumentNullException.ThrowIfNull(message);

        var redisChannel = RedisChannel.Literal(channel);

        if (_pipeline is not null)
        {
            await _pipeline.ExecuteAsync(
                async token => await _subscriber.PublishAsync(redisChannel, message).ConfigureAwait(false),
                ct).ConfigureAwait(false);
        }
        else
        {
            await _subscriber.PublishAsync(redisChannel, message).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async ValueTask SubscribeAsync(string channel, Func<string, ValueTask> handler, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        ArgumentNullException.ThrowIfNull(handler);

        var redisChannel = RedisChannel.Literal(channel);

        // Wrap the async handler in an Action that SE.Redis can invoke synchronously.
        // All exceptions are caught and logged — never propagated to the subscriber thread.
        Action<RedisChannel, RedisValue> wrapper = (ch, value) =>
        {
            var channelName = (string)ch!;
            var messageStr = (string?)value ?? string.Empty;
            // Fire-and-forget with exception containment.
            _ = InvokeHandlerSafeAsync(channelName, messageStr, handler);
        };

        // Hold the registry lock before subscribing, consistent with the reconnect replay logic.
        // This prevents a race between SubscribeAsync and a concurrent reconnect replay.
        lock (_registryLock)
        {
            _handlers[channel] = new SubscriptionEntry(handler, wrapper);
        }

        await _subscriber.SubscribeAsync(redisChannel, wrapper).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask UnsubscribeAsync(string channel, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);

        var redisChannel = RedisChannel.Literal(channel);

        Action<RedisChannel, RedisValue>? wrapper;
        lock (_registryLock)
        {
            if (_handlers.TryGetValue(channel, out var entry))
            {
                wrapper = entry.Wrapper;
                _handlers.Remove(channel);
            }
            else
            {
                wrapper = null;
            }
        }

        if (wrapper is not null)
        {
            await _subscriber.UnsubscribeAsync(redisChannel, wrapper).ConfigureAwait(false);
        }
        else
        {
            // Not subscribed — no-op.
            await _subscriber.UnsubscribeAsync(redisChannel).ConfigureAwait(false);
        }
    }

    // ─── Multiplexer event handlers ─────────────────────────────────────────────

    // Internal visibility allows the test project to simulate events without a live multiplexer.
    internal void OnConnectionRestored(object? sender, ConnectionFailedEventArgs e)
    {
        // Transition health state to Connected.
        _connectionHealth = (int)ConnectionHealthState.Connected;
        Log.ConnectionRestored(_logger);

        // Atomically replay all registered subscriptions.
        // Hold the lock for the entire replay to prevent a concurrent SubscribeAsync from
        // adding a channel mid-replay that gets resubscribed prematurely.
        lock (_registryLock)
        {
            foreach (var (channelName, entry) in _handlers)
            {
                try
                {
                    var redisChannel = RedisChannel.Literal(channelName);
                    // Use GetAwaiter().GetResult() because this is a synchronous event callback.
                    // SE.Redis fires this on a dedicated reconnect thread; blocking is acceptable here.
                    _subscriber.SubscribeAsync(redisChannel, entry.Wrapper).GetAwaiter().GetResult();
                    Log.ChannelResubscribed(_logger, channelName);
                }
                catch (Exception ex)
                {
                    // Log per-channel failures at Error but do not abort remaining channels.
                    Log.ChannelResubscriptionFailed(_logger, channelName, ex);
                }
            }
        }
    }

    // Internal visibility allows the test project to simulate events without a live multiplexer.
    internal void OnConnectionFailed(object? sender, ConnectionFailedEventArgs e)
    {
        // Determine whether the multiplexer is still attempting to reconnect.
        var newState = _multiplexer.IsConnected
            ? ConnectionHealthState.Connected
            : ConnectionHealthState.Reconnecting;

        _connectionHealth = (int)newState;
        Log.ConnectionFailed(_logger, newState);
    }

    // ─── Private helpers ─────────────────────────────────────────────────────────

    private async Task InvokeHandlerSafeAsync(string channelName, string message, Func<string, ValueTask> handler)
    {
        try
        {
            await handler(message).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.HandlerException(_logger, channelName, ex);
        }
    }

    // ─── Inner types ─────────────────────────────────────────────────────────────

    private sealed record SubscriptionEntry(
        Func<string, ValueTask> Handler,
        Action<RedisChannel, RedisValue> Wrapper);

    // ─── Logging ─────────────────────────────────────────────────────────────────

    private static partial class Log
    {
        [LoggerMessage(
            EventId = LoggingEventIdRanges.Caching + 500,
            Level = LogLevel.Error,
            Message = "Unhandled exception in Redis channel handler for channel '{ChannelName}'")]
        internal static partial void HandlerException(ILogger logger, string channelName, Exception exception);

        [LoggerMessage(
            EventId = LoggingEventIdRanges.Caching + 501,
            Level = LogLevel.Information,
            Message = "Redis connection restored; resubscribing all registered channels")]
        internal static partial void ConnectionRestored(ILogger logger);

        [LoggerMessage(
            EventId = LoggingEventIdRanges.Caching + 502,
            Level = LogLevel.Information,
            Message = "Successfully resubscribed channel '{ChannelName}' after reconnect")]
        internal static partial void ChannelResubscribed(ILogger logger, string channelName);

        [LoggerMessage(
            EventId = LoggingEventIdRanges.Caching + 503,
            Level = LogLevel.Error,
            Message = "Failed to resubscribe channel '{ChannelName}' after reconnect")]
        internal static partial void ChannelResubscriptionFailed(ILogger logger, string channelName, Exception exception);

        [LoggerMessage(
            EventId = LoggingEventIdRanges.Caching + 504,
            Level = LogLevel.Warning,
            Message = "Redis connection failed; transitioning health state to {NewState}")]
        internal static partial void ConnectionFailed(ILogger logger, ConnectionHealthState newState);
    }
}
