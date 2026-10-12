# 18.Idempotency — Domain Brain

> The platform's **one idempotency contract and its production stores.** `IIdempotencyStore` (purpose-keyed,
> token-conditional reservations) backs every duplicate-execution guard in a service: the application pipeline's
> `IdempotencyBehavior` (`IdempotencyPurpose.Request`, `05.Application`) and MassTransit consumer idempotency
> (`IdempotencyPurpose.Message`, `07.Messaging`). Two sibling providers implement it — Redis (Lua) and PostgreSQL
> (EF Core). This domain does **not** own the callers, the HTTP `Idempotency-Key` header handling (`14.Presentation`),
> the caller-scoped key digest (`05.Application`), a cleanup job (a consumer recipe) or any caller-specific store
> interface.

## Packages

| Package | Tier | Purpose |
|---|---|---|
| `SharedKernel.Idempotency.Abstractions` | Abstractions | `IIdempotencyStore`, `IdempotencyReservation` / `IdempotencyReservationStatus`, `IdempotencyPurpose`, `IdempotencyPurposeSelection`, `IdempotencyTenantScope`, `IdempotencyStoreUnavailableException`, `IdempotencyPurposes`, the telemetry names
(`IdempotencyTelemetry`, `IdempotencyTagKeys`), registration helpers. References only `SharedKernel.Execution` and
`SharedKernel.Core` (the exception's base) |
| `SharedKernel.Idempotency.Redis` | Adapter (→ `Caching.Redis.Core`) | `RedisIdempotencyStore` (one class, every purpose) — each operation is one Lua script; `RedisIdempotencyMetrics` |
| `SharedKernel.Idempotency.EfCore` | Adapter (→ `Persistence.EfCore`) | `EfCoreIdempotencyStore` (one class, every purpose) over its own `IdempotencyDbContext`, table `idempotency_keys`; `EfCoreIdempotencyMetrics` |
| `SharedKernel.Idempotency.Testing` | Testing | `FakeIdempotencyStore` implementing the same protocol, with an outage switch |

## Public Entry Points

API detail and recipes: each package's `README.md`.

- `IIdempotencyStore` — `TryBeginAsync(purpose, key, fingerprint, ttl)` → `IdempotencyReservation` (`Started(token)`,
  `InProgress`, `Completed(storedResponse)`, `FingerprintMismatch`); `CompleteAsync(purpose, key, token, response,
  retention)` / `ReleaseAsync(purpose, key, token)` → `bool`.
- `IdempotencyPurpose` — `Request`, `Message`; selected with `IdempotencyPurposeSelection` (`ForRequests()`,
  `ForMessages()`, `For(purpose)`).
- `IdempotencyTenantScope` — `For(TenantId?)`, `Current(IRequestContextAccessor)`, `NoTenant`, `MaxLength`.
- `IdempotencyStoreUnavailableException` — what every member of a fail-closed store throws on an outage;
  `IdempotencyPurposes.ThrowIfUndefined(purpose)` — the one purpose guard; `IdempotencyTelemetry` /
  `IdempotencyTagKeys` — the names of the shared source, meter, spans, instruments and tags.
- `AddIdempotencyStore<TStore>(purpose…)` (keyed by purpose; a second store for the same purpose throws),
  `HasIdempotencyStore`, `GetRequiredIdempotencyStore`; resolve with `[FromKeyedServices(IdempotencyPurpose.Request)]`.
- **Redis** — `services.AddRedisConnection(configuration)` (02.Caching) first, then
  `services.AddRedisIdempotency(p => p.ForRequests().ForMessages(), o => …)`; throws when no Redis connection is
  registered.
- **EfCore** — `services.AddEfCoreIdempotency(db => db.UsePostgres(dataSource), p => p.ForRequests(), o => …)`; requires
  an `IClock`; registers `IdempotencyDbContext` and the store, both scoped. Ships no migrations (the README gives the
  design-time factory and cleanup `BackgroundService` recipes).
- Both providers `TryAdd` `IRequestContextAccessor`, call `AddMetrics()` and `TryAdd` their singleton
  `{Provider}IdempotencyMetrics` (the store's constructor takes it, so the type is public).
- `FakeIdempotencyStore.SimulateStoreUnavailable()` / `RestoreStore()` / `StoreUnavailable` — the fake's outage switch.

## Rules & Invariants

1. **Atomicity is the product.** `TryBeginAsync` classifies started / in-flight / completed / fingerprint-mismatch in
   one atomic round trip. Read-then-write, `EXISTS`-then-`SET` or `WATCH`/`MULTI` loops are defects.
2. **Redis layout:** one hash (`status`, `fingerprint`, `token`, `response`) at `sk:idempotency:{tenantScope}:{kind}:{key}`
   (kind `key` for Request, `msg` for Message); Begin/Complete/Release are each a single Lua script. Fingerprint is
   compared before status. A new reservation expires after `ttl`; `CompleteAsync` extends it to `retention`.
3. **EF Core layout:** table `idempotency_keys`, primary key `(tenant_scope, purpose, key)`, `expires_at_utc` indexed.
   `TryBeginAsync` is one raw `INSERT … ON CONFLICT … DO UPDATE … RETURNING` on the context's own ADO.NET connection —
   never `ExecuteSqlInterpolatedAsync` (it discards `RETURNING`). Every `SET` is a no-op unless the row expired. The
   caller won exactly when the returned `reservation_token` is its own — never compare timestamps.
4. **The token guards completion.** `CompleteAsync`/`ReleaseAsync` mutate only while the token still owns an
   `InProgress` entry and return `false` (never throw) otherwise. A completed entry is never released.
5. **A fault must not consume the key.** Callers complete only on success and release on failure; an unconfirmed
   reservation expires after `ttl` regardless.
6. **Tenant scoping by construction.** The store resolves the scope itself via `IdempotencyTenantScope.Current(accessor)`
   ("D" GUID, or `no-tenant`); callers never pass a tenant, and a key string cannot collide across tenants.
7. **Callers own lease and retention.** Providers have no TTL settings; `ttl`/`retention` come from
   `IdempotencyBehaviorOptions.LeaseDuration`/`RetentionWindow` (05) and `Messaging.Abstractions`'
   `IdempotencyOptions.LeaseDuration`/`ExpiryWindow` (07).
8. **Fail closed by default.** An unreachable store throws. The one opt-out, `AllowExecutionOnStoreUnavailable`, makes
   `TryBeginAsync` return `Started` with an unwritten token and Complete/Release return `false`, with a Warning log.
   Classify connectivity/timeout only — never a blanket `catch (Exception)`; keep all three members uniform.
9. **Opaque responses.** `CompleteAsync` stores the caller's string exactly as given (`null` for messages).
10. **Providers are siblings.** Neither references the other; no shared `.Core`. Duplicate small helpers. `.Redis`
    never references `06.Persistence`; `.EfCore` never references `02.Caching`.
11. **Never a private multiplexer** — `.Redis` uses the shared `IConnectionMultiplexer` from `Caching.Redis.Core`.
12. **`IdempotencyDbContext` is a plain `DbContext`**, not `SharedKernelDbContext` (soft-delete/concurrency conventions
    would defeat the hard `DELETE` cleanup). Timestamps come from `IClock`.
13. A contract change updates, together: both providers, `FakeIdempotencyStore`, `IdempotencyBehavior` and the
    MassTransit consumer behavior. A new purpose must fit the Redis key segment and the EF `purpose` column (16 chars).
14. **A fail-closed outage is a classified exception.** All three members throw `IdempotencyStoreUnavailableException`
    (a `SharedKernelException` in `.Abstractions`, `Error.Unavailable`, `ErrorCodes.Idempotency.StoreUnavailable`,
    fixed provider-neutral message, the raw provider exception as `InnerException`). Cancellation is never classified,
    wrapped or swallowed by fail-open (a pre-cancelled token throws before the call; Redis observes the token with
    `WaitAsync`). The Redis classifier wraps connectivity, timeout and transient server states (`LOADING`, `BUSY`,
    `READONLY`, `MASTERDOWN`, `CLUSTERDOWN`, `TRYAGAIN`, `OOM`) only; a Lua or `ERR` reply is a defect and propagates
    raw. A `TryBeginAsync` timeout may leave an unconfirmed reservation that expires after `ttl`, so a retry is safe.
    Sharpens rule 8.
15. **One source and one meter, `SharedKernel.Idempotency`** (`IdempotencyTelemetry`, constants in `.Abstractions`;
    instances per provider, distinguished by the `idempotency.provider` tag `redis`/`efcore`). Spans
    `idempotency begin|complete|release` (Client, tags operation/purpose/provider/result); histogram
    `sharedkernel.idempotency.operation.duration` (`s`; tags operation, purpose, provider, `outcome`, result) and
    counter `sharedkernel.idempotency.fail_open` (`{operation}`). `result` is `started`, `in_progress`, `completed`,
    `fingerprint_mismatch`, `applied`, `lost` or `fail_open`; a failure carries no `result`. Keys, fingerprints,
    tokens, tenants and responses are never a tag, log value or span attribute, and the span records a classified
    outage as a stackless `IdempotencyStoreUnavailableException` copy (the provider text names the key).
16. **One purpose guard.** `IdempotencyPurposes.ThrowIfUndefined` runs first in every provider and fake member; no
    provider validates the purpose lazily.

## Decisions

| Decision | Why |
|---|---|
| One purpose-keyed contract, not one per caller | Both guards need the same reservation protocol; do not reintroduce a request- or message-specific store interface |
| Stores resolve the tenant; callers don't pass it | Tenant isolation cannot be forgotten at a call site |
| Caller identity hashed by the caller (05), not the store | The store stays caller-agnostic; the Request key it sees is a 64-hex SHA-256 digest (a stored format) |
| Message key = `{MessageId:D}:` + SHA-256 hex of endpoint path and consumer type (built in 07) | One reservation per consumer of a message |
| No cleanup loop inside `.EfCore` | Retention is visible: a documented consumer `BackgroundService` or a `19.Scheduling` job |
| EF retry disabled for the idempotency context | Each call is one statement; the caller owns retry |
| A third backing store = new sibling `SharedKernel.Idempotency.{Provider}` | Keeps each provider swappable; it must implement Begin in one atomic round trip |
| The outage exception lives in `.Abstractions`, not the providers | It is the documented failure of the contract and the providers are siblings (rule 10); `.Abstractions` therefore references `SharedKernel.Core` as well as `SharedKernel.Execution` |
| One code, `idempotency.store_unavailable`, for connectivity and timeout; no `IdempotencyErrorCodes` class | The caller's remedy is the same (retry); `ErrorCodes.Idempotency` in Primitives is already the shared public home |
| A fail-open outage is counted and recorded on the span, not only logged | The `fail_open` counter is the alert signal for duplicate-execution risk |
| The metrics singleton is a public class (`RedisIdempotencyMetrics`, `EfCoreIdempotencyMetrics`) | The public store constructor takes it, and a per-scope instrument set would leak instruments on the shared meter; a public `MeterName` const is also what the observability scanner needs per assembly |
| The span source and meter are shared by both providers, not per provider | Rule 10 duplicates the helper, not the names; identical instrument definitions plus the `idempotency.provider` tag keep a host that registers both unambiguous (proven by `MixedProviderTelemetryTests`) |

## Logging

Block `18000`–`18999` (`LoggingEventIdRanges.Idempotency`).

| Sub-block | Package | Current EventIds |
|---|---|---|
| `18000`–`18099` | `.Redis` | `18000` fail-open Warning (`RedisIdempotencyLog`) |
| `18100`–`18199` | `.EfCore` | `18100` fail-open Warning (`EfCoreIdempotencyLog`) |

`.Abstractions` and the fake do not log. Each provider builds its id from
`LoggingEventIdRanges.Idempotency + n * PackageSubBlockWidth` in an internal `{Package}LoggingEventIds`, keeps its log
class under `Diagnostics/`, and uses one single-sentence template with quoted `'{Operation}'` and `'{Purpose}'`
(`begin`/`complete`/`release`, `request`/`message`); the fail-closed path logs nothing (the caller logs the refusal).

## Cross-Domain Couplings

- **References:** `01.Core` `SharedKernel.Execution` (`IRequestContextAccessor`, `TenantId`) and `SharedKernel.Primitives`
  (`IClock`, `LoggingEventIdRanges`); `02.Caching` `Caching.Redis.Core` (`.Redis`); `06.Persistence` `Persistence.EfCore`
  (`.EfCore`, `UsePostgres`).
- **Referenced by:** `05.Application` `Application.Pipeline` (`WithIdempotency()` checks the Request store at host start;
  refusal codes are `ErrorCodes.Idempotency` in `SharedKernel.Primitives`), `07.Messaging` `Messaging.MassTransit`
  (`MessagingBusBuilder.WithIdempotency()`; `Build()` checks `HasIdempotencyStore(IdempotencyPurpose.Message)`). Both
  callers reference only `.Abstractions`; a service picks a provider at its composition root.
- **Inbound from `01.Core`:** `ErrorCodes.Idempotency.StoreUnavailable`, `SharedKernelException`, and the observability
  helpers (`TelemetryVersion`, `ActivityFailureExtensions`, `WellKnownOutcomes`, `LoggingEventIdRanges`). **Outbound:**
  `05.Application` passes the outage exception's `Error` through unchanged; `07.Messaging` sees the new type from a
  fail-closed store and reviews its retry handling; `16.Testing` catalogues the fake's outage switch.

## Testing

- **Unit lane:** `Idempotency.Abstractions.Tests`, `Idempotency.Testing.Tests`.
- **Integration lane:** `Idempotency.Redis.Tests` (`RedisContainerFixture`), `Idempotency.EfCore.Tests`
  (`PostgreSqlContainerFixture`), both from `SharedKernel.Testing.Internal`.
- Concurrency, tenant isolation, expiry reclaim and stale-token claims are proved against real Redis/PostgreSQL; a fake
  is not evidence for an atomicity claim. Each provider covers all four statuses, foreign/stale tokens,
  release-after-complete, fail-open and fail-closed against an unreachable endpoint, and DI registration (duplicate
  purpose, missing Redis connection, a host without `AddMetrics()`).
- Telemetry, logs and outage classification are asserted through the `SharedKernel.Testing` capture helpers
  (`ActivityRecorder`, `MetricRecorder`, `LoggerAssertions`) against real Redis/PostgreSQL and unreachable endpoints; the
  telemetry and fail-open classes join the container collection so a recorder on the shared names sees one test at a
  time. Real server replies (`BUSY`, a wrong-type key) prove the Redis classifier. `MixedProviderTelemetryTests` runs
  Redis for Requests and EfCore for Messages in one host.
- Fakes: `SharedKernel.Idempotency.Testing` — catalogue in `src/Testing/CLAUDE.md`.

## Known Limitations

- `RedisIdempotencyOptions`/`EfCoreIdempotencyOptions` declare a `SectionName` constant but are not
  `ISectionBoundOptions` and are not bound from configuration; set `AllowExecutionOnStoreUnavailable` through the
  `configure` delegate.
- The exception passed to the fail-open Warning (L4) carries the provider's own text, and StackExchange.Redis names the
  command and key in it; sinks that print exceptions must be trusted like the store itself. Spans and metrics never carry
  it (rule 15).
- `.EfCore` ships no migrations and no cleanup job; expired rows accumulate until the consumer runs the README recipe.
- A multi-tenant service that skips the inbound adapters (`UseSharedKernelRequestContext()`, the MassTransit consume
  filter, the job runner) puts every entry in the `no-tenant` scope; anonymous callers of one tenant share a scope, so
  Request keys must be unguessable.
