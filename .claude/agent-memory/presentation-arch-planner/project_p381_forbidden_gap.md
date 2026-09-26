---
name: project_p381_forbidden_gap
description: 01.Core has no Error.Forbidden/ErrorType.Forbidden — blocks WO-058/P-381 declarative role/permission authorization; also fixed a stale pre-existing doc defect in ErrorTypeStatusCodeMap's own note
type: project
---

> WO-086 (2026-09): `ErrorType.Forbidden`/`Error.Forbidden` have since shipped (P-384), and `ErrorTypeStatusCodeMap` now lives in `SharedKernel.Presentation.Core` (`14.Presentation/SharedKernel.Presentation.Core/Errors/ErrorTypeStatusCodeMap.cs`). Kept for the verify-shipped-code lesson.

`01.Core/SharedKernel.Primitives/Errors/ErrorType.cs` only has six members: `None`, `Unexpected`,
`Validation`, `NotFound`, `Conflict`, `Unauthorized`, `BusinessRule`. There is no `Forbidden` and
never has been. `Error.cs` has no `Error.Forbidden(...)` factory to match. Confirmed via direct
read 2026-08-13, not assumed from any brain prose.

**Why:** WO-058/P-381 (declarative `[RequireRole]`/`[RequirePermission]` endpoint-filter
authorization) needs a genuine 403 outcome distinct from the existing `Unauthorized`→401 mapping
(not-authenticated vs. authenticated-but-forbidden are different HTTP semantics). A sibling
`05.Application` phase in the same work order (P-380) independently hit the identical gap —
this is a real, converging cross-domain need, not a one-off ask.

**How to apply:** Do not let a future phase quietly substitute `Error.Unauthorized(...)` for a
403 case — that collapses a real semantic distinction. Any phase needing "authenticated but
lacks permission" must be design-locked now and gated (Scaffold/Core/Tests/Docs/Published all
`○`, Cross-Domain Dependencies row added) until `01.Core` ships `ErrorType.Forbidden`/
`Error.Forbidden(...)`. Once that lands, this domain's own follow-up work includes adding
`ErrorType.Forbidden => StatusCodes.Status403Forbidden` to `ErrorTypeStatusCodeMap.Resolve`
(`14.Presentation/SharedKernel.Presentation.WebApi/Errors/ErrorTypeStatusCodeMap.cs`) — check
whether `core-arch-planner`/`application-arch-planner` already dispatched the `01.Core` side
before re-flagging it as new.

**Separate, unrelated finding fixed in the same pass:** this domain's own `CLAUDE.md` had a
long-standing stale doc defect — its `ErrorTypeStatusCodeMap` Interface Contracts note claimed
"Forbidden → 403 ... Failure → 500", but the real shipped `ErrorType` enum never had a
`Forbidden` or `Failure` member (it has `Unexpected`/`BusinessRule` instead), and the real
shipped `Resolve` switch only ever mapped `Validation/Unauthorized/NotFound/Conflict/
BusinessRule/Unexpected`. This had apparently gone unnoticed through every prior WO pass on
this domain. Corrected in place (see [[project_wo031_phasing]] for the general pattern of
verifying shipped code over trusting prior brain prose).
