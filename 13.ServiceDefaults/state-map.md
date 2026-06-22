# 13.ServiceDefaults — State Map

> **What this file is:** Phase and task tracker for all work within `13.ServiceDefaults`.
> **What it is not:** The root tracker — that lives at `state-map.md`.
> **Sync policy:** When all tasks under a Phase Key are `●`, run `/state-map-phase` with `phase_key: SK.13.{Phase}` to propagate that milestone to the root state-map.

---

## Legend

| Symbol | Meaning |
|--------|---------|
| `○` | Not started |
| `◐` | In progress |
| `●` | Complete |
| `⚑` | Blocked |
| `—` | N/A / Skipped |

---

## Phase Key Registry

> Phase keys are the sync bridge between this sub-state-map and the root `state-map.md`.
> Each key maps a local milestone to a root-level phase. When a key's Promotion Condition is met, the root is updated via `/state-map-phase`.

| Phase Key | Maps to Root Phase | Promotion Condition |
|-----------|-------------------|-------------------|
| `SK.13.Design` | Design | All tasks in Phase: Design are `●` |
| `SK.13.Scaffold` | Scaffold | All tasks in Phase: Scaffold are `●` |
| `SK.13.Core` | Core | All tasks in Phase: Core are `●` |
| `SK.13.Tests` | Tests | All tasks in Phase: Tests are `●` |
| `SK.13.Docs` | Docs | All tasks in Phase: Docs are `●` |
| `SK.13.Published` | Published | All tasks in Phase: Published are `●` |

---

## Active Work

_Nothing in progress._

<!--
Format when active — replace placeholder with table:
| Task | Phase Key | Package | State |
|------|-----------|---------|:-----:|
| Implement AddServiceDefaults() | SK.13.Core | SharedKernel.ServiceDefaults | ◐ |
-->

---

## Blocked

_Nothing blocked._

<!-- C-19 resolved 2026-06-22: 07.Messaging's P-172 (MessagingDiagnostics.ActivitySource) landed (SK.07.OTel 8/8 ●); WithMessagingTelemetry implemented and tested. -->
<!-- WO-028 (P-175/P-176/P-177) introduces no new cross-domain blockers — all three phases correct/extend already-landed 13.ServiceDefaults code or wrap already-shipped 06.Persistence primitives. -->

<!--
Format when blocked — replace placeholder with table:
| Task | Phase Key | Blocker |
|------|-----------|---------|
| Example blocked task | SK.13.Core | Waiting on upstream decision |
-->

---

## Package Board

| Package | Current Phase | State | Notes |
|---------|---------------|:-----:|-------|
| `SharedKernel.ServiceDefaults` | Core | `●` | Core C-01–C-28 all complete and tested (37/37 ServiceDefaults tests passing): `HealthCheckNames` constants class, `AzureServiceBusConnectionStringMarkers` literal promotion, `AddDatabaseReadinessCheck<TContext>`/`AddDapperDatabaseReadinessCheck` adapters wrapping `06.Persistence`'s readiness probes, and `WithMessagingTelemetry()` wiring `"MassTransit"`+`"SharedKernel.Messaging"` (07.Messaging's P-172) into `TracerProvider`/`MeterProvider`. `SK.13.Core` now 28/28 — all Core tasks complete |
| `SharedKernel.MultiTenancy` | Core | `●` | Core C-05–C-12 and WO-028's C-20–C-23/C-26 all complete and tested (26/26 MultiTenancy tests passing): `ITenantResolutionStrategy.StrategyName` explicit contract + `TenantResolutionStrategyNames` constants replace the prior type-name-reflection mapping; `TenantResolutionMiddleware` now uses a `FrozenDictionary` lookup computed once (no per-request allocation); `DatabaseTenantResolutionStrategy` fixed to use genuine async `ExecuteScalarAsync` with `CancellationToken` threaded through; `HeaderTenantResolutionStrategy.DefaultHeaderName` constant added |

---

## Cross-Domain Dependencies

