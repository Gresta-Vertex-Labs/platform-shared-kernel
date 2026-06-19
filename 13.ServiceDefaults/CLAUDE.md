# 13.ServiceDefaults — Host Composition & Cross-Cutting Brain

## What This Domain Is

The host composition layer. This is the only capability domain in the platform whose packages are wired directly into a microservice's `Program.cs` as the **first lines of startup** — OpenTelemetry, health checks, startup/liveness/readiness probes, and concrete multi-tenant resolution strategies. Everything here is composition glue: it assembles abstractions and concrete providers from layers `01`–`12` into ready-to-call extension methods. No business logic, no domain types, no new abstractions are defined here — only wiring.

Philosophy: **Composition-only. Opt-in by default. Liveness ≠ Readiness. No business logic.**

> **Layering exception:** `13.ServiceDefaults` is the **only** domain in the platform permitted to reference concrete infrastructure provider packages directly (`SharedKernel.Caching.Redis*`, `SharedKernel.Messaging.MassTransit`, `SharedKernel.Persistence.EfCore`/`.PostgreSQL`/`.Dapper`, `SharedKernel.Security.Oidc`) in addition to their abstractions. Every other domain (`05.Application`, `03.Domain`, etc.) must depend only on `.Abstractions` packages. This exception exists because `13.ServiceDefaults` *is* the composition root — health checks and telemetry wiring are inherently provider-specific (a Redis health check needs to know about Redis). This is mechanically enforced by `00.Governance`'s `SharedKernelLayeringRules` (Rule 1: no production assembly other than the concrete provider packages and `13.ServiceDefaults` may reference a concrete `02.Caching` provider — the same pattern applies to `07.Messaging` transports).

---

## Packages

| Package | Role | References |
|---------|------|-----------|
| `SharedKernel.ServiceDefaults` | `AddServiceDefaults()` composition entry point; OpenTelemetry (tracing/metrics/logging) wiring; health check composition with a hard liveness/readiness split; opt-in dependency-specific health check adapters (DB, Redis, RabbitMQ, Azure Service Bus); startup-probe gating | `SharedKernel.Primitives`; abstractions from `02.Caching`, `06.Persistence`, `07.Messaging`; concrete providers from the same domains when wiring their health checks/telemetry (see Layering exception above); `OpenTelemetry.*`, `Microsoft.Extensions.Diagnostics.HealthChecks`, `AspNetCore.HealthChecks.*` |
| `SharedKernel.MultiTenancy` | Concrete `ITenantProvider` resolution strategies (HTTP header, JWT claim delegation, DB-isolation directory lookup); `TenantResolutionMiddleware`; `AmbientTenantProvider` | `SharedKernel.Security.Abstractions` (`ITenantProvider`), `SharedKernel.Security.Oidc` (delegates claim resolution to `OidcTenantProvider` — does not reimplement it), `SharedKernel.Persistence.Abstractions` (`IDbConnectionFactory`, for DB-isolation lookups), `Microsoft.AspNetCore.Http.Abstractions` |

Both packages target `net10.0`, `ImplicitUsings` enabled, `Nullable` enabled. Test sub-folders live inside each project folder (never in a top-level `tests/`).

---

## Technology Stack

