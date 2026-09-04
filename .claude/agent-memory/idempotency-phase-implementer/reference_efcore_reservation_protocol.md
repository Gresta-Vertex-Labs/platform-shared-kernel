---
name: reference_efcore_reservation_protocol
description: Exact SQL used by SharedKernel.Idempotency.EfCore for atomic reservation, expiry reclaim, and why the response column must be cleared on reclaim.
type: reference
---

Implemented in `18.Idempotency/SharedKernel.Idempotency.EfCore/KeyStore/EfCoreIdempotencyKeyStore.cs` and `MessageStore/EfCoreIdempotencyMessageStore.cs`. Uses `Database.ExecuteSqlInterpolatedAsync` for the reservation (raw SQL is the only way to express `ON CONFLICT ... DO UPDATE ... WHERE`), and plain EF Core `ExecuteUpdateAsync`/LINQ for confirmation, response read/write (no raw SQL needed there — `ExecuteUpdateAsync` is a clean fit and avoids hand-rolled SQL for non-atomicity-critical paths).

**Reservation SQL** (key store; message store is identical minus the `response` column):
```sql
INSERT INTO idempotency_keys (tenant_id, "key", reserved_at_utc, expires_at_utc, response)
VALUES (@tenantId, @key, @now, @expiresAt, NULL)
ON CONFLICT (tenant_id, "key") DO UPDATE
SET reserved_at_utc = EXCLUDED.reserved_at_utc,
    expires_at_utc = EXCLUDED.expires_at_utc,
    response = NULL
WHERE idempotency_keys.expires_at_utc < @now
```
Affected-row count `1` = fresh insert or reclaimed-expired row → `HasProcessedAsync` returns `false`. `0` = live unexpired conflict → returns `true`.

**The `response = NULL` in the `DO UPDATE SET` list is a deliberate correction beyond the original design doc's literal wording**, recorded in code comments and `18.Idempotency/CLAUDE.md`. Without it: a reclaimed row's stale `Response` from a fully-expired prior reservation episode would incorrectly resurface to the NEW reservation episode, because `TryGetStoredResponseAsync`'s only staleness guard is `ExpiresAtUtc > now`, and the same reclaim statement resets `expires_at_utc` to a fresh non-expired value. If you ever touch this SQL, keep `response = NULL` in the `DO UPDATE SET` list.

**Confirmation**: `ExecuteUpdateAsync(setters => setters.SetProperty(x => x.ExpiresAtUtc, expiresAt))` — extends expiry only, never touches `Response`.

**Response write**: `ExecuteUpdateAsync` with a predicate `WHERE TenantId == t && Key == k && ExpiresAtUtc > now` (the `ExpiresAtUtc > now` guard mirrors the Redis Lua script's `if EXISTS` check — treats a logically-expired-but-not-yet-physically-deleted row as absent).

**Table/column naming**: explicit `.HasColumnName("...")` in each `IEntityTypeConfiguration<T>` (snake_case, matching the raw SQL identifiers hardcoded in the reservation statement) — do NOT rely solely on `SharedKernel.Persistence.PostgreSQL`'s `SnakeCaseNamingConvention` plugin for these two entities, since the raw SQL must match exactly and an implicit convention is one more place drift could sneak in. `UsePostgreSQL(connectionString)` is still called (for pgvector/retry-on-failure infrastructure, harmless-if-unused here) but column names are pinned explicitly.

**Composite primary key IS the unique constraint** — `(TenantId, Key)` / `(TenantId, MessageId)` as the actual PK, no surrogate id column. Simpler than a separate unique index.

**Schema for tests**: no real EF Core migrations ship with this package (by design — consumer adds their own via a documented design-time-factory recipe in each README). Tests create the schema via `context.Database.EnsureCreatedAsync()` against the same `IEntityTypeConfiguration<T>` model — model-equivalent to a migration for narrow test purposes.
