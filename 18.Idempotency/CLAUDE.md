# 18.Idempotency — Domain Brain

## What This Domain Is

The **only legal home in the platform for a concrete idempotency store**. It ships production implementations of three contracts that other domains declare but — by construction — cannot implement themselves.

| Contract | Declared in | Purpose |
|---|---|---|
| `IIdempotencyKeyStore` | `05.Application.Behaviors` | Duplicate-submission protection for an in-process MediatR command |
| `IIdempotencyResponseStore` | `05.Application.Behaviors` | Optional response replay so a genuine retry-after-ambiguous-outcome returns the original result (WO-039/P-242) |
| `IIdempotencyStore` | `07.Messaging.Abstractions` | Idempotent-consumer deduplication for message handling |

This domain **declares no contracts of its own.** It is implementation-only. If you find yourself wanting to add an interface here that consumers depend on, that interface almost certainly belongs in the domain that owns the concept.

---

## Why This Domain Exists At All

This is the important part, and it is not obvious. Read it before proposing any restructuring.

Three idempotency contracts shipped platform-wide and **zero implementations existed** — only `16.Testing` fakes and two throwaway classes inside other domains' test suites. Every consuming microservice hand-rolled duplicate suppression. That was not neglect; it was structurally impossible to fix in place:

- `07.Messaging` may reference only `01–04`, **and** is under a hard rule in root `CLAUDE.md`: *07.Messaging must never reference any `SharedKernel.Caching.*` package.* So no Redis-backed `IIdempotencyStore` could live there. It may not reference `06.Persistence` either, so no EF-backed one could.
- `05.Application` is under the hard rule *05.Application must never reference a concrete infrastructure package — only abstractions.* So neither implementation could live there.
- No layer *below* `07.Messaging` may reference upward into it to implement its interface — that is the dependency direction the whole layering scheme forbids.

A domain numbered **above both `05` and `07`** is the only position from which a single package can legally reference down into both and implement all three. That is this domain's entire reason for existing (WO-070).

**Consequence:** do not "simplify" by moving these packages into `02.Caching`, `05.Application`, `06.Persistence`, or `07.Messaging`. Each of those moves is blocked by a named hard rule, and the move would have to break one to succeed.

---

## Packages

```
SharedKernel.Idempotency.Redis    → Redis-backed; built on 02.Caching.Redis.Core
SharedKernel.Idempotency.EfCore   → EF Core/PostgreSQL-backed; built on 06.Persistence.EfCore/.PostgreSQL
```

**No `.Abstractions` package exists in this domain, and none may be added.** The abstractions already exist — in `05.Application.Behaviors` and `07.Messaging.Abstractions`, owned by the domains that consume them. Adding `SharedKernel.Idempotency.Abstractions` would create a fourth, competing idempotency vocabulary and is the single most likely wrong turn a future session could take here.

The two provider packages are **siblings**: neither references the other, and there is no shared `.Core` package between them. They share an interface surface, not code — mirroring the `08.Storage` `.S3`/`.Obs` and `09.Search` `.Meilisearch`/`.ElasticSearch` precedents.

---

## Layering

```
18.Idempotency → may reference 01.Core, 02.Caching (.Redis package only),
                 05.Application.Behaviors, 06.Persistence (.EfCore package only),
                 07.Messaging.Abstractions
```

**Per-package reference rules — these are narrower than the domain line above and are what actually binds:**

| Package | May reference | Must never reference |
|---|---|---|
| `SharedKernel.Idempotency.Redis` | `01.Core`, `02.Caching.Redis.Core` (+ siblings as needed), `05.Application.Behaviors`, `07.Messaging.Abstractions` | `06.Persistence` — anything |
| `SharedKernel.Idempotency.EfCore` | `01.Core`, `06.Persistence.EfCore`/`.PostgreSQL`, `05.Application.Behaviors`, `07.Messaging.Abstractions` | `02.Caching` — anything |

A package that reached both backing stores would drag Redis into PostgreSQL-only services and vice versa, defeating the reason two providers exist.

Nothing in the platform references `18.Idempotency`. Consuming microservices reference it directly at their own composition root, choosing the provider they want — the same shape as every other `.Abstractions` + `.{Provider}` capability, except the abstraction half lives in someone else's domain.

---

## Domain Invariants

**1 — Atomicity is the product.** The public contracts expose separate `HasProcessedAsync`/`MarkProcessedAsync` calls, which is a check-then-act shape *at the interface level*. The implementation must close that race internally. A `SELECT`-then-`INSERT` or an `EXISTS`-then-`SET` is a defect, not a simplification, regardless of how narrow the window looks.

