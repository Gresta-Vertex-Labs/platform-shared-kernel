# 19.Scheduling — State Map

> Living board for this domain: what exists and what is open. Completed work is not kept here; `git log` is the record.

## Legend

○ Not started · ◐ In progress · ● Done · ⚑ Blocked · ⊘ Declined / superseded

## Package Board

| Package | Tier | Status | Notes |
| --- | --- | :---: | --- |
| `SharedKernel.Scheduling` | Adapter | ● | P-464/WO-073, one package by design (single provider). `IScheduledJobRegistry`, `ScheduledCommandJob<TCommand>` (kernel `ISender`), mandatory `MisfirePolicy`/`OverlapPolicy`, cross-replica single execution through an optional `IDistributedLockService` per-occurrence lease (fencing token in `ScheduledJobExecutionContext.FencingToken`), each run inside a `SystemRequestContext` scope with the job's `TenantScope` (default `TenantScope.Global`) and a new correlation id, `scheduler` probe. Quartz's `CronExpression` for parsing only (Quartz pinned directly in `Directory.Packages.props`). Verified by `consumer-verify`. |

Test double: `InMemoryScheduledJobRegistry` in `src/Infrastructure/Scheduling/SharedKernel.Scheduling.Testing`.

## Phase Key Registry

| Phase key | Phase | Status |
| --- | --- | --- |
| `SK.19.OneReplicaGuidance` | One-replica guarantee: recipe, needs-table row, jobs-aware missing-lock warning | `○` |

Closed keys live in `git log`: check a new key is unused with `git log --oneline -S"SK.NN.Key"`.

## Open Work

### SK.19.OneReplicaGuidance — One-replica guarantee made first-class `○`

**Work order:** WO-090 / P-633 · **Depends on:** SK.11.ServiceContractsRecipe (P-631, sequencing only: both append rows to the consumer needs table, neither edits an existing row)

A request to run a job on one replica used the scheduler but skipped the Redis lock, because the recipe mentions the lock without leading with it. The phase makes the guarantee the first statement of the `add-background-job` recipe, gives it its own needs-table row, and tightens the warning that already exists. Finding: the scheduler already takes an *optional* `IDistributedLockService` from `SharedKernel.Caching.Abstractions` (rule 15 holds, no tier or dependency change) and `SchedulingHostedService.StartAsync` already logs `SingleReplicaWarning` (19002, Warning, sub-block 0) when none is registered. So no new EventId and no new type: the warning is made job-aware (logged only when at least one job is registered, so a host that adds the scheduler and no jobs is not warned) and its text names the remedy. It never fails startup, so a 1.0.0 single-replica service is unaffected; the only observable change is the log line (no longer emitted with zero jobs, new wording). Public API, options, lease, misfire and overlap are untouched.

| ID | Task | Package | State |
| --- | --- | --- | :---: |
| D-01 | Ratify: keep EventId 19002 and `SingleReplicaWarning`; gate it on `_registry.Definitions` being non-empty (the registry is composition-time, so the check at `StartAsync` is complete); template stays one sentence (L3) and names the remedy generically ("register an `IDistributedLockService`, for example with `AddRedisDistributedLocking`"); no package reference, no startup failure, loop-scope emission unchanged (rule 17) | SharedKernel.Scheduling | `○` |
| C-01 | Move the `_lockService is null` check after the job count is known and guard it with it; update the `[LoggerMessage]` text in `SchedulingLog` | SharedKernel.Scheduling | `○` |
| T-01 | Extend `NoDistributedLockServiceRegistered_LogsSingleReplicaStartupWarning`: one job, no lock service, exactly one 19002 Warning, via the `SharedKernel.Testing.Telemetry` log helpers | SharedKernel.Scheduling.Tests | `○` |
| T-02 | No jobs registered and no lock service: no 19002 record; lock service registered (`SharedKernel.Caching.Testing`) with jobs: no 19002 record | SharedKernel.Scheduling.Tests | `○` |
| T-03 | Update the 19002 template pin and its loop-scope classification in `Hosting/SchedulerLogContextTests` (the scenario must register a job) | SharedKernel.Scheduling.Tests | `○` |
| DO-01 | `SharedKernel.Scheduling/README.md`: state that without a lock service every replica fires every occurrence, that the startup warning appears when jobs are registered, and name `AddRedisDistributedLocking()` as the fix | SharedKernel.Scheduling | `○` |
| DO-02 | `add-background-job.md`: open **Use when** and step 1 with the one-replica guarantee (exactly once per occurrence across replicas comes from the per-occurrence lease in `IDistributedLockService`; register the Redis lock or every replica runs every occurrence), move the lock wiring snippet (`AddRedisConnection(configuration).AddRedisDistributedLocking()` plus `AddSharedKernelScheduling()`) into the first registration step so the example shows it, mention the startup warning in **Check**, keep `proven-by` paths valid | consumer skill (`consumer/skill/sharedkernel/recipes`) | `○` |
| DO-03 | `SKILL.md` needs table: append a new row "Run on one replica only" naming `SharedKernel.Scheduling` and `SharedKernel.Caching.Redis.Core` + `SharedKernel.Caching.Redis.DistributedLocking`, tier Infrastructure, entry points `AddSharedKernelScheduling()` and `AddRedisConnection(…).AddRedisDistributedLocking()`, recipe `add-background-job.md`; add it directly after the existing job row without editing any existing row (P-629 touches none, P-631 adds two new rows); then `dotnet run eng/generate-package-index.cs` and the consumer-skill checks | consumer skill (`consumer/skill/sharedkernel`) | `○` |

**Acceptance:**
- [ ] `add-background-job` opens with the one-replica guarantee and what provides it, and its example shows the lock wiring.
- [ ] The needs table has a "Run on one replica only" row naming both packages and the recipe; no existing row changed.
- [ ] No lock service plus at least one job logs 19002 once at start; no jobs, or a registered lock service, logs nothing; startup never fails; EventId and level unchanged, next free stays `+18`.
- [ ] No public API, option, lease, misfire or overlap behavior changes; `PublicAPI.Unshipped.txt` untouched; a 1.0.0 consumer registration behaves identically.
- [ ] `SharedKernel.Scheduling.Tests` and the consumer-skill checks are green; `SharedKernel.Scheduling` still references `Caching.Abstractions` only.

## Blocked

None.

## Cross-Domain Dependencies

None open.
