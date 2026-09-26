---
name: project_17_workflows_docs_phase
description: 17.Workflows SK.17.Docs phase status (2026-07-27) — 74/81 tasks complete, doc/NuGet-metadata work, and the two genuine brain-drift corrections found during the DO-06 check.
type: project
---

> WO-086 (2026-09): `IWorkflowServiceProbe`/`WorkflowServiceHealth` were deleted — the probe is the internal `WorkflowServiceProbe : IReadinessProbe` named `"workflows"`, mapped by `AddSharedKernelReadiness()`; `TaskQueueBacklog` was dropped (P-569).

`SK.17.Docs` (DO-01–DO-06) reached `●` on 2026-07-27 — 74/81 tasks complete overall for
`17.Workflows`/`SharedKernel.Workflows.Temporal`. Only `SK.17.Published` (P-01–P-07) remains.

**What shipped this session:**
- `GenerateDocumentationFile`/`TreatWarningsAsErrors`/full NuGet metadata block (incl.
  `PackageReadmeFile` + `<None Include="README.md" Pack="true" PackagePath="\" />` in the *same* edit,
  per the `08.Storage`→`09.Search` precedent this domain's own phase spec named) on
  `SharedKernel.Workflows.Temporal.csproj`.
- `README.md` written from scratch — determinism rule + the `SK0001` inversion table first, both
  `.AsClientOnly()`/worker-hosting setup shapes, a full `TemporalOptions` config table, a worked
  workflow+activity+`CommandActivity<>` example, the 17000–17012 `EventId` table, the `Workflow.Patched`
  five-step lifecycle as an explicit operational procedure, and the payload-codec Web-UI-opacity/
  key-retention trade-offs.
- DO-06 drift check (read every production `.cs` file against `CLAUDE.md`'s Interface Contracts).

**Notable finding: this domain's XML docs were already essentially complete before this phase.**
Core/Tests-phase authoring had already written full `<summary>`/`<remarks>`/`<param>`/`<exception>` docs
on every public member, including every deliberate-omission rationale (no `StartAndWaitAsync`, no
list/search member, deferred `update`, no reason-defaulting `TerminateAsync`, `WorkflowBase`'s
no-constructor-dependencies, `ActivityBase.Heartbeat`'s duplicate-side-effect consequence,
`WorkflowServiceHealth.TaskQueueBacklog`'s permanent-nullability rationale) and
`ITemporalRawClientAccessor`'s capitalized tenant/id-composition bypass warning. Turning on
`GenerateDocumentationFile`+`TreatWarningsAsErrors` produced **zero** CS1591/CS1574 — the only build
failure was one genuine, doc-unrelated `CS8602` (possible null dereference iterating
`Workflow.Info.Headers` directly in `WorkflowPropagationInterceptor.MergeWithWorkflowHeaders`, which is
nullable); fixed with an `if (Workflow.Info.Headers is { } workflowHeaders)` guard, not suppressed.

**Two genuine brain/source drift findings corrected in `17.Workflows/CLAUDE.md` (DO-06):**
1. `TemporalOptions` was documented in Interface Contracts as a `sealed record` with a
   `.DefaultRetryPolicy` member. The shipped type is a plain `sealed class` (ordinary mutable
   Options-pattern shape, `{ get; set; }`), and there is no `.DefaultRetryPolicy` member at all — the
   real member is `.DefaultRetryMaximumAttempts` (`int`, default `5`), which `WorkflowBase.ExecuteAsync`
   folds into a `Temporalio.Common.RetryPolicy` constructed at the call site only when the caller's own
   `ActivityDispatchOptions.RetryPolicy` is null.
2. `ITemporalWorkflowsBuilder.WithWorker`'s `tune` parameter was documented as
   `Action<WorkerTuningOptions>? tune`. The shipped signature is
   `Func<WorkerTuningOptions, WorkerTuningOptions>? tune` — required because `WorkerTuningOptions` is an
   immutable `sealed record`; the caller writes `tune => tune with { MaxConcurrentActivities = 50 }`,
   which an `Action` could never express (there'd be nothing to mutate in place).

Both corrections are a reminder that even a domain with an unusually detailed, pre-verified Design/Core
brain can still drift from shipped source on record-vs-class shape and delegate-type choice — the DO-06
drift-check step (mirroring `08.Storage` DO-07 / `09.Search` DO-08) is not busywork even when the domain
brain already looks authoritative.

**Also backfilled:** the Technology Stack "Testing" row and the matching Test Rules bullet were still
phrased as "VERIFY at Scaffold phase that the download works" even though S-09 had already CONFIRMED it
on 2026-07-23 (see [[reference_temporalio_1170_sdk_shapes]]). Updated both to state the confirmed
finding directly (cache path, ~11ms time-skip of a 30-day delay, no network restriction encountered).

**Verification:** 158/158 tests still passing after the `CS8602` fix; a scratch `dotnet pack` to a
throwaway output dir produced `SharedKernel.Workflows.Temporal.1.0.0.nupkg`/`.snupkg` with zero
`NU5039`/`NU5128` warnings. `SK.17.Published` (P-01–P-07: pack, `consumer-verify` harness for both
`.AsClientOnly()` and worker-hosting compositions, and a real `IHost.StartAsync()` proof of the
missing-config-throws-`OptionsValidationException` guarantee) is the one remaining phase.

See also [[project_17_workflows_core_implementation]] and [[project_17_workflows_tests_phase]] for the
earlier phases' findings.
