# SharedKernel.Idempotency.EfCore

Atomic, tenant-scoped, PostgreSQL-backed implementation of the platform's two idempotency
contracts:

- `IRequestIdempotencyStore` (`SharedKernel.Application.Behaviors`) — `EfCoreRequestIdempotencyStore`.
- `IIdempotencyStore` (`SharedKernel.Messaging.Abstractions`) — `EfCoreIdempotencyMessageStore`.

For services that run PostgreSQL and do not want to run Redis solely for deduplication. See the
root `CLAUDE.md` Folder Map entry for `18.Idempotency` for why this domain exists at all.

## Quick start

```csharp
services.AddClock(); // 01.Core/SharedKernel.Primitives — IClock, required
services.AddSharedKernelEfCoreIdempotency(
    configureDbContext: options => options.UsePostgres(connectionString),
    configureOptions: o =>
    {
        o.InFlightTtl = TimeSpan.FromSeconds(30);
        o.RetentionWindow = TimeSpan.FromHours(24);
    });

// Required: bridge this platform's tenant identity source. ITenantContextAccessor lives in
// 07.Messaging.Abstractions and is reused here rather than reinvented.
services.AddScoped<ITenantContextAccessor, MyTenantContextAccessor>();
```

Omitting the `ITenantContextAccessor` registration throws `InvalidOperationException` at
`IHost.StartAsync()` — not at first store call.

## Contract

`IRequestIdempotencyStore.TryBeginAsync(key, requestFingerprint, ct)` atomically reserves a new key
and records the caller's request fingerprint, or reports the key's existing state:

| Existing row | Same fingerprint | Different fingerprint |
|---|---|---|
| None, or expired | `Started` — a fresh or reclaimed reservation | (not applicable) |
| Live, not completed | `InProgress` | `FingerprintMismatch` |
| Live, completed | `Completed`, with the stored response | `FingerprintMismatch` |

A winning `Started` result carries a `ReservationToken` — an opaque string the caller must pass back
to `CompleteAsync`/`ReleaseAsync`. `CompleteAsync(key, reservationToken, serializedResponse, ct)`
marks the key completed, stores the response, and extends `expires_at_utc` to `RetentionWindow`, but
only when `reservationToken` still owns the row **and** it is still `InProgress` — otherwise it
returns `false` and touches nothing. `ReleaseAsync(key, reservationToken, ct)` deletes the row under
the same guard — **only** while it is still `InProgress`; a completed row is never deleted by
`ReleaseAsync`. Both return `true` only when the token still owned the reservation and the operation
actually applied; `false` — never an exception — means the reservation was already lost: expired and
reclaimed by someone else, already completed or released, or a foreign/malformed token. A
reservation that is never completed or released stays past its `InFlightTtl` until either the next
`TryBeginAsync` for the same key reclaims it, or the documented cleanup recipe below deletes it —
either way, it can never permanently block that key.

## Atomicity

`TryBeginAsync` performs a single raw-SQL upsert, executed directly against the context's own
connection (not `ExecuteSqlInterpolatedAsync`, which discards `RETURNING` data):

```sql
INSERT INTO idempotency_keys (tenant_id, "key", fingerprint, status, reserved_at_utc, expires_at_utc, response, reservation_token)
VALUES (@tenant_id, @key, @fingerprint, 'InProgress', @reserved_at_utc, @expires_at_utc, NULL, @token)
ON CONFLICT (tenant_id, "key") DO UPDATE SET
    fingerprint = CASE WHEN idempotency_keys.expires_at_utc <= @reserved_at_utc THEN EXCLUDED.fingerprint ELSE idempotency_keys.fingerprint END,
    status = CASE WHEN idempotency_keys.expires_at_utc <= @reserved_at_utc THEN EXCLUDED.status ELSE idempotency_keys.status END,
    -- ...reserved_at_utc / expires_at_utc / response / reservation_token follow the same CASE shape
RETURNING fingerprint, status, response, reservation_token
```

