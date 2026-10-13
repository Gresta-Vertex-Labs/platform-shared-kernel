# 13.ServiceDefaults — State Map

> Living board for this domain: what exists and what is open. Completed work is not kept here; `git log` is the record.

## Legend

○ Not started · ◐ In progress · ● Done · ⚑ Blocked · ⊘ Declined / superseded

## Package Board

| Package | Tier | Status | Notes |
| --- | --- | :---: | --- |
| `SharedKernel.ServiceDefaults` | Host | ● | Composition base, references Foundation-tier packages only (`CompositionBaseIsolationTests`): `AddServiceDefaults` (exports every `SharedKernel.*` source and meter, HttpClient metrics, one resource with the informational `service.version`), the third-party `With{X}Telemetry()` (Communication, Messaging, Persistence, Workflow), `StartupGate`, `/health/live` + `/health/ready`, `AddSharedKernelReadiness()` (every `IReadinessProbe` → a `ready` check), `AddSharedKernelRateLimiting`. |
| `SharedKernel.ServiceDefaults.Security` | Host | ● | `AddSharedKernelRequestContext()` (`IRequestContext` over `IUserContext`) and `app.UseSharedKernelRequestContext()` — correlation id, the request's `RequestContextScope`, inbound-baggage refusal (`TrustInboundBaggage`); registered first. |
| `SharedKernel.ServiceDefaults.Persistence` | Host | ● | `AddDatabaseReadinessCheck<T>`, `AddPersistenceStartupReadinessCheck` — the only per-dependency readiness registrations left. |
| `SharedKernel.ServiceDefaults.Security.Mtls` | Host | ● | Kestrel and trusted forwarded-header client certificates (`AddMtlsClientCertificate`), acceptance delegated to `Security.Mtls`'s `IMtlsCertificateValidator`. |
| `SharedKernel.ServiceDefaults.Configuration.KeyVault` | Host | ● | Key Vault as an `IConfiguration` source (`AddSharedKernelKeyVaultConfiguration()`). |
| `SharedKernel.ServiceDefaults.Localization` | Host | ● | `AddSharedKernelLocalization()` — user preference claim → tenant default culture → `Accept-Language`. |
| `SharedKernel.MultiTenancy` | Host | ● | `TenantResolutionMiddleware` (Claim → Header → Database, returning `TenantId?`, inner scope replaces only the tenant), `ITenantCatalog`/`TenantDescriptor`, `DatabaseTenantCatalog`/`CachedTenantCatalog`, `ITenantStatusValidator`/`CatalogTenantStatusValidator`. |
| `SharedKernel.ServiceDefaults.{AI, Caching, Caching.Redis, Messaging, Scheduling, Search, Storage, Workflows.Temporal, Cryptography.KeyVault}` | — | ⊘ | Created by WO-084, deleted by WO-086 (P-569): providers now register their own `IReadinessProbe`. |

Test doubles: `FakeTenantResolutionStrategy`, `InMemoryTenantCatalog` in `src/Hosting/ServiceDefaults/SharedKernel.ServiceDefaults.Testing`. Verified by `consumer-verify`.

## Phase Key Registry

| Phase key | Phase | Status |
| --- | --- | --- |

Closed keys live in `git log`: check a new key is unused with `git log --oneline -S"SK.NN.Key"`.

## Open Work

None — every phase in this domain is complete.

## Blocked

None.

## Cross-Domain Dependencies

Open notes left by `SK.13.WildcardTelemetry` (nothing here is a task of this domain):
- **Third-party names stay byte-identical (owners):** `MassTransit` (07), `Npgsql` (06), `Polly` and gRPC client instrumentation (11), the Temporal tracing sources (17, owner of the tracing interceptor).
- **00.Governance:** the telemetry-name test (audit G3) is the guard that makes the wildcard safe: every kernel source and meter name must match `^SharedKernel\.[A-Z]\w+$`.
- **Stale references after this ships** to the eight deleted methods (docs of other domains, so their Phase 1/2 or `/sync-brain` fixes them): READMEs and `CLAUDE.md` of 02, 05, 06, 07, 08, 09, 10, 11, 15, 17, 19, 20, `samples/README.md`, and the arch-planner agent files (`caching`, `application`, `persistence`, `messaging`, `storage`, `search`, `intelligence`, `integration`, `reporting`, `scheduling`, `communication`, `workflow`).

Notes for `SK.13.HostObservabilityFixes` (WO-087 / P-594) and `SK.13.ServiceDefaultsConformance` (WO-087 / P-611); nothing here is a task of this domain.

**Inbound**
- **16.Testing, `SK.16.TelemetryCapture` (P-581):** the span, metric and log assertions named above. `InMemoryLogger` does not capture the ambient request context (correlation id and tenant arrive through the OTel baggage processor, not through `ILogger` scopes), so this domain asserts ambient enrichment through the OTel in-memory exporter and everything else through the in-memory logger.
- **00.Governance, `SK.00.ObservabilityReportOnly` (P-583):** the EventId integrity assertion should read this domain's `## Logging` ownership table (seven indexes, one package each, `MultiTenancy` at index 1). SK0044 sees no bare EventId literal after `SK.13.ServiceDefaultsConformance`.
- **11.Communication, `SK.11.TokenUnavailableEdge` (P-592):** needed by T-05 above.

**Outbound (notes only)**
- **14.Presentation (dispatched right after this entry, P-595):** (1) the request-context and tenant tags are written before `UseExceptionHandler()`, so `SharedKernelExceptionHandler`'s `SuppressDiagnosticsCallback` cannot hide them; this domain starts no middleware span. (2) `AddSharedKernelTelemetry()` leaves the ASP.NET Core instrumentation's `RecordException` at its default (off) and this domain will not turn it on: the exception handler and the gRPC interceptor record the failure on the server span themselves with the P-580 recorder, once, so suppressing the framework's own handled-exception diagnostics loses nothing. Verify against the OpenTelemetry package pinned in `Directory.Packages.props` before relying on this. (3) gRPC calls hosted in ASP.NET Core run through the HTTP pipeline, hence through `UseSharedKernelRequestContext()`: they get the `correlation.id` / `tenant.id` tags and the validated correlation id here, and 14 adds only what is gRPC-specific (trace and correlation ids in the status details). (4) HTTP and gRPC servers must not apply inbound actor or tenant headers either (`SK.13.HostObservabilityFixes` D-03).
- **12.Security (`SK.12.SecurityConformance`):** Security keeps its events in 12xxx and Security.Mtls owns 12300-12399; `ServiceDefaults.Security.Mtls` is 13300-13399 in 13.ServiceDefaults. Tests in both domains must name the domain base.
- **01.Core, `SK.01.CoreConformance`:** the "one 100-wide sub-block per package" rule holds here (seven packages, ten slots), so no family sharing or offsets are needed; recorded in `## Decisions`. Root L7 should say what a static logging site or a startup callback uses as its category (an internal marker type per package, `ILoggerFactory.CreateLogger<TMarker>()`); ask `arch-lead` to add it.
- **All consumers of `HealthCheckRegistrationLogging.LogRegistration`:** none outside `ServiceDefaults.Persistence` on disk (src and samples searched); a service's own check that called it must drop the `categoryName` argument (breaking change, release notes).
- **Dashboards and alert rules** keyed on EventIds 13000-13007 or on the old message texts: renumbered and reworded (release notes).
- **Samples (reported, not edited):** the `With{X}Telemetry` call sites listed above still apply; no sample calls `LogRegistration`; the canonical-order note for the Shop is unchanged by these two phases.
