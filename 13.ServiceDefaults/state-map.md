# 13.ServiceDefaults — State Map

> Living board for this domain: what exists, what is open. Completed phase detail is archived outside the repository; `git log` records every change.

## Legend

○ Not started · ◐ In progress · ● Done · ⚑ Blocked · ⊘ Declined / superseded

## Package Board

| Package | Tier | Status | Notes |
| --- | --- | :---: | --- |
| `SharedKernel.ServiceDefaults` | Host | ● | Composition base, references Foundation-tier packages only (`CompositionBaseIsolationTests`): `AddServiceDefaults`, the `WithXTelemetry` family (Application, Caching, Communication, Integration, Intelligence, Messaging, Persistence, Reporting, Scheduling, Search, Storage, Workflow), `StartupGate`, `/health/live` + `/health/ready`, `AddSharedKernelReadiness()` (every `IReadinessProbe` → a `ready` check), `AddSharedKernelRateLimiting`. |
| `SharedKernel.ServiceDefaults.Security` | Host | ● | `AddSharedKernelRequestContext()` (`IRequestContext` over `IUserContext`) and `app.UseSharedKernelRequestContext()` — correlation id, the request's `RequestContextScope`, inbound-baggage refusal (`TrustInboundBaggage`); registered first. |
| `SharedKernel.ServiceDefaults.Persistence` | Host | ● | `AddDatabaseReadinessCheck<T>`, `AddPersistenceStartupReadinessCheck` — the only per-dependency readiness registrations left. |
| `SharedKernel.ServiceDefaults.Security.Mtls` | Host | ● | Kestrel and trusted forwarded-header client certificates (`AddMtlsClientCertificate`), acceptance delegated to `Security.Mtls`'s `IMtlsCertificateValidator`. |
| `SharedKernel.ServiceDefaults.Configuration.KeyVault` | Host | ● | Key Vault as an `IConfiguration` source (`AddSharedKernelKeyVaultConfiguration()`). |
| `SharedKernel.ServiceDefaults.Localization` | Host | ● | `AddSharedKernelLocalization()` — user preference claim → tenant default culture → `Accept-Language`. |
| `SharedKernel.MultiTenancy` | Host | ● | `TenantResolutionMiddleware` (Claim → Header → Database, returning `TenantId?`, inner scope replaces only the tenant), `ITenantCatalog`/`TenantDescriptor`, `DatabaseTenantCatalog`/`CachedTenantCatalog`, `ITenantStatusValidator`/`CatalogTenantStatusValidator`. |
| `SharedKernel.ServiceDefaults.{AI, Caching, Caching.Redis, Messaging, Scheduling, Search, Storage, Workflows.Temporal, Cryptography.KeyVault}` | — | ⊘ | Created by WO-084, deleted by WO-086 (P-569): providers now register their own `IReadinessProbe`. |

Test doubles: `FakeTenantResolutionStrategy`, `InMemoryTenantCatalog` in `16.Testing/SharedKernel.ServiceDefaults.Testing`. Verified by `consumer-verify`.

## Phase Key Registry

| Phase key | Phase | Status |
| --- | --- | :---: |
| `SK.13.Design` | Design (D-01–D-39; D-11 retracted) | ● |
| `SK.13.Scaffold` | Scaffold (S-01–S-24) | ● |
| `SK.13.Core` | Core (C-01–C-70; C-40 N/A) | ● |
| `SK.13.Tests` | Tests (T-01–T-82; T-35 N/A) | ● |
| `SK.13.Docs` | Docs (DO-01–DO-32) | ● |
| `SK.13.Published` | Published | ● |
| `SK.13.WO084` | WO-084 per-integration package split (P-531–P-537; D-40–D-43, S-25–S-26, C-71–C-81, T-83–T-87, DO-33–DO-35, V-01) — its probe-only packages later deleted by WO-086 | ● |
| `SK.13.WO086` | WO-086 foundation refactor (P-565, P-566, P-569, P-574, P-575; W86-01–W86-05) | ● |

## Open Work

None — every phase in this domain is complete.

## Blocked

None.

## Cross-Domain Dependencies

None open. Every inbound telemetry and readiness dependency (02, 06, 07, 15, 17, 19, 20) has shipped; the old 13→17/13→19 layering grants are replaced by the tier check.

## Completed Phases

- WO-086 ● Request context (`AddSharedKernelRequestContext`/`UseSharedKernelRequestContext`), `AddSharedKernelReadiness()` over `IReadinessProbe`, nine probe-only packages deleted, all Host tier (P-565, P-566, P-569, P-574, P-575) (2026-09-26)
- Reporting telemetry ● `WithReportingTelemetry()` for the 20.Reporting pre-publish pass (2026-09-26)
- Bug fix ● `AddSearchReadinessCheck` duplicate-name crash with several indexes (2026-09-23)
- WO-084 ● Composition base split from thirteen integration packages; base closure 25 projects / 73 packages → 1 / 10 (2026-09-14)
- WO-081 ● Key Vault key provider wrapped in `CachedEncryptionKeyProvider` by default (P-503) (2026-09-08)
- WO-075 / WO-068 / WO-073 / WO-078 ● Tenant catalog, cached catalog, Key Vault provider, scheduling telemetry/readiness, localization (P-449, P-465, P-466, P-471, P-472, P-483) (2026-09-04)
- WO-064 ● `WithIntegrationTelemetry` (P-430) (2026-08-21)
- WO-063 ● Rate-limit rejection wired to the platform problem shape (P-419) (2026-08-20)
- WO-061 ● Secure-by-default tenant strategy order, mTLS forwarded-header allow-list, first `[LoggerMessage]` logging (P-393–P-400) (2026-08-19)
- WO-058 ● `AddMtlsClientCertificate`/`AddMtlsForwardedHeaderCertificate` (P-378) (2026-08-14)
- WO-056 ● `WithCommunicationTelemetry` (P-365) (2026-08-12)
- WO-054 ● `AddMessagingReadinessCheck` (P-351) (2026-08-07)
- WO-051 ● `WithPersistenceTelemetry` (P-326) (2026-07-30)
- WO-050 ● `WithCachingTelemetry` tracing (P-305) (2026-07-29)
- WO-047 ● Workflow readiness and telemetry (2026-07-27)
- Earlier phases (initial Design → Published) — archived.

## Changelog

- [2026-09-28] State map rewritten as a living board; completed phase detail archived outside the repository.
- [2026-09-26] `WithReportingTelemetry()` added to the base (20.Reporting pre-publish pass).
- [2026-09-26] WO-086 recorded as `SK.13.WO086`.
- [2026-09-23] `SharedKernel.ServiceDefaults.Search`: per-index readiness checks registered under unique names.
- [2026-09-14] `SK.13.WO084` opened and closed (26/26 ●, user-directed).
