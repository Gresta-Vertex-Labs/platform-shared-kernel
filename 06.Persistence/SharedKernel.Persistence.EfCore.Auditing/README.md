# SharedKernel.Persistence.EfCore.Auditing

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![PostgreSQL 15+](https://img.shields.io/badge/PostgreSQL-15%2B-4169E1?logo=postgresql&logoColor=white)](https://www.postgresql.org/)
![Tamper-evident](https://img.shields.io/badge/ledger-tamper--evident-success)
![Format: AUDITv3](https://img.shields.io/badge/format-AUDITv3%20(specified)-informational)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)

> **An append-only, tamper-evident audit ledger on PostgreSQL: every command records who did what to which resource,
> a background sealer chains the records with keyed MACs, and anyone can verify the chain — while personal data in
> the records stays erasable.**

An audit log in an ordinary table proves nothing: anyone with write access can edit, delete or backfill it. A ledger
that locks on every write slows every request and deadlocks under load. This package does neither. The request path
is **one `INSERT`** inside the business transaction. A background **sealer** links committed records, in commit-safe
order, into one hash chain per tenant and resource type, signs periodic checkpoints outside the chain, and
verification tells you precisely whether a chain is intact, broken — and where — or unverifiable. The byte-level
format is [specified](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/06.Persistence/SharedKernel.Persistence.EfCore.Auditing/AUDIT-FORMAT.md),
with test vectors, so an auditor can verify it with their own code.

| ✍️ Cheap to write | ⛓️ Sealed | 🔍 Verifiable | 🧽 Erasable |
| --- | --- | --- | --- |
| One `INSERT`, no lock, no sequence, no retry | Per-(tenant, resource type) HMAC-SHA256 chains | `Intact` / `Broken` / `Unverifiable` with the failure kind | Snapshots erased for GDPR/KVKK, chain intact |
| `Succeeded` commits with the business write | Commit-safe order: late commits never skipped | Signed checkpoints catch truncation and rewrites | The erasure is itself recorded |
| `Failed` recorded after a rollback | Key rotation and compromise recovery | Append-only enforced by database triggers | Salted payload commitments |

## Contents

- [Install](#install)
- [Quick start](#quick-start)
- [How it works](#how-it-works)
- [Writing records](#writing-records)
- [Reading, verifying, exporting, erasing](#reading-verifying-exporting-erasing)
- [Keys: rotation and compromise](#keys-rotation-and-compromise)
- [Database roles (required for the guarantees)](#database-roles-required-for-the-guarantees)
- [Configuration reference](#configuration-reference)
- [Security model](#security-model)
- [AI quick reference](#ai-quick-reference)

## Install

```shell
dotnet add package SharedKernel.Persistence.EfCore.Auditing
```

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Adapter (its one adapter edge, to `SharedKernel.Persistence.EfCore`, is declared) |
| Database | PostgreSQL 15 or later |
| Builds on | `SharedKernel.Persistence.EfCore`, `SharedKernel.Cryptography` (`IHmacSigner`, optional `IAsymmetricSignatureService`) |
| Implements | `IAuditTrailWriter` from [`SharedKernel.Execution`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/tree/main/01.Core/SharedKernel.Execution) (`SharedKernel.Execution.Auditing`) — used by `SharedKernel.Application.Pipeline`'s `AuditingBehavior` directly |
| Namespaces | `SharedKernel.Persistence` (`UseAuditTrail`), `SharedKernel.Persistence.EfCore.Auditing` (query, maintenance), `SharedKernel.Persistence.EfCore` (migration helpers) |

## Quick start

**1. Register:**

```csharp
using SharedKernel.Persistence;   // AddSharedKernelPostgres, UseMultiTenancy, UseAuditTrail

builder.Services.AddSharedKernelCryptography(builder.Configuration)   // IHmacSigner
    .AddAsymmetricSigning();                                          // checkpoints (optional)

builder.AddSharedKernelPostgres<OrderDbContext>("orders", p => p
    .UseMultiTenancy(rowLevelSecurity: true)
    .UseAuditTrail());          // binds SharedKernel:Persistence:Auditing from the same configuration
```

```json
"SharedKernel": { "Persistence": { "Auditing": {
  "CurrentKeyId": "k2",
  "Keys": {
    "k1": { "Material": "<base64, 32+ bytes, from a secret store>", "Order": 1 },
    "k2": { "Material": "<base64, 32+ bytes, from a secret store>", "Order": 2 }
  },
  "CheckpointSigningKeyId": "audit-checkpoints-2026",
  "AcceptedCheckpointSigningKeyIds": [ "audit-checkpoints-2025" ],
  "Sealer": { "Enabled": true, "Interval": "00:00:02", "BatchSize": 500, "CheckpointInterval": "01:00:00" },
  "SelfCheck": "Fail"
} } }
```

**2. Create the tables** in a migration (they are not part of the EF Core model):

```csharp
protected override void Up(MigrationBuilder migrationBuilder) =>
    migrationBuilder.CreateAuditLedgerTable(runtimeRole: "app_runtime", sealerRole: "app_audit_sealer");
protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropAuditLedgerTable();
```

**3. Audit a command** — with `SharedKernel.Application.Pipeline`, a marker interface is all it takes:

```csharp
public sealed record ApproveOrder(OrderId Id) : ICommand, IAuditableRequest<Result>
{
    public string Action => "order.approved";
    public string ResourceType => nameof(Order);
    public string ResourceId => Id.Value.ToString();
    public string? BeforeSnapshot => null;
    public string? GetAfterSnapshot(Result response) => null;
}
// builder.Services.AddSharedKernelApplicationBehaviors().AddDefaultBehaviors()
//     .AddTransactionBehavior().AddAuditingBehavior().Build();
```

Options are validated at startup: the current key must exist and be the newest (highest `Order`), every key must
decode to at least 32 bytes, orders must be distinct.

## How it works

```text
 request ──► business transaction ───────────────► COMMIT        sealer (every instance; one seals at a time)
             │ business writes                                    │ pg_try_advisory_xact_lock
             │ INSERT record + payload (Succeeded, 1 statement)   │ records committed before the oldest running
             │                                                    │   transaction, in (insert_xid, id) order
             └─ rollback? → INSERT (Failed) on its own connection │ INSERT audit_chain_links:
                                                                  │   sequence, previous MAC, HMAC-SHA256 MAC
 verify ◄── audit_records + audit_chain_links + checkpoints ◄─────┘ every CheckpointInterval: sign the chain head
```

| Step | What happens | Cost on the request |
| --- | --- | --- |
| Write | `IAuditTrailWriter.RecordAsync` runs one `INSERT` (record + erasable payload). A **Succeeded** entry goes into the caller's transaction, so it commits or rolls back with the business write; a **Failed** entry commits on its own connection | one statement |
| Seal | The sealer takes committed records whose inserting transaction is older than the oldest running one, in `(insert_xid, id)` order, and appends a link: sequence, previous MAC, HMAC-SHA256 MAC. A long transaction that commits late is never skipped or sealed out of order | none |
| Checkpoint | Every `Sealer:CheckpointInterval` the sealer verifies each chain that moved and signs its head with an asymmetric key into an `IAuditCheckpointSink` (default: the `audit_checkpoints` table; use WORM storage for stronger guarantees) | none |
| Verify | `VerifyChainAsync` / `VerifyChainFromCheckpointAsync` return `Intact`, `Broken` or `Unverifiable` with a failure kind: `SequenceGap`, `HashMismatch`, `LinkMismatch`, `KeyRegression`, `AnchorMismatch`, `TailTruncated`, `UnknownKey`, `PayloadErased`, `NotSealed` | — |

Each chain is one tenant and one resource type, so tenants never contend and a verification never reads another
tenant's records. The sealer holds a transaction-scoped advisory lock, so it works behind a transaction-mode pooler.

## Writing records

`SharedKernel.Application.Pipeline`'s `AuditingBehavior` writes for every `IAuditableRequest`: `Succeeded` inside the
transaction via `IUnitOfWork.OnBeforeCommit`, `Failed` after a rollback. Outside the request pipeline, write directly:

```csharp
public sealed class ApproveOrderHandler(IAuditTrailWriter audit, IUnitOfWork unitOfWork) : ICommandHandler<ApproveOrder>
{
    public async Task<Result> Handle(ApproveOrder command, CancellationToken ct)
    {
        // ... change the aggregate ...
        unitOfWork.OnBeforeCommit(token => audit.RecordAsync(new AuditEntry
        {
            Action = "OrderApproved",
            ResourceType = "Order",
            ResourceId = command.OrderId.ToString(),
            Outcome = AuditOutcome.Succeeded,
            AfterSnapshot = afterJson,
        }, token));
        return Result.Success();
    }
}
```

| Field | Source |
| --- | --- |
| User, actor kind, client, session, tenant | `IRequestContext` |
| Source service | `SharedKernel:Persistence:ServiceName` / `UseServiceName` |
| Correlation and W3C trace ids | the ambient `Activity` (correlation id from its `WellKnownBaggageKeys.CorrelationId` baggage, set by `UseSharedKernelRequestContext()`) |
| Action, resource, outcome, snapshots, error code, approval id, idempotency key | the `AuditEntry` |

- **Actor kinds:** `User`, `Service` (a machine identity), `System` (a job under `SystemRequestContext`) or
  `Anonymous`. An unauthenticated caller is always recorded as `Anonymous`, never as the platform's own work.
- **Limits** are checked before any SQL (`AuditFieldLimits`): an oversized value throws `ArgumentException` and never
  aborts the business transaction.
- **The system chain:** a record without a tenant is accepted only from an authenticated system identity or inside a
  cross-tenant scope.
- **Idempotency:** a reused `IdempotencyKey` returns the stored record; reused for a different event it throws.

## Reading, verifying, exporting, erasing

```csharp
var history = await auditQuery.QueryAsync(new AuditRecordQuery { ResourceType = "Order", ResourceId = id }, ct);
var chain   = await auditQuery.VerifyChainAsync("Order", requirePayloads: false, ct);
// chain.IsIntact; otherwise chain.Status, chain.FailureKind and chain.FailedAtSequence say what and where
```

| Call | Does |
| --- | --- |
| `IAuditQueryService.QueryAsync(query)` | The caller's tenant; keyset paging (`Cursor`/`NextCursor`); every shape index-backed |
| `QueryAcrossTenantsAsync(query)` | Needs an active `ICrossTenantScope`; records its own `AuditLedger` entry first |
| `ExportRangeAsync(type, from, to)` | Streams a range; records its own entry first |
| `VerifyChainAsync(type, requirePayloads)` / `VerifyChainFromCheckpointAsync(checkpoint)` / `VerifyRecordAsync(id)` | Full or incremental verification |
| `IAuditLedgerMaintenance.ErasePayloadAsync(recordId, reason)` / `EraseResourcePayloadsAsync(type, id, reason)` | Deletes snapshots and their salt, records the erasure; the chain stays verifiable (it commits to `SHA-256(salt ‖ payload)`) |
| `SealPendingAsync()` | Seals now (tests, maintenance) |
| `SealAllChainsAsync(reason)` | After a key compromise — see below |
| The `audit-sealing` readiness probe (`IReadinessProbe`, `AuditSealingReadiness.ProbeName`) | Registered by `UseAuditTrail()`. `Healthy` while sealing keeps up, `Degraded` when the oldest unsealed record is older than `Sealer:MaxReadyLag`, `Unhealthy` when the ledger cannot be read; data `UnsealedRecords`, `OldestUnsealedOccurredOn`, `Lag`. The host maps it with `AddHealthChecks().AddSharedKernelReadiness()` |

## Keys: rotation and compromise

1. **Rotate:** add the new key with a higher `Order`, set `CurrentKeyId` to it, deploy. New seals use it; old records
   keep verifying with the old key, which must stay in `Keys` for as long as they must verify (removing it makes them
   `Unverifiable` / `UnknownKey`).
2. **Compromise:** right after rotating, run `IAuditLedgerMaintenance.SealAllChainsAsync("reason")` inside a
   cross-tenant scope. Every chain gets a marker sealed under the new key and a fresh checkpoint; a record an attacker
   later forges with the old key is reported as `KeyRegression`, a re-sealed old record as `LinkMismatch`.
3. **Checkpoint signing keys** rotate the same way: add the old id to `AcceptedCheckpointSigningKeyIds`. Verification
   never trusts the key id a checkpoint names on its own.

Register your own `IAuditRecordAuthenticator` (for example a KMS that computes MACs remotely) or
`IAuditCheckpointSink` before `UseAuditTrail` to replace the defaults; the keyring is then not required.

## Database roles (required for the guarantees)

The triggers make the ledger tables append-only for every role **except** the table owner and superusers, who can
disable them. Own the tables with a migration role and run the application under a separate one — the platform's
single role script is in
[SharedKernel.Persistence.Npgsql → Roles](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/tree/main/06.Persistence/SharedKernel.Persistence.Npgsql#roles-the-one-canonical-script)
(`app_migrator`, `app_runtime`, `app_audit_sealer`). The migration, run as `app_migrator`, then sets the ledger's
privileges itself:

```csharp
migrationBuilder.CreateAuditLedgerTable(runtimeRole: "app_runtime", sealerRole: "app_audit_sealer");
migrationBuilder.Sql("REVOKE UPDATE, DELETE, TRUNCATE ON audit_records, audit_record_payloads, audit_chain_links, audit_checkpoints FROM app_cross_tenant;");
```

It first revokes everything the roles hold on the four tables — including the `UPDATE`/`DELETE` that
`ALTER DEFAULT PRIVILEGES` grants the runtime role on every new table — and then grants exactly:

| Table | `app_runtime` | `app_audit_sealer` |
| --- | --- | --- |
| `audit_records` | `SELECT, INSERT` | `SELECT` |
| `audit_record_payloads` | `SELECT, INSERT, DELETE` (DELETE = payload erasure) | — |
| `audit_chain_links`, `audit_checkpoints` | `SELECT` (plus `INSERT` when there is no sealer role) | `SELECT, INSERT` |

The ledger tables must not use row-level security: the ledger filters by tenant itself, and the sealer must read every
tenant's records. `AuditLedgerSchema.CreateScript` holds the same DDL (idempotent) for tooling and tests.

**A separate sealer role (recommended).** Without it, anything running as the application — a bug, an injected
statement — can insert into `audit_chain_links`. A forged link cannot hide unsealed records (the sealer and the probe
select records that have no link), and verification reports its MAC, but only a separate role keeps the application
from writing seals at all:

```csharp
// ConnectionStrings:audit-sealer connects as app_audit_sealer
builder.Services.AddSharedKernelNpgsql(builder.Configuration.GetSection("SharedKernel:Persistence:audit-sealer"), "audit-sealer");
```

```json
"SharedKernel": { "Persistence": {
  "audit-sealer": { "ConnectionStringName": "audit-sealer" },
  "Auditing": { "Sealer": { "DataSourceName": "audit-sealer" } }
} }
```

**The startup self-check** (`SelfCheck`: `Off`, `Warn` (default), `Fail` — use `Fail` in production) reports a
superuser runtime role, a runtime role that owns (or is a member of the owner of) a ledger table, any
`UPDATE`/`DELETE`/`TRUNCATE` privilege on it, row-level security on it, and any missing trigger or trigger not
`ENABLE ALWAYS` (which `session_replication_role = replica` would silently bypass). With `Sealer:DataSourceName` set it
also reports a runtime role that can still `INSERT` into `audit_chain_links` or `audit_checkpoints`, and checks the
sealer role for superuser, ownership and `UPDATE`/`DELETE`/`TRUNCATE`.

## Configuration reference

| Key under `SharedKernel:Persistence:Auditing` | Default | Meaning |
| --- | --- | --- |
| `CurrentKeyId`, `Keys:{id}:Material`, `Keys:{id}:Order` | — | The MAC keyring; the current key has the highest order |
| `CheckpointSigningKeyId`, `AcceptedCheckpointSigningKeyIds` | — | Asymmetric checkpoint signing (optional) |
| `Sealer:Enabled` | `true` | Run the background sealer in this process |
| `Sealer:Interval` | 2 seconds | Pause between sealing passes |
| `Sealer:BatchSize` | 500 | Records per pass |
| `Sealer:CheckpointInterval` | 1 hour | How often moved chains are verified and their heads signed |
| `Sealer:DataSourceName` | — | The sealer's own data source (role) |
| `Sealer:MaxReadyLag` | 5 minutes | Oldest-unsealed age above which the `audit-sealing` probe reports `Degraded` |
| `SelfCheck` | `Warn` | `Off`, `Warn`, `Fail` |

**Telemetry:** meter and `ActivitySource` `SharedKernel.Persistence.EfCore.Auditing` — append duration, idempotent
duplicates, sealed records, seal duration and lag, verification failures by kind, checkpoints, erased payloads. Logs
use EventIds 6700–6899.

## Security model

**Detects:** edited, deleted, reordered or inserted records; a truncated chain tail (against a checkpoint); records
forged with a retired or compromised key after `SealAllChainsAsync`; a rewritten checkpoint.

**Prevents** (with the role split): updates and deletes of ledger rows by the application and cross-tenant roles; the
application writing seals (with a separate sealer role); trigger bypass through replica mode (the self-check).

**Does not prevent:** a table owner or superuser disabling the triggers (use the role split, and WORM storage for
checkpoints when that threat matters); losing records that were never written because the business transaction
failed before the audit write (the `Failed` record covers handled failures).

Distinct from `SharedKernel.Persistence.EfCore`'s audit columns, which stamp the mutable `CreatedBy`/`ModifiedOn`
columns and keep no history.

## AI quick reference

```text
REGISTER     AddSharedKernelCryptography(configuration) + AddSharedKernelPostgres<T>("name", p => p.UseAuditTrail()).
             Keys in SharedKernel:Persistence:Auditing:{CurrentKeyId, Keys:{id}:{Material, Order}} from a secret store.
MIGRATION    migrationBuilder.CreateAuditLedgerTable(runtimeRole: "app_runtime", sealerRole: "app_audit_sealer")
             + REVOKE UPDATE, DELETE, TRUNCATE ... FROM app_cross_tenant. Never RLS on ledger tables.
AUDIT        Pipeline: command implements IAuditableRequest<TResponse> (Action, ResourceType, ResourceId, snapshots)
             + AddAuditingBehavior(). Manual: unitOfWork.OnBeforeCommit(t => audit.RecordAsync(new AuditEntry {..}, t)).
ACTIONS      Dotted lowercase names: "order.approved". ResourceType = aggregate name. ResourceId = id string.
QUERY        IAuditQueryService.QueryAsync(new AuditRecordQuery { ResourceType, ResourceId }) — caller's tenant only.
VERIFY       VerifyChainAsync(resourceType) -> Status Intact|Broken|Unverifiable, FailureKind, FailedAtSequence.
ERASE        IAuditLedgerMaintenance.EraseResourcePayloadsAsync(type, id, reason) — never DELETE audit rows.
ROTATE       New key with higher Order + CurrentKeyId; keep old keys; compromise -> SealAllChainsAsync(reason)
             in a cross-tenant scope.
SEALER       Separate role app_audit_sealer: AddSharedKernelNpgsql(section "SharedKernel:Persistence:audit-sealer",
             "audit-sealer") + Auditing:Sealer:DataSourceName = "audit-sealer". SelfCheck = Fail in production.
FORBIDDEN    UPDATE/DELETE on audit tables; the owner or a superuser as the runtime role; personal data in Action,
             ResourceId or ErrorCode (only snapshots are erasable).
```

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) · start at the
[persistence overview](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/tree/main/06.Persistence) ·
byte format in [AUDIT-FORMAT.md](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/06.Persistence/SharedKernel.Persistence.EfCore.Auditing/AUDIT-FORMAT.md).