| Concern | Technology |
|---------|-----------|
| Distributed tracing, metrics, logging | OpenTelemetry .NET SDK; OTLP exporter configured via the standard `OTEL_EXPORTER_OTLP_ENDPOINT` / `OTEL_EXPORTER_OTLP_PROTOCOL` env vars — no SharedKernel-specific config keys, so the OTel Collector convention stays portable |
| ASP.NET Core / HttpClient / EF Core auto-instrumentation | `OpenTelemetry.Instrumentation.AspNetCore`, `OpenTelemetry.Instrumentation.Http`, `OpenTelemetry.Instrumentation.EntityFrameworkCore` |
| Health checks (BCL contract) | `Microsoft.Extensions.Diagnostics.HealthChecks` |
| Dependency-specific health check probes | `AspNetCore.HealthChecks.Redis`, `AspNetCore.HealthChecks.RabbitMQ`, `AspNetCore.HealthChecks.AzureServiceBus`, `AspNetCore.HealthChecks.NpgSql` (community packages, opt-in per service). **API surface gaps discovered (WO-027 Core):** `AspNetCore.HealthChecks.RabbitMQ` 9.0.0 has no direct AMQP-URI-string `AddRabbitMQ` overload — only `Func<IServiceProvider,IConnection>` and `Func<IServiceProvider,Task<IConnection>>` factory overloads exist. `AddRabbitMqMessagingHealthCheck` builds a `RabbitMQ.Client.ConnectionFactory { Uri = ... }` and calls `CreateConnectionAsync()` inside the async factory overload — requires a direct `RabbitMQ.Client` package reference pinned to match `MassTransit.RabbitMQ`'s transitive floor (7.2.1, not 7.1.2 — a lower pin causes NU1605). `AspNetCore.HealthChecks.AzureServiceBus` 9.0.0 ships only queue/topic/subscription-*scoped* checks (`AddAzureServiceBusQueue`/`Topic`/`Subscription`) — there is no namespace-only/connection-only check, but this domain's `AddAzureServiceBusMessagingHealthCheck(connectionStringOrNamespace)` contract takes no entity name. `AzureServiceBusHealthCheck` (internal) is a custom `IHealthCheck` built directly on `Azure.Messaging.ServiceBus.Administration.ServiceBusAdministrationClient.GetNamespacePropertiesAsync()` — detects connection-string vs. fully-qualified-namespace by checking for `"Endpoint="`/`"SharedAccessKey"` substrings, uses `DefaultAzureCredential` for the FQDN path. Requires direct `Azure.Messaging.ServiceBus`/`Azure.Identity` references pinned to `MassTransit.Azure.ServiceBus.Core`'s transitive floor (7.20.1 / 1.21.0). |
| OpenTelemetry runtime metrics instrumentation | `OpenTelemetry.Instrumentation.Runtime` — required for `MeterProviderBuilder.AddRuntimeInstrumentation()`; not part of the original Scaffold package set, added during Core (WO-027) when `AddSharedKernelTelemetry` was implemented |
| Tenant resolution — HTTP header | `Microsoft.AspNetCore.Http.Abstractions` (`HttpContext.Request.Headers`) |
| Tenant resolution — JWT claim | Delegates to `SharedKernel.Security.Oidc.OidcTenantProvider` — never reimplemented here |
| Tenant resolution — DB isolation | `SharedKernel.Persistence.Abstractions.IDbConnectionFactory` + parameterized SQL against a tenant directory table |
| Host composition | `Microsoft.Extensions.Hosting` (`IHostApplicationBuilder`), `Microsoft.Extensions.DependencyInjection` |

---

## Interface Contracts

### `SharedKernel.ServiceDefaults` — public surface

#### Composition entry point (`Extensions/`)

```text
AddServiceDefaults(this IHostApplicationBuilder builder)        → IHostApplicationBuilder
    Wires OpenTelemetry (tracing + metrics + logging via OTLP exporter) and the base health check
    endpoint mappings ("/health/live", "/health/ready" — see HealthChecks/ below).
    NOTE: Modeled on the .NET Aspire ServiceDefaults template — the SharedKernel-flavored equivalent.
          Must be the FIRST call in a microservice's Program.cs composition, before any
          SharedKernel.*.Add... extension from other domains. Resilience defaults for outbound
          HttpClient calls are NOT duplicated here — that is 11.Communication's
          AddSharedKernelRestCommunication() concern.
```

#### Health check composition (`HealthChecks/`)

