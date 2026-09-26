# 18.Idempotency — Domain Brain

> **Audience:** maintainers and AI agents changing code in this folder. Consumers read each package's
> `README.md`. Phase history lives in [`state-map.md`](state-map.md).

## What This Domain Is

The platform's **one idempotency contract and its production stores.** A single purpose-keyed reservation
contract, `IIdempotencyStore`, backs every duplicate-execution guard in a service:

| Caller | Purpose | Declared/used in |
|---|---|---|
| `IdempotencyBehavior<,>` — duplicate-submission protection and response replay for an in-process command (`IIdempotentRequest`) | `IdempotencyPurpose.Request` | `05.Application/SharedKernel.Application.Pipeline` |
| MassTransit consumer idempotency (`MessagingBusBuilder.WithIdempotency()`); key `{MessageId:D}:{sha256-hex("{endpoint path}\|{consumer type}")}`, one reservation per consumer | `IdempotencyPurpose.Message` | `07.Messaging/SharedKernel.Messaging.MassTransit` |

Both callers consume the contract; neither declares its own. Before WO-086 (P-568) there were two separate
contracts — `05`'s `IRequestIdempotencyStore` and Messaging's own `IIdempotencyStore` — and four store classes;
they are gone. Do not reintroduce a caller-specific store interface.

---

## Packages

| Package | Tier | Role | References |
|---|---|---|---|
| `SharedKernel.Idempotency.Abstractions` | Abstractions | `IIdempotencyStore`, `IdempotencyReservation`/`IdempotencyReservationStatus` (`Started`/`InProgress`/`Completed`/`FingerprintMismatch`), `IdempotencyPurpose` (`Request`/`Message`), `IdempotencyPurposeSelection`, `IdempotencyTenantScope` (the one tenant encoding: "D" GUID or `no-tenant`), `AddIdempotencyStore<T>(purpose \| purposes)`, `HasIdempotencyStore`, `GetRequiredIdempotencyStore` | `SharedKernel.Execution`, `Microsoft.Extensions.DependencyInjection.Abstractions` |
| `SharedKernel.Idempotency.Redis` | Adapter | `RedisIdempotencyStore` (one class, every purpose); `AddRedisIdempotency(p => …, o => …)` | Abstractions, `SharedKernel.Primitives`, `SharedKernel.Caching.Redis.Core` (declared adapter edge) |
| `SharedKernel.Idempotency.EfCore` | Adapter | `EfCoreIdempotencyStore` (one class, every purpose) over its own `IdempotencyDbContext`; `AddEfCoreIdempotency(db => …, p => …, o => …)` | Abstractions, `SharedKernel.Primitives`, `SharedKernel.Persistence.EfCore` (declared adapter edge) |

- The two providers are **siblings**: neither references the other and there is no shared `.Core`. Duplicate small
  helpers rather than share them, so each provider stays independently swappable.
- `.Redis` must never reference `06.Persistence`; `.EfCore` must never reference `02.Caching`. Each adapter→adapter
  edge is declared in the csproj's `<SharedKernelAllowedAdapterReferences>` (SKTIER002 otherwise). A package
  reaching both backing stores would drag Redis into PostgreSQL-only services and vice versa.
- Nothing in the kernel references the providers. `05.Application.Pipeline` and `07.Messaging.MassTransit`
  reference only `.Abstractions`; a service picks a provider at its composition root.
- Test double: `16.Testing/SharedKernel.Idempotency.Testing`'s `FakeIdempotencyStore`
  (`AddFakeIdempotencyStore(purposes)`), which implements the same reservation protocol.

---

## The contract

```csharp
Task<IdempotencyReservation> TryBeginAsync(IdempotencyPurpose purpose, string key, string fingerprint, TimeSpan ttl, CancellationToken ct);
Task<bool> CompleteAsync(IdempotencyPurpose purpose, string key, string token, string? response, TimeSpan retention, CancellationToken ct);
Task<bool> ReleaseAsync(IdempotencyPurpose purpose, string key, string token, CancellationToken ct);
```

