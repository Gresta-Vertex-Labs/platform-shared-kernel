---
name: WO-009 Design Phase Closure Pattern
description: Recurring pattern (WO-009, WO-051) — arch-planner pre-writes CLAUDE.md design content in the same pass that creates state-map tasks; implementer's job is independent re-verification against shipped .cs files, not re-implementation
type: project
---

SK.03.Design tasks D-15..D-18 (WO-009) and D-34..D-42 (WO-051, P-307..P-313) were both closed the same way: domain-arch-planner had already written the full design content into 03.Domain/CLAUDE.md in the same session/pass that added the state-map task rows. When domain-phase-implementer was dispatched to complete the Design phase, the correct action both times was to verify CLAUDE.md already contains accurate designs and mark state-map tasks ● — no CLAUDE.md content edits.

**Why:** The arch-planner writes both the CLAUDE.md content and the state-map task rows simultaneously in this codebase's workflow; the implementer's job for a Design phase is verification + bookkeeping only, never re-authoring content that's already there.

**Critical nuance (confirmed in WO-051):** "verify" must mean independently re-reading the actual shipped `.cs` files the design text makes claims about — never trusting the arch-planner's own changelog claim that something was "verified against shipped source." WO-051's phase spec explicitly named four load-bearing claims to re-check (AndSpecification/OrSpecification/NotSpecification's current Includes/StringIncludes propagation behavior, tenanted-aggregate constructor chaining to `base(id, clock)`, AndBusinessRule.Message's `string.Join("; ", ...)` shape, ValueObject.cs's current XML `<example>` block) plus SharedKernel.Guards' actual `Guard.Throw.Null<T>` signature. All were read directly from source (not grepped/assumed) and all matched CLAUDE.md exactly — but treat this as something to actually check each time, not skip because the pattern held twice before.

**How to apply:** For any Design phase task where the arch-planner already updated CLAUDE.md: (1) read every shipped .cs file the design text makes a factual claim about, (2) if all claims hold, mark state-map tasks ● with zero CLAUDE.md content edits, (3) call sync-brain anyway — it should append exactly one CLAUDE.md changelog line noting what was independently re-verified and that no content edits were needed. If a claim is found to be wrong, correct the CLAUDE.md design text (with a call-out) rather than silently propagating the error into the Core phase.
