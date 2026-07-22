---
name: session1_scaffold_core
description: 10.Intelligence SK.10.Scaffold (S-01-S-07) + SK.10.Core C-01 session findings — Milvus.Client verification failure, verified NuGet pins, file layout conventions, and skill-invocation behavior for state-map-phase.
type: project
---

## Milvus.Client is NOT viable — verified 2026-07-22, hard blocker

`Milvus.Client` on nuget.org has **never shipped a stable (non-preview) release** across its entire
published history — every version from `0.1.0` through the latest `2.3.0-preview.1` carries a
`-preview`/`-alpha` suffix. Last published 2024-03-20. GitHub repo (`milvus-io/milvus-sdk-csharp`)
last tagged release `v2.2.2-preview.6` (2023-09-12); `v2.2.0`/`v2.2.1` marked Obsolete on nuget.org.

**Why:** This is a stronger disqualifier than the `09.Search` `NEST`-is-EOL precedent — `NEST` was at
least formerly stable before EOL; `Milvus.Client` never reached stable at all.

**How to apply:** Any future `10.Intelligence` session touching `SharedKernel.AI.Milvus` must re-verify
this via WebFetch against `https://www.nuget.org/packages/Milvus.Client` before assuming it's still
blocked or has changed. Do NOT pin it without re-verification. The proposed (unattempted) alternative
recorded in `10.Intelligence/state-map.md`'s `## Blocked` section and `CLAUDE.md`'s Technology Stack:
hand-roll a Milvus gRPC client directly from Milvus's public `.proto` service contracts
(`Grpc.Net.Client` + `Google.Protobuf`), mirroring how `Qdrant.Client` itself is gRPC/protobuf-based.
This is a materially larger scope than a package pin — treat as its own dispatched phase, not a
quick fix.

## Verified NuGet pins (2026-07-22, via WebFetch against nuget.org — not assumed)

- `Qdrant.Client` → `1.18.1` (latest stable, Apache-2.0, official "Qdrant.com" owner, actively
  maintained, targets net6.0/netstandard2.0/net462).
- `Microsoft.SemanticKernel` → `1.78.0` (latest stable, MIT, official Microsoft owner, ships a real
  `net10.0` target, actively maintained weekly).
- Both confirmed by building real scaffold `.csproj` files against them (`dotnet build`, 0 errors) —
  package existence + version number alone isn't sufficient proof; a clean restore+build is.

**How to apply:** Re-verify version currency (not existence) before any future session bumps these —
`Microsoft.SemanticKernel` in particular ships very frequently (was already at 1.78.0 as of
2026-07-22, published just 2 weeks prior).

## WebFetch works for nuget.org verification in this environment

`https://api.nuget.org/v3-flatcontainer/{id}/index.json` (lowercase package id) returns the full
version list. `https://www.nuget.org/packages/{Id}` (proper-cased) returns a human-readable page with
license/owners/downloads/target-frameworks/last-published — WebFetch's summarization handles this
page well. Also works against raw GitHub repo pages for release/activity history. Use both together:
the flatcontainer JSON for the authoritative version list, the packages page for metadata, GitHub for
maintenance-activity corroboration.

## File layout used for SharedKernel.AI.Abstractions (C-01)

Matches the CLAUDE.md Interface Contracts section headings literally — types are split into
`Abstractions/`, `Models/`, `Constants/`, `Errors/`, `Exceptions/` folders/namespaces based on which
heading the CLAUDE.md groups them under, NOT a uniform "interfaces vs everything else" split like
`09.Search` used. E.g. `TokenUsage`/`EmbeddingResult`/`VectorWriteReceipt`/`VectorHit<TRecord>` all
live in `Abstractions/` (not `Models/`) because their CLAUDE.md section header said `(`Abstractions/`)`.
Only `VectorValue`/`IVectorRecord`/the `VectorFilter` AST/`TenantScope`/`VectorCollectionDefinition`
live in `Models/`. Check the exact heading annotation in the ratified CLAUDE.md before assuming a type's
folder — it's deliberate, not arbitrary, when the domain's own contract is this precisely authored.

## Test patterns proven to work (142 tests, mirrored from 09.Search's Abstractions.Tests)

- `PackagePurityTests` (reflection over `typeof(VectorFilter).Assembly` — checks referenced assembly
  names, no ActivitySource/Meter/LoggerMessage fields, no IHealthCheck).
- `ContractShapeTests` (reflection over interface method signatures — locks bare `IAsyncEnumerable<T>`
  streaming shape, non-optional `TenantScope` parameters, no capability-flags enum properties).
- Golden-value fingerprint pinning (09.Search's `SearchIndexDefinitionFingerprintTests` pins an exact
  SHA-256 hex string) was **deliberately skipped** for `VectorCollectionDefinition.Fingerprint` — I
  cannot hand-compute a correct SHA-256 hash to pin without running the code, and guessing one would be
  worse than not having it. Instead relied on determinism/order-independence/change-sensitivity/format
  (64 lowercase hex chars) assertions, which fully cover the state-map's stated Tests-phase requirement
  ("identical inputs → identical hash; field-declaration-order independence"). A future session with a
  code-execution tool available could add the golden-value pin as a strictly additive test.
- IntelligenceErrors factory tally: 31 factories total — NotFound×3, Validation×12, Conflict×4,
  Unauthorized×2, Unexpected×10, BusinessRule×0. Verify this count if the catalog ever changes.

## Skill invocation behavior: state-map-phase does NOT auto-execute

Invoking `Skill(state-map-phase)` just loads/re-prints the skill's full instructions into context — it
does not read or edit any file itself. The calling agent must then perform the file edits manually,
following the printed steps. Calling it once per task_id for many tasks (S-01..S-07, C-01) would
reprint the ~200-line instruction block every time with no benefit after the first call. **Better
approach:** invoke it once to load the instructions, then perform all the sub-map-mode edits directly
via Edit tool in one pass (task rows, Blocked section, Overall Progress recalculation, changelog
entries), then apply the same Root-mode steps directly to root `state-map.md` if promotion fires. This
matches what the skill's own steps describe — the skill is instructions-for-the-agent, not a script.

## Root state-map.md had a stale, unpropagated SK.10.Design completion

Found during this session: `10.Intelligence/state-map.md` recorded `SK.10.Design` as 16/16 `●` from a
prior session (dated 2026-07-21), but the root `state-map.md` Domain Summary Board / Active Work still
showed "10.Intelligence — Design — ◐" — the promotion was never executed. Root Phase Backlog `P-279`
was also still `◐ Dispatched` despite its acceptance criteria being fully met once this session's S-01/
S-02/S-03 (on-disk cleanup + zero-PackageReference) landed. Fixed by promoting SK.10.Design to root and
closing `P-279` in the same pass as reporting this session's Scaffold/Core progress — reasonable to
retroactively fix a missed propagation from a prior session when you're already touching that exact
row for an unrelated reason, rather than leaving two known-stale facts on record simultaneously.

**How to apply:** Before reporting a domain's root state-map status, check whether an earlier phase's
promotion condition was already met but never executed (compare the sub-map's own Overall Progress ●
counts against what the root file currently shows) — don't assume the root file is authoritative if the
sub-map disagrees loudly.