- Entries are identified by **(tenant scope, purpose, key)**. The store resolves the tenant scope itself from the
  ambient `IRequestContextAccessor` via `IdempotencyTenantScope.Current(accessor)`; callers never pass a tenant.
- **Callers own lease and retention.** `ttl` is the in-flight lease, `retention` how long a completed entry is
  kept. Providers have no TTL settings: `IdempotencyBehaviorOptions.LeaseDuration`/`RetentionWindow` (05) and
  messaging's `IdempotencyOptions.LeaseDuration`/`ExpiryWindow` (07) supply them.
- Registration is keyed by purpose (`[FromKeyedServices(IdempotencyPurpose.Request)]`). A second registration for
  the same purpose throws. `AddSharedKernelApplication(…, app => app.WithIdempotency())` checks the Request store at host start
  (naming it when missing), and `MessagingBusBuilder.Build()` checks
  `HasIdempotencyStore(IdempotencyPurpose.Message)` at startup.

---

## Domain Invariants

**1 — Atomicity is the product.** `TryBeginAsync` classifies the entry's full state (started / in-flight /
completed-with-response / fingerprint-mismatch) in **one atomic round trip**. A read-then-write, `EXISTS`-then-`SET`
or `WATCH`/`MULTI` loop is a defect, however narrow the window looks.

- `.Redis`: each entry is one hash (`status`, `fingerprint`, `token`, `response`) at
  `sk:idempotency:{tenantScope}:key:{key}` (Request) or `sk:idempotency:{tenantScope}:msg:{key}` (Message).
  `TryBeginAsync`/`CompleteAsync`/`ReleaseAsync` are each a single Lua script. Fingerprint is compared before
  status, so a reused key with a different fingerprint is always `FingerprintMismatch`. A new reservation's hash
  expires after `ttl`; `CompleteAsync` extends it to `retention`.
- `.EfCore`: one table `idempotency_keys`, primary key `(tenant_scope, purpose, key)`. `TryBeginAsync` is one raw
  `INSERT … ON CONFLICT (tenant_scope, purpose, "key") DO UPDATE … RETURNING …` on the context's own ADO.NET
  connection (never `ExecuteSqlInterpolatedAsync`, which discards `RETURNING`). Every `SET` is a no-op unless the
  existing row has expired, so a live row returns unchanged. The caller won exactly when the returned
  `reservation_token` is its own — never compare timestamps (a coarse `IClock` can collide).

**2 — The token guards completion.** A winning `TryBeginAsync` returns a fresh per-reservation token.
`CompleteAsync`/`ReleaseAsync` mutate only while that token still owns an **`InProgress`** entry and return `bool`
— `false`, never an exception, otherwise. A late caller whose lease expired and was reclaimed cannot touch the new
owner's entry; a completed entry is never released. Stores hold no reservation state of their own.

**3 — A fault must not consume the key.** Callers complete only on success and release on a failed `Result` or an
exception. Even without a release, an unconfirmed reservation expires after `ttl`, so a crashed caller never
permanently wedges a key.

**4 — Tenant scoping is by construction.** The tenant scope is part of every key (Redis) or of the primary key
(EF Core), from `IdempotencyTenantScope`: the `TenantId` in "D" form, or the fixed `no-tenant` segment when there is
no context or no tenant — never a GUID, so it cannot collide with a real tenant. A caller cannot produce a
cross-tenant collision by choosing a key string. The inbound adapters (`UseSharedKernelRequestContext()`, the
MassTransit consume filter, the job runner) establish the context; a multi-tenant service that skips them shares
`no-tenant`.

**Caller scoping happens before the key reaches a store (P-562 X3).** For `IdempotencyPurpose.Request`,
`IdempotencyBehavior` (`SharedKernel.Application.Pipeline`) never passes the command's raw key: the store receives a
SHA-256 digest (64 lowercase hex characters) of the tenant, the caller (actor kind, subject, client, impersonator) and
the raw key, so one caller can never be replayed another caller's stored response. The store's own tenant partition
stays on top of it. The digest fits the EF Core `key` column and needs no escaping in the Redis key; the digest layout
is a stored format. A malformed or missing key is refused before the store with `ErrorCodes.Idempotency`
(`idempotency.key_required`, `idempotency.key_invalid`); an in-flight or reused key comes back as
`idempotency.in_progress` / `idempotency.key_reused`.

