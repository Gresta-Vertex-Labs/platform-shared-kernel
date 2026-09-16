using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Idempotency.Redis.Internal;
using SharedKernel.Idempotency.Redis.Logging;
using SharedKernel.Idempotency.Redis.Options;
using SharedKernel.Messaging.Abstractions.Idempotency;
using SharedKernel.Messaging.Abstractions.TenantContext;
using StackExchange.Redis;

namespace SharedKernel.Idempotency.Redis.MessageStore;

/// <summary>
/// Atomic, tenant-scoped Redis implementation of <see cref="IIdempotencyStore"/> for
/// consumer-side message deduplication.
/// </summary>
/// <remarks>
/// <para>
/// A separate physical class from <see cref="KeyStore.RedisRequestIdempotencyStore"/> —
/// <see cref="IIdempotencyStore"/> is a distinct, simpler two-method contract with no fingerprint or
/// response-replay concept, so there is no reason to fold it into the request-idempotency store
/// class. It keeps the original <c>SET key &lt;sentinel&gt; NX PX</c> reservation shape unchanged.
/// </para>
/// <para>
/// Keyed under a distinct <c>msg</c> namespace segment (<see cref="Internal.RedisIdempotencyKeyBuilder"/>)
/// so a message id can never collide with an unrelated idempotency key string. The in-flight TTL
/// comes from <see cref="RedisIdempotencyOptions.InFlightTtl"/>; the full-retention window comes
/// from <see cref="IdempotencyOptions.ExpiryWindow"/> (<c>07.Messaging.Abstractions</c>'s existing
/// advisory hint) rather than duplicating a second retention knob (D-08).
/// </para>
/// <para>
/// Registered <c>Scoped</c> — see <see cref="KeyStore.RedisRequestIdempotencyStore"/>'s remarks for why.
/// </para>
/// </remarks>
public sealed class RedisIdempotencyMessageStore : IIdempotencyStore
{
    private readonly IConnectionMultiplexer _connectionMultiplexer;
    private readonly ITenantContextAccessor _tenantContextAccessor;
    private readonly IOptions<RedisIdempotencyOptions> _redisOptions;
    private readonly IOptions<IdempotencyOptions> _messagingOptions;
    private readonly ILogger<RedisIdempotencyMessageStore> _logger;

    /// <summary>Initializes a new instance of <see cref="RedisIdempotencyMessageStore"/>.</summary>
    public RedisIdempotencyMessageStore(
        IConnectionMultiplexer connectionMultiplexer,
        ITenantContextAccessor tenantContextAccessor,
        IOptions<RedisIdempotencyOptions> redisOptions,
        IOptions<IdempotencyOptions> messagingOptions,
        ILogger<RedisIdempotencyMessageStore> logger)
    {
        ArgumentNullException.ThrowIfNull(connectionMultiplexer);
        ArgumentNullException.ThrowIfNull(tenantContextAccessor);
        ArgumentNullException.ThrowIfNull(redisOptions);
        ArgumentNullException.ThrowIfNull(messagingOptions);
        ArgumentNullException.ThrowIfNull(logger);

        _connectionMultiplexer = connectionMultiplexer;
        _tenantContextAccessor = tenantContextAccessor;
        _redisOptions = redisOptions;
        _messagingOptions = messagingOptions;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> HasProcessedAsync(Guid messageId, CancellationToken ct)
    {
        var redisKey = RedisIdempotencyKeyBuilder.BuildMessageStoreKey(_tenantContextAccessor.TenantId, messageId);
        var options = _redisOptions.Value;

        try
        {
            var db = _connectionMultiplexer.GetDatabase();
            var reserved = await db.StringSetAsync(
                    redisKey,
                    RedisIdempotencyResponseSentinel.Value,
                    options.InFlightTtl,
                    When.NotExists)
                .ConfigureAwait(false);

            return !reserved;
        }
        catch (Exception ex) when (RedisStoreUnavailableClassifier.IsStoreUnavailable(ex))
        {
            return HandleStoreUnavailable(ex, nameof(HasProcessedAsync), fallback: false, options);
        }
    }

    /// <inheritdoc />
    public async Task MarkProcessedAsync(Guid messageId, CancellationToken ct)
    {
        var redisKey = RedisIdempotencyKeyBuilder.BuildMessageStoreKey(_tenantContextAccessor.TenantId, messageId);
        var options = _redisOptions.Value;

        try
        {
            var db = _connectionMultiplexer.GetDatabase();
            await db.KeyExpireAsync(redisKey, _messagingOptions.Value.ExpiryWindow).ConfigureAwait(false);
        }
        catch (Exception ex) when (RedisStoreUnavailableClassifier.IsStoreUnavailable(ex))
        {
            HandleStoreUnavailable(ex, nameof(MarkProcessedAsync), fallback: false, options);
        }
    }

    private T HandleStoreUnavailable<T>(Exception exception, string operation, T fallback, RedisIdempotencyOptions options)
    {
        if (!options.AllowExecutionOnStoreUnavailable)
        {
            ExceptionDispatchInfo.Capture(exception).Throw();
        }

        RedisIdempotencyLog.StoreUnavailableFailOpen(_logger, operation, exception);
        return fallback;
    }
}
