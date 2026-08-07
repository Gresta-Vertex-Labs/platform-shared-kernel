# 13.ServiceDefaults — Host Composition & Cross-Cutting Brain

## What This Domain Is

The host composition layer. This is the only capability domain in the platform whose packages are wired directly into a microservice's `Program.cs` as the **first lines of startup** — OpenTelemetry, health checks, startup/liveness/readiness probes, and concrete multi-tenant resolution strategies. Everything here is composition glue: it assembles abstractions and concrete providers from layers `01`–`12` into ready-to-call extension methods. No business logic, no domain types, no new abstractions are defined here — only wiring.

Philosophy: **Composition-only. Opt-in by default. Liveness ≠ Readiness. No business logic.**

> **Layering exception:** `13.ServiceDefaults` is the **only** domain in the platform permitted to reference concrete infrastructure provider packages directly (`SharedKernel.Caching.Redis*`, `SharedKernel.Messaging.MassTransit`, `SharedKernel.Persistence.EfCore`/`.PostgreSQL`/`.Dapper`, `SharedKernel.Security.Oidc`) in addition to their abstractions. Every other domain (`05.Application`, `03.Domain`, etc.) must depend only on `.Abstractions` packages. This exception exists because `13.ServiceDefaults` *is* the composition root — health checks and telemetry wiring are inherently provider-specific (a Redis health check needs to know about Redis). This is mechanically enforced by `00.Governance`'s `SharedKernelLayeringRules` (Rule 1: no production assembly other than the concrete provider packages and `13.ServiceDefaults` may reference a concrete `02.Caching` provider — the same pattern applies to `07.Messaging` transports).

---

## Packages

