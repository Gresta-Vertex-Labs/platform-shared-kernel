# Memory Index

- [WO-035 seven-behavior pipeline (design-only as of 2026-06-29)](project_wo035_seven_behavior_pipeline.md) — Authorization + Idempotency behaviors added, pipeline order revised to 7 steps
- [WO-036 ten-step pipeline (Core complete as of 2026-06-30)](project_wo036_ten_step_pipeline.md) — Tracing, streaming vocabulary, Resilience, test harness, CacheInvalidation; C-18..C-29 ●
- [WO-038 bug fixes and new caps (design-only as of 2026-07-01)](project_wo038_bug_fixes_and_new_caps.md) — P-231 bug fixes, P-232 contract evolution (depends P-230), P-233 parallel dispatch + fire-and-forget, P-234 streaming pipeline behaviors (depends P-232)
- [Local-seam bridging pattern](pattern_local_seam_bridging.md) — IUnitOfWork/IAuthorizationContext/IIdempotencyKeyStore local-seam pattern; WO-036 clarifies not every new behavior needs a NEW seam
