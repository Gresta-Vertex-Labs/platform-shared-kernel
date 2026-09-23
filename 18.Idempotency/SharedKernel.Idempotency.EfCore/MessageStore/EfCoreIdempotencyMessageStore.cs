using System.Runtime.ExceptionServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Idempotency.EfCore.Context;
using SharedKernel.Idempotency.EfCore.Internal;
using SharedKernel.Idempotency.EfCore.Logging;
using SharedKernel.Idempotency.EfCore.Options;
using SharedKernel.Messaging.Abstractions.Idempotency;
using SharedKernel.Messaging.Abstractions.TenantContext;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Idempotency.EfCore.MessageStore;

/// <summary>
/// Atomic, tenant-scoped PostgreSQL implementation of <see cref="IIdempotencyStore"/> for
/// consumer-side message deduplication.
/// </summary>
/// <remarks>
/// <para>
/// A separate physical class from <see cref="KeyStore.EfCoreRequestIdempotencyStore"/> —
/// <see cref="IIdempotencyStore"/> is a distinct, simpler two-method contract with no fingerprint
/// or response-replay concept, so there is no reason to fold it into the request-idempotency store
/// class.
/// </para>
/// <para>
/// Uses the original atomic-reservation upsert shape (D-05) against its own
/// <c>idempotency_messages</c> table, keyed on <c>(tenant_id, message_id)</c> — unaffected by the
/// request-idempotency store's fingerprint/token redesign, since this contract never had a
/// fingerprint or response-replay concept to begin with.
/// The in-flight TTL comes from <see cref="EfCoreIdempotencyOptions.InFlightTtl"/>; the
/// full-retention window comes from <see cref="IdempotencyOptions.ExpiryWindow"/>
/// (<c>07.Messaging.Abstractions</c>'s existing advisory hint) rather than duplicating a second
/// retention knob (D-08).
/// </para>
/// <para>Registered <c>Scoped</c>, matching <see cref="IdempotencyDbContext"/>'s own lifetime.</para>
/// </remarks>
public sealed class EfCoreIdempotencyMessageStore : IIdempotencyStore
{
    private readonly IdempotencyDbContext _context;
    private readonly ITenantContextAccessor _tenantContextAccessor;
    private readonly IClock _clock;
    private readonly IOptions<EfCoreIdempotencyOptions> _efCoreOptions;
    private readonly IOptions<IdempotencyOptions> _messagingOptions;
    private readonly ILogger<EfCoreIdempotencyMessageStore> _logger;

    /// <summary>Initializes a new instance of <see cref="EfCoreIdempotencyMessageStore"/>.</summary>
    public EfCoreIdempotencyMessageStore(
        IdempotencyDbContext context,
        ITenantContextAccessor tenantContextAccessor,
        IClock clock,
        IOptions<EfCoreIdempotencyOptions> efCoreOptions,
        IOptions<IdempotencyOptions> messagingOptions,
        ILogger<EfCoreIdempotencyMessageStore> logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tenantContextAccessor);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(efCoreOptions);
        ArgumentNullException.ThrowIfNull(messagingOptions);
        ArgumentNullException.ThrowIfNull(logger);

