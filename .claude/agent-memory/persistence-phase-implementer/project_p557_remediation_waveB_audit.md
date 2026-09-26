---
name: project-p557-remediation-waveB-audit
description: P-557 Remediation Wave B (audit ledger) — what was fixed, reusable techniques, and file-ownership lessons for multi-agent concurrent sessions on 06.Persistence
metadata:
  type: project
---

> WO-086 (2026-09): `05.Application.Behaviors` is now `SharedKernel.Application.Pipeline`; the "13 bridge" and 05's separate `IAuditTrailWriter` are gone — `IAuditTrailWriter`/`AuditEntry`/`AuditOutcome` live in `SharedKernel.Execution.Auditing`, implemented directly by `.EfCore.Auditing`; audit sealing readiness is the `IReadinessProbe` "audit-sealing".

## Context
P-557 is a gold-standard pre-publish refactor of 06.Persistence, run as sequential waves (W1-W7) plus
post-hoc remediation waves after two independent adversarial reviews found gaps the waves' own tests
missed. Remediation Wave A (DI/tenancy) and Wave C (encryption) ran as SEPARATE, PARALLEL agent
sessions alongside this one (Wave B, audit ledger) — all three shared one Docker daemon and, in
Abstractions' case, one file. See [[project_persistence_domain]] for the domain's general patterns
(EF Core gotchas, Testcontainers technique, PublicAPI workflow) — this file is specific to the audit
ledger's own design and to lessons about working in a genuinely concurrent multi-agent session.

