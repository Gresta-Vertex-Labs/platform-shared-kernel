using System.Runtime.ExceptionServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharedKernel.Application.Behaviors.Idempotency;
using SharedKernel.Idempotency.EfCore.Context;
using SharedKernel.Idempotency.EfCore.Entities;
using SharedKernel.Idempotency.EfCore.Internal;
using SharedKernel.Idempotency.EfCore.Logging;
using SharedKernel.Idempotency.EfCore.Options;
using SharedKernel.Messaging.Abstractions.TenantContext;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Idempotency.EfCore.KeyStore;

/// <summary>
/// Atomic, tenant-scoped PostgreSQL implementation of <see cref="IIdempotencyKeyStore"/> AND
/// <see cref="IIdempotencyResponseStore"/> on a single instance.
/// </summary>
/// <remarks>
/// <para>
/// Implements both interfaces on the same class deliberately (Domain Invariant 7 / D-01) — see
/// <see cref="SharedKernel.Idempotency.Redis.KeyStore.RedisIdempotencyKeyStore"/>'s remarks for the
/// full reasoning (identical here).
/// </para>
/// <para>
/// Registered <c>Scoped</c>, matching <see cref="IdempotencyDbContext"/>'s own <c>AddDbContext</c>
/// scoped lifetime (EF Core <see cref="DbContext"/> instances are not thread-safe and must not be
/// shared across concurrent operations, let alone captured into a singleton).
/// </para>
/// <para>See <c>18.Idempotency/CLAUDE.md</c> Domain Invariant 1 and D-05 for the exact atomic-reservation protocol.</para>
/// </remarks>
public sealed class EfCoreIdempotencyKeyStore : IIdempotencyKeyStore, IIdempotencyResponseStore
{
    private readonly IdempotencyDbContext _context;
    private readonly ITenantContextAccessor _tenantContextAccessor;
    private readonly IClock _clock;
    private readonly IOptions<EfCoreIdempotencyOptions> _options;
    private readonly ILogger<EfCoreIdempotencyKeyStore> _logger;

    /// <summary>Initializes a new instance of <see cref="EfCoreIdempotencyKeyStore"/>.</summary>
    public EfCoreIdempotencyKeyStore(
        IdempotencyDbContext context,
        ITenantContextAccessor tenantContextAccessor,
        IClock clock,
        IOptions<EfCoreIdempotencyOptions> options,
        ILogger<EfCoreIdempotencyKeyStore> logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(tenantContextAccessor);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _context = context;
        _tenantContextAccessor = tenantContextAccessor;
        _clock = clock;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> HasProcessedAsync(string idempotencyKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        var tenantId = EfCoreTenantScope.Resolve(_tenantContextAccessor.TenantId);
        var options = _options.Value;
        var now = _clock.UtcNow;
        var expiresAt = now + options.InFlightTtl;

        try
        {
            // Single-round-trip atomic upsert: a fresh row (no conflict) or a reclaimed expired
            // row both return an affected-row count of 1; a live, unexpired conflicting row
            // returns 0 (the WHERE guard makes DO UPDATE a no-op for that row). No SELECT is ever
            // issued for correctness (D-05). "response = NULL" on reclaim is a deliberate addition
            // to D-05's literal wording: without it, a reclaimed row's stale response from a prior
            // (fully expired) reservation episode would incorrectly resurface, since
            // TryGetStoredResponseAsync's only staleness guard is ExpiresAtUtc, and reclaim resets
            // that column to a fresh, non-expired value in the same statement.
            var affected = await _context.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    INSERT INTO idempotency_keys (tenant_id, "key", reserved_at_utc, expires_at_utc, response)
                    VALUES ({tenantId}, {idempotencyKey}, {now}, {expiresAt}, NULL)
                    ON CONFLICT (tenant_id, "key") DO UPDATE
                    SET reserved_at_utc = EXCLUDED.reserved_at_utc,
                        expires_at_utc = EXCLUDED.expires_at_utc,
                        response = NULL
                    WHERE idempotency_keys.expires_at_utc < {now}
                    """,
                    cancellationToken)
                .ConfigureAwait(false);

            // affected == 1 -> fresh reservation or reclaimed-expired row -> not yet processed
            // affected == 0 -> a live, unexpired row already exists -> already processed
            return affected == 0;
        }
        catch (Exception ex) when (EfCoreStoreUnavailableClassifier.IsStoreUnavailable(ex))
        {
            return HandleStoreUnavailable(ex, nameof(HasProcessedAsync), fallback: false, options);
        }
    }

    /// <inheritdoc />
    public async Task MarkProcessedAsync(string idempotencyKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        var tenantId = EfCoreTenantScope.Resolve(_tenantContextAccessor.TenantId);
        var options = _options.Value;
        var expiresAt = _clock.UtcNow + options.RetentionWindow;

        try
        {
            // Extends expiry only, leaves Response untouched — mirrors the Redis PEXPIRE
            // semantics, so this can never clobber a response StoreResponseAsync already wrote,
            // regardless of call order (D-05).
            await _context.IdempotencyKeys
                .Where(x => x.TenantId == tenantId && x.Key == idempotencyKey)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ExpiresAtUtc, expiresAt), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (EfCoreStoreUnavailableClassifier.IsStoreUnavailable(ex))
        {
            HandleStoreUnavailable(ex, nameof(MarkProcessedAsync), fallback: false, options);
        }
    }

    /// <inheritdoc />
    public async Task<string?> TryGetStoredResponseAsync(string idempotencyKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        var tenantId = EfCoreTenantScope.Resolve(_tenantContextAccessor.TenantId);
        var options = _options.Value;
        var now = _clock.UtcNow;

        try
        {
            // An expired row never surfaces a stale response (D-05) — the ExpiresAtUtc guard
            // excludes it even though the row itself has not been physically deleted yet.
            return await _context.IdempotencyKeys
                .AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.Key == idempotencyKey && x.ExpiresAtUtc > now)
                .Select(x => x.Response)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (EfCoreStoreUnavailableClassifier.IsStoreUnavailable(ex))
        {
            return HandleStoreUnavailable(ex, nameof(TryGetStoredResponseAsync), fallback: (string?)null, options);
        }
    }

    /// <inheritdoc />
    public async Task StoreResponseAsync(string idempotencyKey, string serializedResponse, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentNullException.ThrowIfNull(serializedResponse);

        var tenantId = EfCoreTenantScope.Resolve(_tenantContextAccessor.TenantId);
        var options = _options.Value;
        var now = _clock.UtcNow;

        try
        {
            // The ExpiresAtUtc guard mirrors the Redis Lua script's "if EXISTS" check — a row that
            // is logically expired (even if not yet physically deleted by the cleanup recipe) is
            // treated as absent, so a stale/expired reservation never absorbs a late response
            // write meant for a different reservation episode.
            await _context.IdempotencyKeys
                .Where(x => x.TenantId == tenantId && x.Key == idempotencyKey && x.ExpiresAtUtc > now)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Response, serializedResponse), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (EfCoreStoreUnavailableClassifier.IsStoreUnavailable(ex))
        {
            HandleStoreUnavailable(ex, nameof(StoreResponseAsync), fallback: false, options);
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