| This Phase Key | Needs From Domain | What | Status |
|---------------|------------------|------|--------|
| `SK.13.Scaffold` | `01.Core` | `SharedKernel.Primitives` ProjectReference | Available |
| `SK.13.Core` | `02.Caching` | `SharedKernel.Caching.Abstractions` (`ICacheService`) + concrete `SharedKernel.Caching.Redis*` providers for health checks and telemetry | Available |
| `SK.13.Core` | `06.Persistence` | `SharedKernel.Persistence.Abstractions` (`IDbConnectionFactory`, `DatabaseReadinessResult`) + `SharedKernel.Persistence.EfCore` (`SharedKernelDbContext.CheckReadinessAsync`) | Available |
| `SK.13.Core` (C-23/C-24, `AddDatabaseReadinessCheck<TContext>`/`AddDapperDatabaseReadinessCheck`, P-177) | `06.Persistence` | `SharedKernel.Persistence.Abstractions.Diagnostics.DbConnectionFactoryDiagnosticsExtensions` + `SharedKernel.Persistence.EfCore.Diagnostics.DbContextDiagnosticsExtensions` (both confirmed on disk, P-150) | Available |
| `SK.13.Core` | `07.Messaging` | `SharedKernel.Messaging.Abstractions` + concrete `SharedKernel.Messaging.MassTransit` transport options for health checks and OTel wiring | Available |
| `SK.13.Core` | `12.Security` | `SharedKernel.Security.Abstractions` (`ITenantProvider`) + `SharedKernel.Security.Oidc` (`OidcTenantProvider`, delegated to by `ClaimTenantResolutionStrategy`) | Available |
| `SK.13.Core` (C-19, WithMessagingTelemetry) | `07.Messaging` | `SharedKernel.Messaging.MassTransit`'s static `ActivitySource("SharedKernel.Messaging", "1.0.0")` (P-172) — must exist before C-19 can wire it into the host `TracerProvider`; 13.ServiceDefaults never creates this source itself | Available — `07.Messaging`'s `SK.07.OTel` phase (OT-01–OT-08) is 8/8 `●`; `MessagingDiagnostics.ActivitySource` confirmed on disk. C-19 implemented by string-name wiring only (no new ProjectReference needed — `SharedKernel.Messaging.MassTransit` was already referenced for C-16's RabbitMQ health check) |

---

## Phase: Design <!-- phase-key: SK.13.Design -->

| ID | Task | Work Order | Package(s) | State |
|----|------|-----------|-----------|:-----:|
| D-01 | Tag taxonomy and `HealthStatus` calibration for caching health checks: `AddRedisHealthCheck` → `"ready"`+`"redis"`+`"cache"`, opt-in only; `AddCacheReadinessCheck` → `"ready"`+`"cache"`, reports `Degraded` (never `Unhealthy`) on probe failure since FusionCache L1 fail-safe may be serving correctly | WO-003 | SharedKernel.ServiceDefaults | `●` |
| D-02 | OTel meter-wiring contract design for `WithCachingTelemetry()`: confirm it only wires the pre-existing `"SharedKernel.Caching"` meter (`cache.hits`/`cache.misses`/`cache.errors` counters, `cache.operation.duration` histogram, owned and emitted by `02.Caching`) into the host `MeterProvider` — no new meter is created in this domain | WO-003 | SharedKernel.ServiceDefaults | `●` |

---

## Phase: Scaffold <!-- phase-key: SK.13.Scaffold -->

| ID | Task | Work Order | Package(s) | State |
|----|------|-----------|-----------|:-----:|
| S-01 | Add project references to `SharedKernel.ServiceDefaults.csproj`: `SharedKernel.Primitives` (01.Core), `SharedKernel.Caching.Abstractions` (02.Caching), `SharedKernel.Persistence.Abstractions` (06.Persistence), `SharedKernel.Messaging.Abstractions` (07.Messaging) | WO-027 | SharedKernel.ServiceDefaults | `●` |
| S-02 | Add package references to `SharedKernel.ServiceDefaults.csproj`: `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Instrumentation.AspNetCore`, `OpenTelemetry.Instrumentation.Http`, `OpenTelemetry.Instrumentation.EntityFrameworkCore`, `OpenTelemetry.Exporter.OpenTelemetryProtocol`, `Microsoft.Extensions.Diagnostics.HealthChecks`, `Microsoft.Extensions.Hosting` | WO-027 | SharedKernel.ServiceDefaults | `●` |
| S-03 | Create folder structure inside `SharedKernel.ServiceDefaults`: `Extensions/`, `HealthChecks/`, `Telemetry/`, `Probes/` (empty namespaces only, no implementation) | WO-027 | SharedKernel.ServiceDefaults | `●` |
| S-04 | Add project references to `SharedKernel.MultiTenancy.csproj`: `SharedKernel.Security.Abstractions` (12.Security), `SharedKernel.Persistence.Abstractions` (06.Persistence) | WO-027 | SharedKernel.MultiTenancy | `●` |
| S-05 | Add package reference to `SharedKernel.MultiTenancy.csproj`: `Microsoft.AspNetCore.Http.Abstractions` | WO-027 | SharedKernel.MultiTenancy | `●` |
| S-06 | Create folder structure inside `SharedKernel.MultiTenancy`: `Resolution/`, `Middleware/`, `Extensions/` (empty namespaces only, no implementation) | WO-027 | SharedKernel.MultiTenancy | `●` |
| S-07 | Create `SharedKernel.ServiceDefaults.Tests` project nested inside `13.ServiceDefaults/SharedKernel.ServiceDefaults/`, referencing `SharedKernel.Testing` (16.Testing) | WO-027 | SharedKernel.ServiceDefaults | `●` |
| S-08 | Create `SharedKernel.MultiTenancy.Tests` project nested inside `13.ServiceDefaults/SharedKernel.MultiTenancy/`, referencing `SharedKernel.Testing` (16.Testing) | WO-027 | SharedKernel.MultiTenancy | `●` |
| S-09 | Register all four new/updated projects (`SharedKernel.ServiceDefaults`, `SharedKernel.ServiceDefaults.Tests`, `SharedKernel.MultiTenancy`, `SharedKernel.MultiTenancy.Tests`) in `Platform.SharedKernel.slnx` under the `13.ServiceDefaults` solution folder | WO-027 | SharedKernel.ServiceDefaults, SharedKernel.MultiTenancy | `●` |
| S-10 | Verify `dotnet build` clean across both new project trees with zero implementation code | WO-027 | SharedKernel.ServiceDefaults, SharedKernel.MultiTenancy | `●` |

---

## Phase: Core <!-- phase-key: SK.13.Core -->

| ID | Task | Work Order | Package(s) | State |
|----|------|-----------|-----------|:-----:|
| C-01 | Implement `AddServiceDefaults(this IHostApplicationBuilder)` — wires OTLP-exporting tracing/metrics/logging via `AddSharedKernelTelemetry` and registers base health check infrastructure via `AddSharedKernelHealthChecks`; documented as the mandatory first `Program.cs` call | WO-027 | SharedKernel.ServiceDefaults | `●` |
| C-02 | Implement `AddSharedKernelTelemetry(this IHostApplicationBuilder, string serviceName)` — `ResourceBuilder` with service name + assembly version; ASP.NET Core/HttpClient/EF Core (conditional) instrumentation into `TracerProvider`; runtime + ASP.NET Core instrumentation into `MeterProvider`; OTLP exporter endpoint from standard `OTEL_EXPORTER_OTLP_ENDPOINT`/`_PROTOCOL` env vars; exposed independently of `AddServiceDefaults()` | WO-027 | SharedKernel.ServiceDefaults | `●` |
| C-03 | Implement `AddSharedKernelHealthChecks(this IServiceCollection)` plus `/health/live` and `/health/ready` endpoint mappings with the hard tag split (`"live"` = process-alive only, never dependency-coupled; `"ready"` = gates load-balancer routing, may depend on DB/cache/broker) | WO-027 | SharedKernel.ServiceDefaults | `●` |
| C-04 | Implement `StartupGate` (sealed, singleton, `volatile`-backed `IsReady`/idempotent `MarkReady()`) and `StartupGateHealthCheck` (tagged `"ready"`, reports `Unhealthy` before `MarkReady()`/`Healthy` after); register automatically and unconditionally inside `AddServiceDefaults()` | WO-027 | SharedKernel.ServiceDefaults | `●` |
| C-05 | Implement `ITenantResolutionStrategy` (`TryResolveAsync(HttpContext, CancellationToken) → Task<Guid?>`, null = "not applicable, try next" — never throws for an absent signal) | WO-027 | SharedKernel.MultiTenancy | `●` |
| C-06 | Implement `HeaderTenantResolutionStrategy` (configurable header name, default `X-Tenant-Id`; `Guid.TryParse`; absent/malformed → `null`) | WO-027 | SharedKernel.MultiTenancy | `●` |
| C-07 | Implement `ClaimTenantResolutionStrategy` as a thin delegating adapter to `SharedKernel.Security.Oidc.OidcTenantProvider` — zero reimplemented claim-parsing logic | WO-027 | SharedKernel.MultiTenancy | `●` |
| C-08 | Implement `DatabaseTenantResolutionStrategy` — parameterized tenant-directory lookup via `IDbConnectionFactory`, keyed by request host/subdomain; no string-built SQL | WO-027 | SharedKernel.MultiTenancy | `●` |
| C-09 | Implement `TenantResolutionOptions` (Options-pattern POCO, section `"SharedKernel:MultiTenancy"`, default `StrategyOrder = ["Header", "Claim", "Database"]`) | WO-027 | SharedKernel.MultiTenancy | `●` |
| C-10 | Implement `AmbientTenantProvider` (scoped `ITenantProvider`, `TenantId` defaults to `Guid.Empty`, private setter) | WO-027 | SharedKernel.MultiTenancy | `●` |
| C-11 | Implement `TenantResolutionMiddleware` — runs configured strategy order, sets `AmbientTenantProvider.TenantId` from first non-null result, zero-resolving leaves `Guid.Empty` without throwing; documented as required to run after `UseAuthentication()` | WO-027 | SharedKernel.MultiTenancy | `●` |
| C-12 | Implement `AddSharedKernelMultiTenancy(IServiceCollection, Action<TenantResolutionOptions>?)` — registers `TenantResolutionOptions`, `AmbientTenantProvider` as scoped `ITenantProvider`, and the configured `ITenantResolutionStrategy` set | WO-027 | SharedKernel.MultiTenancy | `●` |
| C-13 | Implement `AddRedisHealthCheck(this IHealthChecksBuilder, string connectionString)` — wraps `AspNetCore.HealthChecks.Redis`; tagged `"ready"`, `"redis"`, `"cache"`; opt-in only, never registered by `AddServiceDefaults()`/`AddSharedKernelHealthChecks()` | WO-003 | SharedKernel.ServiceDefaults | `●` |
| C-14 | Implement `AddCacheReadinessCheck(this IHealthChecksBuilder)` — probes `ICacheService.GetAsync<string>` (`SharedKernel.Caching.Abstractions`) with synthetic key + short timeout; reports `Degraded` (never `Unhealthy`) on failure; tagged `"ready"`, `"cache"` | WO-003 | SharedKernel.ServiceDefaults | `●` |
| C-15 | Implement `WithCachingTelemetry(this IHostApplicationBuilder)` — wires the pre-existing `"SharedKernel.Caching"` meter (owned by `02.Caching`) into the host `MeterProvider`; idempotent across repeated calls | WO-003 | SharedKernel.ServiceDefaults | `●` |
| C-16 | Implement `AddRabbitMqMessagingHealthCheck(this IHealthChecksBuilder, string amqpUri)` — wraps `AspNetCore.HealthChecks.RabbitMQ`; verifies a real AMQP connection (not URI parsing only); tagged `"ready"`, `"messaging"`; connection details sourced from resolved `RabbitMqBusOptions` (07.Messaging.MassTransit), no duplicated connection strings | WO-020 | SharedKernel.ServiceDefaults | `●` |
| C-17 | Implement `AddAzureServiceBusMessagingHealthCheck(this IHealthChecksBuilder, string connectionStringOrNamespace)` — wraps `AspNetCore.HealthChecks.AzureServiceBus`; uses `DefaultAzureCredential` when a fully-qualified namespace (no connection string) is passed; tagged `"ready"`, `"messaging"` | WO-020 | SharedKernel.ServiceDefaults | `●` |
| C-18 | Verify messaging health checks (C-16, C-17) surface only on `/health/ready`, never `/health/live` — confirm against the C-03 endpoint mapping's tag filter | WO-020 | SharedKernel.ServiceDefaults | `●` |
| C-19 | Implement `WithMessagingTelemetry(this IHostApplicationBuilder)` — wires `"MassTransit"` and the pre-existing `"SharedKernel.Messaging"` `ActivitySource` (owned by `07.Messaging.MassTransit`, defined per P-172) into `TracerProvider` via `WithTracing(t => t.AddSource(...))`; wires `"MassTransit"` meter into `MeterProvider`; idempotent across repeated calls; creates no new `ActivitySource`/meter itself | WO-021 | SharedKernel.ServiceDefaults | `●` |
| C-20 | Add `StrategyName` member to `ITenantResolutionStrategy` (explicit-contract property, e.g. `string StrategyName { get; }`) — each strategy declares its own resolution-order key; remove the type-name-reflection mapping (`s.GetType().Name`) from `TenantResolutionMiddleware.InvokeAsync` entirely | WO-028 | SharedKernel.MultiTenancy | `●` |
| C-21 | Add `TenantResolutionStrategyNames` constants class (mirrors `HealthCheckTags` pattern) holding `Header`/`Claim`/`Database` literals; `HeaderTenantResolutionStrategy`, `ClaimTenantResolutionStrategy`, `DatabaseTenantResolutionStrategy` each set `StrategyName` from these constants; `TenantResolutionOptions.StrategyOrder`'s default array references the same constants — zero duplicated bare string literals across strategies/options/tests | WO-028 | SharedKernel.MultiTenancy | `●` |
| C-22 | Refactor `TenantResolutionMiddleware` to compute the `StrategyName → ITenantResolutionStrategy` lookup once (e.g. a `FrozenDictionary`/`Dictionary` built once per DI scope resolution or cached against the fixed strategy set, not rebuilt via a per-request switch expression); `InvokeAsync` iterates `TenantResolutionOptions.StrategyOrder` against that already-available mapping — no fresh `Dictionary` allocation tied to the strategy set on every request | WO-028 | SharedKernel.MultiTenancy | `●` |
| C-23 | Fix `DatabaseTenantResolutionStrategy.TryResolveAsync` — replace the synchronous `IDbCommand.ExecuteScalar()` call with the async ADO.NET path (`DbCommand.ExecuteScalarAsync(CancellationToken)` or equivalent async surface from `IDbConnectionFactory`/`IDbConnection`); thread the supplied `CancellationToken` into the actual database call; preserve the existing parameterized `@host` query exactly as-is | WO-028 | SharedKernel.MultiTenancy | `●` |
| C-24 | Add `HealthCheckNames` constants class (mirrors `HealthCheckTags` pattern) holding `Redis`/`RabbitMq`/`AzureServiceBus`/`Cache`/`Startup` default health-check registration-name literals; update `RedisHealthCheckExtensions`, `RabbitMqMessagingHealthCheckExtensions`, `AzureServiceBusMessagingHealthCheckExtensions`, `CacheReadinessHealthCheckExtensions`, and the inline `"startup"` literal in `HealthCheckExtensions.AddSharedKernelHealthChecks` to reference the new constants instead of bare string defaults | WO-028 | SharedKernel.ServiceDefaults | `●` |
| C-25 | Promote `AzureServiceBusHealthCheck.LooksLikeConnectionString`'s `"Endpoint="`/`"SharedAccessKey"` substring literals to named constants with names that document what they detect (e.g. `AzureServiceBusConnectionStringMarkers.Endpoint`/`.SharedAccessKey`, or folded into `HealthCheckNames` — implementer's call on exact holder) | WO-028 | SharedKernel.ServiceDefaults | `●` |
| C-26 | Promote `HeaderTenantResolutionStrategy`'s default `"X-Tenant-Id"` header-name literal to a named constant in `SharedKernel.MultiTenancy` (alongside `TenantResolutionStrategyNames` from C-21, or its own dedicated holder) | WO-028 | SharedKernel.MultiTenancy | `●` |
| C-27 | Implement `AddDatabaseReadinessCheck<TContext>(this IHealthChecksBuilder, string name = HealthCheckNames.Database) where TContext : SharedKernelDbContext` — wraps `06.Persistence.EfCore`'s `SharedKernelDbContext.CheckReadinessAsync` (P-150) in an `IHealthCheck`; surfaces `DatabaseReadinessResult.IsHealthy`/`Latency`/`Provider` via `HealthCheckResult.Data`; reports `Unhealthy` when `IsHealthy == false`; tagged `HealthCheckTags.Ready` + `HealthCheckTags.Db` — exactly per the existing `CLAUDE.md` Interface Contracts entry | WO-028 | SharedKernel.ServiceDefaults | `●` |
| C-28 | Implement `AddDapperDatabaseReadinessCheck(this IHealthChecksBuilder, string name = HealthCheckNames.Database)` — wraps `06.Persistence.Abstractions`'s `IDbConnectionFactory`-based readiness extension (`DbConnectionFactoryDiagnosticsExtensions`, P-150); same `HealthCheckResult.Data` surfacing and tag placement as C-27 | WO-028 | SharedKernel.ServiceDefaults | `●` |

