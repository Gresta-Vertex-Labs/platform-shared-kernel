using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Execution.Context;
using SharedKernel.Idempotency.Abstractions;
using SharedKernel.Idempotency.Redis.Internal;
using SharedKernel.Idempotency.Redis.Logging;
using SharedKernel.Idempotency.Redis.Options;
using StackExchange.Redis;

namespace SharedKernel.Idempotency.Redis.Store;

/// <summary>Atomic, tenant-scoped, stateless Redis implementation of <see cref="IIdempotencyStore"/>.</summary>
/// <remarks>
/// <para>
/// Each entry is a Redis hash with up to four fields: <c>status</c> (<c>InProgress</c> or <c>Completed</c>),
/// <c>fingerprint</c>, <c>token</c> and — once completed with a response — <c>response</c>. The key is built by
/// <see cref="RedisIdempotencyKeyBuilder"/> from the ambient tenant, the purpose and the raw key.
/// <see cref="TryBeginAsync"/>, <see cref="CompleteAsync"/> and <see cref="ReleaseAsync"/> are each one Lua script —
/// one atomic round trip, never a <c>WATCH</c>/<c>MULTI</c> loop or a check-then-act pair. An unconfirmed reservation
/// expires on its own through the hash's TTL, so a crashed caller cannot wedge a key.
/// </para>
/// <para>
/// <b>Reservation token.</b> A winning <see cref="TryBeginAsync"/> writes a fresh token into the hash and returns it.
/// <see cref="CompleteAsync"/> and <see cref="ReleaseAsync"/> act only while that token still owns an
/// <c>InProgress</c> entry, so a late call from a caller whose reservation expired and was taken over is a no-op
/// (<see langword="false"/>). The store keeps no memory of who won what.
/// </para>
/// <para>One class serves both purposes; each keyed registration is its own scoped instance.</para>
/// </remarks>
public sealed class RedisIdempotencyStore : IIdempotencyStore
{
    // KEYS[1] = redis key; ARGV[1] = fingerprint; ARGV[2] = in-flight TTL (ms); ARGV[3] = token.
    // Fingerprint mismatch is checked before status, so a reused key is always reported as a mismatch,
    // whether the existing entry is in flight or completed.
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

    // KEYS[1] = redis key; ARGV[1] = token; ARGV[2] = response; ARGV[3] = retention TTL (ms);
    // ARGV[4] = '1' when a response is stored. No-op (0) unless the token still owns the entry AND it is
    // still InProgress — a foreign or stale token, or an entry already completed, both no-op.
    private const string CompleteScript = """
        local key = KEYS[1]
        local token = ARGV[1]
        local response = ARGV[2]
        local retentionMs = ARGV[3]
        local hasResponse = ARGV[4]

        if redis.call('HGET', key, 'token') == token and redis.call('HGET', key, 'status') == 'InProgress' then
            if hasResponse == '1' then
                redis.call('HSET', key, 'status', 'Completed', 'response', response)
            else
                redis.call('HSET', key, 'status', 'Completed')
            end
            redis.call('PEXPIRE', key, retentionMs)
            return 1
        end
        return 0
        """;

    // KEYS[1] = redis key; ARGV[1] = token. Deletes only while the token still owns an InProgress
    // entry — a completed entry is never deleted by a release.
    private const string ReleaseScript = """
        local key = KEYS[1]
        local token = ARGV[1]

        if redis.call('HGET', key, 'token') == token and redis.call('HGET', key, 'status') == 'InProgress' then
            redis.call('DEL', key)
            return 1
        end
        return 0
        """;

    private const string StartedReply = "Started";
    private const string CompletedReply = "Completed";
    private const string FingerprintMismatchReply = "FingerprintMismatch";
    private const string HasResponseFlag = "1";
    private const string NoResponseFlag = "0";

    private readonly IConnectionMultiplexer _connectionMultiplexer;
    private readonly IRequestContextAccessor _requestContextAccessor;
    private readonly IOptions<RedisIdempotencyOptions> _options;
    private readonly ILogger<RedisIdempotencyStore> _logger;

