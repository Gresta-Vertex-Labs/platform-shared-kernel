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

No open phase keys. Closed keys live in `git log`: check a new key is unused with `git log --oneline -S"SK.NN.Key"`.

## Open Work

None — every phase in this domain is complete. The domain has never been published; its first release ships with root P-577 (release train).

## Blocked

None.

## Cross-Domain Dependencies

None open.
