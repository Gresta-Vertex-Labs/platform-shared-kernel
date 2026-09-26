---
name: feedback_scoped_not_singleton_stores
description: 18.Idempotency stores are registered Scoped (AddIdempotencyStore<T>'s default), not singleton — WO-070/D-09's "same singleton instance" framing was wrong; the original reason (a Scoped ITenantContextAccessor) is gone, EF Core's scoped DbContext still requires it.
type: feedback
---

> WO-086 (2026-09): `ITenantContextAccessor` was deleted; stores read the tenant from `SharedKernel.Execution`'s `IRequestContextAccessor` (an AsyncLocal-backed singleton, `TryAddSingleton` in both `Add*Idempotency` methods). The four store classes named below became `RedisIdempotencyStore` and `EfCoreIdempotencyStore`, each registered once per `IdempotencyPurpose` through `AddIdempotencyStore<T>(purpose, lifetime = ServiceLifetime.Scoped)`.

**Rule:** register `18.Idempotency` stores `Scoped` (the `AddIdempotencyStore<T>` default). Do not accept a design
doc's "singleton" framing without checking every constructor dependency's lifetime.

**Why:** `18.Idempotency/state-map.md`'s D-09 originally said the Redis store should be registered as "the same
singleton instance". That failed under `ValidateScopes = true` because the tenant seam the stores then injected
(`ITenantContextAccessor`) was Scoped platform-wide. The tenant seam is now a singleton, but
`EfCoreIdempotencyStore` injects `IdempotencyDbContext`, which is Scoped and unsafe to capture in a singleton, and the
shared `IConnectionMultiplexer` (`02.Caching.Redis.Core`'s `AddRedisConnection`) stays a genuine singleton under the
thin Scoped Redis store either way. Each keyed registration (one per purpose) is its own scoped instance; the stores
hold no reservation state, so nothing depends on sharing an instance.

**How to apply:** if a future arch-planner phase for this domain (or any domain wiring a store or behavior)
proposes singleton lifetime, check whether it depends on a `DbContext` or any other Scoped service before accepting
it — "singleton" in a design doc is a starting assumption, not a constraint that survives this platform's DI lifetime
conventions.
