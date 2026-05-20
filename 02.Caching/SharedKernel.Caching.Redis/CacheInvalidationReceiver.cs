using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Caching.Abstractions;

namespace SharedKernel.Caching.Redis;

/// <summary>
/// Background service that subscribes to Redis Pub/Sub invalidation channels and dispatches
/// received <see cref="CacheInvalidationMessage"/> payloads to the local <see cref="ICacheService"/>.
/// </summary>
/// <remarks>
/// <para>
/// Subscribes to two channels on startup:
/// <list type="bullet">
///   <item><description>Own-service targeted channel: <c>sharedkernel:cache:invalidation:{service-name}</c></description></item>
///   <item><description>Broadcast channel: <c>sharedkernel:cache:invalidation:broadcast</c></description></item>
/// </list>
/// </para>
/// <para>
/// Dispatch behaviour by <see cref="CacheInvalidationType"/>:
/// <list type="bullet">
///   <item><description><see cref="CacheInvalidationType.Key"/> — calls <see cref="ICacheService.RemoveAsync"/> for each key.</description></item>
///   <item><description><see cref="CacheInvalidationType.Tag"/> — calls <see cref="ICacheService.RemoveByTagAsync"/> for each tag.</description></item>
///   <item><description><see cref="CacheInvalidationType.All"/> — logs a structured warning; full L1 flush is not supported.</description></item>
/// </list>
/// </para>
/// <para>
/// All deserialization and <see cref="ICacheService"/> errors are caught, logged at
/// <see cref="LogLevel.Error"/>, and swallowed. Processing always continues for subsequent messages.
/// </para>
/// <para>
/// An OTel activity span named <c>"cache.invalidation.receive"</c> is started for each received
/// message with tags for source service, correlation ID, and invalidation type. If
/// <see cref="CacheInvalidationMessage.CorrelationId"/> is a valid W3C traceparent, the span is
/// linked to the originating trace for end-to-end distributed tracing continuity.
/// </para>
/// </remarks>
public sealed partial class CacheInvalidationReceiver : BackgroundService
{
    private const string BroadcastChannel = "sharedkernel:cache:invalidation:broadcast";
    private static readonly ActivitySource ActivitySource = new("SharedKernel.Caching.Invalidation");

    private readonly IRedisChannelService _channelService;
    private readonly ICacheService _cacheService;
    private readonly ILogger<CacheInvalidationReceiver> _logger;
    private readonly string _targetedChannel;

