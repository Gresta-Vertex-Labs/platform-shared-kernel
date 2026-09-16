---
name: reference_efcore_reservation_protocol
description: Exact SQL used by SharedKernel.Idempotency.EfCore for atomic reservation, expiry reclaim, and the reservation-token guard, and why.
type: reference
---

**Current as of 2026-09-15 (P-544 same-day follow-up).** `IRequestIdempotencyStore.CompleteAsync`/`ReleaseAsync`
now take an explicit `reservationToken` (`string`) parameter and return `Task<bool>` (never throw for a
lost reservation). `EfCoreRequestIdempotencyStore` holds **no reservation state of its own** — no
`ConcurrentDictionary` at all. The caller (`IdempotencyBehavior`) remembers the token from
`IdempotencyBeginResult.ReservationToken` and passes it back explicitly; the store parses it with
`Guid.TryParse` and short-circuits to `false` with zero DB round trips on a malformed token.
`EfCoreIdempotencyMessageStore` (`IIdempotencyStore`) is unaffected and still uses the pre-migration shape
described at the bottom.

Implemented in `18.Idempotency/SharedKernel.Idempotency.EfCore/KeyStore/EfCoreRequestIdempotencyStore.cs`.

## `EfCoreRequestIdempotencyStore` (current)

Entity carries three extra columns: `fingerprint` (string), `status` (`IdempotencyRecordStatus` enum, `HasConversion<string>()`), `reservation_token` (`Guid`). The raw-SQL literal `'InProgress'`/`'Completed'` used inside the interpolated `const string` SQL comes from `Internal/IdempotencyRecordStatusNames.cs` (`nameof(IdempotencyRecordStatus.InProgress)` etc.) — keep these in sync with the enum's member names if either ever changes, since `HasConversion<string>()`'s default serialization is exactly `ToString()` of the member name.

**Why raw ADO.NET, not `Database.ExecuteSqlInterpolatedAsync`:** the protocol needs to read back `fingerprint`/`status`/`response`/`reservation_token` from whichever row exists after the upsert — that requires `RETURNING`, and EF Core's `ExecuteSqlInterpolatedAsync` throws away any result set. Solution: open the context's own connection (`await _context.Database.OpenConnectionAsync(ct)` / `CloseConnectionAsync()`, ref-counted, so nesting is safe) and issue a plain `DbCommand` via `_context.Database.GetDbConnection().CreateCommand()`, bind parameters with `command.CreateParameter()` (no `@` prefix on `ParameterName`, `@` prefix in the SQL text — Npgsql normalizes this), and `ExecuteReaderAsync` + `ReadAsync` once. `DbCommand`/`DbDataReader` both implement `IAsyncDisposable` since .NET 5, so `await using` works directly on them.

**TryBeginAsync SQL** (`@reserved_at_utc` doubles as "now" for every expiry comparison in the statement):
```sql
INSERT INTO idempotency_keys (tenant_id, "key", fingerprint, status, reserved_at_utc, expires_at_utc, response, reservation_token)
VALUES (@tenant_id, @key, @fingerprint, 'InProgress', @reserved_at_utc, @expires_at_utc, NULL, @token)
ON CONFLICT (tenant_id, "key") DO UPDATE SET
    fingerprint = CASE WHEN idempotency_keys.expires_at_utc <= @reserved_at_utc THEN EXCLUDED.fingerprint ELSE idempotency_keys.fingerprint END,
    status = CASE WHEN idempotency_keys.expires_at_utc <= @reserved_at_utc THEN EXCLUDED.status ELSE idempotency_keys.status END,
    reserved_at_utc = CASE WHEN idempotency_keys.expires_at_utc <= @reserved_at_utc THEN EXCLUDED.reserved_at_utc ELSE idempotency_keys.reserved_at_utc END,
    expires_at_utc = CASE WHEN idempotency_keys.expires_at_utc <= @reserved_at_utc THEN EXCLUDED.expires_at_utc ELSE idempotency_keys.expires_at_utc END,
    response = CASE WHEN idempotency_keys.expires_at_utc <= @reserved_at_utc THEN NULL ELSE idempotency_keys.response END,
    reservation_token = CASE WHEN idempotency_keys.expires_at_utc <= @reserved_at_utc THEN EXCLUDED.reservation_token ELSE idempotency_keys.reservation_token END
RETURNING fingerprint, status, response, reservation_token
```
Every `SET` is a CASE keyed on "is the existing row already expired" — if not, every field reverts to its own current (`idempotency_keys.*`) value, i.e. a genuine no-op that still participates in `RETURNING`. This is the key mechanical difference from a `WHERE <expired>` guard on the whole `DO UPDATE` clause: that shape would make `RETURNING` produce **nothing** for a live-conflict row (Postgres skips the row entirely when the `DO UPDATE ... WHERE` condition is false), which is useless once you need to read the live row's actual field values.

