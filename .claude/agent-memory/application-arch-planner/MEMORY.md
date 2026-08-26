# Memory Index

- [WO-035 seven-behavior pipeline (design-only as of 2026-06-29)](project_wo035_seven_behavior_pipeline.md) — Authorization + Idempotency behaviors added, pipeline order revised to 7 steps
- [WO-036 ten-step pipeline (Core complete as of 2026-06-30)](project_wo036_ten_step_pipeline.md) — Tracing, streaming vocabulary, Resilience, test harness, CacheInvalidation; C-18..C-29 ●
- [WO-038 bug fixes and new caps (design-only as of 2026-07-01)](project_wo038_bug_fixes_and_new_caps.md) — P-231 bug fixes, P-232 contract evolution (depends P-230), P-233 parallel dispatch + fire-and-forget, P-234 streaming pipeline behaviors (depends P-232)
- [Local-seam bridging pattern](pattern_local_seam_bridging.md) — 4 local seams now; WO-058 adds the sibling-optional-capability-interface technique for extending an already-published seam without breaking it
- [WO-041 logging retrofit (design locked 2026-07-09)](project_wo041_logging_retrofit.md) — [LoggerMessage] EventId allocation table (5100-5199), exhaustive 4-file list, blocked on 01.Core P-249 + 00.Governance P-250
- [WO-058 dual-control/maker-checker (design locked 2026-08-13)](project_wo058_dual_approval.md) — DualApprovalBehavior, 11-step pipeline; Core BLOCKED on 01.Core Error.Forbidden (not dispatched); 16.Testing fake needed (not dispatched)
- [WO-071 AuditingBehavior (design locked 2026-08-26)](project_wo071_auditing_behavior.md) — 12-step pipeline, 5th local seam (IAuditTrailWriter), NO cross-domain Core blocker (novel finding); depends on 06.Persistence P-456