    /// <summary>
    /// Initialises a new <see cref="CacheInvalidationReceiver"/>.
    /// </summary>
    /// <param name="channelService">Redis Pub/Sub channel service for subscription management.</param>
    /// <param name="cacheService">The local cache service that handles key/tag evictions.</param>
    /// <param name="options">Core caching options supplying the service name for channel naming.</param>
    /// <param name="logger">Logger for structured error and warning output.</param>
    public CacheInvalidationReceiver(
        IRedisChannelService channelService,
        ICacheService cacheService,
        IOptions<CachingCoreOptions> options,
        ILogger<CacheInvalidationReceiver> logger)
    {
        ArgumentNullException.ThrowIfNull(channelService);
        ArgumentNullException.ThrowIfNull(cacheService);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _channelService = channelService;
        _cacheService = cacheService;
        _logger = logger;

        var normalised = options.Value.ServiceName.ToLowerInvariant().Replace(' ', '-');
        _targetedChannel = $"sharedkernel:cache:invalidation:{normalised}";
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _channelService.SubscribeAsync(
            _targetedChannel,
            msg => HandleMessageAsync(msg, stoppingToken),
            stoppingToken).ConfigureAwait(false);

        await _channelService.SubscribeAsync(
            BroadcastChannel,
            msg => HandleMessageAsync(msg, stoppingToken),
            stoppingToken).ConfigureAwait(false);

        // Hold until cancellation is requested.
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown — expected when stoppingToken is cancelled.
        }
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await _channelService.UnsubscribeAsync(_targetedChannel, cancellationToken).ConfigureAwait(false);
        await _channelService.UnsubscribeAsync(BroadcastChannel, cancellationToken).ConfigureAwait(false);
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask HandleMessageAsync(string rawMessage, CancellationToken ct)
    {
        CacheInvalidationMessage message;

        try
        {
            var parsed = JsonSerializer.Deserialize(
                rawMessage,
                CacheInvalidationMessageJsonContext.Default.CacheInvalidationMessage);

            if (parsed is null)
            {
                Log.DeserializationReturnedNull(_logger, rawMessage);
                return;
            }

            message = parsed;
        }
        catch (Exception ex)
        {
            Log.DeserializationFailed(_logger, rawMessage, ex);
            return;
        }

        // Attempt to link to the originating trace via W3C traceparent.
        ActivityContext parentContext = default;
        if (!string.IsNullOrEmpty(message.CorrelationId))
        {
            ActivityContext.TryParse(message.CorrelationId, null, isRemote: true, out parentContext);
        }

        using var activity = ActivitySource.StartActivity(
            "cache.invalidation.receive",
            ActivityKind.Consumer,
            parentContext);

        activity?.SetTag("cache.invalidation.source", message.SourceService);
        activity?.SetTag("cache.invalidation.correlation_id", message.CorrelationId);
        activity?.SetTag("cache.invalidation.type", message.InvalidationType.ToString());

        try
        {
            await DispatchAsync(message, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.DispatchFailed(_logger, message.InvalidationType.ToString(), message.SourceService, ex);
        }
    }

    private async Task DispatchAsync(CacheInvalidationMessage message, CancellationToken ct)
    {
        switch (message.InvalidationType)
        {
            case CacheInvalidationType.Key when message.Keys is { Length: > 0 }:
                foreach (var key in message.Keys)
                {
                    await _cacheService.RemoveAsync(key, ct).ConfigureAwait(false);
                }
                break;

            case CacheInvalidationType.Tag when message.Tags is { Length: > 0 }:
                foreach (var tag in message.Tags)
                {
                    await _cacheService.RemoveByTagAsync(tag, ct).ConfigureAwait(false);
                }
                break;

            case CacheInvalidationType.All:
                Log.BroadcastAllReceived(_logger);
                break;

            default:
                Log.UnknownInvalidationType(_logger, message.InvalidationType.ToString());
                break;
        }
    }

    private static partial class Log
    {
        [LoggerMessage(
            EventId = 4001,
            Level = LogLevel.Error,
            Message = "CacheInvalidationReceiver failed to deserialize message: '{RawMessage}'")]
        internal static partial void DeserializationFailed(
            ILogger logger, string rawMessage, Exception exception);

        [LoggerMessage(
            EventId = 4002,
            Level = LogLevel.Error,
            Message = "CacheInvalidationReceiver deserialized null from message: '{RawMessage}'")]
        internal static partial void DeserializationReturnedNull(
            ILogger logger, string rawMessage);

        [LoggerMessage(
            EventId = 4003,
            Level = LogLevel.Warning,
            Message = "CacheInvalidationReceiver received broadcast All invalidation. Full L1 flush is not supported; entries will expire via TTL.")]
        internal static partial void BroadcastAllReceived(ILogger logger);

        [LoggerMessage(
            EventId = 4004,
            Level = LogLevel.Error,
            Message = "CacheInvalidationReceiver failed to dispatch invalidation of type '{InvalidationType}' from service '{SourceService}'")]
        internal static partial void DispatchFailed(
            ILogger logger, string invalidationType, string sourceService, Exception exception);

        [LoggerMessage(
            EventId = 4005,
            Level = LogLevel.Warning,
            Message = "CacheInvalidationReceiver received message with unhandled InvalidationType '{InvalidationType}'")]
        internal static partial void UnknownInvalidationType(
            ILogger logger, string invalidationType);
    }
}