## Multi-agent file ownership — what actually worked
Given a strict per-agent folder allowlist (e.g. "own only `EfCore.Auditing/**` and `Abstractions/Auditing/**`,
do not touch `EfCore/**`/`.Encryption/**`/`.Npgsql/**`/`.PostgreSQL/**`/`.Dapper/**`/`Abstractions/Context/**`/`00.Governance/**`"):
- `Abstractions/PublicAPI.Unshipped.txt` is a PACKAGE-ROOT file, not scoped to any one subfolder — it
  is legitimately touched by whichever agent's interface changes affect it, even though it isn't
  literally inside the `Auditing/` folder. The Edit tool surfaced a real, harmless "file modified on
  disk since you last read it" warning when a sibling agent (working on `Abstractions/Context`) was
  editing the SAME file concurrently — the edit still applied cleanly because the two agents' edits
  targeted different lines. Lesson: for a shared root file, use Edit (never Write, which would clobber
  the other agent's concurrent lines) and treat that warning as informational, not a conflict.
- `16.Testing` was NOT in the explicit "do not touch" list, and fixing `FakeAuditQueryService.cs`
  (16.Testing) plus its self-test was a NECESSARY, mechanical consequence of an owned-interface change
  (`IAuditQueryService.ExportRangeAsync`/`VerifyFullChainAsync` signature change) — required to keep
  the build green, which the task explicitly demanded. Touching a file outside the literal owned-folder
  list is fine when (a) it's a direct, mechanical compile-fix forced by an owned change, and (b) the
  task's own "keep build green" instruction requires it. This is different from touching another
  agent's file to FIX a bug in ITS OWN logic (which must be reported, not fixed).
- A full-solution `dotnet build` in a live multi-agent session is NOT a stable signal — it captures
  whatever transient, mid-edit state every other concurrently-running agent happens to be in at that
  exact moment. Twice in one session, a full-solution build surfaced DIFFERENT errors in packages this
  agent never touched (`SharedKernel.Persistence.Npgsql`'s `CrossTenantEscapeToken`/
  `NpgsqlTenantSessionBinder` RS0016/RS0017 mismatch the first time; `00.Governance.ArchitectureTests.Tests`
  referencing a namespace/class name — `EfCorePersistenceBuilderAuditTrailExtensions` — that doesn't
  match the real shipped class name — `EfCorePersistenceBuilderAuditingExtensions` — the second time).
  Neither was caused by, or fixable within, this agent's owned scope. **The reliable signal is building
  and testing the OWNED packages plus their direct downstream consumers in ISOLATION** (here:
  `SharedKernel.Persistence.Abstractions`, `SharedKernel.Persistence.EfCore.Auditing` [+ .Tests],
  `SharedKernel.Testing` [+ SelfTests filtered], `SharedKernel.ServiceDefaults.Persistence` [+ .Tests])
  — a full-solution build is worth running once for a final honest snapshot to report, but its failures
  outside the owned scope should be reported as observed-and-unresolved, never chased.
- Before touching ANY interface a shared package might implement, `grep` the WHOLE repo for every
  call site of the exact member being changed (not just the interface name) — e.g.
  `grep -rn "VerifyFullChainAsync|ExportRangeAsync"` across the whole tree found the real consumer set
  (06's own tests, 16.Testing's fake + its self-test, a README) and, just as importantly, confirmed
  several superficially-plausible hits (`ConsumerVerify`, `Abstractions.Tests`'
  `AuditingContractTests.cs`, a sample app, `05.Application`'s OWN separate `IAuditTrailWriter`) were
  NOT actual call sites of the changed methods — worth the extra grep pass before assuming "N files
  matched" means "N files need fixing."

## Audit-chain design decisions made this session (all shipped, all Postgres-tested)

**C2 — a Succeeded-outcome audit record must never stand alone.** The real fix (driving an actual
ambient transaction through `05.Application.Behaviors.AuditingBehavior`/the 13 bridge) is out of this
package's ownership. The in-scope fix: `EfAuditTrailWriter.RecordAsync` now THROWS
`InvalidOperationException` immediately (before opening any connection) when
`entry.Outcome == AuditOutcome.Succeeded` and `IAmbientDbTransaction.Current` is null — no more silent
standalone-commit fallback. **This is a deliberately breaking, loud behavior change**: until `05`/`13`
actually wire a real ambient transaction around a Succeeded-outcome audited command, EVERY such call in
the real pipeline will throw. That's the correct fail-closed trade-off for a data-integrity bug this
severe, but it means the audit-trail feature is now BLOCKED end-to-end in production usage until that
composition-root wiring lands — flag this prominently to whichever agent/session owns `05.Application`/
`13.ServiceDefaults.Persistence` next.

**C3 — `ExportRangeAsync`/`VerifyFullChainAsync` dropped their `tenantId` parameter entirely**, matching
`GetResourceHistoryAsync`/`GetActorActionsAsync`'s existing shape (tenant resolved from
`ICurrentTenantContext` only). Considered and rejected: keeping the parameter and gating it against
`ICrossTenantScope` when it differs from the caller's own tenant — rejected because the class's own
documented contract already promised "every method except the two explicitly-named AcrossTenants ones"
never takes a tenant parameter, and removing the parameter is strictly safer (no attack surface at all,
vs. a gate that could itself have a bug). `VerifyChainFromCheckpointAsync`'s `checkpoint.TenantId` is
NOT the same hazard — the checkpoint's ECDSA signature is verified before any of its fields are trusted,
so that "caller-supplied" tenant id is authenticated, unlike a bare parameter.

**H5 — `WithAuditTrail()`'s idempotency guard moved off "is `IAuditTrailWriter` already registered"
onto a dedicated internal empty marker class** (`AuditTrailFeatureMarker`). The final two registrations
(`IAuditTrailWriter`, `IAuditQueryService`) switched from `AddScoped` to `TryAddScoped` so a
consumer-pre-registered custom writer still wins resolution while every supporting registration (options
validation, model configurator, immutability interceptor, mutation guard) still happens unconditionally.
This is the general pattern for ANY future `WithX()` builder extension in this domain that needs to be
idempotent AND allow a consumer override — never gate re-entrancy on "is the thing I'm about to
[Try]Add already present," always use a dedicated marker.

**H6 — hash-format break, `AuditRecord.SchemaVersion` 1 → 2, domain separator `"AUDITv1"` → `"AUDITv2"`.**
`AuditRecordHasher.Fields` gained `HashAlgorithm`/`KeyId` (both non-nullable strings, appended after
`PreviousRecordHash`, before `SchemaVersion` — no presence-flag needed since neither is ever null).
The property this actually buys (confirmed by careful pre-implementation analysis, not assumed): it
does NOT stop an attacker who holds some valid key from forging an entirely new, self-consistent record
under that key's own id (nothing but revoking a compromised key stops that). What it DOES stop: RELABELING
an existing, already-correctly-hashed record's `key_id` (or `hash_algorithm`) column to a DIFFERENT
value — even one that resolves to IDENTICAL key material (e.g. two id labels aliased to the same secret
during a rotation window) — without redoing the hash. Pre-fix this relabeling was completely silent
(neither field fed the hash or, for `hash_algorithm`, the key lookup). Tested with a custom
`IAuditChainKeyProvider` test double mapping two distinct ids to the same byte array specifically to
isolate "id was relabeled" from "key material changed."

**H7 — `VerifyChainFromCheckpointAsync` now compares `expectedHead.RecordHash` against what is ACTUALLY
at that sequence today, not just `lastSequenceSeen >= expectedHead.Sequence`.** `VerifyStreamAsync`
gained a `captureHashAtSequence` parameter and returns a third tuple element (`CapturedHeadHash`,
captured only for a record that already passed its own hash/link checks). Degenerate case handled:
`expectedHead.Sequence == checkpoint.Sequence` (the anchor IS the expected head) compares against
`checkpoint.RecordHash` directly rather than trying to capture a hash from an empty "records after the
anchor" stream. Proven with a genuine forged-rewrite test: real HMAC key + real
`AuditRecordHasher`/`CanonicalEncoding` (both `internal`, reachable from the Tests project via the
existing `InternalsVisibleTo` grant) used to build two forged records with DIFFERENT content that still
chain correctly and land on the SAME final sequence as a later, legitimately-signed checkpoint —
pre-fix this reported Intact, post-fix Broken.

**H8 — writer-side mitigation only, per explicit instruction not to touch `SharedKernel.Persistence.Npgsql`.**
New `AuditChainOptions.AdvisoryLockTimeout` (default 5s, `TimeSpan.Zero` = old unbounded-wait behavior,
validated non-negative). `EfAuditTrailWriter` issues `SET LOCAL lock_timeout = 'Nms'` on its OWN
connection/transaction (the `ownsConnection` branch only — NEVER touches an ambient/enlisted
transaction's session settings) immediately after `BeginTransactionAsync`, before acquiring the advisory
lock. `EfAuditTrailWriter`'s constructor gained a new required `IOptions<AuditChainOptions>` parameter
to read this. On SqlState `55P03` (`Npgsql.PostgresErrorCodes.LockNotAvailable`, confirmed matches this
package's own hardcoded `"55P03"` constant by a real Postgres-backed test), logs a new
`AdvisoryLockTimedOut` event instead of the generic failure path. Proven with a real two-connection
deadlock scenario: connection 1 holds an ambient transaction's advisory lock via a Succeeded write;
connection 2 (same chain, Failed outcome, own connection) blocks; test bounds its OWN wait via
`Task.WhenAny(recordTask, Task.Delay(20s))` against a 2s configured timeout — completes in ~2s, asserts
`PostgresException` with the right SqlState. **The actual root-cause fix (making `NpgsqlAdvisoryTransactionLock`
itself take a timeout, or restructuring so this class of nested-same-chain-different-outcome call can't
happen) still belongs to whoever owns `SharedKernel.Persistence.Npgsql` — this is a mitigation, not a
structural fix, and should be reported as such.**

**Mediums, all fixed and tested:**
- `AuditRecordMutationGuardInterceptor`'s regex widened from `UPDATE|DELETE FROM` to also match
  `TRUNCATE [TABLE]`, `DROP TABLE`, `ALTER TABLE`, an optional `ONLY` keyword, and optional schema
  qualification (`"schema"."table"`) — pattern:
  `\b(?:UPDATE|DELETE\s+FROM|TRUNCATE(?:\s+TABLE)?|DROP\s+TABLE|ALTER\s+TABLE)\s+(?:ONLY\s+)?(?:""?[A-Za-z_][\w$]*""?\s*\.\s*)?""?{table}""?(?!\w)`.
  Two non-obvious regex traps avoided: (1) do NOT end with a literal `\b` after an optional trailing
  quote — `"` and a following space/EOF are BOTH non-word characters, so `\b` never matches there;
  used `(?!\w)` instead, which works whether or not the table name was quoted. (2) the interceptor only
  overrode `NonQueryExecuting(Async)` — added `ReaderExecuting(Async)`/`ScalarExecuting(Async)` overrides
  too, since a RETURNING-clause statement (or any raw query path) doesn't necessarily go through the
  non-query path. Tested by issuing raw SQL THROUGH the DbContext (`context.Database.ExecuteSqlRawAsync`)
  for every named case (TRUNCATE, DROP TABLE, ALTER TABLE DISABLE TRIGGER, UPDATE ONLY, schema-qualified
  DELETE) — distinct from the pre-existing `DatabaseTrigger_*` tests, which prove the SEPARATE,
  mandatory DB-trigger layer via a raw `NpgsqlConnection` that bypasses the app/interceptor entirely.
  Known, documented residual gap: a single `TRUNCATE`/`DROP` naming SEVERAL tables where the audit
  table isn't the FIRST one listed — this is a text-scan heuristic, not a SQL parser, and the class's
  own XML doc now says so honestly instead of over-claiming "any generated UPDATE or DELETE."
- Idempotency-key reuse for a genuinely different event (different Action/ResourceType/ResourceId/Outcome)
  now throws `InvalidOperationException` instead of silently returning the FIRST record under that key —
  checked at both places `existing is not null` is handled (pre-loop AND the in-loop retry path).
- Savepoint leak: `EfAuditTrailWriter` now calls `transaction.ReleaseAsync(SavepointName, ...)` on the
  SUCCESS path (right after `InsertAsync` succeeds) AND immediately after `RollbackAsync` on the
  conflict-retry path — previously a savepoint was created every attempt and NEVER released, only
  rolled-back-to, so a busy chain accumulated one un-released Postgres subtransaction per retry across
  the WHOLE lifetime of one ambient transaction.
- `AuditingLog`'s five EventIds moved from bare literals (`6400`..`6404`) to
  `LoggingEventIdRanges.Persistence + 400`..`+404` (`using SharedKernel.Primitives.Logging;`), matching
  the sibling `EncryptionLog`'s exact pattern — added `+406` for the new `AdvisoryLockTimedOut`.
  `FailureAuditWriteFailed` (`+405`) was defined-but-dead pre-session; now genuinely called from the
  writer's catch block whenever `entry.Outcome == AuditOutcome.Failed` and the write itself throws
  (proven with a real Postgres `22001` "value too long" error via an oversized `ResourceType` string —
  the real `varchar(200)` column constraint, not a simulated failure).
- `EfAuditTrailWriter`'s unique-constraint-retry fallback documented (not code-changed) as correct only
  under READ COMMITTED — a stricter ambient-transaction isolation level would keep re-reading the same
  stale snapshot every retry. Chose documentation over a runtime isolation-level check/workaround given
  the narrow blast radius (only affects a `Succeeded` write inside an EXPLICITLY-stricter ambient
  transaction contending with a concurrent appender on the same chain) and the added complexity/risk of
  a runtime fix within this wave's time budget.

**Deliberately NOT done, and why:**
- The PostgreSQL migration-trigger's `ENABLE ORIGIN` → `ENABLE ALWAYS` fix (closes the
  `session_replication_role='replica'` bypass) lives in `AuditImmutabilityMigrationBuilderExtensions`,
  `SharedKernel.Persistence.PostgreSQL` — out of this session's owned scope, reported not fixed.
- No dedicated ">64 savepoints in one transaction" load test — the fix itself is straightforward and
  obviously correct (release what you create), and engineering a deterministic 64+-retry scenario
  without real contention would be artificial; the existing concurrent-writer tests already exercise the
  retry-then-release path repeatedly and passed.
- `FailureAuditWriteFailed`'s test proves the code path is genuinely LIVE (throws + rolls back cleanly
  on a real DB error) but does not assert the specific log record — wiring `16.Testing`'s
  `AddInMemoryLoggerFactory()` into `AuditTestHost.Build` would require first `RemoveAll<ILoggerFactory>()`
  (the host already calls plain `AddLogging()`, which uses ordinary `Add`, so `AddInMemoryLoggerFactory()`'s
  `TryAddSingleton` is a no-op layered on top of it) — judged not worth the added complexity for this wave.
