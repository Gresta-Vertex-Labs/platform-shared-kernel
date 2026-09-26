---
name: project_status
description: Current dispatch/phase status of 17.Workflows (WO-046/P-287 base scope and WO-081/P-501 both shipped; WO-086 refactor applied) and where to look for the next real work item
type: project
---

**Base scope (WO-046/P-287) and P-501 (WO-081) are both fully implemented and shipped.** `17.Workflows/SharedKernel.Workflows.Temporal/` holds a complete production implementation (dispatch surface, authoring bases, worker-hosting builder, propagation interceptors, failure mapper, payload codec, readiness probe, `[LoggerMessage]` logging), proven through a `consumer-verify` harness on a real `IHost.StartAsync()`. P-501 moved `EncryptionPayloadCodec` onto `ISymmetricEncryptionService.EncryptAsync`/`DecryptAsync` and binds AES-GCM associated data to the `WorkflowId` via `IWithSerializationContext<IPayloadCodec>` (see [[sdk_serialization_context]]); it shipped 2026-09-08, the same day `01.Core`'s P-491/P-492 landed.

**WO-086 (2026-09) changed the domain's surroundings, not its design:** the package is Adapter tier; `TenantScope` is the single `SharedKernel.Execution.Tenancy.TenantScope` (`Global` replaces `None`); `CommandActivity<>` uses the kernel `ISender` from `SharedKernel.Application` (no MediatR); the activity inbound interceptor opens a `RequestContextScope` with a `PropagatedRequestContext`; `IWorkflowServiceProbe`/`WorkflowServiceHealth` were replaced by an internal `IReadinessProbe` named `"workflows"` (the `13 → 17` grant and `AddWorkflowReadinessCheck` are gone); test doubles moved to `16.Testing/SharedKernel.Workflows.Testing`.

**When resuming:** read `17.Workflows/state-map.md`'s Overall Progress table for current counts rather than assuming either "everything is done" or "nothing is done."
