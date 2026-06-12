using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Caching.Abstractions;

namespace SharedKernel.Caching.Redis.PubSub;

/// <summary>
/// Redis Pub/Sub implementation of <see cref="ICacheInvalidationBus"/>.
/// Publishes cache invalidation signals over two channels:
/// a service-targeted channel and a broadcast channel.
/// </summary>
/// <remarks>
/// <para>
/// Channel names are derived from <see cref="CachingCoreOptions.ServiceName"/>:
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
/// <para>
/// <b>Default ServiceName warning:</b> if <see cref="CachingCoreOptions.ServiceName"/> is still
/// the default value <c>"app"</c> when this class is first resolved from DI, a
/// <see cref="LogLevel.Warning"/> is emitted. Set a meaningful service name via
/// <c>AddSharedKernelCaching(o =&gt; o.ServiceName = "my-service")</c> or
/// <c>AddCachingCoreOptions(o =&gt; o.ServiceName = "my-service")</c> to suppress this.
/// </para>
/// </remarks>
internal sealed class RedisCacheInvalidationBus : ICacheInvalidationBus
{
    private const string BroadcastChannel = "sharedkernel:cache:invalidation:broadcast";
    private const string DefaultServiceName = "app";

    private static readonly Action<ILogger, string, Exception?> LogDefaultServiceNameWarning =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(1, nameof(RedisCacheInvalidationBus)),
            "RedisCacheInvalidationBus is using the default ServiceName \"{ServiceName}\". " +
            "This means all services with the default name share the same invalidation channel. " +
            "Set a unique service name via AddSharedKernelCaching(o => o.ServiceName = \"my-service\") " +
            "or AddCachingCoreOptions(o => o.ServiceName = \"my-service\").");

    private readonly IRedisChannelService _channelService;
    private readonly string _targetedChannel;
    private readonly string _serviceName;

    /// <summary>
    /// Initialises a new <see cref="RedisCacheInvalidationBus"/>.
    /// </summary>
    /// <param name="channelService">The Redis Pub/Sub channel service for all I/O.</param>
    /// <param name="options">Core caching options supplying the service name for channel naming.</param>
    /// <param name="logger">
    /// Logger used to emit a startup warning when <see cref="CachingCoreOptions.ServiceName"/>
    /// is still the default <c>"app"</c> value.
    /// </param>
    public RedisCacheInvalidationBus(
        IRedisChannelService channelService,
        IOptions<CachingCoreOptions> options,
        ILogger<RedisCacheInvalidationBus> logger)
    {
        ArgumentNullException.ThrowIfNull(channelService);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _channelService = channelService;
        _serviceName = options.Value.ServiceName;

        var normalised = _serviceName.ToLowerInvariant().Replace(' ', '-');
        _targetedChannel = $"sharedkernel:cache:invalidation:{normalised}";

        // Warn at construction time (first DI resolution) if ServiceName is still the default.
        if (string.Equals(_serviceName, DefaultServiceName, StringComparison.OrdinalIgnoreCase))
        {
            LogDefaultServiceNameWarning(logger, _serviceName, null);
        }
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
