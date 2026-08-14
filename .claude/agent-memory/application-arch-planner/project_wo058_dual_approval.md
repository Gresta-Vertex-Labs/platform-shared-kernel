---
name: project_wo058_dual_approval
description: WO-058 P-380 dual-control/maker-checker DualApprovalBehavior for 05.Application — design locked, Core genuinely blocked on 01.Core Error.Forbidden
metadata:
  type: project
---

WO-058 P-380 ("Application: Dual-Control (Maker-Checker) Authorization for High-Risk Commands") was dispatched
2026-08-13, Design phase written (D-72..D-80, `○` 0/9 pending an implementer session), all other phases `○`
in `05.Application/state-map.md`.

**Why:** maker-checker/four-eyes controls (SOX, banking regulation, PCI-DSS) for high-value commands — payment
approval, credit-limit changes, signing-key rotation, prod config changes. A `05.Application`-native pipeline
gate, structurally identical to the already-shipped `AuthorizationBehavior`, not a `12.Security` capability.

**Shape locked:**
- `IRequiresDualApproval` marker (`.ApprovalKey`) — mirrors `IIdempotentRequest.IdempotencyKey` exactly.
- `IDualApprovalStore` local seam (`TryGetApprovalAsync` returns the approver's identity or `null`;
  `RecordApprovalAsync` called ONLY by the consuming service's own separate approval-recording command/handler,
  never by `DualApprovalBehavior` itself — the behavior only ever reads).
- `IAuthorizationContextIdentity` — a NEW sibling optional-capability interface on `IAuthorizationContext`
  (`GetCurrentIdentityAsync`), NOT a breaking modification to the already-published seam. See
  [[pattern_local_seam_bridging]] for why this technique (mirrors `IIdempotencyResponseStore`) was chosen over
  editing `IAuthorizationContext` directly.
- `DualApprovalBehavior<TRequest,TResponse>` — commands only (`ICommandBase, IRequiresDualApproval`); no record
  → `Result.Failure(Error.Forbidden(...))`; record from the SAME resolved identity as the initiator
  (self-approval) → ALSO `Result.Failure(Error.Forbidden(...))`, unconditionally, even though a record exists;
  record from a DISTINCT identity → `next()`. Reuses `Shared/FailureResponseFactory.cs`, no new generic-failure
  mechanism.
- Pipeline order grows from ten to **eleven** named slots: `DualApprovalBehavior` inserted as step 6,
  immediately after `AuthorizationBehavior`(5) and before `CachingBehavior`(6→7); steps 7-10 renumbered 8-11.
- `ApplicationBehaviorsBuilder.AddDualApprovalBehavior()` — this domain's FIRST two-dependency `Build()`-time
  guard (`IAuthorizationContext` AND `IDualApprovalStore` both required).

**GENUINE cross-domain blocker, not a workaround target:** `Error.Forbidden(...)` does not exist in `01.Core`
today. Verified by direct read of `01.Core/SharedKernel.Primitives/Errors/Error.cs`/`ErrorType.cs` on
2026-08-13 — only `Unexpected`/`Validation`/`NotFound`/`Conflict`/`Unauthorized`/`BusinessRule` exist. This needs
its own `01.Core` phase (a new `ErrorType.Forbidden` enum member + `Error.Forbidden(code, message)` factory,
exactly mirroring how `BusinessRule` was added) — NOT dispatched as of this entry. Do NOT let a future session
substitute `Error.Unauthorized(...)` "temporarily" — semantically wrong (Unauthorized = "not permitted at all";
Forbidden here = "permitted, but awaiting a second distinct approver") and would bake a wrong contract into
shipped tests. C-78 in `05.Application/state-map.md` is marked `⚑` blocked; check `01.Core/state-map.md` for a
`SK.01` phase covering `ErrorType.Forbidden` before resuming this domain's Core phase.

**Second cross-domain item, also not dispatched:** this phase's own acceptance criteria require a `16.Testing`
in-memory `IDualApprovalStore` fake "in the same phase" (mirrors the WO-040/P-244 `IUnitOfWork`/
`IAuthorizationContext`/`IIdempotencyKeyStore` fake precedent) — designed at D-79 but out of this domain's
jurisdiction to build. Needs a companion `testing-arch-planner` dispatch.

See [[pattern_local_seam_bridging]] for the fourth-local-seam precedent and the sibling-optional-capability-
interface technique this phase introduced a second instance of.
