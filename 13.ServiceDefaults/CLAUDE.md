# 13.ServiceDefaults — Host Composition & Cross-Cutting Brain

## What This Domain Is

The host composition layer. This is the only capability domain in the platform whose packages are wired directly into a microservice's `Program.cs` as the **first lines of startup** — OpenTelemetry, health checks, startup/liveness/readiness probes, and concrete multi-tenant resolution strategies. Everything here is composition glue: it assembles abstractions and concrete providers from layers `01`–`12` into ready-to-call extension methods. No business logic, no domain types, no new abstractions are defined here — only wiring.

Philosophy: **Composition-only. Opt-in by default. Liveness ≠ Readiness. No business logic.**

> **Layering exception:** `13.ServiceDefaults` is the **only** domain in the platform permitted to reference concrete infrastructure provider packages directly (`SharedKernel.Caching.Redis*`, `SharedKernel.Messaging.MassTransit`, `SharedKernel.Persistence.EfCore`/`.PostgreSQL`/`.Dapper`, `SharedKernel.Security.Oidc`) in addition to their abstractions. Every other domain (`05.Application`, `03.Domain`, etc.) must depend only on `.Abstractions` packages. This exception exists because `13.ServiceDefaults` *is* the composition root — health checks and telemetry wiring are inherently provider-specific (a Redis health check needs to know about Redis). This is mechanically enforced by `00.Governance`'s `SharedKernelLayeringRules` (Rule 1: no production assembly other than the concrete provider packages and `13.ServiceDefaults` may reference a concrete `02.Caching` provider — the same pattern applies to `07.Messaging` transports).

---

## Packages

