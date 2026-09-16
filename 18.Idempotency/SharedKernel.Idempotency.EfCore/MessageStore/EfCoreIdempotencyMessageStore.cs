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
    public async Task<bool> HasProcessedAsync(Guid messageId, CancellationToken ct)
    {
        var tenantId = EfCoreTenantScope.Resolve(_tenantContextAccessor.TenantId);
        var options = _efCoreOptions.Value;
        var now = _clock.UtcNow;
        var expiresAt = now + options.InFlightTtl;

        try
        {
            var affected = await _context.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    INSERT INTO idempotency_messages (tenant_id, message_id, reserved_at_utc, expires_at_utc)
                    VALUES ({tenantId}, {messageId}, {now}, {expiresAt})
                    ON CONFLICT (tenant_id, message_id) DO UPDATE
                    SET reserved_at_utc = EXCLUDED.reserved_at_utc,
                        expires_at_utc = EXCLUDED.expires_at_utc
                    WHERE idempotency_messages.expires_at_utc < {now}
                    """,
                    ct)
                .ConfigureAwait(false);

            return affected == 0;
        }
        catch (Exception ex) when (EfCoreStoreUnavailableClassifier.IsStoreUnavailable(ex))
        {
            return HandleStoreUnavailable(ex, nameof(HasProcessedAsync), fallback: false, options);
        }
    }

    /// <inheritdoc />
    public async Task MarkProcessedAsync(Guid messageId, CancellationToken ct)
    {
        var tenantId = EfCoreTenantScope.Resolve(_tenantContextAccessor.TenantId);
        var options = _efCoreOptions.Value;
        var expiresAt = _clock.UtcNow + _messagingOptions.Value.ExpiryWindow;

        try
        {
            await _context.IdempotencyMessages
                .Where(x => x.TenantId == tenantId && x.MessageId == messageId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ExpiresAtUtc, expiresAt), ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (EfCoreStoreUnavailableClassifier.IsStoreUnavailable(ex))
        {
            HandleStoreUnavailable(ex, nameof(MarkProcessedAsync), fallback: false, options);
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
