using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace SharedKernel.Caching.Redis.PubSub;

/// <summary><see cref="IRedisChannelService"/> over the shared <see cref="IConnectionMultiplexer"/>.</summary>
internal sealed class RedisChannelService(IConnectionMultiplexer multiplexer, ILogger<RedisChannelService> logger)
    : IRedisChannelService
{
    public async ValueTask<long> PublishAsync(string channel, string message, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        ArgumentNullException.ThrowIfNull(message);
        ct.ThrowIfCancellationRequested();

        return await multiplexer.GetSubscriber().PublishAsync(RedisChannel.Literal(channel), message).ConfigureAwait(false);
    }

    public ValueTask<long> PublishAsync<T>(string channel, T message, JsonTypeInfo<T> typeInfo, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        return PublishAsync(channel, JsonSerializer.Serialize(message, typeInfo), ct);
    }

    public ValueTask<IAsyncDisposable> SubscribeAsync(
        string channel,
        Func<string, CancellationToken, ValueTask> handler,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return SubscribeCoreAsync(channel, handler, ct);
    }

    public ValueTask<IAsyncDisposable> SubscribeAsync<T>(
        string channel,
        JsonTypeInfo<T> typeInfo,
        Func<T, CancellationToken, ValueTask> handler,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        ArgumentNullException.ThrowIfNull(handler);

        return SubscribeCoreAsync(
            channel,
            (message, token) =>
            {
                T value;
                try
                {
                    value = JsonSerializer.Deserialize(message, typeInfo)!;
                }
                catch (JsonException ex)
                {
                    RedisChannelSubscription.Log.MessageNotDeserialized(logger, channel, typeof(T).Name, ex);
                    return ValueTask.CompletedTask;
                }

                return handler(value, token);
            },
            ct);
    }

    private async ValueTask<IAsyncDisposable> SubscribeCoreAsync(
        string channel,
        Func<string, CancellationToken, ValueTask> handler,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        ct.ThrowIfCancellationRequested();

        // A queue per subscription: its own ordered delivery, and unsubscribing it leaves other subscriptions alone.
        var queue = await multiplexer.GetSubscriber().SubscribeAsync(RedisChannel.Literal(channel)).ConfigureAwait(false);
        return RedisChannelSubscription.Start(channel, queue, handler, logger);
    }
}
