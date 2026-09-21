# SharedKernel.Persistence.EfCore.Auditing

A tamper-evident, append-only audit ledger on PostgreSQL. Commands record who did what to which resource;
a background sealer chains every record into a per-`(tenant, resource type)` chain with a keyed MAC; signed
checkpoints stored outside the chain catch truncation and rewrites; snapshots can be erased for GDPR/KVKK
without breaking the chain. The byte-level format is specified in [AUDIT-FORMAT.md](AUDIT-FORMAT.md), with
test vectors.

Distinct from `SharedKernel.Persistence.EfCore`'s `AuditInterceptor`, which only stamps mutable
`CreatedBy`/`ModifiedOn` columns and keeps no history.

## How it works

| Step | What happens | Cost on the request |
|---|---|---|
| Write | `IAuditTrailWriter.RecordAsync` runs one `INSERT` (record + erasable payload). A **Succeeded** entry goes into the caller's transaction, so it commits or rolls back with the business write; a **Failed** entry commits on its own connection. No sequence, hash, lock or retry. | one statement |
| Seal | The sealer (a hosted service; every instance runs it, one seals at a time via `pg_try_advisory_xact_lock`) takes committed records whose inserting transaction is older than the oldest running transaction, in `(insert_xid, id)` order, and appends a link: sequence, previous MAC, HMAC-SHA256 MAC. A long transaction that commits late is never skipped or sealed out of order. | none |
| Checkpoint | Every `Sealer:CheckpointInterval` the sealer verifies each chain that moved (from its last authentic checkpoint) and signs its head with an asymmetric key (`IAsymmetricSignatureService`) into an `IAuditCheckpointSink` (default: the `audit_checkpoints` table; use WORM storage for stronger guarantees). | none |
| Verify | `IAuditQueryService.VerifyChainAsync` / `VerifyChainFromCheckpointAsync` return `Intact`, `Broken` or `Unverifiable` with a failure kind: `SequenceGap`, `HashMismatch`, `LinkMismatch`, `KeyRegression`, `AnchorMismatch`, `TailTruncated`, `UnknownKey`, `PayloadErased`. | — |

## Setup

