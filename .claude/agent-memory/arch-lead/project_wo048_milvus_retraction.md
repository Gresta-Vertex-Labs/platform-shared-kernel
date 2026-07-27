---
name: project_wo048_milvus_retraction
description: WO-048 retired SharedKernel.AI.Milvus from the platform plan; root state-map.md/CLAUDE.md changes and what was deliberately deferred
type: project
---

WO-048 (2026-07-27, user decision, final): `SharedKernel.AI.Milvus` retired from the platform plan entirely. It was never created — blocked at Scaffold S-05 across every `10.Intelligence` session because `Milvus.Client` never shipped a stable release (latest `2.3.0-preview.1`, published 2024-03-20; GitHub repo stalled since 2023-09-12). `SharedKernel.AI.Abstractions`/`.Qdrant`/`.SemanticKernel` are unaffected and remain valid, shipped, Published. A future second vector provider remains a legitimate future phase against the same neutral `.Abstractions` contract.

**Why:** No architectural reason — pure upstream dependency abandonment, same class of call as the `NEST`/`09.Search` EOL precedent. The user made the call after Milvus.Client showed no sign of stabilizing across the whole WO-045 build-out.

**Root state-map.md changes (Phase Backlog):**
- New `⊘` Retracted status value added to the Phase Backlog legend (alongside `○`/`◐`/`●`) — a phase withdrawn before/after dispatch, no longer actionable to `/dispatch-phase`, kept for history.
- P-281 (Intelligence: SharedKernel.AI.Milvus Provider) → `⊘` Retracted with inline rationale.
- P-285 (ServiceDefaults vector-store/orchestration readiness) and P-286 (Governance 10.Intelligence topology) — `P-281` dropped from `Depends on`; Milvus mentions trimmed from acceptance criteria (Qdrant-only / two-sibling scope); each carries an inline RETRACTION NOTE. Their WO-047-resolved intent (orchestration-readiness already out of scope) was otherwise untouched.
- Checked P-280/P-282 (both `●` Complete, "never references Milvus" criteria — trivially still true, no edit needed) and P-283 (`●` Complete, 16.Testing Qdrant+Milvus Testcontainers fixture — historical record, outside root jurisdiction, left untouched).
- New WO-048 changelog line appended right after the WO-045 block (before P-287) in the Phase Backlog's embedded changelog.

**Root CLAUDE.md changes:**
- Folder Map row 10: dropped Milvus, Qdrant now sole vector-DB provider, annotated "(secondary vector DB provider retracted — Milvus.Client abandoned, WO-048)".
- Abstractions Packages table: `SharedKernel.AI.Abstractions` row narrowed to `.Qdrant`, `.SemanticKernel`.
- What Goes Where: deleted the dedicated "Milvus as the vector-database backend" row outright; trimmed Milvus out of the provider-exclusive-capability row and the raw-vector-DB-client-injection row.
- New root Changelog entry dated 2026-07-27 (WO-048).

**Follow-up completed (2026-07-27, same day):** coordinator confirmed `intelligence-arch-planner`'s parallel refactor landed — 49/49 tasks complete, 0 blocked, all six phases `●` (verified against `10.Intelligence/CLAUDE.md`'s own shipped Changelog before trusting the claim, per [[feedback_verify_shipped_code_not_docs]]). Closed both deferred root spots: `## Blocked` table's `10.Intelligence` row removed (replaced with "Nothing blocked" note); Domain Summary Board row 10 updated to `●` Published, 49/49 tasks, 0 blocked, all six phases complete, 276/276 tests green. Also fixed the Overall Progress table's derived counts for consistency (`● Published` 10→11, `⚑ Blocked` 1→0) — not explicitly requested but a direct mechanical consequence of the board edit, and the table's own header says it tracks the board above. Root sync is now fully closed — no more deferred spots remain for WO-048.

See also [[project_wo047_servicedefaults_escalation_resolution]] for the prior WO-047 resolution this retraction builds on (orchestration-readiness already retracted separately; only vector-store readiness proceeds, and now only against Qdrant).
