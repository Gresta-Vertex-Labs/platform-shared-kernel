---
name: reference_efcore_reservation_protocol
description: Exact SQL used by SharedKernel.Idempotency.EfCore (EfCoreIdempotencyStore) for atomic reservation, expiry reclaim, and the reservation-token guard, and why.
type: reference
---

> WO-086 (2026-09): `EfCoreRequestIdempotencyStore` + `EfCoreIdempotencyMessageStore` (and the separate `idempotency_messages` table) were replaced by one `EfCoreIdempotencyStore` implementing `SharedKernel.Idempotency.Abstractions.IIdempotencyStore` for both purposes; the key became `(tenant_scope, purpose, key)`.

**Current (post WO-086).** `IIdempotencyStore.CompleteAsync`/`ReleaseAsync` take the reservation `token`
(`string`) and return `Task<bool>` (never throw for a lost reservation). `EfCoreIdempotencyStore` holds **no
reservation state of its own**; it parses the token with `Guid.TryParse` and short-circuits to `false` with zero DB
round trips on a malformed token. TTL (`TryBeginAsync`) and retention (`CompleteAsync`) are caller arguments; all
timestamps come from an injected `IClock`.

Implemented in `18.Idempotency/SharedKernel.Idempotency.EfCore/Store/EfCoreIdempotencyStore.cs`.

## Table

`idempotency_keys`, composite PK `(tenant_scope, purpose, "key")` (no surrogate id). `tenant_scope` is
`IdempotencyTenantScope.Current(IRequestContextAccessor)` (tenant id in `"D"` form, or `no-tenant`), `purpose` is
`IdempotencyPurpose` stored as string. Other columns: `fingerprint`, `status` (`IdempotencyRecordStatus`,
`HasConversion<string>()`), `reserved_at_utc`, `expires_at_utc` (indexed), `response`, `reservation_token` (`Guid`).
The raw-SQL literals `'InProgress'`/`'Completed'` come from `Internal/IdempotencyRecordStatusNames.cs`
(`nameof(IdempotencyRecordStatus.InProgress)` etc.) — keep them in sync with the enum member names.

**Why raw ADO.NET, not `Database.ExecuteSqlInterpolatedAsync`:** the protocol reads back
`fingerprint`/`status`/`response`/`reservation_token` via `RETURNING`, which `ExecuteSqlInterpolatedAsync` discards.
Open the context's own connection (`OpenConnectionAsync`/`CloseConnectionAsync`, ref-counted, nesting-safe), create a
`DbCommand` from `GetDbConnection()`, bind with `CreateParameter()` (no `@` on `ParameterName`, `@` in the SQL), then
`ExecuteReaderAsync` + one `ReadAsync`. Both `DbCommand` and `DbDataReader` support `await using`.

**TryBegin SQL** (`@reserved_at_utc` doubles as "now" for every expiry comparison):
```sql
INSERT INTO idempotency_keys (tenant_scope, purpose, "key", fingerprint, status, reserved_at_utc, expires_at_utc, response, reservation_token)
VALUES (@tenant_scope, @purpose, @key, @fingerprint, 'InProgress', @reserved_at_utc, @expires_at_utc, NULL, @token)
ON CONFLICT (tenant_scope, purpose, "key") DO UPDATE SET
    fingerprint = CASE WHEN idempotency_keys.expires_at_utc <= @reserved_at_utc THEN EXCLUDED.fingerprint ELSE idempotency_keys.fingerprint END,
    status = CASE WHEN idempotency_keys.expires_at_utc <= @reserved_at_utc THEN EXCLUDED.status ELSE idempotency_keys.status END,
    reserved_at_utc = CASE WHEN idempotency_keys.expires_at_utc <= @reserved_at_utc THEN EXCLUDED.reserved_at_utc ELSE idempotency_keys.reserved_at_utc END,
    expires_at_utc = CASE WHEN idempotency_keys.expires_at_utc <= @reserved_at_utc THEN EXCLUDED.expires_at_utc ELSE idempotency_keys.expires_at_utc END,
    response = CASE WHEN idempotency_keys.expires_at_utc <= @reserved_at_utc THEN NULL ELSE idempotency_keys.response END,
    reservation_token = CASE WHEN idempotency_keys.expires_at_utc <= @reserved_at_utc THEN EXCLUDED.reservation_token ELSE idempotency_keys.reservation_token END
RETURNING fingerprint, status, response, reservation_token
```
Every `SET` is a CASE on "is the existing row already expired" — if not, each field keeps its own value, a genuine
no-op that still participates in `RETURNING`. A `DO UPDATE ... WHERE <expired>` guard would make `RETURNING` produce
**nothing** for a live-conflict row, which is useless when the live row's values must be classified.

**Classifying the result — reservation token, not timestamp equality:** compare the **returned**
`reservation_token` with the token this call generated (`Guid.NewGuid()` per call). Equal → this call won (fresh
insert or reclaim) → `Started(token.ToString())`. Not equal → classify the existing row as
`InProgress`/`Completed`/`FingerprintMismatch`. Comparing `reserved_at_utc == @now` is wrong: two racing callers
under a coarse `IClock` can compute an identical `@now` and both think they won — exercised by the 32-way concurrent
`TryBeginAsync` test.

**Complete / Release — token guard and `Status == InProgress` on both, both `Guid.TryParse`-gated, both return `bool`:**
```csharp
// CompleteAsync — returns affected > 0
.Where(x => x.TenantScope == tenantScope && x.Purpose == purpose && x.Key == key
         && x.ReservationToken == token && x.Status == IdempotencyRecordStatus.InProgress)
.ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, IdempotencyRecordStatus.Completed)
                          .SetProperty(x => x.Response, response)
                          .SetProperty(x => x.ExpiresAtUtc, expiresAt))

// ReleaseAsync — returns affected > 0; InProgress makes this "delete only if not completed"
.Where(... same predicate ...).ExecuteDeleteAsync()
```

**Schema for tests**: `context.Database.EnsureCreatedAsync()` against the `IEntityTypeConfiguration<T>` model — no
migrations ship with this package.

## History — superseded designs

1. **Pre-P-544**: `EfCoreIdempotencyKeyStore` on the two-interface `IIdempotencyKeyStore`/`IIdempotencyResponseStore` split; a separate `EfCoreIdempotencyMessageStore` with its own `idempotency_messages` table (`ExecuteSqlInterpolatedAsync` upsert guarded by `WHERE expires_at_utc < @now`, affected-row count only).
2. **P-544 (2026-09-15)**: `EfCoreRequestIdempotencyStore` on `IRequestIdempotencyStore`, PK `(TenantId, Key)` — first remembering won tokens in a `ConcurrentDictionary<string, Guid>`, then (same day) with the token as an explicit parameter, the dictionary removed and `CompleteAsync` gaining the `Status == InProgress` guard.
3. **P-568/WO-086**: one `EfCoreIdempotencyStore` for both purposes on `SharedKernel.Idempotency.Abstractions`; `tenant_scope` + `purpose` columns replace `tenant_id`.
