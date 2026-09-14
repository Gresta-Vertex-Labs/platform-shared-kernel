# 13.ServiceDefaults — Host Composition & Cross-Cutting Brain

## What This Domain Is

The host composition layer. This is the only capability domain in the platform whose packages are wired directly into a microservice's `Program.cs` as the **first lines of startup** — OpenTelemetry, health checks, startup/liveness/readiness probes, and concrete multi-tenant resolution strategies. Everything here is composition glue: it assembles abstractions and concrete providers from layers `01`–`12` into ready-to-call extension methods. No business logic, no domain types, no new abstractions are defined here — only wiring.

Philosophy: **Composition-only. Opt-in by default. Liveness ≠ Readiness. No business logic.**

> **Layering exception:** `13.ServiceDefaults` is the **only** domain in the platform permitted to reference concrete infrastructure provider packages directly (`SharedKernel.Caching.Redis*`, `SharedKernel.Messaging.MassTransit`, `SharedKernel.Persistence.EfCore`/`.PostgreSQL`/`.Dapper`, `SharedKernel.Security.Oidc`) in addition to their abstractions. Every other domain (`05.Application`, `03.Domain`, etc.) must depend only on `.Abstractions` packages. This exception exists because `13.ServiceDefaults` *is* the composition root — health checks and telemetry wiring are inherently provider-specific (a Redis health check needs to know about Redis). This is mechanically enforced by `00.Governance`'s `SharedKernelLayeringRules` (Rule 1: no production assembly other than the concrete provider packages and `13.ServiceDefaults` may reference a concrete `02.Caching` provider — the same pattern applies to `07.Messaging` transports).

---

## Packages

| Package | Role | References |
|---------|------|-----------|
| `SharedKernel.ServiceDefaults` | **Composition base (WO-084).** `AddServiceDefaults()`; OpenTelemetry traces, metrics, and logs, including OTLP log export and `BaggageLogRecordProcessor`'s ambient `TenantId`/`CorrelationId` enrichment; `StartupGate` + `StartupGateHealthCheck`; `MapDefaultHealthCheckEndpoints`; `HealthCheckNames`/`HealthCheckTags`; the public `HealthCheckRegistrationLogging` extension point; every `WithXTelemetry()` (all wire by string name); `AddSharedKernelRateLimiting()` | **No SharedKernel package.** OpenTelemetry packages only, locked by `CompositionBaseIsolationTests` |
| `SharedKernel.ServiceDefaults.Persistence` | `AddDatabaseReadinessCheck<TContext>()`, `AddDapperDatabaseReadinessCheck()` | base, `Persistence.Abstractions`, `Persistence.EfCore` |
| `SharedKernel.ServiceDefaults.Caching` | `AddCacheReadinessCheck()` (reports `Degraded`, never `Unhealthy`) | base, `Caching.Abstractions` |
| `SharedKernel.ServiceDefaults.Caching.Redis` | `AddRedisHealthCheck(connectionString)` | base, `AspNetCore.HealthChecks.Redis`, `StackExchange.Redis` |
| `SharedKernel.ServiceDefaults.Messaging` | `AddMessagingReadinessCheck()` | base, `Messaging.Abstractions` |
| `SharedKernel.ServiceDefaults.Storage` | `AddStorageReadinessCheck(bucket)` | base, `Storage.Abstractions` |
| `SharedKernel.ServiceDefaults.Search` | `AddSearchReadinessCheck(indexName)` | base, `Search.Abstractions` |
| `SharedKernel.ServiceDefaults.AI` | `AddVectorStoreReadinessCheck(collectionName)` | base, `AI.Abstractions` |
| `SharedKernel.ServiceDefaults.Workflows.Temporal` | `AddWorkflowReadinessCheck()` — holds the WO-047 `13→17` grant | base, `Workflows.Temporal` |
| `SharedKernel.ServiceDefaults.Scheduling` | `AddSchedulerReadinessCheck()` — holds the P-466 `13→19` grant | base, `Scheduling` |
| `SharedKernel.ServiceDefaults.Security.Mtls` | `AddMtlsClientCertificate()`, `AddMtlsForwardedHeaderCertificate()`, `MtlsForwardedHeaderMiddleware`, `MtlsForwardedHeaderOptions`; `MtlsLog` (EventIds 13000/13001/13003) | base, `Security.Mtls` |
| `SharedKernel.ServiceDefaults.Cryptography.KeyVault` | `AddSharedKernelKeyVaultKeyProvider()`, `AddKeyVaultKeyProviderReadinessCheck()` | base, `Cryptography.KeyVault.Azure` |
| `SharedKernel.ServiceDefaults.Configuration.KeyVault` | `AddSharedKernelKeyVaultConfiguration(vaultUri)` | base, `Azure.Extensions.AspNetCore.Configuration.Secrets`, `Azure.Identity` |
| `SharedKernel.ServiceDefaults.Localization` | `AddSharedKernelLocalization()` and its request-culture providers; `LocalizationLog` (EventId 13004) | base, `MultiTenancy`, `Security.Abstractions` |
| `SharedKernel.MultiTenancy` | Concrete `ITenantProvider` resolution strategies (HTTP header, JWT claim delegation, DB-isolation directory lookup); `TenantResolutionMiddleware` (also sets `TenantId` as `Activity` baggage so it becomes ambient to every log record via `SharedKernel.ServiceDefaults`'s log pipeline); `AmbientTenantProvider`. **WO-061/P-393/P-395/P-396/P-400 (dispatched 2026-08-19, IMPLEMENTED and tested in the `SK.13.Core` session, 2026-08-19):** `TenantResolutionOptions.StrategyOrder`'s default corrected to `[Claim, Header, Database]` (was `[Header, Claim, Database]`) — a security fix closing a spoofable cross-tenant-impersonation vector where an unsigned `X-Tenant-Id` header could previously outrank a verified JWT tenant claim (P-393); `MultiTenancyLog` (this package's first-ever production logging, EventIds `13100`–`13199`, shared design with `ServiceDefaultsLog` above) for `TenantResolved`/`TenantNotResolved` (P-395); `TenantResolutionOptionsValidator : IValidateOptions<TenantResolutionOptions>` — a DI-aware `.ValidateOnStart()` chain rejecting an empty or DI-unmatched `StrategyOrder` (P-396; **implementation note:** captures `IServiceProvider` and creates a fresh `IServiceScope` per `Validate()` call rather than constructor-injecting `IEnumerable<ITenantResolutionStrategy>` directly, avoiding a captive-dependency error against the Scoped strategy registrations — mirrors `AddMtlsClientCertificate`'s `IServiceScopeFactory` pattern, C-48); an opt-in `ITenantStatusValidator` seam so Header/Claim-resolved tenants get the same suspend/offboard protection `DatabaseTenantResolutionStrategy` already has by construction (P-400). **WO-075/P-471/P-472 — IMPLEMENTED and tested, shipped 2026-09-04:** a new `Catalog/` sub-surface — `TenantDescriptor`/`TenantStatus`/`TenantIsolationMode`/`ITenantCatalog` (read-only tenant metadata lookup by id or resolution key; provisioning/onboarding explicitly out of scope) giving `ITenantStatusValidator` its first real default implementation, `CatalogTenantStatusValidator` (fails closed on a catalog miss); `DatabaseTenantCatalog` (reuses `DatabaseTenantResolutionStrategy`'s exact `IDbConnectionFactory`/parameterized-query pattern) and `CachedTenantCatalog` (a short-bounded-TTL decorator, `InvalidateTenantAsync` bypassing the TTL immediately, an opt-in `.WithCrossInstanceInvalidation(ICacheInvalidationBus)` — a **direct, compiled** reference to `02.Caching.Abstractions`, never a bridged local seam, since this domain's layering ceiling already legally covers `02.Caching`). Reconciles, never replaces, the platform's other three tenant-adjacent contracts (`ITenantProvider`/`ICurrentTenantService`/`ITenantCacheService`) — see Interface Contracts below | `SharedKernel.Security.Abstractions` (`ITenantProvider`), `SharedKernel.Security.Oidc` (delegates claim resolution to `OidcTenantProvider` — does not reimplement it), `SharedKernel.Persistence.Abstractions` (`IDbConnectionFactory`, for DB-isolation lookups and, new, `DatabaseTenantCatalog`), `Microsoft.AspNetCore.Http.Abstractions`; **WO-061 addition:** `System.Net.IPNetwork` is not needed here (that's `SharedKernel.ServiceDefaults`'s dependency for P-394); **shipped 2026-09-04 (WO-075/P-472):** `ProjectReference` to `02.Caching`'s `SharedKernel.Caching.Abstractions`, interface-only, needed solely for `CachedTenantCatalog`'s opt-in cross-instance invalidation |

Every package in this domain targets `net10.0`, `ImplicitUsings` enabled, `Nullable` enabled. Test sub-folders live inside each project folder (never in a top-level `tests/`).

---

## Technology Stack

| Concern | Technology |
|---------|-----------|
| Distributed tracing, metrics, logging | OpenTelemetry .NET SDK; OTLP exporter configured via the standard `OTEL_EXPORTER_OTLP_ENDPOINT` / `OTEL_EXPORTER_OTLP_PROTOCOL` env vars — no SharedKernel-specific config keys, so the OTel Collector convention stays portable |
| ASP.NET Core / HttpClient / EF Core auto-instrumentation | `OpenTelemetry.Instrumentation.AspNetCore`, `OpenTelemetry.Instrumentation.Http`, `OpenTelemetry.Instrumentation.EntityFrameworkCore` |
| gRPC client auto-instrumentation (WO-056/P-365; shipped and actively invoked 2026-08-12 — S-18/C-47 both `●`) | `OpenTelemetry.Instrumentation.GrpcNetClient` — `PackageReference` on this package itself (S-18), version-aligned with `11.Communication.Grpc.csproj`'s existing beta pin (`1.15.1-beta.1`); the project's pre-existing `<NoWarn>$(NoWarn);NU5104</NoWarn>` suppression (originally added for `OpenTelemetry.Instrumentation.EntityFrameworkCore`) already covers this beta dependency. Actively invoked by `WithCommunicationTelemetry()` via `.AddGrpcClientInstrumentation()` — no longer merely referenced |
| Polly v8 resilience telemetry (WO-056/P-365, shipped 2026-08-12 — metrics only, see Interface Contracts' empirical finding) | No new package — Polly's own `"Polly"`-named `Meter` (surfaced transitively via `11.Communication.Rest`'s `Microsoft.Extensions.Http.Resilience`) is reached by bare string name only, mirroring `WithMessagingTelemetry`'s pre-existing `"MassTransit"`-name wiring precedent; activated by `WithCommunicationTelemetry()` via `WithMetrics` only — Polly v8.4.2 (the version pinned by `Microsoft.Extensions.Http.Resilience 10.7.0`) creates no `ActivitySource`, confirmed by decompiling the installed assembly, so there is no tracing counterpart |
| Health checks (BCL contract) | `Microsoft.Extensions.Diagnostics.HealthChecks` |
| Dependency-specific health check probes | `AspNetCore.HealthChecks.Redis`, `AspNetCore.HealthChecks.NpgSql` (community packages, opt-in per service). **Messaging health checks do not use a community `AspNetCore.HealthChecks.*` package (WO-054/P-351, shipped 2026-08-07):** `AddMessagingReadinessCheck` resolves `07.Messaging`'s own `IMessageBusProbe` (P-347) from DI instead — no third-party HealthChecks dependency at all for messaging. **Retired (WO-054/P-351, S-17/C-46, shipped 2026-08-07):** `AspNetCore.HealthChecks.RabbitMQ`/`AspNetCore.HealthChecks.AzureServiceBus`/`RabbitMQ.Client`/`Azure.Messaging.ServiceBus`/`Azure.Identity` have been removed from `SharedKernel.ServiceDefaults.csproj` — kept below purely as a historical record of why the now-deleted `AddRabbitMqMessagingHealthCheck`/`AddAzureServiceBusMessagingHealthCheck` needed them: **API surface gaps discovered (WO-027 Core):** `AspNetCore.HealthChecks.RabbitMQ` 9.0.0 had no direct AMQP-URI-string `AddRabbitMQ` overload — only `Func<IServiceProvider,IConnection>` and `Func<IServiceProvider,Task<IConnection>>` factory overloads existed. `AddRabbitMqMessagingHealthCheck` built a `RabbitMQ.Client.ConnectionFactory { Uri = ... }` and called `CreateConnectionAsync()` inside the async factory overload — required a direct `RabbitMQ.Client` package reference pinned to match `MassTransit.RabbitMQ`'s transitive floor (7.2.1, not 7.1.2 — a lower pin causes NU1605). This is precisely the "second, independently constructed connection" shape WO-054/P-351 found and retired — the AMQP URI never came from the real configured bus. `AspNetCore.HealthChecks.AzureServiceBus` 9.0.0 shipped only queue/topic/subscription-*scoped* checks (`AddAzureServiceBusQueue`/`Topic`/`Subscription`) — there was no namespace-only/connection-only check, so `AzureServiceBusHealthCheck` (internal) was a custom `IHealthCheck` built directly on `Azure.Messaging.ServiceBus.Administration.ServiceBusAdministrationClient.GetNamespacePropertiesAsync()`, requiring direct `Azure.Messaging.ServiceBus`/`Azure.Identity` references pinned to `MassTransit.Azure.ServiceBus.Core`'s transitive floor (7.20.1 / 1.21.0). |
| OpenTelemetry runtime metrics instrumentation | `OpenTelemetry.Instrumentation.Runtime` — required for `MeterProviderBuilder.AddRuntimeInstrumentation()`; not part of the original Scaffold package set, added during Core (WO-027) when `AddSharedKernelTelemetry` was implemented |
| Tenant resolution — HTTP header | `Microsoft.AspNetCore.Http.Abstractions` (`HttpContext.Request.Headers`) |
| Tenant resolution — JWT claim | Delegates to `SharedKernel.Security.Oidc.OidcTenantProvider` — never reimplemented here |
| Tenant resolution — DB isolation | `SharedKernel.Persistence.Abstractions.IDbConnectionFactory` + parameterized SQL against a tenant directory table |
| Host composition | `Microsoft.Extensions.Hosting` (`IHostApplicationBuilder`), `Microsoft.Extensions.DependencyInjection` |
| mTLS client-certificate composition (WO-058/P-378 — IMPLEMENTED and tested, shipped end to end 2026-08-14) | Kestrel's own `ClientCertificateMode`/`ConfigureHttpsDefaults` (`Microsoft.AspNetCore.Server.Kestrel.Https`/`.Core`, already covered by the existing `<FrameworkReference Include="Microsoft.AspNetCore.App" />` — no new package reference needed); certificate acceptance delegated to `12.Security`'s `SharedKernel.Security.Mtls.IMtlsCertificateValidator` (P-377, shipped 2026-08-13 — `ProjectReference` added to `SharedKernel.ServiceDefaults.csproj`), resolved per-TLS-handshake via a fresh `IServiceScope` created from a captured `IServiceScopeFactory` (never the Scoped validator captured directly — see Interface Contracts); forwarded-header decode via `X509CertificateLoader.LoadCertificate`/`X509Certificate2.CreateFromPem` (both BCL, `System.Security.Cryptography.X509Certificates`) |
| mTLS forwarded-header trust-boundary allowlist (WO-061/P-394 — IMPLEMENTED and tested, shipped 2026-08-19) | `System.Net.IPNetwork` (BCL since .NET 8 — no new package reference); `MtlsForwardedHeaderOptions.TrustedNetworks` + `RemoteIpAddress`-vs-network-collection matching, mirroring ASP.NET Core's own `ForwardedHeadersOptions.KnownProxies`/`KnownNetworks` shape but unified into one collection type |
| Structured audit logging (WO-061/P-395 — IMPLEMENTED and tested, shipped 2026-08-19; this domain's first-ever production logging) | `[LoggerMessage]` source-generated partial methods (`Microsoft.Extensions.Logging.Abstractions`, already referenced transitively via `Microsoft.Extensions.Hosting` — no new package reference); EventIds `13000`–`13099` (`SharedKernel.ServiceDefaults`) / `13100`–`13199` (`SharedKernel.MultiTenancy`), the platform's `01.Core`-registered `13000`–`13999` domain block subdivided 100-wide per package in Packages-table declaration order |
| Opt-in ASP.NET Core rate limiting (WO-061/P-397 — IMPLEMENTED and tested, shipped 2026-08-19) | `Microsoft.AspNetCore.RateLimiting` — ships inside the already-referenced `Microsoft.AspNetCore.App` shared framework (confirmed by the existing Kestrel/mTLS `<FrameworkReference>`) — **no new NuGet package reference of any kind, zero third-party dependency**. `AddRateLimiter`/`RateLimiterOptions`/`RequireRateLimiting` all live in namespace `Microsoft.AspNetCore.Builder`, not `Microsoft.Extensions.DependencyInjection` — verified empirically, not assumed |
| Opt-in Azure Key Vault configuration provider (WO-061/P-398 — `AddSharedKernelKeyVaultConfiguration` IMPLEMENTED and tested, shipped 2026-08-19 (C-56); the platform's first secrets-manager integration) | `Azure.Extensions.AspNetCore.Configuration.Secrets` `1.5.2` + `Azure.Identity` `1.21.0` (`DefaultAzureCredential`) — **`PackageReference`s added to `SharedKernel.ServiceDefaults.csproj` (S-20, Scaffold, shipped 2026-08-19)**; chosen because `12.Security.Oidc` already targets Azure B2C, making an Azure-credential path already first-class in this platform's identity story; first of a pluggable secrets-provider family, AWS Secrets Manager/HashiCorp Vault scoped as future follow-ups |

---

## Interface Contracts

### Package map (WO-084)

The public surface below was written when `SharedKernel.ServiceDefaults` was one package. WO-084 split it
into a dependency-free base plus thirteen integration packages **without changing any namespace or
signature** (one addition: `HealthCheckRegistrationLogging` became public), so every block below still
describes the API exactly — only the package that ships it changed:

| Surface below | Ships in |
| --- | --- |
| Composition entry point (`Extensions/`), `Probes/`, `Telemetry/`, `RateLimiting/`, `HealthCheckExtensions`/`HealthCheckNames`/`HealthCheckTags`/`HealthCheckRegistrationLogging` | `SharedKernel.ServiceDefaults` |
| `AddDatabaseReadinessCheck<TContext>`, `AddDapperDatabaseReadinessCheck` | `.Persistence` |
| `AddCacheReadinessCheck` | `.Caching` |
| `AddRedisHealthCheck` | `.Caching.Redis` |
| `AddMessagingReadinessCheck` | `.Messaging` |
| `AddStorageReadinessCheck` | `.Storage` |
| `AddSearchReadinessCheck` | `.Search` |
| `AddVectorStoreReadinessCheck` | `.AI` |
| `AddWorkflowReadinessCheck` | `.Workflows.Temporal` |
| `AddSchedulerReadinessCheck` | `.Scheduling` |
| `Security/` — mTLS | `.Security.Mtls` |
| `Cryptography/` — `AddSharedKernelKeyVaultKeyProvider`, and `AddKeyVaultKeyProviderReadinessCheck` | `.Cryptography.KeyVault` |
| `Configuration/` — `AddSharedKernelKeyVaultConfiguration` | `.Configuration.KeyVault` |
| `Localization/` | `.Localization` |

`ServiceDefaultsLog` now holds only `HealthCheckRegistered` (13002). Its other four events moved, EventIds
unchanged, into `MtlsLog` (13000/13001/13003) and `LocalizationLog` (13004). Where a block below says
`ServiceDefaultsLog.<mTLS or localization event>`, read the new owner.

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

AddRabbitMqMessagingHealthCheck / AddAzureServiceBusMessagingHealthCheck — RETIRED OUTRIGHT
    (WO-054, P-351, removed 2026-08-07), replaced by AddMessagingReadinessCheck below — not
    deprecated-and-kept; no signature-compatible fix exists. Neither method, nor the internal
    AzureServiceBusHealthCheck/AzureServiceBusConnectionStringMarkers types it used, nor the
    HealthCheckNames.RabbitMq/.AzureServiceBus constants, exist in this package's source anymore.
    Originally (P-122, WO-020): AddRabbitMqMessagingHealthCheck(this IHealthChecksBuilder, string
    amqpUri, ...) built its own RabbitMQ.Client.ConnectionFactory from the caller-supplied amqpUri;
    AddAzureServiceBusMessagingHealthCheck(this IHealthChecksBuilder, string
    connectionStringOrNamespace, ...) built its own ServiceBusAdministrationClient from the
    caller-supplied string. WO-054's ground-truth source read confirmed both open a SECOND
    connection entirely independent of whatever 07.Messaging.MassTransit's MessagingBusBuilder
    actually configured for the service — a health check that can pass while the real bus is down
    (or fail while it is healthy) because it validates a different, independently-configured
    connection. This is worse than no health check: it actively misleads an operator during an
    incident.
    RESOLVED BY RETRACTION-AND-REPLACEMENT, NOT A COMPATIBLE FIX (WO-054, P-351): the
    connection-value parameter each method accepted IS the defect — there is no way to keep either
    signature and also make the check reflect the real bus, so both are retired outright. See
    AddMessagingReadinessCheck below for the replacement. A consuming service must change
    `.AddRabbitMqMessagingHealthCheck(rabbitMqBusOptions.Host)` /
    `.AddAzureServiceBusMessagingHealthCheck(connString)` to `.AddMessagingReadinessCheck()` —
    dropping the connection argument entirely, since the replacement accepts none. This is a
    confirmed breaking change to a published package — a SemVer-major repack is needed at the next
    devops-lead publish pass (not performed as part of this implementation session).

AddMessagingReadinessCheck(this IHealthChecksBuilder, string name = HealthCheckNames.Messaging)
                                                                   → IHealthChecksBuilder
    (P-351, WO-054 — IMPLEMENTED and tested, closed 2026-08-07; state-map S-17/C-46/T-41/T-42/DO-12
    all `●`.) Wraps 07.Messaging's SharedKernel.Messaging.Abstractions.IMessageBusProbe
    .ProbeAsync(CancellationToken ct) → Task<MessageBusHealth> (P-347) in a new
    MessagingReadinessHealthCheck. Resolves only IMessageBusProbe from DI — registered
    unconditionally as a singleton by MessagingBusBuilder.Build() (07.Messaging, no opt-in call
    needed on that side; the health check registration itself remains opt-in here, unchanged from
    every other dependency-specific check in this domain). Reports Healthy iff
    MessageBusHealth.IsHealthy == true; Unhealthy otherwise — never Degraded (no fail-safe-
    absorption layer sits in front of raw message-bus connectivity, same calibration as
    AddDatabaseReadinessCheck<TContext>/AddRedisHealthCheck/AddStorageReadinessCheck/
    AddSearchReadinessCheck/AddVectorStoreReadinessCheck/AddWorkflowReadinessCheck). The probe's
    Description is surfaced via HealthCheckResult.Description. Tagged HealthCheckTags.Ready +
    the pre-existing HealthCheckTags.Messaging (unchanged — already covers both transports, no new
    tag needed).
    NOTE: takes NO caller-supplied identifier or connection parameter of any kind — mirrors
          AddWorkflowReadinessCheck's no-caller-supplied-identifier precedent one step further:
          IMessageBusProbe is a per-host singleton reflecting whichever single transport
          (RabbitMQ or Azure Service Bus) the consuming service's own MessagingBusBuilder already
          configured, so there is nothing left for a call site to supply — and accepting a
          transport-specific connection value the check does not use would itself be the exact
          "dead configuration knob" defect 07.Messaging's own WO-054 audit separately flagged for
          AzureServiceBusOptions.MaxConcurrentCalls (P-342).
    RESOLVED PATTERN (same as AddStorageReadinessCheck/AddSearchReadinessCheck/
          AddVectorStoreReadinessCheck/AddWorkflowReadinessCheck before it): this contract was
          documented ahead of implementation (WO-054 dispatch, 2026-08-04) while 07.Messaging's own
          SK.07.ReadinessProbe phase was still entirely ○ Not Started (all ten RP-01–RP-10 tasks),
          with a tracked, blocked state-map task (C-46) rather than a silent gap. 07.Messaging
          shipped SK.07.ReadinessProbe (P-347) end to end on 2026-08-06 — re-verified directly
          against 07.Messaging/state-map.md (all ten RP-01–RP-10 tasks confirmed `●`) and the
          compiled IMessageBusProbe.cs/MessageBusHealth.cs source on disk before implementing here,
          per this domain's own established verify-before-trusting discipline — and S-17/C-46/
          T-41/T-42/DO-12 were implemented in the same session with zero deviation from the
          contract already locked here.

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

AddSearchReadinessCheck(this IHealthChecksBuilder, string indexName, string name = HealthCheckNames.Search)
                                                                   → IHealthChecksBuilder
    (P-277, WO-044 — IMPLEMENTED and tested, closed 2026-07-24; state-map S-14/C-37/T-33/DO-07 all `●`.)
    Wraps `09.Search`'s `SharedKernel.Search.Abstractions.ISearchIndexProvisioner
    .ProbeAsync(string indexName, ct) → Task<Result<SearchIndexHealth>>` (P-272) in a
    `SearchReadinessHealthCheck`. Resolves only `ISearchIndexProvisioner` from DI plus the explicit
    caller-supplied `indexName` — never a concrete `MeilisearchOptions`/`ElasticSearchOptions` type —
    so one adapter works unmodified against either `SharedKernel.Search.Meilisearch` or
    `SharedKernel.Search.ElasticSearch`, mirroring `AddStorageReadinessCheck`'s bucket-parameter
    precedent exactly. Reports `Healthy` iff `SearchIndexHealth.Reachable && .IndexAddressable &&
    .Searchable` are all true; `Unhealthy` otherwise (including when the underlying `Result` itself
    failed), with the failure surfaced via `HealthCheckResult.Description`. `PendingWriteCount`,
    `DocumentCount`, `EngineVersion`, and `Latency` are surfaced via `HealthCheckResult.Data` as
    informational values only — `PendingWriteCount` is **never** factored into the Healthy/Unhealthy
    decision, per `09.Search/CLAUDE.md`'s own explicit rule ("a deep backlog means results are stale,
    not unavailable, and failing readiness would remove serving capacity exactly when it is most
    needed"). Tagged `HealthCheckTags.Ready` + a new `HealthCheckTags.Search`. Opt-in only.
    NOTE: Contract locked against `09.Search/CLAUDE.md`'s own "Cross-domain work this design requires"
          section (ratified 2026-07-19, ahead of any `09.Search` implementation) — the identical
          documented-ahead-of-implementation pattern already used for `AddStorageReadinessCheck`'s
          D-07/WO-043 precedent.
    RESOLVED: this contract was documented ahead of implementation (WO-044 dispatch, 2026-07-19) while
          `09.Search`'s own `SK.09.Design`/`SK.09.Core` were still `○`, with a tracked, blocked
          state-map task (C-37) rather than a silent gap. `09.Search` reached `Published` end to end
          before this phase resumed; the blocker was re-verified directly against `09.Search
          /state-map.md` and the compiled `SharedKernel.Search.Abstractions` source (not taken on
          trust) and found fully cleared, so S-14/C-37/T-33/DO-07 were implemented in the same session
          with zero deviation from the contract already locked here.

AddVectorStoreReadinessCheck(this IHealthChecksBuilder, string collectionName, string name = HealthCheckNames.VectorStore)
                                                                   → IHealthChecksBuilder
    (P-285, WO-045 — IMPLEMENTED and tested, closed 2026-07-27; state-map C-39/T-34/DO-08 all `●`.)
    Wraps `10.Intelligence`'s `SharedKernel.AI.Abstractions.IVectorCollectionProvisioner
    .ProbeAsync(string collectionName, ct) → Task<Result<VectorCollectionHealth>>` (P-280/P-281) in a
    `VectorStoreReadinessHealthCheck`. Resolves only `IVectorCollectionProvisioner` from DI plus the
    explicit caller-supplied `collectionName` — never a concrete `QdrantOptions`/`MilvusOptions` type —
    so one adapter works unmodified against either `SharedKernel.AI.Qdrant` or `SharedKernel.AI.Milvus`,
    mirroring `AddStorageReadinessCheck`'s bucket-parameter / `AddSearchReadinessCheck`'s indexName-
    parameter precedent exactly. Reports `Healthy` iff `VectorCollectionHealth.Reachable &&
    .CollectionAddressable && .Queryable` are all true (plus `Result.IsSuccess`); `Unhealthy` otherwise
    — never `Degraded` (no fail-safe-absorption layer sits in front of raw vector-store connectivity).
    `VectorCount`, `PendingWriteCount`, `EngineVersion`, `SchemaFingerprint`, and `Latency` are surfaced
    via `HealthCheckResult.Data` as informational values only — `PendingWriteCount` is **never**
    factored into the Healthy/Unhealthy decision, per `10.Intelligence/CLAUDE.md`'s own explicit rule
    ("13.ServiceDefaults MUST NOT treat a deep backlog as a readiness FAILURE — it means results may be
    stale, not unavailable"), directly mirroring `AddSearchReadinessCheck`'s `SearchIndexHealth
    .PendingWriteCount` treatment. Tagged `HealthCheckTags.Ready` + a new `HealthCheckTags.VectorStore`.
    Opt-in only.
    NOTE: Contract locked against `10.Intelligence/CLAUDE.md`'s own "Cross-domain work this design will
          require" section (ratified 2026-07-21, WO-045/P-279) — the identical documented-ahead-of-
          implementation pattern already used for `AddStorageReadinessCheck`'s D-07/WO-043 and
          `AddSearchReadinessCheck`'s D-08/WO-044 precedents.
    RESOLVED: this contract was documented ahead of implementation (WO-045 dispatch, 2026-07-21) while
          `10.Intelligence`'s own `SK.10.Scaffold`/`SK.10.Core` were still unstarted, with a tracked,
          blocked state-map task (C-39) rather than a silent gap. `10.Intelligence` reached `Published`
          end to end before this phase resumed (`IVectorCollectionProvisioner`/`VectorCollectionHealth`
          confirmed shipped), so C-39/T-34/DO-08 were implemented in the same session with zero
          deviation from the contract already locked here. Note the `SharedKernel.AI.Milvus` provider
          was separately retracted (WO-048) — this adapter is unaffected, since it always resolved the
          provider-neutral `IVectorCollectionProvisioner` and never named a concrete provider.

AddOrchestrationReadinessCheck — RETRACTED (WO-047, P-291), permanently out of scope, not merely
    still-blocked.
    Originally intended (P-285, WO-045) to wrap `10.Intelligence`'s `ICompletionProviderDescriptor`
    behind an `OrchestrationReadinessHealthCheck`. Design task D-11 found a genuine, confirmed
    internal inconsistency in `10.Intelligence`'s own ratified brain: "Domain Invariant #8" prose
    claimed a `ProbeAsync` member existed on `ICompletionProviderDescriptor`/`ISemanticKernel`, but
    that interface's own ratified, member-by-member Interface Contracts listing (the section
    explicitly banner'd "RATIFIED... the locked, member-by-member surface") declares exactly four
    members (`ProviderName`, `ContextWindowTokens`, `MaxOutputTokens`,
    `ValidateContextWindow(int estimatedTokens) → Result`) and defines no `ProbeAsync` member
    anywhere. `13.ServiceDefaults` never invents another domain's interface member on its behalf, so
    this could not be resolved locally — flagged for `arch-lead`.
    RESOLVED BY RETRACTION, NOT ADDITION (WO-047, P-291, 2026-07-24): `arch-lead` read the real
    shipped `ICompletionProviderDescriptor.cs` directly and confirmed it is explicitly documented as
    "a singleton, zero-I/O descriptor" — adding a `Task`-returning `ProbeAsync` member would
    contradict that already-shipped, tested (142/142) contract. The only honest alternative (issuing
    a real completion call to check LLM-endpoint reachability) is itself forbidden by
    `10.Intelligence/CLAUDE.md`'s Domain Invariant #5 (no automatic/hidden, re-billing calls).
    `AddOrchestrationReadinessCheck` is therefore dropped from `13.ServiceDefaults`'s scope entirely —
    no `HealthCheckNames.Orchestration`/`HealthCheckTags.Orchestration` constants exist or will be
    added, no `OrchestrationReadinessHealthCheck` type will be written. See the root `CLAUDE.md`'s
    WO-047 changelog entry and its "readiness probe" What-Goes-Where row for the authoritative
    ratification; `10.Intelligence/CLAUDE.md`'s Domain Invariant #8 wording is corrected there too
    (P-291). **No LLM-orchestration readiness probe exists anywhere in this platform, by design, not
    by omission.** D-11 is recorded `—` (N/A/Retracted) in the state-map, not `⚑`/`●`; the downstream
    C-40 (Core)/T-35 (Tests) tasks are likewise `—`.

AddWorkflowReadinessCheck(this IHealthChecksBuilder, string name = HealthCheckNames.Workflows)
                                                                   → IHealthChecksBuilder
    (P-287/P-289, WO-046/WO-047 — IMPLEMENTED and tested, closed 2026-07-27; state-map C-42/T-37/DO-09
    all `●`.)
    Wraps `17.Workflows`'s `SharedKernel.Workflows.Temporal.Health.IWorkflowServiceProbe
    .ProbeAsync(CancellationToken ct) → Task<Result<WorkflowServiceHealth>>` (P-287) in a new
    `WorkflowReadinessHealthCheck`. Resolves only `IWorkflowServiceProbe` from DI — never
    `TemporalOptions`, never `ITemporalClient`, never `WorkflowBase`/`ActivityBase`,
    `IWorkflowDispatcher`, `ITemporalRawClientAccessor`, or any other `17.Workflows` type — and, unlike
    the bucket/indexName/collectionName family (Storage/Search/VectorStore), takes **no
    caller-supplied identifier parameter**: `IWorkflowServiceProbe` is a per-host singleton with
    nothing analogous to a bucket/index/collection name to disambiguate.
    Mapping (CONFIRMED against compiled `WorkflowServiceHealth` source, not assumed): `Healthy` iff
    `Result.IsSuccess && WorkflowServiceHealth.Reachable && .NamespaceAddressable &&
    .WorkerPollersActive`; `Unhealthy` otherwise — never `Degraded` (no fail-safe-absorption layer sits
    in front of raw workflow-service connectivity, same calibration as every other raw-connectivity
    readiness check in this domain). `WorkerPollersActive` is a **non-nullable `bool`** — verified
    directly against `17.Workflows/SharedKernel.Workflows.Temporal/Health/WorkflowServiceHealth.cs`
    during this Design-phase pass, correcting this contract's earlier provisional `bool?` assumption.
    No separate `isWorkerHost` parameter is needed despite that correction: `WorkflowServiceHealth`'s
    own XML doc guarantees `WorkerPollersActive` is always `true` on a client-only (`.AsClientOnly()`)
    registration ("there are no pollers to fail") — the probe implementation itself normalizes the
    client-only case, so a single unconditional `&& .WorkerPollersActive` conjunct correctly serves
    both worker-hosting and client-only registrations with zero caller-supplied disambiguation. This is
    a simpler, more robust resolution than the originally-provisional "`WorkerPollersActive` is null or
    true" shape, and requires no `AddStorageReadinessCheck`-style explicit parameter. `TaskQueueBacklog`
    is surfaced via `HealthCheckResult.Data` as an informational value only and is **never** factored
    into the Healthy/Unhealthy decision, per `17.Workflows/CLAUDE.md`'s own explicit rule that a deep
    backlog means work is slow, not that the service is unavailable — directly mirroring the
    `SearchIndexHealth`/`VectorCollectionHealth.PendingWriteCount` precedent. Tagged
    `HealthCheckTags.Ready` + `HealthCheckTags.Workflows`. Opt-in only.
    LAYERING NOTE (RESOLVED — WO-047, P-291, 2026-07-24): resolving `IWorkflowServiceProbe` requires a
    `ProjectReference` from `SharedKernel.ServiceDefaults` (layer 13) to `SharedKernel.Workflows
    .Temporal` (layer 17) — previously forbidden outright by the root `CLAUDE.md`'s Layering Rules
    table ("13.ServiceDefaults → may reference 01–12"). `arch-lead` granted a narrow, individually-named
    exception, recorded in the root `CLAUDE.md`'s Layering Rules → Hard rules section and in the
    Layering Rules diagram's `13.ServiceDefaults` line: `13.ServiceDefaults` may take a
    `ProjectReference` to `SharedKernel.Workflows.Temporal` **solely** to resolve
    `IWorkflowServiceProbe`/`WorkflowServiceHealth` for this method. No other `17.Workflows` type
    (`TemporalOptions`, `ITemporalClient`, `WorkflowBase`/`ActivityBase`, `IWorkflowDispatcher`,
    `ITemporalRawClientAccessor`, …) may be reached through this exception — doing so is a hard
    violation of the grant's scope, not a style preference. The grant exists only because `17.Workflows`
    ships no lower-numbered `.Abstractions` companion package to reference instead (a deliberate,
    ratified single-package design) — a future domain numbered above `13` wanting the identical pattern
    requires its own named grant, never a widening of this one. The `ProjectReference` itself was added
    to `SharedKernel.ServiceDefaults.csproj` on 2026-07-27 (Scaffold task S-16, `dotnet build` clean),
    guarded by an inline `.csproj` comment restating this exact scope boundary.
    RESOLVED: `17.Workflows/SharedKernel.Workflows.Temporal` shipped its Core —
    `Health/IWorkflowServiceProbe.cs`, `Health/WorkflowServiceHealth.cs`, and
    `Diagnostics/WorkflowDiagnostics.cs` (backing `WithWorkflowTelemetry()` below,
    `WorkflowWellKnown.ActivitySourceName`/`.MeterName` both confirmed `"SharedKernel.Workflows"`) —
    before this phase resumed. C-42/T-37/DO-09 were implemented and tested in the same session with
    zero deviation from the contract already locked here.

AddKeyVaultKeyProviderReadinessCheck(this IHealthChecksBuilder, string name = HealthCheckNames.EncryptionKeyProvider)
                                                                   → IHealthChecksBuilder
    (P-449, WO-068 — IMPLEMENTED and tested, shipped 2026-09-04.)
    Wraps `01.Core`'s `SharedKernel.Cryptography.Symmetric.IEncryptionKeyProviderProbe
    .ProbeAsync(CancellationToken ct) → Task<EncryptionKeyProviderHealth>` (P-487), mirroring the
    established "owning domain ships the probe primitive, this domain ships the `IHealthCheck`
    adapter" split. **RESOLVED — the genuine upstream design gap this method was previously blocked
    on is closed:** `01.Core` shipped P-487 (`IEncryptionKeyProviderProbe`/`EncryptionKeyProviderHealth`,
    `SharedKernel.Cryptography/Symmetric/`) — `EncryptionKeyProviderHealth` is confirmed
    `sealed record EncryptionKeyProviderHealth(bool IsHealthy, string? Description)`, no `Result<T>`
    wrapper (the same no-wrapper shape as `ISchedulerServiceProbe`/`SchedulerServiceHealth`, and
    `07.Messaging`'s `IMessageBusProbe`/`MessageBusHealth` before it). `AzureKeyVaultEncryptionKeyProvider`
    now also implements `IEncryptionKeyProviderProbe`, registered as a third resolvable service type
    by the already-shipped `AddSharedKernelAzureKeyVaultCryptography` — `AddSharedKernelKeyVaultKeyProvider`
    needed zero changes. `ProbeAsync`'s own implementation never lets an exception propagate for an
    ordinary reachability failure (catches everything except `OperationCanceledException`, reports via
    `IsHealthy: false`) — this adapter reads `IsHealthy`/`.Description` directly, no defensive
    try/catch needed. Resolves only `IEncryptionKeyProviderProbe` — never `IEncryptionKeyProvider`,
    `IEnvelopeEncryptionProvider`, or any concrete provider type. No caller-supplied identifier
    (mirrors `AddWorkflowReadinessCheck`/`AddSchedulerReadinessCheck` — a per-host singleton, nothing
    to disambiguate).
    NO NEW LAYERING GRANT NEEDED — unlike `AddWorkflowReadinessCheck`/`AddSchedulerReadinessCheck`:
    `01.Core` is already inside this domain's granted `01`–`12` composition-root range;
    `IEncryptionKeyProviderProbe` is reached via the `ProjectReference` to
    `SharedKernel.Cryptography.KeyVault.Azure` this package already carries for
    `AddSharedKernelKeyVaultKeyProvider`. A future downstream-domain probe wiring only needs its own
    named `13→NN` grant when the owning domain is numbered above 13 with no lower-numbered
    `.Abstractions` companion to reference instead (the `17.Workflows`/`19.Scheduling` shape) — this
    is not that case.
    CALIBRATION: `Unhealthy`, never `Degraded` — no fail-safe-absorption layer sits in front of raw
    KMS connectivity (an encrypt/decrypt call either succeeds or it does not), the same calibration
    family as `AddDatabaseReadinessCheck`/`AddStorageReadinessCheck`/`AddSearchReadinessCheck` —
    never `AddCacheReadinessCheck`'s fail-safe-aware `Degraded`.
    **This closes WO-068/P-449 end to end — every `SK.13.*` phase key is now fully `●`/`—`, closing
    out the last blocked item anywhere in this domain.**
    **UNAFFECTED by WO-081/P-503's `cacheTtl` caching wrap (DESIGN-LOCKED, implementation pending —
    see the Interface Contracts entry above for the full D-37/D-38/D-39 detail):** this check
    continues to resolve only `IEncryptionKeyProviderProbe`, which stays wired to the RAW,
    UNCACHED `AzureKeyVaultEncryptionKeyProvider` singleton regardless of `cacheTtl` — a probe must
    never report a cached reachability signal.

AddSchedulerReadinessCheck(this IHealthChecksBuilder, string name = HealthCheckNames.Scheduler)
                                                                   → IHealthChecksBuilder
    (P-464/P-466, WO-073 — IMPLEMENTED and tested, shipped 2026-09-04.)
    Wraps `19.Scheduling`'s `SharedKernel.Scheduling.Probes.ISchedulerServiceProbe
    .ProbeAsync(CancellationToken ct) → Task<SchedulerServiceHealth>` (P-464). **CORRECTED CONTRACT,
    confirmed by reading the shipped source directly:** unlike `IWorkflowServiceProbe`, this probe
    returns `SchedulerServiceHealth` DIRECTLY — there is no `Result<T>` wrapper to unwrap; the
    original design sketch's "`Result.IsSuccess &&`" framing was corrected during implementation, not
    silently dropped. Resolves only `ISchedulerServiceProbe` from DI — never
    `IScheduledJobRegistry`, `ScheduledCommandJob<TCommand>`, `SchedulingOptions`,
    `MisfirePolicy`/`OverlapPolicy`, or any other `19.Scheduling` type — and, like
    `AddWorkflowReadinessCheck` (and unlike the bucket/indexName/collectionName family), takes **no
    caller-supplied identifier parameter**: `ISchedulerServiceProbe` is a per-host singleton with
    nothing analogous to a bucket/index/collection name to disambiguate.
    Mapping: `Healthy` iff `health.IsRunning`; `Unhealthy` otherwise — never `Degraded` (no
    fail-safe-absorption layer sits in front of raw scheduler-loop liveness, same calibration family
    as `AddWorkflowReadinessCheck`/`AddVectorStoreReadinessCheck`). `RegisteredJobCount` and
    `LastTickUtc` are surfaced via `HealthCheckResult.Data` as informational values only and are
    **never** factored into the Healthy/Unhealthy decision — directly mirrors the `SearchIndexHealth`/
    `VectorCollectionHealth.PendingWriteCount`/`WorkflowServiceHealth.TaskQueueBacklog` precedent (a busy
    scheduler is not an unhealthy one). Tagged `HealthCheckTags.Ready` + `HealthCheckTags.Scheduler`.
    Opt-in only.
    **`IsRunning`/`RegisteredJobCount`/`LastTickUtc` are now CONFIRMED, ratified field names** — verified
    directly against compiled `SharedKernel.Scheduling/Probes/SchedulerServiceHealth.cs`, superseding
    the prior "provisional field names" note, mirroring the `WorkerPollersActive` bool-vs-bool?
    correction precedent (WO-046/WO-047) one more time.
    LAYERING NOTE (ALREADY RATIFIED AT ROOT, and now consumed): root `CLAUDE.md`'s Layering
    Rules → Hard rules confirms `13.ServiceDefaults` may take a `ProjectReference` to
    `SharedKernel.Scheduling` **solely** to resolve `ISchedulerServiceProbe`/`SchedulerServiceHealth` for
    this method — mirrored in `19.Scheduling/CLAUDE.md`'s own Layering section. This is a new,
    independently-earned grant, **never a widening of** the `17.Workflows`/WO-047 grant — per root
    `CLAUDE.md`'s explicit instruction, the two must never be reasoned about by analogy.
    RESOLVED AND SHIPPED (2026-09-04): `SharedKernel.Scheduling` shipped (P-464, `19.Scheduling`) —
    re-verified directly against disk (not taken on trust) before implementing: `.csproj` exists and
    builds; `ProbeAsync`/`SchedulerServiceHealth` byte-identical to this section's corrected contract
    above. `ProjectReference` added to `SharedKernel.ServiceDefaults.csproj`, guarded by an inline
    comment restating the exact grant scope and explicitly disclaiming any relation to the
    `17.Workflows` grant. New `HealthChecks/SchedulerReadinessHealthCheck.cs` +
    `SchedulerReadinessHealthCheckExtensions.cs` + `HealthCheckNames.Scheduler`/`HealthCheckTags.Scheduler`
    constants; new `SchedulerReadinessHealthCheckTests.cs` (4 tests: `IsRunning`-true→Healthy,
    `IsRunning`-false→Unhealthy-never-Degraded, a dedicated `RegisteredJobCount`-never-drives-Unhealthy
    regression mirroring the `TaskQueueBacklog`/`PendingWriteCount` precedent, and a
    `LastTickUtc`-null-is-informational-only test).

NOTE: Every dependency-specific check (Redis, Messaging, DB, Storage, Search, VectorStore, Workflows,
      Scheduler, and — once unblocked — EncryptionKeyProvider/Orchestration) is tagged "ready" and is an
      explicit opt-in call — AddServiceDefaults() / AddSharedKernelHealthChecks() never register any of
      them automatically. A service that does not use a given dependency must not carry a health check
      for it.

HealthCheckNames  (static class, string constants — WO-028/P-177; Storage added WO-043/P-270; Search
                   added WO-044/P-277; VectorStore added WO-045/P-285; Workflows added WO-046/P-289;
                   Messaging added and RabbitMq/AzureServiceBus retired WO-054/P-351 (2026-08-07) —
                   all implemented and tested. Scheduler added WO-073/P-466 and EncryptionKeyProvider
                   added WO-068/P-449 — both DESIGN-LOCKED, not yet added to the shipped source. NOTE:
                   `.Orchestration` was never added — WO-047/P-291 retracted `AddOrchestrationReadinessCheck`
                   before implementation; see Interface Contracts above.)
    .Database = "database"   .Redis = "redis"   .Messaging = "messaging"   (replaces the retired
                          .RabbitMq = "rabbitmq" / .AzureServiceBus = "azure-service-bus", WO-054/P-351)
    .Cache = "cache"   .Startup = "startup"
    .Storage = "storage"   (P-270 — implemented; see AddStorageReadinessCheck above)
    .Search = "search"   (P-277 — implemented and tested; see AddSearchReadinessCheck above)
    .VectorStore = "vector-store"   (P-285 — implemented and tested 2026-07-27; see
                          AddVectorStoreReadinessCheck above)
    .Workflows = "workflows"   (P-289/WO-047 — implemented and tested 2026-07-27; see
                          AddWorkflowReadinessCheck above)
    .Scheduler = "scheduler"   (P-466/WO-073 — design-locked, implementation pending; see
                          AddSchedulerReadinessCheck above)
    .EncryptionKeyProvider = "encryption-key-provider"   (P-449/WO-068 — design-locked for the
                          registration half only; the readiness-check half itself remains genuinely
                          undesignable pending an upstream `01.Core` probe-primitive gap; see
                          AddKeyVaultKeyProviderReadinessCheck above)
    NOTE: Mirrors the pre-existing HealthCheckTags constants-class pattern. Every Add*HealthCheck
          default `name` parameter and the inline "startup" registration inside
          AddSharedKernelHealthChecks reference these constants — zero bare-literal health-check
          names remain anywhere in SharedKernel.ServiceDefaults.
```

#### mTLS client-certificate composition (`Security/`)

```text
AddMtlsClientCertificate(this IHostApplicationBuilder builder,
                          ClientCertificateMode mode = ClientCertificateMode.AllowCertificate)
                                                                   → IHostApplicationBuilder
    (P-378, WO-058 — IMPLEMENTED and tested, shipped end to end 2026-08-14.) For hosts where TLS
    terminates directly at Kestrel. Registers a KestrelServerOptions configuration delegate — via
    services.AddOptions<KestrelServerOptions>().Configure<IServiceScopeFactory>((kestrel,
    scopeFactory) => kestrel.ConfigureHttpsDefaults(https => { ... })) — that sets
    https.ClientCertificateMode = mode and a https.ClientCertificateValidation callback delegating
    the accept/reject decision to IMtlsCertificateValidator. mode defaults to
    ClientCertificateMode.AllowCertificate (request-but-do-not-require); a service that must
    reject any connection without a client certificate passes ClientCertificateMode.
    RequireCertificate explicitly.
    IMtlsCertificateValidator.ValidateAsync is ASYNC-ONLY — Task<MtlsValidationResult>
          ValidateAsync(X509Certificate2 certificate, CancellationToken ct) — confirmed by reading
          12.Security's shipped source directly. Kestrel's ClientCertificateValidation delegate is
          synchronous (Func<X509Certificate2, X509Chain, SslPolicyErrors, bool>) — the TLS
          handshake cannot await — so the validation callback bridges via a blocking
          .GetAwaiter().GetResult() call. THIS IS A REAL, DOCUMENTED LATENCY/THREAD-POOL-
          STARVATION COST UNDER LOAD, called out explicitly (capitalized) in the method's XML
          docs, not buried — a validator used with this method must resolve quickly (an in-memory
          allow-list or a cached trust decision), never issue a slow remote call (CRL/OCSP, an
          external policy service) on this path.
    GENUINE CORRECTION FOUND DURING IMPLEMENTATION, not the design originally sketched: capturing
          IMtlsCertificateValidator itself via Configure<IMtlsCertificateValidator> would resolve
          the Scoped validator ONCE from the ROOT container at Kestrel-options-configuration time
          — Kestrel's TLS handshake has no ambient HttpContext.RequestServices the way
          JwtBearerEvents.OnTokenValidated/CertificateAuthenticationHandler.OnCertificateValidated
          do — exactly the request-scoped-DI-resolution pitfall 12.Security/CLAUDE.md documents
          for its own DpopProofValidator/RevocationCheckRunner, throwing under
          ServiceProviderOptions.ValidateScopes = true (the Development default). The actual
          implementation instead captures IServiceScopeFactory (never itself Scoped) via
          Configure<IServiceScopeFactory> and creates a fresh IServiceScope per TLS handshake
          inside the validation callback, resolving IMtlsCertificateValidator from that scope —
          the standard, safe pattern for resolving a Scoped service outside any ambient request
          scope.
    NOTE: 13.ServiceDefaults never reimplements X.509 chain validation, revocation checking, or
          subject/issuer matching — it only wires ClientCertificateMode and delegates the actual
          accept/reject decision to IMtlsCertificateValidator, mirroring
          ClaimTenantResolutionStrategy's thin-delegating-adapter pattern for OidcTenantProvider
          (delegate to the owning domain, never reimplement).
    NO LAYERING EXCEPTION NEEDED: SharedKernel.Security.Mtls is a concrete 12.Security provider
          package — squarely inside this domain's already-granted 01–12 composition-root range
          (the same range SharedKernel.Security.Oidc already occupies for
          ClaimTenantResolutionStrategy). Unlike AddWorkflowReadinessCheck's ProjectReference to
          17.Workflows, this needs no named arch-lead grant.
    PREREQUISITE, NOT REGISTERED HERE: an IMtlsCertificateValidator implementation must be
          registered separately (typically 12.Security's AddMtlsAuthentication<TValidator>(), or a
          direct services.AddScoped<IMtlsCertificateValidator, TValidator>() call) — omitting it
          throws InvalidOperationException at the first TLS handshake, not at startup.

MtlsForwardedHeaderOptions  (options POCO, Security/MtlsForwardedHeaderOptions.cs)
    .SectionName                                                  → const string
                                                                     ("SharedKernel:ServiceDefaults:MtlsForwardedHeader")
    .HeaderName                                                   → string  (REQUIRED, no default)
    NOTE: For hosts where TLS terminates at an ingress/gateway ahead of Kestrel, which forwards the
          client certificate as a request header instead of negotiating it directly. Ingress
          implementations disagree on both header name and encoding (nginx-ingress commonly uses
          ssl-client-cert; Envoy/Istio commonly use a structured x-forwarded-client-cert; HAProxy
          deployments vary further) — HeaderName carries NO default value tied to any one vendor's
          convention, satisfying P-378's own acceptance criterion directly. A host adopting this
          path MUST configure HeaderName explicitly; an unconfigured (null/empty/whitespace) value
          fails fast at startup via a plain OptionsBuilder<MtlsForwardedHeaderOptions>
          .Validate(...).ValidateOnStart() chain — no new NuGet dependency needed for this single
          check (not routed through 01.Core's SharedKernel.Configuration/AddValidatedOptions).
    .TrustedNetworks                                              → IReadOnlyCollection<IPNetwork>
                                                                     (default empty — WO-061/P-394,
                                                                     IMPLEMENTED and tested,
                                                                     shipped 2026-08-19)
    .AddTrustedProxy(IPAddress)  /  .AddTrustedNetwork(IPNetwork)  → builder-style helpers
    NOTE: Opt-in trust-boundary allowlist, mirroring ASP.NET Core's own ForwardedHeadersOptions
          .KnownProxies/.KnownNetworks shape but unified into one collection type since
          System.Net.IPNetwork (BCL since .NET 8) already expresses a single trusted proxy IP as a
          /32 or /128 network. When non-empty, MtlsForwardedHeaderMiddleware ignores (never
          decodes/validates/sets) a forwarded certificate header from a remote IP outside the
          allowlist — closing the trust-boundary gap where any network path reaching this host
          directly (a misconfigured NetworkPolicy, a multi-hop mesh topology, a debug port, a
          compromised sidecar) could forge the header identically to the real ingress. When left
          unconfigured (the default), existing unrestricted-header behavior is preserved, but a
          one-time startup Warning (ServiceDefaultsLog.ForwardedHeaderTrustBoundaryUnconfigured,
          see the Logging subsection) states any network path reaching this host directly can
          forge the header. Additive/opt-in only — a host that does not configure TrustedNetworks
          is functionally unchanged aside from the new warning log.

AddMtlsForwardedHeaderCertificate(this IHostApplicationBuilder builder,
                                   Action<MtlsForwardedHeaderOptions> configure)
                                                                   → IHostApplicationBuilder
    (P-378, WO-058 — IMPLEMENTED and tested, shipped end to end 2026-08-14.)
    Registers MtlsForwardedHeaderOptions
    (Options pattern, validated) and MtlsForwardedHeaderMiddleware in DI. Must be paired with
    app.UseMiddleware<MtlsForwardedHeaderMiddleware>() — mirrors AddSharedKernelMultiTenancy's
    "register services here, wire the middleware separately" split (registering the services alone
    leaves the middleware absent from the pipeline: a silent no-op, not a crash, the same
    failure-mode shape as forgetting TenantResolutionMiddleware).
    PREREQUISITE, NOT REGISTERED HERE: same as AddMtlsClientCertificate above — an
          IMtlsCertificateValidator implementation must be registered separately.

MtlsForwardedHeaderMiddleware  (sealed class, Security/MtlsForwardedHeaderMiddleware.cs)
    .InvokeAsync(HttpContext context, IMtlsCertificateValidator validator)  → Task
    NOTE: Reads the configured HeaderName from the incoming request and decodes it — tries
          Base64-encoded DER first (Convert.FromBase64String + X509CertificateLoader
          .LoadCertificate — the common minimal forwarding convention, e.g. HAProxy's
          %[ssl_c_der,base64]), falling back to URL-decoded PEM (Uri.UnescapeDataString +
          X509Certificate2.CreateFromPem — the nginx-ingress $ssl_client_escaped_cert
          convention) — into an X509Certificate2, and runs it through the SAME
          IMtlsCertificateValidator instance used by AddMtlsClientCertificate — one validation
          seam serves both TLS-termination topologies, so a policy change to what counts as an
          acceptable client certificate happens once, in 12.Security, never duplicated here.
          ENVOY/ISTIO'S STRUCTURED x-forwarded-client-cert (XFCC) FORMAT IS DELIBERATELY NOT
          PARSED — a host on that ingress must either configure its gateway to forward a
          single-value header carrying only the certificate, or supply its own middleware. On
          successful validation, exposes the certificate via HttpContext.Connection
          .ClientCertificate — confirmed settable, backed by a lazily-created
          Microsoft.AspNetCore.Http.Features.TlsConnectionFeature, by decompiling the real
          Microsoft.AspNetCore.Http.dll (DefaultConnectionInfo) — the SAME property a
          directly-negotiated client certificate populates; disposed via
          context.Response.OnCompleted after being set. Header absent, malformed, or rejected by
          IMtlsCertificateValidator → no certificate is set and the request proceeds
          unauthenticated for mTLS purposes; downstream authorization, not this middleware,
          decides whether that is acceptable for a given endpoint. Never throws.
    SCOPE NOTE: A host MAY register both AddMtlsClientCertificate and
          AddMtlsForwardedHeaderCertificate if its deployment topology genuinely varies by
          environment (e.g. direct Kestrel exposure in one cluster, ingress-fronted in another) —
          the two are not mutually exclusive at the API level, only typically mutually exclusive
          within any single deployment's actual topology.
    NO-OP GUARANTEE: a host that calls neither AddMtlsClientCertificate nor
          AddMtlsForwardedHeaderCertificate is byte-identical in behavior to today — Kestrel's
          default ClientCertificateMode.NoCertificate is left untouched and no new middleware enters
          the pipeline, satisfying P-378's own "no behavior change for non-adopting consumers"
          acceptance criterion directly.
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
    (P-010, WO-003, confirmed in Design phase D-01/D-02; tracing addition D-15, WO-050/P-305 —
    IMPLEMENTED and tested, closed 2026-07-29; state-map C-44/T-39/DO-10 all `●`.) Wires the
    pre-existing "SharedKernel.Caching" meter (version "1.0", static readonly field in
    FusionCacheService — 02.Caching Phase 31) into the host's MeterProvider via
    WithMetrics(m => m.AddMeter(...)), AND the companion "SharedKernel.Caching" ActivitySource
    (same name/version — one instrumentation scope, two signals; 02.Caching Phase 41/P-304) into
    the host's TracerProvider via WithTracing(t => t.AddSource(...)) — bringing this method to
    parity with its five siblings (WithMessagingTelemetry/WithApplicationTelemetry/
    WithSearchTelemetry/WithIntelligenceTelemetry/WithWorkflowTelemetry), all of which wire both a
    tracing source and a meter by name. Idempotent, same rationale as its siblings — calling this
    method more than once registers no duplicate instrument.
    NOTE (verified against 02.Caching/SharedKernel.Caching.FusionCache/Implementations/FusionCacheService.cs):
    the meter's actual instrument set is cache.hits (Counter<long>), cache.misses (Counter<long>),
    cache.errors (Counter<long>), cache.evictions (Counter<long>), and cache.factory.duration
    (Histogram<double>, unit "ms"). This corrects an earlier draft of this contract that named the
    histogram "cache.operation.duration" — no such instrument exists; the correct name is
    cache.factory.duration (factory execution duration on cache miss). WithCachingTelemetry() wires
    the meter by name only — AddMeter("SharedKernel.Caching") subscribes to all five instruments
    automatically; this package does not enumerate or reference individual instrument names in code.
    TRACING ADDITION (D-15, WO-050/P-305 — IMPLEMENTED, 2026-07-29): the private const backing this
    wiring was renamed CachingMeterName → CachingInstrumentationName (same value,
    "SharedKernel.Caching" — zero behavior change to the string, only its name, now that it backs
    both the meter and the tracing source), and a WithTracing(t => t.AddSource(CachingInstrumentationName))
    call was added alongside the existing WithMetrics(...) call. String-name-only wiring, zero new
    ProjectReference (SharedKernel.ServiceDefaults.csproj still references only
    SharedKernel.Caching.Abstractions, never .FusionCache — the ActivitySource's owning assembly),
    mirroring WithMessagingTelemetry's string-name-only wiring of 07.Messaging's internal
    MessagingDiagnostics.ActivitySource.
    RESOLVED: this contract was documented ahead of implementation (WO-050 dispatch, 2026-07-29)
    while 02.Caching's own SK.02.OtelTracingSpans phase was still `○` (all nine OT-01..OT-09 tasks
    Not Started), with a tracked, blocked state-map task (C-44) rather than a silent gap, per this
    domain's own established WO-027/C-19 precedent ("cannot wire a source that does not yet exist
    in code"). 02.Caching's Phase 41 (P-304) reached `●` before this phase resumed; the blocker was
    re-verified directly against 02.Caching/state-map.md (all nine OT-01..OT-09 tasks `●`) and the
    compiled SharedKernel.Caching.FusionCache/Implementations/FusionCacheService.cs source
    (ActivitySource("SharedKernel.Caching", "1.0") confirmed present, byte-identical name/version
    to the existing Meter) and found fully cleared, so C-44/T-39/DO-10 were implemented in the same
    session with zero deviation from the contract already locked here. 13.ServiceDefaults never
    creates this ActivitySource itself — it is created and used entirely within
    SharedKernel.Caching.FusionCache; this method only registers the already-existing source name
    with the host's TracerProvider.

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

WithSearchTelemetry(this IHostApplicationBuilder builder)        → IHostApplicationBuilder
    (P-277, WO-044 — IMPLEMENTED and tested, closed 2026-07-24; state-map C-38/T-33/DO-07 all `●`.)
    Fourth sibling to WithMessagingTelemetry/WithCachingTelemetry/WithApplicationTelemetry — wires
    `"SharedKernel.Search"` into the host's TracerProvider/MeterProvider via
    WithTracing(t => t.AddSource(SearchInstrumentationName)) and
    WithMetrics(m => m.AddMeter(SearchInstrumentationName)), where SearchInstrumentationName is a
    private const string declared on the extension class equal to `"SharedKernel.Search"` — by string
    name only. Idempotent, identical contract to its three siblings. Creates no new
    ActivitySource/Meter itself.
    NOTE: SearchInstrumentationName must stay byte-identical to `09.Search`'s own
          `SearchWellKnown.ActivitySourceName`/`.MeterName` constants (both locked at
          `"SharedKernel.Search"` in `09.Search/CLAUDE.md`'s Interface Contracts). `13.ServiceDefaults`
          deliberately takes **zero `ProjectReference`** to `09.Search` for this wiring — per
          `09.Search`'s own design ("Cross-domain work this design requires" section), that identity is
          held by convention and code review only, not by a shared type — the same situation as
          `WithMessagingTelemetry`'s string-name-only wiring of `07.Messaging`'s internal
          `MessagingDiagnostics.ActivitySource`. This is structurally different from
          `AddSearchReadinessCheck` above, which DOES require a `ProjectReference` (to reach
          `ISearchIndexProvisioner`, a real type consumed by constructor injection, not just a string
          name).
    RESOLVED: this contract was documented ahead of implementation (WO-044 dispatch, 2026-07-19) per
          this domain's established precedent (WO-027/C-19: "cannot wire a source that does not yet
          exist in code"), with a tracked, blocked state-map task (C-38) rather than a silent gap.
          `09.Search` reached `Published` end to end before this phase resumed; the blocker was
          re-verified directly against `09.Search/state-map.md` and the compiled `SharedKernel.Search
          .Abstractions` source (`SearchWellKnown.ActivitySourceName`/`.MeterName` both confirmed
          `"SharedKernel.Search"`) and found fully cleared, so C-38 was implemented in the same session
          with zero deviation from the contract already locked here.

WithIntelligenceTelemetry(this IHostApplicationBuilder builder)  → IHostApplicationBuilder
    (P-285, WO-045 — IMPLEMENTED and tested, closed 2026-07-27; state-map C-41/T-36/DO-08 all `●`.)
    Fifth sibling to WithMessagingTelemetry/WithCachingTelemetry/WithApplicationTelemetry/
    WithSearchTelemetry — wires `"SharedKernel.AI"` into the host's TracerProvider/MeterProvider via
    WithTracing(t => t.AddSource(IntelligenceInstrumentationName)) and
    WithMetrics(m => m.AddMeter(IntelligenceInstrumentationName)), where IntelligenceInstrumentationName
    is a private const string declared on the extension class equal to `"SharedKernel.AI"` — by string
    name only. Idempotent, identical contract to its four siblings. Creates no new ActivitySource/Meter
    itself.
    NOTE: IntelligenceInstrumentationName must stay byte-identical to `10.Intelligence`'s own
          `IntelligenceWellKnown.ActivitySourceName`/`.MeterName` constants (both locked at
          `"SharedKernel.AI"` in `10.Intelligence/CLAUDE.md`'s Interface Contracts, shared identically
          across all three sibling providers' own `internal` diagnostics classes). `13.ServiceDefaults`
          deliberately takes **zero `ProjectReference`** to `10.Intelligence` for this wiring — per
          `10.Intelligence`'s own design ("the entire reason 13.ServiceDefaults can wire one string
          name and cover all three providers with no ProjectReference to 10.Intelligence"), that
          identity is held by convention and code review only, not by a shared type — the same
          situation as `WithMessagingTelemetry`'s string-name-only wiring of `07.Messaging`'s internal
          `MessagingDiagnostics.ActivitySource`. Unlike `AddOrchestrationReadinessCheck` above, this
          method's contract carries no internal inconsistency — `IntelligenceWellKnown`'s constants
          section is self-consistent with the rest of `10.Intelligence/CLAUDE.md`.
    RESOLVED: this contract was documented ahead of implementation (WO-027/C-19 precedent: "cannot wire
          a source that does not yet exist in code") while `10.Intelligence`'s `SK.10.Core` was still
          unstarted, with a tracked, blocked state-map task (C-41) rather than a silent gap.
          `10.Intelligence` reached `Published` end to end before this phase resumed
          (`IntelligenceWellKnown.ActivitySourceName`/`.MeterName` confirmed `"SharedKernel.AI"`), so
          C-41/T-36/DO-08 were implemented in the same session with zero deviation from the contract
          already locked here.

WithWorkflowTelemetry(this IHostApplicationBuilder builder)  → IHostApplicationBuilder
    (P-287/P-289, WO-046/WO-047 — IMPLEMENTED and tested, closed 2026-07-27; state-map C-43/T-38/DO-09
    all `●`.)
    Sixth sibling to WithMessagingTelemetry/WithCachingTelemetry/WithApplicationTelemetry/
    WithSearchTelemetry/WithIntelligenceTelemetry — wires `"SharedKernel.Workflows"` into the host's
    TracerProvider/MeterProvider via WithTracing(t => t.AddSource(WorkflowInstrumentationName)) and
    WithMetrics(m => m.AddMeter(WorkflowInstrumentationName)), where WorkflowInstrumentationName is a
    private const string declared on the extension class equal to `"SharedKernel.Workflows"` — by string
    name only. Idempotent, identical contract to its five siblings. Creates no new ActivitySource/Meter
    itself.
    NOTE: WorkflowInstrumentationName must stay byte-identical to `17.Workflows`'s own
          `WorkflowWellKnown.ActivitySourceName`/`.MeterName` constants (both locked at
          `"SharedKernel.Workflows"` in `17.Workflows/CLAUDE.md`'s Interface Contracts). `13.ServiceDefaults`
          deliberately takes **zero `ProjectReference`** to `17.Workflows` for this wiring — per
          `17.Workflows`'s own design ("13.ServiceDefaults wires them string-name-only via
          WithWorkflowTelemetry() with no ProjectReference to 17.Workflows"), that identity is held by
          convention and code review only, not by a shared type — the same situation as
          `WithMessagingTelemetry`'s string-name-only wiring of `07.Messaging`'s internal
          `MessagingDiagnostics.ActivitySource`. This method's contract carries no internal
          inconsistency — `WorkflowWellKnown`'s constants section is self-consistent with the rest of
          `17.Workflows/CLAUDE.md`.
    RESOLVED: this contract was documented ahead of implementation (WO-027/C-19 precedent: "cannot wire
          a source that does not yet exist in code") while `17.Workflows`'s `SK.17.Core` was still
          unstarted, with a tracked, blocked state-map task (C-43) rather than a silent gap.
          `17.Workflows` reached `Published` end to end before this phase resumed
          (`WorkflowWellKnown.ActivitySourceName`/`.MeterName` confirmed `"SharedKernel.Workflows"`), so
          C-43/T-38/DO-09 were implemented in the same session with zero deviation from the contract
          already locked here.

WithPersistenceTelemetry(this IHostApplicationBuilder builder)  → IHostApplicationBuilder
    (P-326, WO-051 — IMPLEMENTED and tested, closed 2026-07-30; state-map C-45/T-40/DO-11 all `●`.)
    Seventh sibling to WithMessagingTelemetry/WithCachingTelemetry/WithApplicationTelemetry/
    WithSearchTelemetry/WithIntelligenceTelemetry/WithWorkflowTelemetry — but, UNLIKE all six of those
    (each of which wires both a tracing source AND a meter), wires `"SharedKernel.Persistence"` into the
    host TracerProvider ONLY, via WithTracing(t => t.AddSource(PersistenceInstrumentationName)), where
    PersistenceInstrumentationName is a private const string on the extension class equal to
    "SharedKernel.Persistence" — by string name only. Idempotent across repeated calls. Creates no new
    ActivitySource/Meter itself. Deliberately makes NO WithMetrics(...) call.
    NOTE: `06.Persistence`'s P-319 phase (WO-051) ships only `PersistenceActivitySource` (`internal
          static class`, `ActivitySource("SharedKernel.Persistence", "1.0")`) plus `PersistenceTagKeys`
          — no companion `Meter` is planned in that same pass. This makes `WithPersistenceTelemetry` the
          `With*Telemetry` family's first genuinely tracing-only member — the inverse of
          `WithCachingTelemetry`'s pre-D-15/WO-050 metrics-only gap (which was later corrected by ADDING
          a tracing call to bring it to parity with its siblings). This is a deliberate reflection of
          what `06.Persistence` actually ships, documented explicitly so it is never mistaken for an
          unfinished implementation — if `06.Persistence` ever ships a companion `Meter` in a future
          phase, a parallel follow-up (mirroring the D-15/C-44/WithCachingTelemetry precedent) would
          extend this method with a `WithMetrics(...)` call at that time, not before.
          PersistenceInstrumentationName must stay byte-identical to `06.Persistence`'s own
          `PersistenceActivitySource`'s `ActivitySource` name (`"SharedKernel.Persistence"`, version
          `"1.0"`) by convention and code review — never by a shared type. `06.Persistence/CLAUDE.md`
          states this explicitly: "THIS EXACT NAME/VERSION IS THE COORDINATION POINT for
          13.ServiceDefaults' paired phase P-326 — do not rename without updating that consumer."
          `13.ServiceDefaults` deliberately takes **zero new ProjectReference** for this wiring —
          `PersistenceActivitySource` is `internal` with no `InternalsVisibleTo` grant to
          `SharedKernel.ServiceDefaults` — the same situation as `WithMessagingTelemetry`'s/
          `WithApplicationTelemetry`'s string-name-only wiring of their respective domains' internal
          diagnostics classes. (A `ProjectReference` to `SharedKernel.Persistence.EfCore` already exists
          for the unrelated `AddDatabaseReadinessCheck<TContext>` health check, C-27 — irrelevant here,
          since a `ProjectReference` alone does not grant access to an `internal` type without
          `InternalsVisibleTo`.)
    RESOLVED: this contract was documented ahead of implementation (WO-051 dispatch, 2026-07-30) while
          `06.Persistence`'s Core task C-110 (`PersistenceActivitySource`/`PersistenceTagKeys`) was still
          `○` Not Started, with a tracked, blocked state-map task (C-45) rather than a silent gap —
          mirroring the D-07/D-08/D-10/D-13/D-15 documented-ahead-of-implementation precedent. The
          blocker was re-verified directly against `06.Persistence/state-map.md` (C-110 confirmed `●`)
          and the compiled `SharedKernel.Persistence.EfCore/Diagnostics/PersistenceActivitySource.cs`
          source (not taken on trust) — `internal static class PersistenceActivitySource` with
          `Source = new ActivitySource("SharedKernel.Persistence", "1.0")`, byte-identical to this
          contract's locked name/version — before implementing. C-45/T-40/DO-11 were implemented and
          tested in the same session with zero deviation from the contract already locked here. Full
          `SharedKernel.ServiceDefaults.Tests` suite: 91 passed, 0 failed, 0 regressions (+3 new:
          tracing-idempotency, no-throw, and a genuine `BaseProcessor<Activity>`-based span-capture test
          verified to fail when the `AddSource` call is removed).

WithCommunicationTelemetry(this IHostApplicationBuilder builder)  → IHostApplicationBuilder
    (P-365, WO-056 — IMPLEMENTED and tested, closed 2026-08-12; state-map D-18/S-18/C-47/T-43 all
    `●`; DO-13 remains `○` — the doc pass is a separate SK.13.Docs session.)
    Eighth sibling to WithMessagingTelemetry/WithCachingTelemetry/WithApplicationTelemetry/
    WithSearchTelemetry/WithIntelligenceTelemetry/WithWorkflowTelemetry/WithPersistenceTelemetry —
    but architecturally distinct from all seven: it is the family's first member that does NOT wire
    an already-existing SharedKernel-owned ActivitySource/Meter by bare string name. 11.Communication
    owns no "SharedKernel.Communication" instrumentation source of its own; instead this method
    activates two THIRD-PARTY OTel integrations already referenced transitively inside
    11.Communication but never invoked by anything (confirmed by direct .csproj inspection, not
    assumed):
      (1) gRPC (tracing only): WithTracing(t => t.AddGrpcClientInstrumentation()) — activates
          OpenTelemetry.Instrumentation.GrpcNetClient (a PackageReference on THIS package's own
          .csproj since S-18, version-aligned with 11.Communication.Grpc/
          SharedKernel.Communication.Grpc.csproj's existing pin at 1.15.1-beta.1), enriching outbound
          gRPC spans with RPC semantic-convention tags (rpc.system, rpc.service, rpc.method,
          rpc.grpc.status_code) in place of the under-specified generic-HTTP span
          OpenTelemetry.Instrumentation.Http alone produces (already wired unconditionally inside
          AddSharedKernelTelemetry/C-02 — this method is additive to that, never a replacement). The
          family's first member needing a real instrumentation package rather than a bare
          AddSource/AddMeter string call. Deliberately ZERO ProjectReference to 11.Communication.Grpc
          — the instrumentation package hooks the gRPC client pipeline via DiagnosticSource/
          ActivityListener at runtime, with no compile-time coupling to Grpc.Net.Client types.
      (2) Polly (METRICS ONLY, not tracing — a correction to the original design, made under this
          task's own explicit reconfirm-before-shipping gate): WithMetrics(m =>
          m.AddMeter(PollyInstrumentationName)), PollyInstrumentationName a private const string equal
          to "Polly" — Polly v8's own resilience telemetry (retry attempts, circuit-breaker state
          transitions, timeout events), enabled by default whenever 11.Communication.Rest's
          Microsoft.Extensions.Http.Resilience (10.7.0) builds a StandardResilienceHandler/
          AddResilienceHandler() pipeline. Pure string-name wiring, zero new package or project
          reference — mirrors WithMessagingTelemetry's pre-existing third-party "MassTransit"-meter-
          name wiring precedent.
    EMPIRICAL FINDING (C-47, 2026-08-12) — the "Polly" literal and its telemetry shape were
          decompiled (ilspycmd), not assumed, per this domain's standing "verify against compiled/
          decompiled source, never assumed" discipline (a second concrete precedent for this
          discipline, alongside 11.Communication's P-359 CallOptionsActions discovery). Traced the
          exact pinned dependency chain: Microsoft.Extensions.Http.Resilience 10.7.0 →
          Microsoft.Extensions.Resilience 10.7.0 → Polly.Extensions 8.4.2 → Polly.Core 8.4.2.
          CONFIRMED: Polly.Extensions.dll's internal Polly.Telemetry.TelemetryListenerImpl creates
          `internal static readonly Meter Meter = new Meter("Polly", "1.0")` — the "Polly" Meter
          name/version is byte-identical to the design's assumption. NOT CONFIRMED, and corrected: a
          repo-wide grep for "ActivitySource" (and, more broadly, "Activity") across the complete
          pinned chain (Polly.Core.dll, Polly.Extensions.dll, Polly.RateLimiting.dll,
          Microsoft.Extensions.Resilience.dll, Microsoft.Extensions.Http.Resilience.dll,
          Microsoft.Extensions.Http.Diagnostics.dll) returns ZERO matches — Polly v8.4.2's own
          resilience telemetry in this exact dependency chain is reported via this Meter and an
          ILogger category also named "Polly" only; it creates no ActivitySource and starts no
          Activity of its own. Consequently there is NO WithTracing(t =>
          t.AddSource(PollyInstrumentationName)) call for Polly — only WithMetrics(...). Tracing is
          gRPC-only. The outbound HTTP/gRPC request span itself continues to come from
          OpenTelemetry.Instrumentation.Http/.GrpcNetClient, unaffected.
    IDEMPOTENCY FINDING (C-47, 2026-08-12) — CONFIRMED ALREADY DEDUP-SAFE, NO GUARD NEEDED. Flagged
          in Design as a GATING risk because .AddGrpcClientInstrumentation() is an instrumentation-
          factory registration, not a bare AddSource(string)/AddMeter(string) call, so it does not
          automatically inherit the OTel SDK's by-name dedup every other family sibling relies on.
          Resolved by decompiling the installed OpenTelemetry.Instrumentation.GrpcNetClient
          1.15.1-beta.1 and OpenTelemetry.Api.ProviderBuilderExtensions 1.16.0 assemblies (not
          assumed): (1) the extension registers its backing GrpcClientInstrumentation type via
          `services.TryAddSingleton<T>()` — DI-deduped to exactly one instance regardless of call
          count; (2) that singleton's constructor performs the one side-effecting action,
          `DiagnosticSourceSubscriber.Subscribe()` to the "Grpc.Net.Client" DiagnosticListener, which
          runs exactly once (standard singleton construction) and is itself internally guarded
          (`if (allSourcesSubscription == null)`); (3) the per-call span-enrichment logic
          (GrpcClientDiagnosticListener) does not start its own Activity — it only tags
          Activity.Current, which Grpc.Net.Client's own diagnostics start exactly once per call — so
          with only one subscriber ever attached, no duplicate/double-tagged span is possible
          regardless of registration count; (4) the residual case of the same singleton being
          registered more than once in the TracerProviderBuilder's internal bookkeeping list only
          affects Dispose() at provider shutdown, and DiagnosticSourceSubscriber.Dispose() is itself
          idempotent (Interlocked.CompareExchange-guarded). No custom static-flag guard was added —
          none is needed; this method relies entirely on the third-party package's own correctly-
          idempotent design. GENERALIZABLE TECHNIQUE for a future With*Telemetry-family method that
          needs to wire a genuine instrumentation-factory extension (not a bare AddSource/AddMeter
          call): decompile the actual installed package first to check whether its DI registration
          uses TryAddSingleton/is otherwise self-guarding before assuming a custom guard is needed.
    ZERO CROSS-DOMAIN GATE (unique among this family's D-07 through D-17 precedents): every prior
          With*Telemetry/Add*ReadinessCheck phase was locked in Design while genuinely blocked at
          Scaffold/Core pending its owning domain shipping a probe/source/meter that did not yet exist
          in compiled code. This phase needed no 11.Communication code artifact and no ProjectReference
          to it at all — both OpenTelemetry.Instrumentation.GrpcNetClient and Polly's own diagnostics
          are independent third-party NuGet packages this package references directly.
    RESOLVED: this contract was locked in full during Design (WO-056 dispatch, 2026-08-11) — not
          merely "documented ahead of implementation" the way its seven siblings each were, since there
          was no cross-domain landing to wait for. S-18 (PackageReference) landed 2026-08-12; C-47
          (implementation) landed the same day with the two corrections above; T-43 (the elaborate
          genuine-capture proof) landed the same day as well. New
          `CommunicationTelemetryExtensionsTests.cs`: 4 baseline tests (TracerProviderBuilder/
          MeterProviderBuilder idempotency-by-DI-count, no-throw, same-builder-return — matching six of
          the seven existing sibling test files' baseline level) plus 4 genuine-capture gating tests — a
          real in-process gRPC call producing an `rpc.system == "grpc"`-tagged captured span, a real
          Polly retry pipeline emitting a captured `"Polly"`-named metric (metric only — Polly v8.4.2
          emits no `ActivitySource` in this dependency chain), and idempotency exercised directly for
          both (registering twice does not duplicate the span/metric for one call/execution). All four
          verified to fail when their corresponding wiring is removed. `dotnet build -c Release` clean
          (0 errors, 0 warnings); `SharedKernel.ServiceDefaults.Tests` 100/100 passing (+8 total new
          across C-47/T-43), 0 regressions.

WithIntegrationTelemetry(this IHostApplicationBuilder builder)  → IHostApplicationBuilder
    (P-430, WO-064 — IMPLEMENTED and tested, closed 2026-08-21; state-map D-29/C-59/T-69/DO-24 all `●`.)
    Ninth sibling to WithMessagingTelemetry/WithCachingTelemetry/WithApplicationTelemetry/
    WithSearchTelemetry/WithIntelligenceTelemetry/WithWorkflowTelemetry/WithPersistenceTelemetry/
    WithCommunicationTelemetry — but, like WithPersistenceTelemetry (and UNLIKE the six meter+tracing
    siblings), wires `"SharedKernel.Integration"` into the host TracerProvider ONLY, via
    WithTracing(t => t.AddSource(IntegrationInstrumentationName)), where IntegrationInstrumentationName
    is a private const string on the extension class equal to "SharedKernel.Integration" — by string
    name only. Idempotent across repeated calls. Creates no new ActivitySource itself. Deliberately
    makes NO WithMetrics(...) call.
    NOTE: `15.Integration`'s WO-064/P-424 phase (shipped end to end 2026-08-21, `SK.15.WO064` H-15/H-16
          both `●`) ships `WebhookIntegrationActivitySource` (`ActivitySource("SharedKernel.Integration")`,
          `Dispatch/WebhookIntegrationActivitySource.cs`) wrapping
          `WebhookDispatcher.DispatchToSubscriptionAsync`/`.DispatchAsync` — no companion `Meter` exists
          in that pass. This makes `WithIntegrationTelemetry` the family's second genuinely
          tracing-only member, mirroring `WithPersistenceTelemetry`'s exact shape and rationale
          (D-16/DO-11) — a deliberate reflection of what `15.Integration` actually ships, documented
          explicitly so the missing `WithMetrics(...)` call is never mistaken for an unfinished
          implementation. If `15.Integration` ever ships a companion `Meter` in a future phase, a
          parallel follow-up (mirroring the D-15/C-44/WithCachingTelemetry precedent) would extend
          this method with a `WithMetrics(...)` call at that time, not before.
          IntegrationInstrumentationName is byte-identical to `15.Integration`'s own
          `WebhookIntegrationActivitySource.Name` (`"SharedKernel.Integration"`, confirmed by reading
          the shipped source directly, not assumed) by convention and code review — never by a shared
          type, mirroring every prior sibling's cross-domain-name-identity convention.
          `13.ServiceDefaults` takes **zero ProjectReference** to `15.Integration` for this wiring —
          `AddSource(string)` requires no compile-time type reference to the owning assembly at all,
          regardless of the source's declared accessibility (`WebhookIntegrationActivitySource` is in
          fact `public`, but the wiring needs no reference either way), the same mechanism
          `WithMessagingTelemetry`/`WithApplicationTelemetry`/`WithPersistenceTelemetry` already rely
          on to wire an `internal`, no-`InternalsVisibleTo`-grant `ActivitySource` in their respective
          owning domains.
    RESOLVED: the recorded blocker was re-verified directly against `15.Integration/state-map.md` and
          the compiled `SharedKernel.Integration.Webhooks` source (not taken on trust) — `SK.15.WO064`'s
          H-15 (Design)/H-16 (Core) are both `●`, and `Dispatch/WebhookIntegrationActivitySource.cs`
          exists on disk with `public const string Name = "SharedKernel.Integration";`. This contract
          was documented ahead of implementation (WO-027/C-19 precedent: "cannot wire a source that
          does not yet exist in code"), with a tracked, blocked state-map task (C-59) rather than a
          silent gap — mirroring the D-08/D-09/D-10/D-13/D-15/D-16/D-17 documented-ahead-of-
          implementation precedent. C-59/T-69/DO-24 were implemented and tested in the same session
          with zero deviation from the contract already locked here. New
          `Telemetry/IntegrationTelemetryExtensions.cs` + `IntegrationTelemetryExtensionsTests.cs` (3
          tests: tracing-idempotency, no-throw, and a genuine `BaseProcessor<Activity>` span-capture
          test verified to fail when the `AddSource` call is removed — mirrors T-40's discipline
          exactly). `SharedKernel.ServiceDefaults.Tests`: 173 → 176 passing (+3), 0 regressions.

WithSchedulingTelemetry(this IHostApplicationBuilder builder)  → IHostApplicationBuilder
    (P-464/P-465, WO-073 — IMPLEMENTED and tested, shipped 2026-09-04.)
    The **tenth** sibling in the `With*Telemetry` family. **Unlike the two tracing-only members**
    (`WithPersistenceTelemetry`/`WithIntegrationTelemetry`, whose owning domains ship no companion
    `Meter`), this wires **both** signals — confirmed directly against
    `19.Scheduling/SharedKernel.Scheduling/Diagnostics/SchedulingTelemetry.cs` (internal static class):
    `ActivitySource("SharedKernel.Scheduling")` **plus** a companion `Meter("SharedKernel.Scheduling")`,
    both sharing the same name constant — matching the six-sibling shape (`WithMessagingTelemetry`/
    `WithCachingTelemetry`/`WithApplicationTelemetry`/`WithSearchTelemetry`/`WithIntelligenceTelemetry`/
    `WithWorkflowTelemetry`): `WithTracing(t => t.AddSource(SchedulingInstrumentationName))` +
    `WithMetrics(m => m.AddMeter(SchedulingInstrumentationName))`, where `SchedulingInstrumentationName`
    is a `private const string` equal to `"SharedKernel.Scheduling"` — string-name-only, zero
    `ProjectReference` for THIS method specifically (even though the package as a whole now carries one
    for `AddSchedulerReadinessCheck`, below), idempotent across repeated calls (OTel SDK by-name dedup).
    Creates no new `ActivitySource`/`Meter` itself.
    RESOLVED AND SHIPPED (2026-09-04): `19.Scheduling` shipped `SharedKernel.Scheduling` (P-464) —
          re-verified directly (not taken on trust) that `Diagnostics/SchedulingTelemetry.cs` exists on
          disk with `Name = "SharedKernel.Scheduling"` and both instruments, byte-identical to this
          section's already-locked contract. New `Telemetry/SchedulingTelemetryExtensions.cs` +
          `SchedulingTelemetryExtensionsTests.cs` (5 tests: tracing-idempotency, metrics-idempotency,
          no-throw, a genuine `BaseProcessor<Activity>` span-capture test, and a genuine
          `OpenTelemetry.Exporter.InMemory`-based metric-capture test — both capture tests verified
          during implementation to fail when their corresponding `AddSource`/`AddMeter` call was
          temporarily removed, then the production code was restored unchanged).

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

#### Health check endpoint authorization hardening (`HealthChecks/`, WO-061/P-399 — IMPLEMENTED and tested, shipped 2026-08-19)

```text
MapDefaultHealthCheckEndpoints(this IEndpointRouteBuilder, bool requireAuthorization = false)
                                                                   → IEndpointRouteBuilder
    NOTE: Gains a new optional parameter (previously took no parameters). Default `false` is
          byte-identical to today's unauthenticated "/health/live"/"/health/ready" behavior. When
          `true`, both endpoint mappings chain `.RequireAuthorization()`. `README.md`/`CLAUDE.md`
          MUST state, IN CAPITALS, that these endpoints must be network-restricted at the
          ingress/NetworkPolicy layer regardless of this parameter — RequireAuthorization() is
          defense-in-depth, not a substitute for network isolation, since a K8s kubelet's own
          liveness/readiness probe calls are typically unauthenticated and would themselves be
          rejected if this parameter were misapplied to the endpoint set the kubelet calls. A
          worked example minimal ResponseWriter serializing only `{ status }` (never
          HealthReport.Entries[*].Data/.Description) is a documented code sample, not a new type.
```

#### Structured audit logging (`Logging/`, WO-061/P-395 — IMPLEMENTED and tested, shipped 2026-08-19; this domain's first-ever production logging)

```text
ServiceDefaultsLog  (static partial class, [LoggerMessage]-authored, SharedKernel.ServiceDefaults)
    .MtlsCertificateAccepted(ILogger, string thumbprint, string subject)         EventId 13000, Information
    .MtlsCertificateRejected(ILogger, string thumbprint, string subject, string reason)
                                                                                  EventId 13001, Warning
    .HealthCheckRegistered(ILogger, string healthCheckName, string tags)         EventId 13002, Information
    .ForwardedHeaderTrustBoundaryUnconfigured(ILogger, string headerName)        EventId 13003, Warning
    NOTE: Never logs certificate PEM/DER bytes — only .Thumbprint/.Subject/a reason string.
          MtlsCertificateAccepted/Rejected fire from both AddMtlsClientCertificate's
          ClientCertificateValidation delegate and MtlsForwardedHeaderMiddleware (including the
          P-394 TrustedNetworks-rejection case, sourced from Rejected with a reason identifying the
          allowlist check). HealthCheckRegistered fires once per Add*Check/Add*ReadinessCheck
          registration call, never per probe invocation. ForwardedHeaderTrustBoundaryUnconfigured is
          the P-394 startup warning, fired from AddMtlsForwardedHeaderCertificate when
          TrustedNetworks is left empty.

MultiTenancyLog  (static partial class, [LoggerMessage]-authored, SharedKernel.MultiTenancy)
    .TenantResolved(ILogger, Guid tenantId, string strategyName)                 EventId 13100, Debug
    .TenantNotResolved(ILogger)                                                  EventId 13101, Trace
    NOTE: TenantId is the payload here, not repeated ambient context, so it is a legitimate named
          template placeholder — distinct from the platform's CorrelationId/TraceId/TenantId-
          ambient-only rule, which governs context repeated ACROSS unrelated log statements.
          TenantNotResolved is Trace, not Debug, deliberately — it fires on every unresolved
          request including anonymous/health-check traffic and would be excessively noisy at Debug.
          This same call site is reused, not duplicated, when P-400's ITenantStatusValidator
          rejects an otherwise-resolved tenant.

EventId sub-block allocation (inside 01.Core's platform-wide 13000–13999 domain reservation,
subdivided 100-wide per package in Packages-table declaration order):
    SharedKernel.ServiceDefaults  13000–13099
    SharedKernel.MultiTenancy     13100–13199
    NOTE: 01.Core's LoggingEventIdRanges registry (P-249) must record this allocation — flagged for
          arch-lead/core-arch-planner, out of this domain's own jurisdiction to edit directly.
```

#### Opt-in rate limiting (`RateLimiting/`, WO-061/P-397 — IMPLEMENTED and tested, shipped 2026-08-19)

```text
AddSharedKernelRateLimiting(this IHostApplicationBuilder, Action<RateLimiterOptions>? configure = null)
                                                                   → IHostApplicationBuilder
    Wraps the BCL's own Microsoft.AspNetCore.RateLimiting (no new NuGet package — already ships
    inside the referenced Microsoft.AspNetCore.App shared framework). Registers a conservative
    global fixed-window limiter (partitioned by remote IP) plus a named RateLimitPolicyNames
    .Authentication policy the consumer attaches via [EnableRateLimiting(...)] to its own
    token/login routes — this domain never knows the concrete route, so it supplies only the
    mechanism. RejectionStatusCode = 429; OnRejected left at the BCL default (bare 429, no body) —
    the documented recipe shows attaching a real 14.Presentation.WebApi RateLimitRejectionProblemDetails
    .Create(HttpContext, TimeSpan?) call via OnRejected, NEVER a hard 14.Presentation reference from
    this domain. `configure` runs last, so a caller can override any default threshold. Entirely
    opt-in — never called from AddServiceDefaults(); a non-adopting host is byte-identical to today.

RateLimitPolicyNames  (static class, string constants)
    .Authentication = "authentication"
    NOTE: Mirrors the HealthCheckNames/TenantResolutionStrategyNames constants-class pattern.
```

> **WO-063/P-419 status (Tests `●`/Docs `●`, both shipped 2026-08-21 — this closes WO-063/P-419 end to end):** a source-verification pass found the `OnRejected` recipe above was, until this phase shipped, still hand-rolling its own raw `ProblemDetails` in `README.md` rather than calling the real `RateLimitRejectionProblemDetails.Create(...)` helper `14.Presentation`'s P-408 shipped specifically to receive this handoff (`SharedKernel.Presentation.WebApi` `1.2.0`/`1.3.0`, `Published`) — the two independently-shipped halves of the handoff were never actually connected. No Core/Scaffold change was needed to fix this: `configure` already exposes `OnRejected` to the caller. **Tests prove the corrected recipe is genuine, not just documented:** `SharedKernel.ServiceDefaults.Tests/RateLimiting/RateLimitRejectionRecipeTests.cs` wires a real `WebApplication`/`TestServer` host with `options.OnRejected` calling `RateLimitRejectionProblemDetails.Create(context.HttpContext, retryAfter)`, and asserts the rejected response is genuinely `RateLimitRejectionProblemDetails`-shaped (429, `application/problem+json`, `Status`/`Type`/`Extensions["traceId"]`, a parseable `Retry-After` header) — plus a companion regression proving the no-recipe call shape stays the byte-identical BCL default (empty body, no `Content-Type`, no `Retry-After`). This required a **test-only** cross-domain `ProjectReference` from `SharedKernel.ServiceDefaults.Tests.csproj` to `14.Presentation/SharedKernel.Presentation.WebApi.csproj` (mirroring the T-43/WO-056 gating-proof-only precedent) — never a production reference either direction; the production `SharedKernel.ServiceDefaults.csproj`/DI surface is unchanged. **Docs now closes the loop:** `README.md`'s "Rate limiting" section's `OnRejected` example rewritten to call the real helper (extracting `retryAfter` via `context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfterMetadata)`, per the recipe above), noting `Retry-After` now round-trips; `RateLimitingExtensions.AddSharedKernelRateLimiting`'s XML `<remarks>` updated to match, naming `RateLimitRejectionProblemDetails` in prose only — no compiled reference added to the production `.csproj`. All six `SK.13.*` phase keys are `●`/`—` again.

#### Opt-in secrets-manager configuration (`Configuration/`, WO-061/P-398 — IMPLEMENTED and tested, shipped 2026-08-19; the platform's first secrets-manager integration)

```text
AddSharedKernelKeyVaultConfiguration(this IHostApplicationBuilder, Uri vaultUri, TokenCredential? credential = null)
                                                                   → IHostApplicationBuilder
    Wraps Azure.Extensions.AspNetCore.Configuration.Secrets's builder.Configuration
    .AddAzureKeyVault(vaultUri, credential ?? new DefaultAzureCredential()), appending Key Vault as
    an additional IConfiguration source. Chosen as the first provider because 12.Security.Oidc
    already targets Azure B2C — TokenCredential/DefaultAzureCredential is already first-class in
    this platform's identity story. Explicitly the first of a pluggable secrets-provider family
    (mirrors 08.Storage's S3/OBS multi-cloud precedent) — a future AWS Secrets Manager/HashiCorp
    Vault provider is an explicit future follow-up, not this phase. Entirely opt-in — called
    explicitly by the consumer after AddServiceDefaults() and before any code reads vault-backed
    configuration; no new mandatory PackageReference on the AddServiceDefaults() path itself.
    GATING REQUIREMENT: a misconfigured/unreachable vault must throw at startup, never silently
    fall back to an empty configuration source — Core must empirically verify the Azure SDK's own
    default behavior (never assume fail-fast) and wrap it with an explicit connectivity check if
    the SDK's default is lazy/silent instead.
```

#### Opt-in Azure Key Vault key-provider registration (`Cryptography/`, `HealthChecks/`, WO-068/P-449 — IMPLEMENTED and tested, shipped 2026-09-04; caching wrap IMPLEMENTED and tested WO-081/P-503, shipped 2026-09-08)

```text
AddSharedKernelKeyVaultKeyProvider(this IHostApplicationBuilder builder, TimeSpan? cacheTtl = null)
                                                                   → IHostApplicationBuilder
    A composition-root registration helper, **distinctly named from `AddSharedKernelKeyVaultConfiguration()`
    above to avoid the two being confused**: that method wires Azure Key Vault as an `IConfiguration`
    *source*; this one registers `01.Core`'s `SharedKernel.Cryptography.KeyVault.Azure`'s
    `AzureKeyVaultEncryptionKeyProvider` as the platform's `IEncryptionKeyProvider`/
    `IEnvelopeEncryptionProvider`. Implementation is a thin call-through to `01.Core`'s already-fully-
    specified `AddSharedKernelAzureKeyVaultCryptography(builder.Configuration)` (P-447) —
    `13.ServiceDefaults` never reimplements Key Vault key resolution itself, mirroring the standing
    "owning domain ships the provider, this domain ships the composition wiring" rule already applied
    to `06.Persistence`/`08.Storage`/`09.Search`/`10.Intelligence`/`17.Workflows`/`12.Security.Mtls`.
    IDEMPOTENCY: guarded by `services.Any(d => d.ServiceType == typeof(AzureKeyVaultEncryptionKeyProvider))`
    before calling through — `01.Core`'s own registration method registers unconditionally via plain
    `AddSingleton` on every call (correct for its own single-call contract), so a bare call-through here
    would double-register `AzureKeyVaultEncryptionKeyProvider`/`IEncryptionKeyProvider`/
    `IEnvelopeEncryptionProvider` on a second invocation; the guard therefore lives in THIS package.
    RESOLVED AND SHIPPED (2026-09-04): the recorded blocker was found STALE and corrected — re-verified
          directly against disk (not taken on trust): `01.Core`'s `SharedKernel.Cryptography.KeyVault.Azure`
          is fully shipped (`AzureKeyVaultEncryptionKeyProvider`, `AddSharedKernelAzureKeyVaultCryptography`,
          a full test project, all registered in `Platform.SharedKernel.slnx`) — the package is NOT
          zero-code-on-disk as this section previously (incorrectly) stated. `ProjectReference` added to
          `SharedKernel.ServiceDefaults.csproj`.

    **CACHING GAP CONFIRMED AND CLOSED, IMPLEMENTED AND TESTED (WO-081/P-503, D-37/D-38/C-70,
    shipped 2026-09-08):** re-verified directly against source (not assumed): `01.Core`'s
    `AddSharedKernelAzureKeyVaultCryptography` registers ONE `AzureKeyVaultEncryptionKeyProvider`
    singleton and wires it, RAW and UNCACHED, as all three of `IEncryptionKeyProvider`/
    `IEnvelopeEncryptionProvider`/`IEncryptionKeyProviderProbe` — `01.Core`'s own XML doc says
    plainly "registers no caching decorator... wrap explicitly if desired." This composition-root
    method is the deliberate, designated place to make that choice. `cacheTtl` (new optional
    parameter): unless it resolves to `TimeSpan.Zero` (explicit opt-out, mirrors
    `K8sServiceDiscoveryOptions.EndpointCacheTtlSeconds`'s "0 disables" convention from
    `11.Communication`), this method (1) registers `SharedKernel.Cryptography.Symmetric
    .CachedEncryptionKeyProvider` (`01.Core`, P-446) as its OWN concrete singleton, wrapping the raw
    `AzureKeyVaultEncryptionKeyProvider`, and (2) re-registers ONLY `IEncryptionKeyProvider` to
    resolve the cached wrapper — the BCL container resolves the LAST registration for a
    single-instance request, so this cleanly supersedes `01.Core`'s own raw `IEncryptionKeyProvider`
    registration for that one interface, without touching `01.Core`'s own call. `null` → internal
    5-minute default; a negative value throws `ArgumentOutOfRangeException` before any registration
    occurs (mirrors `CachedEncryptionKeyProvider`'s own constructor guard — `01.Core` ships NO
    default TTL of its own, by design, so the default lives entirely in this package).
    **`IEnvelopeEncryptionProvider` and `IEncryptionKeyProviderProbe` are DELIBERATELY LEFT ON THE
    RAW PROVIDER, UNTOUCHED** — two independent, load-bearing reasons: `CachedEncryptionKeyProvider`
    implements `IEncryptionKeyProvider` ONLY, never `IEnvelopeEncryptionProvider` (envelope
    wrap/unwrap is a real per-call crypto operation against the vault, not a cacheable key lookup —
    there is nothing to cache); and a readiness/health probe caching its own reachability signal
    would defeat the entire purpose of `AddKeyVaultKeyProviderReadinessCheck` below — a probe MUST
    always observe live KMS state, never a stale cache entry. `CachedEncryptionKeyProvider` is ALSO
    registered as its own resolvable concrete type (not merely behind `IEncryptionKeyProvider`) so a
    consuming service's `06.Persistence` builder chain can target it explicitly via
    `.WithExternalEncryptionKeyProvider<CachedEncryptionKeyProvider>()`, alongside the
    still-independently-available raw `.WithExternalEncryptionKeyProvider<AzureKeyVaultEncryptionKeyProvider>()`
    — see the AC#3-refutation note immediately below for why this domain does not and must not wire
    that connection itself. **Confirmed this can never unlock any synchronous path:**
    `CachedEncryptionKeyProvider` never implements `01.Core`'s `ISynchronousEncryptionKeyProvider`
    marker (`SK.01.P492`) regardless of cache warmth or TTL — the value here is strictly for ASYNC
    consumers (general-purpose `ISymmetricEncryptionService.EncryptAsync`/`DecryptAsync` callers,
    and `06.Persistence`'s `PreWarmedEncryptionKeyProvider.WarmCurrentAsync`/`WarmVersionAsync` if a
    consumer opts into the cached `TProvider`).

    **STANDING DESIGN NOTE — do not "fix" this back to automatic (WO-081/P-503, D-37):** an earlier
    phase draft assumed this method's registration should make a consuming service automatically
    satisfy `06.Persistence`'s P-498 startup fail-fast check "without extra manual wiring." That
    premise was checked against `06.Persistence`'s own design-locked fix (`SK.06`'s D-131, the same
    WO-081 wave) and found FALSE: the SEVERE defect P-498 corrects is precisely an accidental
    ambient-registration-order collision between `.WithEncryption()` and THIS method both wiring the
    SAME unkeyed `IEncryptionKeyProvider` — `06.Persistence`'s fix eliminates that collision
    STRUCTURALLY by never resolving the ambient slot at all, replacing it with an EXPLICIT,
    consumer-driven opt-in call (`.WithExternalEncryptionKeyProvider<TProvider>()`) the consuming
    service makes on its OWN `EfCorePersistenceBuilder`. If `13.ServiceDefaults` ever tried to make
    that connection automatic, it would recreate the exact hazard `06.Persistence` just eliminated. A
    service wanting BOTH KMS-backed general-purpose crypto (via this method) AND KMS-backed
    persistence-layer column encryption must call `.WithExternalEncryptionKeyProvider<TProvider>()`
    itself, explicitly, in its own `06.Persistence` builder chain — this is coordination with
    `06.Persistence`'s design, not a gap in this one.

    **CROSS-DOMAIN HAZARD — KMS-backed provider + `07.Messaging` payload encryption
    (WO-081/P-503, D-39, documentation-only, no code fix — out of this domain's jurisdiction):**
    this wave's calibration finding (verified by `07.Messaging` via reflection against the installed
    MassTransit assembly) is that `07.Messaging`'s payload-encryption serializer path is
    HARD-SYNCHRONOUS with no async overload. A KMS-backed `IEncryptionKeyProvider` — cache-wrapped
    or not, per the finding directly above — can NEVER satisfy `01.Core`'s `IsGenuinelySynchronous`
    gate. **A service that enables both `AddSharedKernelKeyVaultKeyProvider()` and `07.Messaging`'s
    `WithPayloadTransform()` against the SAME ambient `IEncryptionKeyProvider`/
    `ISymmetricEncryptionService` breaks UNCONDITIONALLY, FOREVER (`NotSupportedException` on every
    message) — `01.Core`'s `SK.01.P492` sync-gate has already shipped** — not a performance hazard, a hard
    functional break, identical in shape to the one `06.Persistence`'s D-126/D-131 found and fixed
    structurally for the persistence path. `13.ServiceDefaults` is the one composition-root location
    where a service wires both together, so this is documented here, IN CAPITALS, rather than
    silently left for a service author to discover in production: keep messaging payload encryption
    on an independently-configured, config-backed `IEncryptionKeyProvider` — never the ambient slot
    this method registers — until/unless `07.Messaging` ships its own keyed-DI isolation mirroring
    `06.Persistence`'s D-131 (that domain's own P-499 territory, not this one's).

AddKeyVaultKeyProviderReadinessCheck  — see the Health check composition subsection above for the full
    signature. IMPLEMENTED and tested, shipped 2026-09-04, in `HealthChecks/`
    (`KeyVaultKeyProviderReadinessHealthCheck.cs`/`KeyVaultKeyProviderReadinessHealthCheckExtensions.cs`) —
    `01.Core` shipped P-487 (`IEncryptionKeyProviderProbe`/`EncryptionKeyProviderHealth`), closing the
    upstream design gap this method was previously blocked on. This closes WO-068/P-449 end to end.
    UNAFFECTED by the caching wrap above — resolves only `IEncryptionKeyProviderProbe`, which
    (per the note above) is never cache-wrapped.
```

#### Opt-in culture resolution (`Localization/`, WO-078/P-483 — IMPLEMENTED and tested, shipped 2026-09-04)

```text
LocalizationResolutionStrategy  (enum)
    UserPreference | TenantDefault | AcceptLanguageHeader

LocalizationResolutionOptions
    .StrategyOrder                                              → IReadOnlyList<LocalizationResolutionStrategy>
        Default: [UserPreference, TenantDefault, AcceptLanguageHeader]. **Signed-signal-before-
        unsigned-header, deliberately mirroring WO-061/P-393's corrected `[Claim, Header, Database]`
        tenant-resolution order** — an authenticated user's own stored preference must outrank a
        browser's `Accept-Language` default for the identical reason a signed JWT tenant claim outranks
        an unsigned `X-Tenant-Id` header. XML docs must cite WO-061's lesson explicitly so a future
        session does not "optimize" this back.
    .UserPreferenceClaimType                                    → string?   (default: null)
        **No default tied to any one identity provider's claim-naming convention** — mirrors
        `MtlsForwardedHeaderOptions.HeaderName`'s "never a vendor-guessed default" rule (WO-058). When
        `null`, the `UserPreference` step is skipped cleanly, never throws.

AddSharedKernelLocalization(this IHostApplicationBuilder builder, Action<LocalizationResolutionOptions>? configure = null)
                                                                   → IHostApplicationBuilder
    Wraps, **never reimplements**, ASP.NET Core's own `RequestLocalizationMiddleware` — configures
    `RequestLocalizationOptions.RequestCultureProviders` with a custom `IRequestCultureProvider`
    implementing the `UserPreference`/`TenantDefault` steps ahead of the BCL's own
    `AcceptLanguageHeaderRequestCultureProvider` for the `AcceptLanguageHeader` step, then calls the
    real `UseRequestLocalization()` under the hood.
    Resolution order at request time: (1) `UserPreference` — reads `IUserContext.Claims`
    (`12.Security.Abstractions`, already referenced) for `.UserPreferenceClaimType`, skipped if
    unconfigured or absent; (2) `TenantDefault` — resolves `ITenantCatalog` (P-471,
    `SharedKernel.MultiTenancy`, see below) **optionally** via `IServiceProvider.GetService<ITenantCatalog>()`,
    calls `.GetByIdAsync(AmbientTenantProvider.TenantId, ct)?.DefaultCulture`, **skips cleanly (never
    throws) when `ITenantCatalog` was never registered** — the phase's own explicit acceptance
    criterion; (3) `AcceptLanguageHeader` — delegates to the BCL provider.
    A startup-time `Warning` (`ServiceDefaultsLog.LocalizationNoDynamicStrategyCanResolve`, EventId
    `13004` — the next sequential `EventId` in the `13000`–`13099` sub-block) fires when neither
    `.UserPreferenceClaimType` is configured nor an `ITenantCatalog` is registered in DI, mirroring
    this platform's "misconfiguration surfaces at startup, not silently" convention. Wired via the same
    `PostConfigure<ILoggerFactory>` + `.ValidateOnStart()` shape `MtlsForwardedHeaderExtensions`
    (P-394/EventId `13003`) already established. Deliberately independent of whether
    `AcceptLanguageHeader` is present in `.StrategyOrder` — it flags "did you forget to configure the
    smart per-user/per-tenant steps," not "can this host resolve any culture at all."
    **SHIPPED: the first-ever intra-domain compiled reference from `SharedKernel.ServiceDefaults` to
    `SharedKernel.MultiTenancy`** (previously fully independent sibling packages within this domain,
    composed only side-by-side at a consumer's own `Program.cs`) — a deliberate design decision, not an
    oversight, since resolving `ITenantCatalog`'s type requires it. One-directional only
    (`ServiceDefaults` → `MultiTenancy`, never the reverse).
    IMPLEMENTATION NOTE: `AddSharedKernelLocalization` builds one `IRequestCultureProvider` per
          configured `.StrategyOrder` entry — two new `internal` types
          (`UserPreferenceRequestCultureProvider`, `TenantDefaultRequestCultureProvider`) composed
          alongside the real BCL `AcceptLanguageHeaderRequestCultureProvider` — so any `.StrategyOrder`
          permutation is honored, not just the default. Does NOT itself call `UseRequestLocalization()`
          (an `IApplicationBuilder`/`WebApplication` extension, unreachable from `IHostApplicationBuilder`
          composition-time code) — it only configures `RequestLocalizationOptions`; the consumer still
          calls the real `app.UseRequestLocalization()`, mirroring `AddSharedKernelMultiTenancy`'s
          "register services here, wire middleware separately" split.
    NAMESPACE GOTCHA (recorded so a future session does not lose time rediscovering it):
          `RequestLocalizationOptions` lives in namespace `Microsoft.AspNetCore.Builder`, NOT
          `Microsoft.AspNetCore.Localization` (where `IRequestCultureProvider`/`ProviderCultureResult`/
          `AcceptLanguageHeaderRequestCultureProvider` actually live) — an explicit
          `using Microsoft.AspNetCore.Builder;` is required, same class of gotcha as
          `AddRateLimiter`/`RateLimiterOptions` living in `Microsoft.AspNetCore.Builder` rather than
          `Microsoft.Extensions.DependencyInjection`.
    RESOLVED AND SHIPPED (2026-09-04): re-examined and confirmed NOT cross-domain-blocked despite the
          root state-map listing "Depends on: P-482, P-471". P-471 landed intra-domain, same session
          (see `SharedKernel.MultiTenancy` below). `01.Core`'s `SharedKernel.Localization`
          (`ILocalizationCatalog`, P-482) is confirmed NOT a functional/compile-time dependency of this
          method at all — it is consumed by `14.Presentation`'s `Error.ToProblemDetails()` (P-484), not
          here.
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
        Default (WO-061/P-393 — IMPLEMENTED and tested, shipped 2026-08-19; SECURITY-MOTIVATED, do not
        reorder without a security review): [TenantResolutionStrategyNames.Claim,
        TenantResolutionStrategyNames.Header, TenantResolutionStrategyNames.Database].
        SUPERSEDES the prior default [Header, Claim, Database] — that ordering let an unsigned,
        caller-supplied X-Tenant-Id header outrank a cryptographically-verified JWT tenant claim
        for the same request, since TenantResolutionMiddleware takes the first strategy that
        resolves and stops. ClaimTenantResolutionStrategy returns null for any unauthenticated
        request or a token with no tenant claim, so the pre-existing B2B/API-key header-only path
        is UNAFFECTED by this reorder — only a request that is both authenticated with a tenant
        claim AND carries a different X-Tenant-Id header changes behavior, and it changes to the
        secure outcome (the claim wins). The first strategy in this order whose TryResolveAsync
        returns non-null wins, matched against each registered strategy's StrategyName (not its
        CLR type name). A service with no tenant directory database simply omits "Database" from
        the configured order — no code change, no null-reference risk. A consuming service's
        custom ITenantResolutionStrategy is reachable by adding its declared StrategyName to this
        list. A service that already explicitly configures its own StrategyOrder via
        AddSharedKernelMultiTenancy(options => ...) is unaffected by this default change entirely.
```

#### Startup-time configuration validation (`Extensions/`, WO-061/P-396 — IMPLEMENTED and tested, shipped 2026-08-19)

```text
TenantResolutionOptionsValidator  (sealed class, implements IValidateOptions<TenantResolutionOptions>)
    Constructor-injects IEnumerable<ITenantResolutionStrategy> — a genuine DI-aware cross-check,
    not a static allowlist of the three platform-shipped strategy names, so a consumer's own
    custom ITenantResolutionStrategy is correctly recognized too. Validate(...) fails (naming the
    specific offending StrategyOrder entry, and listing the currently-registered StrategyNames) for:
      (a) an empty StrategyOrder;
      (b) any StrategyOrder entry whose value matches no registered strategy's StrategyName.
    Registered by AddSharedKernelMultiTenancy via .AddOptions<TenantResolutionOptions>()
    .ValidateOnStart() — a misconfigured host fails at IHost.StartAsync(), not on first request.
    Mirrors MtlsForwardedHeaderOptions.HeaderName's existing fail-fast precedent, extended to a
    DI-aware cross-check that precedent didn't need. Closes a silent-total-tenant-resolution-
    outage failure mode: a typo'd/stale StrategyOrder entry today produces zero signal — the
    service starts normally, every request resolves Guid.Empty, and TenantedDbContext's global
    filter matches zero rows, indistinguishable from "working correctly, no rows exist" without
    deep investigation.
```

#### Opt-in tenant-existence/active-status validation seam (`Resolution/`, WO-061/P-400 — IMPLEMENTED and tested, shipped 2026-08-19)

```text
ITenantStatusValidator
    .IsActiveAsync(Guid tenantId, CancellationToken ct)           → Task<bool>
    NOTE: A locally-owned opt-in seam — mirrors 05.Application's IAuthorizationContext/
          IUnitOfWork bridge pattern, never a direct reference to a specific persistence/cache
          technology. No default implementation ships; the consuming service bridges it to its
          own tenant directory/cache at its own composition root. This domain resolves it as an
          OPTIONAL DI service (null = "not registered", not "registered but unimplemented").
          TenantResolutionMiddleware.InvokeAsync calls IsActiveAsync(tenantId, ct) after any
          strategy resolves a non-Guid.Empty tenant ID, and only when a validator is registered; a
          false result is treated identically to "no strategy resolved a tenant" — the existing
          Guid.Empty fail-closed path, reusing MultiTenancyLog.TenantNotResolved rather than a
          distinct message, so an inactive tenant is deliberately indistinguishable from an absent
          one to every downstream consumer (no new tenant-existence information-disclosure
          surface). When unregistered (the default), the check is skipped entirely — zero added
          latency, zero behavior change. DatabaseTenantResolutionStrategy already provides
          equivalent protection by construction (its directory lookup IS an existence check) —
          registering ITenantStatusValidator alongside DB-isolation resolution is redundant but
          harmless, never double-fails-closed in a way that breaks anything.
```

#### Tenant catalog (`Catalog/`, WO-075/P-471/P-472 — IMPLEMENTED and tested, shipped 2026-09-04)

```text
TenantStatus  (enum)
    Active | Suspended | Offboarded

TenantIsolationMode  (enum)
    Shared | Dedicated
    NOTE: A prose-only mirror of 06.Persistence's existing tenant-isolation vocabulary — no new
          06.Persistence reference is introduced by this enum's existence.

TenantDescriptor  (sealed record)
    .TenantId                                                   → Guid
    .DisplayName                                                → string
    .Status                                                     → TenantStatus
    .IsolationMode                                               → TenantIsolationMode
    .DefaultCulture                                             → string?
        A bare BCL culture-name string, forward-compatible for WO-078/P-483's AddSharedKernelLocalization
        — NO localization logic exists anywhere in this phase.
    .Settings                                                   → IReadOnlyDictionary<string,string>
        An extensible feature/config bag.

ITenantCatalog
    .GetByIdAsync(Guid tenantId, CancellationToken ct)            → Task<TenantDescriptor?>
    .GetByResolutionKeyAsync(string resolutionKey, CancellationToken ct) → Task<TenantDescriptor?>
        Host/claim-value/header-value lookup, mirroring the existing StrategyOrder resolution shapes —
        the caller supplies whatever raw value a resolution strategy already extracted; this contract
        does not re-derive it.
    READ-ONLY, IN CAPITALS IN THE XML DOCS: TENANT PROVISIONING/ONBOARDING (CREATING A NEW TENANT) IS
        OUT OF SCOPE — A CONSUMING SERVICE'S OWN TENANT-MANAGEMENT SURFACE OWNS WRITES; THIS SHIPS
        LOOKUP ONLY.
    RECONCILIATION, NOT A FIFTH UNRELATED CONTRACT: ITenantProvider (12.Security.Abstractions, resolves
        *identity*), ICurrentTenantService (06.Persistence-local seam), and ITenantCacheService
        (02.Caching.Abstractions) are all confirmed unchanged by this phase — none of the three needs
        the full descriptor, all three operate on a bare TenantId Guid.

CatalogTenantStatusValidator  (sealed class, implements ITenantStatusValidator above)
    .IsActiveAsync(Guid tenantId, CancellationToken ct)           → Task<bool>
        Implemented purely in terms of `(await catalog.GetByIdAsync(tenantId, ct))?.Status ==
        TenantStatus.Active` — a tenant absent from the catalog entirely is FAIL-CLOSED, treated
        identically to Suspended/Offboarded, never treated as active. The first real default
        implementation of the ITenantStatusValidator seam above, which shipped WO-061/P-400 with no
        deliverable path to one.

DatabaseTenantCatalog  (sealed class, implements ITenantCatalog)
    Reuses the exact IDbConnectionFactory/parameterized-Dapper-query pattern
    DatabaseTenantResolutionStrategy already established — not a second, independently-invented
    data-access path. No string-built SQL, mirroring the existing hard rule for
    DatabaseTenantResolutionStrategy.

CachedTenantCatalog  (sealed class, decorator over any ITenantCatalog)
    .InvalidateTenantAsync(Guid tenantId, CancellationToken ct)   → Task
        Removes the cache entry immediately, bypassing the TTL entirely — a consuming service calls
        this right after changing a tenant's status.
    .WithCrossInstanceInvalidation(ICacheInvalidationBus bus)     → CachedTenantCatalog
        Opt-in, disabled by default. Publishes an invalidation signal on every local
        InvalidateTenantAsync call. A **direct, compiled** reference to 02.Caching.Abstractions
        (never a bridged local seam, unlike 05.Application/07.Messaging's bridge-pattern for
        cross-domain seams) since 13.ServiceDefaults's layering ceiling already legally covers
        02.Caching. A consumer who never calls this method pulls in no ICacheInvalidationBus DI
        registration and, since only the interface is referenced (never the concrete .Redis.PubSub
        package), no Redis surface at all.
    GENUINE FINDING, confirmed 2026-09-04 by reading the shipped 02.Caching.Abstractions source directly
        (not assumed from its prose): ICacheInvalidationBus exposes PUBLISH-ONLY members
        (PublishKeyInvalidationAsync/PublishTagInvalidationAsync/PublishBroadcastInvalidationAsync/
        PublishInvalidationAsync) — it has NO subscribe/receive surface at all. Receiving an invalidation
        signal is entirely internal 02.Caching plumbing wired against that domain's own ICacheService/
        FusionCache, never exposed generically to an arbitrary consumer. So "subscribes to invalidate the
        local cache entry when a signal arrives from another replica" (this row's own original design
        text) cannot be implemented by CachedTenantCatalog calling anything ON the bus itself.
    .HandleCrossInstanceInvalidationSignal(Guid tenantId)          → void
        The resolution: a new public, non-publishing entry point. A consumer wires their OWN Redis
        Pub/Sub subscription (e.g. 02.Caching.Redis.PubSub's IRedisChannelService.SubscribeAsync, set up
        at their own composition root, outside this domain) and calls this method on receipt of a signal
        for this catalog's channel. Never re-publishes — calling it does not echo the signal back onto
        the bus, which would loop forever across replicas. THIS PATTERN — bus-is-publish-only, a
        catalog/cache-side receive method the consumer wires manually — applies to ANY future consumer of
        ICacheInvalidationBus wanting genuine cross-instance invalidation, not just this one; a generic
        receive/subscribe surface on ICacheInvalidationBus itself does not exist as of this writing (a
        possible future 02.Caching capability gap, flagged for arch-lead/caching-arch-planner, out of
        this domain's authority to add).
    A bounded, SHORT default TTL — mirroring K8sServiceDiscoveryOptions.EndpointCacheTtlSeconds's
        30-second-default reasoning. AN UNBOUNDED OR PURELY-TTL-BASED CACHE LETS A SUSPENDED TENANT
        KEEP OPERATING UNTIL THE TTL EXPIRES — A CORRECTNESS BUG FOR A FINTECH-GRADE PLATFORM, NOT A
        PERF TRADEOFF. CatalogTenantStatusValidator above must compose with CachedTenantCatalog with
        ZERO code changes — it depends only on the ITenantCatalog interface, never a concrete
        implementation type.
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

- **(WO-084) `SharedKernel.ServiceDefaults` references no other SharedKernel package — ever.** Every service on the platform restores whatever the composition base references; before WO-084 it carried fourteen `ProjectReference`s and a project referencing it alone restored 25 SharedKernel projects and 73 NuGet packages (MassTransit, Azure Service Bus, Microsoft.Identity.Web, EF Core, Temporalio, Quartz, StackExchange.Redis), measured by restore against `project.assets.json`; afterwards 1 project and 10 packages, all OpenTelemetry. New integration code that needs another SharedKernel package goes in a `SharedKernel.ServiceDefaults.*` integration package, never the base. Locked by `CompositionBaseIsolationTests` in two layers, **both required**: a metadata test on the compiled assembly catches integration code creeping back, and a project-file test catches what the metadata test is blind to — an **unused** reference, which leaves no trace in IL yet still lands in every consumer's restore (exactly how `SharedKernel.Messaging.MassTransit` and `SharedKernel.Primitives` survived unused on the base), and a reference used only for a **`const`**, whose value the compiler inlines. Measured by perturbation: unused reference → only the project-file test fails; const-only use → only the project-file test fails; genuine type use → both fail; a heavyweight `PackageReference` → only the allowlist test fails.
- **(WO-084) An integration package references the base plus only what its own integration needs — and never another integration package.** Most need one package; `.Persistence` needs the abstractions and EF Core, `.Localization` needs `MultiTenancy` and `Security.Abstractions`. No integration calls another, so none may depend on another. For the same reason, an integration's XML docs refer to a sibling integration's types as `<c>Name</c>`, never `<see cref>` — a resolvable cref would require the reference. (35 such crefs were converted when the split turned them into `CS1574` errors.)
- **(WO-084) Naming a new integration package:** `SharedKernel.ServiceDefaults.` + the integrated package's capability segment, plus its provider segment where that package is provider-specific — **then check the path length.** The name appears three times in `…\{Name}\{Name}.Tests\obj\Release\net10.0\{Name}.Tests.dll`; at the reference clone path `C:\Github\platform-shared-kernel` that path must not exceed 245 characters, the longest the repository already reached. `SharedKernel.ServiceDefaults.Configuration.KeyVault.Azure` reached 261 — past Windows' 260-character `MAX_PATH` — and its build produced no assembly and failed with `MSB3030`, so both Key Vault packages dropped the `.Azure` segment ("Key Vault" already names the vendor), landing at 243 and 240.
- **(WO-084) Readiness checks chain onto `builder.Services.AddHealthChecks()`, never onto a second `AddSharedKernelHealthChecks()`.** `AddServiceDefaults()` already calls `AddSharedKernelHealthChecks()`, which registers the `"startup"` check; calling it again registers `"startup"` twice and the application throws `ArgumentException: Duplicate health checks were registered with the name(s): startup` at startup. Measured by executing the base README's pre-WO-084 Quick Start, which documented exactly that call, as did the Key Vault readiness sample in this file and in that README — all three corrected.
- **(WO-084) Shared helpers an integration package needs from the base are public API, never `InternalsVisibleTo`.** `HealthCheckRegistrationLogging` was made public for that reason: internals exposed to a separately-published package bind it to one exact base build, and NuGet's minimum-version resolution lets base and integration drift apart, which surfaces as `MissingMethodException` at runtime rather than at compile time. Each integration package grants `InternalsVisibleTo` only to its own test project.
- **(WO-084) EventId allocation for the ServiceDefaults package family.** The base and every `SharedKernel.ServiceDefaults.*` integration package **share** `13000`–`13099`, allocated one EventId at a time and never reused; `SharedKernel.MultiTenancy` keeps `13100`–`13199`. The family cannot take one 100-wide sub-block per package: fifteen packages would need fifteen, and `01.Core`'s `13000`–`13999` domain block holds ten. Current allocation — `13000`/`13001`/`13003` `SharedKernel.ServiceDefaults.Security.Mtls` (`MtlsLog`); `13002` base (`ServiceDefaultsLog`); `13004` `SharedKernel.ServiceDefaults.Localization` (`LocalizationLog`). All five predate the split and kept their numbers. **Next free: `13005`.**
- **Phase build order:** Scaffold (real project/package references replacing the bare `.csproj` stubs) must land before any Core work. Within Core, the foundation (`AddServiceDefaults`, `AddSharedKernelTelemetry`, `AddSharedKernelHealthChecks` + the live/ready split, `StartupGate`/`StartupGateHealthCheck`, and the full `SharedKernel.MultiTenancy` surface) must land before any dependency-specific extension (`AddRedisHealthCheck`, `AddCacheReadinessCheck`, `WithCachingTelemetry`, `AddMessagingReadinessCheck`, `WithMessagingTelemetry`) — every dependency-specific check extends the `IHealthChecksBuilder` returned by `AddSharedKernelHealthChecks()`, so that base must exist first. `WithMessagingTelemetry()` additionally depended on `07.Messaging`'s P-172 (the `"SharedKernel.Messaging"` `ActivitySource` definition) — this landed (`SK.07.OTel` 8/8 `●`) and `WithMessagingTelemetry()` is now implemented (C-19, `SK.13.Core` 28/28 `●`); the cross-domain gate is fully resolved and requires no further check by future agents. `AddSearchReadinessCheck`/`WithSearchTelemetry` (C-37/C-38, WO-044/P-277), `AddVectorStoreReadinessCheck`/`WithIntelligenceTelemetry` (C-39/C-41, WO-045/P-285), `AddWorkflowReadinessCheck`/`WithWorkflowTelemetry` (C-42/C-43, WO-046/WO-047), and `AddMessagingReadinessCheck` (C-46, WO-054/P-351) are all implemented and tested (`SK.13.Core` 46/46 `●`, the permanently-retracted C-40 remaining `—`). `AddOrchestrationReadinessCheck` is retracted (WO-047/P-291) — permanently out of scope, never to be implemented; see the Interface Contracts NOTE on that method. `AddRabbitMqMessagingHealthCheck`/`AddAzureServiceBusMessagingHealthCheck` are retired outright, removed from shipped code, and replaced by `AddMessagingReadinessCheck` (WO-054/P-351) — see the Interface Contracts NOTE for the full retraction rationale. (WO-028) The `ITenantResolutionStrategy.StrategyName` contract member and `TenantResolutionStrategyNames` constants class must land before the cached-lookup refactor in `TenantResolutionMiddleware`, since the cache is keyed by `StrategyName` — implement in the order `StrategyName` contract → constants class → three platform strategies updated → middleware lookup refactor.
- The liveness/readiness tag split is the central invariant of this domain: any check that depends on an external system (database, cache, message broker) is tagged `"ready"` and **never** `"live"`. The `"/health/live"` endpoint must answer only "is this process alive" — never "are this process's dependencies alive."
- Every dependency-specific health check (`AddRedisHealthCheck`, `AddMessagingReadinessCheck`, `AddDatabaseReadinessCheck<TContext>`, `AddDapperDatabaseReadinessCheck`, `AddCacheReadinessCheck`) is an explicit opt-in call on `IHealthChecksBuilder`. `AddServiceDefaults()` and `AddSharedKernelHealthChecks()` register **only** the base endpoint mappings and `StartupGateHealthCheck` — never a dependency-specific check.
- `AddCacheReadinessCheck` must report `Degraded`, not `Unhealthy`, on a cache probe failure — FusionCache's L1 fail-safe may still be correctly serving stale data, and an `Unhealthy` readiness result removes the pod from rotation unnecessarily during a transient Redis blip.
- **`AddMessagingReadinessCheck` must never construct its own connection to the message broker — it resolves only `IMessageBusProbe` (`07.Messaging.MassTransit`'s already-configured bus) from DI** (WO-054/P-351, implemented and tested, 2026-08-07). This corrects and supersedes the prior version of this rule, which permitted messaging health checks to "read their connection details from the already-resolved options objects (`RabbitMqBusOptions`, `AzureServiceBusOptions`)" and construct their own connection from them — ground-truth verification found this is exactly what the retired `AddRabbitMqMessagingHealthCheck`/`AddAzureServiceBusMessagingHealthCheck` did, and it is the confirmed defect this rewrite exists to eliminate: a second, independently-configured connection can pass or fail independently of the real bus's actual health. Cache health checks (`AddCacheReadinessCheck`) are unaffected by this correction — they still read the registered `ICacheService`/Redis connection directly, which is not a second independent connection (there is only one `ICacheService` registration to begin with).
- Every method in the `With*Telemetry` family — `WithMessagingTelemetry()`, `WithCachingTelemetry()`, `WithApplicationTelemetry()`, `WithSearchTelemetry()`, `WithIntelligenceTelemetry()`, `WithWorkflowTelemetry()`, `WithPersistenceTelemetry()`, `WithCommunicationTelemetry()`, and `WithIntegrationTelemetry()` — must be idempotent — calling any of the nine more than once must not register duplicate `ActivitySource`/meter instruments. `WithPersistenceTelemetry()` and `WithIntegrationTelemetry()` are the two tracing-only members (no meter — `06.Persistence`/`15.Integration` each ship no companion `Meter`) — their idempotency requirement still applies to the tracing half. `WithCommunicationTelemetry()` (WO-056/P-365, IMPLEMENTED and tested, shipped 2026-08-12) is the sole member whose idempotency is NOT guaranteed for free by the OTel SDK's by-name dedup — its gRPC half calls `.AddGrpcClientInstrumentation()`, an instrumentation-factory registration rather than a bare `AddSource`/`AddMeter` call. Empirically confirmed already dedup-safe by decompiling the installed package (its backing type is DI-singleton-registered with an internally-guarded one-time subscription) — no custom guard was needed; see the Interface Contracts entry for the full finding.
- `13.ServiceDefaults` never *creates* an `ActivitySource` or custom meter on behalf of another domain — `07.Messaging`'s `"SharedKernel.Messaging"` source, `02.Caching`'s `"SharedKernel.Caching"` source/meter pair, `05.Application`'s `"SharedKernel.Application"` source/meter pair, `09.Search`'s `"SharedKernel.Search"` source/meter pair, `10.Intelligence`'s `"SharedKernel.AI"` source/meter pair, `17.Workflows`'s `"SharedKernel.Workflows"` source/meter pair, `06.Persistence`'s `"SharedKernel.Persistence"` source, and (once `15.Integration`'s WO-064/P-424 ships) `15.Integration`'s `"SharedKernel.Integration"` source are all created in their own domains; this package only wires already-existing sources/meters into the host's `TracerProvider`/`MeterProvider`. This rule holds even though `15.Integration` is a higher-numbered domain than `13.ServiceDefaults` and this domain is otherwise forbidden from referencing it (see the layering rule below) — the wiring needs no `ProjectReference` at all, only a bare string name, so the referencing prohibition and the "wire an already-existing source" pattern do not conflict. Every such wiring extension follows the identical shape: string-name-only `AddSource`/`AddMeter` calls, no `ProjectReference` to the owning domain's concrete assembly when its diagnostics class is `internal` (the common case), and idempotent by construction because the underlying OTel SDK no-ops on a repeated source/meter name. **`WithCommunicationTelemetry()` (WO-056/P-365) is the one documented exception to "string-name-only, no new PackageReference":** it activates two THIRD-PARTY instrumentation sources — `OpenTelemetry.Instrumentation.GrpcNetClient` (a real instrumentation-extension-method call, `.AddGrpcClientInstrumentation()`, requiring a new `PackageReference` on this package itself) and Polly v8's own `"Polly"`-named diagnostics (string-name-only, no new reference) — neither of which is owned by `11.Communication` or any SharedKernel domain; this does not weaken the "never creates a source on another domain's behalf" rule, since neither instrumentation source is created here either, only activated.
- `ClaimTenantResolutionStrategy` must delegate to `SharedKernel.Security.Oidc.OidcTenantProvider` — it must never reimplement claim-name parsing or duplicate `SecurityClaimTypes.TenantId` resolution logic.
- `DatabaseTenantResolutionStrategy` must use parameterized queries exclusively — building SQL by string interpolation or concatenation with request-derived values (host, subdomain) is a hard violation.
- `TenantResolutionMiddleware` must be registered after `UseAuthentication()` in the request pipeline — `ClaimTenantResolutionStrategy` requires a populated `ClaimsPrincipal`.
- `AmbientTenantProvider.TenantId` defaults to `Guid.Empty` and is set exactly once per request by `TenantResolutionMiddleware` — no other component may set it.
- `13.ServiceDefaults` may reference concrete provider packages from `02.Caching`, `06.Persistence`, `07.Messaging`, and `12.Security` (see Layering exception above) — but must never reference `14.Presentation`, `15.Integration`, or `16.Testing`. **By one narrow, individually-named exception granted by `arch-lead` (WO-047)**, `13.ServiceDefaults` may also take a `ProjectReference` to `17.Workflows`'s `SharedKernel.Workflows.Temporal`, scoped **exclusively** to `IWorkflowServiceProbe`/`WorkflowServiceHealth` for `AddWorkflowReadinessCheck` — recorded in the root `CLAUDE.md`'s Layering Rules → Hard rules section and Layering Rules diagram. No other `17.Workflows` type (`TemporalOptions`, `ITemporalClient`, `WorkflowBase`/`ActivityBase`, `IWorkflowDispatcher`, `ITemporalRawClientAccessor`, …) may be reached through this exception — reaching for one is a hard violation of the grant's scope. Outside this one named exception, the dependency direction remains strictly downward (`13` may reference `01`–`12` only); a future domain numbered above `13` wanting the identical pattern requires its own named grant, never an inferred widening of this one.
- No static mutable state anywhere in this domain except `StartupGate`, which is an intentional, narrowly-scoped, thread-safe (`volatile`) singleton gate — not a general-purpose static cache. **`WithCommunicationTelemetry()`'s gRPC-instrumentation idempotency was investigated (C-47, WO-056/P-365, 2026-08-12) and confirmed already dedup-safe by the third-party package's own design (DI `TryAddSingleton` + internally-guarded one-time subscription) — no custom guard was added, so this rule's list of exceptions stays at exactly one (`StartupGate`).**
- **`WithCommunicationTelemetry()` (WO-056/P-365, IMPLEMENTED and tested, shipped 2026-08-12) must never map `PendingWriteCount`-shaped or resilience-internal detail onto a health decision — it is telemetry-only, not a health check.** It never references `GrpcClientOptions`/`RestClientOptions`/`RestResilienceOptions` or any other concrete `11.Communication` options type, and takes **zero `ProjectReference`** to `11.Communication.Grpc`/`.Rest` — the gRPC half is activated purely via the `OpenTelemetry.Instrumentation.GrpcNetClient` package's own runtime `DiagnosticSource` hook, and the Polly half purely via a bare `"Polly"` string name, exactly as `AddStorageReadinessCheck`/`AddSearchReadinessCheck`/`AddVectorStoreReadinessCheck` never reference a concrete provider options type.
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
- **`AddSearchReadinessCheck` (WO-044/P-277, implemented) must never map `SearchIndexHealth.PendingWriteCount` to `Unhealthy` (or `Degraded`)** — per `09.Search/CLAUDE.md`'s own rule, a deep write backlog means search results are stale, not unavailable; failing readiness on it would remove serving capacity exactly when it is most needed. `PendingWriteCount` is surfaced only as informational `HealthCheckResult.Data`, never as a health-status input. Healthy/Unhealthy is decided solely by `Reachable && IndexAddressable && Searchable`.
- **`AddSearchReadinessCheck` must never reference a concrete search provider options type** (`MeilisearchOptions`, `ElasticSearchOptions`) — it resolves only `ISearchIndexProvisioner` (`SharedKernel.Search.Abstractions`) and takes `indexName` as an explicit caller-supplied parameter, the same discipline as `AddRedisHealthCheck`'s connection string and `AddStorageReadinessCheck`'s bucket name. This is what lets one adapter work unmodified against either `.Meilisearch` or `.ElasticSearch`.
- **`WithSearchTelemetry()` is the fourth sibling in the `WithMessagingTelemetry`/`WithCachingTelemetry`/`WithApplicationTelemetry` family and follows the identical string-name-only wiring shape** — no `ProjectReference` to `09.Search` (its diagnostics constants are `internal`-equivalent by convention, mirroring the `MessagingDiagnostics`/`ApplicationDiagnostics` precedent), idempotent by construction. Per this domain's established precedent (WO-027/C-19), it is not implemented until `09.Search`'s `"SharedKernel.Search"` `ActivitySource`/`Meter` are confirmed to actually exist in shipped code — a source cannot be verifiably wired before it exists, even though the string-name call itself would compile harmlessly either way.
- **`AddVectorStoreReadinessCheck` (WO-045/P-285, implemented) must never map `VectorCollectionHealth.PendingWriteCount` to `Unhealthy` (or `Degraded`)** — per `10.Intelligence/CLAUDE.md`'s own rule, a deep write/optimizer backlog means query results may be stale, not unavailable. `PendingWriteCount` is surfaced only as informational `HealthCheckResult.Data`, never as a health-status input. Healthy/Unhealthy is decided solely by `Reachable && CollectionAddressable && Queryable`. Directly mirrors the `AddSearchReadinessCheck`/`SearchIndexHealth.PendingWriteCount` rule above.
- **`AddVectorStoreReadinessCheck` must never reference a concrete vector provider options type** (`QdrantOptions`, `MilvusOptions`) — it resolves only `IVectorCollectionProvisioner` (`SharedKernel.AI.Abstractions`) and takes `collectionName` as an explicit caller-supplied parameter, the same discipline as `AddStorageReadinessCheck`'s bucket name and `AddSearchReadinessCheck`'s indexName. This is what lets one adapter work unmodified against either `.Qdrant` or `.Milvus`.
- **`AddOrchestrationReadinessCheck` is retracted (WO-047, P-291) — permanently out of scope, never to be implemented.** It was to wrap a `ProbeAsync`-shaped member on `10.Intelligence`'s `ICompletionProviderDescriptor` that `10.Intelligence/CLAUDE.md`'s own ratified Interface Contracts listing never actually declared (a confirmed internal inconsistency, not merely implementation lag). `arch-lead` resolved this by retraction rather than inventing the missing member: adding a `ProbeAsync` member would contradict `ICompletionProviderDescriptor`'s own already-shipped "singleton, zero-I/O descriptor" contract, and the only honest alternative — issuing a real, billed completion call to probe reachability — is itself forbidden by Domain Invariant #5. No `HealthCheckNames.Orchestration`/`HealthCheckTags.Orchestration` constants exist or will be added. `13.ServiceDefaults` must never invent another domain's interface member to route around a gap in that domain's own brain — this retraction, not a workaround, is the correct response when the owning domain's own architectural authority (`arch-lead`) confirms the member cannot exist.
- **`WithIntelligenceTelemetry()` is the fifth sibling in the `WithMessagingTelemetry`/`WithCachingTelemetry`/`WithApplicationTelemetry`/`WithSearchTelemetry` family and follows the identical string-name-only wiring shape** — no `ProjectReference` to `10.Intelligence` (its diagnostics constants are per-provider `internal`-equivalent by convention, mirroring the `MessagingDiagnostics`/`ApplicationDiagnostics`/`SearchDiagnostics` precedent), idempotent by construction. Per this domain's established precedent (WO-027/C-19), it is not implemented until `10.Intelligence`'s `"SharedKernel.AI"` `ActivitySource`/`Meter` are confirmed to actually exist in shipped code.
- **`AddWorkflowReadinessCheck` (WO-046/WO-047/P-289, implemented) required resolving a root-level layering conflict before it could ship.** `arch-lead` granted a narrow, individually-named exception permitting `13.ServiceDefaults` a `ProjectReference` to `SharedKernel.Workflows.Temporal` scoped **exclusively** to `IWorkflowServiceProbe`/`WorkflowServiceHealth` — recorded in the root `CLAUDE.md`'s Layering Rules → Hard rules section. No other `17.Workflows` type may be reached through this exception. `WorkerPollersActive` was verified against compiled `WorkflowServiceHealth` source to be a **non-nullable `bool`** — its own XML docs guarantee it is always `true` on a client-only registration, so the Healthy mapping needs no separate `isWorkerHost` parameter; a single `&& WorkerPollersActive` conjunct suffices.
- **`WithWorkflowTelemetry()` is the sixth sibling in the `With*Telemetry` family and follows the identical string-name-only wiring shape** — no `ProjectReference` to `17.Workflows` (its `WorkflowWellKnown` diagnostics constants are referenced by convention only, mirroring the `MessagingDiagnostics`/`ApplicationDiagnostics`/`SearchDiagnostics`/`IntelligenceWellKnown` precedent), idempotent by construction. This method carries **no** layering conflict of its own — string-name wiring needs no compile-time reference to the owning assembly at all, unlike `AddWorkflowReadinessCheck`. Per this domain's established precedent (WO-027/C-19), it is not implemented until `17.Workflows`'s `"SharedKernel.Workflows"` `ActivitySource`/`Meter` are confirmed to actually exist in shipped code.
- **`WithCachingTelemetry()`'s tracing addition (D-15, WO-050/P-305 — IMPLEMENTED and tested, closed 2026-07-29) brings the family's original, first-built member into parity with the five siblings above.** `WithCachingTelemetry` shipped at P-010/WO-003, before this platform's "wire both a tracing source and a meter" convention existed for the `With*Telemetry` family — it wired the `"SharedKernel.Caching"` meter alone from then until this fix. The fix follows the identical string-name-only wiring shape as its siblings: `WithTracing(t => t.AddSource(CachingInstrumentationName))` alongside the existing `WithMetrics(...)` call, no new `ProjectReference` (`SharedKernel.ServiceDefaults.csproj` still references only `SharedKernel.Caching.Abstractions`, never `.FusionCache`), idempotent by construction. The pre-existing `private const string CachingMeterName` was renamed to `CachingInstrumentationName` (same `"SharedKernel.Caching"` value — a naming-parity rename only, since the constant now backs both signals), matching `SearchInstrumentationName`/`IntelligenceInstrumentationName`/`WorkflowInstrumentationName`'s naming convention. Per this domain's established precedent (WO-027/C-19), it was not implemented until `02.Caching`'s Phase 41 (`SK.02.OtelTracingSpans`, P-304) shipped the real `"SharedKernel.Caching"` `ActivitySource` on `FusionCacheService` — the blocker was re-verified directly against `02.Caching/state-map.md` (all nine OT-01..OT-09 tasks confirmed `●`) and the compiled `FusionCacheService.cs` source before implementation began.
- **`WithIntegrationTelemetry()` (D-29/C-59, WO-064/P-430 — IMPLEMENTED and tested, shipped 2026-08-21) is the ninth `With*Telemetry` sibling and follows `WithPersistenceTelemetry`'s exact tracing-only shape** — no `ProjectReference` to `15.Integration` (`AddSource(string)` requires no compile-time type reference to the owning assembly at all, regardless of the source's declared accessibility), deliberately no `WithMetrics(...)` call (`15.Integration`'s WO-064/P-424 design ships an `ActivitySource` only, no companion `Meter`), idempotent by construction. Per this domain's established precedent (WO-027/C-19), it was not implemented until `15.Integration`'s `"SharedKernel.Integration"` `ActivitySource` (`WebhookIntegrationActivitySource`) was confirmed to actually exist in shipped code — re-verified directly against `15.Integration/state-map.md` (`SK.15.WO064`'s H-15/H-16 both `●`) and the compiled `Dispatch/WebhookIntegrationActivitySource.cs` source before implementing.
- **The ambient-logging-enrichment mechanism (`BaggageLogRecordProcessor` + `TenantResolutionMiddleware`'s baggage set) is scoped to the HTTP-request path only.** A message-consumption-scope equivalent (tenant/correlation enrichment during MassTransit consumer execution) is explicitly out of this domain's jurisdiction — it would require a parallel mechanism inside `07.Messaging`'s consumer pipeline (e.g., an `IMessageHeaderPropagator`-adjacent filter setting the same `Activity` baggage keys from propagated message headers) and must be dispatched as a separate cross-domain work order if needed, never implemented here as a workaround.
- `13.ServiceDefaults` never takes a `ProjectReference` to `14.Presentation` to support the CorrelationId-on-logs acceptance criterion — `BaggageLogRecordProcessor` reads `Activity.Baggage` generically; verification uses a direct BCL `Activity.SetBaggage(...)` call simulating `14.Presentation`'s own documented mechanism (WO-031), never a real cross-domain reference.
- **`AddMtlsClientCertificate`/`AddMtlsForwardedHeaderCertificate` (WO-058/P-378 — IMPLEMENTED and tested, shipped end to end 2026-08-14) must never reimplement X.509 chain validation, revocation checking, or subject/issuer matching** — both composition surfaces delegate the accept/reject decision exclusively to `12.Security`'s `SharedKernel.Security.Mtls.IMtlsCertificateValidator`, mirroring `ClaimTenantResolutionStrategy`'s delegate-never-reimplement rule for `OidcTenantProvider`. Referencing `SharedKernel.Security.Mtls` needs no named layering exception — it is a concrete `12.Security` provider package, already inside this domain's granted `01`–`12` composition-root range.
- **`MtlsForwardedHeaderOptions.HeaderName` must never carry a default value tied to any one ingress/gateway vendor's convention** (nginx-ingress, Envoy/Istio, and HAProxy each forward a client certificate under a different header name/encoding) — a consuming service must configure it explicitly; an unconfigured value fails fast at startup, never silently guesses a literal.
- A host that calls neither `AddMtlsClientCertificate` nor `AddMtlsForwardedHeaderCertificate` must be byte-identical in behavior to today — Kestrel's default `ClientCertificateMode.NoCertificate` stays untouched and no new middleware enters the pipeline unless explicitly opted in.
- **`TenantResolutionOptions.StrategyOrder`'s default is `[Claim, Header, Database]` — a security-motivated default, not an arbitrary ordering choice, and must never be silently reordered back to `[Header, Claim, Database]` without a security review** (WO-061/P-393, IMPLEMENTED and tested, shipped 2026-08-19). The prior default let an unsigned, caller-supplied `X-Tenant-Id` header outrank a cryptographically-verified JWT tenant claim for the same request — a direct cross-tenant data-access vector, since `AmbientTenantProvider.TenantId` is what `06.Persistence`'s `TenantedDbContext` global filter trusts. `ClaimTenantResolutionStrategy` returning `null` for any unauthenticated/no-claim request is what makes the reorder provably safe for the pre-existing B2B/API-key header-only path.
- **`MtlsForwardedHeaderMiddleware` must ignore a forwarded certificate header from a remote IP outside a configured `TrustedNetworks` allowlist** (WO-061/P-394, IMPLEMENTED and tested, shipped 2026-08-19) — never decode, never pass to `IMtlsCertificateValidator`, never set `HttpContext.Connection.ClientCertificate`, regardless of whether the certificate itself would otherwise validate. When `TrustedNetworks` is left unconfigured, a one-time startup `Warning` must state the trust-boundary risk explicitly — silence is not an acceptable default for a mechanism whose entire purpose is letting a downstream service trust a certificate it never itself negotiated.
- **Every new `[LoggerMessage]` call site in this domain (WO-061/P-395, IMPLEMENTED and tested, shipped 2026-08-19; this domain's first-ever production logging) must never log certificate PEM/DER bytes, a raw JWT, or a raw header value verbatim** — only thumbprint/subject/tenant-id-shaped identifiers. CorrelationId/TraceId/TenantId flow ambiently through the existing OTel logging pipeline, never as an explicit template placeholder in these new statements, per the platform-wide logging convention — except `MultiTenancyLog.TenantResolved`'s `{TenantId}` placeholder, which is a legitimate exception since `TenantId` there IS the message's own payload, not repeated ambient context.
- **`AddSharedKernelMultiTenancy` must fail fast at `IHost.StartAsync()` for an empty or DI-unmatched `TenantResolutionOptions.StrategyOrder`** (WO-061/P-396, IMPLEMENTED and tested, shipped 2026-08-19), via a genuine `IValidateOptions<TenantResolutionOptions>` cross-checking the real DI-registered `ITenantResolutionStrategy` set — never a bare `.Validate(Func<T,bool>)` lambda, which cannot see the container. A misconfigured host must never run indefinitely with tenant resolution silently degraded to "always resolves `Guid.Empty`" — that is a silent, total denial-of-service for every tenant on that service instance.
- **`AddSharedKernelRateLimiting()` (WO-061/P-397, IMPLEMENTED and tested, shipped 2026-08-19) must never be called from `AddServiceDefaults()`** — it is entirely opt-in, mirroring every other dependency-specific extension in this domain, and must never reference `14.Presentation` directly (only a documented `OnRejected`-to-`ProblemDetails` recipe).
- **The `OnRejected` recipe must call `14.Presentation.WebApi`'s real `RateLimitRejectionProblemDetails.Create(HttpContext, TimeSpan?)` helper, never hand-roll a raw `ProblemDetails` object** (WO-063/P-419, D-28 — Tests `●`/Docs `●`, both shipped 2026-08-21). A source-verification pass found the recipe this domain's own `README.md` shipped alongside P-397 predated `14.Presentation`'s P-408 and hand-rolled its own `ProblemDetails` — reproducing, inside this domain's own documentation, exactly the inline-`ProblemDetails`-construction anti-pattern the platform forbids everywhere else, and never setting the `Retry-After` header. This is a **documentation-only correction, not a reopening of the "never a hard reference" rule above** — `AddSharedKernelRateLimiting`'s existing `configure` parameter already gives a consumer everything needed. **The fix is proven, not just documented:** `RateLimitRejectionRecipeTests.cs` drives a real `WebApplication`/`TestServer` host through the corrected recipe and asserts the actual 429/`application/problem+json`/`ProblemDetails`/`Retry-After` output, plus a regression proving the no-recipe call shape is unchanged — via a **test-only** cross-domain `ProjectReference` from `SharedKernel.ServiceDefaults.Tests` to `SharedKernel.Presentation.WebApi` (mirroring the T-43/WO-056 gating-proof-only precedent), never a production reference in either direction. `README.md`'s recipe text and `RateLimitingExtensions.AddSharedKernelRateLimiting`'s XML `<remarks>` are both now corrected to match, naming `RateLimitRejectionProblemDetails` in prose only — no compiled reference added to the production `.csproj`. This closes WO-063/P-419 end to end.
- **`AddSharedKernelKeyVaultConfiguration()` (WO-061/P-398, IMPLEMENTED and tested, shipped 2026-08-19) must fail fast at startup for a misconfigured/unreachable vault, never silently proceed with an empty configuration source** — this is a GATING requirement to be empirically verified against the Azure SDK's actual default behavior, never assumed.
- **`MapDefaultHealthCheckEndpoints`'s `requireAuthorization` parameter (WO-061/P-399, IMPLEMENTED and tested, shipped 2026-08-19) is defense-in-depth, never a substitute for network isolation** — `README.md`/`CLAUDE.md` must state, IN CAPITALS, that `/health/live`/`/health/ready` must be network-restricted at the ingress/`NetworkPolicy` layer in any environment where they are not intentionally public, independent of whether `requireAuthorization` is used.
- **`ITenantStatusValidator` (WO-061/P-400, IMPLEMENTED and tested, shipped 2026-08-19) must be resolved as an optional DI service — `null` means "not registered," never "registered but unimplemented"** — and a `false` result from `IsActiveAsync` must route through the exact same `Guid.Empty` fail-closed path as "no strategy resolved," reusing `MultiTenancyLog.TenantNotResolved` rather than a distinct log message, so an inactive/suspended tenant is deliberately indistinguishable from an absent one to every downstream consumer.
- **`ITenantCatalog` (WO-075/P-471, IMPLEMENTED) is a read-only lookup contract — tenant provisioning/onboarding must never be added to it.** `CatalogTenantStatusValidator.IsActiveAsync` fails closed on a catalog miss (an absent tenant is never treated as active) — the same fail-closed discipline every other tenant-adjacent contract in this domain already follows. `ITenantProvider` (`12.Security.Abstractions`), `ICurrentTenantService` (`06.Persistence`), and `ITenantCacheService` (`02.Caching.Abstractions`) remain unaffected by this contract's existence — it reconciles, never replaces, any of the three.
- **`CachedTenantCatalog`'s default TTL is short and bounded (30s), never unbounded or purely-TTL-based** (WO-075/P-472, IMPLEMENTED) — an unbounded cache entry lets a suspended tenant keep operating until the TTL expires, a correctness bug for a fintech-grade platform, not a perf tradeoff. `InvalidateTenantAsync` bypasses the TTL immediately, proven by a dedicated test. `.WithCrossInstanceInvalidation(ICacheInvalidationBus)` remains opt-in and disabled by default — a consumer who never calls it gains no implicit `02.Caching.Redis.PubSub`-shaped dependency. **`ICacheInvalidationBus` is publish-only** (confirmed by reading `02.Caching.Abstractions` source directly, 2026-09-04) — it exposes no subscribe/receive surface at all, so `CachedTenantCatalog.HandleCrossInstanceInvalidationSignal(Guid)` exists as the receive-side entry point a consumer wires to their own `IRedisChannelService.SubscribeAsync` (or equivalent); it must never re-publish on receipt, or two replicas would echo an invalidation signal back and forth forever.
- **`AddSharedKernelKeyVaultKeyProvider()` (WO-068/P-449, IMPLEMENTED) must remain distinctly named and distinctly documented from `AddSharedKernelKeyVaultConfiguration()`** — the two must never be merged, aliased, or allowed to read as interchangeable in any doc surface: one wires an `IConfiguration` source, the other registers an `IEncryptionKeyProvider`/`IEnvelopeEncryptionProvider`. It must remain idempotent — the guard lives in THIS package (`services.Any(d => d.ServiceType == typeof(AzureKeyVaultEncryptionKeyProvider))`), never assumed from `01.Core`'s own unconditional-`AddSingleton` registration method.
- **`AddKeyVaultKeyProviderReadinessCheck()` (WO-068/P-449, IMPLEMENTED, shipped 2026-09-04) must never map `EncryptionKeyProviderHealth.IsHealthy == false` to `Degraded`** — `Unhealthy` only, same calibration family as every other raw-connectivity readiness check in this domain. **Resolution of the previously-documented "never invent another domain's interface member" rule:** this domain correctly waited for `01.Core` to ship `IEncryptionKeyProviderProbe`/`EncryptionKeyProviderHealth` itself (P-487) rather than guessing a shape — the rule worked exactly as intended; keep following it for any future cross-domain probe wiring this domain does not yet have a ratified contract for.
- **Before treating any cross-domain blocker in `state-map.md` as still valid, re-verify it directly against the actual repo — do not take a prior session's blocker note on trust.** A confirmed instance (2026-09-04): this file and `state-map.md` both recorded `01.Core`'s `SharedKernel.Cryptography.KeyVault.Azure` as "zero code on disk," but it was in fact fully shipped — the note had simply gone stale between the `arch-lead` dispatch and this implementation session. Mirrors this domain's long-standing "re-verify, never trust the phase-dispatch framing" discipline (see the WO-044/WO-045/WO-046/WO-054 precedents throughout this file's Changelog).
- **`WithSchedulingTelemetry()` (WO-073/P-465, IMPLEMENTED) wires BOTH a tracing source and a meter** — unlike `WithPersistenceTelemetry`/`WithIntegrationTelemetry`'s deliberate tracing-only shape, `19.Scheduling` ships a companion `Meter` alongside its `ActivitySource`, so omitting `WithMetrics(...)` here would be a real gap, not a documented scope decision.
- **`AddSchedulerReadinessCheck` (WO-073/P-466, IMPLEMENTED) must never map `SchedulerServiceHealth.RegisteredJobCount` to `Unhealthy` (or `Degraded`)** — a busy scheduler is not an unhealthy one, directly mirroring the `PendingWriteCount`/`TaskQueueBacklog` rule already established for `AddSearchReadinessCheck`/`AddVectorStoreReadinessCheck`/`AddWorkflowReadinessCheck`. It resolves only `ISchedulerServiceProbe` — never `IScheduledJobRegistry`, `ScheduledCommandJob<TCommand>`, `SchedulingOptions`, `MisfirePolicy`/`OverlapPolicy`, or any other `19.Scheduling` type — through the narrow, already-root-ratified, independently-earned `13→19` grant; reaching for any other type through that `ProjectReference` is a hard violation of the grant's scope. **`ISchedulerServiceProbe.ProbeAsync` returns `SchedulerServiceHealth` directly, never a `Result<T>` wrapper** — confirmed by reading the shipped source, distinct from `IWorkflowServiceProbe`'s shape; do not "fix" this to expect a `Result<T>`.
- **`AddSharedKernelLocalization()`'s `StrategyOrder` default is `[UserPreference, TenantDefault, AcceptLanguageHeader]` — a security-motivated default mirroring `TenantResolutionOptions.StrategyOrder`'s `[Claim, Header, Database]` correction (WO-061/P-393), and must never be silently reordered to put `AcceptLanguageHeader` ahead of `UserPreference`** (WO-078/P-483, IMPLEMENTED). `UserPreferenceClaimType` never carries a default value tied to any one identity provider's claim-naming convention — a consuming service must configure it explicitly, mirroring `MtlsForwardedHeaderOptions.HeaderName`'s identical rule (WO-058). The `TenantDefault` step degrades cleanly (skips, never throws) when `ITenantCatalog` is not registered in DI. `RequestLocalizationOptions` lives in namespace `Microsoft.AspNetCore.Builder`, not `Microsoft.AspNetCore.Localization` — a namespace gotcha worth remembering before adding `using` statements here, same class as `Microsoft.AspNetCore.RateLimiting`'s `AddRateLimiter`/`RateLimiterOptions` living in `Microsoft.AspNetCore.Builder`.
- **`SharedKernel.ServiceDefaults` may now take exactly one intra-domain `ProjectReference` to its sibling package `SharedKernel.MultiTenancy`, one-directional only (never the reverse)** — added for `AddSharedKernelLocalization`'s `ITenantCatalog` consumption (WO-078/P-483). This is the first and, as of this writing, only such reference between the two packages; do not add a second one casually — if a future capability needs one, treat it the same way this one was: a deliberate, individually-documented decision, not a default assumption that the two packages may freely reference each other.

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
    .AddMessagingReadinessCheck()                     // requires 07.Messaging's MessagingBusBuilder
                                                        // .Build() to have already been called so
                                                        // IMessageBusProbe is registered in DI.
    .AddSchedulerReadinessCheck();                     // optional — services hosting 19.Scheduling's
                                                        // scheduling loop in-process

builder.WithMessagingTelemetry();                      // optional — services using 07.Messaging
builder.WithCachingTelemetry();                        // optional — services using 02.Caching
builder.WithApplicationTelemetry();                    // optional — services using 05.Application's pipeline behaviors
builder.WithIntegrationTelemetry();                    // optional — services dispatching webhooks via 15.Integration
                                                        // (tracing only, no companion meter)
builder.WithSchedulingTelemetry();                     // optional — services using 19.Scheduling

var app = builder.Build();

app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>();       // required when AddSharedKernelMultiTenancy() is used — must run after UseAuthentication()

app.MapDefaultHealthCheckEndpoints();                  // "/health/live", "/health/ready"

app.Run();
```

`SharedKernel.MultiTenancy` ships no telemetry or health check wiring of its own — that composition stays in `SharedKernel.ServiceDefaults`.

**Log export + ambient enrichment (WO-041/P-251):** no additional call is required beyond `AddServiceDefaults()` (+ `AddSharedKernelMultiTenancy()` for TenantId enrichment) — OTLP log export, `IncludeScopes`/`IncludeFormattedMessage`, and the `BaggageLogRecordProcessor` wiring all happen automatically inside `AddSharedKernelTelemetry`. Application code never needs to pass `CorrelationId` or `TenantId` as an explicit log template placeholder.

**mTLS client-certificate composition (WO-058/P-378 — IMPLEMENTED 2026-08-14, not shown in the snippet above):** a service terminating TLS directly at Kestrel calls `builder.AddMtlsClientCertificate(ClientCertificateMode.RequireCertificate);`; a service behind an ingress that forwards the client certificate via a header instead calls `builder.AddMtlsForwardedHeaderCertificate(o => o.HeaderName = "ssl-client-cert");` (the header name matching whatever the consuming service's actual ingress emits — never a platform-guessed default) paired with `app.UseMiddleware<MtlsForwardedHeaderMiddleware>();`. Both require `12.Security`'s `SharedKernel.Security.Mtls` to be registered first — typically via `AddMtlsAuthentication<TValidator>()` — so `IMtlsCertificateValidator` resolves from DI; omitting it throws at the first TLS handshake (Kestrel path) or first request (forwarded-header path), not at startup. **Trust-boundary hardening (WO-061/P-394, IMPLEMENTED and tested, shipped 2026-08-19):** `builder.AddMtlsForwardedHeaderCertificate(o => { o.HeaderName = "ssl-client-cert"; o.AddTrustedNetwork(IPNetwork.Parse("10.0.0.0/8")); });` restricts which network path may set the forwarded header — omitting `TrustedNetworks` preserves today's behavior plus a new startup warning.

**Opt-in rate limiting (WO-061/P-397, IMPLEMENTED and tested, shipped 2026-08-19):** `builder.Services.AddSharedKernelRateLimiting();` before `builder.Build()`, then `app.UseRateLimiter();` after `builder.Build()`; attach the stricter policy to sensitive endpoints via `[EnableRateLimiting(RateLimitPolicyNames.Authentication)]`. Entirely opt-in — omitted by default from the snippet above.

**Opt-in Azure Key Vault configuration (WO-061/P-398, IMPLEMENTED and tested, shipped 2026-08-19):** `builder.AddSharedKernelKeyVaultConfiguration(new Uri("https://my-vault.vault.azure.net/"));` called after `AddServiceDefaults()` and before any code reads vault-backed configuration values.

**Startup-time tenant-resolution validation (WO-061/P-396, IMPLEMENTED and tested, shipped 2026-08-19):** no code change required — `AddSharedKernelMultiTenancy()` now fails fast at `IHost.StartAsync()` for an empty or DI-unmatched `StrategyOrder` automatically.

**Hardened health check endpoints (WO-061/P-399, IMPLEMENTED and tested, shipped 2026-08-19):** `app.MapDefaultHealthCheckEndpoints(requireAuthorization: true);` — opt-in, default unchanged; REMEMBER this is defense-in-depth, not a substitute for restricting `/health/*` at the ingress/`NetworkPolicy` layer.

**Tenant catalog (WO-075/P-471/P-472, IMPLEMENTED and tested, shipped 2026-09-04):** `builder.Services.AddScoped<ITenantCatalog>(sp => new CachedTenantCatalog(new DatabaseTenantCatalog(sp.GetRequiredService<IDbConnectionFactory>())));` registers the first real `ITenantStatusValidator` implementation as a byproduct — `builder.Services.AddScoped<ITenantStatusValidator, CatalogTenantStatusValidator>();`. Cross-instance invalidation is a further opt-in: `.WithCrossInstanceInvalidation(sp.GetRequiredService<ICacheInvalidationBus>())` (requires `02.Caching.Redis.PubSub` registered separately by the consumer — never a hard dependency of `SharedKernel.MultiTenancy` itself) — receiving a signal from another replica is a further, separate step: wire your own `IRedisChannelService.SubscribeAsync` and call the resulting `CachedTenantCatalog`'s `.HandleCrossInstanceInvalidationSignal(tenantId)` on receipt (`ICacheInvalidationBus` itself is publish-only — see Interface Contracts above).

**Opt-in Azure Key Vault key-provider registration (WO-068/P-449, IMPLEMENTED and tested, shipped 2026-09-04):** `builder.AddSharedKernelKeyVaultKeyProvider();` — distinct from `AddSharedKernelKeyVaultConfiguration()` above; do not confuse the two in a service's own `Program.cs`. Idempotent — safe to call more than once. Pair it with `builder.Services.AddHealthChecks().AddKeyVaultKeyProviderReadinessCheck();` — `AddHealthChecks()`, never a second `AddSharedKernelHealthChecks()`, which re-registers the `"startup"` check and throws at startup (corrected WO-084) so an unreachable KMS/HSM shows up on `/health/ready` — resolves `01.Core`'s `IEncryptionKeyProviderProbe` (already registered as a byproduct of `AddSharedKernelKeyVaultKeyProvider()`).

**Opt-in culture resolution (WO-078/P-483, IMPLEMENTED and tested, shipped 2026-09-04):** `builder.AddSharedKernelLocalization(o => o.UserPreferenceClaimType = "preferred_culture");` — called after `AddServiceDefaults()`/`AddSharedKernelMultiTenancy()`, before `builder.Build()`; the `TenantDefault` step activates automatically once an `ITenantCatalog` is registered (see above) and degrades cleanly otherwise. The consumer still calls the real `app.UseRequestLocalization();` after `builder.Build()` — this method configures `RequestLocalizationOptions` only, it does not wire the middleware itself.

---

## AOT Compatibility

- OpenTelemetry .NET SDK is largely AOT-compatible as of the versions targeted by `net10.0`; some exporter resource-detectors use reflection-based assembly metadata lookup — this is encapsulated entirely behind `AddSharedKernelTelemetry`/`AddServiceDefaults`, so the AOT blast radius does not reach consuming application code.
- `AspNetCore.HealthChecks.*` community packages vary in AOT readiness by transport; this is not a hard blocker per the repo's pragmatic AOT policy — each opt-in `Add*HealthCheck` method is documented individually as adopted rather than the whole package being declared AOT-unsafe.
- `ITenantResolutionStrategy` implementations (`HeaderTenantResolutionStrategy`, `ClaimTenantResolutionStrategy`, `DatabaseTenantResolutionStrategy`) are plain sealed classes using `Guid.TryParse`, a `StrategyName` get-only property, and parameterized async ADO.NET queries — AOT-safe. `TenantResolutionMiddleware`'s once-computed `StrategyName → ITenantResolutionStrategy` lookup is a plain `Dictionary<string, ITenantResolutionStrategy>` (or `FrozenDictionary`) built from already-resolved DI instances — no reflection involved in the lookup itself.
- `StartupGate` / `StartupGateHealthCheck` are plain classes with a `volatile bool` field — AOT-safe, no reflection.
- No `Activator.CreateInstance`, no `Assembly.Load`, no `MakeGenericMethod`/`Invoke` reflection anywhere in this domain.
- `BaggageLogRecordProcessor` (WO-041/P-251) is a plain sealed class over `System.Diagnostics.Activity.Baggage` (a BCL `IEnumerable<KeyValuePair<string,string?>>`) and `OpenTelemetry.Logs.LogRecord.Attributes` — no reflection, AOT-safe. `TenantBaggageKeys` is a static string-constants class, identical AOT profile to `HealthCheckNames`/`TenantResolutionStrategyNames`.
- `StorageReadinessHealthCheck` (WO-043/P-270, implemented) is a plain sealed class over `IFileStorage`/`Result` (both BCL-primitive-shaped, no reflection) — same AOT profile as `DatabaseReadinessHealthCheck<TContext>`/`CacheReadinessHealthCheck`.
- `SearchReadinessHealthCheck` (WO-044/P-277, implemented) is a plain sealed class over `ISearchIndexProvisioner`/`Result` (both BCL-primitive-shaped per `09.Search/CLAUDE.md`'s own AOT notes, no reflection) — same AOT profile as `StorageReadinessHealthCheck`. `SearchTelemetryExtensions.WithSearchTelemetry` is a plain static class doing string-name `AddSource`/`AddMeter` calls — same AOT profile as `MessagingTelemetryExtensions`/`CachingTelemetryExtensions`/`ApplicationTelemetryExtensions`.
- `VectorStoreReadinessHealthCheck` (WO-045/P-285, implemented) is a plain sealed class over `IVectorCollectionProvisioner`/`Result` (both BCL-primitive-shaped per `10.Intelligence/CLAUDE.md`'s own AOT notes — `VectorValue`, `VectorCollectionHealth`, and the closed `VectorFilter` AST are all `sealed record`/`readonly record struct` over BCL primitives, no reflection) — same AOT profile as `StorageReadinessHealthCheck`/`SearchReadinessHealthCheck`. `IntelligenceTelemetryExtensions.WithIntelligenceTelemetry` is a plain static class doing string-name `AddSource`/`AddMeter` calls — same AOT profile as `MessagingTelemetryExtensions`/`CachingTelemetryExtensions`/`ApplicationTelemetryExtensions`/`SearchTelemetryExtensions`. Note `10.Intelligence/CLAUDE.md` separately documents `Microsoft.SemanticKernel` as a non-AOT-safe dependency encapsulated entirely inside `SharedKernel.AI.SemanticKernel` — that blast radius never reaches this domain, since `13.ServiceDefaults` only ever references `SharedKernel.AI.Abstractions`. (`AddOrchestrationReadinessCheck` is retracted, WO-047/P-291 — no AOT profile applicable, it will never be written.)
- `WorkflowReadinessHealthCheck` (WO-046/WO-047/P-289, implemented) is a plain sealed class over `IWorkflowServiceProbe`/`Result` (per `17.Workflows/CLAUDE.md`'s own AOT notes, `WorkflowServiceHealth` is a `sealed record` over BCL primitives, no reflection) — same AOT profile as `StorageReadinessHealthCheck`/`SearchReadinessHealthCheck`/`VectorStoreReadinessHealthCheck`. `WorkflowTelemetryExtensions.WithWorkflowTelemetry` (string-name-only, no `ProjectReference` needed) is a plain static class doing string-name `AddSource`/`AddMeter` calls — same AOT profile as its five siblings. Note `17.Workflows/CLAUDE.md` separately documents the `Temporalio` native Rust core's RID requirement and its reflection-based default data converter as constraints entirely internal to `SharedKernel.Workflows.Temporal` — that blast radius never reaches this domain, since `13.ServiceDefaults` only ever references `IWorkflowServiceProbe`/`WorkflowServiceHealth` (plain interface/record types) through its narrowly-scoped `ProjectReference` (WO-047).
- `PersistenceTelemetryExtensions.WithPersistenceTelemetry` (WO-051/P-326, implemented) is a plain static class doing a single string-name `AddSource` call, no `ProjectReference` needed (`PersistenceActivitySource` is `internal`) — same AOT profile as its six siblings, minus the `AddMeter` half it deliberately never makes. No corresponding `IHealthCheck` adapter exists for `06.Persistence` in this pass — this method is telemetry-only.
- `CommunicationTelemetryExtensions.WithCommunicationTelemetry` (WO-056/P-365, IMPLEMENTED and tested, shipped 2026-08-12) is a plain static class doing one instrumentation-extension-method call (`.AddGrpcClientInstrumentation()`) plus one string-name `AddMeter` call — no reflection of its own. `OpenTelemetry.Instrumentation.GrpcNetClient` is pinned at a beta version (`1.15.1-beta.1`, matching `11.Communication.Grpc.csproj`'s existing pin) — not a hard AOT blocker per the repo's pragmatic AOT policy (mirrors the `AspNetCore.HealthChecks.*` community-package precedent above), but its AOT readiness has not been independently verified by this domain and should be re-evaluated once the package ships a stable release. Polly v8's resilience telemetry (the `"Polly"` meter half) needs no new package reference at all — it is reached by bare string name only, identical AOT profile to `WithMessagingTelemetry`'s `"MassTransit"` wiring. Empirically confirmed (C-47) Polly v8.4.2 in this exact dependency chain creates no `ActivitySource` — only the `Meter` is wired, so there is no `AddSource("Polly")` call to evaluate for AOT either.
- `MessagingReadinessHealthCheck` (WO-054/P-351, implemented 2026-08-07) is a plain sealed class over `IMessageBusProbe`/`MessageBusHealth` (per `07.Messaging/CLAUDE.md`'s own AOT notes, `MessageBusHealth` is a `sealed record` over two BCL primitives — `bool`/`string?` — no reflection) — same AOT profile as `StorageReadinessHealthCheck`/`SearchReadinessHealthCheck`/`VectorStoreReadinessHealthCheck`/`WorkflowReadinessHealthCheck`. Its removal of `AzureServiceBusHealthCheck` (which used `Azure.Identity.DefaultAzureCredential` — itself not a reflection concern, but a non-trivial third-party SDK surface) and the two retired transport-specific extension methods **reduced** this package's AOT surface — neither `RabbitMQ.Client` nor the Azure SDKs remain referenced at all.
- `AddMtlsClientCertificate`/`AddMtlsForwardedHeaderCertificate`/`MtlsForwardedHeaderMiddleware` (WO-058/P-378, IMPLEMENTED 2026-08-14) are plain types over BCL `System.Security.Cryptography.X509Certificates.X509Certificate2`/`X509CertificateLoader`/`System.Net.Security.SslPolicyErrors` and Kestrel's own `ClientCertificateMode`/`HttpsConnectionAdapterOptions` — no reflection. The async-to-sync bridge (`IMtlsCertificateValidator.ValidateAsync(...)` inside Kestrel's synchronous `ClientCertificateValidation` delegate) is a plain `.GetAwaiter().GetResult()` call plus an `IServiceScopeFactory.CreateScope()` — both ordinary BCL/DI-abstraction calls, no dynamic-dispatch shim, no AOT concern beyond the already-documented latency/thread-pool cost (a runtime-behavior concern, not an AOT one).
- `MtlsForwardedHeaderOptions.TrustedNetworks`/`AddTrustedProxy`/`AddTrustedNetwork` (WO-061/P-394, IMPLEMENTED and tested, shipped 2026-08-19) are plain types over BCL `System.Net.IPNetwork`/`IPAddress` — no reflection, no dynamic dispatch, same AOT profile as the rest of the mTLS surface.
- `ServiceDefaultsLog`/`MultiTenancyLog` (WO-061/P-395, IMPLEMENTED and tested, shipped 2026-08-19) are `[LoggerMessage]` source-generated partial classes — the mechanism is compile-time code generation, not reflection, and is fully AOT-safe by construction (the same reasoning `01.Core`'s WO-041 mandate relies on platform-wide).
- `TenantResolutionOptionsValidator` (WO-061/P-396, IMPLEMENTED and tested, shipped 2026-08-19) is a plain `IValidateOptions<TenantResolutionOptions>` implementation — no reflection. **Implementation note:** captures `IServiceProvider` and creates a fresh `IServiceScope` per `Validate()` call (not a direct constructor-injected `IEnumerable<ITenantResolutionStrategy>`), avoiding a captive-dependency error against the Scoped strategy registrations.
- `AddSharedKernelRateLimiting`/`RateLimitPolicyNames` (WO-061/P-397, IMPLEMENTED and tested, shipped 2026-08-19) wrap the BCL's own `Microsoft.AspNetCore.RateLimiting` — the same AOT profile as any other `Microsoft.AspNetCore.App`-shared-framework API this domain already depends on (e.g. `MapHealthChecks`).
- `AddSharedKernelKeyVaultConfiguration` (WO-061/P-398, IMPLEMENTED and tested, shipped 2026-08-19 — C-56) depends on `Azure.Extensions.AspNetCore.Configuration.Secrets`/`Azure.Identity`, both already referenced in `SharedKernel.ServiceDefaults.csproj` (S-20, Scaffold, shipped 2026-08-19) — not independently AOT-verified by this domain; not a hard blocker per the repo's pragmatic AOT policy (mirrors the `OpenTelemetry.Instrumentation.GrpcNetClient` precedent above), encapsulated entirely behind this one opt-in extension method so the blast radius never reaches a consuming service that doesn't call it.
- `ITenantStatusValidator` (WO-061/P-400, IMPLEMENTED and tested, shipped 2026-08-19) is a plain interface with one `Task<bool>`-returning member — no reflection; `TenantResolutionMiddleware`'s optional resolution via `IServiceProvider.GetService<T>()` is an ordinary DI call, not a reflection-based lookup.
- `IntegrationTelemetryExtensions.WithIntegrationTelemetry` (WO-064/P-430, IMPLEMENTED and tested, shipped 2026-08-21) is a plain static class doing a single string-name `AddSource` call, no `ProjectReference` needed regardless of `WebhookIntegrationActivitySource`'s declared accessibility — same AOT profile as `PersistenceTelemetryExtensions.WithPersistenceTelemetry`, minus the `AddMeter` half it also deliberately never makes.
- `TenantDescriptor`/`TenantStatus`/`TenantIsolationMode`/`ITenantCatalog`/`CatalogTenantStatusValidator`/`DatabaseTenantCatalog`/`CachedTenantCatalog` (WO-075/P-471/P-472, IMPLEMENTED, shipped 2026-09-04) are plain sealed records/classes over BCL primitives (`Guid`, `string`, `IReadOnlyDictionary<string,string>`) and the already-AOT-safe `IDbConnectionFactory`/parameterized-ADO.NET-`IDataReader` pattern this domain already relies on for `DatabaseTenantResolutionStrategy` — no reflection anywhere. `CachedTenantCatalog`'s TTL logic is a plain `ConcurrentDictionary`-backed cache keyed against an injectable `TimeProvider` — no reflection.
- `AddSharedKernelKeyVaultKeyProvider` (WO-068/P-449, IMPLEMENTED, shipped 2026-09-04) is a plain, reflection-free call-through plus an `IServiceCollection.Any(...)` idempotency guard — same AOT profile as `AddSharedKernelKeyVaultConfiguration` above, still dependent on `01.Core`'s `SharedKernel.Cryptography.KeyVault.Azure`'s own `Azure.Security.KeyVault.Keys`/`Azure.Identity` AOT status, encapsulated entirely behind this one opt-in extension method. **`cacheTtl` caching-wrap addition (WO-081/P-503, IMPLEMENTED and tested, shipped 2026-09-08, C-70):** the added registrations (`CachedEncryptionKeyProvider` factory + the `IEncryptionKeyProvider` re-registration) are plain reflection-free lambdas over already-AOT-safe `01.Core` types — no new AOT exposure beyond what this method already carries.
- `KeyVaultKeyProviderReadinessHealthCheck`/`AddKeyVaultKeyProviderReadinessCheck` (WO-068/P-449, IMPLEMENTED, shipped 2026-09-04) is a plain sealed class over `IEncryptionKeyProviderProbe`/`EncryptionKeyProviderHealth` (no `Result<T>` wrapper, no reflection) — same AOT profile as `WorkflowReadinessHealthCheck`/`SchedulerReadinessHealthCheck`.
- `SchedulingTelemetryExtensions.WithSchedulingTelemetry` (WO-073/P-465, IMPLEMENTED, shipped 2026-09-04) is a plain static class doing string-name `AddSource`/`AddMeter` calls — same AOT profile as `MessagingTelemetryExtensions`/`CachingTelemetryExtensions`/every other string-name-only sibling.
- `SchedulerReadinessHealthCheck` (WO-073/P-466, IMPLEMENTED, shipped 2026-09-04) is a plain sealed class over `ISchedulerServiceProbe`/`SchedulerServiceHealth` (no `Result<T>` wrapper, no reflection) — same AOT profile as `WorkflowReadinessHealthCheck`/`VectorStoreReadinessHealthCheck`.
- `LocalizationResolutionOptions`/`AddSharedKernelLocalization`'s two custom `IRequestCultureProvider` implementations (WO-078/P-483, IMPLEMENTED, shipped 2026-09-04) are plain types over BCL `CultureInfo`/`IRequestCultureProvider`/`ProviderCultureResult` and the already-referenced `IUserContext.Claims`/`ITenantCatalog` — no reflection.

---

## Test Rules

- Unit tests for `SharedKernel.ServiceDefaults` live in `13.ServiceDefaults/SharedKernel.ServiceDefaults/SharedKernel.ServiceDefaults.Tests/`.
- Unit tests for `SharedKernel.MultiTenancy` live in `13.ServiceDefaults/SharedKernel.MultiTenancy/SharedKernel.MultiTenancy.Tests/`.
- Health check tag tests: every dependency-specific check (`Redis`, `Messaging`, `Database`, `Cache`, `Storage`, `Search`, `VectorStore`, `Workflows`) is registered with tag `"ready"` and never with tag `"live"` — assert against the registered `HealthCheckRegistration.Tags`. (`Orchestration` is retracted, WO-047/P-291 — no such check will ever exist.)
- **`AddMessagingReadinessCheck` (WO-054/P-351, implemented and tested, 2026-08-07 — the phase's own gating acceptance criterion):** against a local, test-only `IMessageBusProbe` stub (never a real RabbitMQ/Azure Service Bus/MassTransit dependency — mirrors how `AddStorageReadinessCheck`/`AddSearchReadinessCheck`/`AddVectorStoreReadinessCheck`/`AddWorkflowReadinessCheck` are each tested), `MessageBusHealth.IsHealthy == true` → `Healthy`; `false` → `Unhealthy` (never `Degraded`), with `Description` surfaced verbatim via `HealthCheckResult.Description`. **The proof this test exists to make is structural, not merely behavioral:** because the DI container in this test registers the stub for `IMessageBusProbe` with zero `RabbitMQ.Client`/`Azure.Messaging.ServiceBus`/AMQP-URI/connection-string wiring present anywhere in it, a passing test is only possible once the check has zero remaining independent-connection code path — this is what proves "genuinely reflects the real configured bus's health, not an independent connection" (the phase's own acceptance criterion), not merely that the renamed method still passes its old mocks.
- `AddStorageReadinessCheck` (WO-043/P-270, implemented): a forced `IFileStorage.CheckHealthAsync` failure (via a test-double `IFileStorage`, never a real S3/OBS/MinIO dependency in this domain's own unit tests) must report `HealthStatus.Unhealthy` — this check has no fail-safe-absorption layer in front of it, unlike `AddCacheReadinessCheck`, so it must **not** report `Degraded`.
- `AddSearchReadinessCheck` (WO-044/P-277, implemented): via a test-double `ISearchIndexProvisioner` (never a real Meilisearch/ElasticSearch container in this domain's own unit tests), `Reachable && IndexAddressable && Searchable` all `true` → `Healthy`; any one `false`, or the underlying `Result` itself failing, → `Unhealthy`. **Dedicated regression test required for the acceptance criterion:** a probe result with all three booleans `true` but a large/non-null `SearchIndexHealth.PendingWriteCount` must still report `Healthy` — proving the backlog-is-never-unhealthy rule directly, not merely by omission. Never `Degraded` for this check (no fail-safe-absorption layer sits in front of raw search-index connectivity, same rationale as `AddStorageReadinessCheck`/`AddDatabaseReadinessCheck<TContext>`).
- `WithSearchTelemetry()` (WO-044/P-277, implemented): calling it twice on the same builder registers exactly one instance of the `"SharedKernel.Search"` `ActivitySource`/`Meter` name — mirrors T-13/T-23's idempotency coverage for the other three `With*Telemetry()` siblings.
- `AddVectorStoreReadinessCheck` (WO-045/P-285, implemented): via a test-double `IVectorCollectionProvisioner` (never a real Qdrant container in this domain's own unit tests), `Reachable && CollectionAddressable && Queryable` all `true` → `Healthy`; any one `false`, or the underlying `Result` itself failing, → `Unhealthy`. **Dedicated regression test required, mirroring the `AddSearchReadinessCheck` acceptance criterion exactly:** a probe result with all three booleans `true` but a large/non-null `VectorCollectionHealth.PendingWriteCount` must still report `Healthy`. Never `Degraded` for this check (same rationale as `AddStorageReadinessCheck`/`AddSearchReadinessCheck`).
- `WithIntelligenceTelemetry()` (WO-045/P-285, implemented): calling it twice on the same builder registers exactly one instance of the `"SharedKernel.AI"` `ActivitySource`/`Meter` name — mirrors T-13/T-23/T-33's idempotency coverage for the other four `With*Telemetry()` siblings.
- `AddWorkflowReadinessCheck` (WO-046/WO-047/P-289, implemented): via a test-double `IWorkflowServiceProbe` (never a real Temporal server/dev-server in this domain's own unit tests), `Reachable && NamespaceAddressable && WorkerPollersActive` all `true` → `Healthy`; any one `false`, or the underlying `Result` itself failing, → `Unhealthy`, never `Degraded`. **Dedicated regression test required:** all else healthy but a large/non-null `TaskQueueBacklog` must still report `Healthy` — mirrors the `PendingWriteCount`-is-never-unhealthy regression exactly. `WorkerPollersActive` is confirmed a non-nullable `bool` (verified against compiled `WorkflowServiceHealth` source) — no separate client-only/`isWorkerHost` test path is needed, since the probe implementation itself always reports `true` on a client-only registration; a dedicated regression test proves this directly (`WorkflowReadinessHealthCheckTests.CheckHealthAsync_ClientOnlyRegistration_WorkerPollersActiveAlwaysTrue_ReportsHealthy`).
- `WithWorkflowTelemetry()` (WO-046/P-289, implemented): calling it twice on the same builder registers exactly one instance of the `"SharedKernel.Workflows"` `ActivitySource`/`Meter` name — mirrors T-13/T-23/T-33/T-36's idempotency coverage for the other five `With*Telemetry()` siblings.
- `WithCachingTelemetry()`'s tracing addition (T-39, WO-050/P-305 — implemented, 2026-07-29): `CachingTelemetryExtensionsTests.WithCachingTelemetry_SpanFromCachingActivitySource_IsCaptured` proves a span emitted by a `"SharedKernel.Caching"`-named `ActivitySource` is captured once `WithCachingTelemetry()` has been called — the acceptance criterion P-305 named explicitly. **Pattern note (corrects the prior draft's assumption):** none of the five sibling `With*Telemetry` test files (`MessagingTelemetryExtensionsTests`, `SearchTelemetryExtensionsTests`, `ApplicationTelemetryExtensionsTests`, `IntelligenceTelemetryExtensionsTests`, `WorkflowTelemetryExtensionsTests`) actually contain an `ActivityListener`-based span-capture test — they only assert `TracerProviderBuilder`/`MeterProviderBuilder` DI-registration counts. The genuine capture proof instead mirrors this same test project's own `BaggageLogRecordProcessorTests`/`AmbientLoggingEnrichmentAcceptanceTests` "attach a capturing `BaseProcessor<T>` to the real pipeline, then resolve the built provider from DI to force it live" idiom, applied to `OpenTelemetry.BaseProcessor<Activity>` via `WithTracing(t => t.AddProcessor(...))` plus `provider.GetRequiredService<TracerProvider>()` (which forces the SDK to build the pipeline — `BuildServiceProvider()` alone never does, since that normally happens inside a hosted service at `IHost.StartAsync()` time). Verified to fail (empty captured list) when `WithCachingTelemetry`'s `AddSource` call is removed — proving the test is not trivially passing regardless of the extension's implementation. Any future `With*Telemetry` sibling wanting the same stronger proof should follow this `BaseProcessor<Activity>` + `GetRequiredService<TracerProvider>()` pattern, not a raw `ActivityListener` registered directly by the test (a test-owned `ActivityListener` with a matching `ShouldListenTo` predicate would capture the span regardless of whether the extension under test wired anything at all, proving nothing). The pre-existing metrics-only idempotency test was also split into two — one for `TracerProviderBuilder`, one for `MeterProviderBuilder` — mirroring `WithSearchTelemetry`'s/`WithIntelligenceTelemetry`'s/`WithWorkflowTelemetry`'s already-split test shape.
- **`WithCommunicationTelemetry()` (T-43, WO-056/P-365) — GATING acceptance criterion, genuine capture proof — IMPLEMENTED and tested, closed 2026-08-12.** `CommunicationTelemetryExtensionsTests.cs` gained 4 new tests atop the pre-existing baseline coverage (TracerProviderBuilder/MeterProviderBuilder idempotency-by-DI-count, no-throw, same-builder-return): (1) `WithCommunicationTelemetry_GrpcCall_ProducesSpanWithRpcSystemGrpcTag` — a real in-process outbound gRPC call (a `WebApplicationFactory`-hosted minimal `Grpc.AspNetCore` test service under new `Telemetry/GrpcFixtures/` fixtures — `greeter.proto`, `GreeterService`, `GrpcTestWebApplicationFactory`, `ResponseVersionHandler` — called through a real `Grpc.Net.Client` channel), made after `WithCommunicationTelemetry()` is wired, producing a captured span carrying `rpc.system == "grpc"`; (2) `WithCommunicationTelemetry_PollyRetry_EmitsPollyMeterMetric` — a real standalone `ResiliencePipelineBuilder` retry pipeline (`ConfigureTelemetry` enabled, one forced retry attempt) emitting a captured `"Polly"`-named `Metric` via `OpenTelemetry.Exporter.InMemory`'s `AddInMemoryExporter` — **metric only, since C-47 empirically confirmed Polly v8.4.2 creates no `ActivitySource` in this dependency chain; no attempt was made to prove a `"Polly"`-sourced span, since none exists — a documented deviation from the phase's original text, not a silently-missed criterion.** Both follow the `BaseProcessor<Activity>`/analogous metrics-capture idiom established by `WithCachingTelemetry`'s T-39/`WithPersistenceTelemetry`'s T-40 — never a test-owned `ActivityListener`. (3) `WithCommunicationTelemetry_CalledTwice_GrpcCall_DoesNotProduceDuplicateSpan` and `WithCommunicationTelemetry_CalledTwice_PollyRetry_DoesNotDuplicateMetric` — C-47's own decompiled-source idempotency finding (DI `TryAddSingleton` + internally-guarded subscription for gRPC; by-name `Meter` dedup for Polly) exercised directly against a live gRPC call and a live retry execution, not merely trusted from its changelog note: registering twice produces exactly one — not two — tagged spans/metric measurements for one call/execution. All four tests were verified during implementation to genuinely fail when their corresponding production wiring was temporarily removed from `WithCommunicationTelemetry()`, then the production code was restored unchanged. Test-project-only `PackageReference`s added (never the production `.csproj`): `Grpc.AspNetCore`/`Grpc.Net.Client` `2.80.0`, `Polly.Core`/`Polly.Extensions` `8.7.0` (bumped from the production-pinned `8.4.2` — `SharedKernel.Testing -> SharedKernel.Application.Behaviors` already floors `Polly.Core` at `>= 8.7.0` transitively, and NU1605 forbids a downgrade below a floor set elsewhere in the graph), `OpenTelemetry.Exporter.InMemory` `1.16.0`. `SharedKernel.ServiceDefaults.Tests`: 100/100 passing (+4 new), 0 regressions.
- `WithPersistenceTelemetry()` (T-40, WO-051/P-326 — implemented, 2026-07-30): `PersistenceTelemetryExtensionsTests.WithPersistenceTelemetry_SpanFromPersistenceActivitySource_IsCaptured` proves a span emitted by a `"SharedKernel.Persistence"`-named `ActivitySource` is captured once `WithPersistenceTelemetry()` has been called — mirrors `WithCachingTelemetry`'s T-39 `BaseProcessor<Activity>` + `provider.GetRequiredService<TracerProvider>()` capture idiom exactly, verified to fail (empty captured list) when the `AddSource` call is removed. A single tracing-only idempotency test (`TracerProviderBuilder` registration count `<= 1` after calling the method twice) covers the seventh sibling's idempotency contract — **no metrics-idempotency counterpart test exists or is needed**, since this method deliberately makes no `WithMetrics(...)` call (D-16: `06.Persistence` ships no companion `Meter`).
- `WithIntegrationTelemetry()` (T-69, WO-064/P-430 — IMPLEMENTED and tested, closed 2026-08-21): `IntegrationTelemetryExtensionsTests.WithIntegrationTelemetry_SpanFromIntegrationActivitySource_IsCaptured` proves a span emitted by a `"SharedKernel.Integration"`-named `ActivitySource` is captured once `WithIntegrationTelemetry()` has been called — mirroring `WithPersistenceTelemetry`'s T-40 `BaseProcessor<Activity>` + `provider.GetRequiredService<TracerProvider>()` capture idiom exactly, verified during implementation to fail (empty captured list) when the `AddSource` call is temporarily removed, then restored unchanged. A single tracing-only idempotency test covers the ninth sibling's idempotency contract — no metrics-idempotency counterpart test, mirroring T-40's identical omission (D-29: no meter to wire).
- `AddOrchestrationReadinessCheck` is retracted (WO-047/P-291) — no test coverage exists or will be authored for it.
- `AddCacheReadinessCheck`: a forced cache-probe failure must report `HealthStatus.Degraded`, never `HealthStatus.Unhealthy`.
- `StartupGateHealthCheck`: reports `Unhealthy` before `MarkReady()` is called; reports `Healthy` after; `MarkReady()` is idempotent (calling twice does not throw and does not toggle state back).
- `WithMessagingTelemetry()` / `WithCachingTelemetry()` / `WithApplicationTelemetry()`: calling each twice on the same builder registers exactly one instance of each `ActivitySource`/meter name (no duplicate-instrument assertion via the OTel SDK's exposed listener APIs).
- `HeaderTenantResolutionStrategy`: present + parseable header → resolved `Guid`; absent header → `null`; malformed header value → `null` (never throws).
- `ClaimTenantResolutionStrategy`: delegates correctly to a fake/mocked `OidcTenantProvider`-shaped dependency; never re-parses raw claims itself (verified by testing through the seam, not by reflection over private state).
- `DatabaseTenantResolutionStrategy`: resolves a known host/subdomain to the expected `TenantId` against a test double `IDbConnectionFactory`; unknown host → `null`; query parameterization verified (no string-built SQL in the executed command text); **async-call assertion (WO-028/P-176):** the test double's command surface must assert that the async ADO.NET path is invoked (not the synchronous `ExecuteScalar()`), and a separate test must prove a cancelled `CancellationToken` actually cancels the in-flight call rather than being silently ignored.
- `TenantResolutionOptions.StrategyOrder`: default order is `[TenantResolutionStrategyNames.Claim, TenantResolutionStrategyNames.Header, TenantResolutionStrategyNames.Database]` (corrected WO-061/P-393, 2026-08-19 — this line previously stated the superseded `[Header, Claim, Database]` order and was stale until this correction); first non-null strategy result wins; omitting a strategy's `StrategyName` from the order means it is never invoked.
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
- **`AddMtlsClientCertificate`/`AddMtlsForwardedHeaderCertificate` (WO-058/P-378, Core+Tests IMPLEMENTED 2026-08-14 — T-44 closed, `SharedKernel.ServiceDefaults.Tests/Security/`):** `AddMtlsClientCertificate` — `MtlsClientCertificateExtensionsTests` resolves the real `KestrelServerOptions` a built `IHost` produces (forcing the `Configure<IServiceScopeFactory>` delegate to run) and, via reflection over Kestrel's private `HttpsDefaults` property (decompiling the shipped `Microsoft.AspNetCore.Server.Kestrel.Core.dll` confirmed `ConfigureHttpsDefaults(...)` overwrites a single delegate field, not a list, and Kestrel exposes no public API to read it back), invokes the wired delegate against a fresh `HttpsConnectionAdapterOptions`: a test-double `IMtlsCertificateValidator` returning accept/reject is reflected verbatim in the wired `ClientCertificateValidation` outcome (`AddMtlsClientCertificate_ValidatorAccepts_WiredValidationDelegateReturnsTrue`/`..._ValidatorRejects_...ReturnsFalse`); the SAME certificate flips outcome purely because the validator's own decision flips, proving no independent chain/subject/issuer path exists (`..._OutcomeTracksValidatorDecisionExactly_NoIndependentValidationPath`); `mode` defaults to `ClientCertificateMode.AllowCertificate`, with an explicit `RequireCertificate` override honored; a Scoped (never Singleton) validator factory is invoked once per handshake, proving a fresh `IServiceScope` per call rather than a root-captured instance. `MtlsForwardedHeaderMiddleware` — `MtlsForwardedHeaderMiddlewareTests` proves a present, well-formed header (Base64 DER, and the URL-encoded-PEM fallback) produces a validated certificate exposed via `HttpContext.Connection.ClientCertificate` exactly when the test-double validator accepts it; a rejected/absent/malformed header (including Envoy/Istio's deliberately-unsupported structured XFCC format) is a no-op — no exception, no certificate set, `next()` still called. `MtlsForwardedHeaderExtensionsTests` proves `MtlsForwardedHeaderOptions` with an unconfigured (empty/whitespace) `HeaderName` fails fast at `IHost.StartAsync()` with `OptionsValidationException`, never silently defaulting to a vendor-specific literal — the phase's own gating acceptance criterion, asserted directly against a real started host, not merely documented. `MtlsNoOpRegressionTests` proves the "no behavior change for non-adopting consumers" criterion directly: a host built with neither extension called has its `KestrelServerOptions`' `HttpsDefaults` delegate produce Kestrel's own out-of-the-box `ClientCertificateMode.NoCertificate`/null `ClientCertificateValidation` (matching `HttpsConnectionAdapterOptions`'s decompiled-confirmed constructor defaults) and registers zero `IValidateOptions<MtlsForwardedHeaderOptions>` — proving no fail-fast side effect exists unless explicitly opted in. **Every genuine-proof test in this set was verified during implementation to actually fail when its corresponding production wiring (the `ClientCertificateValidation` assignment, the `Connection.ClientCertificate` assignment, and the `.Validate(...).ValidateOnStart()` chain) was temporarily removed, then the production code was restored unchanged** — mirroring the T-39/T-40/T-41/T-43 discipline. `SharedKernel.ServiceDefaults.Tests`: 130/130 passing (+30 new), 0 regressions.
- **WO-061/P-393–P-400 (`SK.13.Tests`, T-45–T-66) IMPLEMENTED and closed 2026-08-20 — the full 22-task gating-acceptance-criteria checklist was walked task-by-task against real behavior, not trusted from the prior `SK.13.Core` session's approximating coverage.** Twelve tasks (T-45/T-46/T-47/T-48/T-49-partial/T-50/T-51/T-57/T-62/T-64/T-65/T-66) were confirmed already fully satisfied by tests written during `SK.13.Core`. Ten needed genuinely new, stronger proofs, each closing the exact gap the `SK.13.Core` note below used to flag: `ServiceDefaultsLogWiringTests` gained a Thumbprint/Subject-populated-and-no-raw-certificate-bytes-anywhere-in-the-log-state test (T-52), a real-`HealthCheckService.CheckHealthAsync()`-invoked-three-times fires-exactly-once test (T-53), and a fires-exactly-once test for `ForwardedHeaderTrustBoundaryUnconfigured` (T-54). A new `MultiTenancyRealHostStartupTests.cs` proves `TenantResolutionOptionsValidator`'s fail-fast behavior through a genuine `Host.CreateApplicationBuilder()` → `.Build()` → `.StartAsync()` pipeline (T-55/T-56) — replacing the prior `IOptions<T>.Value`-resolved-directly approximation; required a new test-only `Microsoft.Extensions.Hosting` `PackageReference` on `SharedKernel.MultiTenancy.Tests.csproj` (the package itself gained none). `RateLimitingExtensionsTests` gained a no-op-regression test (T-58) and a test proving the `configure` callback overrides `options.GlobalLimiter` ITSELF, not merely `RejectionStatusCode` (T-59), via a real request burst against a plain endpoint with no `[EnableRateLimiting]` policy. `KeyVaultConfigurationExtensionsTests` gained a literal `Build()`-never-reached proof (T-60, on top of the pre-existing Add-call-itself-throws proof) and a no-op-regression test (T-61). `HealthCheckEndpointTests` gained a real `TestServer` pipeline wired with a minimal test-only `AuthenticationHandler<AuthenticationSchemeOptions>` (`AddAuthentication`/`AddScheme`/`AddAuthorization`/`UseAuthentication`/`UseAuthorization`), proving 401 for an unauthenticated request and 200 for an authenticated one against both `/health/live` and `/health/ready` — replacing the prior metadata-only `IAuthorizeData` proof (T-63). **Clerical correction:** T-66's `Package(s)` cell is `SharedKernel.MultiTenancy`, not the phase spec's literal `SharedKernel.ServiceDefaults` — its subject (`ITenantStatusValidator`/`TenantResolutionMiddleware`) matches sibling rows T-64/T-65. `SharedKernel.ServiceDefaults.Tests`: 162 → 171 passing (+9). `SharedKernel.MultiTenancy.Tests`: 47 → 51 passing (+4). Zero regressions in either suite.
- **Test-authoring gotcha found while writing T-54 (`ServiceDefaultsLogWiringTests`'s `ForwardedHeaderTrustBoundaryUnconfigured`-fires-exactly-once test) — will recur for any future "fires exactly once" test on a type that combines `PostConfigure<T>` with `.ValidateOnStart()`:** `AddMtlsForwardedHeaderCertificate` wires its trust-boundary warning via `.PostConfigure<ILoggerFactory>(...)` on `MtlsForwardedHeaderOptions` AND separately chains `.ValidateOnStart()` on the same options type. These resolve through two **independently-cached** paths — `IOptions<T>` (an `OptionsManager<T>` singleton's own `Lazy<T>` cache) versus `.ValidateOnStart()`'s internal startup-validation pass (which resolves through `IOptionsMonitor<T>`'s separate `OptionsCache<T>`) — so a test that calls `host.StartAsync()` (exercising the `ValidateOnStart` path) and THEN also resolves `IOptions<T>.Value` repeatedly (a second, different path) observes the `PostConfigure` callback fire twice, not because it isn't cached but because each path caches independently. This is not a production defect — it is a trap for a test mixing both resolution paths inside one "fires exactly once" assertion. **Fix:** exercise exactly one resolution path per test — build a bare `ServiceProvider` from `builder.Services` and resolve `IOptions<T>.Value` repeatedly without ever calling `host.StartAsync()` in that same test (mirrors the pre-existing `AddCacheReadinessCheck_ResolvingHealthCheckOptionsRepeatedly_LogsHealthCheckRegisteredExactlyOnce` shape), or test the `ValidateOnStart` path in isolation without a follow-up `IOptions<T>.Value` resolution. See `AddMtlsForwardedHeaderCertificate_TrustedNetworksLeftEmpty_OptionsResolvedRepeatedly_LogsForwardedHeaderTrustBoundaryUnconfiguredExactlyOnce` for the corrected shape.
- **`RateLimitRejectionRecipeTests` (T-67/T-68, WO-063/P-419 — implemented and tested, shipped 2026-08-21):** proves the D-28-corrected `AddSharedKernelRateLimiting()` `OnRejected` recipe against a real `WebApplication`/`TestServer` host — a permit-limit-1 fixed-window policy, two requests in quick succession, and assertions on the second (rejected) response: `429`, `Content-Type: application/problem+json`, a `ProblemDetails` body with `Status`/`Type`/`Extensions["traceId"]` populated, and a parseable non-negative `Retry-After` header (T-67); a companion test proves the pre-existing no-recipe call shape stays the byte-identical BCL default — empty body, no `Content-Type`, no `Retry-After` (T-68). This required a **test-only** `ProjectReference` from `SharedKernel.ServiceDefaults.Tests.csproj` to `14.Presentation/SharedKernel.Presentation.WebApi.csproj` (inline-commented as gating-proof-only), mirroring the T-43/WO-056 `Grpc.AspNetCore`/`Polly.Core` precedent — **the first time this domain has taken a test-only reference to `14.Presentation` specifically**, established as the sanctioned pattern for proving a documented cross-domain recipe compiles and behaves correctly without ever letting the production `.csproj` cross the `01`–`12` composition-root ceiling. `SharedKernel.ServiceDefaults.Tests`: 173/173 passing, 0 regressions.
- **`CatalogTenantStatusValidatorTests`/`DatabaseTenantCatalogTests`/`CachedTenantCatalogTests`/`CachedTenantCatalogComposesWithCatalogTenantStatusValidatorTests`/`CachedTenantCatalogCrossInstanceInvalidationTests` (WO-075/P-471/P-472, IMPLEMENTED and tested, shipped 2026-09-04, `SharedKernel.MultiTenancy.Tests`, +22 tests):** `Active`/`Suspended`/`Offboarded`/catalog-miss cases for `IsActiveAsync` (fail-closed on a miss — never `true`); `DatabaseTenantCatalog` parameterization proven via a test-double `IDbCommand`/`IDataReader`, mirroring `DatabaseTenantResolutionStrategyTests`; `CachedTenantCatalog`'s TTL-bypass-on-`InvalidateTenantAsync` proven directly via a controllable `TimeProvider` fake (never by waiting out a real TTL); `CatalogTenantStatusValidator` composes with `CachedTenantCatalog` with zero code changes, proven directly, per P-472's own explicit acceptance criterion; cross-instance invalidation proven via a test-double `ICacheInvalidationBus` (publish half) plus a direct call to `HandleCrossInstanceInvalidationSignal` (receive half — see the Interface Contracts publish-only finding above), never a real Redis dependency.
- **`KeyVaultKeyProviderExtensionsTests` (WO-068/P-449, IMPLEMENTED and tested, shipped 2026-09-04, `SharedKernel.ServiceDefaults.Tests`; `cacheTtl` additions WO-081/P-503, T-82, IMPLEMENTED and tested, shipped 2026-09-08 — +8 tests, 14 total in this file):** proven via `Host.CreateApplicationBuilder()`/`builder.Services` inspection (never `WebApplicationFactory`) that `AddSharedKernelKeyVaultKeyProvider` registers `AzureKeyVaultEncryptionKeyProvider`/`IEnvelopeEncryptionProvider` exactly once, including when called twice — **`IEncryptionKeyProvider` registers TWICE under the default `cacheTtl` (the raw registration from `01.Core`'s call-through, plus this method's own re-registration redirecting to the cached wrapper — MS.DI resolves the LAST registration for a single-instance request), a corrected count from the pre-P-503 shape, proven via a dedicated resolution-level (not just registration-count) assertion.** `cacheTtl` coverage, proven via `IServiceCollection.BuildServiceProvider()` resolution against a syntactically-valid (never-reached) `AzureKeyVaultCryptographyOptions` configuration: default call resolves `IEncryptionKeyProvider` as `CachedEncryptionKeyProvider`; `IEnvelopeEncryptionProvider`/`IEncryptionKeyProviderProbe` stay reference-equal to the raw `AzureKeyVaultEncryptionKeyProvider` singleton; `cacheTtl: TimeSpan.Zero` disables wrapping (`IEncryptionKeyProvider` resolves reference-equal to the raw singleton, zero `CachedEncryptionKeyProvider` registrations); a negative `cacheTtl` throws `ArgumentOutOfRangeException` before any service is registered (verified via a zero-registrations assertion, not just the exception type); `CachedEncryptionKeyProvider` independently resolvable as its own concrete type (proving `06.Persistence`'s `.WithExternalEncryptionKeyProvider<CachedEncryptionKeyProvider>()` compatibility with no reference to `06.Persistence` itself); double-call does not double-register the cached wrapper.
- **`KeyVaultKeyProviderReadinessHealthCheckTests` (WO-068/P-449, IMPLEMENTED and tested, shipped 2026-09-04, `SharedKernel.ServiceDefaults.Tests`, +4 tests):** via a test-double `IEncryptionKeyProviderProbe`, `IsHealthy == true` → `Healthy`; `IsHealthy == false` → `Unhealthy`, never `Degraded`; `Description` surfaces in the result when unhealthy; a null `Description` on an unhealthy result does not throw. **This closes WO-068/P-449 end to end — every `SK.13.*` phase key is now fully `●`/`—`.**
- **`SchedulingTelemetryExtensionsTests`/`SchedulerReadinessHealthCheckTests` (WO-073/P-465/P-466, IMPLEMENTED and tested, shipped 2026-09-04, `SharedKernel.ServiceDefaults.Tests`, +9 tests):** `SchedulingTelemetryExtensionsTests` proves BOTH a tracing-capture (via `BaseProcessor<Activity>`) and a metrics-capture (via `OpenTelemetry.Exporter.InMemory`'s `AddInMemoryExporter`) genuine proof (unlike `WithPersistenceTelemetry`'s/`WithIntegrationTelemetry`'s tracing-only test shape), each independently verified during implementation to fail when its corresponding `AddSource`/`AddMeter` call was temporarily removed. `SchedulerReadinessHealthCheckTests` includes a dedicated `RegisteredJobCount`-never-drives-Unhealthy regression, mirroring the `PendingWriteCount`/`TaskQueueBacklog` precedent exactly, plus a `LastTickUtc`-null-is-informational-only test.
- **`LocalizationResolutionOptionsTests`/`SharedKernelLocalizationExtensionsTests`/`SharedKernelLocalizationWrapsRequestLocalizationMiddlewareTests` (WO-078/P-483, IMPLEMENTED and tested, shipped 2026-09-04, `SharedKernel.ServiceDefaults.Tests`, +13 tests):** proves the `UserPreference`-beats-`TenantDefault`-beats-`AcceptLanguageHeader` precedence directly (not by omission — all three signals configured simultaneously in one test, asserting `UserPreference` wins) — the WO-061-lesson acceptance criterion; the `TenantDefault` step proven to skip cleanly (never throw) with zero `ITenantCatalog` registered in DI at all; a startup `Warning`-fires-exactly-once test (EventId `13004`) for the fully-unconfigured case, plus negative tests proving it does NOT fire once either dynamic step is configured; the custom providers proven present ahead of the real BCL `AcceptLanguageHeaderRequestCultureProvider` in the resolved `RequestLocalizationOptions.RequestCultureProviders` list; a no-op regression proving a host that never calls `AddSharedKernelLocalization()` is unaffected.

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
- [2026-08-04] WO-054/P-351: `AddRabbitMqMessagingHealthCheck`/`AddAzureServiceBusMessagingHealthCheck` retired outright — replaced by a single `AddMessagingReadinessCheck()` wrapping `07.Messaging`'s new bus-backed readiness-probe primitive (`IMessageBusProbe`/`MessageBusHealth`, P-347). Ground-truth source read (`RabbitMqMessagingHealthCheckExtensions.cs`/`AzureServiceBusMessagingHealthCheckExtensions.cs`/`AzureServiceBusHealthCheck.cs`) confirmed the defect the phase input described: both retired methods build their own second connection entirely from a caller-supplied string, with zero reference to whatever `07.Messaging.MassTransit`'s `MessagingBusBuilder` actually configured — a health check that can pass while the real bus is down, or fail while it is healthy, because it validates a different connection. No signature-compatible fix exists (the connection-value parameter each method accepted IS the defect), so this is a confirmed **breaking change**, not a compatible extension — flagged explicitly in the Packages table row, the new Interface Contracts entry, and `state-map.md`'s Published-phase note (a SemVer-major repack will be needed at the next `devops-lead` publish pass). The replacement takes no connection/identifier parameter at all — mirrors `AddWorkflowReadinessCheck`'s no-caller-supplied-identifier precedent one step further, since `IMessageBusProbe` is a per-host singleton with nothing left for a call site to supply. New `HealthCheckNames.Messaging` constant targets the retired `.RabbitMq`/`.AzureServiceBus`; `HealthCheckTags.Messaging` is unchanged and reused (it already covered both transports). **Dependency verified against ground truth, not assumed** from the phase-dispatch note's "already dispatched and planned... but not yet implemented in code" framing: `07.Messaging/state-map.md` confirms all ten tasks of the new `SK.07.ReadinessProbe` phase (RP-01–RP-10, P-347) are `○` Not Started. This section, the Technology Stack row, the `HealthCheckNames` block, Implementation Rules, DI Registration snippet, AOT Compatibility, and Test Rules were all updated to describe the locked target contract — explicitly noting throughout that `AddRabbitMqMessagingHealthCheck`/`AddAzureServiceBusMessagingHealthCheck` remain live in shipped code until `state-map.md`'s C-46 (blocked, pending `07.Messaging`'s Abstractions-side `IMessageBusProbe`/`MessageBusHealth`) actually lands and deletes them — consult `state-map.md` for current implementation status, not this file alone. State-map tasks added: D-17 (`SK.13.Design`, locked/closed now), S-17/C-46/T-41/T-42/DO-12 (Scaffold/Core/Tests/Docs, all `⚑` genuinely blocked) (servicedefaults-arch-planner, WO-054, P-351)
- [2026-07-09] WO-041/P-251: OpenTelemetry log export + ambient Correlation/Tenant enrichment phase added. `AddSharedKernelTelemetry` gains a `.WithLogging(...)` registration — additive inside its existing signature, no new public method — enabling OTLP log export (same standard env vars as tracing/metrics) with `IncludeScopes`/`IncludeFormattedMessage` both `true`. New `BaggageLogRecordProcessor` (`Telemetry/`, sealed `BaseProcessor<LogRecord>`) generically copies `Activity.Current?.Baggage` onto `LogRecord.Attributes` (never overwriting an explicit attribute, no hardcoded key names) — this is the single mechanism that surfaces both `14.Presentation`'s pre-existing CorrelationId `Activity`-baggage convention (WO-031) and a new `SharedKernel.MultiTenancy` TenantId baggage entry onto every log record, with zero `13.ServiceDefaults` → `14.Presentation` reference. `SharedKernel.MultiTenancy`'s `TenantResolutionMiddleware` gains a matching `Activity.Current?.SetBaggage(TenantBaggageKeys.TenantId, ...)` call (new `TenantBaggageKeys` constants class) alongside its existing `AmbientTenantProvider.TenantId` assignment — set unconditionally, including for the `Guid.Empty` no-tenant sentinel, so log aggregation can distinguish "no tenant resolved" from "enrichment never wired." Interface Contracts, Implementation Rules, AOT Compatibility, Test Rules, and DI Registration sections all updated. Explicit scope boundary documented: this mechanism covers the HTTP-request path only — a message-consumption-scope equivalent is a future `07.Messaging`-owned follow-up, outside this domain's jurisdiction to dispatch, not implemented here as a workaround. State-map tasks added: D-03–D-05 (`SK.13.Design`), S-11 (`SK.13.Scaffold`), C-30–C-33 (`SK.13.Core`), T-24–T-28 (`SK.13.Tests`), DO-04 (`SK.13.Docs`) — all `○` pending implementation; no new Published task (additive internals only, mirrors the WithApplicationTelemetry/WO-040 precedent) (servicedefaults-arch-planner, WO-041)
- [2026-07-09] WO-041/P-251 implemented and tested — `SK.13.Core` (C-30–C-33), `SK.13.Tests` (T-24–T-28), and `SK.13.Docs` (DO-04) all `●`; `13.ServiceDefaults` domain remains fully Published end to end (no new Published task, per the WithApplicationTelemetry/WO-040 precedent). `BaggageLogRecordProcessorTests`/`TelemetryExtensionsTests`/`AmbientLoggingEnrichmentAcceptanceTests` established the test pattern for this class of processor: build a real `LoggerFactory.Create(b => b.AddOpenTelemetry(o => o.AddProcessor(new BaggageLogRecordProcessor()).AddProcessor(new CapturingProcessor(...))))`, where the second custom `BaseProcessor<LogRecord>` copies `LogRecord.Attributes` into a side list at `OnEnd` time rather than retaining the `LogRecord` reference itself (the OTel SDK may pool/reset it after the pipeline completes). `AmbientLoggingEnrichmentAcceptanceTests` (T-27/T-28) took a **test-only** `ProjectReference` from `SharedKernel.ServiceDefaults.Tests` to the sibling `SharedKernel.MultiTenancy.csproj` — acceptable because both packages share this domain and the reference is test-only, never shipped — specifically to run the real `TenantResolutionMiddleware` end-to-end alongside a BCL-simulated `CorrelationId` baggage entry, proving the two ambient-enrichment sources compose without collision. `OpenTelemetry.Extensions.Hosting` confirmed already pinned to 1.16.0 — no version bump needed for `WithLogging(...)`. Test counts: 48/48 `SharedKernel.ServiceDefaults.Tests` (+8), 29/29 `SharedKernel.MultiTenancy.Tests` (+3) (servicedefaults-phase-implementer)
- [2026-07-14] WO-042/P-261 phase added — retrofit this domain to consume `01.Core`'s `WellKnownHeaders`/`WellKnownBaggageKeys` (`SK.01.P259`), fixing the confirmed live baggage-key mismatch flagged as DO-07 in `14.Presentation/CLAUDE.md`'s WO-041 changelog. Ground-truth verified: `14.Presentation.CorrelationIdMiddleware.BaggageKey = "correlation.id"`, while this domain's own `BaggageLogRecordProcessorTests`/`AmbientLoggingEnrichmentAcceptanceTests` independently hardcode `"CorrelationId"` — the two literals never matched, so the test suite was proving the processor round-trips an arbitrary key, never the actual production key. `HeaderTenantResolutionStrategy.DefaultHeaderName`'s retrofit (source relocated to `WellKnownHeaders.TenantId`) is, by contrast, a zero-behavior-change relocation — the value `"X-Tenant-Id"` was already correct. **Discrepancy flagged:** the dispatched phase input's premise that P-259 is "already implemented in 01.Core" does not match `01.Core/state-map.md` ground truth as of this pass — `SK.01.P259` shows D-30 (design) `●` but C-43 (implementation)/T-34/DO-16 all `○`. This domain's tasks (state-map `D-06`/`S-12`/`C-34`/`C-35`/`T-29`–`T-31`/`DO-05`) are recorded and design-locked now so implementation is unblocked the instant `01.Core` ships C-43; `S-12` (new `SharedKernel.MultiTenancy` → `SharedKernel.Primitives` `ProjectReference`) is not blocked and can land immediately. Interface Contracts (`HeaderTenantResolutionStrategy`, `BaggageLogRecordProcessor`), Implementation Rules, and Test Rules sections updated to describe the post-fix target state. No new Published task. **Out of this domain's jurisdiction:** closing `14.Presentation/CLAUDE.md`'s DO-07 changelog cross-reference requires the `presentation-arch-planner` agent — flagged for `arch-lead` to dispatch, mirroring the WO-040/P-247 precedent (servicedefaults-arch-planner, WO-042)
- [2026-07-15] WO-042/P-261 unblocked and closed — `01.Core`'s `SK.01.P259` (`WellKnownHeaders`/`WellKnownBaggageKeys`, C-43/T-34/DO-16) confirmed landed 2026-07-14. Implemented: `SharedKernel.MultiTenancy.csproj` gained a new `ProjectReference` to `SharedKernel.Primitives` (S-12); `HeaderTenantResolutionStrategy.DefaultHeaderName` now `= WellKnownHeaders.TenantId` (C-34, zero-behavior-change relocation, value unchanged at `"X-Tenant-Id"`); `BaggageLogRecordProcessorTests`/`AmbientLoggingEnrichmentAcceptanceTests` now assert against `WellKnownBaggageKeys.CorrelationId` instead of the previously-mismatched standalone `"CorrelationId"` literal (C-35, closes DO-07); new regression test `HeaderTenantResolutionStrategyTests.DefaultHeaderName_SourcedFromWellKnownHeaders_StillEqualsXTenantId` proves source relocation without value change (T-31). Interface Contracts, Implementation Rules, and Test Rules "pending" language all updated to reflect shipped state. 48/48 `SharedKernel.ServiceDefaults.Tests` + 30/30 `SharedKernel.MultiTenancy.Tests` passing (+1 new). All 6 `SK.13.*` phase keys fully `●` again. **Still out of this domain's jurisdiction:** `14.Presentation/CLAUDE.md`'s DO-07 changelog cross-reference closure remains tracked under root `P-262` (`14.Presentation` domain, already dispatched) — this file's changelog above already records that hand-off correctly (servicedefaults-phase-implementer, WO-042)
- [2026-07-16] WO-043/P-270 phase added — Storage Readiness Health Check Adapter, mirroring the `06.Persistence`/`13.ServiceDefaults` DB-readiness-probe split (root `CLAUDE.md`) applied to `08.Storage`. New `AddStorageReadinessCheck(this IHealthChecksBuilder, string bucket, string name = HealthCheckNames.Storage)` design-locked: wraps a new `StorageReadinessHealthCheck` around `08.Storage`'s `IFileStorage.CheckHealthAsync(bucket, ct) → Task<Result>` (P-265); resolves only `IFileStorage` (never a concrete `S3StorageOptions`/`ObsStorageOptions` type) so one adapter works against either `.S3` or `.Obs` unmodified; `bucket` is a required explicit parameter for the same reason; calibrated `Unhealthy` on failure (no fail-safe-absorption layer sits in front of raw storage connectivity, unlike `AddCacheReadinessCheck`'s `Degraded`); new `HealthCheckTags.Storage`/`HealthCheckNames.Storage` constants. **Dependency verified against ground truth, not assumed:** `08.Storage/state-map.md` shows `SK.08.Design` 0/15 `○` and `SK.08.Core` 0/30 `○` as of this pass — nothing in `08.Storage` has been implemented yet, only planned (P-265/P-266/P-267 exist solely as a dispatched task breakdown). Interface Contracts, Implementation Rules, AOT Compatibility, and Test Rules sections all updated with the new contract, explicitly marked pending/blocked rather than presented as shipped. State-map tasks added: D-07 (`SK.13.Design`, `●` — locked now, since the signature is already ratified in `08.Storage/CLAUDE.md`'s prose even though no code exists), S-13 (`SK.13.Scaffold`, `○` — unblocked, the target `.csproj` already exists as a registered bare stub), C-36/T-32/DO-06 (`SK.13.Core`/`SK.13.Tests`/`SK.13.Docs`, all `⚑` Blocked pending `08.Storage`'s Core Abstractions sub-phase). No new Published task (mirrors the `AddDatabaseReadinessCheck<TContext>`/WO-028 precedent — additive extension method to an already-published package) (servicedefaults-arch-planner, WO-043, P-270)
- [2026-07-18] WO-043/P-270 unblocked and closed — re-verified `08.Storage`'s blocker directly against `08.Storage/state-map.md` and the compiled `SharedKernel.Storage.Abstractions` source (not taken on trust): `08.Storage` had since reached `Published` end to end (all six phases `●`), and `IFileStorage.CheckHealthAsync(string bucket, CancellationToken) → Task<Result>` exists exactly per the D-07 contract already locked here. S-13 landed (new `ProjectReference` from `SharedKernel.ServiceDefaults.csproj` to `SharedKernel.Storage.Abstractions.csproj`); C-36 landed (`StorageReadinessHealthCheck` + `StorageReadinessHealthCheckExtensions.AddStorageReadinessCheck`, `HealthCheckTags.Storage`/`HealthCheckNames.Storage` constants) — zero deviation from the contract already documented above, this was a pure "implement exactly what was already designed" pass once the blocker cleared, not a redesign. T-32 landed (`StorageReadinessHealthCheckTests` — Healthy/Unhealthy-never-Degraded via a substituted `IFileStorage`, plus tag/name registration assertions folded into the existing `HealthCheckTagTests`/`HealthCheckNamesTests`). DO-06 landed (XML docs; `README.md`'s `Program.cs` snippet gained `.AddStorageReadinessCheck("my-bucket")`). Interface Contracts, Implementation Rules, Test Rules, and the `Packages` table's `SharedKernel.ServiceDefaults` row all updated to replace "design-locked/blocked/pending" language with "implemented". All 6 `SK.13.*` phase keys fully `●` again. 52/52 `SharedKernel.ServiceDefaults.Tests` (+4) + 30/30 `SharedKernel.MultiTenancy.Tests` (unchanged) passing. No new Published task (mirrors the `AddDatabaseReadinessCheck<TContext>`/WO-028 and `WithApplicationTelemetry`/WO-040 precedents) (servicedefaults-phase-implementer)
- [2026-07-19] WO-044/P-277 phase added — Search Readiness Health Check + Telemetry Wiring, mirroring the `06.Persistence`/`08.Storage` "owning domain ships the probe, `13.ServiceDefaults` ships the `IHealthCheck` adapter" split applied to the new `09.Search` domain. Two new contracts design-locked: `AddSearchReadinessCheck(this IHealthChecksBuilder, string indexName, string name = HealthCheckNames.Search)` wraps a new `SearchReadinessHealthCheck` around `09.Search`'s `ISearchIndexProvisioner.ProbeAsync(indexName, ct) → Task<Result<SearchIndexHealth>>` (P-272) — resolves only `ISearchIndexProvisioner`, never a concrete `MeilisearchOptions`/`ElasticSearchOptions` type, mapping `Reachable && IndexAddressable && Searchable` → `Healthy`, else `Unhealthy`; `SearchIndexHealth.PendingWriteCount` is surfaced as informational `HealthCheckResult.Data` only and is **never** factored into the health decision, per this phase's own acceptance criterion and `09.Search/CLAUDE.md`'s explicit staleness-not-unavailability rule; tagged `HealthCheckTags.Ready` + new `HealthCheckTags.Search`/`HealthCheckNames.Search`. `WithSearchTelemetry(this IHostApplicationBuilder)` is the fourth sibling to `WithMessagingTelemetry`/`WithCachingTelemetry`/`WithApplicationTelemetry` — string-name-only `AddSource`/`AddMeter` wiring of `"SharedKernel.Search"`, zero `ProjectReference` to `09.Search`, byte-identical by convention to `09.Search`'s own `SearchWellKnown.ActivitySourceName`/`.MeterName`. **Dependency verified against ground truth, not assumed** (per this domain's own standing discipline): read `09.Search/state-map.md` and `09.Search/CLAUDE.md` directly rather than trusting P-277's "Depends on: P-272, P-273, P-274" line — found `SK.09.Design` 0/28 `○` and `SK.09.Scaffold`/`SK.09.Core` entirely unstarted; nothing has been implemented in `09.Search` yet. However, `09.Search/CLAUDE.md`'s own "Cross-domain work this design requires" section had *already* ratified this exact contract in prose (locked 2026-07-19, same session) — the identical documented-ahead-of-implementation situation as `AddStorageReadinessCheck`'s D-07/WO-043 precedent, so the Design tasks (D-08, D-09) are locked `●` now rather than left `○`. Scaffold (S-14, the new `ProjectReference` to `SharedKernel.Search.Abstractions`) is **not** blocked — the target `.csproj` already exists as a registered bare stub in `Platform.SharedKernel.slnx` per `09.Search/state-map.md`'s Package Board, mirroring the S-12/S-13 precedent. Core (C-37, C-38), Tests (T-33), and Docs (DO-07) are `⚑` Blocked pending `09.Search`'s `SK.09.Core` landing — recorded with the exact verified evidence, not a one-word label. No new Published task (mirrors the `AddDatabaseReadinessCheck<TContext>`/WO-028 and `AddStorageReadinessCheck`/WO-043 precedents — additive extension methods to an already-published package). Interface Contracts, Implementation Rules, AOT Compatibility, and Test Rules sections all updated with the new contracts, explicitly marked design-locked/blocked rather than presented as shipped. Package Board `SharedKernel.ServiceDefaults` reopened `●` (Published) → `◐` (Core) (servicedefaults-arch-planner, WO-044, P-277)
- [2026-07-21] WO-045/P-285 phase added — Vector-Store/Orchestration Readiness Health Check + Telemetry Wiring, extending the `06.Persistence`/`08.Storage`/`09.Search` "owning domain ships the probe, `13.ServiceDefaults` ships the `IHealthCheck` adapter" split to the newly-designed `10.Intelligence` domain (`SK.10.Design` reached full 16/16 `●` ratification the same day, WO-045/P-279). Two of three new contracts locked cleanly: `AddVectorStoreReadinessCheck(this IHealthChecksBuilder, string collectionName, string name = HealthCheckNames.VectorStore)` wraps `IVectorCollectionProvisioner.ProbeAsync(collectionName, ct) → Task<Result<VectorCollectionHealth>>` (P-280/P-281) — resolves only the abstraction plus the explicit `collectionName`, never a concrete `QdrantOptions`/`MilvusOptions` type, mapping `Reachable && CollectionAddressable && Queryable` → `Healthy`, else `Unhealthy` (never `Degraded`), with `PendingWriteCount` locked as informational-only, directly mirroring `AddSearchReadinessCheck`'s `SearchIndexHealth.PendingWriteCount` treatment; new `HealthCheckTags.VectorStore`/`HealthCheckNames.VectorStore`. `WithIntelligenceTelemetry(this IHostApplicationBuilder)` is the fifth sibling in the `With*Telemetry` family, string-name-only wiring `"SharedKernel.AI"` (`IntelligenceWellKnown.ActivitySourceName`/`.MeterName`), zero `ProjectReference`. **The third contract surfaced a new class of finding not previously encountered in the WO-043/WO-044 precedents:** `AddOrchestrationReadinessCheck` was to wrap a `ProbeAsync`-shaped member on `ICompletionProviderDescriptor` per `10.Intelligence/CLAUDE.md`'s Domain Invariant #8 prose — but that interface's own ratified, member-by-member Interface Contracts listing (verified by direct reading, not trusted from the invariant text alone) declares only `ProviderName`/`ContextWindowTokens`/`MaxOutputTokens`/`ValidateContextWindow`, with **no `ProbeAsync` member anywhere on `ICompletionProviderDescriptor` or `ISemanticKernel`**. This is a confirmed internal inconsistency inside `10.Intelligence`'s own brain file — a stronger blocker than the routine "design ratified, implementation pending" pattern recorded for `08.Storage`/`09.Search` (there the signature WAS fully specified in prose even though no code existed; here the signature itself is undefined). `13.ServiceDefaults` does not invent another domain's interface member on its behalf: only the extension method's outer shape was locked (no caller-supplied identifier needed, since `ICompletionProviderDescriptor` registers once per host; new `HealthCheckTags.Orchestration`/`HealthCheckNames.Orchestration` constants), and this Design task (D-11) itself is recorded `⚑` Blocked — the first Design-phase row in this domain's history to carry that state rather than `●`/`○`. Scaffold (S-15, the new `ProjectReference` to `SharedKernel.AI.Abstractions`) is not blocked — the target `.csproj` already exists as a registered bare stub, mirroring the S-12/S-13/S-14 precedents. Core (C-39, C-40, C-41), Tests (T-34, T-35, T-36), and Docs (DO-08) are all `⚑` Blocked. Interface Contracts, Implementation Rules, AOT Compatibility, and Test Rules sections all updated with the new contracts. No new Published task (mirrors the `AddSearchReadinessCheck`/WO-044 precedent). Package Board `SharedKernel.ServiceDefaults` reopened `●` (Published) → `◐` (Core); Overall Progress counts updated across Design/Scaffold/Core/Tests/Docs. **Out of this domain's jurisdiction:** the `ICompletionProviderDescriptor.ProbeAsync` contract-definition gap can only be resolved inside `10.Intelligence/CLAUDE.md` itself — flagged for `arch-lead`/`intelligence-arch-planner` (servicedefaults-arch-planner, WO-045, P-285)
- [2026-07-22] WO-046/P-289 phase added — Workflow Readiness Health Check + Telemetry Wiring, extending the "owning domain ships the probe, `13.ServiceDefaults` ships the `IHealthCheck` adapter" split toward the newly-designed `17.Workflows` domain. `WithWorkflowTelemetry(this IHostApplicationBuilder)` locked cleanly as the sixth sibling in the `With*Telemetry` family — string-name-only wiring `"SharedKernel.Workflows"` (`WorkflowWellKnown.ActivitySourceName`/`.MeterName`), zero `ProjectReference`, no issue found. **`AddWorkflowReadinessCheck` surfaced a genuine root-level LAYERING CONFLICT — a new and stronger class of finding than any prior instance of this pattern, including `AddOrchestrationReadinessCheck`'s D-11 gap:** wiring `IWorkflowServiceProbe.ProbeAsync(ct) → Task<Result<WorkflowServiceHealth>>` (P-287) requires a `ProjectReference` from `SharedKernel.ServiceDefaults` (layer 13) to `SharedKernel.Workflows.Temporal` (layer 17) — the root `CLAUDE.md`'s own Layering Rules table states "13.ServiceDefaults → may reference 01–12," confirmed by reading the table directly. Every prior instance of this domain's probe-wrapping pattern (`06.Persistence`/`08.Storage`/`09.Search`/`10.Intelligence`) involved an owning domain numbered ≤12; `17.Workflows` at layer 17 is the first case where it does not, and — unlike D-11, where the blocker was an undefined interface member inside `10.Intelligence`'s own brain — `17.Workflows/CLAUDE.md` fully and consistently specifies `IWorkflowServiceProbe`/`WorkflowServiceHealth`. The blocker here is purely architectural: the reference itself is forbidden, and `17.Workflows`'s own deliberate rejection of an `.Abstractions` package split ("durable execution's programming model IS the abstraction") leaves no lower-numbered companion package to reference instead. This planner has no authority to amend the root Layering Rules table or redesign `17.Workflows`'s package split, so `AddWorkflowReadinessCheck` is locked only to its outer shape (name/signature/no-identifier rationale mirroring `AddOrchestrationReadinessCheck`; new `HealthCheckTags.Workflows`/`HealthCheckNames.Workflows` constants) with the internal probe-wiring left explicitly open pending `arch-lead` resolving the conflict — via a second documented layering exception, directing `17.Workflows` to extract a lower-numbered probe-only package, or another root-level decision. Interface Contracts, Implementation Rules, AOT Compatibility, and Test Rules sections all updated to describe this as a partially-locked, layering-blocked contract rather than presented as a routine "design ratified, implementation pending" case. State-map: D-14 `●`; D-13 `⚑` Blocked at Design (the second Design-phase row in this domain's history to carry that state, after D-11); S-16 `⚑` Blocked (the `ProjectReference` itself, not merely "target not registered yet" — it is registered); C-42 doubly `⚑` Blocked (the layering conflict, plus `17.Workflows` being entirely unimplemented — `17.Workflows/state-map.md` verified directly, 0/81 tasks across all six phases); C-43/T-37/T-38/DO-09 `⚑` Blocked transitively. No new Published task (mirrors the `AddVectorStoreReadinessCheck`/WO-045 precedent). Package Board `SharedKernel.ServiceDefaults` reopened `●` (Published) → `◐` (Core). **Out of this domain's jurisdiction:** the layering conflict can only be resolved by `arch-lead` — flagged, not silently worked around (servicedefaults-arch-planner, WO-046, P-289)
- [2026-07-24] WO-044/P-277 unblocked and closed — re-verified `09.Search`'s blocker directly against `09.Search/state-map.md` and the compiled `SharedKernel.Search.Abstractions` source (not taken on trust): `09.Search` had since reached `Published` end to end (all six phases `●`, 139/139 tasks), and `ISearchIndexProvisioner.ProbeAsync(string indexName, CancellationToken) → Task<Result<SearchIndexHealth>>` + `SearchWellKnown.ActivitySourceName`/`.MeterName = "SharedKernel.Search"` exist exactly per the D-08/D-09 contracts already locked here. S-14 landed (new `ProjectReference` from `SharedKernel.ServiceDefaults.csproj` to `SharedKernel.Search.Abstractions.csproj`); C-37 landed (`SearchReadinessHealthCheck` + `SearchReadinessHealthCheckExtensions.AddSearchReadinessCheck`, `HealthCheckTags.Search`/`HealthCheckNames.Search` constants) and C-38 landed (`WithSearchTelemetry`) — zero deviation from the contracts already documented above, this was a pure "implement exactly what was already designed" pass once the blocker cleared. T-33 landed (`SearchReadinessHealthCheckTests` — Healthy/Unhealthy-never-Degraded via a substituted `ISearchIndexProvisioner`, a dedicated large-`PendingWriteCount`-still-`Healthy` regression, tag/name registration assertions folded into `HealthCheckTagTests`/`HealthCheckNamesTests`; `SearchTelemetryExtensionsTests` — idempotency/no-throw). DO-07 landed (XML docs; `README.md`'s `Program.cs` snippet gained `.AddSearchReadinessCheck("products-index")` + `builder.WithSearchTelemetry();`). Interface Contracts, the `Packages` table's `SharedKernel.ServiceDefaults` row, and the `HealthCheckNames.Search` note all updated to replace "design-locked/blocked" language with "implemented". `SK.13.Design/Scaffold/Core/Tests/Docs` phase keys remain `◐` — `WO-045`/`10.Intelligence` and `WO-046`/`17.Workflows` tasks under the same phase keys remain genuinely blocked, unaffected by this change. 63/63 `SharedKernel.ServiceDefaults.Tests` (+11) + 30/30 `SharedKernel.MultiTenancy.Tests` (unchanged) passing. Root `state-map.md`'s P-277 Phase Backlog entry promoted to `●` Complete (servicedefaults-phase-implementer)
- [2026-07-24] WO-047/P-291 — `SK.13.Design`'s two remaining `⚑` Blocked tasks (D-11, D-13) resolved, closing `SK.13.Design` to fully `●`/`—`. Both resolutions were ratified by `arch-lead` at the root level (recorded in the root `CLAUDE.md`'s WO-047 changelog entry, Layering Rules Hard rules, and Layering Rules diagram) and carried into this domain's own files in this pass — `arch-lead` deliberately did not edit this domain's files directly. **(1) D-11/`AddOrchestrationReadinessCheck` RETRACTED, not merely unblocked:** the `ICompletionProviderDescriptor.ProbeAsync` contract-definition gap this task was blocked on was resolved by retraction, not addition — `ICompletionProviderDescriptor` is a ratified zero-I/O singleton descriptor whose contract a `ProbeAsync` member would break, and the only honest reachability probe (a real, billed completion call) is itself forbidden by Domain Invariant #5. `AddOrchestrationReadinessCheck`, `HealthCheckNames.Orchestration`, and `HealthCheckTags.Orchestration` are permanently retracted from this domain's scope — removed from the Interface Contracts, Implementation Rules, AOT Compatibility, and Test Rules sections (previously only provisionally, partially locked, never implemented). D-11 recorded `—` (N/A/Retracted); downstream C-40 (Core)/T-35 (Tests) likewise `—`. **(2) D-13/`AddWorkflowReadinessCheck` FULLY LOCKED:** the root-level layering conflict (a `ProjectReference` from layer 13 to layer 17, forbidden by the root Layering Rules table) is resolved via a narrow, individually-named exception permitting `13.ServiceDefaults` a `ProjectReference` to `SharedKernel.Workflows.Temporal` scoped **exclusively** to `IWorkflowServiceProbe`/`WorkflowServiceHealth` — no other `17.Workflows` type. With the conflict cleared, the full contract (not just the outer shape) is now locked: `Healthy` iff `Result.IsSuccess && Reachable && NamespaceAddressable && WorkerPollersActive`. Re-verified `WorkerPollersActive`'s real CLR type against compiled `17.Workflows/SharedKernel.Workflows.Temporal/Health/WorkflowServiceHealth.cs` as D-13's own task note required: it is a **non-nullable `bool`**, not the provisionally-assumed `bool?` — but no `isWorkerHost` parameter is needed after all, because `WorkflowServiceHealth`'s own XML docs guarantee it is always `true` on a client-only registration ("there are no pollers to fail"); the probe implementation itself normalizes this case, so a single unconditional conjunct suffices. D-13 recorded `●`. **(3) Implementation-lag blockers independently cleared for both domains, verified on disk this pass (not implemented in this session — Core-phase work, out of scope for a Design-phase pass):** `10.Intelligence/SharedKernel.AI.Abstractions/Abstractions/IVectorCollectionProvisioner.cs`/`Models/VectorCollectionHealth.cs`/`Constants/IntelligenceWellKnown.cs` and `17.Workflows/SharedKernel.Workflows.Temporal/Health/IWorkflowServiceProbe.cs`/`Health/WorkflowServiceHealth.cs`/`Diagnostics/WorkflowDiagnostics.cs`/`Constants/WorkflowWellKnown.cs` were all confirmed present and matching their locked contracts. C-39/C-41 (Intelligence) and C-42/C-43 (Workflows) — plus their downstream T-34/T-36/T-37/T-38/DO-08/DO-09 and Scaffold task S-16 — are reclassified from `⚑` Blocked to `○` Not Started in the state-map: no cross-domain blocker of any kind remains for any of them, only the ordinary "not yet implemented" state pending a future `SK.13.Core`/`SK.13.Scaffold` session. Interface Contracts, Implementation Rules, AOT Compatibility, and Test Rules sections updated throughout to reflect "upstream shipped, Core pending" instead of "blocked". `SK.13.Design` now 13 `●` + 1 `—` (D-11) = fully resolved, promoted to root via `/state-map-phase`. No C# code changed in this session (Design-phase documentation/contract-resolution pass only); `SharedKernel.ServiceDefaults.Tests`/`SharedKernel.MultiTenancy.Tests` re-run unchanged as a regression gate (servicedefaults-phase-implementer, WO-047, P-291)
- [2026-07-27] `SK.13.Scaffold` closed to 16/16 `●` — S-15/S-16 landed, both pure `.csproj` `ProjectReference` additions with zero implementation logic (Core-phase C-39/C-41–43 remain out of scope, still pending). `SharedKernel.ServiceDefaults.csproj` gained `ProjectReference`s to `SharedKernel.AI.Abstractions` (10.Intelligence, S-15) and `SharedKernel.Workflows.Temporal` (17.Workflows, S-16 — the WO-047-granted reference, now guarded by an inline `.csproj` comment restating its exact `IWorkflowServiceProbe`/`WorkflowServiceHealth`-only scope). `dotnet build -c Release` clean (0 warnings/0 errors); full `SharedKernel.ServiceDefaults.Tests` regression 63/63 passing, 0 regressions. Packages table and the `AddWorkflowReadinessCheck` LAYERING NOTE/Implementation Rules bullet updated from "not yet added" to reflect the reference now exists — the only stale present-tense claims this session's ground-truth check found. Promoted to root; Domain Summary Board row 13 deliberately left at `Published`/`●` (Summary cells refreshed instead), mirroring the SK.13.Design/2026-07-24 precedent (servicedefaults-phase-implementer, WO-045, WO-046, WO-047)
- [2026-07-27] `SK.13.Core`/`SK.13.Tests`/`SK.13.Docs` all closed to `●` (42/43, 37/38, 9/9 — the only non-`●` rows are the permanently-retracted C-40/T-35) — C-39/C-41/C-42/C-43 implemented exactly per the D-10/D-12/D-13/D-14 contracts already locked in this file, zero deviation: `VectorStoreReadinessHealthCheck`/`AddVectorStoreReadinessCheck` and `WorkflowReadinessHealthCheck`/`AddWorkflowReadinessCheck` (`HealthChecks/`), `IntelligenceTelemetryExtensions.WithIntelligenceTelemetry`/`WorkflowTelemetryExtensions.WithWorkflowTelemetry` (`Telemetry/`). All six `SK.13.*` phase keys are now `●`/`—` — domain fully complete end to end again, closing WO-045/WO-046/WO-047's entire `13.ServiceDefaults`-side scope. Interface Contracts, Implementation Rules, AOT Compatibility, and Test Rules sections all updated throughout to replace "design-locked"/"blocked"/"Core implementation pending" language with "implemented" (also caught and fixed two already-stale `AddSearchReadinessCheck`/`WithSearchTelemetry` mentions that had never been updated after WO-044's own 2026-07-24 closeout). `README.md`'s `Program.cs` snippet and ordering rules updated with both new checks and both new telemetry methods, plus a note on the WO-047 layering exception. 86/86 `SharedKernel.ServiceDefaults.Tests` (+23) + 30/30 `SharedKernel.MultiTenancy.Tests` passing, 0 regressions. Root `state-map.md`'s P-285/P-289 Phase Backlog entries promoted to `●` Complete (servicedefaults-phase-implementer, WO-045, WO-046, WO-047)
- [2026-07-29] `SK.13.Core`/`SK.13.Tests`/`SK.13.Docs` all closed to `●` again (43/44+1 N/A, 38/39+1 N/A, 10/10) — C-44/T-39/DO-10 (WO-050/P-305) implemented exactly per D-15's already-locked contract, zero deviation, once `02.Caching`'s Phase 41 (`SK.02.OtelTracingSpans`, P-304) blocker was re-verified cleared directly against `02.Caching/state-map.md` (all nine `OT-01..OT-09` tasks confirmed `●`) and the compiled `FusionCacheService.cs` source (`ActivitySource("SharedKernel.Caching", "1.0")` present, byte-identical name/version to the existing `Meter`) — not taken on trust, per this domain's own established WO-043/WO-044/WO-045/WO-046 re-verify discipline. `CachingTelemetryExtensions.WithCachingTelemetry()` now wires both `WithTracing(t => t.AddSource(CachingInstrumentationName))`/`WithMetrics(m => m.AddMeter(CachingInstrumentationName))`; `CachingMeterName` renamed `CachingInstrumentationName`. `CachingTelemetryExtensionsTests` gained a genuine span-capture test (`WithCachingTelemetry_SpanFromCachingActivitySource_IsCaptured`, `OpenTelemetry.BaseProcessor<Activity>` + `provider.GetRequiredService<TracerProvider>()` to force the SDK pipeline live — verified to fail without the `AddSource` call, so it is not trivially passing) and the pre-existing single idempotency test was split into separate tracing/metrics variants, matching the already-split shape of its five siblings. **Documentation-drift correction found and fixed while implementing T-39:** the phase input's claim that `MessagingTelemetryExtensionsTests`'/`SearchTelemetryExtensionsTests`'/`IntelligenceTelemetryExtensionsTests`' own test files already contain an `ActivityListener`-based span-capture pattern was checked directly against those files and found false — all five sibling test files only assert `TracerProviderBuilder`/`MeterProviderBuilder` DI-registration counts, never actual span capture; `WithCachingTelemetry`'s new test is the first genuine capture proof in this domain's `Telemetry/` test suite, built instead on this same project's own `BaggageLogRecordProcessorTests`/`AmbientLoggingEnrichmentAcceptanceTests` "capturing `BaseProcessor<T>` on the real pipeline" idiom. Interface Contracts, the two Implementation Rules bullets (ActivitySource-ownership ledger, the `WithCachingTelemetry` parity bullet), and Test Rules all updated from "DESIGNED/blocked" to "implemented", with the pattern correction recorded inline for future sibling `With*Telemetry` phases. `README.md` checked and confirmed to already describe all six `With*Telemetry` methods generically — no change needed. 88/88 `SharedKernel.ServiceDefaults.Tests` (+2) + 30/30 `SharedKernel.MultiTenancy.Tests` passing, 0 regressions. Root `state-map.md`'s P-305 Phase Backlog entry promoted to `●` Complete (servicedefaults-phase-implementer, WO-050, P-305)
- [2026-07-29] WO-050/P-305 phase added — Wire the new `02.Caching` `ActivitySource` into `WithCachingTelemetry`. Closes `WithCachingTelemetry`'s status as the sole remaining single-signal member of the `With*Telemetry` family — it has wired the `"SharedKernel.Caching"` meter alone since its P-010/WO-003 origin, predating the "wire both a tracing source and a meter" convention this platform later established for every sibling added since (`WithMessagingTelemetry`/`WithApplicationTelemetry`/`WithSearchTelemetry`/`WithIntelligenceTelemetry`/`WithWorkflowTelemetry`). D-15 design-locked and closed immediately, mirroring the D-08/D-09/D-10/D-12/D-14 documented-ahead-of-implementation precedent: add `WithTracing(t => t.AddSource(CachingInstrumentationName))` alongside the existing metrics wiring; rename the pre-existing `private const string CachingMeterName` to `CachingInstrumentationName` (same value, naming-parity rename only, matching `SearchInstrumentationName`/`IntelligenceInstrumentationName`/`WorkflowInstrumentationName`); zero new `ProjectReference` (string-name-only wiring, `SharedKernel.ServiceDefaults.csproj` already references only `SharedKernel.Caching.Abstractions`, never `.FusionCache`). **Dependency verified against ground truth, not assumed** from the phase input's "P-304 dispatched in the same run" framing: read `02.Caching/state-map.md` and `02.Caching/CLAUDE.md` directly — Phase 41 (`SK.02.OtelTracingSpans`, P-304) already ratifies the exact contract in prose (`ActivitySource("SharedKernel.Caching", "1.0")`, same instrumentation-scope name/version as the existing Phase 31 `Meter`, producing `cache.get`/`cache.set`/`cache.get_or_set` spans tagged `cache.key_prefix`+`cache.outcome`) but all nine of its tasks (OT-01 through OT-09) are `○` Not Started as of this dispatch — nothing has been implemented in `02.Caching` yet. C-44 (Core), T-39 (Tests), DO-10 (Docs) are recorded `⚑` Blocked in the state-map pending `02.Caching`'s `SK.02.OtelTracingSpans` phase landing, per this domain's own established WO-027/C-19 precedent ("cannot wire a source that does not yet exist in code"). No new Scaffold task (no new `ProjectReference` needed) and no new Published task (additive behavior change inside an already-shipped method, mirroring the `AddWorkflowReadinessCheck`/WO-046 precedent). Interface Contracts (`WithCachingTelemetry`), Implementation Rules, and Test Rules sections all updated with the new contract, explicitly marked DESIGNED/blocked rather than presented as shipped. Package Board `SharedKernel.ServiceDefaults` reopened `●` (Published) → `◐` (Core) (servicedefaults-arch-planner, WO-050, P-305)
- [2026-07-30] WO-051/P-326 phase added — `WithPersistenceTelemetry`, wiring `06.Persistence`'s new `PersistenceActivitySource` (WO-051/P-319) into the host, extending the OpenTelemetry wiring section's `With*Telemetry` family to a seventh member and closing the gap `06.Persistence` was the only foundational infrastructure domain (`01`–`12`) never to receive a `WithXTelemetry` entry point, because it had no `ActivitySource`/`Meter` to wire until P-319. Design D-16 design-locked and closed immediately, mirroring the D-08/D-09/D-10/D-12/D-14/D-15 documented-ahead-of-implementation precedent: `WithPersistenceTelemetry(this IHostApplicationBuilder)` wires `"SharedKernel.Persistence"` into `TracerProvider` via `WithTracing(t => t.AddSource(PersistenceInstrumentationName))` — string-name-only, zero new `ProjectReference` (`PersistenceActivitySource` is `internal`, no `InternalsVisibleTo` grant to `SharedKernel.ServiceDefaults`, the same situation as `WithMessagingTelemetry`/`WithApplicationTelemetry`). **Materially different from all six existing siblings, locked explicitly as such rather than left ambiguous:** this method wires tracing ONLY — deliberately no `WithMetrics(...)` call — because `06.Persistence/CLAUDE.md`'s own ratified Observability section (design task D-73/P-319) documents only an `ActivitySource` (`"SharedKernel.Persistence"`, `"1.0"`) plus `PersistenceTagKeys`, no companion `Meter`, in this pass; this is recorded as a deliberate scope decision traceable to what `06.Persistence` actually ships, distinct from `WithCachingTelemetry`'s metrics-only-then-both evolution (D-15/WO-050), which was a genuine retrofit of a pre-existing gap rather than a settled design choice. **Dependency verified against ground truth, not assumed** from the WO-051 dispatcher note's framing (per this domain's own standing verify-before-trusting discipline): read `06.Persistence/state-map.md` directly — C-110 (`PersistenceActivitySource`/`PersistenceTagKeys`) is `○` Not Started as of this dispatch; nothing has been implemented in `06.Persistence` yet, only ratified in `06.Persistence/CLAUDE.md`'s Observability section, which itself explicitly names this phase (P-326) as its coordination point ("do not rename without updating that consumer"). C-45 (Core), T-40 (Tests), DO-11 (Docs) are recorded `⚑` Blocked in the state-map pending `06.Persistence`'s C-110 landing. No new Scaffold task (string-name-only wiring needs no `ProjectReference`, the same shape as five of its six siblings) and no new Published task (additive extension method inside the already-published package). Interface Contracts' OpenTelemetry wiring section updated with the new `WithPersistenceTelemetry` contract, explicitly marked design-locked/blocked rather than presented as shipped. Package Board `SharedKernel.ServiceDefaults` reopened `●` (Published) → `◐` (Core) (servicedefaults-arch-planner, WO-051, P-326)
- [2026-07-30] `SK.13.Core`/`SK.13.Tests`/`SK.13.Docs` all closed to `●` again (45/45, 40/40, 11/11 — no retracted rows remain in the tables this session touched) — C-45/T-40/DO-11 (WO-051/P-326) implemented exactly per D-16's already-locked contract, zero deviation, once `06.Persistence`'s C-110 blocker was re-verified cleared directly against `06.Persistence/state-map.md` (C-110 confirmed `●`) and the compiled `SharedKernel.Persistence.EfCore/Diagnostics/PersistenceActivitySource.cs` source (`internal static class` with `Source = new ActivitySource("SharedKernel.Persistence", "1.0")`, byte-identical to the locked name/version) — not taken on trust, per this domain's own established WO-043/WO-044/WO-045/WO-046/WO-050 re-verify discipline. New `Telemetry/PersistenceTelemetryExtensions.cs` wires `WithTracing(t => t.AddSource(PersistenceInstrumentationName))` only — no `WithMetrics(...)` call, the first genuinely tracing-only member of the `With*Telemetry` family. New `PersistenceTelemetryExtensionsTests.cs` (3 tests): tracing-idempotency (`TracerProviderBuilder` registration count `<= 1`), a no-throw test, and a genuine `BaseProcessor<Activity>` + `provider.GetRequiredService<TracerProvider>()` span-capture test (`WithPersistenceTelemetry_SpanFromPersistenceActivitySource_IsCaptured`) mirroring `WithCachingTelemetry`'s T-39 precedent exactly — verified to fail (empty captured list) when the `AddSource` call is temporarily removed, confirming it is not trivially passing. No metrics-idempotency counterpart test was written, per D-16/T-40's own scope. Interface Contracts, Implementation Rules (`With*Telemetry` idempotency bullet), AOT Compatibility, and Test Rules sections all updated from "design-locked/blocked" to "implemented". `README.md`'s `Program.cs` snippet and ordering rule 4 updated with `builder.WithPersistenceTelemetry();` and its tracing-only rationale. 91/91 `SharedKernel.ServiceDefaults.Tests` passing (+3), 0 regressions; `SharedKernel.MultiTenancy.Tests` untouched this session. Root `state-map.md`'s P-326 Phase Backlog entry promoted to `●` Complete (servicedefaults-phase-implementer, WO-051, P-326)

- [2026-08-07] WO-054/P-351 implemented and tested — S-17/C-46/T-41/T-42/DO-12 all `●`. Re-verified the blocker directly against ground truth before implementing (per this domain's own established verify-before-trusting discipline): `07.Messaging/state-map.md` confirmed `SK.07.ReadinessProbe` (RP-01–RP-10) fully `●` (10/10), and the real `SharedKernel.Messaging.Abstractions.MessageBus.IMessageBusProbe.ProbeAsync(CancellationToken) → Task<MessageBusHealth>` / `MessageBusHealth(bool IsHealthy, string? Description)` were read directly from compiled source — both match D-17's locked contract exactly, no correction needed. `S-17`: removed `AspNetCore.HealthChecks.Rabbitmq`, `AspNetCore.HealthChecks.AzureServiceBus`, `RabbitMQ.Client`, `Azure.Messaging.ServiceBus`, `Azure.Identity` `PackageReference`s from `SharedKernel.ServiceDefaults.csproj`; `<Description>` metadata updated. `C-46`: added `MessagingReadinessHealthCheck` (internal sealed, ctor takes only `IMessageBusProbe`, maps `IsHealthy` → `Healthy`/`Unhealthy`, never `Degraded`, `Description` surfaced via `HealthCheckResult.Description`) and `MessagingReadinessHealthCheckExtensions.AddMessagingReadinessCheck` (mirrors `AddWorkflowReadinessCheck`'s registration shape, tags `Ready`+`Messaging`), both in `HealthChecks/`; added `HealthCheckNames.Messaging = "messaging"`. Deleted as dead code in the same commit: `RabbitMqMessagingHealthCheckExtensions.cs`, `AzureServiceBusMessagingHealthCheckExtensions.cs`, `AzureServiceBusHealthCheck.cs` (incl. `AzureServiceBusConnectionStringMarkers`), and the `HealthCheckNames.RabbitMq`/`.AzureServiceBus` constants. `T-41`/`T-42`: new `MessagingReadinessHealthCheckTests.cs` (3 tests — Healthy mapping, Unhealthy-never-Degraded with `Description` surfaced verbatim, and cancellation-token propagation; its own XML doc records the structural gating proof — the constructor accepts only `IMessageBusProbe`, no `RabbitMQ.Client`/`Azure.Messaging.ServiceBus`/AMQP-URI/connection-string surface exists anywhere in the type or its test's dependency graph); `HealthCheckNamesTests.cs`/`HealthCheckTagTests.cs` had their two RabbitMq/AzureServiceBus cases each replaced by one `AddMessagingReadinessCheck` case; `HealthCheckEndpointTests.cs`'s `HealthLive_NeverEvaluatesRealRabbitMqOrAzureServiceBusHealthCheckRegistrations` replaced with `HealthLive_NeverEvaluatesRealMessagingReadinessCheckRegistration`, using a substituted `IMessageBusProbe` returning an unhealthy result to prove the real opt-in extension method (not a synthetic `AddCheck`) is excluded from `/health/live` by tag. `DO-12`: XML docs on all new types; `README.md`'s `Program.cs` snippet and ordering rule 3 updated (`.AddMessagingReadinessCheck()` replacing the two retired calls), plus a migration note stating the two old methods are removed, not deprecated, and why; this file's Packages/Technology Stack/Interface Contracts/`HealthCheckNames`/Implementation Rules/DI Registration/AOT Compatibility/Test Rules sections all updated from "design-locked, not yet implemented"/"blocked" to "implemented and tested". Confirmed a **confirmed breaking change** to a published package (two public extension methods removed outright) — a SemVer-major repack is needed at the next `devops-lead` publish pass, not performed here. `state-map.md`'s `## Blocked` table cleared (all four rows resolved). 92/92 `SharedKernel.ServiceDefaults.Tests` passing (+1 net: -4 retired-method tests, +5 new/replacement tests), 0 regressions; `SharedKernel.MultiTenancy.Tests` untouched this session (servicedefaults-phase-implementer, WO-054, P-351)
- [2026-08-07] Brain-sync verification pass for WO-054/P-351 — confirmed the phase-implementer's direct edits (Packages table row, Technology Stack row, both Interface Contracts entries incl. the "RESOLVED PATTERN" tail, `HealthCheckNames` block, two Implementation Rules bullets, DI Registration snippet, one AOT Compatibility bullet, two Test Rules bullets, changelog entry) are complete, correctly placed, and consistent with the rest of the file — no stray "design-locked"/"blocked"/"not yet implemented" language referring to `AddMessagingReadinessCheck` or the retired `AddRabbitMqMessagingHealthCheck`/`AddAzureServiceBusMessagingHealthCheck` remains anywhere outside the (correctly append-only, historical) Changelog. Root `CLAUDE.md`'s "OTel, health check, or probe wiring → 13.ServiceDefaults" row was checked directly (not assumed) — it names no specific method signatures for this domain, so it needs no update. No further sub-domain or root edits warranted (sync-brain)
- [2026-08-11] WO-056/P-365: `WithCommunicationTelemetry` phase added — the eighth `With*Telemetry` sibling, dispatched from `arch-lead`'s `11.Communication` gold-standard architecture review (WO-056, ten phases total, nine landing in `11.Communication` itself and this one phase landing here). Design D-18 locked and closed immediately with **zero cross-domain gate** — the first phase in this domain's history able to say that — confirmed by direct `.csproj` inspection rather than trusted from the phase-dispatch note: `11.Communication.Grpc/SharedKernel.Communication.Grpc.csproj` already references `OpenTelemetry.Instrumentation.GrpcNetClient` (`1.15.1-beta.1`) with zero call site anywhere invoking `.AddGrpcClientInstrumentation()` (confirmed via repo-wide grep); `11.Communication.Rest/SharedKernel.Communication.Rest.csproj` already references `Microsoft.Extensions.Http.Resilience` (`10.7.0`), whose default `StandardResilienceHandler` telemetry (Polly v8's own `"Polly"`-named `ActivitySource`/`Meter`) is likewise never wired into any `TracerProvider`/`MeterProvider`. `WithCommunicationTelemetry(this IHostApplicationBuilder)` activates both: (1) gRPC (tracing only) via `.AddGrpcClientInstrumentation()` — the family's first member requiring a genuine new instrumentation `PackageReference` (S-18, version-aligned with `11.Communication.Grpc.csproj`'s pin) rather than pure string-name wiring, and deliberately taking zero `ProjectReference` to `11.Communication.Grpc` (the instrumentation package hooks the client pipeline via `DiagnosticSource` at runtime); (2) Polly (tracing + metrics) via bare `AddSource("Polly")`/`AddMeter("Polly")` — zero new package/project reference, mirroring `WithMessagingTelemetry`'s pre-existing third-party `"MassTransit"`-name precedent. **A genuine idempotency risk was surfaced and recorded as a GATING Core/Tests criterion, not assumed away:** `.AddGrpcClientInstrumentation()` is an instrumentation-factory registration, not a bare `AddSource(string)` call, so unlike every other family sibling it does not automatically inherit the OTel SDK's by-name dedup — Core (C-47) must empirically verify repeat-call behavior and guard the method if one is needed, and Tests (T-43) must exercise that guard directly, plus prove genuine span/metric capture from a real (or in-process) gRPC call and a Polly retry, following the `BaseProcessor<Activity>` + `GetRequiredService<TracerProvider>()` capture idiom already established by `WithCachingTelemetry`'s T-39/`WithPersistenceTelemetry`'s T-40 — never a test-owned `ActivityListener`, which would prove nothing. Packages/Technology Stack/Interface Contracts/Implementation Rules/AOT Compatibility/Test Rules sections all updated to describe the locked target contract, explicitly marked "design-locked, not yet implemented" throughout — consult `state-map.md` for current status. State-map tasks added: D-18 (`SK.13.Design`, locked/closed now), S-18/C-47/T-43/DO-13 (Scaffold/Core/Tests/Docs, all ordinary `○` Not Started, never `⚑` Blocked). No new Published task (mirrors the `WithPersistenceTelemetry`/WO-051 precedent). Package Board `SharedKernel.ServiceDefaults` reopened `●` (Published) → `◐` (Core) (servicedefaults-arch-planner, WO-056, P-365)
- [2026-08-12] C-47 implemented and tested — `WithCommunicationTelemetry()` shipped in `Telemetry/CommunicationTelemetryExtensions.cs` (S-18's `OpenTelemetry.Instrumentation.GrpcNetClient` `PackageReference` landed the same day). Two empirical findings corrected/resolved D-18's design under its own explicit reconfirm-before-shipping gate, both by decompiling (`ilspycmd`) the actual installed assemblies rather than trusting the design note: (1) the `"Polly"` `Meter` name/version is confirmed correct, but Polly v8.4.2 (the version pinned by `Microsoft.Extensions.Http.Resilience 10.7.0`) creates **no `ActivitySource`** anywhere in its dependency chain (zero matches across all six pinned assemblies) — so `WithMetrics(AddMeter("Polly"))` is wired but `WithTracing(AddSource("Polly"))` is not; tracing is gRPC-only. (2) `.AddGrpcClientInstrumentation()`'s idempotency was confirmed already dedup-safe by the third-party package's own `TryAddSingleton` + internally-guarded one-time-subscription design — no custom static-flag guard was added. Packages/Technology Stack/Interface Contracts/Implementation Rules/AOT Compatibility/Test Rules sections all updated from "design-locked, not yet implemented" to "IMPLEMENTED and tested" (Test Rules' T-43 entry updated to reflect what remains — the genuine capture proof — rather than marked implemented, since T-43 itself is still `○`). New `CommunicationTelemetryExtensionsTests.cs` (4 baseline tests). `dotnet build -c Release` clean; `SharedKernel.ServiceDefaults.Tests` 96/96 passing (+4), 0 regressions. `SK.13.Core` now 46/47 `●` (+1 `—` N/A: C-40), fully closed; `SK.13.Tests` (T-43)/`SK.13.Docs` (DO-13) remain `○` for a future session (servicedefaults-phase-implementer, WO-056, P-365)
- [2026-08-12] T-43 implemented and tested — the phase's own explicit gating acceptance criterion (genuine capture proof, not registration-count-only) proven in `CommunicationTelemetryExtensionsTests.cs`, extended with 4 new tests atop C-47's 4 baseline tests. Added new test-only fixtures under `Telemetry/GrpcFixtures/` (`greeter.proto`, `GreeterService`, `GrpcTestWebApplicationFactory`, `ResponseVersionHandler`) hosting a minimal, real, in-process `Grpc.AspNetCore` service via `WebApplicationFactory`'s in-memory `TestServer` (never a real Kestrel socket), called through a genuine `Grpc.Net.Client` channel — the well-documented `ResponseVersionHandler`/content-root-pinning workarounds this combination requires were both needed and applied. `WithCommunicationTelemetry_GrpcCall_ProducesSpanWithRpcSystemGrpcTag`: makes one real gRPC call after wiring, captures the resulting span via this domain's established `BaseProcessor<Activity>` + `provider.GetRequiredService<TracerProvider>()` idiom, asserts `rpc.system == "grpc"` — proving `.AddGrpcClientInstrumentation()` genuinely took effect, not merely that the call didn't throw. `WithCommunicationTelemetry_CalledTwice_GrpcCall_DoesNotProduceDuplicateSpan`: registers twice, asserts exactly one — not two — rpc.system-tagged spans for one call, exercising C-47's idempotency finding directly rather than trusting its changelog note, per D-18/C-47's flagged GATING risk. `WithCommunicationTelemetry_PollyRetry_EmitsPollyMeterMetric`: a real standalone `ResiliencePipelineBuilder` retry pipeline (`AddRetry` + `ConfigureTelemetry(new TelemetryOptions())`, one forced retry attempt) captured via `OpenTelemetry.Exporter.InMemory`'s `AddInMemoryExporter`, asserting an exported `Metric` with `MeterName == "Polly"` — **metric only, per C-47's already-confirmed finding that Polly v8.4.2 emits no `ActivitySource` in this dependency chain; the span half of the phase's original criterion (2) is UNACHIEVABLE against the pinned version, not missed, and is recorded as such rather than silently dropped.** `WithCommunicationTelemetry_CalledTwice_PollyRetry_DoesNotDuplicateMetric`: runs the identical retry execution against a single vs. a double `WithCommunicationTelemetry()` registration in two isolated `MeterProvider`s and asserts equal exported-metric counts, proving no duplication — the metrics-only reduction of criterion (3)'s idempotency guard. All four new tests were verified during implementation to genuinely FAIL (temporarily removing `.AddGrpcClientInstrumentation()` empties the gRPC tests' captured-span collection entirely; temporarily removing `WithMetrics(AddMeter("Polly"))` empties the Polly tests' exported-metrics collection) before the production code was restored unchanged — not trivially passing, mirroring the T-39/T-40 precedent's own verification discipline. New test-project-only `PackageReference`s (never added to the production `SharedKernel.ServiceDefaults.csproj`): `Grpc.AspNetCore`/`Grpc.Net.Client` `2.80.0`; `Polly.Core`/`Polly.Extensions` `8.7.0` — bumped up from the production-pinned `8.4.2` because `SharedKernel.Testing -> SharedKernel.Application.Behaviors` already floors `Polly.Core` at `>= 8.7.0` transitively in this test project's dependency graph and NU1605 (an error under this solution's `TreatWarningsAsErrors`) forbids downgrading below a floor already set elsewhere in the graph — the "Polly"-named `Meter` this test proves against is a stable, documented public surface of Polly's own resilience telemetry, not a version-pinned implementation detail, so the version bump does not weaken the proof; `OpenTelemetry.Exporter.InMemory` `1.16.0`, the metrics-signal analogue of this project's existing tracing-capture idiom, with no prior precedent in this test suite. Interface Contracts and Test Rules sections updated from "still `○` Not Started" to "IMPLEMENTED and tested". `dotnet build -c Release` clean (0 errors); full `SharedKernel.ServiceDefaults.Tests` suite: 100/100 passing (+4 new), 0 regressions. `SK.13.Tests` now 42/43 `●` (+1 `—` N/A: T-35) — fully `●`/`—`, promoted to root (Domain Summary Board row 13 deliberately left at `Published`/`●` — Summary cells refreshed instead — per this domain's own twice-corrected convention). `SK.13.Docs` (DO-13) remains `○` Not Started for a future session — out of scope for this Tests-only pass. Root `state-map.md`'s Phase Backlog `P-365` entry left open (not closed by this promotion — `SK.13.Tests` is a standard lifecycle phase key with no individual Phase Backlog binding; `P-365` closes once `SK.13.Docs`/DO-13 lands and the domain's SemVer-major repack from WO-054/P-351 is separately handled by `devops-lead`) (servicedefaults-phase-implementer, WO-056, P-365)
- [2026-08-12] Brain-sync verification pass for WO-056/P-365's DO-13 — confirmed the `WithCommunicationTelemetry` Interface Contracts entry, the Implementation Rules bullets (idempotency/ActivitySource-ownership/no-static-mutable-state/telemetry-only), the AOT Compatibility bullet, and the Test Rules T-43 bullet were all already complete and correctly marked "IMPLEMENTED and tested" from the C-47/T-43 sessions — no stray "design-locked, not yet implemented" language referring to `WithCommunicationTelemetry`/`PollyInstrumentationName`/`GrpcNetClient` remains anywhere outside the (correctly append-only, historical) Changelog. DO-13's only real edit landed in `README.md` (`Program.cs` snippet + ordering rule 4), not this file. This closes WO-056/P-365 end to end — all six `SK.13.*` phase keys `●` again, root Phase Backlog P-365 closed. No further sub-domain edits warranted this pass (sync-brain)
- [2026-08-13] WO-058/P-378 phase added — `AddMtlsClientCertificate`/`AddMtlsForwardedHeaderCertificate`, a new Kestrel mTLS client-certificate composition surface, dispatched from `arch-lead` alongside `12.Security`'s concurrent `SharedKernel.Security.Mtls` design (P-377, dispatched to `security-arch-planner` in the same run). New `Security/` folder and Interface Contracts subsection added: `AddMtlsClientCertificate(this IHostApplicationBuilder, ClientCertificateMode mode = AllowCertificate)` wires Kestrel's `ConfigureHttpsDefaults` for direct-TLS-termination hosts via `services.AddOptions<KestrelServerOptions>().Configure<IMtlsCertificateValidator>(...)`, delegating the accept/reject decision to `12.Security`'s not-yet-shipped `IMtlsCertificateValidator` — never reimplemented here, mirroring `ClaimTenantResolutionStrategy`'s delegate-never-reimplement pattern. `AddMtlsForwardedHeaderCertificate`/`MtlsForwardedHeaderOptions`/`MtlsForwardedHeaderMiddleware` covers ingress-terminated-TLS hosts, reading a client certificate from a request header whose name carries **no default tied to any ingress vendor's convention** — a consuming service must configure `HeaderName` explicitly, satisfying the phase's own acceptance criterion directly. **Genuinely blocked at Scaffold/Core, not merely design-ahead-of-implementation like most of this domain's prior cross-domain phases:** `SharedKernel.Security.Mtls` does not exist on disk at all as of this dispatch — no placeholder `.csproj` is registered in `Platform.SharedKernel.slnx` either, unlike the `08.Storage`/`09.Search`/`10.Intelligence` precedents where a bare stub project already existed to reference ahead of its real types landing. **One open design question flagged explicitly, not guessed:** Kestrel's `ClientCertificateValidation` delegate is synchronous, so if `IMtlsCertificateValidator` ships async-only (this platform's usual convention), Core must document a blocking `.GetAwaiter().GetResult()` bridge inside the TLS handshake path rather than assume a sync member will exist — to be resolved by reading `12.Security`'s actual shipped signature, mirroring this domain's standing "verify against compiled/decompiled source, never assumed" discipline (the `C-47` Polly precedent). **No layering exception needed** — `SharedKernel.Security.Mtls` is a concrete `12.Security` provider package, already inside this domain's granted `01`–`12` composition-root range (the same range `SharedKernel.Security.Oidc` already occupies), unlike `AddWorkflowReadinessCheck`'s named `17.Workflows` grant. State-map tasks added: D-19 (`SK.13.Design`, locked/closed now, mirroring the D-07/D-08/D-17 documented-ahead-of-implementation precedent), S-19/C-48/T-44/DO-14 (Scaffold/Core/Tests/Docs, all `⚑` Blocked — not ordinary `○` Not Started, since unlike `WithCommunicationTelemetry`'s zero-cross-domain-gate precedent this phase has a real, currently-nonexistent dependency to wait for). No new Published task (mirrors the `WithPersistenceTelemetry`/`WithCommunicationTelemetry` precedent — additive extension methods inside the already-published `SharedKernel.ServiceDefaults` package). Package Board `SharedKernel.ServiceDefaults` reopened `●`/`◐` → `◐` with a new blocked note; Blocked section and Cross-Domain Dependencies updated with the genuine `12.Security` gate (servicedefaults-arch-planner, WO-058, P-378)
- [2026-08-14] S-19 (`SK.13.Scaffold`) implemented — the recorded blocker was re-verified, not taken on trust, and found stale: `12.Security` shipped `SharedKernel.Security.Mtls` end to end on 2026-08-13. `ProjectReference` added to `SharedKernel.ServiceDefaults.csproj`; empty `Security/` folder created. `IMtlsCertificateValidator.ValidateAsync` confirmed async-only (`Task<MtlsValidationResult> ValidateAsync(X509Certificate2, CancellationToken)`), resolving D-19's open sync-vs-async question — Core must bridge via `.GetAwaiter().GetResult()`. Packages table, Technology Stack row, and both Interface Contracts entries updated from "blocked at Scaffold/Core" to "Scaffold shipped, Core/Tests/Docs (C-48/T-44/DO-14) still not yet implemented." `dotnet build -c Release` clean; `SharedKernel.ServiceDefaults.Tests` 100/100 passing, 0 regressions — no test changes needed for this Scaffold-only wiring task (servicedefaults-phase-implementer, WO-058, P-378)
- [2026-08-14] C-48 (`SK.13.Core`) implemented — `AddMtlsClientCertificate`/`MtlsForwardedHeaderOptions`/`AddMtlsForwardedHeaderCertificate`/`MtlsForwardedHeaderMiddleware` shipped in new `Security/`. Re-verified `IMtlsCertificateValidator.ValidateAsync` async-only directly (not trusted from the S-19 note). **Genuine correction to D-19's original design, found during implementation:** `Configure<IMtlsCertificateValidator>(...)` would resolve the Scoped validator once from the ROOT container at Kestrel-options-configuration time — no ambient `HttpContext.RequestServices` exists at the TLS-handshake level — throwing under `ServiceProviderOptions.ValidateScopes = true`; corrected to capture `IServiceScopeFactory` and create a fresh `IServiceScope` per TLS handshake instead, documented explicitly in XML docs alongside the pre-planned blocking-bridge latency cost. `MtlsForwardedHeaderMiddleware`'s previously-open decode-shape (Base64 DER, falling back to URL-decoded PEM; XFCC deliberately unsupported) and certificate-exposure (`HttpContext.Connection.ClientCertificate`, confirmed settable by decompiling `Microsoft.AspNetCore.Http.dll`) questions are both resolved. Packages/Technology Stack/Interface Contracts/AOT Compatibility/Test Rules sections all updated from "design-locked, not yet implemented" to "IMPLEMENTED". `dotnet build -c Release` clean (0 warnings); `SharedKernel.ServiceDefaults.Tests` 100/100 passing, 0 regressions (no new tests — T-44 remains its own separate, still-open phase key). `SK.13.Core` now 47/48 `●` (+1 `—` N/A: C-40), fully `●`/`—`, promoted to root (servicedefaults-phase-implementer, WO-058, P-378)
- [2026-08-14] T-44 (`SK.13.Tests`) implemented — the recorded `⚑` blocker (stated dependency: C-48) was re-verified against the real shipped files, not taken on trust, and found stale: `SK.13.Core` was already 47/48 `●` and all four `Security/*.cs` production files existed. New `SharedKernel.ServiceDefaults.Tests/Security/` folder: `MtlsClientCertificateExtensionsTests` (8 tests) resolves the real `KestrelServerOptions` a built `IHost` produces and, via reflection over Kestrel's private `HttpsDefaults` property — confirmed by decompiling the shipped `Microsoft.AspNetCore.Server.Kestrel.Core.dll` (`ilspycmd`) to be a single overwritten delegate field, not a list, with no public read-back API — invokes the wired delegate against a fresh `HttpsConnectionAdapterOptions` to prove genuine delegation (validator accept/reject reflected verbatim in the wired `ClientCertificateValidation` outcome; the same certificate flips outcome purely because the validator's decision flips; a Scoped validator factory is invoked once per handshake, proving a fresh `IServiceScope` per call). `MtlsForwardedHeaderMiddlewareTests` (11 tests) proves the Base64-DER/URL-encoded-PEM decode paths, the deliberately-unsupported Envoy/Istio XFCC format, and the never-throws contract for absent/malformed headers. `MtlsForwardedHeaderExtensionsTests` (8 tests) proves the fail-fast-at-startup acceptance criterion directly via a real `IHost.StartAsync()` throwing `OptionsValidationException` for an unconfigured `HeaderName`. `MtlsNoOpRegressionTests` (4 tests) proves the "no behavior change for non-adopting consumers" criterion directly against the real `KestrelServerOptions`/DI container of a host built with neither extension called. **All four genuine-proof categories (delegation outcome, certificate-exposure, fail-fast validation, no-op regression) were verified during implementation to actually fail when their corresponding production wiring was temporarily removed** (the `ClientCertificateValidation` assignment, the `Connection.ClientCertificate` assignment, and the `.Validate(...).ValidateOnStart()` chain), then the production code was restored unchanged (confirmed byte-identical via `git diff`) — mirroring the T-39/T-40/T-41/T-43 discipline this domain's Test Rules mandate. Interface Contracts and Test Rules sections updated from "T-44 still pending" to "Core+Tests IMPLEMENTED"/"T-44 closed". `dotnet build -c Release` clean (0 warnings); `SharedKernel.ServiceDefaults.Tests` 130/130 passing (+30 new), 0 regressions. `SK.13.Tests` now 43/44 `●` (+1 `—` N/A: T-35) — only DO-14 (`SK.13.Docs`) remains open for WO-058 (servicedefaults-phase-implementer, WO-058, P-378)
- [2026-08-19] WO-061/P-393–P-400 (eight phases, `arch-lead`'s fintech/big-security-review pass) dispatched. All eight Designs locked and closed immediately — zero cross-domain gate, every phase extends this domain's own already-shipped surface using only BCL/already-referenced dependencies (the sole new NuGet package, `Azure.Extensions.AspNetCore.Configuration.Secrets` for P-398, needs nothing from another domain first). **P-393** corrects `TenantResolutionOptions.StrategyOrder`'s default from `[Header, Claim, Database]` to `[Claim, Header, Database]`, closing a confirmed spoofable cross-tenant-impersonation vector present since this domain's original WO-027 Core phase — an unsigned, caller-supplied `X-Tenant-Id` header could outrank a cryptographically-verified JWT tenant claim for the same request; `ClaimTenantResolutionStrategy`'s existing `null`-on-no-claim behavior proves the pre-existing B2B/API-key header-only path is unaffected. **P-394** adds an opt-in `MtlsForwardedHeaderOptions.TrustedNetworks` IP/CIDR allowlist (`System.Net.IPNetwork`, BCL since .NET 8) to `MtlsForwardedHeaderMiddleware`, mirroring `ForwardedHeadersOptions.KnownProxies`/`KnownNetworks`, closing the same class of header-spoofing trust-boundary gap `12.Security`'s own WO-060 review found and fixed for `SharedKernel.Security.Mtls` (P-386). **P-395** is this domain's first-ever production `[LoggerMessage]` logging — reserving `13000`–`13099` (`SharedKernel.ServiceDefaults`)/`13100`–`13199` (`SharedKernel.MultiTenancy`) inside `01.Core`'s platform-wide `13000`–`13999` block — for tenant-resolution outcomes, mTLS accept/reject decisions, health-check registration, and the P-394 forwarded-header trust warning; the `01.Core`-owned `LoggingEventIdRanges` registry addition is flagged for `arch-lead`, out of this planner's jurisdiction. **P-396** adds a genuine DI-aware `IValidateOptions<TenantResolutionOptions>` `.ValidateOnStart()` fail-fast chain, closing a silent-total-tenant-resolution-outage misconfiguration class. **P-397** adds opt-in `AddSharedKernelRateLimiting()` wrapping the BCL's own `Microsoft.AspNetCore.RateLimiting` — confirmed to need zero new `PackageReference` (already inside the referenced `Microsoft.AspNetCore.App` shared framework) — with a conservative global policy plus a named `RateLimitPolicyNames.Authentication` policy the consumer attaches to its own routes; never a hard `14.Presentation` reference. **P-398** adds opt-in `AddSharedKernelKeyVaultConfiguration()`, the platform's first secrets-manager `IConfiguration` provider, with a GATING Core sub-task to empirically verify (never assume) fail-fast-on-unreachable-vault behavior. **P-399** adds an optional `requireAuthorization` parameter to `MapDefaultHealthCheckEndpoints` (default unchanged) plus mandatory capitalized network-isolation documentation, closing a latent information-disclosure risk this domain's own health checks already populate `HealthCheckResult.Data` for. **P-400** adds an opt-in `ITenantStatusValidator` seam (mirrors `05.Application`'s `IAuthorizationContext` bridge pattern) so Header/Claim-resolved tenants get the same suspend/offboard protection `DatabaseTenantResolutionStrategy` already has by construction — a `false` result routes through the exact same `Guid.Empty`/`TenantNotResolved` path as "no strategy resolved," deliberately creating no new tenant-existence information-disclosure surface. Packages table, Technology Stack, Interface Contracts (five new subsections: health-check-endpoint authorization hardening, structured audit logging, opt-in rate limiting, opt-in secrets-manager configuration, startup-time configuration validation, plus the `ITenantStatusValidator`/`TrustedNetworks` extensions to existing subsections), Implementation Rules (eight new bullets), DI Registration, AOT Compatibility, and Test Rules all updated — every new capability marked "design-locked, not yet implemented," consistent with this domain's own established pre-implementation documentation style (mirrors the WO-058/P-378 D-19 precedent before it shipped). **Note for the next dispatch: P-401 (`00.Governance`), to be dispatched after this session, depends on P-393 and P-394's locked shapes staying stable** — the corrected `StrategyOrder` default and the `TrustedNetworks` allowlist design are the surfaces it will mechanically lock (servicedefaults-arch-planner, WO-061, P-393–P-400)
- [2026-08-19] S-20 (`SK.13.Scaffold`) implemented — `Azure.Extensions.AspNetCore.Configuration.Secrets` `1.5.2` + `Azure.Identity` `1.21.0` `PackageReference`s added to `SharedKernel.ServiceDefaults.csproj` for the future `AddSharedKernelKeyVaultConfiguration` (C-56). Floor versions confirmed live against the NuGet feed rather than assumed. Packages table, Interface Contracts, and AOT Compatibility corrected to distinguish "packages referenced" (S-20, shipped) from "extension method implemented" (C-56, still pending). `dotnet build`/`dotnet pack -c Release` both clean, no new `NU5104`; `SharedKernel.ServiceDefaults.Tests` 130/130 passing. `SK.13.Scaffold` now 20/20 `●`, promoted to root (servicedefaults-phase-implementer, WO-061, P-398)
- [2026-08-19] C-49–C-58 (`SK.13.Core`) implemented and tested — all ten remaining WO-061 Core tasks shipped in one session. `TenantResolutionOptions.StrategyOrder` default corrected to `[Claim, Header, Database]` (C-49). `MtlsForwardedHeaderOptions.TrustedNetworks`/`AddTrustedProxy`/`AddTrustedNetwork` implemented; `MtlsForwardedHeaderMiddleware` short-circuits before decode when the remote IP falls outside a non-empty allowlist (C-50). `MultiTenancyLog`/`ServiceDefaultsLog` (this domain's first-ever production `[LoggerMessage]` logging, EventIds `13000`–`13003`/`13100`–`13101`) wired into `TenantResolutionMiddleware`, both mTLS composition surfaces, and — via a new shared `HealthCheckRegistrationLogging` helper — all nine dependency-specific health-check registration methods (C-51/C-52/C-53). `TenantResolutionOptionsValidator : IValidateOptions<TenantResolutionOptions>` wired via `.ValidateOnStart()` (C-54) — **genuine correction from the literal design sketch, not a formality:** captures `IServiceProvider` + a fresh `IServiceScope` per `Validate()` call rather than constructor-injecting `IEnumerable<ITenantResolutionStrategy>` directly, avoiding a captive-dependency error against the Scoped strategy registrations (a Singleton validator cannot safely consume a Scoped dependency directly) — mirrors `AddMtlsClientCertificate`'s (C-48) established `IServiceScopeFactory` pattern for the identical class of problem. `AddSharedKernelRateLimiting()`/`RateLimitPolicyNames` implemented, zero new `PackageReference` (C-55). `AddSharedKernelKeyVaultConfiguration()` implemented — **GATING sub-task resolved empirically:** `IHostApplicationBuilder.Configuration` is a `ConfigurationManager`, which rebuilds its `IConfigurationRoot` eagerly/synchronously on every `.Add(...)` call, so an unreachable vault throws directly from this method's own call, confirmed via a real test against an unreachable loopback endpoint with a fake instantly-resolving `TokenCredential` (C-56). `MapDefaultHealthCheckEndpoints(requireAuthorization:)` implemented (C-57). `ITenantStatusValidator` seam implemented — `TenantResolutionMiddleware` resolves it via `context.RequestServices?.GetService<ITenantStatusValidator>()` (null-safe on `RequestServices` itself too, since a bare test `HttpContext` can have a null `RequestServices`) (C-58). Packages table, Technology Stack, all five new Interface Contracts subsections plus the `TrustedNetworks`/`StrategyOrder` extensions to existing ones, Implementation Rules (eight bullets), DI Registration (five examples), AOT Compatibility (six bullets), and Test Rules all updated from "design-locked, not yet implemented" to "IMPLEMENTED and tested, shipped 2026-08-19" — except the T-45–T-66 gating-acceptance-criteria Test Rules bullet, deliberately left open/reframed (not marked shipped) since `SK.13.Tests` did not close this session. `MultiTenancy.csproj` gained its first-ever `InternalsVisibleTo` grant (needed for `MultiTenancyLog`). `SharedKernel.ServiceDefaults.Tests` 162/162 passing (+32); `SharedKernel.MultiTenancy.Tests` 47/47 passing (+17); both production projects and `consumer-verify` build clean. `SK.13.Core` now 57/58 `●` (+1 `—` N/A: C-40), promoted to root. `SK.13.Tests` (T-45–T-66)/`SK.13.Docs` (DO-15–DO-22) remain their own separate, still-open phase keys for a future session (servicedefaults-phase-implementer, WO-061, P-393–P-400)
- [2026-08-20] T-45–T-66 (`SK.13.Tests`) implemented and closed — the full 22-task WO-061 gating checklist walked task-by-task; ten genuinely new tests added (T-52–T-56, T-58–T-61, T-63), twelve already covered by the prior `SK.13.Core` session's tests. Fixed a stale Test Rules line still documenting the pre-P-393 `[Header, Claim, Database]` default. Recorded a test-authoring gotcha (`IOptions<T>` vs. `.ValidateOnStart()`'s `IOptionsMonitor<T>` caching independently for the same options type — mixing both resolution paths in one "fires exactly once" test double-counts). `SharedKernel.ServiceDefaults.Tests` 162→171 (+9); `SharedKernel.MultiTenancy.Tests` 47→51 (+4); 0 regressions. `SK.13.Tests` now 65/66 `●` (+1 `—` N/A: T-35), promoted to root. `SK.13.Docs` (DO-15–DO-22) is the last open phase key (servicedefaults-phase-implementer, WO-061, P-393–P-400)
- [2026-08-20] WO-063/P-419 processed (`arch-lead` dispatch, servicedefaults-arch-planner): a source-verification pass found this domain's own `AddSharedKernelRateLimiting()` (P-397) and `14.Presentation`'s `RateLimitRejectionProblemDetails` (P-408) — two independently-shipped halves of one documented handoff — were never actually connected; `RateLimiting/RateLimitingExtensions.cs`'s `OnRejected` is still the BCL default, and this domain's own `README.md` recipe hand-rolls a raw `ProblemDetails` object instead of calling the real, `Published` (`1.2.0`) helper, reproducing inside this domain's own docs exactly the inline-`ProblemDetails`-construction anti-pattern the platform forbids everywhere else. New `state-map.md` tasks: D-28 (locked immediately — declines a named `13→14` layering exception, reaffirming rather than reopening D-24's "never a hard reference" rule, since `AddSharedKernelRateLimiting`'s existing `configure` parameter already exposes `OnRejected` with no Core/Scaffold change needed); T-67/T-68 (a genuine, compiled `TestServer` proof of the corrected recipe via a **test-only** cross-domain `ProjectReference` from `SharedKernel.ServiceDefaults.Tests` to `SharedKernel.Presentation.WebApi`, mirroring the T-43/WO-056 gating-proof-only precedent, plus a no-recipe regression test); DO-23 (corrected README/XML-doc recipe + `CLAUDE.md` correction, with a flagged cross-domain follow-up for `14.Presentation/CLAUDE.md`'s own P-408 status note, out of this planner's jurisdiction). `CLAUDE.md`'s "Opt-in rate limiting" Interface Contracts subsection and its corresponding Implementation Rules bullet both updated in place to describe the gap and point to D-28/T-67/T-68/DO-23 rather than restating the handoff as closed — all three tasks `○` Pending, not yet implemented, zero cross-domain gate (servicedefaults-arch-planner, WO-063, P-419)
- [2026-08-21] T-67/T-68 (`SK.13.Tests`) implemented and closed — `RateLimiting/RateLimitRejectionRecipeTests.cs` added to `SharedKernel.ServiceDefaults.Tests`, proving the D-28-corrected `AddSharedKernelRateLimiting()` `OnRejected` recipe against a real `WebApplication`/`TestServer` host: the rejected response is genuinely `RateLimitRejectionProblemDetails`-shaped (429, `application/problem+json`, `ProblemDetails.Status`/`Type`/`Extensions["traceId"]`, a parseable `Retry-After` header), plus a companion regression proving the no-recipe call shape stays the byte-identical BCL default. Added a **test-only** `ProjectReference` from `SharedKernel.ServiceDefaults.Tests.csproj` to `14.Presentation/SharedKernel.Presentation.WebApi.csproj`, inline-commented as gating-proof-only, mirroring the T-43/WO-056 precedent — never referenced by the production `SharedKernel.ServiceDefaults.csproj`; the production surface is unchanged. Interface Contracts (the WO-063/P-419 status note) and the D-28 Implementation Rules bullet both corrected from "Tests/Docs `○` Pending, not yet implemented" to "Tests `●` shipped 2026-08-21; Docs `○` Pending"; a new Test Rules bullet documents the recipe-proof pattern. `SharedKernel.ServiceDefaults.Tests`: 173/173 passing, 0 regressions; `SharedKernel.MultiTenancy.Tests`: 51/51 passing, unaffected. `SK.13.Tests` now 67/68 `●` (+1 `—` N/A: T-35), promoted to root. `SK.13.Docs` (DO-23) is the domain's one remaining open phase key (servicedefaults-phase-implementer, WO-063, P-419)
- [2026-08-21] WO-064/P-430 processed (`arch-lead` dispatch, servicedefaults-arch-planner): `WithIntegrationTelemetry` added as the ninth `With*Telemetry` sibling, dispatched once `15.Integration`'s own concurrent gold-standard hardening review (WO-064) design-locked its first `ActivitySource` (`WebhookIntegrationActivitySource`, `"SharedKernel.Integration"`, P-424). Design D-29 locked immediately against `15.Integration/CLAUDE.md`'s own design-locked "Distributed tracing" prose, mirroring the D-08/D-09/D-10/D-12/D-14/D-15/D-16/D-17 documented-ahead-of-implementation precedent this domain has followed for every prior cross-domain `With*Telemetry`/`Add*ReadinessCheck` sibling. `WithIntegrationTelemetry(this IHostApplicationBuilder)` wires `"SharedKernel.Integration"` into the host `TracerProvider` ONLY — deliberately tracing-only, mirroring `WithPersistenceTelemetry`'s exact shape and rationale (`15.Integration`'s WO-064/P-424 design ships an `ActivitySource` but no companion `Meter`) — via bare string-name `AddSource(string)` wiring requiring **zero `ProjectReference`** to `15.Integration` regardless of the source's declared accessibility, the same mechanism `WithMessagingTelemetry`/`WithApplicationTelemetry`/`WithPersistenceTelemetry` already rely on. **Genuinely blocked at Core/Tests/Docs, re-verified directly against `15.Integration/state-map.md` (not assumed from the phase input's "Depends on: P-424" line):** `SK.15.WO064`'s H-15 (Design)/H-16 (Core) are both `○` Not Started — the `ActivitySource` does not yet exist in compiled `SharedKernel.Integration.Webhooks` source. State-map tasks added: D-29 (`SK.13.Design`, locked/closed now), C-59/T-69/DO-24 (Core/Tests/Docs, all `⚑` Blocked, not ordinary `○` Not Started). No Scaffold task needed. No new Published task (mirrors the `WithPersistenceTelemetry`/`WithCommunicationTelemetry` precedent — additive extension method inside the already-published `SharedKernel.ServiceDefaults` package). `CLAUDE.md` updated: Packages table row, new Interface Contracts entry (mirroring `WithPersistenceTelemetry`'s block shape), Implementation Rules (idempotency-family bullet, ActivitySource-ownership-ledger bullet, and a new sibling bullet), DI Registration snippet (commented-out call, design-locked), AOT Compatibility bullet, and Test Rules bullet — all marked "DESIGN-LOCKED, not yet implemented." Package Board `SharedKernel.ServiceDefaults` reopened `●` (Published/Docs) → `◐` (Core) with a new dispatch note; Blocked section and Cross-Domain Dependencies updated with the genuine `15.Integration` gate (servicedefaults-arch-planner, WO-064, P-430)
- [2026-08-21] C-59/T-69/DO-24 (`SK.13.Core`/`SK.13.Tests`/`SK.13.Docs`) implemented and tested, closing WO-064/P-430 end to end for this domain — the recorded blocker was re-verified directly against `15.Integration/state-map.md` (not taken on trust) and found stale: `SK.15.WO064`'s H-15 (Design)/H-16 (Core) are both `●`, and `SharedKernel.Integration.Webhooks/Dispatch/WebhookIntegrationActivitySource.cs` exists on disk with `public const string Name = "SharedKernel.Integration";` — byte-identical to D-29's already-locked contract, zero deviation. New `Telemetry/IntegrationTelemetryExtensions.cs` wires `WithTracing(t => t.AddSource(IntegrationInstrumentationName))` only — no `WithMetrics(...)` call, mirroring `WithPersistenceTelemetry`'s exact tracing-only shape (D-16/D-29). New `IntegrationTelemetryExtensionsTests.cs` (3 tests): tracing-idempotency (`TracerProviderBuilder` registration count `<= 1`), a no-throw test, and a genuine `BaseProcessor<Activity>` + `provider.GetRequiredService<TracerProvider>()` span-capture test (`WithIntegrationTelemetry_SpanFromIntegrationActivitySource_IsCaptured`) mirroring `WithPersistenceTelemetry`'s T-40 precedent exactly — verified during implementation to fail (empty captured list) when the `AddSource` call was temporarily removed, then the production code was restored unchanged. No metrics-idempotency counterpart test, mirroring T-40's identical omission. Interface Contracts, both Implementation Rules bullets (idempotency-family list, sibling-shape bullet), the AOT Compatibility bullet, and the Test Rules bullet all updated from "DESIGN-LOCKED, blocked"/"not yet implemented" to "IMPLEMENTED and tested". `README.md`'s `Program.cs` snippet and ordering rule 4 updated with `builder.WithIntegrationTelemetry();` (uncommented, no longer design-locked) and the corrected nine-method idempotency/shape prose. `dotnet build -c Release` clean (0 warnings); `SharedKernel.ServiceDefaults.Tests` 176/176 passing (+3), 0 regressions. `SK.13.Core`/`SK.13.Tests`/`SK.13.Docs` all promoted `◐` → `●` again — all six `SK.13.*` phase keys fully `●`/`—`, domain closed out end to end. `SK.13.Scaffold`/`SK.13.Published` unaffected (no new `ProjectReference`, no new NuGet package). Root `state-map.md`'s P-430 Phase Backlog entry promoted to `●` Complete (servicedefaults-phase-implementer, WO-064, P-430)
- [2026-08-26] WO-075/P-471/P-472 processed (`arch-lead` dispatch, servicedefaults-arch-planner): `SharedKernel.MultiTenancy` gains a new `Catalog/` sub-surface — `TenantDescriptor`/`TenantStatus`/`TenantIsolationMode`/`ITenantCatalog` (read-only tenant metadata lookup, provisioning explicitly out of scope) giving the pre-existing `ITenantStatusValidator` seam (P-400/WO-061) its first real default implementation, `CatalogTenantStatusValidator` (fails closed on a catalog miss); `DatabaseTenantCatalog` (reuses `DatabaseTenantResolutionStrategy`'s exact data-access pattern) and `CachedTenantCatalog` (short-bounded-TTL decorator, `InvalidateTenantAsync` bypassing the TTL immediately, opt-in `.WithCrossInstanceInvalidation(ICacheInvalidationBus)` — a direct, compiled reference to `02.Caching.Abstractions`, never a bridged local seam, since this domain's layering ceiling already legally covers `02.Caching`). Reconciles, never replaces, `ITenantProvider`/`ICurrentTenantService`/`ITenantCacheService` — all three confirmed unchanged. D-30/D-31 locked immediately — **zero cross-domain gate**, `ICacheInvalidationBus` already `Available`/shipped (P-012); `state-map.md` gains S-21/C-60–C-64/T-70–T-75/DO-25/DO-26, all ordinary `○` Not Started, never `⚑` Blocked. `CLAUDE.md` updated: Packages table row, new "Tenant catalog" Interface Contracts subsection, two new Implementation Rules bullets, a new DI Registration example, an AOT Compatibility bullet, and a Test Rules bullet — all marked "DESIGN-LOCKED, not yet implemented." Package Board `SharedKernel.MultiTenancy` reopened `●` → `◐` with a new dispatch note; new Cross-Domain Dependencies row added (`02.Caching`, status Available) (servicedefaults-arch-planner, WO-075, P-471, P-472)
- [2026-08-26] WO-068/P-449 processed (`arch-lead` dispatch, servicedefaults-arch-planner): Azure Key Vault key-provider composition-root registration + readiness probe. `AddSharedKernelKeyVaultKeyProvider()` (D-32, locked) — distinctly named from `AddSharedKernelKeyVaultConfiguration()` (P-398) to avoid confusion (one wires a Key Vault `IConfiguration` source, the other registers an `IEncryptionKeyProvider`/`IEnvelopeEncryptionProvider`) — locked against `01.Core`'s already-fully-ratified `SharedKernel.Cryptography.KeyVault.Azure` design (P-446/P-447), itself zero code on disk. `AddKeyVaultKeyProviderReadinessCheck` (D-33) **cannot be locked — a genuine upstream design gap, not implementation lag:** `01.Core/CLAUDE.md`'s ratified Interface Contracts for that package declare no `ProbeAsync`-shaped member and no companion probe-primitive type at all, mirroring the WO-045/D-11 finding exactly, but NOT retracted (unlike D-11) — an `IEncryptionKeyProvider` failing silently is at least as operationally dangerous as a DB/cache failing, so this genuinely needs a probe once `01.Core`/`core-arch-planner`/`arch-lead` adds the missing member. Calibration decision locked for when it unblocks: `Unhealthy`, never `Degraded` (no fail-safe-absorption layer sits in front of raw KMS connectivity). **Genuinely blocked at Scaffold/Core/Tests/Docs, a stronger blocker than most precedents:** `SharedKernel.Cryptography.KeyVault.Azure` does not exist on disk at all — no `.csproj`, no `.slnx` registration (verified directly) — mirrors the WO-058/S-19 mTLS finding exactly. `state-map.md` gains S-22/C-65/C-66/T-76/T-77/DO-27/DO-28, all `⚑` Blocked; new Cross-Domain Dependencies row added (`01.Core`, status Blocked, two distinct gaps documented) and a new Blocked-section entry. `CLAUDE.md` updated: Packages table row, new "Opt-in Azure Key Vault key-provider registration" Interface Contracts subsection (with the design-gap explicitly called out inside the Health check composition subsection too), two new Implementation Rules bullets, a new DI Registration example, an AOT Compatibility bullet, and a Test Rules bullet — all marked "DESIGN-LOCKED, blocked" (servicedefaults-arch-planner, WO-068, P-449)
- [2026-08-26] WO-073/P-465/P-466 processed (`arch-lead` dispatch, servicedefaults-arch-planner): `WithSchedulingTelemetry` (D-34, the tenth `With*Telemetry` sibling — **unlike the two tracing-only siblings, wires BOTH tracing and metrics**, since `19.Scheduling` ships an `ActivitySource`+`Meter` pair) and `AddSchedulerReadinessCheck` (D-35 — wraps `ISchedulerServiceProbe`/`SchedulerServiceHealth`, no caller-supplied identifier, mirrors `AddWorkflowReadinessCheck`; `IsRunning`/`RegisteredJobCount` field names provisional pending compiled-source confirmation) both locked against `19.Scheduling/CLAUDE.md`'s own ratified prose, mirroring the documented-ahead-of-implementation precedent this domain has followed for every prior cross-domain sibling. **The narrow `13→19` layering grant D-35 needs is already ratified at root** (confirmed in root `CLAUDE.md`'s Hard rules and `19.Scheduling/CLAUDE.md`'s own Layering section) — a new, independently-earned grant, never a widening of the `17.Workflows`/WO-047 grant, honoring root `CLAUDE.md`'s explicit "never by analogy" instruction. **Genuinely blocked at Scaffold/Core/Tests/Docs, a stronger blocker than most precedents:** `SharedKernel.Scheduling` does not exist on disk at all — no `.csproj`, no `.slnx` registration (verified directly against `19.Scheduling/state-map.md`'s Overall Progress, Core 0/9) — mirrors the WO-058/S-19 mTLS finding exactly. `state-map.md` gains S-23/C-67/C-68/T-78/T-79/DO-29/DO-30, all `⚑` Blocked; two new Cross-Domain Dependencies rows added (`19.Scheduling`) plus a third flagging a `00.Governance` architecture-test extension as needed-but-outside-this-domain's-jurisdiction (mirroring `19.Scheduling`'s own Quartz-pin-flagging precedent), and a new Blocked-section entry. `CLAUDE.md` updated: Packages table row, new `WithSchedulingTelemetry`/`AddSchedulerReadinessCheck` Interface Contracts entries, two new Implementation Rules bullets, DI Registration snippet line + example, two AOT Compatibility bullets, and a Test Rules bullet — all marked "DESIGN-LOCKED, blocked" (servicedefaults-arch-planner, WO-073, P-465, P-466)
- [2026-08-26] WO-078/P-483 processed (`arch-lead` dispatch, servicedefaults-arch-planner): `AddSharedKernelLocalization` added — `LocalizationResolutionOptions`/`LocalizationResolutionStrategy` (`UserPreference`/`TenantDefault`/`AcceptLanguageHeader`, default order exactly this), wrapping (never reimplementing) `RequestLocalizationMiddleware`. D-36 locked immediately: precedence is deliberately signed-signal-before-unsigned-header, XML docs cite WO-061/P-393's `[Claim, Header, Database]` correction explicitly so this does not repeat that mistake. **Re-examined and found NOT cross-domain-blocked despite root state-map's "Depends on: P-482, P-471" framing:** P-471 is intra-domain/same-session (this very dispatch); `01.Core`'s `ILocalizationCatalog` (P-482) is confirmed NOT a functional/compile-time dependency of this composition-root middleware at all — it is consumed by `14.Presentation`'s P-484, not here, a genuine finding recorded in Cross-Domain Dependencies rather than assumed. **First-ever intra-domain compiled `ProjectReference` from `SharedKernel.ServiceDefaults` to `SharedKernel.MultiTenancy`** (previously fully independent sibling packages, composed only side-by-side at a consumer's own `Program.cs`) — needed to resolve `ITenantCatalog`'s type for the optional `TenantDefault` step, which itself degrades cleanly (never throws) when `ITenantCatalog` was never registered. `state-map.md` gains S-24/C-69/T-80/T-81/DO-31, all ordinary `○` Not Started (S-24/C-69 carry an intra-domain sequencing note: must land in the same session as or after P-471's `ITenantCatalog`, never before). `CLAUDE.md` updated: Packages table row, new "Opt-in culture resolution" Interface Contracts subsection, a new DI Registration snippet line + example, an Implementation Rules bullet, an AOT Compatibility bullet, and a Test Rules bullet — all marked "DESIGN-LOCKED, not yet implemented" (servicedefaults-arch-planner, WO-078, P-483)
- [2026-09-04] P-471/P-472/P-449/P-483 implemented and tested — four root-backlog phases shipped in dependency order (P-471 → P-472 → P-449/P-483, per the dispatching prompt's explicit ordering), zero deviation from D-30/D-31/D-32/D-36's locked contracts. **P-471** (`SharedKernel.MultiTenancy/Catalog/`): `TenantStatus`/`TenantIsolationMode`/`TenantDescriptor`/`ITenantCatalog`/`CatalogTenantStatusValidator` — C-60/C-61/T-70/T-71/DO-25 all `●`. **P-472** (`Catalog/`): `DatabaseTenantCatalog` (reuses `DatabaseTenantResolutionStrategy`'s exact `IDbConnectionFactory`/parameterized-query pattern, single-row `IDataReader` read, `Settings` always empty by design) and `CachedTenantCatalog` (`ConcurrentDictionary`-backed, 30s default TTL via an injectable `TimeProvider`, `InvalidateTenantAsync` bypasses the TTL, `.WithCrossInstanceInvalidation(ICacheInvalidationBus)`) — S-21/C-62/C-63/C-64/T-72/T-73/T-74/T-75/DO-26 all `●`. **GENUINE FINDING:** `ICacheInvalidationBus` (`02.Caching.Abstractions`) is publish-only — confirmed by reading its shipped source directly, it has no subscribe/receive surface at all. Resolved by adding `CachedTenantCatalog.HandleCrossInstanceInvalidationSignal(Guid)` — a consumer wires their own `IRedisChannelService.SubscribeAsync` at their own composition root and calls this on receipt, never re-publishing. Documented in the type's own XML docs, this file's Interface Contracts/Implementation Rules, and `SharedKernel.MultiTenancy/README.md`'s new "Tenant catalog" section. **P-449** (`SharedKernel.ServiceDefaults/Cryptography/`): `AddSharedKernelKeyVaultKeyProvider` — a thin, idempotency-guarded (`services.Any(d => d.ServiceType == typeof(AzureKeyVaultEncryptionKeyProvider))`) call-through to `01.Core`'s `AddSharedKernelAzureKeyVaultCryptography` — S-22/C-65/T-76/DO-27 all `●`. **CORRECTED STALE BLOCKER:** `01.Core`'s `SharedKernel.Cryptography.KeyVault.Azure` was recorded here and in `state-map.md` as nonexistent on disk; re-verified directly and found fully shipped. Only the readiness-probe half (C-66/T-77/DO-28/D-33) remains genuinely blocked — `01.Core`'s own ratified design still declares no `ProbeAsync`-shaped member — deliberately NOT implemented this session, per explicit instruction and this domain's own "never invent another domain's interface member" rule. **P-483** (`SharedKernel.ServiceDefaults/Localization/`): `LocalizationResolutionStrategy`/`LocalizationResolutionOptions`/`AddSharedKernelLocalization`, plus two new `internal` `IRequestCultureProvider`s (`UserPreferenceRequestCultureProvider`, `TenantDefaultRequestCultureProvider`) composed alongside the real BCL `AcceptLanguageHeaderRequestCultureProvider`, one per configured `StrategyOrder` entry — S-24/C-69/T-80/T-81/DO-31 all `●`. New `ServiceDefaultsLog.LocalizationNoDynamicStrategyCanResolve`, EventId `13004`, via the established `PostConfigure<ILoggerFactory>` + `.ValidateOnStart()` pattern. **GENUINE NAMESPACE GOTCHA:** `RequestLocalizationOptions` lives in `Microsoft.AspNetCore.Builder`, not `Microsoft.AspNetCore.Localization` — required an explicit `using`. First-ever intra-domain `ProjectReference`, `SharedKernel.ServiceDefaults` → `SharedKernel.MultiTenancy`, one-directional. Packages table, all four affected Interface Contracts subsections, DI Registration, Implementation Rules, AOT Compatibility, and Test Rules sections all updated from "DESIGN-LOCKED"/"not yet implemented" to "IMPLEMENTED and tested" (Key Vault: registration half only). `README.md` updated for both packages (new "Tenant catalog" section in `SharedKernel.MultiTenancy`; new Key Vault two-methods and culture-resolution sections in `SharedKernel.ServiceDefaults`). `SharedKernel.MultiTenancy.Tests`: 73/73 passing (+22); `SharedKernel.ServiceDefaults.Tests`: 190/190 passing (+17); 0 regressions in either suite. `state-map.md`'s Blocked table split (the old composite Key Vault row replaced by a narrower C-66/T-77/DO-28-only row) and Overall Progress counts corrected (`SK.13.Scaffold` 23/24 `●`; `SK.13.Core` 65/69 `●`+1 N/A; `SK.13.Tests` 77/81 `●`+1 N/A; `SK.13.Docs` 28/31 `●`) — no `SK.13.*` phase key reached 100% this session (Key Vault readiness-probe and the entire `19.Scheduling`-blocked family remain `⚑`), so no root `state-map.md` promotion was made (servicedefaults-phase-implementer, WO-075/WO-068/WO-078, P-471/P-472/P-449/P-483)
- [2026-09-04] P-465/P-466 implemented and tested — `19.Scheduling` shipped `SharedKernel.Scheduling` (P-464) since the last session; re-verified directly against disk (not taken on trust) before implementing: `.csproj` exists/builds, `Probes/ISchedulerServiceProbe.cs`/`Probes/SchedulerServiceHealth.cs`/`Diagnostics/SchedulingTelemetry.cs` all confirmed byte-identical to this file's own already-locked D-34/D-35 contracts, with one genuine correction: `ISchedulerServiceProbe.ProbeAsync` returns `Task<SchedulerServiceHealth>` directly, never a `Task<Result<SchedulerServiceHealth>>` wrapper (unlike `IWorkflowServiceProbe`) — the original design sketch's `Result.IsSuccess &&` framing was corrected, not silently dropped. **`WithSchedulingTelemetry`** (`Telemetry/SchedulingTelemetryExtensions.cs`) wires both `AddSource("SharedKernel.Scheduling")` and `AddMeter("SharedKernel.Scheduling")` — string-name-only, zero `ProjectReference` needed for this method specifically. **`AddSchedulerReadinessCheck`** (`HealthChecks/SchedulerReadinessHealthCheck.cs` + `SchedulerReadinessHealthCheckExtensions.cs` + `HealthCheckNames.Scheduler`/`HealthCheckTags.Scheduler`) resolves only `ISchedulerServiceProbe`, mapping `Healthy` iff `health.IsRunning`, never `Degraded`; `RegisteredJobCount`/`LastTickUtc` are informational-only `HealthCheckResult.Data`, never factored into the decision — mirrors `TaskQueueBacklog`/`PendingWriteCount` exactly. New, independently-earned `13→19` `ProjectReference` added to `SharedKernel.ServiceDefaults.csproj` (S-23), inline-commented to explicitly disclaim any relation to the `17.Workflows`/WO-047 grant, per root `CLAUDE.md`'s "never by analogy" instruction. New tests: `SchedulingTelemetryExtensionsTests.cs` (5: tracing-idempotency, metrics-idempotency, no-throw, a genuine `BaseProcessor<Activity>` span-capture proof, a genuine `OpenTelemetry.Exporter.InMemory` metric-capture proof — both capture tests verified during implementation to fail when their production wiring was temporarily removed, then restored unchanged) and `SchedulerReadinessHealthCheckTests.cs` (4: Healthy/Unhealthy-never-Degraded mapping, a dedicated `RegisteredJobCount`-never-drives-Unhealthy regression, a `LastTickUtc`-null-is-informational-only test) plus one case each added to the existing `HealthCheckNamesTests`/`HealthCheckTagTests` enumeration files, mirroring every prior dependency-specific check's coverage shape. Packages table, both affected Interface Contracts entries, DI Registration snippet + example, both Implementation Rules bullets, both AOT Compatibility bullets, and the Test Rules bullet all updated from "DESIGN-LOCKED, blocked" to "IMPLEMENTED and tested". `README.md`'s `Program.cs`/readiness-check snippet updated. `SharedKernel.ServiceDefaults.Tests`: 190 → 201 passing (+11), 0 regressions; `SharedKernel.MultiTenancy.Tests` unaffected (51→73 from the prior session, untouched here). `state-map.md`: C-67/T-78/DO-29 (P-465) and S-23/C-68/T-79/DO-30 (P-466) all `●`; the two now-resolved Blocked-table rows removed outright, leaving only the narrower C-66/T-77/DO-28/D-33 Key-Vault-probe row from the prior session. **Not yet done, flagged as an open cross-domain follow-up:** a `00.Governance` architecture-test assertion mirroring the existing `17.Workflows`-grant-scope test for this new, independently-earned `13→19` grant — `00.Governance` is a separate domain with its own implementer; noted here, not attempted (servicedefaults-phase-implementer, WO-073, P-465, P-466)
- [2026-09-04] P-449 (WO-068) closed end to end — **this closes out the LAST blocked item anywhere in this domain.** `01.Core` shipped P-487 — `IEncryptionKeyProviderProbe`/`EncryptionKeyProviderHealth` (`SharedKernel.Cryptography/Symmetric/`) — since the pass above. Re-verified directly against disk before implementing: `EncryptionKeyProviderHealth` confirmed `sealed record EncryptionKeyProviderHealth(bool IsHealthy, string? Description)` — no `Result<T>` wrapper, the same no-wrapper shape as `ISchedulerServiceProbe`/`SchedulerServiceHealth` and `07.Messaging`'s `IMessageBusProbe`/`MessageBusHealth`. `AzureKeyVaultEncryptionKeyProvider` now also implements `IEncryptionKeyProviderProbe`; `AddSharedKernelAzureKeyVaultCryptography` already registers it as a third resolvable singleton, so the already-shipped `AddSharedKernelKeyVaultKeyProvider` (C-65) needed zero changes. New `HealthChecks/KeyVaultKeyProviderReadinessHealthCheck.cs` + `KeyVaultKeyProviderReadinessHealthCheckExtensions.cs` + `HealthCheckNames.EncryptionKeyProvider`/`HealthCheckTags.EncryptionKeyProvider` — `Healthy` iff `health.IsHealthy`, `Unhealthy` otherwise, never `Degraded`. **No new layering grant needed** — `01.Core` is already inside this domain's `01`–`12` range, reached via the `ProjectReference` already added for C-65/S-22, unlike the `17.Workflows`/`19.Scheduling` cases — worth remembering that a new named `13→NN` grant is only needed when the owning domain is numbered above 13 with no lower-numbered `.Abstractions` companion. C-66/T-77/DO-28 all `●`; D-33 corrected from "cannot be locked, design gap" to the actual shipped, no-wrapper contract and marked `●`. New `KeyVaultKeyProviderReadinessHealthCheckTests.cs` (4: Healthy/Unhealthy-never-Degraded, `Description` surfaced, null-`Description`-does-not-throw), plus one case each added to `HealthCheckNamesTests`/`HealthCheckTagTests`. `README.md` gains a readiness-probe table row and a worked example in the Key Vault section. `SharedKernel.ServiceDefaults.Tests`: 201 → 207 passing (+6), 0 regressions; `SharedKernel.MultiTenancy.Tests` unaffected (73/73); sanity-checked `00.Governance/SharedKernel.ArchitectureTests.Tests`: 253/253, unchanged from baseline (confirms no layering violation introduced). **`state-map.md`'s `## Blocked` table is now fully empty and every `SK.13.*` phase key (Design/Scaffold/Core/Tests/Docs/Published) is `●`/`—`** — verified directly by scanning every row, not assumed. `state-map-phase` invoked for `SK.13.Design`/`SK.13.Core`/`SK.13.Tests`/`SK.13.Docs` (sub-map side only, per the shared-file protocol — root `state-map.md` propagation intentionally left to the coordinator, as for `SK.13.Scaffold` two passes prior). Open cross-domain follow-up, unchanged, not attempted: the `00.Governance` architecture-test extension for the `19.Scheduling` `13→19` grant (P-449 needed no such test itself — no new grant was created this pass) (servicedefaults-phase-implementer, WO-068, P-449)
- [2026-09-08] WO-081/P-503 processed (`arch-lead` dispatch, servicedefaults-arch-planner, part of a coordinated cross-domain breaking wave — `01.Core`'s AAD/sync-gate/Key-Vault-hardening phases and `06.Persistence`'s P-498 SEVERE-defect correction both design-locked earlier the same session). **The phase input's own acceptance criterion #3 was refuted, not implemented as written (D-37):** it claimed the fix should make a service "satisfy `06.Persistence`'s P-498 startup fail-fast check without extra manual wiring." Checked directly against `06.Persistence`'s own design-locked D-131 (same wave) and found false — that fix deliberately replaces an ambient-registration-order collision (this method and `.WithEncryption()` both wiring the SAME unkeyed `IEncryptionKeyProvider`) with an EXPLICIT, consumer-driven opt-in (`.WithExternalEncryptionKeyProvider<TProvider>()`, called by the consuming service on its OWN `EfCorePersistenceBuilder`) specifically to eliminate that collision; making the connection automatic here would recreate the exact hazard `06.Persistence` just fixed. **The real, confirmed gap (AC#1/#2) is closed (D-38):** direct source read of `Cryptography/KeyVaultKeyProviderExtensions.cs` and `01.Core`'s `AzureKeyVaultCryptographyServiceCollectionExtensions.cs` confirms `AddSharedKernelKeyVaultKeyProvider` wires `AzureKeyVaultEncryptionKeyProvider` raw and UNCACHED as `IEncryptionKeyProvider`/`IEnvelopeEncryptionProvider`/`IEncryptionKeyProviderProbe` — `01.Core`'s own doc says "wrap explicitly if desired," and this composition-root method is the deliberate place to make that call. New optional `cacheTtl` parameter wraps ONLY `IEncryptionKeyProvider` in `01.Core`'s `CachedEncryptionKeyProvider` (P-446) — internal 5-minute default, `TimeSpan.Zero` disables, negative throws `ArgumentOutOfRangeException`; `IEnvelopeEncryptionProvider`/`IEncryptionKeyProviderProbe` deliberately stay wired to the raw provider (neither is implemented by `CachedEncryptionKeyProvider`, and a probe must never cache its own reachability signal); `CachedEncryptionKeyProvider` is also registered as its own concrete singleton so `06.Persistence`'s explicit opt-in can target either the raw or cached variant by its own choice — confirmed compatible with, never fighting, `06.Persistence`'s own `PreWarmedEncryptionKeyProvider` design note that a cache-wrapped `TProvider` is explicitly tolerated ("only its async members are ever called"); confirmed this can never unlock any synchronous path, since `CachedEncryptionKeyProvider` never implements `01.Core`'s `ISynchronousEncryptionKeyProvider` marker regardless of cache warmth. **New cross-domain hazard surfaced and documented, not fixed — out of this domain's jurisdiction (D-39):** this wave's `07.Messaging` calibration finding (its payload-encryption serializer is hard-synchronous, no async overload) means a service enabling both this method and `07.Messaging` payload encryption against the same ambient `IEncryptionKeyProvider` will break unconditionally (`NotSupportedException`, forever) once `01.Core`'s `SK.01.P492` ships — documented IN CAPITALS at the one composition-root location a developer would compose both, with no code fix attempted (that's `07.Messaging`'s own P-499 territory). Zero cross-domain gate for implementation — no new `PackageReference`/`ProjectReference` needed, `CachedEncryptionKeyProvider` already transitively reachable via the existing reference to `SharedKernel.Cryptography.KeyVault.Azure`. `state-map.md` gains D-37/D-38/D-39 (all `●`, locked in this planning pass) and C-70/T-82/DO-32 (all ordinary `○` Not Started, never `⚑` Blocked), plus three new Cross-Domain Dependencies rows (`01.Core` — `CachedEncryptionKeyProvider` availability; `06.Persistence` — informational, the D-131 coordination; `07.Messaging` — informational, the hazard source). `CLAUDE.md` updated: the "Opt-in Azure Key Vault key-provider registration" Interface Contracts subsection gains the full caching-wrap design plus the AC#3-refutation standing note and the cross-domain hazard warning (both marked DESIGN-LOCKED, implementation pending); the `AddKeyVaultKeyProviderReadinessCheck` entry gains an "unaffected by caching" cross-reference; the AOT Compatibility and Test Rules bullets each gain a forward-looking note. **Not a breaking API change** (the new parameter is optional/additive) but IS a default behavior change for existing callers (their ambient `IEncryptionKeyProvider` becomes cache-wrapped by default where it was previously raw) — flagged for release notes at the next `devops-lead` publish pass, mirroring the WO-054/P-351 precedent of documenting a behavior note without performing the repack in a planning-only session (servicedefaults-arch-planner, WO-081, P-503)
- [2026-09-08] C-70/T-82/DO-32 implemented and tested (WO-081/P-503) — the `cacheTtl` caching-wrap addition shipped exactly per the D-37/D-38/D-39 design already locked above, zero deviation. `Cryptography/KeyVaultKeyProviderExtensions.cs`: `AddSharedKernelKeyVaultKeyProvider` gained the optional `TimeSpan? cacheTtl = null` parameter; a negative value is validated and throws `ArgumentOutOfRangeException` BEFORE the pre-existing idempotency guard/call-through runs — proven by a dedicated test asserting zero `AzureKeyVaultEncryptionKeyProvider` registrations after the throw. Under the default (or any non-`TimeSpan.Zero`) TTL, `CachedEncryptionKeyProvider` (`01.Core`, P-446) is registered as its own concrete singleton wrapping `AzureKeyVaultEncryptionKeyProvider`, and `IEncryptionKeyProvider` is RE-registered (a second, additive registration — the BCL container resolves the LAST one) to redirect to it; `IEnvelopeEncryptionProvider`/`IEncryptionKeyProviderProbe` are untouched. **Confirmed and recorded as a genuine registration-count consequence, not a bug:** `IEncryptionKeyProvider`'s registration count under the default call is now 2 (raw + cached-redirect), not 1 — the two pre-existing `KeyVaultKeyProviderExtensionsTests` asserting count-of-1 (from the WO-068/P-449 session) were corrected in place to assert 2 and to document why, rather than silently left describing stale behavior. Idempotency is unaffected — the method's existing top-level guard (checking whether `AzureKeyVaultEncryptionKeyProvider` is already registered) short-circuits the entire method including the new caching block, so a second call never double-registers `CachedEncryptionKeyProvider` and never re-applies a different `cacheTtl` argument — this was already true by construction, verified directly rather than assumed. `SharedKernel.ServiceDefaults.Tests/Cryptography/KeyVaultKeyProviderExtensionsTests.cs` gained 7 new tests (T-82, 4 → 11 tests in this file): resolution-level proofs (via `IServiceCollection.BuildServiceProvider()` against a syntactically-valid, never-network-reached `AzureKeyVaultCryptographyOptions` configuration — `AzureKeyVaultEncryptionKeyProvider`'s constructor eagerly reads `IOptions<T>.Value` but performs no I/O of its own) that the default call resolves `IEncryptionKeyProvider` as `CachedEncryptionKeyProvider`; that `IEnvelopeEncryptionProvider`/`IEncryptionKeyProviderProbe` stay REFERENCE-EQUAL to the raw `AzureKeyVaultEncryptionKeyProvider` singleton (the load-bearing proof D-38 named explicitly); `TimeSpan.Zero` disables wrapping (`IEncryptionKeyProvider` reference-equal to raw, zero `CachedEncryptionKeyProvider` registrations); a negative `cacheTtl` throws before any registration; `CachedEncryptionKeyProvider` is independently resolvable as its own concrete type; and a second call does not double-register it. No new `PackageReference`/`ProjectReference` was needed, confirming D-37/D-38's own "zero cross-domain gate" framing empirically, not just by assertion. `README.md` gained a full "cacheTtl" subsection (default/custom/opt-out examples, the never-unlocks-synchronous-path note, the `.WithExternalEncryptionKeyProvider<T>()` recipe, the AC#3-refutation standing note, and the `07.Messaging` cross-domain hazard warning, all mirroring the XML docs verbatim). `CLAUDE.md`'s "Opt-in Azure Key Vault key-provider registration" Interface Contracts heading and its "CACHING GAP" paragraph, the AOT Compatibility bullet, and the Test Rules bullet all corrected from "DESIGN-LOCKED, implementation pending" to "IMPLEMENTED and tested, shipped 2026-09-08"; the cross-domain hazard paragraph's "the moment `01.Core`'s `SK.01.P492` ships" wording corrected to reflect that `01.Core`'s sync-gate has, in fact, already shipped (verified directly against `01.Core/CLAUDE.md` before writing this entry — WO-081's P-491/P-492/P-493 are all recorded there as SHIPPED). Full solution build (`Platform.SharedKernel.slnx`, 0 errors, only pre-existing unrelated warnings) and `SharedKernel.ServiceDefaults.Tests` (214/214 passing, 0 regressions) both re-verified via real `dotnet build`/`dotnet test` — no substitute harness was needed this session, unlike the two prior `01.Core` sessions this dispatch's brief warned about. `SharedKernel.MultiTenancy` was not touched. **Not a breaking API change** (the new `cacheTtl` parameter is optional/additive) but IS a default behavior change for existing callers of `AddSharedKernelKeyVaultKeyProvider()` — their ambient `IEncryptionKeyProvider` becomes cache-wrapped by default where it was previously raw — flagged for release notes at the next `devops-lead` publish pass, per D-37/D-38's own framing; no repack was performed in this session. Root-owned files (`state-map.md`, root `CLAUDE.md`, `Directory.Packages.props`, `Platform.SharedKernel.slnx`) were not touched, per this wave's explicit protocol; no missing CPM pin or `ProjectReference` was encountered (servicedefaults-phase-implementer, WO-081, P-503)
- [2026-09-14] WO-084: split into dependency-free base + 13 integration packages; 6 rules added (agent)
