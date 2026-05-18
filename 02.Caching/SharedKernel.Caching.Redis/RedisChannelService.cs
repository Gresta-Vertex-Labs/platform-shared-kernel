using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SharedKernel.Caching.Abstractions;
using StackExchange.Redis;

namespace SharedKernel.Caching.Redis;

/// <summary>
/// <see cref="IRedisChannelService"/> implementation backed by StackExchange.Redis Pub/Sub.
/// Uses literal channel names exclusively — pattern subscriptions are not supported.
/// </summary>
/// <remarks>
/// All subscriber handler exceptions are caught, logged at <see cref="LogLevel.Error"/>, and
/// swallowed. The Redis subscriber thread is never exposed to handler failures.
/// </remarks>
internal sealed partial class RedisChannelService : IRedisChannelService
{
    private readonly ISubscriber _subscriber;
    private readonly ILogger<RedisChannelService> _logger;

    // Registry of active subscriptions: channel name → the Action wrapper we registered with SE.Redis.
    private readonly ConcurrentDictionary<string, Action<RedisChannel, RedisValue>> _handlers = new(StringComparer.Ordinal);

    /// <summary>
    /// Initialises a new <see cref="RedisChannelService"/> using the supplied multiplexer.
    /// </summary>
    /// <param name="multiplexer">The singleton Redis connection multiplexer.</param>
    /// <param name="logger">Logger for structured error recording.</param>
    public RedisChannelService(IConnectionMultiplexer multiplexer, ILogger<RedisChannelService> logger)
    {
        ArgumentNullException.ThrowIfNull(multiplexer);
        ArgumentNullException.ThrowIfNull(logger);
        _subscriber = multiplexer.GetSubscriber();
        _logger = logger;
    }

    /// <inheritdoc />
    public async ValueTask PublishAsync(string channel, string message, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        ArgumentNullException.ThrowIfNull(message);

        var redisChannel = RedisChannel.Literal(channel);
        await _subscriber.PublishAsync(redisChannel, message).ConfigureAwait(false);
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

        // Store in registry before subscribing so UnsubscribeAsync can clean up.
        _handlers[channel] = wrapper;

        await _subscriber.SubscribeAsync(redisChannel, wrapper).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask UnsubscribeAsync(string channel, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);

        var redisChannel = RedisChannel.Literal(channel);

        if (_handlers.TryRemove(channel, out var wrapper))
        {
            await _subscriber.UnsubscribeAsync(redisChannel, wrapper).ConfigureAwait(false);
        }
        else
        {
            // Not subscribed — no-op.
            await _subscriber.UnsubscribeAsync(redisChannel).ConfigureAwait(false);
        }
    }

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

    private static partial class Log
    {
        [LoggerMessage(
            EventId = 3001,
            Level = LogLevel.Error,
            Message = "Unhandled exception in Redis channel handler for channel '{ChannelName}'")]
        internal static partial void HandlerException(ILogger logger, string channelName, Exception exception);
    }
}
