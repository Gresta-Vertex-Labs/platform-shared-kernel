# 06.Persistence — State Map

> Living board for this domain: what exists, what is open. Completed phase detail is archived outside the repository; `git log` records every change.

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
| `SK.06.Design` | Design | ● |
| `SK.06.Scaffold` | Scaffold | ● |
| `SK.06.Core` | Core | ● |
| `SK.06.Tests` | Tests | ● |
| `SK.06.Docs` | Docs | ● |
| `SK.06.Published` | Published | ● |
| `SK.06.P541` | P-541 Attach the Domain Clock on Load and Persist the Event Sequence | ● |
| `SK.06.P557` | P-557 Pre-First-Publish Gold-Standard Pass (85/86; P-17 per-package publish superseded by the release train, root P-577) | ● |

## Open Work

None — every phase in this domain is complete. The domain has never been published; its first release ships with root P-577 (release train).

## Blocked

None.

## Cross-Domain Dependencies

None open.

## Completed Phases

- WO-086 ● Foundation refactor — tiers and adapter edges, `SharedKernel.Execution` contracts, `TenantId` on every tenant parameter, `audit-sealing`/`field-encryption` readiness probes, `SharedKernel.Persistence.Testing` (P-563–P-575) (2026-09-26)
- P-558 ● `SharedKernel.Persistence.PostgreSQL` merged into `.EfCore` (six packages) (user-directed)
- P-557 ● `06.Persistence` pre-first-publish gold-standard pass — split into Npgsql/EfCore.Encryption/EfCore.Auditing, local seams instead of `05`/`12` references, public API tracked (2026-09-20)
- P-541 ● Attach the domain clock on load and persist the event sequence (2026-09-15)
- P-498 ● Close the sync-over-async KMS materializer defect (WO-081) (2026-09-08)
- P-456, P-457 ● Append-only audit trail contracts and EF Core hash-chain implementation (WO-071) (2026-09-02)
- P-448 ● Migrate EF Core key-provider consumers onto the async contract (WO-068)
- P-440 ● EF Core value conversion for `Money` (WO-066) (2026-09-02)
- P-333–P-339 ● WO-053 — observability, named configuration binding, shared PostgreSQL fixture, soft-delete restore + command timeout, read-replica routing, pgvector ergonomics
- P-315–P-325 ● WO-051 — `xmin` concurrency, tenanted repository expressions, keyset paging, split queries, query tagging and tracing, transient-fault retry, Dapper multi-mapping, DbContext pooling, performance, packaging, async probes
- P-227, P-228 ● Field encryption delegated to `SharedKernel.Cryptography`; `EfUnitOfWork` bridges the unit-of-work seam (WO-037)
- P-147–P-151 ● WO-024 — key-rotation fix, bulk mutations, streaming reads, database readiness contract, migration and seed runner
- P-111–P-113 ● WO-019 — field encryption options, converter, rotation infrastructure and tests
- P-105–P-109 ● WO-018 — EF Core correctness fixes, capability gaps, string includes, PostgreSQL and Dapper packages
- Earlier phases (P-033, P-065–P-074, P-078–P-082, P-091–P-094, P-097–P-102) — archived.

## Changelog

- [2026-09-28] State map slimmed to a living board; completed phase detail archived outside the repository — public-release cleanup
- [2026-09-26] WO-086 record added (root P-564–P-575): tiers, `SharedKernel.Execution` contracts, `TenantId`, readiness probes; P-17 superseded by the release train
- [2026-09-20] P-557 docs wave — `06.Persistence/CLAUDE.md` rewritten for the post-split domain
- [2026-09-19/20] SK.06.P557 opened and closed for implementation (85/86 ●) — seven build waves plus four remediation waves
- [2026-09-15] SK.06.P541 opened and closed (7/7 ●) — `DomainClockMaterializationInterceptor`, persisted event sequence
