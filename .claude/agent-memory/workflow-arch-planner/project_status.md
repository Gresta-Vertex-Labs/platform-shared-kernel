---
name: project_status
description: Current dispatch/phase status of 17.Workflows (WO-046/P-287) and what the next real work item is
type: project
---

As of 2026-07-22, `17.Workflows/CLAUDE.md` and `17.Workflows/state-map.md` already contain the **complete** ratified design and 81-task plan (16 Design + 10 Scaffold + 26 Core + 16 Tests + 6 Docs + 7 Published) for `SharedKernel.Workflows.Temporal`, drafted in full ahead of the formal work order arriving. WO-046 was then formally dispatched as **P-287** by arch-lead — its acceptance criteria were cross-checked line-by-line against the existing plan and matched exactly, so no task content changed; only the "pending root dispatch" status notes were updated to "dispatched" and a confirmation changelog entry was added to both files.

**Why:** the domain brain (CLAUDE.md) was apparently pre-drafted (by a prior session or by arch-lead prep) to the full depth the eventual work order would require, so when the real dispatch (P-287) arrived there was nothing left to design — only to confirm.

**How to apply:** Current real state is: all 81 tasks are still `○` Pending — **zero implementation exists**, only two bare placeholder `.csproj` files on disk. The next actual work is the **Scaffold phase** (S-01–S-10), which must resolve two SDK-shape unknowns before Core can start:
- **S-05**: verify the real `Temporalio` 1.17.0 assembly (enum names for `WorkflowIdReusePolicy`/id-conflict-policy, `ApplicationFailureException` ctor shape, `IClientInterceptor`/`IWorkerInterceptor` member shape, `IPayloadCodec` signature, `Workflow.Patched` naming) — documentation/guesses are explicitly distrusted here per the `09.Search` reflect-the-real-SDK precedent (caught five defects there).
- **S-08**: read `01.Core/SharedKernel.Cryptography`'s real `ISymmetricEncryptionService` source to confirm exact encrypt/decrypt signatures and key-version shape before `EncryptionPayloadCodec` is designed against it.

When resuming work on this domain, check `17.Workflows/state-map.md`'s Overall Progress table first for the current phase-completion counts rather than assuming Design-phase drafting is still needed.
