using System.Collections.Concurrent;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Hosting;
using SharedKernel.Caching.Redis.PubSub;
using Shop.Catalog.Application.Products;

namespace Shop.Catalog.Infrastructure.Pricing;

/// <summary>The Redis channel price changes travel on.</summary>
public static class PriceChannels
{
    public const string PriceChanged = "catalog:price-changed";
}

[JsonSerializable(typeof(PriceChange))]
internal sealed partial class PriceChangeJsonContext : JsonSerializerContext;

/// <summary>Publishes price changes on Redis Pub/Sub.</summary>
public sealed class RedisPriceChangeBroadcaster(IRedisChannelService channels)
    : IPriceChangeBroadcaster
{
    public async Task PublishAsync(PriceChange change, CancellationToken cancellationToken) =>
        await channels.PublishAsync(
            PriceChannels.PriceChanged,
            change,
            PriceChangeJsonContext.Default.PriceChange,
            cancellationToken
        );
}

/// <summary>
/// The price changes this replica heard, newest last. A replica serves it on <c>/ops/price-changes</c>, which is how
/// the end-to-end tests prove a change made on one replica reached the others.
/// </summary>
public sealed class PriceChangeLog
{
    private const int Capacity = 100;
    private readonly ConcurrentQueue<PriceChange> _heard = new();

    /// <summary>The changes heard so far, oldest first.</summary>
    public IReadOnlyList<PriceChange> Heard => [.. _heard];

    internal void Record(PriceChange change)
    {
        _heard.Enqueue(change);
        while (_heard.Count > Capacity && _heard.TryDequeue(out _)) { }
    }
}

/// <summary>Subscribes to <see cref="PriceChannels.PriceChanged"/> for the lifetime of the host.</summary>
public sealed class PriceChangeListener(IRedisChannelService channels, PriceChangeLog log)
    : IHostedService,
        IAsyncDisposable
{
    private IAsyncDisposable? _subscription;

    public async Task StartAsync(CancellationToken cancellationToken) =>
        _subscription = await channels.SubscribeAsync(
            PriceChannels.PriceChanged,
            PriceChangeJsonContext.Default.PriceChange,
            (change, _) =>
            {
                log.Record(change);
                return ValueTask.CompletedTask;
            },
            cancellationToken
        );

    public Task StopAsync(CancellationToken cancellationToken) => DisposeAsync().AsTask();

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _subscription, null) is { } subscription)
        {
            await subscription.DisposeAsync();
        }
    }
}
