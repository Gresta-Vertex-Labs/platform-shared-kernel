using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Application.Behaviors.Idempotency;
using SharedKernel.Idempotency.Redis.Internal;
using SharedKernel.Idempotency.Redis.Logging;
using SharedKernel.Idempotency.Redis.Options;
using SharedKernel.Messaging.Abstractions.TenantContext;
using StackExchange.Redis;

namespace SharedKernel.Idempotency.Redis.KeyStore;

/// <summary>
/// Atomic, tenant-scoped Redis implementation of <see cref="IIdempotencyKeyStore"/> AND
/// <see cref="IIdempotencyResponseStore"/> on a single instance.
/// </summary>
/// <remarks>
/// <para>
/// Implements both interfaces on the same class deliberately (Domain Invariant 7 / D-01):
/// <see cref="IIdempotencyResponseStore"/>'s own documentation states that
/// <c>IdempotentCommandBehavior&lt;TRequest,TResponse&gt;</c> detects replay support via a plain
/// <see langword="is"/> <see cref="IIdempotencyResponseStore"/> pattern-match on the *injected*
/// <see cref="IIdempotencyKeyStore"/> instance — replay support is structurally unreachable if the
/// two interfaces are implemented by separate classes, regardless of DI wiring.
/// </para>
/// <para>
/// Registered <c>Scoped</c>, not singleton (a deliberate correction of the work order's literal
/// "singleton instance" framing — see <c>18.Idempotency/state-map.md</c> D-09 and this package's
/// <c>README.md</c>): <see cref="ITenantContextAccessor"/> implementations are conventionally
/// registered <c>Scoped</c> in this platform (mirroring
/// <c>SharedKernel.Messaging.MassTransit.MessagingBusBuilder.WithTenantContext</c>), and a
/// singleton cannot safely consume a scoped dependency under a DI container built with
/// <c>ValidateScopes = true</c>.
/// </para>
/// <para>
/// See <c>18.Idempotency/CLAUDE.md</c> Domain Invariant 1 and D-04 for the exact atomic-reservation
/// protocol this class implements.
/// </para>
/// </remarks>
public sealed class RedisIdempotencyKeyStore : IIdempotencyKeyStore, IIdempotencyResponseStore
{
    private const string StoreResponseScript = """
        if redis.call('EXISTS', KEYS[1]) == 1 then
            redis.call('SET', KEYS[1], ARGV[1], 'KEEPTTL')
        end
        return 1
        """;

    private readonly IConnectionMultiplexer _connectionMultiplexer;
    private readonly ITenantContextAccessor _tenantContextAccessor;
    private readonly IOptions<RedisIdempotencyOptions> _options;
    private readonly ILogger<RedisIdempotencyKeyStore> _logger;

    /// <summary>Initializes a new instance of <see cref="RedisIdempotencyKeyStore"/>.</summary>
    public RedisIdempotencyKeyStore(
        IConnectionMultiplexer connectionMultiplexer,
        ITenantContextAccessor tenantContextAccessor,
        IOptions<RedisIdempotencyOptions> options,
        ILogger<RedisIdempotencyKeyStore> logger)
    {
        ArgumentNullException.ThrowIfNull(connectionMultiplexer);
        ArgumentNullException.ThrowIfNull(tenantContextAccessor);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _connectionMultiplexer = connectionMultiplexer;
        _tenantContextAccessor = tenantContextAccessor;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> HasProcessedAsync(string idempotencyKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        var redisKey = RedisIdempotencyKeyBuilder.BuildKeyStoreKey(_tenantContextAccessor.TenantId, idempotencyKey);
        var options = _options.Value;

        try
        {
            var db = _connectionMultiplexer.GetDatabase();
            var reserved = await db.StringSetAsync(
                    redisKey,
                    RedisIdempotencyResponseSentinel.Value,
                    options.InFlightTtl,
                    When.NotExists)
                .ConfigureAwait(false);

            // reserved == true  -> fresh reservation created -> not yet processed
            // reserved == false -> key already exists (in-flight or confirmed) -> already processed
            return !reserved;
        }
        catch (Exception ex) when (RedisStoreUnavailableClassifier.IsStoreUnavailable(ex))
        {
            return HandleStoreUnavailable(ex, nameof(HasProcessedAsync), fallback: false, options);
        }
    }

    /// <inheritdoc />
    public async Task MarkProcessedAsync(string idempotencyKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        var redisKey = RedisIdempotencyKeyBuilder.BuildKeyStoreKey(_tenantContextAccessor.TenantId, idempotencyKey);
        var options = _options.Value;

        try
        {
            var db = _connectionMultiplexer.GetDatabase();
            // Extends TTL only — never rewrites the value, so it can never clobber a response
            // StoreResponseAsync already wrote, regardless of call order (D-04).
            await db.KeyExpireAsync(redisKey, options.RetentionWindow).ConfigureAwait(false);
        }
        catch (Exception ex) when (RedisStoreUnavailableClassifier.IsStoreUnavailable(ex))
        {
            HandleStoreUnavailable(ex, nameof(MarkProcessedAsync), fallback: false, options);
        }
    }

    /// <inheritdoc />
    public async Task<string?> TryGetStoredResponseAsync(string idempotencyKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        var redisKey = RedisIdempotencyKeyBuilder.BuildKeyStoreKey(_tenantContextAccessor.TenantId, idempotencyKey);
        var options = _options.Value;

        try
        {
            var db = _connectionMultiplexer.GetDatabase();
            var value = await db.StringGetAsync(redisKey).ConfigureAwait(false);

            if (!value.HasValue || value == RedisIdempotencyResponseSentinel.Value)
            {
                return null;
            }

            return value.ToString();
        }
        catch (Exception ex) when (RedisStoreUnavailableClassifier.IsStoreUnavailable(ex))
        {
            return HandleStoreUnavailable(ex, nameof(TryGetStoredResponseAsync), fallback: (string?)null, options);
        }
    }

    /// <inheritdoc />
    public async Task StoreResponseAsync(string idempotencyKey, string serializedResponse, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentNullException.ThrowIfNull(serializedResponse);

        var redisKey = RedisIdempotencyKeyBuilder.BuildKeyStoreKey(_tenantContextAccessor.TenantId, idempotencyKey);
        var options = _options.Value;

        try
        {
            var db = _connectionMultiplexer.GetDatabase();
            RedisKey[] keys = [redisKey];
            RedisValue[] values = [serializedResponse];
            await db.ScriptEvaluateAsync(StoreResponseScript, keys, values).ConfigureAwait(false);
        }
        catch (Exception ex) when (RedisStoreUnavailableClassifier.IsStoreUnavailable(ex))
        {
            HandleStoreUnavailable(ex, nameof(StoreResponseAsync), fallback: false, options);
        }
    }

    private T HandleStoreUnavailable<T>(Exception exception, string operation, T fallback, RedisIdempotencyOptions options)
    {
        if (!options.AllowExecutionOnStoreUnavailable)
        {
            // Re-throws the original exception with its original stack trace preserved —
            // a bare `throw;` is not legal here because we are no longer lexically inside the
            // catch block that caught it.
            ExceptionDispatchInfo.Capture(exception).Throw();
        }

        RedisIdempotencyLog.StoreUnavailableFailOpen(_logger, operation, exception);
        return fallback;
    }
}
