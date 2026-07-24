---
name: project_wo046_workflows_testing
description: Status of WO-046 (P-288) — 16.Testing's Workflows/ in-memory IWorkflowDispatcher/IWorkflowHandle test doubles for 17.Workflows.
type: project
---

WO-046/P-288 status as of 2026-07-23: `SK.16.Design`/`SK.16.Scaffold`/`SK.16.Core`/`SK.16.Docs` all `●` Complete. Only `SK.16.Tests` (T-55, `SharedKernel.Testing.SelfTests/Workflows/`) remains — `◐` 54/55, unblocked, actionable for a future session.

**Why this took two dispatch cycles:** `17.Workflows/SharedKernel.Workflows.Temporal` was a genuinely empty placeholder `.csproj` when Design/Scaffold were first done (2026-07-22) — the fourth "hard design-ahead-of-schedule" occurrence in `16.Testing`'s history (after Storage/P-269, Search/P-276, Intelligence/P-284). Unlike those three, the blocker did NOT clear within the same Design-confirmation pass — `17.Workflows` hadn't even closed its own `SK.17.Design` yet. It cleared later (17.Workflows's `SK.17.Core` reached `●` 2026-07-23), unblocking this session's Core-phase implementation.

**Files delivered** (`16.Testing/SharedKernel.Testing/Workflows/`):
- `InMemoryWorkflowExecution.cs` — `WorkflowLifecycleStatus` public enum + `InMemoryWorkflowExecution` internal mutable sealed class (Volatile-guarded status/reason, ConcurrentQueue signal/query history, Interlocked-guarded boxed pending result)
- `InMemoryWorkflowDispatcher.cs` — `InMemoryWorkflowStartRecord` + the dispatcher (all 3 `StartAsync` overloads, `GetHandle`/`GetHandle<TResult>`, `DescribeAsync`, `ConfigureQueryHandler<TQueryResult>`, `CompleteWorkflow<TResult>`/`FailWorkflow`, `ShouldHaveStarted*`, `Reset()`)
- `InMemoryWorkflowHandle.cs` — Signal/Query/Cancel/Terminate + assertion helpers
- `InMemoryWorkflowHandle{TResult}.cs` — composes `InMemoryWorkflowHandle`, adds `GetResultAsync`
- `WorkflowServiceCollectionExtensions.cs` — `AddInMemoryWorkflowDispatcher()`, singleton

**Two corrections made against the pre-verification design draft** (see [[feedback_verify_live_source]] for the general lesson): `GetHandle`/`GetHandle<TResult>` throw `ArgumentException` (not `Result.Failure`) since they return `IWorkflowHandle`/`IWorkflowHandle<TResult>` directly, non-`Task`-wrapped; `WorkflowLifecycleStatus` is `public` not `internal` (forced by being the return type of a public property, else `CS0051`). Also added `ConfigureQueryHandler<TQueryResult>` on the dispatcher — the draft implied query handlers via `InMemoryWorkflowExecution.QueryHandlers` but never named a way to populate them.

**Ownership boundary honored:** a separate concurrent session was authoring `17.Workflows/SharedKernel.Workflows.Temporal.Tests/` in the same window — this session touched only `16.Testing/`, never `17.Workflows/`.

**Root state-map.md**: Domain Summary Board row 16 updated to `Tests`/`◐`; Active Work table gained a 16.Testing row (T-55 only remaining task). Root Phase Backlog `P-288` left untouched (Case 3 in state-map-phase's S8a — standard lifecycle phase keys don't get individual Phase Backlog auto-close), matching the WO-044/WO-045 precedent, not the WO-043 one.
