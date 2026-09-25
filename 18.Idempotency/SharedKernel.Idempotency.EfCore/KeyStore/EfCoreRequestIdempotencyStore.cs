using System.Data.Common;
using System.Runtime.ExceptionServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Application.Pipeline.Idempotency;
using SharedKernel.Idempotency.EfCore.Context;
using SharedKernel.Idempotency.EfCore.Entities;
using SharedKernel.Idempotency.EfCore.Internal;
using SharedKernel.Idempotency.EfCore.Logging;
using SharedKernel.Idempotency.EfCore.Options;
using SharedKernel.Execution.Context;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Idempotency.EfCore.KeyStore;

/// <summary>
/// Atomic, tenant-scoped, stateless PostgreSQL implementation of <see cref="IRequestIdempotencyStore"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="TryBeginAsync"/> is a single round trip: a raw-SQL <c>INSERT ... ON CONFLICT
/// (tenant_id, "key") DO UPDATE ... RETURNING fingerprint, status, response, reservation_token</c>
/// upsert, executed directly against the context's own ADO.NET connection (never
/// <c>Database.ExecuteSqlInterpolatedAsync</c>, which discards <c>RETURNING</c> data). The
/// <c>DO UPDATE</c> clause only overwrites the row's fields with the caller's fresh values when the
/// existing row is already expired (<c>expires_at_utc &lt;= @reserved_at_utc</c>); otherwise every
/// <c>SET</c> is a no-op that leaves the live row's own fingerprint/status/response/token
/// untouched. <c>RETURNING</c> always reports the row's final (post-statement) state, so this method
/// never issues a second <c>SELECT</c> to classify the outcome.
/// </para>
/// <para>
/// <b>Reservation token — this store keeps none of its own.</b> Every call generates a fresh
/// <see cref="Guid"/> token and passes it into the upsert. Comparing the <em>returned</em> token
/// against the token this call generated is how the store learns whether this call actually won the
/// row (fresh insert or expired-row reclaim) versus merely observing an existing live row — this is
/// more robust than comparing timestamps, which can collide under a coarse-resolution
/// <see cref="IClock"/> when two calls race for the same key. On a win, the token is returned as
/// <see cref="IdempotencyBeginResult.ReservationToken"/> — this class does not remember it anywhere.
/// <see cref="CompleteAsync"/> and <see cref="ReleaseAsync"/> require the caller to pass that exact
/// token back, and only touch the row when it still matches the row's current token <em>and</em> the
/// row is still <c>InProgress</c> (a second confirmation of an already-completed row, or a
/// confirm/release against a row that no longer exists, both no-op). This prevents a slow caller's
/// late confirm/release, arriving after its own reservation already expired and a different caller
/// has since re-reserved the same key, from corrupting that other caller's row.
/// </para>
/// <para>
/// Registered <c>Scoped</c>, matching <see cref="IdempotencyDbContext"/>'s own <c>AddDbContext</c>
/// scoped lifetime (EF Core <see cref="DbContext"/> instances are not thread-safe and must not be
/// shared across concurrent operations, let alone captured into a singleton).
/// </para>
/// </remarks>
public sealed class EfCoreRequestIdempotencyStore : IRequestIdempotencyStore
{
    private const string TryBeginSql = $"""
        INSERT INTO idempotency_keys (tenant_id, "key", fingerprint, status, reserved_at_utc, expires_at_utc, response, reservation_token)
        VALUES (@tenant_id, @key, @fingerprint, '{IdempotencyRecordStatusNames.InProgress}', @reserved_at_utc, @expires_at_utc, NULL, @token)
        ON CONFLICT (tenant_id, "key") DO UPDATE SET
            fingerprint = CASE WHEN idempotency_keys.expires_at_utc <= @reserved_at_utc THEN EXCLUDED.fingerprint ELSE idempotency_keys.fingerprint END,
            status = CASE WHEN idempotency_keys.expires_at_utc <= @reserved_at_utc THEN EXCLUDED.status ELSE idempotency_keys.status END,
            reserved_at_utc = CASE WHEN idempotency_keys.expires_at_utc <= @reserved_at_utc THEN EXCLUDED.reserved_at_utc ELSE idempotency_keys.reserved_at_utc END,
            expires_at_utc = CASE WHEN idempotency_keys.expires_at_utc <= @reserved_at_utc THEN EXCLUDED.expires_at_utc ELSE idempotency_keys.expires_at_utc END,
            response = CASE WHEN idempotency_keys.expires_at_utc <= @reserved_at_utc THEN NULL ELSE idempotency_keys.response END,
            reservation_token = CASE WHEN idempotency_keys.expires_at_utc <= @reserved_at_utc THEN EXCLUDED.reservation_token ELSE idempotency_keys.reservation_token END
        RETURNING fingerprint, status, response, reservation_token
        """;

    private readonly IdempotencyDbContext _context;
    private readonly IRequestContextAccessor _requestContextAccessor;
    private readonly IClock _clock;
    private readonly IOptions<EfCoreIdempotencyOptions> _options;
    private readonly ILogger<EfCoreRequestIdempotencyStore> _logger;

    /// <summary>Initializes a new instance of <see cref="EfCoreRequestIdempotencyStore"/>.</summary>
    public EfCoreRequestIdempotencyStore(
        IdempotencyDbContext context,
        IRequestContextAccessor requestContextAccessor,
        IClock clock,
        IOptions<EfCoreIdempotencyOptions> options,
        ILogger<EfCoreRequestIdempotencyStore> logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requestContextAccessor);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _context = context;
        _requestContextAccessor = requestContextAccessor;
        _clock = clock;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IdempotencyBeginResult> TryBeginAsync(string key, string requestFingerprint, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestFingerprint);