Every `SET` clause is a no-op unless the existing row is already expired, so a live conflicting row
is returned completely unchanged. Comparing the row's *returned* `reservation_token` against the
token this call generated is how the store learns whether it won the row (fresh insert or
expired-row reclaim) versus merely observing an existing live one — one round trip, no separate
`SELECT`, and no plain `DbSet.Add()` + caught `DbUpdateException` control flow anywhere. This store
keeps none of that token for itself — it is returned to the caller as `ReservationToken` and never
remembered here.

`CompleteAsync`/`ReleaseAsync` only affect the row when the caller-supplied `reservationToken`
still matches the row's current `reservation_token` **and** the row is still `InProgress` — this
closes a race a slower confirm or release, arriving after its own reservation already expired and a
different caller has since re-reserved the same key, could otherwise corrupt. A syntactically
invalid (non-`Guid`) token is recognized as unable to match any real row without even a database
round trip.

## Fault vs. failure

A thrown exception from the guarded call never reaches `CompleteAsync` — the reservation's short
`InFlightTtl` window elapses, and the next `TryBeginAsync` for the same key reclaims the expired row
(self-healing, no action from this package). A returned business failure calls `ReleaseAsync`
instead, which deletes the row immediately. See `IRequestIdempotencyStore`'s own XML docs for the
full contract this package honors.

## Fail-closed by default

When PostgreSQL is unreachable, every store call throws by default. Set
`EfCoreIdempotencyOptions.AllowExecutionOnStoreUnavailable = true` to instead let `TryBeginAsync`
proceed as `Started` during an outage.

> **ENABLING `AllowExecutionOnStoreUnavailable` INCREASES DUPLICATE-EXECUTION RISK.** While the
> store is unreachable, every call — including genuine duplicates — is treated as novel. Only
> enable this for operations where executing twice is safer than blocking entirely.

## Migrations

This package ships no EF Core migrations. Add a design-time factory in your own service/migrations
project:

```csharp
public sealed class IdempotencyDbContextFactory : IDesignTimeDbContextFactory<IdempotencyDbContext>
{
    public IdempotencyDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<IdempotencyDbContext>();
        optionsBuilder.UsePostgres("Host=localhost;Database=mydb;Username=...;Password=...");
        return new IdempotencyDbContext(optionsBuilder.Options);
    }
}
```

Then run `dotnet ef migrations add InitialIdempotency --context IdempotencyDbContext` from that
project. The two tables (`idempotency_keys`, `idempotency_messages`) are snake_case-named via
`SharedKernel.Persistence.PostgreSQL`'s `UsePostgres(...)` conventions.

## Cleanup recipe (bounded retention)

This package never starts a hidden background loop and never prunes rows itself. Register your own
periodic cleanup — a plain `IHostedService`:

```csharp
public sealed class IdempotencyCleanupService(
    IDbContextFactory<IdempotencyDbContext> contextFactory,
    IClock clock) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await using var db = await contextFactory.CreateDbContextAsync(stoppingToken);
            var now = clock.UtcNow;
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM idempotency_keys WHERE expires_at_utc < {now}", stoppingToken);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM idempotency_messages WHERE expires_at_utc < {now}", stoppingToken);
        }
    }
}
```

Or, once `19.Scheduling` ships, a `ScheduledCommandJob`-wrapped equivalent.

## Registration lifetime

Both store classes and `IdempotencyDbContext` are registered `Scoped` — a plain EF Core
`DbContext` is not thread-safe and must never be captured into a singleton.

## Verifying DI registration resolves

```csharp
var host = Host.CreateDefaultBuilder()
    .ConfigureServices(services =>
    {
        services.AddClock();
        services.AddSharedKernelEfCoreIdempotency(o => o.UsePostgres(connectionString));
        services.AddScoped<ITenantContextAccessor, MyTenantContextAccessor>();
    })
    .Build();

await host.StartAsync(); // throws InvalidOperationException here if ITenantContextAccessor is missing

using var scope = host.Services.CreateScope();
var requestStore = scope.ServiceProvider.GetRequiredService<IRequestIdempotencyStore>();
var messageStore = scope.ServiceProvider.GetRequiredService<IIdempotencyStore>();
```
