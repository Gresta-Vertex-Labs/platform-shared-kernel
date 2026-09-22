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
/// Redis-backed <see cref="IIdempotencyStore"/> using an atomic Lua reservation.
/// </summary>
/// <remarks>
/// <para>
/// Keys are tenant-scoped by <see cref="RedisIdempotencyKeyBuilder"/>, so two tenants cannot
/// collide on the same message id.
/// </para>
/// <para>
/// Rewritten by P-560 onto the reserve/complete/release contract. The previous version exposed
/// <c>HasProcessedAsync</c>, which was documented as a query but implemented as a mutating
/// <c>SET NX</c> — it could not distinguish "another delivery is running" from "already consumed",
/// so a redelivery following a <em>failed</em> attempt was reported as a duplicate and dropped.
/// </para>
/// </remarks>
public sealed class RedisIdempotencyMessageStore : IIdempotencyStore
{
    private readonly IConnectionMultiplexer _connectionMultiplexer;
    private readonly ITenantContextAccessor _tenantContextAccessor;
    private readonly IOptions<RedisIdempotencyOptions> _redisOptions;
    private readonly IOptions<IdempotencyOptions> _messagingOptions;
    private readonly ILogger<RedisIdempotencyMessageStore> _logger;

    /// <summary>Creates the store.</summary>
    /// <param name="connectionMultiplexer">The shared Redis connection.</param>
    /// <param name="tenantContextAccessor">Supplies the ambient tenant for key scoping.</param>
    /// <param name="redisOptions">Redis-specific options, including the in-flight lease.</param>
    /// <param name="messagingOptions">Messaging options supplying the completed-record retention window.</param>
    /// <param name="logger">Logger for fail-open diagnostics.</param>
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
    public async Task<IdempotencyReservation> TryBeginAsync(Guid messageId, CancellationToken ct)
    {
        var redisKey = RedisIdempotencyKeyBuilder.BuildMessageStoreKey(_tenantContextAccessor.TenantId, messageId);
        var options = _redisOptions.Value;
        var token = Guid.NewGuid().ToString("N");

        try
        {
            var db = _connectionMultiplexer.GetDatabase();
            var outcome = (string?)await db.ScriptEvaluateAsync(
                    RedisIdempotencyMessageScripts.TryBegin,
                    [redisKey],
                    [
                        token,
                        (long)options.InFlightTtl.TotalMilliseconds,
                        RedisIdempotencyMessageScripts.CompletedValue,
                    ])
                .ConfigureAwait(false);

            return outcome switch
            {
                "started" => IdempotencyReservation.Started(token),
                "completed" => IdempotencyReservation.AlreadyProcessed(),
                _ => IdempotencyReservation.InProgress(),
            };
        }
        catch (Exception ex) when (RedisStoreUnavailableClassifier.IsStoreUnavailable(ex))
        {
            // Fail-open means "let the consumer run", which is a Started reservation. The token is
            // still issued so Complete/Release stay symmetric; those calls will simply find no key.
            return HandleStoreUnavailable(
                ex, nameof(TryBeginAsync), IdempotencyReservation.Started(token), options);
        }
    }

    /// <inheritdoc />
    public async Task CompleteAsync(Guid messageId, string reservationToken, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(reservationToken);

        var redisKey = RedisIdempotencyKeyBuilder.BuildMessageStoreKey(_tenantContextAccessor.TenantId, messageId);
        var options = _redisOptions.Value;

        try
        {
            var db = _connectionMultiplexer.GetDatabase();
            await db.ScriptEvaluateAsync(
                    RedisIdempotencyMessageScripts.Complete,
                    [redisKey],
                    [
                        reservationToken,
                        (long)_messagingOptions.Value.ExpiryWindow.TotalMilliseconds,
                        RedisIdempotencyMessageScripts.CompletedValue,
                    ])
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (RedisStoreUnavailableClassifier.IsStoreUnavailable(ex))
        {
            HandleStoreUnavailable(ex, nameof(CompleteAsync), fallback: false, options);
        }
    }

    /// <inheritdoc />
    public async Task ReleaseAsync(Guid messageId, string reservationToken, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(reservationToken);

        var redisKey = RedisIdempotencyKeyBuilder.BuildMessageStoreKey(_tenantContextAccessor.TenantId, messageId);
        var options = _redisOptions.Value;

        try
        {
            var db = _connectionMultiplexer.GetDatabase();
            await db.ScriptEvaluateAsync(
                    RedisIdempotencyMessageScripts.Release,
                    [redisKey],
                    [reservationToken])
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (RedisStoreUnavailableClassifier.IsStoreUnavailable(ex))
        {
            // Swallowing here is safe in a way it is not for TryBegin: an unreleased reservation
            // simply expires with its lease, so the message is retried a little later rather than
            // lost. Rethrowing would replace the consumer's real failure with this one.
            HandleStoreUnavailable(ex, nameof(ReleaseAsync), fallback: false, options);
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
