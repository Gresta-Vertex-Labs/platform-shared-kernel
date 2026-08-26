---
name: project_p464_wo073_initial_design
description: P-464/WO-073 first-dispatch Design decisions for SharedKernel.Scheduling — Quartz pin resolution, job execution model, and cross-domain blockers
type: project
---

19.Scheduling was founded and given its first phase input (P-464/WO-073) on 2026-08-26. This session populated all six state-map phase sections (46 tasks: D-01–D-08, S-01–S-05, C-01–C-09, T-01–T-07, DO-01–DO-05, P-01–P-03) and resolved one open Design question.

**Quartz dependency — resolved, direct pin required.** Verified on disk (not assumed) by reading `07.Messaging/SharedKernel.Messaging.MassTransit/obj/project.assets.json`: `MassTransit.Quartz` 9.1.2 (the repo's only `Quartz*` `PackageVersion` entry at the time) transitively resolves `Quartz/3.18.1`. Decided `19.Scheduling` must not ride that transitive edge — `07.Messaging` can bump/drop `MassTransit.Quartz` independently and silently break this domain's compile-time-visible `CronExpression` usage. Root `Directory.Packages.props` needs its own `<PackageVersion Include="Quartz" Version="3.18.1" />`. That edit is outside `19.Scheduling/`'s jurisdiction (it's a root file) — recorded as a Scaffold-phase (S-03) cross-domain blocker for whoever executes it (likely `devops-lead`).

**Job execution model locked at Design** (see `19.Scheduling/CLAUDE.md`'s "Job Execution Model" section for the durable record — this memory is a pointer, not a duplicate):
1. Registration takes `Func<ScheduledJobExecutionContext, TCommand>` (a factory), not a bare `TCommand` — a fired job has no external caller to supply one, unlike `17.Workflows`' `CommandActivity<TCommand>`.
2. `OverlapPolicy` (in-process, guards a still-running job) and `MisfirePolicy` (in-process, compares `IClock.UtcNow` against the loop's own last-known fire time — no persisted store) are separate, additive protections, neither a substitute for `IFencedLock` cross-replica exclusivity.
3. `ScheduledJobExecutionContext.FencingToken` is exposed for a job body to *propagate* to a downstream fenced resource — never re-checked internally, since this domain owns no protected resource of its own.

**Why this matters for future sessions:** P-465 (`13.ServiceDefaults` `WithSchedulingTelemetry`), P-466 (`AddSchedulerReadinessCheck`, carries the new `13→19` grant), and P-467 (`16.Testing` fake) all read `19.Scheduling/CLAUDE.md` as their source of truth — the command-factory signature and the `ActivitySource`/`Meter`/probe names must stay stable once those phases start.

**Cross-domain blockers still open (as of 2026-08-26), check before continuing implementation:**
- Root `Directory.Packages.props` Quartz pin not yet added (blocks S-03).
- `01.Core`'s `LoggingEventIdRanges` `Scheduling = 19000` is design-locked (`core-arch-planner`, phase key `SK.01.LoggingRangesNewDomains`) but not yet implemented (blocks C-07, C-09 — no `[LoggerMessage]` method may be authored until it ships).

Verify both are still open by re-reading `state-map.md`'s Cross-Domain Dependencies table before assuming — they may have shipped since this memory was written.