    /// <summary>Initializes a new instance of <see cref="RedisIdempotencyStore"/>.</summary>
    /// <param name="connectionMultiplexer">The shared Redis connection (<c>02.Caching.Redis.Core</c>).</param>
    /// <param name="requestContextAccessor">Supplies the ambient tenant every key is scoped by.</param>
    /// <param name="options">The fail-open setting.</param>
    /// <param name="logger">Logs a fail-open decision.</param>
    public RedisIdempotencyStore(
        IConnectionMultiplexer connectionMultiplexer,
        IRequestContextAccessor requestContextAccessor,
        IOptions<RedisIdempotencyOptions> options,
        ILogger<RedisIdempotencyStore> logger)
    {
        ArgumentNullException.ThrowIfNull(connectionMultiplexer);
        ArgumentNullException.ThrowIfNull(requestContextAccessor);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _connectionMultiplexer = connectionMultiplexer;
        _requestContextAccessor = requestContextAccessor;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IdempotencyReservation> TryBeginAsync(
        IdempotencyPurpose purpose,
        string key,
        string fingerprint,
        TimeSpan ttl,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ttl, TimeSpan.Zero);

        var redisKey = BuildKey(purpose, key);
        var token = Guid.NewGuid().ToString("N");

        try
        {
            var db = _connectionMultiplexer.GetDatabase();
            RedisKey[] keys = [redisKey];
            RedisValue[] values = [fingerprint, ToMilliseconds(ttl), token];

            var reply = (RedisResult[])(await db.ScriptEvaluateAsync(TryBeginScript, keys, values).ConfigureAwait(false))!;
            var status = (string)reply[0]!;
            var storedResponse = reply.Length > 1 && !reply[1].IsNull ? (string?)reply[1] : null;

            return status switch
            {
                StartedReply => IdempotencyReservation.Started(token),
                FingerprintMismatchReply => IdempotencyReservation.FingerprintMismatch(),
                CompletedReply => IdempotencyReservation.Completed(storedResponse),
                _ => IdempotencyReservation.InProgress(),
            };
        }
        catch (Exception ex) when (RedisStoreUnavailableClassifier.IsStoreUnavailable(ex))
        {
            // Nothing was written, so the token identifies no entry; a later Complete/Release against it hits
            // the same outage path and follows the same fail-open/fail-closed decision.
            return HandleStoreUnavailable(ex, nameof(TryBeginAsync), IdempotencyReservation.Started(token));
        }
    }

    /// <inheritdoc />
    public async Task<bool> CompleteAsync(
        IdempotencyPurpose purpose,
        string key,
        string token,
        string? response,
        TimeSpan retention,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(retention, TimeSpan.Zero);

        var redisKey = BuildKey(purpose, key);

        try
        {
            var db = _connectionMultiplexer.GetDatabase();
            RedisKey[] keys = [redisKey];
            RedisValue[] values =
            [
                token,
                response ?? string.Empty,
                ToMilliseconds(retention),
                response is null ? NoResponseFlag : HasResponseFlag,
            ];
            var reply = await db.ScriptEvaluateAsync(CompleteScript, keys, values).ConfigureAwait(false);
            return (int)reply! == 1;
        }
        catch (Exception ex) when (RedisStoreUnavailableClassifier.IsStoreUnavailable(ex))
        {
            return HandleStoreUnavailable(ex, nameof(CompleteAsync), false);
        }
    }

    /// <inheritdoc />
    public async Task<bool> ReleaseAsync(
        IdempotencyPurpose purpose,
        string key,
        string token,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        var redisKey = BuildKey(purpose, key);

        try
        {
            var db = _connectionMultiplexer.GetDatabase();
            RedisKey[] keys = [redisKey];
            RedisValue[] values = [token];
            var reply = await db.ScriptEvaluateAsync(ReleaseScript, keys, values).ConfigureAwait(false);
            return (int)reply! == 1;
        }
        catch (Exception ex) when (RedisStoreUnavailableClassifier.IsStoreUnavailable(ex))
        {
            // Swallowing under fail-open is safe: an unreleased reservation expires with its TTL, so the key is
            // retried a little later rather than lost.
            return HandleStoreUnavailable(ex, nameof(ReleaseAsync), false);
        }
    }

    private string BuildKey(IdempotencyPurpose purpose, string key) =>
        RedisIdempotencyKeyBuilder.Build(IdempotencyTenantScope.Current(_requestContextAccessor), purpose, key);

    private static long ToMilliseconds(TimeSpan value) => Math.Max(1L, (long)value.TotalMilliseconds);

    private T HandleStoreUnavailable<T>(Exception exception, string operation, T fallback)
    {
        if (!_options.Value.AllowExecutionOnStoreUnavailable)
        {
            // Rethrow the original exception with its stack trace; a bare `throw;` is not legal outside the catch.
            ExceptionDispatchInfo.Capture(exception).Throw();
        }

        RedisIdempotencyLog.StoreUnavailableFailOpen(_logger, operation, exception);
        return fallback;
    }
}
