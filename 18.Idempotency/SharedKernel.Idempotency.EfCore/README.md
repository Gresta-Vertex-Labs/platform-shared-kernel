# SharedKernel.Idempotency.EfCore

Atomic, tenant-scoped, PostgreSQL-backed implementation of `SharedKernel.Idempotency.Abstractions`'
`IIdempotencyStore`, for services that run PostgreSQL and do not want Redis solely for deduplication. See the
[abstractions package's README](../SharedKernel.Idempotency.Abstractions/README.md) for the contract and how the
pipeline and MassTransit use it.

**Tier:** Adapter. References `SharedKernel.Idempotency.Abstractions`, `SharedKernel.Primitives` and
`SharedKernel.Persistence.EfCore` (its one declared adapter edge, for `UsePostgres`) — never `02.Caching`.

## Install

```xml
<PackageReference Include="SharedKernel.Idempotency.EfCore" />
```

Versions come from your single `SharedKernelVersion` property (the repository's `PLATFORM.md`, "Consuming the
kernel").

## Quick start

```csharp
services.AddClock(); // 01.Core/SharedKernel.Primitives — IClock, required
var dataSource = NpgsqlDataSource.Create(connectionString); // or the service's shared data source
services.AddEfCoreIdempotency(
    configureDbContext: options => options.UsePostgres(dataSource), // namespace SharedKernel.Persistence
    purposes: p => p.ForRequests().ForMessages(),
    configureOptions: o => o.AllowExecutionOnStoreUnavailable = false); // the default: fail closed
```

`AddEfCoreIdempotency` registers `IdempotencyDbContext` and `EfCoreIdempotencyStore` as the keyed
`IIdempotencyStore` for every selected purpose, and the ambient `IRequestContextAccessor` unless one exists. The
reservation lease and the retention window are passed by the caller on every call, so this package has no TTL
settings.

## Table

One table, `idempotency_keys`, one row per (tenant scope, purpose, key). The key is stored exactly as given. For
`IdempotencyPurpose.Request` it is, through `IdempotencyBehavior`, never the command's raw key but a SHA-256 digest of
tenant, caller and key — always 64 lowercase hex characters, whatever the raw key's length (P-562 X3) — so a
reservation belongs to one caller of one tenant, and another caller using the same key cannot be handed its stored
response. The `tenant_scope` column stays on top of that.

| Column | Type | Notes |
| --- | --- | --- |
| `tenant_scope` | `varchar(36)` | `IdempotencyTenantScope`: tenant id in "D" form, or `no-tenant` |
| `purpose` | `varchar(16)` | `Request` or `Message` |
| `key` | `varchar(512)` | the idempotency key, or the message id in "D" form |
| `fingerprint` | `varchar(128)` | |
| `status` | `varchar(20)` | `InProgress` or `Completed` |
| `reservation_token` | `uuid` | fresh per winning reservation |
| `reserved_at_utc`, `expires_at_utc` | `timestamptz` | from `IClock`; `expires_at_utc` indexed for cleanup |
| `response` | `text` | stored response, never parsed; `NULL` for messages |

Primary key `(tenant_scope, purpose, key)`.

**Persisted format (P-568).** The table was previously keyed by `(tenant_id uuid, key)` with a sentinel GUID
`00000000-0000-0000-0000-000000000001` for "no tenant", and messages lived in a separate `idempotency_messages`
table. Both stores now share `idempotency_keys` with a `purpose` column and the string tenant scope; drop
`idempotency_messages` and recreate `idempotency_keys` from a new migration (nothing was in production).

## Atomicity

`TryBeginAsync` is one raw-SQL upsert on the context's own connection:

```sql
INSERT INTO idempotency_keys (tenant_scope, purpose, "key", fingerprint, status, reserved_at_utc, expires_at_utc, response, reservation_token)
VALUES (@tenant_scope, @purpose, @key, @fingerprint, 'InProgress', @reserved_at_utc, @expires_at_utc, NULL, @token)
ON CONFLICT (tenant_scope, purpose, "key") DO UPDATE SET
    fingerprint = CASE WHEN idempotency_keys.expires_at_utc <= @reserved_at_utc THEN EXCLUDED.fingerprint ELSE idempotency_keys.fingerprint END,
    -- status / reserved_at_utc / expires_at_utc / response / reservation_token follow the same CASE shape
RETURNING fingerprint, status, response, reservation_token
```

Every `SET` is a no-op unless the existing row has expired, so a live row is returned unchanged. The call won the
row (fresh insert or expired-row reclaim) exactly when the returned `reservation_token` is its own — one round trip,
no second `SELECT`. `CompleteAsync`/`ReleaseAsync` touch the row only while the supplied token matches and the row is
still `InProgress`; a malformed token is rejected without a round trip. A reservation never completed or released
is reclaimed by the next `TryBeginAsync` after its `ttl`, or deleted by the cleanup job below.

## Fail-closed by default

When PostgreSQL is unreachable every call throws. `EfCoreIdempotencyOptions.AllowExecutionOnStoreUnavailable =
true` instead lets `TryBeginAsync` return `Started`, logging EventId 18100 at Warning. Retry is switched off for
this context on purpose: each store call is one atomic statement, and the caller owns the retry.

> **ENABLING `AllowExecutionOnStoreUnavailable` INCREASES DUPLICATE-EXECUTION RISK.** It applies to every purpose
> the registration serves.

## Migrations

This package ships no migrations. Add a design-time factory to your migrations project:

```csharp
public sealed class IdempotencyDbContextFactory : IDesignTimeDbContextFactory<IdempotencyDbContext>
{
    public IdempotencyDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<IdempotencyDbContext>();
        optionsBuilder.UsePostgres(NpgsqlDataSource.Create("Host=localhost;Database=mydb;Username=...;Password=..."));
        return new IdempotencyDbContext(optionsBuilder.Options);
    }
}
```

Then `dotnet ef migrations add InitialIdempotency --context IdempotencyDbContext`.

## Cleanup (bounded retention)

This package never prunes rows itself. Register a periodic job:

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
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM idempotency_keys WHERE expires_at_utc < {clock.UtcNow}", stoppingToken);
        }
    }
}
```

## Registration lifetime

The store and `IdempotencyDbContext` are `Scoped` — a `DbContext` is not thread-safe.

## Upgrading from the pre-WO-086 API

| Before | Now |
| --- | --- |
| `AddSharedKernelEfCoreIdempotency(db => …, o => …)` | `AddEfCoreIdempotency(db => …, p => p.ForRequests().ForMessages(), o => …)` — purposes are explicit |
| `EfCoreRequestIdempotencyStore` + `EfCoreIdempotencyMessageStore` (two contracts) | `EfCoreIdempotencyStore`, one `IIdempotencyStore` keyed by `IdempotencyPurpose` |
| `EfCoreIdempotencyOptions.InFlightTtl` / `.RetentionWindow` | Removed — the caller passes them: `IdempotencyBehaviorOptions.LeaseDuration`/`RetentionWindow`, messaging `IdempotencyOptions.LeaseDuration`/`ExpiryWindow` |
| Tenant from Messaging's `ITenantContextAccessor` (startup check required one) | Tenant from the ambient `IRequestContextAccessor`; no startup validator |
| Tables `idempotency_keys (tenant_id uuid, key)` + `idempotency_messages` | One table `idempotency_keys (tenant_scope, purpose, key)` — new migration (see "Persisted format" above) |

## Related packages

- [`SharedKernel.Idempotency.Abstractions`](../SharedKernel.Idempotency.Abstractions/README.md) — the contract.
- [`SharedKernel.Idempotency.Redis`](../SharedKernel.Idempotency.Redis/README.md) — the Redis sibling.
- `SharedKernel.Persistence.EfCore` (`06.Persistence`) — `UsePostgres(dataSource)`.
- `SharedKernel.Idempotency.Testing` (`16.Testing`) — `FakeIdempotencyStore` for unit tests.
