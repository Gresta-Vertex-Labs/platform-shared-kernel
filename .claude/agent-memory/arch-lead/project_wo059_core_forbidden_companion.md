---
name: wo059_core_forbidden_companion
description: WO-059 — companion 01.Core phase (P-384) adding Error.Forbidden/ErrorType.Forbidden to unblock WO-058's 05.Application/14.Presentation phases
type: project
---

WO-059 dispatched P-384 (01.Core, purely additive `ErrorType.Forbidden` + `Error.Forbidden(code, message)` factory, mirroring `BusinessRule`'s shape) after the user reported — and this session independently re-verified against real shipped source — that `05.Application` (WO-058/P-380, `DualApprovalBehavior`) and `14.Presentation` (WO-058/P-381, `[RequireRole]`/`[RequirePermission]`) both hit the identical missing primitive and both correctly declined to substitute `Error.Unauthorized(...)` as an interim.

**Why:** `Unauthorized` (401) = not permitted to attempt at all. `Forbidden` (403) = permitted in general, but a specific per-instance condition (second maker-checker approver, role/permission check) is not met. Silently reusing `Unauthorized` would bake the wrong semantic into shipped tests/behavior with no natural trigger to fix it later — this is why both domains flagged rather than worked around.

**Confirmed while verifying:** `14.Presentation`'s own `P-381` phase already carries the companion `ErrorTypeStatusCodeMap.Resolve(ErrorType.Forbidden) → 403` mapping inside its own scope (task C-24) — no separate root phase was needed for that half. Checked the rest of the platform for a live hand-rolled 403 substitute worth migrating once `Error.Forbidden` ships; found none (only a doc-comment mention in `UnauthorizedException.cs`) — declined to dispatch a migration phase.

**Versioning judgment call:** treated the new enum member as additive/MINOR, not MAJOR — binary-non-breaking, but flagged in the phase's acceptance criteria that a consumer with an exhaustive `switch` over `ErrorType` (no discard arm) will source-break and need a trivial update. This mirrors how `ErrorType.BusinessRule` was added previously with no MAJOR bump.

**Process note — this is the second time (see [[project_wo047_servicedefaults_escalation_resolution]] for the first) a downstream domain's arch-planner/phase-implementer correctly self-identified a missing upstream primitive, flagged it in its own state-map's Cross-Domain Dependencies table, and declined to route around it with a semantically-wrong substitute, rather than silently working around it or blocking indefinitely.** This is the pattern to reinforce: when two independent domains independently hit the same gap and both decline the same workaround for the same reason, that's strong signal the missing primitive is real and correctly scoped — dispatch it fast, as its own small Work Order, rather than folding it into either consumer's WO.

Since this was a single-phase companion dispatch (only 01.Core touched, domain board row already `●` Published), no `state-map-phase` call was made — only the root Phase Backlog entry (P-384) and a `sync-brain` documentation pass (new "What Goes Where" row distinguishing `Error.Forbidden` from `Error.Unauthorized`).
