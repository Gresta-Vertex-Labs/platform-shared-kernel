using System.Data.Common;
using System.Runtime.ExceptionServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Execution.Context;
using SharedKernel.Idempotency.Abstractions;
using SharedKernel.Idempotency.EfCore.Context;
using SharedKernel.Idempotency.EfCore.Entities;
using SharedKernel.Idempotency.EfCore.Internal;
using SharedKernel.Idempotency.EfCore.Logging;
using SharedKernel.Idempotency.EfCore.Options;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Idempotency.EfCore.Store;

/// <summary>Atomic, tenant-scoped, stateless PostgreSQL implementation of <see cref="IIdempotencyStore"/>.</summary>
/// <remarks>
/// <para>
/// <see cref="TryBeginAsync"/> is one round trip: a raw-SQL <c>INSERT … ON CONFLICT (tenant_scope, purpose, "key")
/// DO UPDATE … RETURNING fingerprint, status, response, reservation_token</c>, executed on the context's own ADO.NET
/// connection (never <c>ExecuteSqlInterpolatedAsync</c>, which discards <c>RETURNING</c> data). The <c>DO UPDATE</c>
/// overwrites the row only when it has already expired (<c>expires_at_utc &lt;= @reserved_at_utc</c>); otherwise
/// every <c>SET</c> keeps the live row's values. <c>RETURNING</c> reports the row's final state, so no second
/// <c>SELECT</c> is needed to classify the outcome.
/// </para>
/// <para>
/// <b>Reservation token.</b> Every call generates a fresh token and passes it into the upsert. The call won the row
/// (a fresh insert or an expired-row reclaim) exactly when the returned token equals its own — more robust than
/// comparing timestamps, which can collide under a coarse <see cref="IClock"/>. <see cref="CompleteAsync"/> and
/// <see cref="ReleaseAsync"/> touch the row only while the supplied token still matches <em>and</em> the row is
/// still <c>InProgress</c>, so a late call from a caller whose reservation was taken over is a no-op.
/// </para>
/// <para>
/// Registered <c>Scoped</c>, matching <see cref="IdempotencyDbContext"/>'s lifetime. One class serves both
/// purposes; each keyed registration is its own instance.
/// </para>
/// </remarks>
public sealed class EfCoreIdempotencyStore : IIdempotencyStore
{
    private const string Table = IdempotencyKeyRecordConfiguration.TableName;

    private const string TryBeginSql = $"""
        INSERT INTO {Table} (tenant_scope, purpose, "key", fingerprint, status, reserved_at_utc, expires_at_utc, response, reservation_token)
        VALUES (@tenant_scope, @purpose, @key, @fingerprint, '{IdempotencyRecordStatusNames.InProgress}', @reserved_at_utc, @expires_at_utc, NULL, @token)
        ON CONFLICT (tenant_scope, purpose, "key") DO UPDATE SET
            fingerprint = CASE WHEN {Table}.expires_at_utc <= @reserved_at_utc THEN EXCLUDED.fingerprint ELSE {Table}.fingerprint END,
            status = CASE WHEN {Table}.expires_at_utc <= @reserved_at_utc THEN EXCLUDED.status ELSE {Table}.status END,
            reserved_at_utc = CASE WHEN {Table}.expires_at_utc <= @reserved_at_utc THEN EXCLUDED.reserved_at_utc ELSE {Table}.reserved_at_utc END,
            expires_at_utc = CASE WHEN {Table}.expires_at_utc <= @reserved_at_utc THEN EXCLUDED.expires_at_utc ELSE {Table}.expires_at_utc END,
            response = CASE WHEN {Table}.expires_at_utc <= @reserved_at_utc THEN NULL ELSE {Table}.response END,
            reservation_token = CASE WHEN {Table}.expires_at_utc <= @reserved_at_utc THEN EXCLUDED.reservation_token ELSE {Table}.reservation_token END
        RETURNING fingerprint, status, response, reservation_token
        """;

    private readonly IdempotencyDbContext _context;
    private readonly IRequestContextAccessor _requestContextAccessor;
    private readonly IClock _clock;
    private readonly IOptions<EfCoreIdempotencyOptions> _options;
    private readonly ILogger<EfCoreIdempotencyStore> _logger;

    /// <summary>Initializes a new instance of <see cref="EfCoreIdempotencyStore"/>.</summary>
    /// <param name="context">The idempotency context.</param>
    /// <param name="requestContextAccessor">Supplies the ambient tenant every key is scoped by.</param>
    /// <param name="clock">The source of every reservation and expiry timestamp.</param>
    /// <param name="options">The fail-open setting.</param>
    /// <param name="logger">Logs a fail-open decision.</param>
    public EfCoreIdempotencyStore(
        IdempotencyDbContext context,
        IRequestContextAccessor requestContextAccessor,
        IClock clock,
        IOptions<EfCoreIdempotencyOptions> options,
        ILogger<EfCoreIdempotencyStore> logger)
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
    public async Task<IdempotencyReservation> TryBeginAsync(
        IdempotencyPurpose purpose,
        string key,
        string fingerprint,
        TimeSpan ttl,
        CancellationToken cancellationToken)
    {
        EnsureDefined(purpose);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ttl, TimeSpan.Zero);

