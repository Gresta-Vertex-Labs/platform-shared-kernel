---
name: project_metrics_outcome_tag_phase
description: SK.00.MetricsOutcomeTagAndMisregistrationGuard (WO-038 P-235) design decisions — SK0014-SK0016, MetricsInstrumentationRules, and the explicit out-of-jurisdiction MetricsBehavior retrofit
metadata:
  type: project
---

> WO-086 (2026-09): SK0015 was deleted (ID retired) — the kernel pipeline has no MediatR stream-behavior registration to misregister. `SharedKernel.Application.Behaviors` is now `SharedKernel.Application.Pipeline`. SK0014, SK0016 and `MetricsInstrumentationRules.RequestDurationRecordsIncludeOutcomeTag` still exist.

WO-038 P-235 added phase `SK.00.MetricsOutcomeTagAndMisregistrationGuard` (15 tasks: D-57,
C-90–C-94, T-161–T-168, DO-29; total governance tasks now 388) to
`00.Governance/state-map.md`, with corresponding additions to `00.Governance/CLAUDE.md`. It
addresses four WO-038 audit findings in `05.Application`, all of which were already shipped
(P-217, P-231, P-234 complete per `05.Application/state-map.md` as of 2026-07-02) — unlike most
prior governance phases in this file, this one was NOT designed ahead of its triggering domain.

**Why this phase exists:** same standing rationale as every prior phase here — a documented
finding decays without mechanical enforcement. WO-038's audit found four concrete, recurring
mistake shapes in the extended `05.Application` pipeline (resilience, metrics, streaming
registration, key construction) that will resurface as new behaviors ship.

**Design decisions:**

1. **Three new SK IDs assigned: SK0014, SK0015, SK0016** (see [[project_sk_diagnostic_registry]]
   for full descriptors). Next available sequential-block ID after this phase is **SK0017**.
   - SK0014 `ClosedGenericResiliencePipelineRegistration` — Roslyn syntax-only. Fires on any
     arity-1 `ResiliencePipeline<T>` generic-name usage (DI registration, parameter, field,
     local) anywhere, no namespace exemption — the closed-generic anti-pattern is unsafe in any
     assembly, not just `05.Application`.
   - SK0015 `StreamPipelineBehaviorMisregistration` — the domain's **second** semantic-model
     Roslyn analyzer (after SK0011). A naming-heuristic approach (mirroring SK0708's
     `"BatchConsumer"` substring convention) was deliberately rejected: the five known streaming
     behavior names (`Stream*Behavior`) are a naming convention, not a structural guarantee, so
     `SemanticModel.GetSymbolInfo` + `ITypeSymbol.AllInterfaces` resolves whether a DI
     registration's second type argument actually implements `MediatR.IStreamPipelineBehavior<,>`.
     Self-exemption (method named `AddStreamingBehaviors`) stays syntax-only and short-circuits
     before the semantic-model call.
   - SK0016 `RequestTypeShortNameUsage` — Roslyn syntax-only, but notable as the **first
     trigger-IN namespace-scoped rule** in this domain: it fires only INSIDE
     `SharedKernel.Application`/`SharedKernel.Application.Behaviors`, the inverse of the usual
     trigger-everywhere-except-exemption shape (SK0001/SK0007/SK0013). Closes the mechanical-
     enforcement gap left by the P-231 manual fix (`typeof(TRequest).FullName ?? .Name`).

2. **`MetricsInstrumentationRules.RequestDurationRecordsIncludeOutcomeTag`** — NO new SK ID
   (NetArchTest `ICustomRule`, consistent with the `HealthCheckTagIntegrityRules` precedent of
   SK-less rules for instrumentation-completeness checks). Reuses the `Ldstr` literal-collection
   IL technique from `HealthCheckTagIntegrityRules` (WO-027 P-173) against a NEW call-site
   search target: `Histogram<T>.Record` (first use of this search target in the domain). Backed
   by `RequestDurationRecordMissingOutcomeTagPredicate` in `Predicates/`.

3. **Explicit jurisdiction boundary — the most important decision in this phase.** The WO-038
   phase input's acceptance criteria asked for the non-streaming `MetricsBehavior<,>` (P-217) to
   be "retrofitted" to emit the `"outcome"` tag "in the same pass." That retrofit is 05.Application
   PRODUCTION CODE — strictly outside this agent's jurisdiction (`00.Governance` writes planning/
   enforcement artifacts only, touches only `00.Governance/`, never implementation files for
   another domain). Resolution: `MetricsInstrumentationRules` ships the mechanical rule, designed
   and tested against CONTRIVED in-memory fixtures ONLY — it is explicitly documented (in
   `CLAUDE.md`, in `state-map.md`, and here) as EXPECTED TO FAIL if pointed at the real
   `SharedKernel.Application.Behaviors` assembly until a companion `05.Application` work order
   retrofits `MetricsBehavior<,>`. This is recorded as a genuine, populated entry in
   `00.Governance/state-map.md`'s "Cross-Domain Dependencies" section (the first non-empty entry
   there — every prior phase left that section as "No active cross-domain dependencies").

4. **Baseline before this phase:** 125/125 architecture tests (SK.00.CryptoDelegationAndUowSeamGuard
   closeout, 2026-07-02). This phase adds 2 new architecture tests (T-167/T-168, contrived
   fixtures only) and 6 new analyzer tests (T-161–T-166) — none should turn the existing suite
   red, since the new NetArchTest rule is never pointed at a real assembly in this phase.

**How to apply:** When a future `05.Application` work order retrofits `MetricsBehavior<,>` to add
the `"outcome"` tag, the governance implementer should (a) re-point
`MetricsInstrumentationRules.RequestDurationRecordsIncludeOutcomeTag` at the real
`SharedKernel.Application.Behaviors` assembly as a follow-up verification pass (same pattern as
the P-170/P-227/P-228 precedents), and (b) update the Cross-Domain Dependencies row in
`00.Governance/state-map.md` from `○ Pending` to `●` once that retrofit ships and the real-assembly
check passes.

Last D/C/T/DO IDs before this phase: D-56, C-89, T-160, DO-28. After this phase: D-57, C-94,
T-168, DO-29. Total governance tasks: 373 → 388.