```text
AddSharedKernelHealthChecks(this IServiceCollection services)   → IHealthChecksBuilder
    Registers the base health check infrastructure and the two endpoint mappings:
      "/health/live"  — only checks tagged "live": process-alive signal ONLY. Must never depend on
                         an external dependency (DB, cache, broker). A slow/unavailable dependency
                         must not cause K8s to kill and restart an otherwise-healthy pod.
      "/health/ready" — checks tagged "ready": DB, cache, and broker connectivity. A pod reporting
                         Unhealthy or Degraded here is removed from Service/Ingress load-balancer
                         rotation but is NOT restarted.
    NOTE: This live/ready tag split is the central design invariant of this package — see
          Implementation Rules and Hard Violations.

AddDatabaseReadinessCheck<TContext>(this IHealthChecksBuilder, string name = "database")
    where TContext : SharedKernelDbContext                       → IHealthChecksBuilder
    Wraps SharedKernelDbContext.CheckReadinessAsync (06.Persistence.EfCore, P-150) in an
    IHealthCheck. Reports Unhealthy when DatabaseReadinessResult.IsHealthy == false; Latency and
    Provider are surfaced via HealthCheckResult.Data. Tagged "ready", "db".
    NOTE: 06.Persistence ships only the probe primitive (DatabaseReadinessResult,
          CheckReadinessAsync) — per the root layering rule "OTel, health check, or probe wiring →
          13.ServiceDefaults", the IHealthCheck adapter lives here, not in 06.Persistence.

AddDapperDatabaseReadinessCheck(this IHealthChecksBuilder, string name = "database")
                                                                   → IHealthChecksBuilder
    Wraps IDbConnectionFactory.CheckReadinessAsync (06.Persistence.Abstractions) for Dapper-only
    read services that have no DbContext in scope. Tagged "ready", "db".

AddRedisHealthCheck(this IHealthChecksBuilder, string connectionString)
                                                                   → IHealthChecksBuilder
    (P-010, WO-003). Verifies StackExchange.Redis connectivity via AspNetCore.HealthChecks.Redis.
    Tagged "ready", "redis", "cache". Opt-in — a service using only the L1 in-process cache must
    never register this and must never fail readiness because no Redis is configured.

AddCacheReadinessCheck(this IHealthChecksBuilder)                → IHealthChecksBuilder
    (P-010, WO-003). Probes ICacheService.GetAsync<string> (02.Caching.Abstractions) with a
    synthetic key and a short timeout. Reports Degraded — NOT Unhealthy — on failure, because
    FusionCache's L1 fail-safe may still be serving stale data correctly; only a hard Unhealthy
    should pull a pod from rotation. Tagged "ready", "cache".

AddRabbitMqMessagingHealthCheck(this IHealthChecksBuilder, string amqpUri)
                                                                   → IHealthChecksBuilder
    (P-122, WO-020). Verifies a real AMQP connection (not just URI parsing) via
    AspNetCore.HealthChecks.RabbitMQ. Tagged "ready", "messaging". Connection details should be
    sourced from the resolved RabbitMqBusOptions (07.Messaging.MassTransit) — callers must not
    duplicate connection strings in calling code.

AddAzureServiceBusMessagingHealthCheck(this IHealthChecksBuilder, string connectionStringOrNamespace)
                                                                   → IHealthChecksBuilder
    (P-122, WO-020). Verifies ASB connectivity via AspNetCore.HealthChecks.AzureServiceBus. Uses
    DefaultAzureCredential when a fully-qualified namespace (no connection string) is passed.
    Tagged "ready", "messaging".

NOTE: Every dependency-specific check (Redis, RabbitMQ, ASB, DB) is tagged "ready" and is an
      explicit opt-in call — AddServiceDefaults() / AddSharedKernelHealthChecks() never register
      any of them automatically. A service that does not use a given dependency must not carry a
      health check for it.
```

#### OpenTelemetry wiring (`Telemetry/`)

```text
AddSharedKernelTelemetry(this IHostApplicationBuilder builder, string serviceName)
                                                                   → IHostApplicationBuilder
    Configures a ResourceBuilder with serviceName + assembly version; wires ASP.NET Core, HttpClient,
    and (when SharedKernel.Persistence.EfCore is referenced) EFCore instrumentation into the
    TracerProvider; wires runtime + ASP.NET Core instrumentation into the MeterProvider; OTLP
    exporter endpoint comes from the standard OTEL_EXPORTER_OTLP_ENDPOINT / _PROTOCOL env vars.
    Called internally by AddServiceDefaults() — exposed separately for services that need a custom
    serviceName distinct from the assembly name.

WithMessagingTelemetry(this IHostApplicationBuilder builder)     → IHostApplicationBuilder
    (P-132, WO-021). Adds "MassTransit" and "SharedKernel.Messaging" as traced ActivitySource names
    via WithTracing(t => t.AddSource(...)); adds the "MassTransit" meter via
    WithMetrics(m => m.AddMeter(...)). Idempotent — calling more than once registers no duplicate
    instruments (the underlying OTel SDK no-ops on a repeated source/meter name).
    NOTE: The "SharedKernel.Messaging" ActivitySource is created and used inside
          SharedKernel.Messaging.MassTransit (07.Messaging) — ConsumerBase<TMessage>.Consume() and
          MassTransitEventPublisher.PublishAsync() start child Activities from it. This method only
          wires that already-existing source into the host's TracerProvider/MeterProvider;
          13.ServiceDefaults never creates an ActivitySource itself.

WithCachingTelemetry(this IHostApplicationBuilder builder)       → IHostApplicationBuilder
    (P-010, WO-003, confirmed in Design phase D-01/D-02). Wires the pre-existing "SharedKernel.Caching"
    meter (version "1.0", static readonly field in FusionCacheService — 02.Caching Phase 31) into the
    host's MeterProvider via WithMetrics(m => m.AddMeter("SharedKernel.Caching")). Idempotent, same
    rationale as WithMessagingTelemetry.
    NOTE (verified against 02.Caching/SharedKernel.Caching.FusionCache/Implementations/FusionCacheService.cs):
    the meter's actual instrument set is cache.hits (Counter<long>), cache.misses (Counter<long>),
    cache.errors (Counter<long>), cache.evictions (Counter<long>), and cache.factory.duration
    (Histogram<double>, unit "ms"). This corrects an earlier draft of this contract that named the
    histogram "cache.operation.duration" — no such instrument exists; the correct name is
    cache.factory.duration (factory execution duration on cache miss). WithCachingTelemetry() wires
    the meter by name only — AddMeter("SharedKernel.Caching") subscribes to all five instruments
    automatically; this package does not enumerate or reference individual instrument names in code.
```

