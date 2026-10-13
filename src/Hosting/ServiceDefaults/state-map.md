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

- **Third-party names stay byte-identical (owners):** `MassTransit` (07), `Npgsql` (06), `Polly` and gRPC client instrumentation (11), the Temporal tracing sources (17, owner of the tracing interceptor). The four `With{X}Telemetry()` extensions subscribe them by name.
- **00.Governance:** the telemetry-name test is the guard that makes the `SharedKernel.*` wildcard safe: every kernel source and meter name must match `^SharedKernel\.[A-Z]\w+$`.
