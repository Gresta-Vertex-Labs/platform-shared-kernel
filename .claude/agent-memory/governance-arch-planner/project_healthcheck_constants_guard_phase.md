---
name: project_healthcheck_constants_guard_phase
description: SK.00.HealthCheckConstantsGuard (WO-028 P-178) design decisions — generalized magic-string-vs-constants-class guard, third Mono.Cecil technique (field-shape + literal-value resolution)
metadata:
  type: project
---

WO-028 P-178 added phase `SK.00.HealthCheckConstantsGuard` (8 tasks: D-53, C-75–C-77, T-139–T-142, DO-25; total governance tasks now 336) to `00.Governance/state-map.md`, with corresponding additions to `00.Governance/CLAUDE.md`. Depends on P-177 (13.ServiceDefaults — introduces `HealthCheckTags`/`HealthCheckNames` constants classes and the five sibling files that hardcoded literals instead) for **real-assembly verification only** — design/implementation proceeds against contrived in-memory fixtures, same pattern as [[project_servicedefaults_governance_phase]].

**Why this phase exists:** P-177's audit found two generations of the same mistake in one domain — `HealthCheckTags` was built correctly as a constants class, but five sibling files kept hardcoding default health-check *names* as bare literals instead of extending the same discipline. Nothing mechanically caught the inconsistency. Same category of gap as Redis topology (P-145) and composition-root exclusivity (P-173) — a documented invariant with no mechanical backstop.

**Critical distinction from P-173:** `HealthCheckConstantsUsageRules` (this phase) is explicitly ADDITIVE to `HealthCheckTagIntegrityRules` (P-173, see [[project_servicedefaults_governance_phase]]) — never merge them. P-173 enforces tag *semantics* ("live"/"ready" mutual exclusivity). P-178 enforces *source discipline* (bare literal duplicating an existing constant's value). Different concerns, same target assembly (`SharedKernel.ServiceDefaults`).

**Design decisions:**

1. **No new SK ID.** NetArchTest `ICustomRule`, consistent with every other assembly-level governance rule in this domain. Next available sequential-block ID remains **SK0014** (see [[project_sk_diagnostic_registry]]).

2. **Generality requirement (acceptance-critical).** The rule must NOT hardcode `"HealthCheckTags"`/`"HealthCheckNames"` (or any concrete constants-class name) anywhere in the implementation. Detection is by *shape* (a `static` — IL `abstract sealed` — class whose fields are all `const string`/`static readonly string`) and by *value comparison* (does a bare literal at a health-check call site equal a resolved constant's value), never by name. This is what lets the rule generalize to any future domain's constants class without modification.

3. **Third distinct Mono.Cecil technique introduced: field-shape + literal-value resolution (`StringConstantsClassDetector`).** Prior techniques in this domain: (1) opcode-presence — single opcode/method-name match (`NoMakeGenericMethodReflectionPredicate`); (2) Ldstr literal-collection — set of string operands within a scoped *method body* (`HealthCheckTagIntegrityRules`, P-173). This phase's new technique inspects `TypeDefinition.Fields` (not method bodies) to resolve a type's *declared constant values*. `StringConstantsClassDetector` is a reusable helper, NOT itself an `ICustomRule` — lives in `Predicates/` co-located with its sole consumer.

4. **Value comparison, not name comparison.** The predicate compares a bare literal's *value* against the resolved constants' *values* — never against field/class names. This was a deliberate choice over a weaker "any literal + any constants class coexist" shape-only heuristic, which was rejected as too noisy (would fire on every call site in an assembly containing any unrelated string-constants class).

5. **Vacuous pass when no constants class exists.** If `StringConstantsClassDetector` resolves an empty set for the assembly, the rule passes unconditionally — covers any domain before it adopts the constants-class pattern (matches the pre-`HealthCheckTags` "before" state).

6. **Mixed-type constants classes still qualify.** A `static class` with both string and non-string constants still counts as a "string constants class" — only the string-typed fields contribute to the resolved value set; non-string fields are ignored, not disqualifying.

**Follow-up tracked (not blocking phase completion):** confirm the exact `Microsoft.Extensions.Diagnostics.HealthChecks` declaring-type name for the `IHealthChecksBuilder.Add`/`.AddCheck` extension-method host class during implementation (a wrong name silently produces zero matches, not a build error) and record it in `00.Governance/CLAUDE.md`. Also: real-assembly re-verification once P-177/P-170 ship, same precedent as P-145/P-173 closeouts.

Last D/C/T/DO IDs before this phase: D-52, C-74, T-138, DO-24. After this phase: D-53, C-77, T-142, DO-25.