| Package | Role | References |
|---------|------|-----------|
| `SharedKernel.ServiceDefaults` | `AddServiceDefaults()` composition entry point; OpenTelemetry (tracing/metrics/logging) wiring, including OTLP log export with scopes + formatted message and generic `Activity.Baggage`→`LogRecord.Attributes` enrichment (`BaggageLogRecordProcessor`); health check composition with a hard liveness/readiness split; opt-in dependency-specific health check adapters (DB, Redis, RabbitMQ, Azure Service Bus, object storage); startup-probe gating | `SharedKernel.Primitives`; abstractions from `02.Caching`, `06.Persistence`, `07.Messaging`, `08.Storage` (`SharedKernel.Storage.Abstractions` — `IFileStorage` consumed by `AddStorageReadinessCheck`); concrete providers from the same domains when wiring their health checks/telemetry (see Layering exception above); `OpenTelemetry.*`, `Microsoft.Extensions.Diagnostics.HealthChecks`, `AspNetCore.HealthChecks.*` |
| `SharedKernel.MultiTenancy` | Concrete `ITenantProvider` resolution strategies (HTTP header, JWT claim delegation, DB-isolation directory lookup); `TenantResolutionMiddleware` (also sets `TenantId` as `Activity` baggage so it becomes ambient to every log record via `SharedKernel.ServiceDefaults`'s log pipeline); `AmbientTenantProvider` | `SharedKernel.Security.Abstractions` (`ITenantProvider`), `SharedKernel.Security.Oidc` (delegates claim resolution to `OidcTenantProvider` — does not reimplement it), `SharedKernel.Persistence.Abstractions` (`IDbConnectionFactory`, for DB-isolation lookups), `Microsoft.AspNetCore.Http.Abstractions` |

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

AddDatabaseReadinessCheck<TContext>(this IHealthChecksBuilder, string name = HealthCheckNames.Database)
    where TContext : SharedKernelDbContext                       → IHealthChecksBuilder
    (P-150 contract, implemented WO-028/P-177, C-27). Wraps SharedKernelDbContext.CheckReadinessAsync
    (06.Persistence.EfCore) in an IHealthCheck. Reports Unhealthy when
    DatabaseReadinessResult.IsHealthy == false; Latency and Provider are surfaced via
    HealthCheckResult.Data. Tagged "ready", "db".
    NOTE: 06.Persistence ships only the probe primitive (DatabaseReadinessResult,
          CheckReadinessAsync) — per the root layering rule "OTel, health check, or probe wiring →
          13.ServiceDefaults", the IHealthCheck adapter lives here, not in 06.Persistence.

AddDapperDatabaseReadinessCheck(this IHealthChecksBuilder, string name = HealthCheckNames.Database)
                                                                   → IHealthChecksBuilder
    (P-150 contract, implemented WO-028/P-177, C-28). Wraps IDbConnectionFactory's readiness
    extension (06.Persistence.Abstractions, DbConnectionFactoryDiagnosticsExtensions) for
    Dapper-only read services that have no DbContext in scope. Tagged "ready", "db".

AddRedisHealthCheck(this IHealthChecksBuilder, string connectionString, string name = HealthCheckNames.Redis)
                                                                   → IHealthChecksBuilder
    (P-010, WO-003). Verifies StackExchange.Redis connectivity via AspNetCore.HealthChecks.Redis.
    Tagged "ready", "redis", "cache". Opt-in — a service using only the L1 in-process cache must
    never register this and must never fail readiness because no Redis is configured.

AddCacheReadinessCheck(this IHealthChecksBuilder, string name = HealthCheckNames.Cache)
                                                                   → IHealthChecksBuilder
    (P-010, WO-003). Probes ICacheService.GetAsync<string> (02.Caching.Abstractions) with a
    synthetic key and a short timeout. Reports Degraded — NOT Unhealthy — on failure, because
    FusionCache's L1 fail-safe may still be serving stale data correctly; only a hard Unhealthy
    should pull a pod from rotation. Tagged "ready", "cache".

AddRabbitMqMessagingHealthCheck(this IHealthChecksBuilder, string amqpUri, string name = HealthCheckNames.RabbitMq)
                                                                   → IHealthChecksBuilder
    (P-122, WO-020). Verifies a real AMQP connection (not just URI parsing) via
    AspNetCore.HealthChecks.RabbitMQ. Tagged "ready", "messaging". Connection details should be
    sourced from the resolved RabbitMqBusOptions (07.Messaging.MassTransit) — callers must not
    duplicate connection strings in calling code.

AddAzureServiceBusMessagingHealthCheck(this IHealthChecksBuilder, string connectionStringOrNamespace, string name = HealthCheckNames.AzureServiceBus)
                                                                   → IHealthChecksBuilder
    (P-122, WO-020). Verifies ASB connectivity via AspNetCore.HealthChecks.AzureServiceBus. Uses
    DefaultAzureCredential when a fully-qualified namespace (no connection string) is passed.
    Tagged "ready", "messaging".

AddStorageReadinessCheck(this IHealthChecksBuilder, string bucket, string name = HealthCheckNames.Storage)
                                                                   → IHealthChecksBuilder
    (P-270, WO-043 — IMPLEMENTED and tested, closed 2026-07-18; state-map C-36/T-32/DO-06 all `●`).
    Wraps `08.Storage`'s `SharedKernel.Storage.Abstractions.IFileStorage
    .CheckHealthAsync(bucket, ct) → Task<Result>` (P-265) in a `StorageReadinessHealthCheck`.
    Resolves `IFileStorage` from DI — works uniformly against whichever provider
    (`SharedKernel.Storage.S3` or `SharedKernel.Storage.Obs`) a service has registered, with zero
    provider-specific branching in this package (never references `S3StorageOptions`/
    `ObsStorageOptions` directly). Reports Healthy when `Result.IsSuccess`; Unhealthy otherwise —
    calibrated like `AddRedisHealthCheck`/`AddDatabaseReadinessCheck<TContext>` (raw connectivity,
    no fail-safe-absorption layer sits in front of object-storage reachability, unlike
    `AddCacheReadinessCheck`'s `Degraded` rationale). The failing `Result.Error.Message` is surfaced
    via `HealthCheckResult.Description`. Tagged "ready", "storage". Opt-in only.
    NOTE: `bucket` is a required explicit parameter — deliberately never defaulted from either
          provider's `DefaultBucket` option, since doing so would require referencing a concrete
          provider options type and reintroduce exactly the provider-specific coupling this method
          exists to avoid; `IFileStorage` itself carries no "default bucket" concept.
    RESOLVED: this contract was documented ahead of implementation (WO-043 dispatch, 2026-07-16)
          while `08.Storage`'s own `SK.08.Design`/`SK.08.Core` were still `○`, with a tracked,
          blocked state-map task (C-36) rather than a silent gap. `08.Storage` reached `Published`
          end to end before this phase resumed; the blocker was re-verified directly against
          `08.Storage/state-map.md` and the compiled `SharedKernel.Storage.Abstractions` source
          (not taken on trust) and found fully cleared, so C-36/T-32/DO-06 were implemented in the
          same session with zero deviation from the contract already locked here.

NOTE: Every dependency-specific check (Redis, RabbitMQ, ASB, DB, Storage) is tagged "ready" and is
      an explicit opt-in call — AddServiceDefaults() / AddSharedKernelHealthChecks() never register
      any of them automatically. A service that does not use a given dependency must not carry a
      health check for it.

HealthCheckNames  (static class, string constants — WO-028/P-177; Storage added WO-043/P-270)
    .Database = "database"   .Redis = "redis"   .RabbitMq = "rabbitmq"
    .AzureServiceBus = "azure-service-bus"   .Cache = "cache"   .Startup = "startup"
    .Storage = "storage"   (P-270 — implemented; see AddStorageReadinessCheck above)
    NOTE: Mirrors the pre-existing HealthCheckTags constants-class pattern. Every Add*HealthCheck
          default `name` parameter and the inline "startup" registration inside
          AddSharedKernelHealthChecks reference these constants — zero bare-literal health-check
          names remain anywhere in SharedKernel.ServiceDefaults.
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

WithApplicationTelemetry(this IHostApplicationBuilder builder)  → IHostApplicationBuilder
    (P-247, WO-040). Wires the pre-existing "SharedKernel.Application" ActivitySource and Meter
    (both version "1.0.0", owned by 05.Application.Behaviors' internal ApplicationDiagnostics
    static class — WO-035/WO-036) into the host's TracerProvider/MeterProvider via
    WithTracing(t => t.AddSource("SharedKernel.Application")) and
    WithMetrics(m => m.AddMeter("SharedKernel.Application")), by string name only. Idempotent —
    calling more than once registers no duplicate instruments, identical contract to
    WithMessagingTelemetry/WithCachingTelemetry.
    NOTE: The "SharedKernel.Application" ActivitySource/Meter pair is created and used inside
          SharedKernel.Application.Behaviors — TracingBehavior<,> starts spans from the source,
          MetricsBehavior<,> records the sharedkernel.application.request.duration histogram from
          the meter. This method only wires that already-existing pair into the host's
          TracerProvider/MeterProvider; 13.ServiceDefaults never creates an ActivitySource/Meter
          itself. ApplicationDiagnostics is internal to its own assembly with no InternalsVisibleTo
          grant to SharedKernel.ServiceDefaults, so string-name wiring is the only viable approach
          (same situation as WithMessagingTelemetry's internal MessagingDiagnostics) — no new
          ProjectReference to any SharedKernel.Application.* package is added or needed.
          Closes 05.Application.Behaviors' documented forward reference to this domain.

AddSharedKernelTelemetry(...)  — logging export addition (WO-041/P-251)
    In addition to the tracing/metrics wiring documented above, AddSharedKernelTelemetry's
    existing OpenTelemetry builder chain gains a matching log-export registration:
        .WithLogging(
            loggerProviderBuilder => loggerProviderBuilder
                .AddProcessor<BaggageLogRecordProcessor>()
                .AddOtlpExporter(),
            options =>
            {
                options.IncludeScopes = true;
                options.IncludeFormattedMessage = true;
            })
    NOTE: No new public extension method — this is additive behavior inside AddSharedKernelTelemetry's
          existing signature, the same way tracing and metrics were already wired. The OTLP log
          exporter reads the same standard OTEL_EXPORTER_OTLP_ENDPOINT / _PROTOCOL env vars already
          used for traces and metrics — no new SharedKernel-specific config keys. Every
          [LoggerMessage]-authored log record platform-wide (root CLAUDE.md Logging Conventions,
          WO-041) is now exported through the same OTLP pipeline as traces and metrics — closing the
          "13.ServiceDefaults wires tracing/metrics but not logging" gap the WO-041 audit found.

BaggageLogRecordProcessor  (sealed class, implements BaseProcessor<LogRecord>, in Telemetry/)
    .OnEnd(LogRecord data)                                        → void
        Reads Activity.Current?.Baggage at the moment the log record is finalized and appends every
        baggage key not already present in LogRecord.Attributes. No-op (no throw, no attributes
        added) when Activity.Current is null. Never overwrites an attribute already present at the
        same key — an explicit call-site value always wins over ambient baggage.
    NOTE: This is a GENERIC mechanism — it carries no hardcoded key names ("CorrelationId",
          "TenantId", or otherwise). This is what lets 14.Presentation's pre-existing CorrelationId
          Activity-baggage mechanism (WO-031 — correlation-id middleware "owns its own Activity
          baggage key directly against System.Diagnostics.Activity") land on every log record
          produced during that request, with ZERO ProjectReference from 13.ServiceDefaults to
          14.Presentation. The same mechanism is what surfaces SharedKernel.MultiTenancy's new
          TenantBaggageKeys.TenantId baggage (see below) onto every log record during a
          tenant-resolved request. Any future domain that sets its own Activity baggage key
          automatically gets the same free ambient-log-enrichment behavior — no 13.ServiceDefaults
          change required.
    SCOPE NOTE: This mechanism covers the HTTP-request path only, via whatever sets Activity baggage
          during that request (TenantResolutionMiddleware, 14.Presentation's correlation-id
          middleware). A message-consumption-scope equivalent (e.g. a MassTransit consumer filter
          setting the same baggage keys from propagated message headers) is NOT implemented here —
          it is a future 07.Messaging-owned follow-up, outside this domain's jurisdiction to dispatch.

TEST-SUITE CORRECTION (WO-042/P-261, landed 2026-07-15):
    BaggageLogRecordProcessorTests and AmbientLoggingEnrichmentAcceptanceTests previously simulated
    14.Presentation's CorrelationId enrichment using a standalone "CorrelationId" literal. Ground-truth
    verification against 14.Presentation.CorrelationIdMiddleware.BaggageKey found this literal never
    matched what production code actually writes ("correlation.id") — a confirmed defect (flagged as
    DO-07 in 14.Presentation/CLAUDE.md's WO-041 changelog), not a false alarm. Both test files are now
    retrofitted to assert against 01.Core's SharedKernel.Primitives.Propagation.WellKnownBaggageKeys.
    CorrelationId ("correlation.id") instead — proving the processor is exercised against the actual
    key CorrelationIdMiddleware writes, not an arbitrary stand-in string. BaggageLogRecordProcessor
    itself is unchanged and remains genuinely generic — only the test suite's assumed key was wrong.
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
    .StrategyName                                                 → string  (get-only)
        (WO-028/P-175, C-20). The explicit, type-safe resolution-order key this strategy is
        identified by — replaces the prior s.GetType().Name reflection lookup. A custom strategy
        registered by a consuming service declares its own StrategyName and becomes reachable from
        TenantResolutionOptions.StrategyOrder purely by that declared value, independent of the
        implementing class's name.
    .TryResolveAsync(HttpContext context, CancellationToken ct) → Task<Guid?>
    NOTE: Returns null when this strategy cannot resolve a tenant from the given request — the
          composing resolver then tries the next strategy in TenantResolutionOptions.StrategyOrder.
          Must never throw for an absent/not-found tenant signal; reserve exceptions for genuinely
          exceptional conditions, not the expected "this strategy doesn't apply to this request" case.

TenantResolutionStrategyNames  (static class, string constants — WO-028/P-175, C-21)
    .Header = "Header"   .Claim = "Claim"   .Database = "Database"
    NOTE: Mirrors the HealthCheckTags constants-class pattern. The three platform strategies'
          StrategyName values and TenantResolutionOptions.StrategyOrder's default array both
          reference these constants — zero duplicated bare string literals. A custom
          fourth-strategy implementation is free to use its own string literal for StrategyName;
          this constants class only covers the three platform-shipped strategies.

HeaderTenantResolutionStrategy  (sealed class, implements ITenantResolutionStrategy)
    constructor: HeaderTenantResolutionStrategy(string headerName = HeaderTenantResolutionStrategy.DefaultHeaderName)
    .StrategyName                                                 → TenantResolutionStrategyNames.Header
    NOTE: Resolves TenantId from the named HTTP request header; Guid.TryParse — returns null when
          the header is absent or its value does not parse. Intended for B2B / API-key clients that
          have no tenant claim embedded in their access token. DefaultHeaderName is sourced from
          01.Core's SharedKernel.Primitives.Propagation.WellKnownHeaders.TenantId (WO-042/P-261,
          landed 2026-07-15) — NOT an independently-declared literal.
          SUPERSEDES: WO-028/P-177/C-26 originally promoted "X-Tenant-Id" to a local
          DefaultHeaderName constant inside this package; WO-042 relocated the value's authoritative
          source to 01.Core's shared cross-domain propagation-constant registry so this header name
          cannot drift independently from the identical literal redeclared in
          11.Communication.Rest.TenantIdDelegatingHandler and 11.Communication.Grpc.TenantIdInterceptor.
          The literal value itself is unchanged ("X-Tenant-Id") — this was a source-of-truth
          relocation, not a behavior change. SharedKernel.MultiTenancy.csproj carries a new
          ProjectReference to SharedKernel.Primitives to support this.

ClaimTenantResolutionStrategy  (sealed class, implements ITenantResolutionStrategy)
    .StrategyName                                                 → TenantResolutionStrategyNames.Claim
    NOTE: Thin adapter that delegates to SharedKernel.Security.Oidc.OidcTenantProvider when an
          authenticated ClaimsPrincipal carries the SecurityClaimTypes.TenantId claim
          (12.Security.Abstractions). Must NOT duplicate claim-parsing logic — any change to claim
          resolution happens once, in 12.Security.Oidc.

DatabaseTenantResolutionStrategy  (sealed class, implements ITenantResolutionStrategy)
    .StrategyName                                                 → TenantResolutionStrategyNames.Database
    NOTE: For DB-per-tenant / schema-per-tenant isolation models. Resolves tenant identity from a
          tenant-directory lookup keyed by request host or subdomain (e.g.
          "acme.api.example.com" → TenantId) via IDbConnectionFactory (06.Persistence.Abstractions)
          using a parameterized query — string interpolation into SQL is a hard violation (SK0xxx).
          Distinct from 06.Persistence's TenantedDbContext: that applies a row-level filter once the
          tenant is already known; this strategy answers the prior question of "which tenant is
          this request for" in isolation models where the tenant is not visible in a header or claim.
    ASYNC NOTE (WO-028/P-176, C-23): TryResolveAsync uses the asynchronous ADO.NET path
          (DbCommand.ExecuteScalarAsync(CancellationToken) or equivalent), with the supplied
          CancellationToken threaded through to the actual database call. Corrects a prior
          sync-over-async defect where the method's async/CancellationToken signature was not
          honored by its implementation (IDbCommand.ExecuteScalar() blocked a thread-pool thread
          and ignored the token entirely).

TenantResolutionOptions  (options POCO, section "SharedKernel:MultiTenancy")
    .StrategyOrder                                                → IReadOnlyList<string>
        Default: [TenantResolutionStrategyNames.Header, TenantResolutionStrategyNames.Claim,
        TenantResolutionStrategyNames.Database]. The first strategy in this order whose
        TryResolveAsync returns non-null wins, matched against each registered strategy's
        StrategyName (not its CLR type name). A service with no tenant directory database simply
        omits "Database" from the configured order — no code change, no null-reference risk. A
        consuming service's custom ITenantResolutionStrategy is reachable by adding its declared
        StrategyName to this list.
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
    LOOKUP NOTE (WO-028/P-175, C-22): Matches each StrategyOrder entry against each registered
          strategy's declared StrategyName (ITenantResolutionStrategy.StrategyName) — never against
          s.GetType().Name. The StrategyName → ITenantResolutionStrategy mapping is computed once
          against the fixed, DI-registered strategy set (which does not change for the process
          lifetime) rather than rebuilt as a fresh Dictionary allocation on every InvokeAsync call;
          the per-request cost is limited to iterating StrategyOrder against that already-available
          mapping.
    LOGGING ENRICHMENT NOTE (WO-041/P-251, C-33): Immediately after determining the winning
          TenantId (or confirming Guid.Empty), also calls
          Activity.Current?.SetBaggage(TenantBaggageKeys.TenantId, tenantId.ToString()). This makes
          TenantId ambient to every log record produced for the remainder of the request via
          SharedKernel.ServiceDefaults's BaggageLogRecordProcessor — no call site anywhere in the
          request needs to pass TenantId as an explicit log template placeholder. The baggage value
          is set even when TenantId is Guid.Empty, so log aggregation can distinguish "no tenant
          resolved for this request" from "TenantId enrichment was never wired" (an explicit
          sentinel value vs. a genuinely absent baggage key). Scoped to the HTTP-request path only —
          see BaggageLogRecordProcessor's SCOPE NOTE above.

TenantBaggageKeys  (static class, string constants — WO-041/P-251)
    .TenantId = "TenantId"
    NOTE: Mirrors the TenantResolutionStrategyNames/HealthCheckNames constants-class pattern.
          TenantResolutionMiddleware's Activity.SetBaggage call references this constant — zero
          bare string literals for the baggage key anywhere in SharedKernel.MultiTenancy.
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

- **Phase build order:** Scaffold (real project/package references replacing the bare `.csproj` stubs) must land before any Core work. Within Core, the foundation (`AddServiceDefaults`, `AddSharedKernelTelemetry`, `AddSharedKernelHealthChecks` + the live/ready split, `StartupGate`/`StartupGateHealthCheck`, and the full `SharedKernel.MultiTenancy` surface) must land before any dependency-specific extension (`AddRedisHealthCheck`, `AddCacheReadinessCheck`, `WithCachingTelemetry`, `AddRabbitMqMessagingHealthCheck`, `AddAzureServiceBusMessagingHealthCheck`, `WithMessagingTelemetry`) — every dependency-specific check extends the `IHealthChecksBuilder` returned by `AddSharedKernelHealthChecks()`, so that base must exist first. `WithMessagingTelemetry()` additionally depended on `07.Messaging`'s P-172 (the `"SharedKernel.Messaging"` `ActivitySource` definition) — this landed (`SK.07.OTel` 8/8 `●`) and `WithMessagingTelemetry()` is now implemented (C-19, `SK.13.Core` 28/28 `●`); the cross-domain gate is fully resolved and requires no further check by future agents. (WO-028) The `ITenantResolutionStrategy.StrategyName` contract member and `TenantResolutionStrategyNames` constants class must land before the cached-lookup refactor in `TenantResolutionMiddleware`, since the cache is keyed by `StrategyName` — implement in the order `StrategyName` contract → constants class → three platform strategies updated → middleware lookup refactor.
- The liveness/readiness tag split is the central invariant of this domain: any check that depends on an external system (database, cache, message broker) is tagged `"ready"` and **never** `"live"`. The `"/health/live"` endpoint must answer only "is this process alive" — never "are this process's dependencies alive."
- Every dependency-specific health check (`AddRedisHealthCheck`, `AddRabbitMqMessagingHealthCheck`, `AddAzureServiceBusMessagingHealthCheck`, `AddDatabaseReadinessCheck<TContext>`, `AddDapperDatabaseReadinessCheck`, `AddCacheReadinessCheck`) is an explicit opt-in call on `IHealthChecksBuilder`. `AddServiceDefaults()` and `AddSharedKernelHealthChecks()` register **only** the base endpoint mappings and `StartupGateHealthCheck` — never a dependency-specific check.
- `AddCacheReadinessCheck` must report `Degraded`, not `Unhealthy`, on a cache probe failure — FusionCache's L1 fail-safe may still be correctly serving stale data, and an `Unhealthy` readiness result removes the pod from rotation unnecessarily during a transient Redis blip.
- Messaging and cache health checks must read their connection details from the already-resolved options objects (`RabbitMqBusOptions`, `AzureServiceBusOptions`, the registered `ICacheService`/Redis connection) — never require the caller to duplicate a connection string or URI in the `Add*HealthCheck` call site beyond what is unavoidable for opt-in registration.
- `WithMessagingTelemetry()`, `WithCachingTelemetry()`, and `WithApplicationTelemetry()` must be idempotent — calling any of the three more than once must not register duplicate `ActivitySource`/meter instruments.
- `13.ServiceDefaults` never *creates* an `ActivitySource` or custom meter on behalf of another domain — `07.Messaging`'s `"SharedKernel.Messaging"` source, `02.Caching`'s `"SharedKernel.Caching"` meter, and `05.Application`'s `"SharedKernel.Application"` source/meter pair are created in their own domains; this package only wires already-existing sources/meters into the host's `TracerProvider`/`MeterProvider`. Every such wiring extension follows the identical shape: string-name-only `AddSource`/`AddMeter` calls, no `ProjectReference` to the owning domain's concrete assembly when its diagnostics class is `internal` (the common case), and idempotent by construction because the underlying OTel SDK no-ops on a repeated source/meter name.
- `ClaimTenantResolutionStrategy` must delegate to `SharedKernel.Security.Oidc.OidcTenantProvider` — it must never reimplement claim-name parsing or duplicate `SecurityClaimTypes.TenantId` resolution logic.
- `DatabaseTenantResolutionStrategy` must use parameterized queries exclusively — building SQL by string interpolation or concatenation with request-derived values (host, subdomain) is a hard violation.
- `TenantResolutionMiddleware` must be registered after `UseAuthentication()` in the request pipeline — `ClaimTenantResolutionStrategy` requires a populated `ClaimsPrincipal`.
- `AmbientTenantProvider.TenantId` defaults to `Guid.Empty` and is set exactly once per request by `TenantResolutionMiddleware` — no other component may set it.
- `13.ServiceDefaults` may reference concrete provider packages from `02.Caching`, `06.Persistence`, `07.Messaging`, and `12.Security` (see Layering exception above) — but must never reference `14.Presentation`, `15.Integration`, `16.Testing`, or `17.Workflows`. The dependency direction remains strictly downward (`13` may reference `01`–`12` only).
- No static mutable state anywhere in this domain except `StartupGate`, which is an intentional, narrowly-scoped, thread-safe (`volatile`) singleton gate — not a general-purpose static cache.
- `SharedKernel.ServiceDefaults.csproj` requires `<FrameworkReference Include="Microsoft.AspNetCore.App" />`. The project is a plain `Microsoft.NET.Sdk` class library (not `Microsoft.NET.Sdk.Web`), but `MapHealthChecks`/`HealthCheckOptions`/`IEndpointRouteBuilder` (used by `MapDefaultHealthCheckEndpoints`) come from the ASP.NET Core shared framework, which is otherwise unresolvable from a plain class library.
- **`ITenantResolutionStrategy` identification is explicit-contract-based, never reflection-based** (WO-028/P-175). Each strategy declares its own `StrategyName`; `TenantResolutionMiddleware` matches `TenantResolutionOptions.StrategyOrder` entries against that declared value. `s.GetType().Name`-based switching is a hard violation — it silently breaks the package's one explicit extensibility point (a consuming service registering a custom fourth strategy) with no compiler error and no runtime signal. This supersedes the prior version of this rule, which documented the type-name switch as an accepted test limitation rather than a defect — that was incorrect; it has been fixed, not merely worked around.
- **The `StrategyName → ITenantResolutionStrategy` lookup is computed once, never rebuilt per request.** The strategy set registered via DI is fixed for the process lifetime; `TenantResolutionMiddleware.InvokeAsync` must not allocate a fresh `Dictionary` keyed by strategy name on every HTTP request. This is a hot-path package — the per-request cost is limited to iterating `StrategyOrder` against an already-available mapping.
- **`DatabaseTenantResolutionStrategy.TryResolveAsync` must use a genuinely asynchronous database call with the supplied `CancellationToken` actually threaded through** (WO-028/P-176). A method whose signature is `async Task<Guid?>(..., CancellationToken)` must not block a thread-pool thread via a synchronous ADO.NET call (`IDbCommand.ExecuteScalar()`) nor silently ignore the cancellation token — both are hard violations in a package that runs in every multi-tenant microservice's request hot path.
- Every default health-check **registration name** (not just tags) is a named constant from `HealthCheckNames` (WO-028/P-177) — mirroring the `HealthCheckTags` constants-class pattern already established for tags. No `Add*HealthCheck` method's `name` parameter default may be a bare string literal.
- **`AddSharedKernelTelemetry` exports logs through the same OTLP pipeline as traces and metrics** (WO-041/P-251) — `.WithLogging(...)` with `IncludeScopes = true` and `IncludeFormattedMessage = true`, reading the OTLP endpoint from the same standard env vars. This is additive behavior inside the existing method signature, not a new public extension method.
- **`BaggageLogRecordProcessor` is the platform's single, generic mechanism for making `Activity` baggage ambient to every log record** (WO-041/P-251). It must never hardcode a specific baggage key name (no `"CorrelationId"`, no `"TenantId"` string literal inside the processor itself) — its entire value is that it works uniformly for any domain that sets `Activity` baggage, without `13.ServiceDefaults` needing to know that domain's concept by name. An explicit `LogRecord.Attributes` entry already present at a given key must never be overwritten by a baggage value at the same key.
- **`TenantResolutionMiddleware` sets `TenantId` as `Activity` baggage (`TenantBaggageKeys.TenantId`), in addition to `AmbientTenantProvider.TenantId`** (WO-041/P-251, C-33) — this is what makes TenantId ambient to logs via `BaggageLogRecordProcessor`. The baggage value is set unconditionally, including for the `Guid.Empty` no-tenant sentinel — never skip setting it just because no tenant resolved, since an absent key and an explicit empty-sentinel value carry different diagnostic meaning.
- **Cross-service propagation identifiers (HTTP/gRPC header names, `Activity` baggage keys) must be sourced from `01.Core`'s `SharedKernel.Primitives.Propagation.WellKnownHeaders`/`WellKnownBaggageKeys` — never redeclared as an independent local literal or constant** (WO-042/P-261, landed 2026-07-15). This domain previously carried two such redeclarations: `HeaderTenantResolutionStrategy.DefaultHeaderName`'s own `"X-Tenant-Id"` constant (superseded — now sourced from `WellKnownHeaders.TenantId`, same value, source-of-truth relocation only) and this package's own test suite's standalone `"CorrelationId"` baggage-key literal (a genuine defect — it never matched `14.Presentation.CorrelationIdMiddleware.BaggageKey`'s actual `"correlation.id"` value; corrected to reference `WellKnownBaggageKeys.CorrelationId`). Both fixes are shipped and tested — see this file's Changelog.
- **`AddStorageReadinessCheck` (WO-043/P-270, implemented) must never reference a concrete storage provider options type** (`S3StorageOptions`, `ObsStorageOptions`) — it resolves only `IFileStorage` (`SharedKernel.Storage.Abstractions`) and takes `bucket` as an explicit caller-supplied parameter, exactly the same way `AddRedisHealthCheck` takes an explicit connection string rather than reaching into a concrete Redis options type it doesn't own. This is what lets one adapter work unmodified against either `.S3` or `.Obs`.
- **The ambient-logging-enrichment mechanism (`BaggageLogRecordProcessor` + `TenantResolutionMiddleware`'s baggage set) is scoped to the HTTP-request path only.** A message-consumption-scope equivalent (tenant/correlation enrichment during MassTransit consumer execution) is explicitly out of this domain's jurisdiction — it would require a parallel mechanism inside `07.Messaging`'s consumer pipeline (e.g., an `IMessageHeaderPropagator`-adjacent filter setting the same `Activity` baggage keys from propagated message headers) and must be dispatched as a separate cross-domain work order if needed, never implemented here as a workaround.
- `13.ServiceDefaults` never takes a `ProjectReference` to `14.Presentation` to support the CorrelationId-on-logs acceptance criterion — `BaggageLogRecordProcessor` reads `Activity.Baggage` generically; verification uses a direct BCL `Activity.SetBaggage(...)` call simulating `14.Presentation`'s own documented mechanism (WO-031), never a real cross-domain reference.

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
builder.WithApplicationTelemetry();                    // optional — services using 05.Application's pipeline behaviors

var app = builder.Build();

app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>();       // required when AddSharedKernelMultiTenancy() is used — must run after UseAuthentication()

app.MapDefaultHealthCheckEndpoints();                  // "/health/live", "/health/ready"

app.Run();
```

`SharedKernel.MultiTenancy` ships no telemetry or health check wiring of its own — that composition stays in `SharedKernel.ServiceDefaults`.

**Log export + ambient enrichment (WO-041/P-251):** no additional call is required beyond `AddServiceDefaults()` (+ `AddSharedKernelMultiTenancy()` for TenantId enrichment) — OTLP log export, `IncludeScopes`/`IncludeFormattedMessage`, and the `BaggageLogRecordProcessor` wiring all happen automatically inside `AddSharedKernelTelemetry`. Application code never needs to pass `CorrelationId` or `TenantId` as an explicit log template placeholder.

---

## AOT Compatibility

- OpenTelemetry .NET SDK is largely AOT-compatible as of the versions targeted by `net10.0`; some exporter resource-detectors use reflection-based assembly metadata lookup — this is encapsulated entirely behind `AddSharedKernelTelemetry`/`AddServiceDefaults`, so the AOT blast radius does not reach consuming application code.
- `AspNetCore.HealthChecks.*` community packages vary in AOT readiness by transport; this is not a hard blocker per the repo's pragmatic AOT policy — each opt-in `Add*HealthCheck` method is documented individually as adopted rather than the whole package being declared AOT-unsafe.
- `ITenantResolutionStrategy` implementations (`HeaderTenantResolutionStrategy`, `ClaimTenantResolutionStrategy`, `DatabaseTenantResolutionStrategy`) are plain sealed classes using `Guid.TryParse`, a `StrategyName` get-only property, and parameterized async ADO.NET queries — AOT-safe. `TenantResolutionMiddleware`'s once-computed `StrategyName → ITenantResolutionStrategy` lookup is a plain `Dictionary<string, ITenantResolutionStrategy>` (or `FrozenDictionary`) built from already-resolved DI instances — no reflection involved in the lookup itself.
- `StartupGate` / `StartupGateHealthCheck` are plain classes with a `volatile bool` field — AOT-safe, no reflection.
- No `Activator.CreateInstance`, no `Assembly.Load`, no `MakeGenericMethod`/`Invoke` reflection anywhere in this domain.
- `BaggageLogRecordProcessor` (WO-041/P-251) is a plain sealed class over `System.Diagnostics.Activity.Baggage` (a BCL `IEnumerable<KeyValuePair<string,string?>>`) and `OpenTelemetry.Logs.LogRecord.Attributes` — no reflection, AOT-safe. `TenantBaggageKeys` is a static string-constants class, identical AOT profile to `HealthCheckNames`/`TenantResolutionStrategyNames`.
- `StorageReadinessHealthCheck` (WO-043/P-270, pending implementation) will be a plain sealed class over `IFileStorage`/`Result` (both BCL-primitive-shaped, no reflection) — same AOT profile as `DatabaseReadinessHealthCheck<TContext>`/`CacheReadinessHealthCheck`.

---

## Test Rules

- Unit tests for `SharedKernel.ServiceDefaults` live in `13.ServiceDefaults/SharedKernel.ServiceDefaults/SharedKernel.ServiceDefaults.Tests/`.
- Unit tests for `SharedKernel.MultiTenancy` live in `13.ServiceDefaults/SharedKernel.MultiTenancy/SharedKernel.MultiTenancy.Tests/`.
- Health check tag tests: every dependency-specific check (`Redis`, `RabbitMQ`, `AzureServiceBus`, `Database`, `Cache`, `Storage`) is registered with tag `"ready"` and never with tag `"live"` — assert against the registered `HealthCheckRegistration.Tags`.
- `AddStorageReadinessCheck` (WO-043/P-270, implemented): a forced `IFileStorage.CheckHealthAsync` failure (via a test-double `IFileStorage`, never a real S3/OBS/MinIO dependency in this domain's own unit tests) must report `HealthStatus.Unhealthy` — this check has no fail-safe-absorption layer in front of it, unlike `AddCacheReadinessCheck`, so it must **not** report `Degraded`.
- `AddCacheReadinessCheck`: a forced cache-probe failure must report `HealthStatus.Degraded`, never `HealthStatus.Unhealthy`.
- `StartupGateHealthCheck`: reports `Unhealthy` before `MarkReady()` is called; reports `Healthy` after; `MarkReady()` is idempotent (calling twice does not throw and does not toggle state back).
- `WithMessagingTelemetry()` / `WithCachingTelemetry()` / `WithApplicationTelemetry()`: calling each twice on the same builder registers exactly one instance of each `ActivitySource`/meter name (no duplicate-instrument assertion via the OTel SDK's exposed listener APIs).
- `HeaderTenantResolutionStrategy`: present + parseable header → resolved `Guid`; absent header → `null`; malformed header value → `null` (never throws).
- `ClaimTenantResolutionStrategy`: delegates correctly to a fake/mocked `OidcTenantProvider`-shaped dependency; never re-parses raw claims itself (verified by testing through the seam, not by reflection over private state).
- `DatabaseTenantResolutionStrategy`: resolves a known host/subdomain to the expected `TenantId` against a test double `IDbConnectionFactory`; unknown host → `null`; query parameterization verified (no string-built SQL in the executed command text); **async-call assertion (WO-028/P-176):** the test double's command surface must assert that the async ADO.NET path is invoked (not the synchronous `ExecuteScalar()`), and a separate test must prove a cancelled `CancellationToken` actually cancels the in-flight call rather than being silently ignored.
- `TenantResolutionOptions.StrategyOrder`: default order is `[TenantResolutionStrategyNames.Header, TenantResolutionStrategyNames.Claim, TenantResolutionStrategyNames.Database]`; first non-null strategy result wins; omitting a strategy's `StrategyName` from the order means it is never invoked.
- `TenantResolutionMiddleware`: sets `AmbientTenantProvider.TenantId` from the first resolving strategy; no strategy resolves → `TenantId` remains `Guid.Empty`; middleware does not throw when zero strategies are configured. Strategy-ordering/omission tests use **real, named, registered strategies** (platform strategies or a purpose-built test strategy with a declared `StrategyName`) — never a bare `NSubstitute.For<ITenantResolutionStrategy>()` proxy with no `StrategyName` override, since that proves nothing about the omission logic itself (it would be "omitted" from any `StrategyOrder` regardless of configuration, which is a different, weaker claim). The "omitted-from-order is never invoked" test must fail if the omission logic breaks, not merely because the test double is structurally unreachable.
- `HealthCheckNames` constants: every `Add*HealthCheck` default `name` parameter resolves to the corresponding `HealthCheckNames` constant value — assert via the registered `HealthCheckRegistration.Name`.
- `AddDatabaseReadinessCheck<TContext>` / `AddDapperDatabaseReadinessCheck`: tagged `"ready"` + `"db"`, never `"live"`; `Healthy` when `DatabaseReadinessResult.IsHealthy == true`, `Unhealthy` otherwise; `Latency`/`Provider` present in `HealthCheckResult.Data`.
- `AmbientTenantProvider`: defaults to `Guid.Empty`; `TenantId` setter is `private` (verified via reflection, mirroring the equivalent test pattern used for `TenantedAggregateRoot<TId>.TenantId` in `03.Domain`).
- DI registration tests use `IServiceCollection` / `ServiceCollection` directly with `BuildServiceProvider()` for unit-level verification — no `WebApplicationFactory` required except for endpoint-mapping integration tests (`/health/live`, `/health/ready` return expected status codes and bodies).
- `/health/live`/`/health/ready` endpoint-mapping integration tests use `new HostBuilder().ConfigureWebHost(webHost => webHost.UseTestServer()...)` + `IHost.GetTestClient()` — not `WebApplicationFactory<TEntryPoint>`, since `SharedKernel.ServiceDefaults` is a class library with no `Program` marker type to target. This manual host setup does **not** implicitly register routing services the way `WebApplication.CreateBuilder()` does — an explicit `services.AddRouting()` call is required before `AddSharedKernelHealthChecks()` or `UseRouting()` throws `InvalidOperationException`.
- `DatabaseTenantResolutionStrategy` tests mock at the raw ADO.NET interface level (`IDbConnection`/`IDbCommand`/`IDbDataParameter` via NSubstitute) — `16.Testing` has no `IDbConnectionFactory` fake yet.
- `ClaimTenantResolutionStrategy` tests build a real `ClaimsPrincipal` carrying `SecurityClaimTypes.TenantId` rather than mocking `OidcTenantProvider` — it has no interface and is a concrete sealed class constructed from a `ClaimsPrincipal`, so delegation is verified end-to-end through the seam instead.
- `BaggageLogRecordProcessor`: baggage entries present on `Activity.Current` are copied onto `LogRecord.Attributes`; an existing explicit attribute at the same key is never overwritten; `Activity.Current == null` produces no exception and adds no attributes.
- `AddSharedKernelTelemetry` logging export: `OpenTelemetryLoggerOptions.IncludeScopes`/`IncludeFormattedMessage` are both asserted `true`; `BaggageLogRecordProcessor` is registered exactly once; the full pre-existing `AddSharedKernelTelemetry` tracing/metrics test suite is re-run as a regression gate (no assertions loosened or removed to accommodate the new logging wiring).
- `TenantResolutionMiddleware` + `TenantBaggageKeys`: a resolved `TenantId` appears on `Activity.Current.Baggage` under `TenantBaggageKeys.TenantId`; an unresolved request sets the baggage value to the `Guid.Empty` string rather than leaving the key absent; no exception when `Activity.Current` is `null`.
- **CorrelationId acceptance-criterion test (no `14.Presentation` reference):** a test sets `Activity.Current?.SetBaggage(WellKnownBaggageKeys.CorrelationId, ...)` directly via the BCL — the same mechanism `14.Presentation`'s middleware itself uses (WO-031), and now the same shared constant it uses too (WO-042/P-261, superseding the prior standalone `"CorrelationId"` literal) — and asserts the value appears in the `LogRecord.Attributes` produced through the wired `BaggageLogRecordProcessor`. A companion combined test asserts both a `TenantBaggageKeys.TenantId` entry (set via `TenantResolutionMiddleware`) and a simulated CorrelationId entry are simultaneously present on the same `LogRecord.Attributes` set without collision.
- **`WellKnownHeaders`/`WellKnownBaggageKeys` value-compatibility regression (WO-042/P-261):** after retrofitting `HeaderTenantResolutionStrategy.DefaultHeaderName` to source from `01.Core`'s `WellKnownHeaders.TenantId`, a test confirms the resolved value is still exactly `"X-Tenant-Id"` — proving the retrofit changed the constant's source, not its value. (`HeaderTenantResolutionStrategyTests.DefaultHeaderName_SourcedFromWellKnownHeaders_StillEqualsXTenantId`, landed 2026-07-15.)

---

## Changelog

> Maintained by `/sync-brain` and the servicedefaults-arch-planner domain agent. One line per significant change.

- [2026-06-19] Domain brain initialized — packages (`SharedKernel.ServiceDefaults`, `SharedKernel.MultiTenancy`), liveness/readiness health check split, OTel composition (`AddServiceDefaults`, `WithMessagingTelemetry`, `WithCachingTelemetry`), dependency-specific health check adapters wrapping `06.Persistence` probe primitives and `02.Caching`/`07.Messaging` connectivity, three-strategy multi-tenant resolution (Header/Claim/DB isolation) with `AmbientTenantProvider` + `TenantResolutionMiddleware`, implementation rules, AOT notes, test rules. Derived from the root `CLAUDE.md` folder map entry for `13.ServiceDefaults` and the pending root-level backlog items P-010 (WO-003, Redis health check + cache readiness probe + caching OTel metrics), P-122 (WO-020, RabbitMQ/Azure Service Bus messaging health checks), and P-132 (WO-021, messaging OTel wiring) — no dedicated servicedefaults-arch-planner agent exists yet, so this pass is an arch-lead-equivalent placeholder pending formal Design-phase task breakdown in `state-map.md` (arch-lead)
- [2026-06-19] Full Design→Published task breakdown landed in `state-map.md` for six root backlog items: P-169 (WO-027, Scaffold — real project/package references and folder structure for both packages, replacing the bare `.csproj` stubs; nested `.Tests` projects; `.slnx` registration); P-170 (WO-027, Core — `AddServiceDefaults`, `AddSharedKernelTelemetry`, `AddSharedKernelHealthChecks` with the live/ready endpoint split, `StartupGate`/`StartupGateHealthCheck`); P-171 (WO-027, Core — the full `SharedKernel.MultiTenancy` surface: `ITenantResolutionStrategy` + three concrete strategies, `TenantResolutionOptions`, `AmbientTenantProvider`, `TenantResolutionMiddleware`, `AddSharedKernelMultiTenancy`); P-010 (WO-003, `AddRedisHealthCheck`/`AddCacheReadinessCheck`/`WithCachingTelemetry`); P-122 (WO-020, `AddRabbitMqMessagingHealthCheck`/`AddAzureServiceBusMessagingHealthCheck`); P-132 (WO-021, `WithMessagingTelemetry`, scoped strictly to wiring the pre-existing `"MassTransit"`/`"SharedKernel.Messaging"` source names — the latter gated on `07.Messaging`'s P-172, confirmed still pending as of this pass). Phase ordering established: P-169 (Scaffold) must land before P-170/P-171 (Core foundation), which must land before P-010/P-122/P-132 (Core dependency-specific additions extending `AddSharedKernelHealthChecks()`'s `IHealthChecksBuilder` base). No new interface contracts beyond what was already documented in this file — this pass formalized existing design into concrete, numbered, sequenced tasks (servicedefaults-arch-planner)
- [2026-06-19] Design phase (`SK.13.Design`, D-01/D-02) confirmed and closed by the servicedefaults-phase-implementer. D-01: tag taxonomy/calibration for `AddRedisHealthCheck` (`"ready"`+`"redis"`+`"cache"`, opt-in) and `AddCacheReadinessCheck` (`"ready"`+`"cache"`, `Degraded` never `Unhealthy` on probe failure — FusionCache L1 fail-safe rationale) re-verified against the existing Interface Contracts / Implementation Rules sections of this file — no changes needed, design was already correctly specified. D-02: `WithCachingTelemetry()` meter-wiring contract re-verified directly against `02.Caching/SharedKernel.Caching.FusionCache/Implementations/FusionCacheService.cs` (ground truth) — found and corrected a discrepancy: the contract previously named a `cache.operation.duration` histogram, which does not exist in the actual `"SharedKernel.Caching"` meter; the real instrument set is `cache.hits`, `cache.misses`, `cache.errors`, `cache.evictions` (all `Counter<long>`) and `cache.factory.duration` (`Histogram<double>`, unit `"ms"`, factory-execution time on cache miss). Confirmed `WithCachingTelemetry()` wires the meter by name only (`AddMeter("SharedKernel.Caching")`) and never enumerates individual instruments, and that no new `Meter`/`ActivitySource` is created in `13.ServiceDefaults` — ownership stays entirely with `02.Caching`. No code implementation in this phase — Scaffold (S-01–S-10) has not landed, `.csproj` files remain bare stubs (servicedefaults-phase-implementer)
- [2026-06-19] SK.13.Core C-01–C-18 implemented and tested (18 ServiceDefaults + 23 MultiTenancy tests passing): `AddServiceDefaults`/`AddSharedKernelTelemetry`/`AddSharedKernelHealthChecks` + live/ready endpoint split, `StartupGate`/`StartupGateHealthCheck`, full `SharedKernel.MultiTenancy` surface (Header/Claim/Database strategies, `TenantResolutionOptions`, `AmbientTenantProvider`, `TenantResolutionMiddleware`, `AddSharedKernelMultiTenancy`), `AddRedisHealthCheck`, `AddCacheReadinessCheck`, `WithCachingTelemetry`, `AddRabbitMqMessagingHealthCheck`, `AddAzureServiceBusMessagingHealthCheck`. `SharedKernel.MultiTenancy` gained a new `ProjectReference` to `SharedKernel.Security.Oidc` (previously only `.Abstractions`) so `ClaimTenantResolutionStrategy` can construct `OidcTenantProvider(context.User)` directly per-request. C-19 (`WithMessagingTelemetry`) left `⚑` blocked in `state-map.md` — `07.Messaging`'s P-172 `ActivitySource("SharedKernel.Messaging","1.0.0")` confirmed still entirely `○` (`SK.07.OTel` phase, tasks OT-01–OT-08) as of this pass; cannot wire a source that does not yet exist in code (servicedefaults-phase-implementer)
- [2026-06-22] C-19 implemented — `WithMessagingTelemetry()` unblocked now that `07.Messaging`'s P-172 (`MessagingDiagnostics.ActivitySource`) landed (`SK.07.OTel` 8/8 `●`). `SK.13.Core` is now 28/28 `●`, fully complete. Implementation mirrors `WithCachingTelemetry()` exactly: registers `"MassTransit"` + `"SharedKernel.Messaging"` via `WithTracing(t => t.AddSource(...))` and `"MassTransit"` via `WithMetrics(m => m.AddMeter(...))`, all by string name only — confirmed no new `ProjectReference` was needed (`SharedKernel.ServiceDefaults.csproj` already referenced `SharedKernel.Messaging.MassTransit`, added earlier for C-16's RabbitMQ health check) and no direct reference to the internal `MessagingDiagnostics` type, which is `internal` to its own assembly and has no `InternalsVisibleTo` grant to `SharedKernel.ServiceDefaults` — string-name wiring is the only viable approach, consistent with the "13.ServiceDefaults never creates an ActivitySource/Meter on behalf of another domain" rule. Closed the documented T-13 test gap: added `Telemetry/MessagingTelemetryExtensionsTests.cs` (3 tests) alongside the pre-existing `CachingTelemetryExtensionsTests.cs`, giving T-13 full two-method idempotency coverage. 37/37 SharedKernel.ServiceDefaults.Tests passing (+3), 26/26 SharedKernel.MultiTenancy.Tests unchanged. Root `state-map.md` Phase Backlog entries P-010, P-122, P-132, P-175, P-176, P-177 closed to `●` Complete as part of this Core-completion propagation (servicedefaults-phase-implementer)
- [2026-06-19] WO-028 gold-standard-audit remediation planned (state-map tasks C-20–C-28, T-15–T-22; not yet implemented). Three root inputs processed: P-175 replaces `TenantResolutionMiddleware`'s `s.GetType().Name`-reflection strategy-name mapping with an explicit `ITenantResolutionStrategy.StrategyName` contract member backed by a new `TenantResolutionStrategyNames` constants class, and replaces the per-request fresh-`Dictionary` allocation with a lookup computed once against the fixed DI-registered strategy set — this is a **correction**, not a new feature: the prior version of this file documented the type-name switch as an accepted test limitation, which was wrong; it is a defect and has been re-classified as such. P-176 fixes `DatabaseTenantResolutionStrategy.TryResolveAsync`'s sync-over-async defect (blocking `IDbCommand.ExecuteScalar()` despite an `async`/`CancellationToken` signature; token was never threaded through) — replaced with the async ADO.NET path. P-177 adds a `HealthCheckNames` constants class (mirroring `HealthCheckTags`) consolidating five bare health-check name literals, promotes `AzureServiceBusHealthCheck`'s connection-string-detection substrings and `HeaderTenantResolutionStrategy`'s default header name to named constants, and implements `AddDatabaseReadinessCheck<TContext>`/`AddDapperDatabaseReadinessCheck` — both specified in this file's Interface Contracts section since the brain was written (P-150) but never actually implemented or tracked as Core tasks until now. Interface Contracts, Implementation Rules, AOT notes, and Test Rules sections all updated to reflect post-fix state (forward-looking, not a change log re-narration) (servicedefaults-arch-planner, WO-028)
- [2026-06-22] `SK.13.Docs` (DO-01, DO-02) closed — 2/2. XML doc audit across the full public surface of both packages found and fixed one gap (`AzureServiceBusHealthCheck`'s public constructor lacked a doc comment); confirmed `GenerateDocumentationFile=true` already set and clean Release builds produce 0 CS1591 warnings. `13.ServiceDefaults/README.md` (previously empty) populated with the `Program.cs` composition snippet and ordering rules, sourced from this file's existing "DI Registration (expected shape)" section. No interface, tag-taxonomy, or rule changes — pure documentation-completeness pass (servicedefaults-phase-implementer)
- [2026-07-08] WO-040/P-247 implemented: `WithApplicationTelemetry()` shipped in `Telemetry/ApplicationTelemetryExtensions.cs`, mirroring `WithMessagingTelemetry()`/`WithCachingTelemetry()` exactly — `AddSource("SharedKernel.Application")` + `AddMeter("SharedKernel.Application")`, string-name-only, no new `ProjectReference`. Added `Telemetry/ApplicationTelemetryExtensionsTests.cs` (3 tests, mirroring `MessagingTelemetryExtensionsTests.cs`). `SK.13.Core` 29/29 `●`, `SK.13.Tests` 23/23 `●`, `SK.13.Docs` 3/3 `●` — all promoted to root; Phase Backlog P-247 closed. README.md `Program.cs` snippet and ordering rule 4 updated to include `WithApplicationTelemetry()`. 40/40 SharedKernel.ServiceDefaults.Tests passing (26/26 MultiTenancy unchanged). Closing the `05.Application/CLAUDE.md` side of the forward-reference (P-247's last acceptance criterion) remains out of this domain's jurisdiction — flagged for `application-arch-planner` (servicedefaults-phase-implementer)
- [2026-07-07] WO-040/P-247: `WithApplicationTelemetry(this IHostApplicationBuilder)` contract added to the OpenTelemetry wiring section — third sibling alongside `WithMessagingTelemetry()`/`WithCachingTelemetry()`, wiring `05.Application.Behaviors`'s pre-existing `"SharedKernel.Application"` `ActivitySource`/`Meter` pair (`ApplicationDiagnostics`, WO-035/WO-036, `internal`) into the host `TracerProvider`/`MeterProvider` by string name only, same idempotency contract, no new `ProjectReference`. Closes `05.Application.Behaviors`' documented forward reference to this domain ("13.ServiceDefaults (future work, out of scope here) registers the 'SharedKernel.Application' meter/source name..."). Implementation Rules, DI Registration shape, and Test Rules sections updated to fold the new method into the existing three-way OTel-wiring pattern description. State-map tasks added: C-29 (`SK.13.Core`), T-23 (`SK.13.Tests`), DO-03 (`SK.13.Docs`) — all `○` pending implementation. **Scope note:** the `05.Application/CLAUDE.md` side of this forward-reference closeout is outside this domain's jurisdiction (`13.ServiceDefaults`' planner may only write inside `13.ServiceDefaults/`) — flagged in the state-map for the `application-arch-planner` agent to close on the `05.Application` side (servicedefaults-arch-planner, WO-040)
- [2026-07-09] WO-041/P-251: OpenTelemetry log export + ambient Correlation/Tenant enrichment phase added. `AddSharedKernelTelemetry` gains a `.WithLogging(...)` registration — additive inside its existing signature, no new public method — enabling OTLP log export (same standard env vars as tracing/metrics) with `IncludeScopes`/`IncludeFormattedMessage` both `true`. New `BaggageLogRecordProcessor` (`Telemetry/`, sealed `BaseProcessor<LogRecord>`) generically copies `Activity.Current?.Baggage` onto `LogRecord.Attributes` (never overwriting an explicit attribute, no hardcoded key names) — this is the single mechanism that surfaces both `14.Presentation`'s pre-existing CorrelationId `Activity`-baggage convention (WO-031) and a new `SharedKernel.MultiTenancy` TenantId baggage entry onto every log record, with zero `13.ServiceDefaults` → `14.Presentation` reference. `SharedKernel.MultiTenancy`'s `TenantResolutionMiddleware` gains a matching `Activity.Current?.SetBaggage(TenantBaggageKeys.TenantId, ...)` call (new `TenantBaggageKeys` constants class) alongside its existing `AmbientTenantProvider.TenantId` assignment — set unconditionally, including for the `Guid.Empty` no-tenant sentinel, so log aggregation can distinguish "no tenant resolved" from "enrichment never wired." Interface Contracts, Implementation Rules, AOT Compatibility, Test Rules, and DI Registration sections all updated. Explicit scope boundary documented: this mechanism covers the HTTP-request path only — a message-consumption-scope equivalent is a future `07.Messaging`-owned follow-up, outside this domain's jurisdiction to dispatch, not implemented here as a workaround. State-map tasks added: D-03–D-05 (`SK.13.Design`), S-11 (`SK.13.Scaffold`), C-30–C-33 (`SK.13.Core`), T-24–T-28 (`SK.13.Tests`), DO-04 (`SK.13.Docs`) — all `○` pending implementation; no new Published task (additive internals only, mirrors the WithApplicationTelemetry/WO-040 precedent) (servicedefaults-arch-planner, WO-041)
- [2026-07-09] WO-041/P-251 implemented and tested — `SK.13.Core` (C-30–C-33), `SK.13.Tests` (T-24–T-28), and `SK.13.Docs` (DO-04) all `●`; `13.ServiceDefaults` domain remains fully Published end to end (no new Published task, per the WithApplicationTelemetry/WO-040 precedent). `BaggageLogRecordProcessorTests`/`TelemetryExtensionsTests`/`AmbientLoggingEnrichmentAcceptanceTests` established the test pattern for this class of processor: build a real `LoggerFactory.Create(b => b.AddOpenTelemetry(o => o.AddProcessor(new BaggageLogRecordProcessor()).AddProcessor(new CapturingProcessor(...))))`, where the second custom `BaseProcessor<LogRecord>` copies `LogRecord.Attributes` into a side list at `OnEnd` time rather than retaining the `LogRecord` reference itself (the OTel SDK may pool/reset it after the pipeline completes). `AmbientLoggingEnrichmentAcceptanceTests` (T-27/T-28) took a **test-only** `ProjectReference` from `SharedKernel.ServiceDefaults.Tests` to the sibling `SharedKernel.MultiTenancy.csproj` — acceptable because both packages share this domain and the reference is test-only, never shipped — specifically to run the real `TenantResolutionMiddleware` end-to-end alongside a BCL-simulated `CorrelationId` baggage entry, proving the two ambient-enrichment sources compose without collision. `OpenTelemetry.Extensions.Hosting` confirmed already pinned to 1.16.0 — no version bump needed for `WithLogging(...)`. Test counts: 48/48 `SharedKernel.ServiceDefaults.Tests` (+8), 29/29 `SharedKernel.MultiTenancy.Tests` (+3) (servicedefaults-phase-implementer)
- [2026-07-14] WO-042/P-261 phase added — retrofit this domain to consume `01.Core`'s `WellKnownHeaders`/`WellKnownBaggageKeys` (`SK.01.P259`), fixing the confirmed live baggage-key mismatch flagged as DO-07 in `14.Presentation/CLAUDE.md`'s WO-041 changelog. Ground-truth verified: `14.Presentation.CorrelationIdMiddleware.BaggageKey = "correlation.id"`, while this domain's own `BaggageLogRecordProcessorTests`/`AmbientLoggingEnrichmentAcceptanceTests` independently hardcode `"CorrelationId"` — the two literals never matched, so the test suite was proving the processor round-trips an arbitrary key, never the actual production key. `HeaderTenantResolutionStrategy.DefaultHeaderName`'s retrofit (source relocated to `WellKnownHeaders.TenantId`) is, by contrast, a zero-behavior-change relocation — the value `"X-Tenant-Id"` was already correct. **Discrepancy flagged:** the dispatched phase input's premise that P-259 is "already implemented in 01.Core" does not match `01.Core/state-map.md` ground truth as of this pass — `SK.01.P259` shows D-30 (design) `●` but C-43 (implementation)/T-34/DO-16 all `○`. This domain's tasks (state-map `D-06`/`S-12`/`C-34`/`C-35`/`T-29`–`T-31`/`DO-05`) are recorded and design-locked now so implementation is unblocked the instant `01.Core` ships C-43; `S-12` (new `SharedKernel.MultiTenancy` → `SharedKernel.Primitives` `ProjectReference`) is not blocked and can land immediately. Interface Contracts (`HeaderTenantResolutionStrategy`, `BaggageLogRecordProcessor`), Implementation Rules, and Test Rules sections updated to describe the post-fix target state. No new Published task. **Out of this domain's jurisdiction:** closing `14.Presentation/CLAUDE.md`'s DO-07 changelog cross-reference requires the `presentation-arch-planner` agent — flagged for `arch-lead` to dispatch, mirroring the WO-040/P-247 precedent (servicedefaults-arch-planner, WO-042)
- [2026-07-15] WO-042/P-261 unblocked and closed — `01.Core`'s `SK.01.P259` (`WellKnownHeaders`/`WellKnownBaggageKeys`, C-43/T-34/DO-16) confirmed landed 2026-07-14. Implemented: `SharedKernel.MultiTenancy.csproj` gained a new `ProjectReference` to `SharedKernel.Primitives` (S-12); `HeaderTenantResolutionStrategy.DefaultHeaderName` now `= WellKnownHeaders.TenantId` (C-34, zero-behavior-change relocation, value unchanged at `"X-Tenant-Id"`); `BaggageLogRecordProcessorTests`/`AmbientLoggingEnrichmentAcceptanceTests` now assert against `WellKnownBaggageKeys.CorrelationId` instead of the previously-mismatched standalone `"CorrelationId"` literal (C-35, closes DO-07); new regression test `HeaderTenantResolutionStrategyTests.DefaultHeaderName_SourcedFromWellKnownHeaders_StillEqualsXTenantId` proves source relocation without value change (T-31). Interface Contracts, Implementation Rules, and Test Rules "pending" language all updated to reflect shipped state. 48/48 `SharedKernel.ServiceDefaults.Tests` + 30/30 `SharedKernel.MultiTenancy.Tests` passing (+1 new). All 6 `SK.13.*` phase keys fully `●` again. **Still out of this domain's jurisdiction:** `14.Presentation/CLAUDE.md`'s DO-07 changelog cross-reference closure remains tracked under root `P-262` (`14.Presentation` domain, already dispatched) — this file's changelog above already records that hand-off correctly (servicedefaults-phase-implementer, WO-042)
- [2026-07-16] WO-043/P-270 phase added — Storage Readiness Health Check Adapter, mirroring the `06.Persistence`/`13.ServiceDefaults` DB-readiness-probe split (root `CLAUDE.md`) applied to `08.Storage`. New `AddStorageReadinessCheck(this IHealthChecksBuilder, string bucket, string name = HealthCheckNames.Storage)` design-locked: wraps a new `StorageReadinessHealthCheck` around `08.Storage`'s `IFileStorage.CheckHealthAsync(bucket, ct) → Task<Result>` (P-265); resolves only `IFileStorage` (never a concrete `S3StorageOptions`/`ObsStorageOptions` type) so one adapter works against either `.S3` or `.Obs` unmodified; `bucket` is a required explicit parameter for the same reason; calibrated `Unhealthy` on failure (no fail-safe-absorption layer sits in front of raw storage connectivity, unlike `AddCacheReadinessCheck`'s `Degraded`); new `HealthCheckTags.Storage`/`HealthCheckNames.Storage` constants. **Dependency verified against ground truth, not assumed:** `08.Storage/state-map.md` shows `SK.08.Design` 0/15 `○` and `SK.08.Core` 0/30 `○` as of this pass — nothing in `08.Storage` has been implemented yet, only planned (P-265/P-266/P-267 exist solely as a dispatched task breakdown). Interface Contracts, Implementation Rules, AOT Compatibility, and Test Rules sections all updated with the new contract, explicitly marked pending/blocked rather than presented as shipped. State-map tasks added: D-07 (`SK.13.Design`, `●` — locked now, since the signature is already ratified in `08.Storage/CLAUDE.md`'s prose even though no code exists), S-13 (`SK.13.Scaffold`, `○` — unblocked, the target `.csproj` already exists as a registered bare stub), C-36/T-32/DO-06 (`SK.13.Core`/`SK.13.Tests`/`SK.13.Docs`, all `⚑` Blocked pending `08.Storage`'s Core Abstractions sub-phase). No new Published task (mirrors the `AddDatabaseReadinessCheck<TContext>`/WO-028 precedent — additive extension method to an already-published package) (servicedefaults-arch-planner, WO-043, P-270)
- [2026-07-18] WO-043/P-270 unblocked and closed — re-verified `08.Storage`'s blocker directly against `08.Storage/state-map.md` and the compiled `SharedKernel.Storage.Abstractions` source (not taken on trust): `08.Storage` had since reached `Published` end to end (all six phases `●`), and `IFileStorage.CheckHealthAsync(string bucket, CancellationToken) → Task<Result>` exists exactly per the D-07 contract already locked here. S-13 landed (new `ProjectReference` from `SharedKernel.ServiceDefaults.csproj` to `SharedKernel.Storage.Abstractions.csproj`); C-36 landed (`StorageReadinessHealthCheck` + `StorageReadinessHealthCheckExtensions.AddStorageReadinessCheck`, `HealthCheckTags.Storage`/`HealthCheckNames.Storage` constants) — zero deviation from the contract already documented above, this was a pure "implement exactly what was already designed" pass once the blocker cleared, not a redesign. T-32 landed (`StorageReadinessHealthCheckTests` — Healthy/Unhealthy-never-Degraded via a substituted `IFileStorage`, plus tag/name registration assertions folded into the existing `HealthCheckTagTests`/`HealthCheckNamesTests`). DO-06 landed (XML docs; `README.md`'s `Program.cs` snippet gained `.AddStorageReadinessCheck("my-bucket")`). Interface Contracts, Implementation Rules, Test Rules, and the `Packages` table's `SharedKernel.ServiceDefaults` row all updated to replace "design-locked/blocked/pending" language with "implemented". All 6 `SK.13.*` phase keys fully `●` again. 52/52 `SharedKernel.ServiceDefaults.Tests` (+4) + 30/30 `SharedKernel.MultiTenancy.Tests` (unchanged) passing. No new Published task (mirrors the `AddDatabaseReadinessCheck<TContext>`/WO-028 and `WithApplicationTelemetry`/WO-040 precedents) (servicedefaults-phase-implementer)
