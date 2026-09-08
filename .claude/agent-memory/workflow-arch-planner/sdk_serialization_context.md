---
name: sdk_serialization_context
description: Temporalio 1.17.0's IPayloadCodec has no context param, but an opt-in IWithSerializationContext<T> mechanism exposes WorkflowId (never RunId) to codecs/converters
type: reference
---

Verified 2026-09-08 by reflecting the real compiled `Temporalio` 1.17.0 assembly (`~/.nuget/packages/temporalio/1.17.0/lib/net462|netcoreapp3.1|netstandard2.0/Temporalio.dll` + matching `.xml` doc file) via a scratch console project — not guessed, not taken from documentation.

**Facts, durable across future `17.Workflows` design work touching the codec/converter layer:**
- `Temporalio.Converters.IPayloadCodec.EncodeAsync`/`DecodeAsync` take only `IReadOnlyCollection<Payload>` — no context parameter, no `CancellationToken`. This is fixed and was already correctly recorded in `17.Workflows/CLAUDE.md`'s S-05 finding.
- A separate, **opt-in** mechanism exists: `Temporalio.Converters.IWithSerializationContext<T>` — implement it on a codec/converter to get `T WithSerializationContext(ISerializationContext context)` called by the SDK before certain operations. Per the SDK's own XML doc: called with `ISerializationContext.Workflow` (`Namespace`, `WorkflowId`) before client dispatch (start/signal/query/schedule create-or-describe/child-or-external-workflow) and before a workflow task runs in the worker; called with `ISerializationContext.Activity` (`Namespace`, `ActivityId`, `WorkflowId`, `WorkflowType`, `ActivityType`, `ActivityTaskQueue`, `IsLocal`) before an activity is invoked (from workflow, on the activity worker, or via `AsyncActivityHandle`). Both implement a shared `ISerializationContext.IHasWorkflow.WorkflowId` (nullable — null for e.g. standalone activities).
- **`RunId` is absent from every one of these types** — `ISerializationContext`, `.IHasWorkflow`, `.Activity`, `.Workflow`. There is no SDK mechanism in 1.17.0 by which a codec/converter can observe a run id. Do not design anything (AAD, cache keys, audit trails) assuming a payload codec can see `RunId` — it cannot, in this SDK version.
- The SDK's own doc warns `WithSerializationContext` "may be called many times... make sure this is very inexpensive to call... [may] return `this` to bypass" — any implementer must be a cheap, effectively-immutable clone, never a fresh expensive construction.
- Separately (architectural, not just an SDK gap): binding anything to `RunId` would be wrong even if it were available, because Temporal's continue-as-new and workflow-retry mechanisms assign a **new** `RunId` to the **same** `WorkflowId` while carrying data forward across that boundary — `WorkflowId` is the stable identity/idempotency unit on this platform (see `17.Workflows/CLAUDE.md` D-06), `RunId` is not.

**How this was used:** designed `EncryptionPayloadCodec`'s AAD (P-501/WO-081) to implement `IWithSerializationContext<IPayloadCodec>` and bind AES-GCM associated data to `WorkflowId` alone, explicitly rejecting the commissioning brief's "workflow id + run id" as factually wrong on the RunId half.
