# Memory Index

- [Project status: 17.Workflows / WO-046 base + WO-081 P-501 in-flight](project_status.md) — base scope (81 tasks) fully shipped; P-501 (13 tasks) designed, blocked on 01.Core P-491/P-492 landing
- [Verify before rewriting a dispatched phase](feedback_verify_before_rewrite.md) — check on-disk state-map/CLAUDE.md first; a dispatch may already be fully answered by existing content
- [Temporalio 1.17.0 IWithSerializationContext / no RunId in codecs](sdk_serialization_context.md) — verified real-assembly reflection finding: IPayloadCodec gets WorkflowId via opt-in mechanism, never RunId
