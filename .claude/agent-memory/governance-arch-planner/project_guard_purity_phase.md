---
name: project-guard-purity-phase
description: Guard purity enforcement phase (SK.00.GuardPurity) — design decisions, SK0006 assignment, NetArchTest IL inspection approach
metadata:
  type: project
---

Phase SK.00.GuardPurity was added on 2026-05-15 under WO-002 P-004.

**Why:** The two-path guard design (Guard.Against.* returns Error?, Guard.Throw.* throws) only delivers value if the functional path is provably pure. A future contributor adding a throwing guard extension breaks domain constructors relying on collection semantics — failure only surfaces at runtime without enforcement.

**Design decisions:**

- NetArchTest.eNt's standard predicate API (dependency graph) cannot inspect method bodies for throw IL opcodes. The correct approach is a custom `ICustomRule` / `MeetCustomPredicate` that uses Mono.Cecil `MethodDefinition.Body.Instructions` to detect `OpCodes.Throw`.
- If NetArchTest.eNt does not expose `IType.Definition` publicly, add `Mono.Cecil >= 0.11.5` explicitly to `SharedKernel.ArchitectureTests.csproj`.
- `Guard.Throw` exclusion is by full CLR nested-type name (`"Guard+Throw"`) — not namespace prefix.
- SK0006 `GuardClauseThrow` was assigned as the next available ID (SK0005 was last registered).
- SK0006 severity is `Warning` at introduction; escalation to `Error` is gated on confirming Guard+Throw exclusion produces zero false positives.

**Cross-domain dependency:** Implementation tasks C-15, C-16 are blocked on P-003 (IGuardClause, Guard.Against, Guard.Throw types in SharedKernel.Guards must exist before assembly can be loaded).

**How to apply:** When implementing C-15/C-16, verify NetArchTest.eNt version and whether Mono.Cecil is publicly accessible. Check if `IType` exposes `Definition` before deciding whether to add Mono.Cecil as explicit dep. See [[sk-diagnostic-registry]] for next available ID after SK0006.