        _context = context;
        _tenantContextAccessor = tenantContextAccessor;
        _clock = clock;
        _efCoreOptions = efCoreOptions;
        _messagingOptions = messagingOptions;
        _logger = logger;
    }

    /// <inheritdoc />
    /// <inheritdoc />
    public async Task<IdempotencyReservation> TryBeginAsync(Guid messageId, CancellationToken ct)
    {
        var tenantId = EfCoreTenantScope.Resolve(_tenantContextAccessor.TenantId);
        var options = _efCoreOptions.Value;
        var now = _clock.UtcNow;
        var expiresAt = now + options.InFlightTtl;
        var token = Guid.NewGuid().ToString("N");

        try
        {
            // One statement, so the claim is atomic. The DO UPDATE fires only for a row whose lease
            // has lapsed and which never completed; a completed row and a live in-flight row both
            // fall through to the conflict with no rows affected.
            var affected = await _context.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    INSERT INTO idempotency_messages
                        (tenant_id, message_id, reserved_at_utc, expires_at_utc, reservation_token, completed_at_utc)
                    VALUES ({tenantId}, {messageId}, {now}, {expiresAt}, {token}, NULL)
                    ON CONFLICT (tenant_id, message_id) DO UPDATE
                    SET reserved_at_utc   = EXCLUDED.reserved_at_utc,
                        expires_at_utc    = EXCLUDED.expires_at_utc,
                        reservation_token = EXCLUDED.reservation_token
                    WHERE idempotency_messages.completed_at_utc IS NULL
                      AND idempotency_messages.expires_at_utc < {now}
                    """,
                    ct)
                .ConfigureAwait(false);

            if (affected > 0)
                return IdempotencyReservation.Started(token);

            // Nothing was written, so a row already holds this id. Read it to say which case it is.
            var existing = await _context.IdempotencyMessages
                .AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.MessageId == messageId)
                .Select(x => new { x.CompletedAtUtc })
                .FirstOrDefaultAsync(ct)
                .ConfigureAwait(false);

            // A row that vanished between the two statements means another delivery released it;
            // treating that as in-progress lets the broker redeliver rather than dropping it.
            return existing?.CompletedAtUtc is not null
                ? IdempotencyReservation.AlreadyProcessed()
                : IdempotencyReservation.InProgress();
        }
        catch (Exception ex) when (EfCoreStoreUnavailableClassifier.IsStoreUnavailable(ex))
        {
            // Fail-open means "let the consumer run", i.e. a Started reservation.
            return HandleStoreUnavailable(
                ex, nameof(TryBeginAsync), IdempotencyReservation.Started(token), options);
        }
    }

    /// <inheritdoc />
    public async Task CompleteAsync(Guid messageId, string reservationToken, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(reservationToken);

        var tenantId = EfCoreTenantScope.Resolve(_tenantContextAccessor.TenantId);
        var options = _efCoreOptions.Value;
        var now = _clock.UtcNow;
        var expiresAt = now + _messagingOptions.Value.ExpiryWindow;

        try
        {
            // The token predicate stops a stale holder — one whose lease lapsed and whose id was
            // re-claimed by a redelivery — from marking the new holder's work complete.
            await _context.IdempotencyMessages
                .Where(x => x.TenantId == tenantId
                            && x.MessageId == messageId
                            && x.ReservationToken == reservationToken)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(x => x.CompletedAtUtc, now)
                        .SetProperty(x => x.ExpiresAtUtc, expiresAt),
                    ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (EfCoreStoreUnavailableClassifier.IsStoreUnavailable(ex))
        {
            HandleStoreUnavailable(ex, nameof(CompleteAsync), fallback: false, options);
        }
    }

    /// <inheritdoc />
    public async Task ReleaseAsync(Guid messageId, string reservationToken, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(reservationToken);

        var tenantId = EfCoreTenantScope.Resolve(_tenantContextAccessor.TenantId);
        var options = _efCoreOptions.Value;

        try
        {
            // Delete rather than expire, so the broker's redelivery can claim the id immediately.
            // Guarded on the token and on the row still being in flight, so a release can never
            // erase a completed record.
            await _context.IdempotencyMessages
                .Where(x => x.TenantId == tenantId
                            && x.MessageId == messageId
                            && x.ReservationToken == reservationToken
                            && x.CompletedAtUtc == null)
                .ExecuteDeleteAsync(ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (EfCoreStoreUnavailableClassifier.IsStoreUnavailable(ex))
        {
            // Safe to swallow: an unreleased row expires with its lease, so the message is retried
            // slightly later rather than lost, and the consumer's real failure keeps propagating.
            HandleStoreUnavailable(ex, nameof(ReleaseAsync), fallback: false, options);
        }
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