#### Startup / liveness probes (`Probes/`)

```text
StartupGate  (sealed class, registered as a singleton)
    .IsReady                                                      → bool  (volatile read)
    .MarkReady()                                                  → void  (called once, idempotent)
    NOTE: Backs the distinction between a K8s startup probe and the liveness/readiness probes.
          A long-running warm-up step (e.g. 06.Persistence's MigrationAndSeedHostedService applying
          pending migrations) must gate "/health/ready" — and optionally "/health/live" if the
          process genuinely cannot serve traffic yet — until MarkReady() has been called by an
          IHostedLifecycleService whose StartAsync runs after all other hosted services.

StartupGateHealthCheck  (sealed class, implements IHealthCheck)
    Reports Unhealthy while StartupGate.IsReady == false; Healthy once true. Tagged "ready".
    Registered automatically by AddServiceDefaults() — always present, zero configuration needed,
    and adds no dependency-specific coupling (it depends only on StartupGate, not on any provider).
```

### `SharedKernel.MultiTenancy` — public surface

#### Resolution strategies (`Resolution/`)

```text
ITenantResolutionStrategy
    .TryResolveAsync(HttpContext context, CancellationToken ct) → Task<Guid?>
    NOTE: Returns null when this strategy cannot resolve a tenant from the given request — the
          composing resolver then tries the next strategy in TenantResolutionOptions.StrategyOrder.
          Must never throw for an absent/not-found tenant signal; reserve exceptions for genuinely
          exceptional conditions, not the expected "this strategy doesn't apply to this request" case.

HeaderTenantResolutionStrategy  (sealed class, implements ITenantResolutionStrategy)
    constructor: HeaderTenantResolutionStrategy(string headerName = "X-Tenant-Id")
    NOTE: Resolves TenantId from the named HTTP request header; Guid.TryParse — returns null when
          the header is absent or its value does not parse. Intended for B2B / API-key clients that
          have no tenant claim embedded in their access token.

ClaimTenantResolutionStrategy  (sealed class, implements ITenantResolutionStrategy)
    NOTE: Thin adapter that delegates to SharedKernel.Security.Oidc.OidcTenantProvider when an
          authenticated ClaimsPrincipal carries the SecurityClaimTypes.TenantId claim
          (12.Security.Abstractions). Must NOT duplicate claim-parsing logic — any change to claim
          resolution happens once, in 12.Security.Oidc.

DatabaseTenantResolutionStrategy  (sealed class, implements ITenantResolutionStrategy)
    NOTE: For DB-per-tenant / schema-per-tenant isolation models. Resolves tenant identity from a
          tenant-directory lookup keyed by request host or subdomain (e.g.
          "acme.api.example.com" → TenantId) via IDbConnectionFactory (06.Persistence.Abstractions)
          using a parameterized query — string interpolation into SQL is a hard violation (SK0xxx).
          Distinct from 06.Persistence's TenantedDbContext: that applies a row-level filter once the
          tenant is already known; this strategy answers the prior question of "which tenant is
          this request for" in isolation models where the tenant is not visible in a header or claim.

TenantResolutionOptions  (options POCO, section "SharedKernel:MultiTenancy")
    .StrategyOrder                                                → IReadOnlyList<string>
        Default: ["Header", "Claim", "Database"]. The first strategy in this order whose
        TryResolveAsync returns non-null wins. A service with no tenant directory database simply
        omits "Database" from the configured order — no code change, no null-reference risk.
```

#### Ambient provider and middleware (`Middleware/`)

```text
AmbientTenantProvider  (sealed class, implements ITenantProvider)
    .TenantId                                                     → Guid  (private set)
    NOTE: Scoped per HTTP request. Defaults to Guid.Empty until TenantResolutionMiddleware runs.
          This is the ITenantProvider implementation registered when a service opts into
          SharedKernel.MultiTenancy — it is mutually exclusive with registering
          SharedKernel.Security.Oidc.OidcTenantProvider directly as ITenantProvider; the latter is
          instead wrapped by ClaimTenantResolutionStrategy above so header/claim/DB resolution can
          compose in priority order.

TenantResolutionMiddleware  (sealed class)
    .InvokeAsync(HttpContext context, RequestDelegate next)      → Task
    NOTE: Runs the configured ITenantResolutionStrategy list in TenantResolutionOptions.StrategyOrder
          and sets AmbientTenantProvider.TenantId from the first non-null result. If no strategy
          resolves a tenant, TenantId remains Guid.Empty — consistent with the Guid.Empty
          no-tenant sentinel rule documented in 06.Persistence (P-092): the TenantedDbContext global
          filter then matches zero rows rather than risking a cross-tenant data leak.
          Must be registered AFTER UseAuthentication() in the request pipeline so
          ClaimTenantResolutionStrategy has access to a populated ClaimsPrincipal.
```

