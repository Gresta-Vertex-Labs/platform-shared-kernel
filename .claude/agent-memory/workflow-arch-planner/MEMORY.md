# Memory Index

- [Project status: 17.Workflows](project_status.md) — WO-046 base + WO-081 P-501 both shipped; WO-086 tier/probe/TenantScope changes applied
- [Verify before rewriting a dispatched phase](feedback_verify_before_rewrite.md) — check on-disk state-map/CLAUDE.md first; a dispatch may already be fully answered by existing content
- [Temporalio 1.17.0 IWithSerializationContext / no RunId in codecs](sdk_serialization_context.md) — verified real-assembly reflection finding: IPayloadCodec gets WorkflowId via opt-in mechanism, never RunId
