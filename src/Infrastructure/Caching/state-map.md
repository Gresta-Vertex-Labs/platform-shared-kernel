# 02.Caching — State Map

> Living board for this domain: what exists and what is open. Completed work is not kept here; `git log` is the record.

## Legend

○ Not started · ◐ In progress · ● Done · ⚑ Blocked · ⊘ Declined / superseded

## Package Board

| Package | Tier | Status | Notes |
| --- | --- | --- | --- |
| `SharedKernel.Caching.Abstractions` | Abstractions | ● | `ICacheService`, `ITenantCacheService` (explicit `TenantId` on every call), `CachePolicy`, `CacheKeyFormat`, `IDistributedLockService` (locks, leases, fencing tokens), `ICacheWarmupStrategy`. The only caching package application code references. |
| `SharedKernel.Caching.FusionCache` | Adapter | ● | The cache: required `ServiceName`, Brotli compression, `AddCacheEncryption()` (cache key as associated data), warmup, `cache` readiness probe. |
| `SharedKernel.Caching.Redis.Core` | Adapter | ● | `AddRedisConnection(configuration)` — the one multiplexer (TLS/mTLS, fail-fast), `redis` readiness probe. |
| `SharedKernel.Caching.Redis` | Adapter | ● | `AddRedisL2()` — distributed L2 and backplane (removals, expirations, tag evictions, clears). Edge → Redis.Core. |
| `SharedKernel.Caching.Redis.DistributedLocking` | Adapter | ● | `IDistributedLockService` over Redis — kept-alive locks, leases, atomic fencing tokens. Edge → Redis.Core. |
| `SharedKernel.Caching.Redis.HashStore` | Adapter | ● | `IRedisHashService`, `ITypedHashStore<T>`. Edge → Redis.Core. |
| `SharedKernel.Caching.Redis.PubSub` | Adapter | ● | `IRedisChannelService` — loss-tolerant Pub/Sub, never messaging. Edge → Redis.Core. |

## Phase Key Registry

| Phase key | Phase | Status |
| --- | --- | --- |
| `SK.02.RecipeRequiredSettings` | Caching recipe names every required setting | ○ |

Closed keys live in `git log`: check a new key is unused with `git log --oneline -S"SK.NN.Key"`.

## Open Work

### SK.02.RecipeRequiredSettings — Caching recipe names every required setting `○`

**Work order:** WO-090 / P-629 · **Depends on:** None

The consumer recipe `add-caching` wires `AddSharedKernelCaching(configuration)` and `AddRedisConnection(configuration)` without saying that `SharedKernel:Caching:ServiceName` and `SharedKernel:Caching:Redis:ConnectionString` must be set, so an AI run builds a host that fails at start. Documentation-only phase: no production code, no public API, a 1.0.0 consumer is untouched. Planner verification (on disk): both settings are validated at host start. `ServiceName` — `CachingOptionsValidator` (`CacheKeyFormat.IsValidServiceName`: 1-64 lowercase ASCII letters, digits, `.`, `_`, `-`, starting with a letter or digit) behind `ValidateOnStart` on both `AddSharedKernelCaching` overloads (the configuration overload via `AddValidatedOptions`). `ConnectionString` — `[Required]` plus `RedisConnectionOptionsValidator` (must parse and name an endpoint) behind `ValidateOnStart`. `RedisL2Options` has no required setting (`KeyPrefix` optional, only bounds validated); locking, hash store and pub/sub have no settings of their own. So the claim "a missing required setting stops the host at startup" is true for both and may be documented; nothing is reported as unvalidated. Caveat to document precisely: failure surfaces when the host starts (`ValidateOnStart`), not at the `Add…` call.

| ID | Task | Package | State |
| --- | --- | --- | :---: |
| D-01 | Re-verify at implementation time that both `ValidateOnStart` registrations and both validators are still in place (read `CachingServiceCollectionExtensions`, `RedisConnectionCoreExtensions`, the two validators); if either is not, stop and report to the caller instead of writing the claim | recipe | `○` |
| DO-01 | In `consumer/skill/sharedkernel/recipes/add-caching.md`: add a "Required settings" table (setting path, package, rule, example) listing `SharedKernel:Caching:ServiceName` (FusionCache) and `SharedKernel:Caching:Redis:ConnectionString` (Redis.Core, needed by every Redis package), and a note that L2, locking, hash store and pub/sub add none | recipe | `○` |
| DO-02 | Same recipe, step 3 (and step 6 Configuration): show wiring with the settings present, an `appsettings.json` snippet (`"Caching": { "ServiceName": "catalog" }` plus the Redis connection supplied by environment `SharedKernel__Caching__Redis__ConnectionString`) next to the registration code, matching the Shop Catalog wiring | recipe | `○` |
| DO-03 | Same recipe "Check" section: add that a missing or malformed required setting fails host start (`OptionsValidationException`), that the message names the setting and never echoes the connection string, and that it surfaces when the host starts, not at registration | recipe | `○` |
| DO-04 | Add `samples/Shop/Catalog/Shop.Catalog.Api/appsettings.json` (carries the `ServiceName`) to the recipe's `proven-by` list; confirm every listed path still exists and the recipe's snippet matches `CatalogInfrastructure.cs` | recipe | `○` |
| T-01 | Run the consumer-skill checks (`dotnet run eng/generate-package-index.cs`, the recipe/proven-by checks described in `consumer/README.md`) and build the Shop Catalog projects; all green | consumer skill | `○` |

**Scope notes**
- `SKILL.md` needs table: **no row is touched** by this phase (the recipe's rows already route to `add-caching.md`). Do not edit `SKILL.md` unless `generate-package-index.cs` demands it; then say exactly which rows to the caller, since P-631 and P-633 edit the same table afterwards.
- Consumer vocabulary only in the recipe: no domain ids, `CLAUDE.md`, `state-map` or phase keys; no rule numbers.
- No package README, `PublicAPI` or EventId change.

**Acceptance:**
- [ ] `add-caching` names `ServiceName` and the Redis `ConnectionString` (and states L2 and the other Redis roles need nothing more) in a table and in the code it shows.
- [ ] The recipe states a missing required setting fails host start, and D-01 confirmed that against the real validators.
- [ ] The `proven-by` Shop paths exist and still match the recipe's wiring; consumer-skill checks and the Shop build are green.


## Blocked

None.

## Cross-Domain Dependencies

None open.
