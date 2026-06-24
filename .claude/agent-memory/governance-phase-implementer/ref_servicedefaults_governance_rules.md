---
name: ref_servicedefaults_governance_rules
description: HealthCheckTagIntegrityRules and CompositionRootExclusivityRules (WO-027 P-173) — new Ldstr literal-collection technique, ConditionList[] pattern, registry gap found
metadata:
  type: reference
---

## HealthCheckTagIntegrityRules (WO-027 P-173)

New Mono.Cecil technique distinct from prior opcode-presence walks: **Ldstr literal collection**.
`NoConflictingLivenessReadinessTagsPredicate`/`DependencyHealthChecksCarryReadyNotLivePredicate`
scope to methods by `MethodDefinition.Name` prefix/suffix match (`Add*HealthCheck`/`Add*ReadinessCheck`,
or caller-supplied prefixes), then walk `method.Body.Instructions` collecting every `OpCodes.Ldstr`
operand into a `HashSet<string>` — no attempt to associate literals with a specific array/argument
position. This is documented as a known limitation (not a defect): dynamically-computed tag values
(e.g. from `IConfiguration`) are invisible to the rule.

`DependencyHealthChecksCarryReadyNotLivePredicate` is constructed with `params string[]` prefixes
(not hardcoded) — reuses `NoConflictingLivenessReadinessTagsPredicate.CollectStringLiterals` as an
`internal static` helper shared between the two predicate classes, avoiding duplicated IL-walk code.

## CompositionRootExclusivityRules (WO-027 P-173)

Returns `ConditionList[]` (not a single `ConditionList`) — one element per forbidden term, in fixed
order: `SharedKernel.Persistence.EfCore`, `.PostgreSQL`, `.Dapper`, `SharedKernel.Messaging.MassTransit`,
`SharedKernel.Security.Oidc`. Pure NetArchTest, no Mono.Cecil — `Types.InAssemblies(assembliesUnderTest)
.That().HaveNameStartingWith(string.Empty).Should().NotHaveDependencyOn(term)` per term. The
composition-root exemption (`SharedKernel.ServiceDefaults`, `SharedKernel.MultiTenancy`, the five
provider packages themselves) is enforced entirely by **caller discipline** — the rule itself has no
exemption logic; the caller simply must never pass exempt assemblies into `assembliesUnderTest`. This
mirrors `CachingAbstractionRules` exactly but that class does have an internal `IsExempt` filter — this
one does not, by design (mirrors `CachingAbstractionRules`'s *structure*, not its exemption-filtering
mechanism). Confirmed acceptable by the pre-written CLAUDE.md spec, which frames it the same way.

## Pre-written CLAUDE.md documentation pattern (confirmed again)

Same pattern observed in WO-026 (see ref_communication_arch_rules.md): the governance-arch-planner
agent had ALREADY written full CLAUDE.md documentation (Architecture Test Contracts entries for both
`HealthCheckTagIntegrityRules` and `CompositionRootExclusivityRules`, plus the two predicate classes,
plus the Changelog entry) before this implementer session started. Verified word-for-word against the
actual implementation — zero discrepancy found, zero CLAUDE.md edits needed. Always check for
pre-written brain content before assuming DO-style tasks require new authoring.

## Phase Key Registry gap found

`SK.00.ServiceDefaultsGovernance` and `SK.00.HealthCheckConstantsGuard` phase keys exist as full phase
sections (with Task Rows, Goal, Scope, etc.) and in the `## Overall Progress` table, but were **never
added** to the `## Phase Key Registry` table near the top of `00.Governance/state-map.md` (lines 21-51).
Per the state-map-phase command's format contract, the Registry must never be edited at runtime by the
implementer — only arch-planner agents may add rows there. When this happens, fall back to: (1) use the
`## Overall Progress` row as authoritative for Total/Done/State: (2) for Root Backlog ID resolution, use
the `P-NNN` token from the phase's own `> **Trigger:** WO-0NN P-NNN.` annotation at the top of the phase
section — confirmed present in the root `state-map.md` Phase Backlog (`### P-173 — ...`) and closeable
the same way Case 1/2 would resolve it had the Registry row existed. Report the registry gap to the user
rather than silently patching the Registry yourself.

## Task-count discrepancy pattern recurs

Same pattern as WO-025/SK.00.CommunicationArchRules (see ref_communication_arch_rules.md): the phase
header text and the "Context" block both claimed 17 tasks for `SK.00.ServiceDefaultsGovernance`, but the
actual Task Rows table has only 16 (D-52, C-71–C-74, T-129–T-138, DO-24). The Overall Progress summary
table's `Total` column is authoritative — always recount the actual table rows rather than trusting
prose mentions of a task count. Corrected both the Overall Progress row and the original "Phase added"
changelog line (changed "17 tasks" → "16 tasks", and adjusted the cumulative running total note from
328→327 to keep the historical math self-consistent for the next phase's "total tasks now N" line).

## Pre-existing Overall Progress total-count drift (NOT fixed — out of scope)

The `## Overall Progress` header note says "Total tasks: 336" but summing every `SK.00.*` row's Total
column actually yields 407 — a pre-existing drift unrelated to this phase, present before this session
started. Did not attempt a full historical re-audit/fix since it's out of scope for a single phase
closure; flagged to the user in the final report instead. A future `sync-brain` or dedicated cleanup
pass should resolve it properly by recomputing every row.

## P-170 (13.ServiceDefaults Core) landed mid-flight

This phase was designed and built entirely against contrived in-memory fixtures because
`SharedKernel.ServiceDefaults` didn't exist yet (per Implementation Rule 8 / Dependencies section). By
the time this implementer session ran, `13.ServiceDefaults` had already reached Published (confirmed via
its own CLAUDE.md changelog and state-map). The real assembly's actual shape
(`HealthCheckTags.Live`/`.Ready` string constants, `Add*HealthCheck` extensions with literal tag arrays
via `tags: [HealthCheckTags.Ready, HealthCheckTags.Redis, HealthCheckTags.Cache]`) matches the fixture
shapes used in this phase's tests almost exactly. The real-assembly re-verification pass is still treated
as a non-blocking follow-up (per the phase's own Dependencies section) and was NOT performed in this
session (would require adding a `ProjectReference` from `SharedKernel.ArchitectureTests.Tests` to
`SharedKernel.ServiceDefaults`) — noted as a recommended near-term follow-up in the closing changelog
entry instead of doing it unprompted, since it wasn't a stated deliverable of this phase.