**5 — Fail closed by default.** An unreachable store throws. Each provider has one opt-out,
`AllowExecutionOnStoreUnavailable`, whose XML doc says **in capitals** that it increases duplicate-execution risk;
under it `TryBeginAsync` returns `Started` with a token never written and `CompleteAsync`/`ReleaseAsync` return
`false`, logging a Warning. Classification is narrow (connectivity/timeout only, never a blanket
`catch (Exception)`) and uniform across all three members. EF Core retry is off for this context: each call is one
statement and the caller owns retry.

**6 — Retention is bounded and visible.** Redis gets TTL for free. `.EfCore` carries `expires_at_utc` (indexed),
excludes expired rows, and ships cleanup as a **documented recipe** (a consumer `BackgroundService` or a
`19.Scheduling` job) — never a hidden background loop. `IdempotencyDbContext` is a plain `DbContext`, deliberately
not `SharedKernelDbContext` (soft-delete/concurrency conventions would defeat the hard `DELETE`).

**7 — Opaque responses.** `CompleteAsync` persists the caller's string exactly as given (`null` for messages).
Stores never inspect, reshape or re-serialize it.

---

## Technology

| Concern | Choice |
|---|---|
| Redis access | `02.Caching.Redis.Core`'s shared `IConnectionMultiplexer`; `AddRedisIdempotency` throws when `AddRedisConnection` was not called first. Never a private multiplexer |
| Relational access | `06.Persistence.EfCore`'s `UsePostgres(dataSource)` for the context options; raw `DbCommand` for the upsert |
| Clock | `IClock` for every `.EfCore` timestamp; `.Redis` uses relative TTLs only |
| Options | `RedisIdempotencyOptions` (`SharedKernel:Idempotency:Redis`) / `EfCoreIdempotencyOptions` (`SharedKernel:Idempotency:EfCore`) — only `AllowExecutionOnStoreUnavailable` |
| Lifetimes | Stores and `IdempotencyDbContext` scoped; the multiplexer stays a singleton. Both provider registrations `TryAdd` `IRequestContextAccessor` |
| Logging | `[LoggerMessage]`, EventIds `18000`–`18099` (`.Redis`, fail-open Warning `18000`), `18100`–`18199` (`.EfCore`, `18100`) |

---

## What Goes Where (within this domain)

| I need to add… | It belongs in… |
|---|---|
| A change to the reservation contract | `SharedKernel.Idempotency.Abstractions` — then update both providers, `FakeIdempotencyStore`, `IdempotencyBehavior` and the MassTransit filter together |
| A new purpose | `IdempotencyPurpose` + `IdempotencyPurposeSelection`; check the Redis key segment and the EF `purpose` column width (16) |
| A Redis-specific behavior | `SharedKernel.Idempotency.Redis` |
| A relational behavior | `SharedKernel.Idempotency.EfCore` |
| Code shared by both providers | Nowhere — duplicate it |
| A third backing store | A new sibling `SharedKernel.Idempotency.{Provider}` (Adapter tier) |
| A cleanup/expiry job | Not here — a documented consumer recipe or a `19.Scheduling` job |

---

## Test Rules

- Concurrency, tenant isolation, expiry reclaim and stale-token claims are proved against **real** Redis/PostgreSQL
  (Testcontainers, Integration lane). A fake is not evidence for an atomicity claim.
- Both providers cover all four statuses, foreign/stale tokens, release-after-complete, fail-open and fail-closed
  against an unreachable endpoint, and DI registration (duplicate purpose, missing Redis connection).

---

## Open Items

- **Persisted formats changed in P-568** (documented in each provider README): Redis message entries are hashes
  (old string values fail with `WRONGTYPE`); the EF table is keyed `(tenant_scope, purpose, key)` and
  `idempotency_messages` is gone. Message keys also gained the endpoint/consumer hash (`docs/refactor/MIGRATION.md`).
  Nothing was in production, so no data migration ships.