**Classifying the result — reservation token, not timestamp equality:** compare the **returned** `reservation_token` against the token this call generated (`Guid.NewGuid()` per call). Equal → this call won the row (fresh insert or reclaim) → `Started(token.ToString())`, returned to the caller, never stored anywhere on the .NET side. Not equal → read `fingerprint`/`status`/`response` off the *existing* row to classify `InProgress`/`Completed`/`FingerprintMismatch`.

**Why not compare `reserved_at_utc == @now` instead?** Two callers racing for the same key under a coarse-resolution `IClock` (or even a real clock with millisecond-only precision under heavy concurrency) could compute an *identical* `@now`, and if the first writer's fresh reservation happens to carry that exact same timestamp, the second (losing) writer would misclassify itself as the winner too — a genuine correctness bug, exercised by the 32-way concurrent `TryBeginAsync` test. A GUID token has no realistic collision risk and needs no clock-precision assumption at all.

**CompleteAsync / ReleaseAsync — token guard on both, both `Guid.TryParse`-gated, both return `bool`:**
```csharp
// CompleteAsync — returns affected > 0
.Where(x => x.TenantId == tenantId && x.Key == key && x.ReservationToken == token && x.Status == InProgress)
.ExecuteUpdateAsync(s => s.SetProperty(Status, Completed).SetProperty(Response, r).SetProperty(ExpiresAtUtc, ...))

// ReleaseAsync — returns affected > 0; Status == InProgress is what makes this "delete only if not completed"
.Where(x => x.TenantId == tenantId && x.Key == key && x.ReservationToken == token && x.Status == InProgress)
.ExecuteDeleteAsync()
```
Both parse `reservationToken` (a `string`) via `Guid.TryParse` first and return `false` immediately — no DB round trip — if it doesn't parse. Both now carry the `Status == InProgress` guard; **`CompleteAsync` gained this same-day** (previously it only checked the token, letting a second confirm silently re-write an already-completed row — `ReleaseAsync` already had the guard). Neither method remembers anything between calls — every invocation is a fresh, independent, stateless round trip keyed entirely by the caller-supplied token.

**Composite PK unchanged**: `(TenantId, Key)` as the actual PK, no surrogate id column.

**Schema for tests**: still `context.Database.EnsureCreatedAsync()` against the `IEntityTypeConfiguration<T>` model — no real migrations ship with this package.

## `EfCoreIdempotencyMessageStore` (unchanged — `IIdempotencyStore`, message-dedup only)

Still the pre-migration shape: `Database.ExecuteSqlInterpolatedAsync` upsert with `WHERE idempotency_messages.expires_at_utc < @now` guarding the whole `DO UPDATE` (affected-row count 0/1 is sufficient — this contract never needed `RETURNING`), and plain `ExecuteUpdateAsync` for `MarkProcessedAsync`. No fingerprint, no token, no `response` column on that table at all.

## History — superseded designs

1. **Pre-P-544**: `EfCoreIdempotencyKeyStore` implementing the old two-interface `IIdempotencyKeyStore`/`IIdempotencyResponseStore` split.
2. **P-544 (2026-09-15, same day, earlier revision)**: rewritten as `EfCoreRequestIdempotencyStore` implementing the new single `IRequestIdempotencyStore`, but each winning `TryBeginAsync` remembered its token (a `Guid`) in an instance-level `ConcurrentDictionary<string, Guid>` (keyed by raw key), consulted by `CompleteAsync`/`ReleaseAsync` (both took only `key` + payload, no token parameter). Replaced same-day once `IRequestIdempotencyStore` itself was redesigned to make the token an explicit `string` round-trip parameter instead of store-remembered state — the dictionary was removed entirely, and `CompleteAsync` gained the `Status == InProgress` guard it had been missing.
