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
/// Atomic, tenant-scoped, stateless Redis implementation of <see cref="IRequestIdempotencyStore"/>.
/// </summary>
/// <remarks>
/// <para>
/// Each entry is a Redis hash with up to four fields: <c>status</c> (<c>InProgress</c> or
/// <c>Completed</c>), <c>fingerprint</c>, <c>token</c> (an opaque per-reservation identifier, see
/// below), and <c>response</c> (present only once completed). <see cref="TryBeginAsync"/>,
/// <see cref="CompleteAsync"/> and <see cref="ReleaseAsync"/> are each a single Lua script — one
/// atomic Redis round trip, never a <c>WATCH</c>/<c>MULTI</c> retry loop or a check-then-act pair of
/// commands. An unconfirmed reservation expires on its own via the hash's own TTL
/// (<see cref="RedisIdempotencyOptions.InFlightTtl"/>) — this is the self-healing property a fault
/// must not consume the key requires; this class performs no compensating cleanup.
/// </para>
/// <para>
/// <b>Reservation token — this store keeps none of its own.</b> Every winning
/// <see cref="TryBeginAsync"/> call generates a fresh token, writes it into the hash's <c>token</c>
/// field, and returns it as <see cref="IdempotencyBeginResult.ReservationToken"/>. The caller — not
/// this store — is responsible for passing that exact token back to <see cref="CompleteAsync"/>/
/// <see cref="ReleaseAsync"/>. Those calls mutate the row only when the supplied token still matches
/// what is stored there (and, for <see cref="CompleteAsync"/>, only while the row is still
/// <c>InProgress</c> — a second confirmation of an already-completed row is rejected, not silently
/// re-applied). This class holds no per-instance memory of "which caller won which reservation" —
/// the token round trip through the caller is what makes this a genuinely stateless store, and what
/// turns a late confirm/release from a caller whose reservation already expired and was reclaimed by
/// someone else into a safe, detectable no-op (<see langword="false"/>) instead of silent corruption
/// of the new owner's row.
/// </para>
/// <para>
/// Registered <c>Scoped</c>, not singleton: <see cref="ITenantContextAccessor"/> implementations are
/// conventionally registered <c>Scoped</c> in this platform (mirroring
/// <c>SharedKernel.Messaging.MassTransit.MessagingBusBuilder.WithTenantContext</c>), and a singleton
/// cannot safely consume a scoped dependency under a DI container built with
/// <c>ValidateScopes = true</c>.
/// </para>
/// </remarks>
public sealed class RedisRequestIdempotencyStore : IRequestIdempotencyStore
{
    // KEYS[1] = redis key; ARGV[1] = fingerprint; ARGV[2] = in-flight TTL (ms); ARGV[3] = token.
    // Fingerprint mismatch is checked before status so a reused key against a different fingerprint
    // is always reported as FingerprintMismatch, whether the existing entry is in-flight or
    // completed.
    private const string TryBeginScript = """
        local key = KEYS[1]
        local fingerprint = ARGV[1]
        local ttlMs = ARGV[2]
        local token = ARGV[3]

        if redis.call('EXISTS', key) == 0 then
            redis.call('HSET', key, 'status', 'InProgress', 'fingerprint', fingerprint, 'token', token)
            redis.call('PEXPIRE', key, ttlMs)
            return {'Started', false}
        end

        if redis.call('HGET', key, 'fingerprint') ~= fingerprint then
            return {'FingerprintMismatch', false}
        end

        if redis.call('HGET', key, 'status') == 'Completed' then
            return {'Completed', redis.call('HGET', key, 'response')}
        end

        return {'InProgress', false}
        """;

    // KEYS[1] = redis key; ARGV[1] = token; ARGV[2] = response; ARGV[3] = retention TTL (ms).
    // No-ops (returns 0) unless the supplied token still owns the row AND the row is still
    // InProgress — a foreign/stale token, or a row already Completed/reclaimed, both no-op.
    private const string CompleteScript = """
        local key = KEYS[1]
        local token = ARGV[1]
        local response = ARGV[2]
        local retentionMs = ARGV[3]

        if redis.call('HGET', key, 'token') == token and redis.call('HGET', key, 'status') == 'InProgress' then
            redis.call('HSET', key, 'status', 'Completed', 'response', response)
            redis.call('PEXPIRE', key, retentionMs)
            return 1
        end
        return 0
        """;

    // KEYS[1] = redis key; ARGV[1] = token. Deletes only when the token still matches AND the
    // status is still InProgress — a completed reservation is never deleted by Release.
    private const string ReleaseScript = """
        local key = KEYS[1]
        local token = ARGV[1]

        if redis.call('HGET', key, 'token') == token and redis.call('HGET', key, 'status') == 'InProgress' then
            redis.call('DEL', key)
            return 1
        end
        return 0
        """;

