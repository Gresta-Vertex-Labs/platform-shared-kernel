# 18.Idempotency — State Map

> Living board for this domain: what exists and what is open. Completed work is not kept here; `git log` is the record.

## Legend

○ Not started · ◐ In progress · ● Done · ⚑ Blocked · ⊘ Declined / superseded

## Package Board

| Package | Tier | Status | Notes |
| --- | --- | :---: | --- |
| `SharedKernel.Idempotency.Abstractions` | Abstractions | ● | The one purpose-keyed `IIdempotencyStore` (`IdempotencyPurpose.Request`/`.Message`), `IdempotencyReservation`, `IdempotencyTenantScope` (`no-tenant`), `AddIdempotencyStore<T>`/`HasIdempotencyStore`/`GetRequiredIdempotencyStore`. Consumed by `05`'s `IdempotencyBehavior` and `07`'s consumer idempotency. |
| `SharedKernel.Idempotency.Redis` | Adapter | ● | `RedisIdempotencyStore` (atomic Lua) for every purpose; `AddRedisIdempotency(p => …, o => …)`; message entries are hashes. Built on `Caching.Redis.Core` (declared adapter edge). |
| `SharedKernel.Idempotency.EfCore` | Adapter | ● | `EfCoreIdempotencyStore` (`INSERT … ON CONFLICT`); `AddEfCoreIdempotency(db => …, p => …, o => …)`; table keyed `(tenant_scope, purpose, key)`; opt-in `MigrateOnStartup` applies the shipped initial migration on the migrator connection. Built on `Persistence.EfCore` (declared adapter edge). |

Test double: `FakeIdempotencyStore` + `AddFakeIdempotencyStore(purposes)` in `src/Infrastructure/Idempotency/SharedKernel.Idempotency.Testing`. Provider tests run in the Integration lane against real Redis/PostgreSQL.

## Phase Key Registry

No open phase keys. Closed keys live in `git log`: check a new key is unused with `git log --oneline -S"SK.NN.Key"`.

## Open Work

None — every phase in this domain is complete.

## Blocked

None.

## Cross-Domain Dependencies

- **Outbound to 06.Persistence (note only):** the Npgsql README role script note should mention that package-supplied contexts such as the idempotency one rely on the `ALTER DEFAULT PRIVILEGES FOR ROLE app_migrator` defaults.
- **Outbound to consumer skill (`consumer/`):** the `add-idempotency` recipe lists the `MigrateOnStartup` option as `unproven` (the Shop creates its table itself and does not opt in); a Shop use of the flag would make a snippet provable, outside this domain.
- **Observation for 06.Persistence (note only):** under `UsePostgres` the history table's columns are snake_case (`migration_id`, `product_version`), because the snake-case naming convention applies to EF's history table too.

The domain needs no readiness probe of its own: Redis and database connectivity are covered by the `redis` probe and the persistence readiness checks.
