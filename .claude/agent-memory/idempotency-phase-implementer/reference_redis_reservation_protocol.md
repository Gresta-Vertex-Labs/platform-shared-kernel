---
name: reference_redis_reservation_protocol
description: Exact Redis commands used by SharedKernel.Idempotency.Redis (RedisIdempotencyStore) for atomic reservation, completion, release and response replay, and why.
type: reference
---

> WO-086 (2026-09): `RedisRequestIdempotencyStore` + `RedisIdempotencyMessageStore` (implementing `05`'s old `IRequestIdempotencyStore` and `07`'s old `IIdempotencyStore`) were replaced by one `RedisIdempotencyStore` implementing `SharedKernel.Idempotency.Abstractions.IIdempotencyStore` for both `IdempotencyPurpose.Request` and `.Message`; the message-store sentinel protocol and `RedisIdempotencyResponseSentinel` were deleted.

**Current (post WO-086).** `IIdempotencyStore.TryBeginAsync(purpose, key, fingerprint, ttl, ct)` returns an
`IdempotencyReservation` (`Started(token)`/`InProgress`/`Completed(response)`/`FingerprintMismatch`);
`CompleteAsync(purpose, key, token, response, retention, ct)` and `ReleaseAsync(purpose, key, token, ct)` return
`Task<bool>` (never throw for a lost reservation). The in-flight TTL and the retention window are **caller
arguments**, not store options. `RedisIdempotencyStore` holds **no reservation state of its own** — the caller
keeps the token from the reservation and passes it back.

Implemented in `18.Idempotency/SharedKernel.Idempotency.Redis/Store/RedisIdempotencyStore.cs`.

## Entry shape and scripts

Each entry is a Redis **hash**: `status` (`InProgress`/`Completed`), `fingerprint`, `token`, and — once completed
with a response — `response`. `TryBeginAsync`/`CompleteAsync`/`ReleaseAsync` are each a single Lua script via
`ScriptEvaluateAsync` — one atomic round trip apiece, never `WATCH`/`MULTI`.

**TryBegin** (`KEYS[1]`=key, `ARGV[1]`=fingerprint, `ARGV[2]`=in-flight TTL ms, `ARGV[3]`=fresh token):
```lua
if redis.call('EXISTS', key) == 0 then
    redis.call('HSET', key, 'status', 'InProgress', 'fingerprint', fingerprint, 'token', token)
    redis.call('PEXPIRE', key, ttlMs)
    return {'Started', false}
end
if redis.call('HGET', key, 'fingerprint') ~= fingerprint then
    return {'FingerprintMismatch', false}
end
if redis.call('HGET', key, 'status') == 'Completed' then
    return {'Completed', redis.call('HGET', key, 'response')}
end
return {'InProgress', false}
```
Fingerprint is compared **before** status — a reused key with a different fingerprint is always
`FingerprintMismatch`, in flight or completed. Lua tables truncate at the first `nil`, so the second element is
`false` except in the `Completed` branch — the C# side checks `!reply[1].IsNull`. The token is generated fresh on
every call and returned as `IdempotencyReservation.Started(token)` on a win; it is never stored on the .NET side.

**Complete** (`ARGV[1]`=token, `ARGV[2]`=response, `ARGV[3]`=retention ms, `ARGV[4]`=`'1'` when a response is
stored) — no-op (0 → `false`) unless the token matches **and** status is still `InProgress`; a `null` response
completes without a `response` field:
```lua
if redis.call('HGET', key, 'token') == token and redis.call('HGET', key, 'status') == 'InProgress' then
    if hasResponse == '1' then
        redis.call('HSET', key, 'status', 'Completed', 'response', response)
    else
        redis.call('HSET', key, 'status', 'Completed')
    end
    redis.call('PEXPIRE', key, retentionMs)
    return 1
end
return 0
```

**Release** (`ARGV[1]`=token) — `DEL` only while the token owns an `InProgress` entry; a completed entry is never
deleted by a release.

**Key shape**: `sk:idempotency:{tenantScope}:{kind}:{rawKey}` (`Internal/RedisIdempotencyKeyBuilder.cs`), `kind` =
`key` for `Request`, `msg` for `Message`; `tenantScope` = `IdempotencyTenantScope.Current(IRequestContextAccessor)`
(tenant id in `"D"` form, or `no-tenant`). Request keys are byte-identical to the pre-P-568 keys.

**No `IClock` needed** — all TTLs are relative durations passed straight into `PEXPIRE`.

**Fail-open token subtlety:** on a store-unreachable exception during `TryBeginAsync`, the fail-open fallback
(`RedisIdempotencyOptions.AllowExecutionOnStoreUnavailable`) still returns `Started(token)` with a token never
written to Redis. A later `CompleteAsync`/`ReleaseAsync` with it hits the same connectivity failure and follows the
same branch — `false` under fail-open, throw under fail-closed — never a silent success against a row that never
existed.

**Store registration lifetime**: `Scoped` by default (`AddIdempotencyStore<T>(purpose, lifetime = Scoped)`), one
keyed registration per purpose — see [[feedback_scoped_not_singleton_stores]].

## History — superseded designs

1. **Pre-P-544**: `RedisIdempotencyKeyStore` on the two-interface `IIdempotencyKeyStore`/`IIdempotencyResponseStore` split, plus a separate plain-string sentinel `RedisIdempotencyMessageStore` (`SET NX` + `KeyExpireAsync`).
2. **P-544 (2026-09-15)**: `RedisRequestIdempotencyStore` on `IRequestIdempotencyStore` — first with an instance-level `ConcurrentDictionary` of won tokens, then (same day) with the token as an explicit round-trip parameter and the dictionary removed.
3. **P-568/WO-086**: one `RedisIdempotencyStore` for both purposes on `SharedKernel.Idempotency.Abstractions`; TTL and retention moved from options to call arguments.