- `.Redis`: `HasProcessedAsync` performs an atomic conditional reservation (`SET key <sentinel> NX PX <inFlightTtl>`) that doubles as an in-flight lock — success means a fresh reservation (`false`, not yet processed); failure means a live entry already exists, in-flight or confirmed (`true`, block it). `MarkProcessedAsync` extends that reservation to the full retention window via `PEXPIRE` alone — it never rewrites the value, so it can never clobber a response `StoreResponseAsync` already wrote regardless of call order. `StoreResponseAsync` uses a short Lua script (`if EXISTS then SET ... KEEPTTL`) — this is the one and only Lua use in the domain; the base reservation itself never needs it.
- `.EfCore`: atomicity comes from a single raw-SQL `INSERT ... ON CONFLICT (TenantId, Key) DO UPDATE ... WHERE <existing row is expired>` upsert (`Database.ExecuteSqlInterpolatedAsync`), never a plain `DbSet.Add` + caught-`DbUpdateException` shape. The affected-row count (`1` = fresh reservation or reclaimed-expired row, `0` = live conflict) is the atomicity signal, in one round trip, with expiry-reclaim built into the same statement — no separate `SELECT` is ever needed for correctness.

**2 — A fault must not consume the key.** `IIdempotencyKeyStore.MarkProcessedAsync`'s existing documented semantics require that a thrown exception leaves the key retryable; only a `next()` return — success *or* business failure — consumes it. An unconfirmed reservation therefore expires on its own (the short in-flight TTL/`ExpiresAtUtc`) and the key becomes available again. Never mark on entry.

**3 — Tenant scoping is by construction, never by convention.** Every key is tenant-scoped through a composed seam (`.Redis`) or a mandatory `TenantId` column (`.EfCore`). A caller must not be able to produce a cross-tenant collision by supplying an unprefixed key string. Tenant identity itself is resolved through `07.Messaging.Abstractions`'s existing `TenantContext.ITenantContextAccessor` (`Guid? TenantId`), reused rather than reinvented — both provider packages already carry a legal reference to that assembly for `IIdempotencyStore`, so this authors no new contract in `18.Idempotency`, preserving "declares no contracts of its own." A `null` `TenantId` (unauthenticated/background/system context) resolves to a single fixed, non-caller-suppliable non-tenant segment — never simply omitted — so a null-tenant entry can never collide with a real tenant's entry. This mirrors `02.Caching`'s `ITenantCacheService` (P-435/WO-065), which exists because tag-based cross-tenant invalidation was reachable by convention error.

**4 — Fail-closed by default.** Store unavailability blocks the guarded command/consumer rather than letting it run unprotected. Fail-open is available as a single explicit `AllowExecutionOnStoreUnavailable` flag whose XML doc states **in capitals** that it increases duplicate-execution risk. A narrow internal exception classifier (never a blanket `catch (Exception)`) recognizes only genuine connectivity/timeout exceptions per provider, and applies uniformly across `HasProcessedAsync`/`MarkProcessedAsync`/`StoreResponseAsync` so a mid-flight outage after fail-open let a call through does not then throw out of the confirmation step. This follows the platform's conservative-default convention: deny-by-default CORS (P-404), fail-closed tenant cache scoping (P-435), WO-060's revocation-caching bias toward safety.

**5 — Retention is bounded and visible.** `.Redis` gets TTL for free. `.EfCore` must carry an `ExpiresAtUtc` column (with a non-unique index supporting a cleanup scan), exclude expired rows from reads, and ship cleanup as a **documented recipe** — a consumer-registered `IHostedService`, or a `19.Scheduling` job once that domain ships. This package must never start a hidden background loop of its own, and must never silently grow an unbounded table. `.EfCore`'s own `IdempotencyDbContext` deliberately does **not** extend `06.Persistence.EfCore`'s `SharedKernelDbContext` — that base's `SoftDeleteInterceptor` would silently turn the cleanup recipe's hard `DELETE` into an update, defeating retention outright, and its `ConcurrencyInterceptor` assumes a row-version column this table has no reason to carry. `IdempotencyDbContext` is a small, self-contained, plain-EF-Core context owning exactly two entities.

**6 — Opaque response payloads.** `IIdempotencyResponseStore` persists the caller-supplied serialized string exactly as given. This domain never inspects, reshapes, or re-serializes a stored response, and never assumes a format.

**7 — `IIdempotencyKeyStore` and `IIdempotencyResponseStore` are implemented by the same class, never split across two.** `IIdempotencyResponseStore`'s own XML docs state that response-replay detection is a runtime `is IIdempotencyResponseStore` pattern-match performed on the *injected* `IIdempotencyKeyStore` instance. If the two interfaces were implemented by separate classes, replay support would be structurally unreachable no matter how DI wired them. Both providers therefore ship exactly **two** physical store classes, not three (`RedisIdempotencyKeyStore`/`EfCoreIdempotencyKeyStore` cover both key-store interfaces; `RedisIdempotencyMessageStore`/`EfCoreIdempotencyMessageStore` cover `IIdempotencyStore` alone) — a deliberate, reasoned correction of WO-070's "three focused store classes" framing, recorded here so it is never "fixed" back to three in a later session.

