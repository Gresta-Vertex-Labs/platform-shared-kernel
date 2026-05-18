using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.Extensions;

namespace SharedKernel.Caching.Redis;

/// <summary>
/// Redis Pub/Sub implementation of <see cref="ICacheInvalidationBus"/>.
/// Publishes cache invalidation signals over two channels:
/// a service-targeted channel and a broadcast channel.
/// </summary>
/// <remarks>
/// <para>
/// Channel names are derived from <see cref="CachingOptions.ServiceName"/>:
/// <list type="bullet">
///   <item><description>Targeted: <c>sharedkernel:cache:invalidation:{service-name}</c></description></item>
///   <item><description>Broadcast: <c>sharedkernel:cache:invalidation:broadcast</c></description></item>
/// </list>
/// </para>
/// <para>
/// All I/O goes through <see cref="IRedisChannelService"/> — this class has no direct dependency
/// on StackExchange.Redis or <c>IConnectionMultiplexer</c>.
/// </para>
/// <para>
/// <b>No delivery guarantees.</b> Messages are transmitted over Redis Pub/Sub (at-most-once).
/// Subscribers that are offline when a message is published will not receive it and must
/// rely on TTL expiry. This is not a substitute for <c>07.Messaging</c>.
/// </para>
/// </remarks>
internal sealed class RedisCacheInvalidationBus : ICacheInvalidationBus
{
    private const string BroadcastChannel = "sharedkernel:cache:invalidation:broadcast";

    private readonly IRedisChannelService _channelService;
    private readonly string _targetedChannel;
    private readonly string _serviceName;

    /// <summary>
    /// Initialises a new <see cref="RedisCacheInvalidationBus"/>.
    /// </summary>
    /// <param name="channelService">The Redis Pub/Sub channel service for all I/O.</param>
    /// <param name="options">Caching options supplying the service name for channel naming.</param>
    public RedisCacheInvalidationBus(
        IRedisChannelService channelService,
        IOptions<CachingOptions> options)
    {
        ArgumentNullException.ThrowIfNull(channelService);
        ArgumentNullException.ThrowIfNull(options);

        _channelService = channelService;
        _serviceName = options.Value.ServiceName;

        var normalised = _serviceName.ToLowerInvariant().Replace(' ', '-');
        _targetedChannel = $"sharedkernel:cache:invalidation:{normalised}";
    }

    /// <inheritdoc />
    public ValueTask PublishKeyInvalidationAsync(string[] keys, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(keys);

        var message = new CacheInvalidationMessage(
            SourceService: _serviceName,
            InvalidationType: CacheInvalidationType.Key,
            Keys: keys,
            Tags: null,
            CorrelationId: Activity.Current?.Id ?? Guid.NewGuid().ToString("N"),
            TimestampUtc: DateTimeOffset.UtcNow);

        return PublishInvalidationAsync(message, ct);
    }

    /// <inheritdoc />
    public ValueTask PublishTagInvalidationAsync(string[] tags, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tags);

        var message = new CacheInvalidationMessage(
            SourceService: _serviceName,
            InvalidationType: CacheInvalidationType.Tag,
            Keys: null,
            Tags: tags,
            CorrelationId: Activity.Current?.Id ?? Guid.NewGuid().ToString("N"),
            TimestampUtc: DateTimeOffset.UtcNow);

        return PublishInvalidationAsync(message, ct);
    }

    /// <inheritdoc />
    public ValueTask PublishBroadcastInvalidationAsync(CancellationToken ct = default)
    {
        var message = new CacheInvalidationMessage(
            SourceService: _serviceName,
            InvalidationType: CacheInvalidationType.All,
            Keys: null,
            Tags: null,
            CorrelationId: Activity.Current?.Id ?? Guid.NewGuid().ToString("N"),
            TimestampUtc: DateTimeOffset.UtcNow);

        return PublishOnChannelAsync(BroadcastChannel, message, ct);
    }

    /// <inheritdoc />
    public ValueTask PublishInvalidationAsync(CacheInvalidationMessage message, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        return PublishOnChannelAsync(_targetedChannel, message, ct);
    }

    private async ValueTask PublishOnChannelAsync(
        string channel,
        CacheInvalidationMessage message,
        CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(
            message,
            CacheInvalidationMessageJsonContext.Default.CacheInvalidationMessage);

        await _channelService.PublishAsync(channel, json, ct).ConfigureAwait(false);
    }
}