        var tenantId = EfCoreTenantScope.Resolve(_requestContextAccessor.Current?.TenantId?.Value);
        var options = _options.Value;
        var now = _clock.UtcNow;
        var expiresAt = now + options.InFlightTtl;
        var token = Guid.NewGuid();

        try
        {
            await _context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var connection = _context.Database.GetDbConnection();
                await using var command = connection.CreateCommand();
                command.CommandText = TryBeginSql;
                AddParameter(command, "tenant_id", tenantId);
                AddParameter(command, "key", key);
                AddParameter(command, "fingerprint", requestFingerprint);
                AddParameter(command, "reserved_at_utc", now);
                AddParameter(command, "expires_at_utc", expiresAt);
                AddParameter(command, "token", token);

                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    throw new InvalidOperationException(
                        "The idempotency reservation upsert returned no row; this should be " +
                        "structurally impossible for an INSERT ... ON CONFLICT DO UPDATE ... " +
                        "RETURNING statement.");
                }

                var storedFingerprint = reader.GetString(0);
                var storedStatus = reader.GetString(1);
                var storedResponse = reader.IsDBNull(2) ? null : reader.GetString(2);
                var storedToken = reader.GetGuid(3);

                if (storedToken == token)
                {
                    // This call won the row — either a fresh insert (no prior row) or it reclaimed
                    // an expired one. Either way it is now the sole owner of this reservation; the
                    // caller is responsible for passing the token back to CompleteAsync/ReleaseAsync.
                    return IdempotencyBeginResult.Started(token.ToString());
                }

                if (!string.Equals(storedFingerprint, requestFingerprint, StringComparison.Ordinal))
                {
                    return IdempotencyBeginResult.FingerprintMismatch();
                }

                return storedStatus == IdempotencyRecordStatusNames.Completed
                    ? IdempotencyBeginResult.Completed(storedResponse!)
                    : IdempotencyBeginResult.InProgress();
            }
            finally
            {
                await _context.Database.CloseConnectionAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (EfCoreStoreUnavailableClassifier.IsStoreUnavailable(ex))
        {
            // Nothing was actually written to PostgreSQL, so the returned token identifies no real
            // row — a subsequent CompleteAsync/ReleaseAsync call against it will hit the same
            // store-unavailable path and follow the same fail-open/fail-closed decision.
            return HandleStoreUnavailable(ex, nameof(TryBeginAsync), IdempotencyBeginResult.Started(token.ToString()), options);
        }
    }

    /// <inheritdoc />
    public async Task<bool> CompleteAsync(string key, string reservationToken, string serializedResponse, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(reservationToken);
        ArgumentNullException.ThrowIfNull(serializedResponse);

        var options = _options.Value;

        // An unparsable token can never match a real reservation_token column value — no round
        // trip needed to know the answer is "reservation lost".
        if (!Guid.TryParse(reservationToken, out var token))
            return false;

        var tenantId = EfCoreTenantScope.Resolve(_requestContextAccessor.Current?.TenantId?.Value);
        var expiresAt = _clock.UtcNow + options.RetentionWindow;

        try
        {
            // The ReservationToken + Status guard means this update affects zero rows — reported
            // back as false — when the reservation was reclaimed by a different caller in the
            // meantime, or was already completed by an earlier CompleteAsync call.
            var affected = await _context.IdempotencyKeys
                .Where(x => x.TenantId == tenantId
                    && x.Key == key
                    && x.ReservationToken == token
                    && x.Status == IdempotencyRecordStatus.InProgress)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, IdempotencyRecordStatus.Completed)
                    .SetProperty(x => x.Response, serializedResponse)
                    .SetProperty(x => x.ExpiresAtUtc, expiresAt), cancellationToken)
                .ConfigureAwait(false);

            return affected > 0;
        }
        catch (Exception ex) when (EfCoreStoreUnavailableClassifier.IsStoreUnavailable(ex))
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

        if (!Guid.TryParse(reservationToken, out var token))
            return false;

        var tenantId = EfCoreTenantScope.Resolve(_requestContextAccessor.Current?.TenantId?.Value);

        try
        {
            // The Status guard is what makes this "delete only if not completed" — a row that was
            // already completed by a concurrent CompleteAsync call keeps its token but flips its
            // status, so this predicate stops matching it.
            var affected = await _context.IdempotencyKeys
                .Where(x => x.TenantId == tenantId
                    && x.Key == key
                    && x.ReservationToken == token
                    && x.Status == IdempotencyRecordStatus.InProgress)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            return affected > 0;
        }
        catch (Exception ex) when (EfCoreStoreUnavailableClassifier.IsStoreUnavailable(ex))
        {
            return HandleStoreUnavailable(ex, nameof(ReleaseAsync), false, options);
        }
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private T HandleStoreUnavailable<T>(Exception exception, string operation, T fallback, EfCoreIdempotencyOptions options)
    {
        if (!options.AllowExecutionOnStoreUnavailable)
        {
            ExceptionDispatchInfo.Capture(exception).Throw();
        }

        EfCoreIdempotencyLog.StoreUnavailableFailOpen(_logger, operation, exception);
        return fallback;
    }
}