#### DI registration (`Extensions/`)

```text
AddSharedKernelMultiTenancy(this IServiceCollection services,
                             Action<TenantResolutionOptions>? configure = null) → IServiceCollection
    Registers TenantResolutionOptions (via the Options pattern from SharedKernel.Configuration),
    AmbientTenantProvider as scoped ITenantProvider, and the configured set of
    ITenantResolutionStrategy implementations (scoped).
    NOTE: Must be paired with app.UseMiddleware<TenantResolutionMiddleware>() placed after
          UseAuthentication(). Registering the services without wiring the middleware leaves
          AmbientTenantProvider.TenantId permanently Guid.Empty — a silent (zero-rows) failure
          mode by design, not a crash.
```

---

## Implementation Rules

- **Phase build order:** Scaffold (real project/package references replacing the bare `.csproj` stubs) must land before any Core work. Within Core, the foundation (`AddServiceDefaults`, `AddSharedKernelTelemetry`, `AddSharedKernelHealthChecks` + the live/ready split, `StartupGate`/`StartupGateHealthCheck`, and the full `SharedKernel.MultiTenancy` surface) must land before any dependency-specific extension (`AddRedisHealthCheck`, `AddCacheReadinessCheck`, `WithCachingTelemetry`, `AddRabbitMqMessagingHealthCheck`, `AddAzureServiceBusMessagingHealthCheck`, `WithMessagingTelemetry`) — every dependency-specific check extends the `IHealthChecksBuilder` returned by `AddSharedKernelHealthChecks()`, so that base must exist first. `WithMessagingTelemetry()` additionally depends on `07.Messaging`'s P-172 (the `"SharedKernel.Messaging"` `ActivitySource` definition) landing before it can be implemented — check `07.Messaging/state-map.md`'s `SK.07.OTel` phase state before starting that task.
- The liveness/readiness tag split is the central invariant of this domain: any check that depends on an external system (database, cache, message broker) is tagged `"ready"` and **never** `"live"`. The `"/health/live"` endpoint must answer only "is this process alive" — never "are this process's dependencies alive."
- Every dependency-specific health check (`AddRedisHealthCheck`, `AddRabbitMqMessagingHealthCheck`, `AddAzureServiceBusMessagingHealthCheck`, `AddDatabaseReadinessCheck<TContext>`, `AddDapperDatabaseReadinessCheck`, `AddCacheReadinessCheck`) is an explicit opt-in call on `IHealthChecksBuilder`. `AddServiceDefaults()` and `AddSharedKernelHealthChecks()` register **only** the base endpoint mappings and `StartupGateHealthCheck` — never a dependency-specific check.
- `AddCacheReadinessCheck` must report `Degraded`, not `Unhealthy`, on a cache probe failure — FusionCache's L1 fail-safe may still be correctly serving stale data, and an `Unhealthy` readiness result removes the pod from rotation unnecessarily during a transient Redis blip.
- Messaging and cache health checks must read their connection details from the already-resolved options objects (`RabbitMqBusOptions`, `AzureServiceBusOptions`, the registered `ICacheService`/Redis connection) — never require the caller to duplicate a connection string or URI in the `Add*HealthCheck` call site beyond what is unavoidable for opt-in registration.
- `WithMessagingTelemetry()` and `WithCachingTelemetry()` must be idempotent — calling either more than once must not register duplicate `ActivitySource`/meter instruments.
- `13.ServiceDefaults` never *creates* an `ActivitySource` or custom meter on behalf of another domain — `07.Messaging`'s `"SharedKernel.Messaging"` source and `02.Caching`'s `"SharedKernel.Caching"` meter are created in their own domains; this package only wires already-existing sources/meters into the host's `TracerProvider`/`MeterProvider`.
- `ClaimTenantResolutionStrategy` must delegate to `SharedKernel.Security.Oidc.OidcTenantProvider` — it must never reimplement claim-name parsing or duplicate `SecurityClaimTypes.TenantId` resolution logic.
- `DatabaseTenantResolutionStrategy` must use parameterized queries exclusively — building SQL by string interpolation or concatenation with request-derived values (host, subdomain) is a hard violation.
- `TenantResolutionMiddleware` must be registered after `UseAuthentication()` in the request pipeline — `ClaimTenantResolutionStrategy` requires a populated `ClaimsPrincipal`.
- `AmbientTenantProvider.TenantId` defaults to `Guid.Empty` and is set exactly once per request by `TenantResolutionMiddleware` — no other component may set it.
- `13.ServiceDefaults` may reference concrete provider packages from `02.Caching`, `06.Persistence`, `07.Messaging`, and `12.Security` (see Layering exception above) — but must never reference `14.Presentation`, `15.Integration`, `16.Testing`, or `17.Workflows`. The dependency direction remains strictly downward (`13` may reference `01`–`12` only).
- No static mutable state anywhere in this domain except `StartupGate`, which is an intentional, narrowly-scoped, thread-safe (`volatile`) singleton gate — not a general-purpose static cache.
- `SharedKernel.ServiceDefaults.csproj` requires `<FrameworkReference Include="Microsoft.AspNetCore.App" />`. The project is a plain `Microsoft.NET.Sdk` class library (not `Microsoft.NET.Sdk.Web`), but `MapHealthChecks`/`HealthCheckOptions`/`IEndpointRouteBuilder` (used by `MapDefaultHealthCheckEndpoints`) come from the ASP.NET Core shared framework, which is otherwise unresolvable from a plain class library.
- `TenantResolutionMiddleware.InvokeAsync` maps each injected `ITenantResolutionStrategy` to a `"Header"`/`"Claim"`/`"Database"` name by switching on the strategy's **concrete CLR type name** (`s.GetType().Name`), then looks up that name against `TenantResolutionOptions.StrategyOrder`. Consequence for tests: an `NSubstitute.For<ITenantResolutionStrategy>()` proxy's type name will never match `"Header"`/`"Claim"`/`"Database"`, so it is silently unreachable from `StrategyOrder`. Tests verifying strategy-ordering/precedence behavior must use the real strategy types (`HeaderTenantResolutionStrategy`, etc.) — substitutes/mocks are only useful for proving an *omitted* strategy is never invoked (since their unmatched name guarantees that outcome, not because of correct name-mapping).