---

## Phase: Tests <!-- phase-key: SK.13.Tests -->

| ID | Task | Work Order | Package(s) | State |
|----|------|-----------|-----------|:-----:|
| T-01 | `/health/live` reports Healthy even when a deliberately-failing `"ready"`-tagged check is registered | WO-027 | SharedKernel.ServiceDefaults | `●` |
| T-02 | `/health/ready` aggregates all `"ready"`-tagged checks independently of `"live"`-tagged ones | WO-027 | SharedKernel.ServiceDefaults | `●` |
| T-03 | `StartupGate.IsReady` defaults `false`; `MarkReady()` is idempotent (repeated calls do not throw or toggle state back) | WO-027 | SharedKernel.ServiceDefaults | `●` |
| T-04 | `StartupGateHealthCheck` reports `Unhealthy` before `MarkReady()`, `Healthy` after | WO-027 | SharedKernel.ServiceDefaults | `●` |
| T-05 | `HeaderTenantResolutionStrategy`: present+parseable header → resolved `Guid`; absent → `null`; malformed → `null` (never throws) | WO-027 | SharedKernel.MultiTenancy | `●` |
| T-06 | `ClaimTenantResolutionStrategy` delegates to a fake/mocked `OidcTenantProvider`-shaped dependency; never re-parses raw claims itself | WO-027 | SharedKernel.MultiTenancy | `●` |
| T-07 | `DatabaseTenantResolutionStrategy` resolves known host/subdomain via test-double `IDbConnectionFactory`; unknown host → `null`; query parameterization verified (no string-built SQL in executed command text) | WO-027 | SharedKernel.MultiTenancy | `●` |
| T-08 | `TenantResolutionOptions.StrategyOrder` default `["Header", "Claim", "Database"]`; first non-null strategy wins; omitted strategy name is never invoked | WO-027 | SharedKernel.MultiTenancy | `●` |
| T-09 | `TenantResolutionMiddleware`: sets `TenantId` from first resolving strategy; zero-resolving leaves `Guid.Empty` without throwing | WO-027 | SharedKernel.MultiTenancy | `●` |
| T-10 | `AmbientTenantProvider`: defaults `Guid.Empty`; `TenantId` setter is `private` (reflection-verified) | WO-027 | SharedKernel.MultiTenancy | `●` |
| T-11 | Every dependency-specific health check (`Redis`, `RabbitMQ`, `AzureServiceBus`, `Cache`) registers with tag `"ready"` and never `"live"` — assert against `HealthCheckRegistration.Tags` | WO-003, WO-020 | SharedKernel.ServiceDefaults | `●` |
| T-12 | `AddCacheReadinessCheck`: forced cache-probe failure reports `HealthStatus.Degraded`, never `HealthStatus.Unhealthy` | WO-003 | SharedKernel.ServiceDefaults | `●` |
| T-13 | `WithMessagingTelemetry()` / `WithCachingTelemetry()`: calling each twice registers exactly one instance of each `ActivitySource`/meter name | WO-021, WO-003 | SharedKernel.ServiceDefaults | `●` (full coverage: `WithCachingTelemetry` half pre-existing, `WithMessagingTelemetry` half added in `MessagingTelemetryExtensionsTests.cs` once C-19 unblocked) |
| T-14 | Messaging health checks (`AddRabbitMqMessagingHealthCheck`, `AddAzureServiceBusMessagingHealthCheck`) appear only on `/health/ready`, never `/health/live` | WO-020 | SharedKernel.ServiceDefaults | `●` |
| T-15 | A custom, non-platform `ITenantResolutionStrategy` (a type not named `Header`/`Claim`/`Database`-anything) is correctly invoked when its declared `StrategyName` appears in `StrategyOrder` — proves the new explicit-contract mechanism (C-20/C-21) replaces type-name reflection, not just relocates it | WO-028 | SharedKernel.MultiTenancy | `●` |
| T-16 | Rewrite `InvokeAsync_StrategyOmittedFromOrder_IsNeverInvoked` — uses a real, named, registered strategy (not a structurally-unreachable test double) proven skipped specifically because it is absent from `StrategyOrder`; replaces the prior false-confidence version | WO-028 | SharedKernel.MultiTenancy | `●` |
| T-17 | Strategy name→instance lookup is not rebuilt as a fresh allocation on every `InvokeAsync` call — test or benchmark demonstrating no per-request `Dictionary` allocation tied to the strategy set itself (C-22) | WO-028 | SharedKernel.MultiTenancy | `●` |
| T-18 | All existing 23 `SharedKernel.MultiTenancy` tests continue passing after the C-20/C-21/C-22 refactor — net new tests added, none removed without a strictly-stronger replacement | WO-028 | SharedKernel.MultiTenancy | `●` |
| T-19 | `DatabaseTenantResolutionStrategy.TryResolveAsync` uses an asynchronous database call (`ExecuteScalarAsync` or equivalent) — zero synchronous, thread-blocking ADO.NET calls remain; existing parameterized-query assertions (known host resolves, unknown host → `null`, no string-built SQL) continue passing unchanged in intent | WO-028 | SharedKernel.MultiTenancy | `●` |
| T-20 | A cancelled `CancellationToken` actually cancels the in-flight `DatabaseTenantResolutionStrategy` database call rather than being silently ignored | WO-028 | SharedKernel.MultiTenancy | `●` |
| T-21 | `AddDatabaseReadinessCheck<TContext>` / `AddDapperDatabaseReadinessCheck`: correct tag placement (`"ready"` + `"db"`, never `"live"`) and correct `Healthy`/`Unhealthy` mapping from `DatabaseReadinessResult.IsHealthy` | WO-028 | SharedKernel.ServiceDefaults | `●` |
| T-22 | Full existing 18 `SharedKernel.ServiceDefaults` test suite continues passing after the C-24–C-28 magic-string cleanup and new adapter additions | WO-028 | SharedKernel.ServiceDefaults | `●` |

