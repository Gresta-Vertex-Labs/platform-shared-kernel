---
name: phase-sequencing
description: Work order and phase sequencing for 04.Contracts — WO-011, WO-012, WO-026, WO-051
metadata:
  type: project
---

## Phase Sequencing for 04.Contracts

**WO-011** covers P-055 (EventEnvelope<TEvent>) only. It depends on P-053 (03.Domain base domain implementations being complete).

**WO-012** covers the full implementation chain:
- P-058 Scaffold — depends on nothing (creates project structure)
- P-059 Core — depends on P-058 (scaffold) and P-055 (EventEnvelope from WO-011)
- P-060 Tests — depends on P-059 (core implementation complete)
- P-061 Docs — depends on P-060 (tests validate implementation before docs are written)
- P-062 Published — depends on P-061 (docs + XML complete before packaging)

**Total tasks as of 2026-05-30:** 32 tasks across 6 phases (D-01–D-06, S-01–S-04, C-01–C-06, T-01–T-06, DO-01–DO-06, P-01–P-04).

**WO-026** covers P-166 (ResultEnvelopeExtensions) — additive to the already-published 1.0.0 package:
- D-07 Design — pure static class shape, 4 extension method signatures, namespace, purity contract
- S-05 Scaffold — create Mapping/ subfolder and empty placeholder file
- C-07 Core — implement all 4 extension methods; verify Result factory names against SharedKernel.Primitives API before coding
- T-07 Tests — 10 test scenarios (generic + non-generic, success + failure + double round-trip)
- DO-07 Docs — XML doc on all 4 methods; README section 3 updated with usage examples
- P-05 Published — version bump to 1.1.0; consumer-verify extended; all tests pass
- No new NuGet dependencies. Both Result<T> (SharedKernel.Primitives) and Envelope<T> (this package) already in scope.

**Why P-166 is separate from WO-012:** ResultEnvelopeExtensions was not part of the original 1.0.0 design. It was identified in WO-026 as a platform-standard bridge that eliminates per-site inline boilerplate across typed clients, controllers, and gRPC handlers.

**How to apply:** When dispatching C-07 (Core), verify the exact `Result` factory method names (`Success`/`Failure` vs `Ok`/`Fail`) against the current SharedKernel.Primitives source before writing any code — the names must match exactly.

**WO-051 covers P-314** — a documentation-only correction to `EventEnvelope<TEvent>.Payload`'s XML doc, added as DO-08 in the Docs phase (no Design/Scaffold/Core/Tests/Published tasks — a pure doc fix doesn't warrant a full six-phase cycle; see [[event-envelope-decisions]] for the factual detail). It has a real cross-domain dependency: `03.Domain`'s `IHasAggregateId<TId>` marker interface (WO-051/P-309, tracked as C-41 in `03.Domain/state-map.md`, still `○` pending as of 2026-07-29). DO-08 must be sequenced to run *after* P-309 ships, because the corrected doc's `<see cref="IHasAggregateId{TId}"/>` needs a real compiled type to resolve against. This is the first time a 04.Contracts phase had a hard doc-content dependency on an unshipped 03.Domain phase — check root `state-map.md`/`03.Domain/state-map.md` for P-309's status before dispatching DO-08 to `contracts-phase-implementer`.

**WO-052 covers P-328/P-331/P-332** (dispatched 2026-07-31, all `Depends on: None`, no ordering constraint between them, all `○` Pending — see [[envelope-namespace-rename]] and [[event-envelope-decisions]] for factual detail):
- **P-328** — rename `Envelope`/`Envelope<T>`'s namespace from `SharedKernel.Contracts.Envelope` to `SharedKernel.Contracts.Envelopes` (folder `Envelope/` → `Envelopes/`). Namespace-only move, zero member/behavior change. Tasks: D-08, S-06, C-08, T-08, DO-09, P-06 (full six-phase cycle — even though it's "just a rename," it touches every phase: Scaffold needs the folder move, Core needs the namespace recompile, Tests need the old-namespace references updated, Docs need the collision-workaround rule removed).
- **P-331** — add nullable `TenantId` (`Guid?`) to `EventEnvelope<TEvent>` plus a trailing optional `Guid? tenantId = null` on `Wrap`. Purely additive. Tasks: D-09, C-09, T-09, DO-10, P-07 — **no Scaffold task**, since `EventEnvelope<TEvent>` already exists in `Events/EventEnvelope.cs` and no new file/folder is needed. This is a useful precedent: when a capability only extends a type that already has a home, skip the Scaffold phase entirely rather than inventing a filler task — don't pad phase task lists just to keep row counts symmetric across all six phases.
- **P-332** — new `CursorPagedList<T>` sealed record in `Pagination/`, the keyset-pagination counterpart to `PagedList<T>`. Tasks: D-10, S-07, C-10, T-10, DO-11, P-08.
- **Versioning across a multi-phase WO with no ordering constraint:** when several same-WO phases have no `Depends on` relationship and are expected to ship in one release, only the *dominant* SemVer bump applies once — a major bump (P-328's breaking rename → target `2.0.0`) subsumes any additive changes released in the same pass (P-331/P-332 would individually be minor bumps alone). Say this explicitly in the Published-phase task text so the phase-implementer doesn't double-bump to `2.1.0` after already going to `2.0.0`. Package Board should record the queued target version as a note without changing the "currently shipped" version field until the Published tasks actually complete.