---

## DI Registration (expected shape)

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();                          // OTel + base health endpoints — FIRST call
builder.Services.AddSharedKernelMultiTenancy();        // optional — multi-tenant services only

// Opt-in dependency-specific health checks — only what this service actually uses:
builder.Services.AddHealthChecks()
    .AddDatabaseReadinessCheck<MyDbContext>()
    .AddRedisHealthCheck(redisConnectionString)
    .AddRabbitMqMessagingHealthCheck(amqpUri);

builder.WithMessagingTelemetry();                      // optional — services using 07.Messaging
builder.WithCachingTelemetry();                        // optional — services using 02.Caching

var app = builder.Build();

app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>();       // required when AddSharedKernelMultiTenancy() is used — must run after UseAuthentication()

app.MapDefaultHealthCheckEndpoints();                  // "/health/live", "/health/ready"

app.Run();
```

`SharedKernel.MultiTenancy` ships no telemetry or health check wiring of its own — that composition stays in `SharedKernel.ServiceDefaults`.

---

## AOT Compatibility

- OpenTelemetry .NET SDK is largely AOT-compatible as of the versions targeted by `net10.0`; some exporter resource-detectors use reflection-based assembly metadata lookup — this is encapsulated entirely behind `AddSharedKernelTelemetry`/`AddServiceDefaults`, so the AOT blast radius does not reach consuming application code.
- `AspNetCore.HealthChecks.*` community packages vary in AOT readiness by transport; this is not a hard blocker per the repo's pragmatic AOT policy — each opt-in `Add*HealthCheck` method is documented individually as adopted rather than the whole package being declared AOT-unsafe.
- `ITenantResolutionStrategy` implementations (`HeaderTenantResolutionStrategy`, `ClaimTenantResolutionStrategy`, `DatabaseTenantResolutionStrategy`) are plain sealed classes using `Guid.TryParse` and parameterized ADO.NET queries — AOT-safe.
- `StartupGate` / `StartupGateHealthCheck` are plain classes with a `volatile bool` field — AOT-safe, no reflection.
- No `Activator.CreateInstance`, no `Assembly.Load`, no `MakeGenericMethod`/`Invoke` reflection anywhere in this domain.

---

## Test Rules

- Unit tests for `SharedKernel.ServiceDefaults` live in `13.ServiceDefaults/SharedKernel.ServiceDefaults/SharedKernel.ServiceDefaults.Tests/`.
- Unit tests for `SharedKernel.MultiTenancy` live in `13.ServiceDefaults/SharedKernel.MultiTenancy/SharedKernel.MultiTenancy.Tests/`.
- Health check tag tests: every dependency-specific check (`Redis`, `RabbitMQ`, `AzureServiceBus`, `Database`, `Cache`) is registered with tag `"ready"` and never with tag `"live"` — assert against the registered `HealthCheckRegistration.Tags`.
- `AddCacheReadinessCheck`: a forced cache-probe failure must report `HealthStatus.Degraded`, never `HealthStatus.Unhealthy`.
- `StartupGateHealthCheck`: reports `Unhealthy` before `MarkReady()` is called; reports `Healthy` after; `MarkReady()` is idempotent (calling twice does not throw and does not toggle state back).
- `WithMessagingTelemetry()` / `WithCachingTelemetry()`: calling each twice on the same builder registers exactly one instance of each `ActivitySource`/meter name (no duplicate-instrument assertion via the OTel SDK's exposed listener APIs).
- `HeaderTenantResolutionStrategy`: present + parseable header → resolved `Guid`; absent header → `null`; malformed header value → `null` (never throws).
- `ClaimTenantResolutionStrategy`: delegates correctly to a fake/mocked `OidcTenantProvider`-shaped dependency; never re-parses raw claims itself (verified by testing through the seam, not by reflection over private state).
- `DatabaseTenantResolutionStrategy`: resolves a known host/subdomain to the expected `TenantId` against a test double `IDbConnectionFactory`; unknown host → `null`; query parameterization verified (no string-built SQL in the executed command text).
- `TenantResolutionOptions.StrategyOrder`: default order is `["Header", "Claim", "Database"]`; first non-null strategy result wins; omitting a strategy from the order means it is never invoked.
- `TenantResolutionMiddleware`: sets `AmbientTenantProvider.TenantId` from the first resolving strategy; no strategy resolves → `TenantId` remains `Guid.Empty`; middleware does not throw when zero strategies are configured.
- `AmbientTenantProvider`: defaults to `Guid.Empty`; `TenantId` setter is `private` (verified via reflection, mirroring the equivalent test pattern used for `TenantedAggregateRoot<TId>.TenantId` in `03.Domain`).
- DI registration tests use `IServiceCollection` / `ServiceCollection` directly with `BuildServiceProvider()` for unit-level verification — no `WebApplicationFactory` required except for endpoint-mapping integration tests (`/health/live`, `/health/ready` return expected status codes and bodies).
- `/health/live`/`/health/ready` endpoint-mapping integration tests use `new HostBuilder().ConfigureWebHost(webHost => webHost.UseTestServer()...)` + `IHost.GetTestClient()` — not `WebApplicationFactory<TEntryPoint>`, since `SharedKernel.ServiceDefaults` is a class library with no `Program` marker type to target. This manual host setup does **not** implicitly register routing services the way `WebApplication.CreateBuilder()` does — an explicit `services.AddRouting()` call is required before `AddSharedKernelHealthChecks()` or `UseRouting()` throws `InvalidOperationException`.
- `DatabaseTenantResolutionStrategy` tests mock at the raw ADO.NET interface level (`IDbConnection`/`IDbCommand`/`IDbDataParameter` via NSubstitute) — `16.Testing` has no `IDbConnectionFactory` fake yet.
- `ClaimTenantResolutionStrategy` tests build a real `ClaimsPrincipal` carrying `SecurityClaimTypes.TenantId` rather than mocking `OidcTenantProvider` — it has no interface and is a concrete sealed class constructed from a `ClaimsPrincipal`, so delegation is verified end-to-end through the seam instead.

---

## Changelog

> Maintained by `/sync-brain` and the servicedefaults-arch-planner domain agent. One line per significant change.

- [2026-06-19] Domain brain initialized — packages (`SharedKernel.ServiceDefaults`, `SharedKernel.MultiTenancy`), liveness/readiness health check split, OTel composition (`AddServiceDefaults`, `WithMessagingTelemetry`, `WithCachingTelemetry`), dependency-specific health check adapters wrapping `06.Persistence` probe primitives and `02.Caching`/`07.Messaging` connectivity, three-strategy multi-tenant resolution (Header/Claim/DB isolation) with `AmbientTenantProvider` + `TenantResolutionMiddleware`, implementation rules, AOT notes, test rules. Derived from the root `CLAUDE.md` folder map entry for `13.ServiceDefaults` and the pending root-level backlog items P-010 (WO-003, Redis health check + cache readiness probe + caching OTel metrics), P-122 (WO-020, RabbitMQ/Azure Service Bus messaging health checks), and P-132 (WO-021, messaging OTel wiring) — no dedicated servicedefaults-arch-planner agent exists yet, so this pass is an arch-lead-equivalent placeholder pending formal Design-phase task breakdown in `state-map.md` (arch-lead)
- [2026-06-19] Full Design→Published task breakdown landed in `state-map.md` for six root backlog items: P-169 (WO-027, Scaffold — real project/package references and folder structure for both packages, replacing the bare `.csproj` stubs; nested `.Tests` projects; `.slnx` registration); P-170 (WO-027, Core — `AddServiceDefaults`, `AddSharedKernelTelemetry`, `AddSharedKernelHealthChecks` with the live/ready endpoint split, `StartupGate`/`StartupGateHealthCheck`); P-171 (WO-027, Core — the full `SharedKernel.MultiTenancy` surface: `ITenantResolutionStrategy` + three concrete strategies, `TenantResolutionOptions`, `AmbientTenantProvider`, `TenantResolutionMiddleware`, `AddSharedKernelMultiTenancy`); P-010 (WO-003, `AddRedisHealthCheck`/`AddCacheReadinessCheck`/`WithCachingTelemetry`); P-122 (WO-020, `AddRabbitMqMessagingHealthCheck`/`AddAzureServiceBusMessagingHealthCheck`); P-132 (WO-021, `WithMessagingTelemetry`, scoped strictly to wiring the pre-existing `"MassTransit"`/`"SharedKernel.Messaging"` source names — the latter gated on `07.Messaging`'s P-172, confirmed still pending as of this pass). Phase ordering established: P-169 (Scaffold) must land before P-170/P-171 (Core foundation), which must land before P-010/P-122/P-132 (Core dependency-specific additions extending `AddSharedKernelHealthChecks()`'s `IHealthChecksBuilder` base). No new interface contracts beyond what was already documented in this file — this pass formalized existing design into concrete, numbered, sequenced tasks (servicedefaults-arch-planner)
- [2026-06-19] Design phase (`SK.13.Design`, D-01/D-02) confirmed and closed by the servicedefaults-phase-implementer. D-01: tag taxonomy/calibration for `AddRedisHealthCheck` (`"ready"`+`"redis"`+`"cache"`, opt-in) and `AddCacheReadinessCheck` (`"ready"`+`"cache"`, `Degraded` never `Unhealthy` on probe failure — FusionCache L1 fail-safe rationale) re-verified against the existing Interface Contracts / Implementation Rules sections of this file — no changes needed, design was already correctly specified. D-02: `WithCachingTelemetry()` meter-wiring contract re-verified directly against `02.Caching/SharedKernel.Caching.FusionCache/Implementations/FusionCacheService.cs` (ground truth) — found and corrected a discrepancy: the contract previously named a `cache.operation.duration` histogram, which does not exist in the actual `"SharedKernel.Caching"` meter; the real instrument set is `cache.hits`, `cache.misses`, `cache.errors`, `cache.evictions` (all `Counter<long>`) and `cache.factory.duration` (`Histogram<double>`, unit `"ms"`, factory-execution time on cache miss). Confirmed `WithCachingTelemetry()` wires the meter by name only (`AddMeter("SharedKernel.Caching")`) and never enumerates individual instruments, and that no new `Meter`/`ActivitySource` is created in `13.ServiceDefaults` — ownership stays entirely with `02.Caching`. No code implementation in this phase — Scaffold (S-01–S-10) has not landed, `.csproj` files remain bare stubs (servicedefaults-phase-implementer)
- [2026-06-19] SK.13.Core C-01–C-18 implemented and tested (18 ServiceDefaults + 23 MultiTenancy tests passing): `AddServiceDefaults`/`AddSharedKernelTelemetry`/`AddSharedKernelHealthChecks` + live/ready endpoint split, `StartupGate`/`StartupGateHealthCheck`, full `SharedKernel.MultiTenancy` surface (Header/Claim/Database strategies, `TenantResolutionOptions`, `AmbientTenantProvider`, `TenantResolutionMiddleware`, `AddSharedKernelMultiTenancy`), `AddRedisHealthCheck`, `AddCacheReadinessCheck`, `WithCachingTelemetry`, `AddRabbitMqMessagingHealthCheck`, `AddAzureServiceBusMessagingHealthCheck`. `SharedKernel.MultiTenancy` gained a new `ProjectReference` to `SharedKernel.Security.Oidc` (previously only `.Abstractions`) so `ClaimTenantResolutionStrategy` can construct `OidcTenantProvider(context.User)` directly per-request. C-19 (`WithMessagingTelemetry`) left `⚑` blocked in `state-map.md` — `07.Messaging`'s P-172 `ActivitySource("SharedKernel.Messaging","1.0.0")` confirmed still entirely `○` (`SK.07.OTel` phase, tasks OT-01–OT-08) as of this pass; cannot wire a source that does not yet exist in code (servicedefaults-phase-implementer)