```csharp
services.AddSharedKernelCryptography(configuration)   // IHmacSigner (+ .AddAsymmetricSigning() for checkpoints)
    .AddAsymmetricSigning();
services.AddSharedKernelNpgsql(configuration);          // IDbConnectionFactory
services.AddSharedKernelEfCore<OrderDbContext>(o => o.UsePostgreSQL(sp))
    .UseAuditTrail(configuration)                       // or WithAuditTrail(configuration)
    .Build();
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

Options are validated at startup: the current key must exist and be the newest (highest `Order`), every key
must decode to at least 32 bytes, orders must be distinct. Register your own `IAuditRecordAuthenticator` (for
example a KMS that computes MACs remotely) or `IAuditCheckpointSink` before `UseAuditTrail` to replace the
defaults; the keyring is then not required.

Create the tables in a migration (they are not part of the EF Core model):

```csharp
protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.CreateAuditLedgerTable();
protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropAuditLedgerTable();
```

`AuditLedgerSchema.CreateScript` holds the same DDL (idempotent) for tooling and tests. PostgreSQL 15+.

## Writing

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

`05.Application`'s `AuditingBehavior` does this for every `IAuditableRequest`, and writes the **Failed** entry
after a rollback. Identity (user, actor kind, client, session, impersonator, tenant) comes from `IRequestContext`,
the source service from `PersistenceServiceOptions.ServiceName`, the correlation and W3C trace ids from the
ambient `Activity`. Field lengths are checked before any SQL (`AuditFieldLimits`), so an oversized value throws
`ArgumentException` and never aborts the business transaction. A record without a tenant (the system chain) is
only accepted from an authenticated system identity or inside a cross-tenant scope. A reused `IdempotencyKey`
returns the stored record; reused for a different event it throws.

## Reading, exporting, erasing

* `QueryAsync(new AuditRecordQuery { ResourceType = "Order", ResourceId = id })` — the caller's tenant, keyset
  paging (`Cursor`/`NextCursor`), every shape index-backed.
* `QueryAcrossTenantsAsync(...)` and `ExportRangeAsync(...)` write their own `AuditLedger` record first
  (`AuditLedgerActions`). The cross-tenant query needs an active `ICrossTenantScope`.
* `IAuditLedgerMaintenance.ErasePayloadAsync(recordId, reason)` / `EraseResourcePayloadsAsync(type, id, reason)`
  delete the snapshots and their salt, record the erasure, and leave the chain verifiable (the chain commits to
  `SHA-256(salt ‖ payload)`). Verification counts erased payloads, or reports `PayloadErased` when payloads are
  required.

## Keys: rotation and compromise

1. Add the new key with a higher `Order`, set `CurrentKeyId` to it, deploy. New seals use it; old records keep
   verifying with the old key, which must stay in `Keys` for as long as those records must verify (removing it
   makes them `Unverifiable` / `UnknownKey`).
2. If the old key leaked, run `IAuditLedgerMaintenance.SealAllChainsAsync("reason")` inside a cross-tenant scope
   right after rotating. Every chain gets a marker sealed under the new key and a fresh checkpoint; any record an
   attacker later forges with the old key is reported as `KeyRegression`, and any re-sealed old record as
   `LinkMismatch`.
3. Checkpoint signing keys rotate the same way: add the old id to `AcceptedCheckpointSigningKeyIds`.
   Verification never trusts the key id a checkpoint names on its own.

## Database roles (required for the guarantees)

The triggers make the tables append-only for every role **except** the table owner and superusers, who can
disable them. Own the tables with a migration role and run the application under a separate role:

```sql
-- once, as an administrator
CREATE ROLE app_migrator LOGIN PASSWORD '...';          -- runs migrations, owns the ledger tables
CREATE ROLE app_runtime  LOGIN PASSWORD '...' NOSUPERUSER NOBYPASSRLS;
GRANT CONNECT ON DATABASE orders TO app_runtime;
GRANT USAGE ON SCHEMA public TO app_runtime;

-- after migrationBuilder.CreateAuditLedgerTable() ran as app_migrator
GRANT SELECT, INSERT ON audit_records, audit_chain_links, audit_checkpoints TO app_runtime;
GRANT SELECT, INSERT, DELETE ON audit_record_payloads TO app_runtime;   -- DELETE = payload erasure
```

Never grant `UPDATE`, `TRUNCATE`, or `DELETE` on the other three tables. The ledger tables must not use
row-level security: the ledger filters by tenant itself, and the sealer must read every tenant's records.

The startup self-check (`SelfCheck`: `Off`, `Warn` (default), `Fail` — use `Fail` in production) reports a
superuser runtime role, a runtime role that owns (or is a member of the owner of) a ledger table, any
`UPDATE`/`DELETE`/`TRUNCATE` privilege on it, row-level security on it, and any missing trigger or trigger not
`ENABLE ALWAYS` (which `session_replication_role = replica` would silently bypass).

The sealer holds its lock with a transaction-scoped advisory lock, so it works behind a transaction-mode pooler.

## Health and telemetry

* `IAuditSealingProbe.ProbeAsync()` returns the unsealed record count and the age of the oldest one — a
  readiness/alerting primitive; no `IHealthCheck` is shipped.
* Meter and ActivitySource `SharedKernel.Persistence.EfCore.Auditing` (`AuditingMeter`): append duration,
  idempotent duplicates, sealed records, seal duration and lag, verification failures (tagged with the failure
  kind), checkpoints, erased payloads. Logs use EventIds 6700–6899.

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) — see
[06.Persistence/CLAUDE.md](../CLAUDE.md).
