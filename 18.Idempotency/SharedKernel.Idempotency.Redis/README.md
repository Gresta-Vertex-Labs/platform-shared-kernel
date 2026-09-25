# SharedKernel.Idempotency.Redis

Atomic, tenant-scoped, Redis-backed implementation of `SharedKernel.Idempotency.Abstractions`' `IIdempotencyStore`,
the one reservation contract behind the application pipeline's command idempotency and MassTransit's consumer
idempotency. See that package's README for the contract and how each caller uses it.

## Quick start

```csharp
services.AddRedisConnection(configuration);                  // 02.Caching.Redis.Core — the shared connection
services.AddRedisIdempotency(p => p.ForRequests().ForMessages());

// optional: the single fail-open switch (default: fail closed)
services.AddRedisIdempotency(p => p.ForRequests(), o => o.AllowExecutionOnStoreUnavailable = true);
```

`AddRedisIdempotency` registers `RedisIdempotencyStore` as the keyed `IIdempotencyStore` for every selected
purpose. It throws when `AddRedisConnection` has not been called, when no purpose is selected, or when a store is
already registered for a selected purpose. It registers the ambient `IRequestContextAccessor` unless one exists.
The reservation lease and the retention window are passed by the caller on every call
(`IdempotencyBehaviorOptions`, messaging's `IdempotencyOptions`), so this package has no TTL settings.

## Storage shape and atomicity

Each entry is one Redis hash — `status` (`InProgress`/`Completed`), `fingerprint`, `token` and, once completed with
a response, `response` — keyed as:

```
sk:idempotency:{tenantScope}:key:{rawKey}      IdempotencyPurpose.Request
sk:idempotency:{tenantScope}:msg:{messageId}   IdempotencyPurpose.Message
```

`{tenantScope}` is `IdempotencyTenantScope`'s encoding: the tenant id in "D" form, or `no-tenant`. `TryBeginAsync`,
`CompleteAsync` and `ReleaseAsync` are each one Lua script — one atomic round trip comparing fingerprint, token and
status and mutating the hash in the same call; no `WATCH`/`MULTI` loop and no check-then-act window. A new
reservation's hash expires after the caller's `ttl`; `CompleteAsync` extends it to the caller's `retention`.
`CompleteAsync` and `ReleaseAsync` act only while the supplied token owns an `InProgress` entry, so a late call
from a caller whose reservation expired and was reclaimed returns `false` and touches nothing, and a completed
entry is never released.

**Persisted format (P-568).** Request keys and hashes are byte-identical to the previous
`RedisRequestIdempotencyStore`. Message entries changed from a plain string (token or a completed sentinel) to the
same hash shape as requests, under the same `…:msg:{messageId}` key; an old string-valued message key fails with
`WRONGTYPE`, so flush those keys when upgrading (nothing was in production).

## Fail-closed by default

When Redis is unreachable every call throws. `RedisIdempotencyOptions.AllowExecutionOnStoreUnavailable = true`
instead lets `TryBeginAsync` return `Started` (and `CompleteAsync`/`ReleaseAsync` return `false`), logging
EventId 18000 at Warning.

> **ENABLING `AllowExecutionOnStoreUnavailable` INCREASES DUPLICATE-EXECUTION RISK.** While the store is
> unreachable, every call — including genuine duplicates — is treated as new. It applies to every purpose the
> registration serves.

## Registration lifetime

The store is `Scoped`; the shared `IConnectionMultiplexer` underneath stays a singleton.
