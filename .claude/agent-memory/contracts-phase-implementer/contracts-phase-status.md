---
name: contracts-phase-status
description: Phase completion status for 04.Contracts — what each phase delivered and what comes next
metadata:
  type: project
---

## Historical note (superseded — kept for context)
The section below described the very first build-out (2026-05-30, WO-011/WO-012), when all six phases (Design→Published) were completed together in one session and 04.Contracts shipped as `SharedKernel.Contracts 1.0.0`. Two more full WO cycles have since shipped on top of that baseline. Do not treat the "62 tests"/"1.0.0" figures below as current — see "Status as of 2026-07-31" instead.

## Status as of 2026-07-31 (current)
`SharedKernel.Contracts` is **shipped and Published at v1.1.0** (WO-026/P-166 added `ResultEnvelopeExtensions`, `SharedKernel.Contracts.Mapping` namespace; 72 tests green; consumer-verify covers 6 surfaces). WO-051/P-314 later fixed a doc-accuracy defect (`EventEnvelope<TEvent>.Payload`'s XML doc falsely claimed `AggregateId` was guaranteed).

**WO-052 is now in flight, targeting a breaking `2.0.0` release**, three changes designed by contracts-arch-planner and recorded in full in `04.Contracts/CLAUDE.md`:
- P-328: rename `Envelope`/`Envelope<T>` namespace `SharedKernel.Contracts.Envelope` → `SharedKernel.Contracts.Envelopes` (folder `Envelope/` → `Envelopes/`), eliminating the long-standing namespace/type-name collision (see `contracts-design-patterns.md`). **Breaking** — this is why the release is 2.0.0, not 1.2.0.
- P-331: add nullable `Guid? TenantId` to `EventEnvelope<TEvent>` (positioned after `CausationId`, before `SourceService`) + trailing optional `Guid? tenantId = null` param on `Wrap`. Purely additive/source-compatible.
- P-332: add new `CursorPagedList<T>` sealed record in `Pagination/` (namespace `SharedKernel.Contracts.Pagination`) — `Items`/`NextCursor`/`HasMore`, `Create`-only construction, deliberately no `TotalCount`/`Page`/`PageSize`. Purely additive. Doc-only cross-ref to `03.Domain`'s `KeysetSpecification<T, TKey>` (no compile dependency).

**SK.04.Design is 10/10 ● complete** (2026-07-31) — D-08/D-09/D-10 verified against CLAUDE.md and the real pre-implementation source tree.

**SK.04.Scaffold is now 7/7 ● complete** (2026-07-31, this session) — S-06/S-07 executed:
- S-06: `git mv Envelope/ Envelopes/` (history preserved via git rename detection). **Namespace declarations inside the moved files were deliberately left untouched** (`Envelope.cs`/`EnvelopeT.cs` still declare `namespace SharedKernel.Contracts.Envelope;`, the OLD singular name) — a folder rename does not change a compiled namespace, and the actual namespace edit is explicitly Core-phase work (C-08), not Scaffold. Do not conflate "folder renamed" with "namespace renamed" — they are two different phases by design here.
- S-07: stubbed `Pagination/CursorPagedList.cs` as an empty placeholder — just `namespace SharedKernel.Contracts.Pagination;` plus a one-line comment pointing to C-10. No record/type defined yet.
- Verified: `dotnet build` on both the main csproj and the Tests csproj = 0 errors; `dotnet test` = 72/72 green (no regressions from the folder rename); `.slnx` registration unaffected (references `.csproj` paths only, never folder paths, so no `.slnx` edit was needed for S-06).
- Root `state-map.md` row 04 propagated to `Current Phase: Scaffold ●`, mirroring the same "latest lifecycle phase, not frozen at Published" pattern used for `02.Caching`/`07.Messaging`. Shipped package remains 1.1.0.

**Next up:** Core (C-08: move `Envelope`/`Envelope<T>` from namespace `SharedKernel.Contracts.Envelope` → `SharedKernel.Contracts.Envelopes`, update `ResultEnvelopeExtensions`' `using EnvelopeNs = ...` alias and `ContractsJsonContext`'s `[JsonSerializable]` entries; C-09: add `TenantId`/`Wrap` param; C-10: implement `CursorPagedList<T>` + register in `ContractsJsonContext`), then Tests/Docs/Published (T-08/09/10, DO-09/10/11, P-06/07/08). All three WO-052 phases (P-328/P-331/P-332) have `Depends on: None` and ship together in one `2.0.0` pack — do not double-bump to 2.1.0 for the additive parts.

**Reminder for the Core-phase session:** `Mapping/ResultEnvelopeExtensions.cs` currently has `using EnvelopeNs = SharedKernel.Contracts.Envelope;` at the top and uses `EnvelopeNs.Envelope<T>`/`EnvelopeNs.Envelope` throughout — this alias must be updated to point at `SharedKernel.Contracts.Envelopes` as part of C-08 (the file itself doesn't move, only the `using` target changes).

## Key file locations
- Main project: `04.Contracts/SharedKernel.Contracts/`
- Test project: `04.Contracts/SharedKernel.Contracts/SharedKernel.Contracts.Tests/`
- Types (current, post-Scaffold layout): `Pagination/PagedList.cs`, `Pagination/CursorPagedList.cs` (empty stub), `Envelopes/Envelope.cs`, `Envelopes/EnvelopeT.cs` (folder renamed, namespace still OLD `SharedKernel.Contracts.Envelope` pending C-08), `Events/IIntegrationEvent.cs`, `Events/EventEnvelope.cs`, `Serialization/ContractsJsonContext.cs`, `Mapping/ResultEnvelopeExtensions.cs`
