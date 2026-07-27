---
name: project-core-domain
description: 01.Core implementation status — WO-049 gold-standard review in progress, seven packages
metadata:
  type: project
---

The 01.Core domain shipped six packages as fully `Published` as of 2026-06-26 (Primitives, Core, Guards, Configuration, FeatureManagement, Cryptography). On 2026-07-27, arch-lead's WO-049 gold-standard review dispatched nine additive phases against this domain, all initially `○ Pending`/design-only in the Phase Backlog:

- P-292 `ResultTry`/`ResultCombine` (SharedKernel.Core) — **implemented**
- P-293 `IIdGenerator`/`UuidV7IdGenerator` (SharedKernel.Primitives) — **implemented**
- P-294 `WellKnownTagKeys` (SharedKernel.Primitives) — pending
- P-295 `SystemClock` `TimeProvider`-backed rewrite (SharedKernel.Primitives) — pending
- P-296 `IContentHasher` (SharedKernel.Cryptography) — pending
- P-297 new seventh package `SharedKernel.Compression` (`IPayloadCompressor`) — pending
- P-298 feature-flag variants/`GetVariantAsync` (SharedKernel.FeatureManagement) — pending
- P-299 Result-discard analyzer — targets `00.Governance`, not `01.Core`
- P-300 Cryptography/FeatureManagement fakes — targets `16.Testing`, not `01.Core`

Each of P-292–P-298 was **design-locked in a single core-arch-planner pass** (D-31→D-41 all `●` in `01.Core/state-map.md` from the start) — the Design task was already done before any phase-implementer session touches it. What remains per phase is Scaffold (only P-297, since it's a new package)/Core/Tests/Docs. Each phase is self-contained (own `SK.01.P29X` phase key, own task-ID range) and does not depend on the others completing first.

**Why:** Six-package baseline was closed out over several work orders (P-003 added Guards, WO-033 added Cryptography). WO-049 is a single review that queued seven in-domain gap-fills plus two cross-domain follow-ups in one pass, rather than one at a time.

**How to apply:** Before starting any 01.Core session, check `01.Core/state-map.md`'s Phase Key Registry for `SK.01.P29X` rows still `◐`/`○` — those are the next candidates for `/implement-phase-core`. Root `state-map.md`'s Phase Backlog `### P-29X` entries mirror these; when a phase's sub-map key reaches all-`●`, propagate to root and flip that Phase Backlog entry's Status to `● Complete` (see `feedback_verify_design_claims_before_shipping.md` for a gotcha found doing this for P-293).
