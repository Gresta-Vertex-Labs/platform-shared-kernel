---
name: feedback_core_tests_phase_boundary
description: Whether to write xUnit tests during 08.Storage's Core phase given the state-map splits Core and Tests into separate phases
type: feedback
---

08.Storage's `state-map.md` splits implementation and test-writing into two separate, independently-dispatched phases: `SK.08.Core` (C-01–C-30, all "Implement"/"Confirm" tasks, zero test-writing tasks) and `SK.08.Tests` (T-01–T-17, full unit + Testcontainers-MinIO provider-round-trip coverage, already fully specified in detail). When implementing a Core-phase dispatch, do **not** write the Tests-phase xUnit suite even though the storage-phase-implementer agent's own persona/system-prompt has a detailed generic "Testing Workflow" section that reads as if every phase should end with test-writing.

**Why:** The agent identity's hard constraints say "Implement only what the current phase asks for — nothing more, nothing less" and "Never add features... or anticipate future phases." The dispatched Core-phase spec listed only C-01–C-30 with no test tasks. Writing the T-01–T-17 suite during Core would violate those constraints and duplicate/preempt a phase that's already fully planned in fine-grained detail (down to exact Testcontainers MinIO round-trip assertions). The prior Scaffold-phase session in this same domain set the precedent: it produced zero test-body logic, only `GlobalUsings.cs` stubs, deferring everything to later phases.

**How to apply:** Treat the generic "Testing Workflow"/"Execution Order" sections of the persona prompt as describing *what tests should look like when the dispatched phase actually calls for them* — not as an unconditional mandate to write tests regardless of the phase spec's own task list. Before writing tests during any 08.Storage phase, check whether the domain's own state-map already reserves a separate, later Tests phase covering the same package — if so, defer to it and instead spend the session on build verification (`dotnet build`) and the phase's own literal "Confirm..." tasks (grep-based checks, singleton-registration checks, etc.), which read as build/inspection tasks, not xUnit-writing tasks.

If a future dispatch is ambiguous about this (e.g. a phase spec that doesn't cleanly separate impl vs. tests), default back to writing tests per the persona's Testing Workflow section — this deferral is specific to domains (like 08.Storage) that have already carved out an explicit separate Tests phase key.