| Package | Role | References |
|---------|------|-----------|
| `SharedKernel.ServiceDefaults` | `AddServiceDefaults()` composition entry point; OpenTelemetry (tracing/metrics/logging) wiring, including OTLP log export with scopes + formatted message and generic `Activity.Baggage`→`LogRecord.Attributes` enrichment (`BaggageLogRecordProcessor`); health check composition with a hard liveness/readiness split; opt-in dependency-specific health check adapters (DB, Redis, messaging, object storage, search, vector store, and workflow service readiness — all implemented, messaging rewired 2026-08-07/WO-054/P-351). **LLM-orchestration readiness (`AddOrchestrationReadinessCheck`) is retracted, not implemented and never will be** — see Interface Contracts below and WO-047/P-291; startup-probe gating. **Messaging health check rewired (WO-054/P-351, shipped 2026-08-07):** the prior `AddRabbitMqMessagingHealthCheck`/`AddAzureServiceBusMessagingHealthCheck` — each of which built its own second, independently-configured connection rather than reflecting the real bus `07.Messaging.MassTransit`'s `MessagingBusBuilder` actually configured — have been **removed outright** (not deprecated) and replaced by a single `AddMessagingReadinessCheck()` wrapping `07.Messaging`'s `IMessageBusProbe` (P-347). This is a confirmed **breaking change** to a published package — a SemVer-major repack is needed at the next `devops-lead` publish pass | `SharedKernel.Primitives`; abstractions from `02.Caching`, `06.Persistence`, `07.Messaging`, `08.Storage` (`SharedKernel.Storage.Abstractions` — `IFileStorage` consumed by `AddStorageReadinessCheck`), `09.Search` (`SharedKernel.Search.Abstractions` — `ISearchIndexProvisioner` consumed by `AddSearchReadinessCheck`); **by a narrow, individually-named layering exception (WO-047)**, also a `ProjectReference` to `17.Workflows`'s `SharedKernel.Workflows.Temporal` — scoped **exclusively** to `IWorkflowServiceProbe`/`WorkflowServiceHealth` for `AddWorkflowReadinessCheck`, never any other `17.Workflows` type; also a `ProjectReference` to `10.Intelligence`'s `SharedKernel.AI.Abstractions` for `IVectorCollectionProvisioner`/`VectorCollectionHealth` (`AddVectorStoreReadinessCheck`); concrete providers from the same domains when wiring their health checks/telemetry (see Layering exception above); `OpenTelemetry.*`, `Microsoft.Extensions.Diagnostics.HealthChecks`, `AspNetCore.HealthChecks.*` |
| `SharedKernel.MultiTenancy` | Concrete `ITenantProvider` resolution strategies (HTTP header, JWT claim delegation, DB-isolation directory lookup); `TenantResolutionMiddleware` (also sets `TenantId` as `Activity` baggage so it becomes ambient to every log record via `SharedKernel.ServiceDefaults`'s log pipeline); `AmbientTenantProvider` | `SharedKernel.Security.Abstractions` (`ITenantProvider`), `SharedKernel.Security.Oidc` (delegates claim resolution to `OidcTenantProvider` — does not reimplement it), `SharedKernel.Persistence.Abstractions` (`IDbConnectionFactory`, for DB-isolation lookups), `Microsoft.AspNetCore.Http.Abstractions` |

Both packages target `net10.0`, `ImplicitUsings` enabled, `Nullable` enabled. Test sub-folders live inside each project folder (never in a top-level `tests/`).

---

## Technology Stack

| Concern | Technology |
|---------|-----------|
| Distributed tracing, metrics, logging | OpenTelemetry .NET SDK; OTLP exporter configured via the standard `OTEL_EXPORTER_OTLP_ENDPOINT` / `OTEL_EXPORTER_OTLP_PROTOCOL` env vars — no SharedKernel-specific config keys, so the OTel Collector convention stays portable |
| ASP.NET Core / HttpClient / EF Core auto-instrumentation | `OpenTelemetry.Instrumentation.AspNetCore`, `OpenTelemetry.Instrumentation.Http`, `OpenTelemetry.Instrumentation.EntityFrameworkCore` |
| Health checks (BCL contract) | `Microsoft.Extensions.Diagnostics.HealthChecks` |
| Dependency-specific health check probes | `AspNetCore.HealthChecks.Redis`, `AspNetCore.HealthChecks.NpgSql` (community packages, opt-in per service). **Messaging health checks do not use a community `AspNetCore.HealthChecks.*` package (WO-054/P-351, shipped 2026-08-07):** `AddMessagingReadinessCheck` resolves `07.Messaging`'s own `IMessageBusProbe` (P-347) from DI instead — no third-party HealthChecks dependency at all for messaging. **Retired (WO-054/P-351, S-17/C-46, shipped 2026-08-07):** `AspNetCore.HealthChecks.RabbitMQ`/`AspNetCore.HealthChecks.AzureServiceBus`/`RabbitMQ.Client`/`Azure.Messaging.ServiceBus`/`Azure.Identity` have been removed from `SharedKernel.ServiceDefaults.csproj` — kept below purely as a historical record of why the now-deleted `AddRabbitMqMessagingHealthCheck`/`AddAzureServiceBusMessagingHealthCheck` needed them: **API surface gaps discovered (WO-027 Core):** `AspNetCore.HealthChecks.RabbitMQ` 9.0.0 had no direct AMQP-URI-string `AddRabbitMQ` overload — only `Func<IServiceProvider,IConnection>` and `Func<IServiceProvider,Task<IConnection>>` factory overloads existed. `AddRabbitMqMessagingHealthCheck` built a `RabbitMQ.Client.ConnectionFactory { Uri = ... }` and called `CreateConnectionAsync()` inside the async factory overload — required a direct `RabbitMQ.Client` package reference pinned to match `MassTransit.RabbitMQ`'s transitive floor (7.2.1, not 7.1.2 — a lower pin causes NU1605). This is precisely the "second, independently constructed connection" shape WO-054/P-351 found and retired — the AMQP URI never came from the real configured bus. `AspNetCore.HealthChecks.AzureServiceBus` 9.0.0 shipped only queue/topic/subscription-*scoped* checks (`AddAzureServiceBusQueue`/`Topic`/`Subscription`) — there was no namespace-only/connection-only check, so `AzureServiceBusHealthCheck` (internal) was a custom `IHealthCheck` built directly on `Azure.Messaging.ServiceBus.Administration.ServiceBusAdministrationClient.GetNamespacePropertiesAsync()`, requiring direct `Azure.Messaging.ServiceBus`/`Azure.Identity` references pinned to `MassTransit.Azure.ServiceBus.Core`'s transitive floor (7.20.1 / 1.21.0). |
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

NOTE: Every dependency-specific check (Redis, Messaging, DB, Storage, Search, and — once
      unblocked — VectorStore/Orchestration/Workflows) is tagged "ready" and is an explicit opt-in call
      — AddServiceDefaults() / AddSharedKernelHealthChecks() never register any of them automatically. A
      service that does not use a given dependency must not carry a health check for it.

HealthCheckNames  (static class, string constants — WO-028/P-177; Storage added WO-043/P-270; Search
                   added WO-044/P-277; VectorStore added WO-045/P-285; Workflows added WO-046/P-289;
                   Messaging added and RabbitMq/AzureServiceBus retired WO-054/P-351 (2026-08-07) —
                   all implemented and tested. NOTE: `.Orchestration` was never added — WO-047/P-291
                   retracted `AddOrchestrationReadinessCheck` before implementation; see Interface
                   Contracts above.)
    .Database = "database"   .Redis = "redis"   .Messaging = "messaging"   (replaces the retired
                          .RabbitMq = "rabbitmq" / .AzureServiceBus = "azure-service-bus", WO-054/P-351)
    .Cache = "cache"   .Startup = "startup"
    .Storage = "storage"   (P-270 — implemented; see AddStorageReadinessCheck above)
    .Search = "search"   (P-277 — implemented and tested; see AddSearchReadinessCheck above)
    .VectorStore = "vector-store"   (P-285 — implemented and tested 2026-07-27; see
                          AddVectorStoreReadinessCheck above)
    .Workflows = "workflows"   (P-289/WO-047 — implemented and tested 2026-07-27; see
                          AddWorkflowReadinessCheck above)
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

- **Phase build order:** Scaffold (real project/package references replacing the bare `.csproj` stubs) must land before any Core work. Within Core, the foundation (`AddServiceDefaults`, `AddSharedKernelTelemetry`, `AddSharedKernelHealthChecks` + the live/ready split, `StartupGate`/`StartupGateHealthCheck`, and the full `SharedKernel.MultiTenancy` surface) must land before any dependency-specific extension (`AddRedisHealthCheck`, `AddCacheReadinessCheck`, `WithCachingTelemetry`, `AddMessagingReadinessCheck`, `WithMessagingTelemetry`) — every dependency-specific check extends the `IHealthChecksBuilder` returned by `AddSharedKernelHealthChecks()`, so that base must exist first. `WithMessagingTelemetry()` additionally depended on `07.Messaging`'s P-172 (the `"SharedKernel.Messaging"` `ActivitySource` definition) — this landed (`SK.07.OTel` 8/8 `●`) and `WithMessagingTelemetry()` is now implemented (C-19, `SK.13.Core` 28/28 `●`); the cross-domain gate is fully resolved and requires no further check by future agents. `AddSearchReadinessCheck`/`WithSearchTelemetry` (C-37/C-38, WO-044/P-277), `AddVectorStoreReadinessCheck`/`WithIntelligenceTelemetry` (C-39/C-41, WO-045/P-285), `AddWorkflowReadinessCheck`/`WithWorkflowTelemetry` (C-42/C-43, WO-046/WO-047), and `AddMessagingReadinessCheck` (C-46, WO-054/P-351) are all implemented and tested (`SK.13.Core` 46/46 `●`, the permanently-retracted C-40 remaining `—`). `AddOrchestrationReadinessCheck` is retracted (WO-047/P-291) — permanently out of scope, never to be implemented; see the Interface Contracts NOTE on that method. `AddRabbitMqMessagingHealthCheck`/`AddAzureServiceBusMessagingHealthCheck` are retired outright, removed from shipped code, and replaced by `AddMessagingReadinessCheck` (WO-054/P-351) — see the Interface Contracts NOTE for the full retraction rationale. (WO-028) The `ITenantResolutionStrategy.StrategyName` contract member and `TenantResolutionStrategyNames` constants class must land before the cached-lookup refactor in `TenantResolutionMiddleware`, since the cache is keyed by `StrategyName` — implement in the order `StrategyName` contract → constants class → three platform strategies updated → middleware lookup refactor.
- The liveness/readiness tag split is the central invariant of this domain: any check that depends on an external system (database, cache, message broker) is tagged `"ready"` and **never** `"live"`. The `"/health/live"` endpoint must answer only "is this process alive" — never "are this process's dependencies alive."
- Every dependency-specific health check (`AddRedisHealthCheck`, `AddMessagingReadinessCheck`, `AddDatabaseReadinessCheck<TContext>`, `AddDapperDatabaseReadinessCheck`, `AddCacheReadinessCheck`) is an explicit opt-in call on `IHealthChecksBuilder`. `AddServiceDefaults()` and `AddSharedKernelHealthChecks()` register **only** the base endpoint mappings and `StartupGateHealthCheck` — never a dependency-specific check.
- `AddCacheReadinessCheck` must report `Degraded`, not `Unhealthy`, on a cache probe failure — FusionCache's L1 fail-safe may still be correctly serving stale data, and an `Unhealthy` readiness result removes the pod from rotation unnecessarily during a transient Redis blip.
- **`AddMessagingReadinessCheck` must never construct its own connection to the message broker — it resolves only `IMessageBusProbe` (`07.Messaging.MassTransit`'s already-configured bus) from DI** (WO-054/P-351, implemented and tested, 2026-08-07). This corrects and supersedes the prior version of this rule, which permitted messaging health checks to "read their connection details from the already-resolved options objects (`RabbitMqBusOptions`, `AzureServiceBusOptions`)" and construct their own connection from them — ground-truth verification found this is exactly what the retired `AddRabbitMqMessagingHealthCheck`/`AddAzureServiceBusMessagingHealthCheck` did, and it is the confirmed defect this rewrite exists to eliminate: a second, independently-configured connection can pass or fail independently of the real bus's actual health. Cache health checks (`AddCacheReadinessCheck`) are unaffected by this correction — they still read the registered `ICacheService`/Redis connection directly, which is not a second independent connection (there is only one `ICacheService` registration to begin with).
- Every method in the `With*Telemetry` family — `WithMessagingTelemetry()`, `WithCachingTelemetry()`, `WithApplicationTelemetry()`, `WithSearchTelemetry()`, `WithIntelligenceTelemetry()`, `WithWorkflowTelemetry()`, `WithPersistenceTelemetry()` — must be idempotent — calling any of the seven more than once must not register duplicate `ActivitySource`/meter instruments. `WithPersistenceTelemetry()` is the sole member that wires tracing only (no meter, since `06.Persistence` ships no companion `Meter`) — its idempotency requirement still applies to the tracing half.
- `13.ServiceDefaults` never *creates* an `ActivitySource` or custom meter on behalf of another domain — `07.Messaging`'s `"SharedKernel.Messaging"` source, `02.Caching`'s `"SharedKernel.Caching"` source/meter pair, `05.Application`'s `"SharedKernel.Application"` source/meter pair, `09.Search`'s `"SharedKernel.Search"` source/meter pair, `10.Intelligence`'s `"SharedKernel.AI"` source/meter pair, and `17.Workflows`'s `"SharedKernel.Workflows"` source/meter pair are all created in their own domains; this package only wires already-existing sources/meters into the host's `TracerProvider`/`MeterProvider`. Every such wiring extension follows the identical shape: string-name-only `AddSource`/`AddMeter` calls, no `ProjectReference` to the owning domain's concrete assembly when its diagnostics class is `internal` (the common case), and idempotent by construction because the underlying OTel SDK no-ops on a repeated source/meter name.
- `ClaimTenantResolutionStrategy` must delegate to `SharedKernel.Security.Oidc.OidcTenantProvider` — it must never reimplement claim-name parsing or duplicate `SecurityClaimTypes.TenantId` resolution logic.
- `DatabaseTenantResolutionStrategy` must use parameterized queries exclusively — building SQL by string interpolation or concatenation with request-derived values (host, subdomain) is a hard violation.
- `TenantResolutionMiddleware` must be registered after `UseAuthentication()` in the request pipeline — `ClaimTenantResolutionStrategy` requires a populated `ClaimsPrincipal`.
- `AmbientTenantProvider.TenantId` defaults to `Guid.Empty` and is set exactly once per request by `TenantResolutionMiddleware` — no other component may set it.
- `13.ServiceDefaults` may reference concrete provider packages from `02.Caching`, `06.Persistence`, `07.Messaging`, and `12.Security` (see Layering exception above) — but must never reference `14.Presentation`, `15.Integration`, or `16.Testing`. **By one narrow, individually-named exception granted by `arch-lead` (WO-047)**, `13.ServiceDefaults` may also take a `ProjectReference` to `17.Workflows`'s `SharedKernel.Workflows.Temporal`, scoped **exclusively** to `IWorkflowServiceProbe`/`WorkflowServiceHealth` for `AddWorkflowReadinessCheck` — recorded in the root `CLAUDE.md`'s Layering Rules → Hard rules section and Layering Rules diagram. No other `17.Workflows` type (`TemporalOptions`, `ITemporalClient`, `WorkflowBase`/`ActivityBase`, `IWorkflowDispatcher`, `ITemporalRawClientAccessor`, …) may be reached through this exception — reaching for one is a hard violation of the grant's scope. Outside this one named exception, the dependency direction remains strictly downward (`13` may reference `01`–`12` only); a future domain numbered above `13` wanting the identical pattern requires its own named grant, never an inferred widening of this one.
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
    .AddMessagingReadinessCheck();                    // requires 07.Messaging's MessagingBusBuilder
                                                        // .Build() to have already been called so
                                                        // IMessageBusProbe is registered in DI.

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
- `StorageReadinessHealthCheck` (WO-043/P-270, implemented) is a plain sealed class over `IFileStorage`/`Result` (both BCL-primitive-shaped, no reflection) — same AOT profile as `DatabaseReadinessHealthCheck<TContext>`/`CacheReadinessHealthCheck`.
- `SearchReadinessHealthCheck` (WO-044/P-277, implemented) is a plain sealed class over `ISearchIndexProvisioner`/`Result` (both BCL-primitive-shaped per `09.Search/CLAUDE.md`'s own AOT notes, no reflection) — same AOT profile as `StorageReadinessHealthCheck`. `SearchTelemetryExtensions.WithSearchTelemetry` is a plain static class doing string-name `AddSource`/`AddMeter` calls — same AOT profile as `MessagingTelemetryExtensions`/`CachingTelemetryExtensions`/`ApplicationTelemetryExtensions`.
- `VectorStoreReadinessHealthCheck` (WO-045/P-285, implemented) is a plain sealed class over `IVectorCollectionProvisioner`/`Result` (both BCL-primitive-shaped per `10.Intelligence/CLAUDE.md`'s own AOT notes — `VectorValue`, `VectorCollectionHealth`, and the closed `VectorFilter` AST are all `sealed record`/`readonly record struct` over BCL primitives, no reflection) — same AOT profile as `StorageReadinessHealthCheck`/`SearchReadinessHealthCheck`. `IntelligenceTelemetryExtensions.WithIntelligenceTelemetry` is a plain static class doing string-name `AddSource`/`AddMeter` calls — same AOT profile as `MessagingTelemetryExtensions`/`CachingTelemetryExtensions`/`ApplicationTelemetryExtensions`/`SearchTelemetryExtensions`. Note `10.Intelligence/CLAUDE.md` separately documents `Microsoft.SemanticKernel` as a non-AOT-safe dependency encapsulated entirely inside `SharedKernel.AI.SemanticKernel` — that blast radius never reaches this domain, since `13.ServiceDefaults` only ever references `SharedKernel.AI.Abstractions`. (`AddOrchestrationReadinessCheck` is retracted, WO-047/P-291 — no AOT profile applicable, it will never be written.)
- `WorkflowReadinessHealthCheck` (WO-046/WO-047/P-289, implemented) is a plain sealed class over `IWorkflowServiceProbe`/`Result` (per `17.Workflows/CLAUDE.md`'s own AOT notes, `WorkflowServiceHealth` is a `sealed record` over BCL primitives, no reflection) — same AOT profile as `StorageReadinessHealthCheck`/`SearchReadinessHealthCheck`/`VectorStoreReadinessHealthCheck`. `WorkflowTelemetryExtensions.WithWorkflowTelemetry` (string-name-only, no `ProjectReference` needed) is a plain static class doing string-name `AddSource`/`AddMeter` calls — same AOT profile as its five siblings. Note `17.Workflows/CLAUDE.md` separately documents the `Temporalio` native Rust core's RID requirement and its reflection-based default data converter as constraints entirely internal to `SharedKernel.Workflows.Temporal` — that blast radius never reaches this domain, since `13.ServiceDefaults` only ever references `IWorkflowServiceProbe`/`WorkflowServiceHealth` (plain interface/record types) through its narrowly-scoped `ProjectReference` (WO-047).
- `PersistenceTelemetryExtensions.WithPersistenceTelemetry` (WO-051/P-326, implemented) is a plain static class doing a single string-name `AddSource` call, no `ProjectReference` needed (`PersistenceActivitySource` is `internal`) — same AOT profile as its six siblings, minus the `AddMeter` half it deliberately never makes. No corresponding `IHealthCheck` adapter exists for `06.Persistence` in this pass — this method is telemetry-only.
- `MessagingReadinessHealthCheck` (WO-054/P-351, implemented 2026-08-07) is a plain sealed class over `IMessageBusProbe`/`MessageBusHealth` (per `07.Messaging/CLAUDE.md`'s own AOT notes, `MessageBusHealth` is a `sealed record` over two BCL primitives — `bool`/`string?` — no reflection) — same AOT profile as `StorageReadinessHealthCheck`/`SearchReadinessHealthCheck`/`VectorStoreReadinessHealthCheck`/`WorkflowReadinessHealthCheck`. Its removal of `AzureServiceBusHealthCheck` (which used `Azure.Identity.DefaultAzureCredential` — itself not a reflection concern, but a non-trivial third-party SDK surface) and the two retired transport-specific extension methods **reduced** this package's AOT surface — neither `RabbitMQ.Client` nor the Azure SDKs remain referenced at all.

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
- `WithPersistenceTelemetry()` (T-40, WO-051/P-326 — implemented, 2026-07-30): `PersistenceTelemetryExtensionsTests.WithPersistenceTelemetry_SpanFromPersistenceActivitySource_IsCaptured` proves a span emitted by a `"SharedKernel.Persistence"`-named `ActivitySource` is captured once `WithPersistenceTelemetry()` has been called — mirrors `WithCachingTelemetry`'s T-39 `BaseProcessor<Activity>` + `provider.GetRequiredService<TracerProvider>()` capture idiom exactly, verified to fail (empty captured list) when the `AddSource` call is removed. A single tracing-only idempotency test (`TracerProviderBuilder` registration count `<= 1` after calling the method twice) covers the seventh sibling's idempotency contract — **no metrics-idempotency counterpart test exists or is needed**, since this method deliberately makes no `WithMetrics(...)` call (D-16: `06.Persistence` ships no companion `Meter`).
- `AddOrchestrationReadinessCheck` is retracted (WO-047/P-291) — no test coverage exists or will be authored for it.
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