---

## Technology

| Concern | Choice | Notes |
|---|---|---|
| Redis access | `02.Caching.Redis.Core`'s shared `IConnectionMultiplexer` | Never a privately-constructed multiplexer — connection, health, and the resilience pipeline are already owned there |
| Redis atomicity | `SET key <sentinel> NX PX` for reservation, `PEXPIRE` for confirmation, a 3-line Lua script only for the response-replay write | Never `WATCH`/`MULTI` optimistic retry loops. Lua is the exception, not the base path — the reservation itself is a single native command |
| Relational access | `06.Persistence.EfCore` + `.PostgreSQL` | `INSERT ... ON CONFLICT (TenantId, Key) DO UPDATE ... WHERE <expired>` via `Database.ExecuteSqlInterpolatedAsync` (Npgsql) — the `DO UPDATE ... WHERE` guard is what reclaims an expired reservation atomically, not a plain `DO NOTHING` |
| Tenant resolution | `07.Messaging.Abstractions.TenantContext.ITenantContextAccessor` (`Guid? TenantId`), reused — not reinvented | Both provider packages already reference that assembly for `IIdempotencyStore`; a `null` `TenantId` maps to one fixed non-tenant segment, never omitted |
| Options validation | `01.Core/SharedKernel.Configuration`'s `AddValidatedOptions` | Fail fast at `IHost.StartAsync()`, never at first use |
| Clock | `01.Core`'s `IClock` | Never `DateTime.UtcNow` — every `.EfCore` `ReservedAtUtc`/`ExpiresAtUtc` timestamp and expiry comparison is clock-sourced |
| Logging | `[LoggerMessage]`, EventIds `18000`–`18099` (`.Redis`), `18100`–`18199` (`.EfCore`) | `Idempotency = 18000` is **design-locked** in `01.Core`'s `LoggingEventIdRanges` (`SK.01.LoggingRangesNewDomains`, this session) but not yet shipped in code — the Core-phase logging tasks (C-05/C-10) stay blocked on that shipping; the sub-block split itself is safe to treat as fixed |

---

## What Goes Where (within this domain)

| I need to add… | It belongs in… |
|---|---|
| A new idempotency *contract* | **Not here.** `05.Application.Behaviors` for command-side, `07.Messaging.Abstractions` for consumer-side |
| A Redis-specific store behavior | `SharedKernel.Idempotency.Redis` |
| A relational store behavior | `SharedKernel.Idempotency.EfCore` |
| Tenant identity for a store call | Resolve via `07.Messaging.Abstractions.TenantContext.ITenantContextAccessor` — never a new `18.Idempotency`-local contract (Invariant 3/7) |
| Response-replay support on a provider | Implement `IIdempotencyResponseStore` on the **same class** that implements `IIdempotencyKeyStore` — never a second class; replay detection is a runtime `is` check on one instance (Invariant 7) |
| Code shared by both providers | Nowhere — duplicate it. There is deliberately no `.Core` sibling; the two providers must stay independently swappable |
| A third backing store (e.g. DynamoDB, Cosmos) | A new sibling `SharedKernel.Idempotency.{Provider}` — never a branch inside an existing provider |
| An in-memory test double | `16.Testing/SharedKernel.Testing` — `FakeIdempotencyKeyStore`/`FakeIdempotencyResponseStore` already exist there |
| A cleanup/expiry job | **Not here.** A documented consumer recipe, or a `19.Scheduling` job |

---

## Open Items

- `01.Core`'s `LoggingEventIdRanges` `Idempotency = 18000` entry is **design-locked but not yet shipped in code** (`SK.01.LoggingRangesNewDomains`). Core-phase tasks C-05/C-10 (the fail-open Warning log statements) stay blocked on that shipping; nothing else in this domain's plan depends on it. Tracked in the Cross-Domain Dependencies table of this domain's `state-map.md`.
- The full Design phase (D-01–D-09) is authored and locked — store-class decomposition, the tenant-resolution seam, both atomic-reservation protocols, the EfCore schema, the fail-open surface, TTL/retention options, and DI registration shape are all decided. Nothing here is still an open design question; what remains is Scaffold → Core → Tests → Docs → Published execution, in that order, per `state-map.md`.
- Root Phase Backlog P-454 and P-455 (WO-070) are `○` Pending. Nothing in this domain is implemented yet.
