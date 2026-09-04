# SharedKernel.Idempotency.EfCore

Atomic, tenant-scoped, PostgreSQL-backed implementation of the platform's three idempotency
contracts:

- `IIdempotencyKeyStore` + `IIdempotencyResponseStore` (`SharedKernel.Application.Behaviors`) — one class, `EfCoreIdempotencyKeyStore`.
- `IIdempotencyStore` (`SharedKernel.Messaging.Abstractions`) — `EfCoreIdempotencyMessageStore`.

For services that run PostgreSQL and do not want to run Redis solely for deduplication. See the
root `CLAUDE.md` Folder Map entry for `18.Idempotency` for why this domain exists at all.

## Quick start

```csharp
services.AddClock(); // 01.Core/SharedKernel.Primitives — IClock, required
services.AddSharedKernelEfCoreIdempotency(
    configureDbContext: options => options.UsePostgreSQL(connectionString),
    configureOptions: o =>
    {
        o.InFlightTtl = TimeSpan.FromSeconds(30);
        o.RetentionWindow = TimeSpan.FromHours(24);
    });

// Required: bridge this platform's tenant identity source. ITenantContextAccessor lives in
// 07.Messaging.Abstractions and is reused here rather than reinvented (Design D-02).
services.AddScoped<ITenantContextAccessor, MyTenantContextAccessor>();
```

Omitting the `ITenantContextAccessor` registration throws `InvalidOperationException` at
`IHost.StartAsync()` — not at first store call.

## Atomicity

`HasProcessedAsync` performs a single raw-SQL upsert:

```sql
INSERT INTO idempotency_keys (tenant_id, "key", reserved_at_utc, expires_at_utc, response)
VALUES (@tenantId, @key, @now, @expiresAt, NULL)
ON CONFLICT (tenant_id, "key") DO UPDATE
SET reserved_at_utc = EXCLUDED.reserved_at_utc,
    expires_at_utc = EXCLUDED.expires_at_utc,
    response = NULL
WHERE idempotency_keys.expires_at_utc < @now
```

An affected-row count of `1` means a fresh row was inserted, or a genuinely expired row was
reclaimed (both mean "not yet processed" — `false`). `0` means a live, unexpired row already
exists ("already processed" — `true`). This single statement performs reservation AND
expiry-reclaim atomically, in one round trip — no `SELECT` is ever issued for correctness, and no
plain `DbSet.Add()` + caught `DbUpdateException` control flow is used anywhere.

`MarkProcessedAsync` extends `expires_at_utc` only, via `ExecuteUpdateAsync` — it never touches
`response`, so it can never clobber a response `StoreResponseAsync` already wrote regardless of
call order.

## Fault vs. failure

A thrown exception from the guarded call never reaches `MarkProcessedAsync` — the reservation's
short `InFlightTtl` expires on its own, and the next `HasProcessedAsync` call for the same key
reclaims the expired row (self-healing, no action from this package). A returned business failure
**does** consume the key; see `IIdempotencyKeyStore.MarkProcessedAsync`'s own XML docs for the full
fault-vs-failure contract this package honors but does not restate.

## Fail-closed by default

When PostgreSQL is unreachable, every store call throws by default. Set
`EfCoreIdempotencyOptions.AllowExecutionOnStoreUnavailable = true` to instead let the call proceed
as "not yet processed" during an outage.

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
        optionsBuilder.UsePostgreSQL("Host=localhost;Database=mydb;Username=...;Password=...");
        return new IdempotencyDbContext(optionsBuilder.Options);
    }
}
```

Then run `dotnet ef migrations add InitialIdempotency --context IdempotencyDbContext` from that
project. The two tables (`idempotency_keys`, `idempotency_messages`) are snake_case-named via
`SharedKernel.Persistence.PostgreSQL`'s `UsePostgreSQL(...)` conventions.

## Cleanup recipe (bounded retention)

This package never starts a hidden background loop and never prunes rows itself (Domain
Invariant 5). Register your own periodic cleanup — a plain `IHostedService`:

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
        services.AddSharedKernelEfCoreIdempotency(o => o.UsePostgreSQL(connectionString));
        services.AddScoped<ITenantContextAccessor, MyTenantContextAccessor>();
    })
    .Build();

await host.StartAsync(); // throws InvalidOperationException here if ITenantContextAccessor is missing

using var scope = host.Services.CreateScope();
var keyStore = scope.ServiceProvider.GetRequiredService<IIdempotencyKeyStore>();
var responseStore = keyStore as IIdempotencyResponseStore; // non-null — same instance
var messageStore = scope.ServiceProvider.GetRequiredService<IIdempotencyStore>();
```
