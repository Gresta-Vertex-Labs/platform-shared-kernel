# 06.Persistence — State Map

> Living board for this domain: what exists and what is open. Completed work is not kept here; `git log` is the record.

## Legend

○ Not started · ◐ In progress · ● Done · ⚑ Blocked · ⊘ Declined / superseded

## Package Board

| Package | Tier | Status | Notes |
| --- | --- | --- | --- |
| `SharedKernel.Persistence.Abstractions` | Abstractions | ● | ORM-free: repositories, `EntityVersion`, bulk mutations, `ICrossTenantScope`, `IDbConnectionFactory`; implements Execution's `IUnitOfWork`/`IAuditTrailWriter` contracts, never redeclares them. |
| `SharedKernel.Persistence.Npgsql` | Adapter | ● | Data sources, TLS (`VerifyFull` by default), the canonical role script, SQLSTATE classifier, advisory locks, tenant session binding. |
| `SharedKernel.Persistence.EfCore` | Adapter | ● | `builder.AddSharedKernelPostgres<TContext>(…)` — conventions, one transaction per DI scope, tenant filter + write guard + transaction-local RLS, migrations/seeding; absorbed the former `.PostgreSQL` package (P-558). Edge → Npgsql. |
| `SharedKernel.Persistence.Dapper` | Adapter | ● | `IDbSessionFactory` sessions joining the unit of work, parameterized SQL only, type handlers incl. `TenantId`. Edge → Npgsql. |
| `SharedKernel.Persistence.EfCore.Auditing` | Adapter | ● | HMAC-chained append-only audit ledger v3, background sealer, `audit-sealing` readiness probe. Edge → EfCore. |
| `SharedKernel.Persistence.EfCore.Encryption` | Adapter | ● | Field encryption v3 with blind indexes, rotation and crypto-shredding, `field-encryption` readiness probe. Edge → EfCore. |

## Phase Key Registry

| Phase key | Phase | Status |
| --- | --- | --- |

Closed keys live in `git log`: check a new key is unused with `git log --oneline -S"SK.NN.Key"`.

## Open Work

None — every phase in this domain is complete.

## Blocked

None.

## Cross-Domain Dependencies

- **Inbound, 01.Core:** SK.01.ObservabilityHelpers (`TelemetryVersion`, `ActivityFailureExtensions`, `WellKnownOutcomes`, `LoggingEventIdRanges.PackageSubBlockWidth/SubBlockStart/IsInSubBlock`) is a hard dependency of both phases; SK.01.CoreObservabilityFixes makes `TransactionRolledBackException` a `SharedKernelException` (`execution.transaction_rolled_back`, `Unexpected`) and adds Execution -> Core, so `Persistence.Abstractions` inherits Core transitively (Dapper takes an explicit Core reference in SK.06.PersistenceErrorSafety).
- **Inbound, 03.Domain:** SK.03.DomainErrorCodes turns a stored `Money` amount with excess decimals into `DomainException`/`Unexpected` (500) instead of `BusinessRuleViolationException` (422); SK.06.PersistenceErrorSafety T-06 pins the new type.
- **Inbound, 16.Testing:** SK.16.TelemetryCapture (`ActivityRecorder`, `MetricRecorder`, log assertions) is used by every telemetry and log test of both phases.
- **Inbound, 00.Governance:** SK.00.ObservabilityReportOnly must show zero Persistence findings (SK.06.PersistenceConformance T-05).
- **Outbound, 13.ServiceDefaults:** after SK.13.WildcardTelemetry `WithPersistenceTelemetry` keeps only `Npgsql`; until then it names `SharedKernel.Persistence.EfCore.Auditing` and `.Encryption`, which SK.06.PersistenceConformance removes (nothing is dropped meanwhile: the folded name `SharedKernel.Persistence` is already subscribed). `PersistenceReadmeSampleTests` must not name a removed meter or error code.
- **Outbound, 14.Presentation:** 57014 now answers `Timeout` (504); audit record not-found answers 404 and no-tenant 403 (`persistence.tenant_required`); decrypt failures remain 500 but carry `persistence.encryption.*` codes. Presentation maps by `Error.Type`, so no change is expected; confirm `ErrorTypeStatusCodeMap` covers `Timeout`.
- **Outbound, 18.Idempotency / 07.Messaging:** `Idempotency.EfCore` and `Messaging.MassTransit.EfCore` build on `Persistence.EfCore`; neither catches `KeyNotFoundException`, `CryptographicException` or the changed classifier types (verified by grep at planning time). Release notes list the renamed meters, instruments, codes and EventIds for dashboards.
