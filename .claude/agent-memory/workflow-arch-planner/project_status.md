---
name: project_status
description: Current dispatch/phase status of 17.Workflows (WO-046/P-287 base scope, WO-081/P-501 in-flight) and what the next real work item is
type: project
---

**Base scope (WO-046/P-287) is fully implemented and shipped**, not merely planned — this corrected a stale belief from an earlier session. `17.Workflows/SharedKernel.Workflows.Temporal/` holds a complete, real production implementation (dispatch surface, authoring bases, worker-hosting builder, propagation interceptors, failure mapper, payload codec, service probe, `[LoggerMessage]` logging), 0 build errors, `SharedKernel.Workflows.Temporal.Tests` has 158/158 passing (134 unit + 24 real-`WorkflowEnvironment`), packed clean, and a `consumer-verify` harness proves it through a real `IHost.StartAsync()`. All 81 original tasks (D-01–D-16, S-01–S-10, C-01–C-26, T-01–T-16, DO-01–DO-06, P-01–P-07) are `●`.

**As of 2026-09-08, a second phase P-501 (WO-081) was designed and queued on top of that base scope.** It migrates `EncryptionPayloadCodec` off the synchronous `ISymmetricEncryptionService.Encrypt`/`.Decrypt` onto the `*Async` members, and adds AES-GCM associated-data (AAD) binding. 3 Design tasks (D-17–D-19) are locked `●`; 12 Core/Tests/Docs/Published tasks (C-27–C-30, T-17–T-20, DO-07, P-08) are `○` Pending, **genuinely blocked** on `01.Core`'s own P-491 (AAD required param, its C-81 still `○`) and P-492 (sync-gating via `EncryptionKeyProviderCapabilities.IsGenuinelySynchronous`, its C-82/C-83 still `○`) landing first — verified by reading `01.Core/SharedKernel.Cryptography/Symmetric/ISymmetricEncryptionService.cs` directly, which as of this writing still has the pre-migration signature (no `associatedData` parameter). Check `01.Core/state-map.md`'s `SK.01.P491`/`SK.01.P492` phase-key rows before resuming this work — if their C-81/C-82/C-83 have shipped, `17.Workflows`' C-27–C-30 become unblocked and are the next real work.

**When resuming:** check `17.Workflows/state-map.md`'s Overall Progress table for current counts (94 tasks total as of this entry: 81 base + 13 P-501) rather than assuming either "everything is done" or "nothing is done."