    private readonly IConnectionMultiplexer _connectionMultiplexer;
    private readonly ITenantContextAccessor _tenantContextAccessor;
    private readonly IOptions<RedisIdempotencyOptions> _options;
    private readonly ILogger<RedisRequestIdempotencyStore> _logger;

    /// <summary>Initializes a new instance of <see cref="RedisRequestIdempotencyStore"/>.</summary>
    public RedisRequestIdempotencyStore(
        IConnectionMultiplexer connectionMultiplexer,
        ITenantContextAccessor tenantContextAccessor,
        IOptions<RedisIdempotencyOptions> options,
        ILogger<RedisRequestIdempotencyStore> logger)
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
    public async Task<IdempotencyBeginResult> TryBeginAsync(string key, string requestFingerprint, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestFingerprint);

        var redisKey = RedisIdempotencyKeyBuilder.BuildKeyStoreKey(_tenantContextAccessor.TenantId, key);
        var options = _options.Value;
        var token = Guid.NewGuid().ToString("N");

        try
        {
            var db = _connectionMultiplexer.GetDatabase();
            RedisKey[] keys = [redisKey];
            RedisValue[] values = [requestFingerprint, (long)options.InFlightTtl.TotalMilliseconds, token];

            var reply = (RedisResult[])(await db.ScriptEvaluateAsync(TryBeginScript, keys, values).ConfigureAwait(false))!;
            var status = (string)reply[0]!;
            var storedResponse = reply.Length > 1 && !reply[1].IsNull ? (string?)reply[1] : null;

            return status switch
            {
                "Started" => IdempotencyBeginResult.Started(token),
                "FingerprintMismatch" => IdempotencyBeginResult.FingerprintMismatch(),
                "Completed" => IdempotencyBeginResult.Completed(storedResponse!),
                _ => IdempotencyBeginResult.InProgress(),
            };
        }
        catch (Exception ex) when (RedisStoreUnavailableClassifier.IsStoreUnavailable(ex))
        {
            // Nothing was actually written to Redis, so the returned token identifies no real row —
            // a subsequent CompleteAsync/ReleaseAsync call against it will hit the same
            // store-unavailable path and follow the same fail-open/fail-closed decision.
            return HandleStoreUnavailable(ex, nameof(TryBeginAsync), IdempotencyBeginResult.Started(token), options);
        }
    }

    /// <inheritdoc />
    public async Task<bool> CompleteAsync(string key, string reservationToken, string serializedResponse, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(reservationToken);
        ArgumentNullException.ThrowIfNull(serializedResponse);

        var options = _options.Value;
        var redisKey = RedisIdempotencyKeyBuilder.BuildKeyStoreKey(_tenantContextAccessor.TenantId, key);

        try
        {
            var db = _connectionMultiplexer.GetDatabase();
            RedisKey[] keys = [redisKey];
            RedisValue[] values = [reservationToken, serializedResponse, (long)options.RetentionWindow.TotalMilliseconds];
            var reply = await db.ScriptEvaluateAsync(CompleteScript, keys, values).ConfigureAwait(false);
            return (int)reply! == 1;
        }
        catch (Exception ex) when (RedisStoreUnavailableClassifier.IsStoreUnavailable(ex))
        {
            return HandleStoreUnavailable(ex, nameof(CompleteAsync), false, options);
        }
    }

    /// <inheritdoc />
    public async Task<bool> ReleaseAsync(string key, string reservationToken, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(reservationToken);

        var options = _options.Value;
        var redisKey = RedisIdempotencyKeyBuilder.BuildKeyStoreKey(_tenantContextAccessor.TenantId, key);

        try
        {
            var db = _connectionMultiplexer.GetDatabase();
            RedisKey[] keys = [redisKey];
            RedisValue[] values = [reservationToken];
            var reply = await db.ScriptEvaluateAsync(ReleaseScript, keys, values).ConfigureAwait(false);
            return (int)reply! == 1;
        }
        catch (Exception ex) when (RedisStoreUnavailableClassifier.IsStoreUnavailable(ex))
        {
            return HandleStoreUnavailable(ex, nameof(ReleaseAsync), false, options);
        }
    }

    private T HandleStoreUnavailable<T>(Exception exception, string operation, T fallback, RedisIdempotencyOptions options)
    {
        if (!options.AllowExecutionOnStoreUnavailable)
        {
            // Re-throws the original exception with its original stack trace preserved — a bare
            // `throw;` is not legal here because we are no longer lexically inside the catch block
            // that caught it.
            ExceptionDispatchInfo.Capture(exception).Throw();
        }

        RedisIdempotencyLog.StoreUnavailableFailOpen(_logger, operation, exception);
        return fallback;
    }
}