        var tenantScope = IdempotencyTenantScope.Current(_requestContextAccessor);
        var now = _clock.UtcNow;
        var expiresAt = now + ttl;
        var token = Guid.NewGuid();

        try
        {
            await _context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var connection = _context.Database.GetDbConnection();
                await using var command = connection.CreateCommand();
                command.CommandText = TryBeginSql;
                AddParameter(command, "tenant_scope", tenantScope);
                AddParameter(command, "purpose", purpose.ToString());
                AddParameter(command, "key", key);
                AddParameter(command, "fingerprint", fingerprint);
                AddParameter(command, "reserved_at_utc", now);
                AddParameter(command, "expires_at_utc", expiresAt);
                AddParameter(command, "token", token);

                await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    throw new InvalidOperationException(
                        "The idempotency reservation upsert returned no row; this should be structurally " +
                        "impossible for an INSERT ... ON CONFLICT DO UPDATE ... RETURNING statement.");
                }

                var storedFingerprint = reader.GetString(0);
                var storedStatus = reader.GetString(1);
                var storedResponse = reader.IsDBNull(2) ? null : reader.GetString(2);
                var storedToken = reader.GetGuid(3);

                // This call won the row — a fresh insert or a reclaimed expired one.
                if (storedToken == token)
                    return IdempotencyReservation.Started(token.ToString());

                if (!string.Equals(storedFingerprint, fingerprint, StringComparison.Ordinal))
                    return IdempotencyReservation.FingerprintMismatch();

                return storedStatus == IdempotencyRecordStatusNames.Completed
                    ? IdempotencyReservation.Completed(storedResponse)
                    : IdempotencyReservation.InProgress();
            }
            finally
            {
                await _context.Database.CloseConnectionAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (EfCoreStoreUnavailableClassifier.IsStoreUnavailable(ex))
        {
            // Nothing was written, so the token identifies no row; a later Complete/Release against it hits the
            // same outage path and follows the same fail-open/fail-closed decision.
            return HandleStoreUnavailable(ex, nameof(TryBeginAsync), IdempotencyReservation.Started(token.ToString()));
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
        EnsureDefined(purpose);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(retention, TimeSpan.Zero);

        // An unparsable token can never match a reservation_token value.
        if (!Guid.TryParse(token, out var reservationToken))
            return false;

        var tenantScope = IdempotencyTenantScope.Current(_requestContextAccessor);
        var expiresAt = _clock.UtcNow + retention;

        try
        {
            // The token + status guard makes this affect zero rows — reported as false — when the reservation was
            // taken over by another caller or already completed.
            var affected = await _context.IdempotencyKeys
                .Where(x => x.TenantScope == tenantScope
                    && x.Purpose == purpose
                    && x.Key == key
                    && x.ReservationToken == reservationToken
                    && x.Status == IdempotencyRecordStatus.InProgress)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.Status, IdempotencyRecordStatus.Completed)
                    .SetProperty(x => x.Response, response)
                    .SetProperty(x => x.ExpiresAtUtc, expiresAt), cancellationToken)
                .ConfigureAwait(false);

            return affected > 0;
        }
        catch (Exception ex) when (EfCoreStoreUnavailableClassifier.IsStoreUnavailable(ex))
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
        EnsureDefined(purpose);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);

        if (!Guid.TryParse(token, out var reservationToken))
            return false;

        var tenantScope = IdempotencyTenantScope.Current(_requestContextAccessor);

        try
        {
            // The status guard is what makes this "delete only if not completed".
            var affected = await _context.IdempotencyKeys
                .Where(x => x.TenantScope == tenantScope
                    && x.Purpose == purpose
                    && x.Key == key
                    && x.ReservationToken == reservationToken
                    && x.Status == IdempotencyRecordStatus.InProgress)
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            return affected > 0;
        }
        catch (Exception ex) when (EfCoreStoreUnavailableClassifier.IsStoreUnavailable(ex))
        {
            // Swallowing under fail-open is safe: an unreleased row expires with its TTL.
            return HandleStoreUnavailable(ex, nameof(ReleaseAsync), false);
        }
    }

    private static void EnsureDefined(IdempotencyPurpose purpose)
    {
        if (!Enum.IsDefined(purpose))
            throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "Unknown idempotency purpose.");
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private T HandleStoreUnavailable<T>(Exception exception, string operation, T fallback)
    {
        if (!_options.Value.AllowExecutionOnStoreUnavailable)
            ExceptionDispatchInfo.Capture(exception).Throw();

        EfCoreIdempotencyLog.StoreUnavailableFailOpen(_logger, operation, exception);
        return fallback;
    }
}
