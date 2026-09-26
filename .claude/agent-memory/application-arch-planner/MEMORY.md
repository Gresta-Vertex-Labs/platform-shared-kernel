# Memory Index

- [WO-035 seven-behavior pipeline (historical)](project_wo035_seven_behavior_pipeline.md) — why Authorization applies to queries too and Idempotency is commands-only; order since superseded by the five PipelineStages
- [WO-036 ten-step pipeline (historical)](project_wo036_ten_step_pipeline.md) — Tracing, streaming vocabulary (no Result<T> per item), cache invalidation after commit; Resilience since deleted
- [Contracts over seams (current)](pattern_local_seam_bridging.md) — behaviors depend on Execution / Idempotency.Abstractions / Caching.Abstractions contracts or SharedKernel.Application ports; Build() guard; sibling-capability technique
- [WO-041 logging retrofit (historical)](project_wo041_logging_retrofit.md) — [LoggerMessage] EventId allocation 5100-5199, now in SharedKernel.Application.Pipeline
- [WO-071 AuditingBehavior (historical)](project_wo071_auditing_behavior.md) — why the audit write sits inside the transaction; now two halves over SharedKernel.Execution.Auditing
