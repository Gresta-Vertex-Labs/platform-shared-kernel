---
name: wo023-caching-redis-topology
description: WO-023 decision — 02.Caching Redis package split into Core + 4 role-specific packages (L2, DistributedLocking, HashStore, PubSub)
metadata:
  type: project
---

WO-023 (P-140–P-146) splits the monolithic `SharedKernel.Caching.Redis` package into five packages, all within `02.Caching`.

**User's three observations and verdicts:**
1. "FusionCache vs Redis package split looks wrong" — DECLINED. The `.FusionCache`/`.Redis` provider split is correct gold-standard `.{Capability}.{Provider}` naming. Conflating them back would be the anti-pattern.
2. "One Redis package does too much, split by role (locking, hashing, etc.)" — UPGRADED. Correct intent, but a 1:1 interface-to-package split is overkill. Cut by infrastructure role with a shared connection core.
3. "Redis Pub/Sub belongs in 07.Messaging since it's messaging not caching" — UPGRADED (redirected). Correct that it doesn't feel like "caching," but the destination is wrong. `IRedisChannelService`/`ICacheInvalidationBus` are explicitly ephemeral/no-delivery-guarantee — the architectural opposite of `07.Messaging`'s durable/outbox contract. Moving it would violate layering (07.Messaging can't reference 02.Caching types like `CacheInvalidationMessage`/`CachingCoreOptions`) and create a guarantee-mismatch trap. Solution: extract to its own package `SharedKernel.Caching.Redis.PubSub`, staying in `02.Caching`, with the boundary now enforced by package naming itself plus a new root hard rule forbidding `02.Caching` <-> `07.Messaging` cross-references.

**New package topology (P-140–P-144):**
- `SharedKernel.Caching.Redis.Core` (P-140) — shared `IConnectionMultiplexer` (TryAddSingleton, first-caller-wins preserved), `ConnectionHealthState` tracking, Polly v8 circuit breaker pipeline. Zero capability-specific types.
- `SharedKernel.Caching.Redis` (P-141) — slimmed to ONLY `AddRedisL2`/FusionCache backplane/Brotli. Sources multiplexer from `.Redis.Core`.
- `SharedKernel.Caching.Redis.DistributedLocking` (P-142) — `IDistributedLockService`/`IRenewableLock`/RedLock.net.
- `SharedKernel.Caching.Redis.HashStore` (P-143) — `IRedisHashService`/`ITypedHashStore<T>`.
- `SharedKernel.Caching.Redis.PubSub` (P-144) — `IRedisChannelService`/`ICacheInvalidationBus`/`CacheInvalidationReceiver`.

All four capability packages depend on `.Redis.Core` + `Abstractions` only — never on each other (enforced by P-145 NetArchTest rules).

**`SharedKernel.Caching.Abstractions` interface contracts are UNCHANGED** — this is a packaging/assembly refactor, not an interface redesign. Major version bump for affected NuGet packages.

**New naming pattern documented in root CLAUDE.md:** `SharedKernel.{Capability}.{Provider}.Core` + `SharedKernel.{Capability}.{Provider}.{Role}` — for when one technology serves multiple architectural roles within a capability. Reusable for future cases (e.g., if PostgreSQL ever needed a similar split).

**New root hard rule:** `07.Messaging` <-> `02.Caching` mutual exclusion — codified in Layering Rules hard rules list.

**Domain states at write time:** 02.Caching = `●` Complete, 00.Governance = `●` Complete, 16.Testing = `◐` In Progress. None qualified for `state-map-phase` per Step 6 (only `○` domains get that call) — all three phases (P-140–P-146 across 02.Caching/00.Governance/16.Testing) are queue-only for `/dispatch-phase`.

See [[project_phase_numbering]] for the updated P-NNN/WO-NNN state.