---

## Phase: Docs <!-- phase-key: SK.13.Docs -->

| ID | Task | Work Order | Package(s) | State |
|----|------|-----------|-----------|:-----:|
| DO-01 | XML doc comments on all public extension methods and types delivered in Core (C-01 through C-19) | WO-027, WO-003, WO-020, WO-021 | SharedKernel.ServiceDefaults, SharedKernel.MultiTenancy | `●` |
| DO-02 | README `Program.cs` composition snippet covering `AddServiceDefaults()`, opt-in health checks, `WithMessagingTelemetry()`/`WithCachingTelemetry()`, and `AddSharedKernelMultiTenancy()` + middleware ordering | WO-027 | SharedKernel.ServiceDefaults, SharedKernel.MultiTenancy | `●` |

---

## Phase: Published <!-- phase-key: SK.13.Published -->

| ID | Task | Work Order | Package(s) | State |
|----|------|-----------|-----------|:-----:|
| P-01 | NuGet packaging metadata, pack, and publish `SharedKernel.ServiceDefaults` | WO-027 | SharedKernel.ServiceDefaults | `●` |
| P-02 | NuGet packaging metadata, pack, and publish `SharedKernel.MultiTenancy` | WO-027 | SharedKernel.MultiTenancy | `●` |
| P-03 | Consumer verification — a sample `Program.cs` composition resolves both packages end-to-end | WO-027 | SharedKernel.ServiceDefaults, SharedKernel.MultiTenancy | `●` |

---

## Overall Progress

> Counts updated whenever a task state changes.

| Phase Key | Phase | Total | ● Done | ○ Pending | State |
|-----------|-------|:-----:|:------:|:---------:|:-----:|
| `SK.13.Design` | Design | 2 | 2 | 0 | `●` |
| `SK.13.Scaffold` | Scaffold | 10 | 10 | 0 | `●` |
| `SK.13.Core` | Core | 28 | 28 | 0 | `●` |
| `SK.13.Tests` | Tests | 22 | 22 | 0 | `●` |
| `SK.13.Docs` | Docs | 2 | 2 | 0 | `●` |
| `SK.13.Published` | Published | 3 | 3 | 0 | `●` |

---

## Changelog

> One line per session. Format: `[YYYY-MM-DD] {what changed} — {trigger}`.

- [2026-06-19] Sub state-map initialized — phase key registry and 6 phases scaffolded at `○`, no tasks yet; Package Board lists `SharedKernel.ServiceDefaults` and `SharedKernel.MultiTenancy` as not started; Cross-Domain Dependencies pre-populated against already-available abstractions/providers in `01`/`02`/`06`/`07`/`12`; no phases promoted to root — root backlog items P-010 (WO-003), P-122 (WO-020), and P-132 (WO-021) remain pending formal Design-phase task breakdown (arch-lead)
- [2026-06-19] Full task breakdown landed for all 6 phases across 6 root backlog items: P-169 (WO-027, Scaffold — S-01→S-10: project/package references, folder structure, nested .Tests projects, .slnx registration for both packages); P-170 (WO-027, Core foundation — C-01→C-04: AddServiceDefaults, AddSharedKernelTelemetry, AddSharedKernelHealthChecks with the live/ready tag split, StartupGate/StartupGateHealthCheck); P-171 (WO-027, Core — C-05→C-12: ITenantResolutionStrategy + 3 concrete strategies, TenantResolutionOptions, AmbientTenantProvider, TenantResolutionMiddleware, AddSharedKernelMultiTenancy); P-010 (WO-003, Design D-01/D-02 + Core C-13→C-15: AddRedisHealthCheck, AddCacheReadinessCheck with Degraded calibration, WithCachingTelemetry); P-122 (WO-020, Core C-16→C-18: AddRabbitMqMessagingHealthCheck, AddAzureServiceBusMessagingHealthCheck, readiness-only placement verification); P-132 (WO-021, Core C-19: WithMessagingTelemetry wiring the pre-existing "MassTransit" and "SharedKernel.Messaging" sources — the latter gated on 07.Messaging's P-172 landing first, confirmed still `○` as of this pass and logged as a Pending cross-domain dependency). 14 Tests-phase rows and 2 Docs-phase rows added covering all Core deliverables. Overall Progress counts updated: Design 2, Scaffold 10, Core 19, Tests 14, Docs 2, Published 3 — all phases `○`. Package Board updated to reflect Scaffold as the current phase for both packages. No layering, tag-calibration, or ownership violations found in any of the 6 inputs — P-132's instruction to NOT redesign the ActivitySource itself (that is 07.Messaging's P-172 scope) honored by scoping C-19 to wiring only (servicedefaults-arch-planner)
- [2026-06-19] D-01 → `●`, D-02 → `●` in SK.13.Design — tag taxonomy/calibration and OTel meter-wiring contract confirmed against ground-truth source; cache.operation.duration corrected to cache.factory.duration (state-map-phase)
- [2026-06-19] C-01 → C-18 → `●` in SK.13.Core; C-19 → `⚑` — `AddServiceDefaults`, `AddSharedKernelTelemetry`, `AddSharedKernelHealthChecks` with the live/ready endpoint split, `StartupGate`/`StartupGateHealthCheck`, full `SharedKernel.MultiTenancy` surface (`ITenantResolutionStrategy` + Header/Claim/Database strategies, `TenantResolutionOptions`, `AmbientTenantProvider`, `TenantResolutionMiddleware`, `AddSharedKernelMultiTenancy`), `AddRedisHealthCheck`, `AddCacheReadinessCheck` (Degraded calibration), `WithCachingTelemetry`, `AddRabbitMqMessagingHealthCheck`, `AddAzureServiceBusMessagingHealthCheck`, readiness-only placement verified; 18 ServiceDefaults + 23 MultiTenancy tests passing. C-19 (`WithMessagingTelemetry`) left `⚑` blocked — `07.Messaging`'s P-172 `ActivitySource` confirmed still `○` (servicedefaults-phase-implementer)
- [2026-06-19] S-01 → S-10 → `●` in SK.13.Scaffold — real project/package references landed for both `SharedKernel.ServiceDefaults.csproj` (Primitives, Caching.Abstractions, Persistence.Abstractions, Messaging.Abstractions + OpenTelemetry/HealthChecks/Hosting packages) and `SharedKernel.MultiTenancy.csproj` (Security.Abstractions, Persistence.Abstractions + Http.Abstractions); `Extensions/HealthChecks/Telemetry/Probes` and `Resolution/Middleware/Extensions` folder structures created; `SharedKernel.ServiceDefaults.Tests` and `SharedKernel.MultiTenancy.Tests` nested test projects created referencing `SharedKernel.Testing`; all four projects registered in `Platform.SharedKernel.slnx` under the `13.ServiceDefaults` solution folder; `dotnet build` verified clean (0 errors) across `SharedKernel.ServiceDefaults`, `SharedKernel.MultiTenancy`, and both `.Tests` projects. Overall Progress: SK.13.Scaffold 10/10 `●`. Package Board updated — both packages now at Core, awaiting C-01 onward (servicedefaults-phase-implementer, resumed after a session-limit interruption mid-report; all task work and state-map edits had already landed before the interruption — this entry closes out the remaining changelog/Package-Board bookkeeping)
- [2026-06-19] WO-028 gold-standard-audit remediation: 9 new Core tasks (C-20–C-28) and 8 new Tests tasks (T-15–T-22) added across three root inputs. P-175 (C-20–C-22, T-15–T-18): replaces `TenantResolutionMiddleware`'s `s.GetType().Name`-reflection strategy-mapping with an explicit `ITenantResolutionStrategy.StrategyName` contract member backed by a new `TenantResolutionStrategyNames` constants class, and replaces the per-request fresh-`Dictionary` allocation with a lookup computed once against the fixed DI-registered strategy set; also rewrites the previously-false-confidence `InvokeAsync_StrategyOmittedFromOrder_IsNeverInvoked` test. This corrects a known limitation this file's own Implementation Rules section had documented as acceptable (line previously at CLAUDE.md:267) — re-classified as a defect, not a permanent constraint, per WO-028's audit finding. P-176 (C-23, T-19–T-20): fixes `DatabaseTenantResolutionStrategy.TryResolveAsync`'s sync-over-async defect (`IDbCommand.ExecuteScalar()` blocking a thread-pool thread despite an `async`/`CancellationToken` signature) — replaced with the async ADO.NET path, `CancellationToken` actually threaded through; parameterized-query shape preserved exactly. P-177 (C-24–C-28, T-21–T-22): adds `HealthCheckNames` constants class consolidating five bare-literal health-check name defaults (mirroring the already-correct `HealthCheckTags` pattern), promotes `AzureServiceBusHealthCheck`'s connection-string-detection substrings and `HeaderTenantResolutionStrategy`'s default header name to named constants, and — closing a real implementation gap, not just a style fix — implements `AddDatabaseReadinessCheck<TContext>` and `AddDapperDatabaseReadinessCheck`, both already specified in this file's Interface Contracts section since the domain brain was written (P-150) but never implemented or tracked as Core tasks until now. Overall Progress: SK.13.Core 28 total (18 done, 10 pending, still `⚑` pending C-19's external 07.Messaging gate); SK.13.Tests 22 total (0 done, 22 pending). Package Board: `SharedKernel.MultiTenancy` reopened from `●` to `◐` to reflect C-20–C-23/C-26 reopening already-shipped code for correction; `SharedKernel.ServiceDefaults` Notes updated with the new adapter gap-fill. No layering, tag-calibration, or ownership violations found in any of the three inputs — all three correct or extend already-landed/already-documented surface within existing package boundaries; the two new health check adapters wrap only already-shipped `06.Persistence` probe primitives (P-150), consistent with the standing "06.Persistence ships the probe, 13.ServiceDefaults ships the IHealthCheck adapter" rule (servicedefaults-arch-planner, WO-028)
- [2026-06-22] C-20→C-28 → `●` in SK.13.Core; T-15→T-22 → `●` in SK.13.Tests — resumed after a session-limit interruption mid-implementation; verified all WO-028 code (StrategyName contract, TenantResolutionStrategyNames, FrozenDictionary cached lookup, async ExecuteScalarAsync fix, HealthCheckNames, AzureServiceBusConnectionStringMarkers, HeaderTenantResolutionStrategy.DefaultHeaderName, AddDatabaseReadinessCheck<TContext>, AddDapperDatabaseReadinessCheck) and its T-15/T-16/T-19/T-20/T-21 test coverage had already landed in source before the cutoff; ran both suites clean (29/29 ServiceDefaults, 26/26 MultiTenancy) and closed the bookkeeping. SK.13.Core now 27/28 done (`⚑`, only C-19 outstanding — still externally blocked on 07.Messaging's P-172, left untouched). SK.13.Tests now 8/22 done (`◐` — T-01–T-14 predate WO-028 and remain outside this resumption's scope). Package Board: `SharedKernel.MultiTenancy` promoted back to `●` (WO-028 corrections fully landed and tested); `SharedKernel.ServiceDefaults` Notes updated to reflect C-24–C-28 closure (servicedefaults-phase-implementer)
- [2026-06-22] T-01→T-14 → `●` in SK.13.Tests — closed the pre-WO-028 test backlog. T-01/T-03/T-04/T-05/T-06/T-07/T-09/T-10/T-12 found already covered by pre-existing tests (StartupGateTests, StartupGateHealthCheckTests, HeaderTenantResolutionStrategyTests, ClaimTenantResolutionStrategyTests, DatabaseTenantResolutionStrategyTests, TenantResolutionMiddlewareTests, AmbientTenantProviderTests, CacheReadinessHealthCheckTests, HealthCheckEndpointTests); T-08 covered by TenantResolutionOptionsTests + TenantResolutionMiddlewareTests; T-13 covered for the WithCachingTelemetry half only (WithMessagingTelemetry intentionally untested — still gated on C-19/07.Messaging P-172). T-02/T-11/T-14 newly added this session in SharedKernel.ServiceDefaults.Tests (HealthCheckEndpointTests.cs, HealthCheckTagTests.cs): HealthReady_AggregatesReadyTaggedChecks_IndependentlyOfLiveTaggedChecks, AddDatabaseReadinessCheck_RegistersWithReadyDbTags_NeverLive, AddDapperDatabaseReadinessCheck_RegistersWithReadyDbTags_NeverLive, HealthLive_NeverEvaluatesMessagingReadinessChecks, HealthLive_NeverEvaluatesRealRabbitMqOrAzureServiceBusHealthCheckRegistrations. SK.13.Tests now 22/22 `●` — all 22 tasks complete, promoting to root. Test results: 34/34 SharedKernel.ServiceDefaults.Tests passing (+5 new), 26/26 SharedKernel.MultiTenancy.Tests passing (unchanged) (servicedefaults-phase-implementer)
- [2026-06-22] C-19 → `●` in SK.13.Core — unblocked: `07.Messaging`'s P-172 (`MessagingDiagnostics.ActivitySource("SharedKernel.Messaging", "1.0.0")`, internal to `SharedKernel.Messaging.MassTransit`) confirmed landed (`SK.07.OTel` 8/8 `●`). Implemented `WithMessagingTelemetry(this IHostApplicationBuilder)` in `Telemetry/MessagingTelemetryExtensions.cs`, mirroring the existing `WithCachingTelemetry` shape exactly: `WithTracing(t => t.AddSource("MassTransit").AddSource("SharedKernel.Messaging"))` + `WithMetrics(m => m.AddMeter("MassTransit"))`, both registered by string name only — no new `ProjectReference` needed (`SharedKernel.ServiceDefaults.csproj` already references `SharedKernel.Messaging.MassTransit`, added earlier for C-16's RabbitMQ health check) and no direct reference to the internal `MessagingDiagnostics` type. `SK.13.Core` now 28/28 `●` — promoted to root via `/state-map-phase`. Closed the documented T-13 test gap: added `MessagingTelemetryExtensionsTests.cs` (3 tests covering `WithTracing`/`WithMetrics` double-call idempotency and no-throw) alongside the pre-existing `CachingTelemetryExtensionsTests.cs`, giving T-13 full two-method coverage. Blocked section cleared; Cross-Domain Dependencies C-19 row updated Blocked → Available. Test results: 37/37 SharedKernel.ServiceDefaults.Tests passing (+3 new), 26/26 SharedKernel.MultiTenancy.Tests passing (unchanged) (servicedefaults-phase-implementer)
- [2026-06-22] DO-01 → `●`, DO-02 → `●` in SK.13.Docs — XML doc audit across both packages' full public surface found one gap (`AzureServiceBusHealthCheck`'s public constructor lacked a doc comment); fixed. Confirmed `GenerateDocumentationFile=true` already set in both `.csproj` files; clean Release build for both packages produced 0 CS1591 (missing-doc) warnings. Wrote `13.ServiceDefaults/README.md` (previously an empty 1-line stub) with the full `Program.cs` composition snippet and six ordering rules, sourced verbatim from this file's "DI Registration (expected shape)" section. `SK.13.Docs` now 2/2 `●` — promoted to root. Test results: 37/37 SharedKernel.ServiceDefaults.Tests passing, 26/26 SharedKernel.MultiTenancy.Tests passing — no regressions (servicedefaults-phase-implementer)
- [2026-06-22] P-01 → `●`, P-02 → `●`, P-03 → `●` in SK.13.Published — both packages packed to `nupkgs/` with embedded XML docs. Added `PackageVersion` + `TreatWarningsAsErrors` to both csproj files (matching the established `04.Contracts`/`07.Messaging` packaging convention). `SharedKernel.ServiceDefaults.csproj`: removed `Microsoft.Extensions.Diagnostics.HealthChecks`/`Microsoft.Extensions.Hosting` PackageReferences (NU1510 — redundant given the existing `FrameworkReference Microsoft.AspNetCore.App`); suppressed NU5104 via `NoWarn` (a stable package may not depend on a prerelease package, but `OpenTelemetry.Instrumentation.EntityFrameworkCore` has never shipped a stable release upstream — documented, re-evaluate when OTel ships stable). `SharedKernel.MultiTenancy.csproj`: suppressed NU1903 via `NoWarn` (transitive high-severity advisory on `System.Security.Cryptography.Xml` pulled in via `SharedKernel.Security.Oidc` — upstream `12.Security` dependency-tree issue, not owned by this domain; documented, re-evaluate when `12.Security` upgrades). P-03: created `13.ServiceDefaults/consumer-verify/` (console harness, `IsPackable=false`, mirrors the `04.Contracts/consumer-verify` pattern) — `Program.cs` calls `AddServiceDefaults()` + `AddSharedKernelMultiTenancy()` against a real `WebApplicationBuilder`, builds the app, and resolves 5 surfaces with zero exceptions (`HealthCheckService`, `StartupGate`, `ITenantProvider`/`AmbientTenantProvider`, all three `ITenantResolutionStrategy` implementations, `TenantResolutionMiddleware` construction via `ActivatorUtilities`); registered a no-op `IDbConnectionFactory` test double so `DatabaseTenantResolutionStrategy` resolves (a real consumer using the Database strategy must register one too); registered in `Platform.SharedKernel.slnx`. Ran successfully — all 5 surfaces PASS. `SK.13.Published` now 3/3 `●` — promoted to root; `13.ServiceDefaults` domain reaches full Design→Scaffold→Core→Tests→Docs→Published completion. Test results: 37/37 SharedKernel.ServiceDefaults.Tests passing, 26/26 SharedKernel.MultiTenancy.Tests passing — no regressions (servicedefaults-phase-implementer)
