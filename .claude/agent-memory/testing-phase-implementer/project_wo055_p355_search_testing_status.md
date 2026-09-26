---
name: project_wo055_p355_search_testing_status
description: WO-055/P-355 Search/ bulk-write-throttle testing status — CLOSED 2026-08-11 end to end (all six SK.16.* phases ● again)
metadata:
  type: project
---

> WO-086 (2026-09): `SharedKernel.Testing` was split into 20 packable Testing-tier packages (core `SharedKernel.Testing` + 19 `SharedKernel.{Capability}.Testing`) plus the non-packable `SharedKernel.Testing.Internal` (containers, EF/Npgsql/audit helpers, MassTransit harness); `SharedKernel.Testing.SelfTests` became each package's own nested `.Tests` project. Paths and project names below are pre-split history; the technique/lesson still applies.

WO-055/P-355 extended the already-`**implemented**` `Search/` folder with an opt-in bulk-write
throttle surface on `InMemorySearchIndex<TDocument>` — two new 4-arg `IndexManyAsync`/
`DeleteManyAsync` overloads carrying a new `SearchBulkWriteOptions` record, plus a
`LastBulkWriteOptions` audit property. CLOSED end to end 2026-08-11: Design/Scaffold/Core
(D-180–D-183/S-46/C-108–C-110) closed 2026-08-10/11, Tests (T-75–T-77) closed 2026-08-11, and
Docs (DO-37) closed 2026-08-11 in the final session. All six `SK.16.*` phase keys `●` again
(455/455 tasks). Root `state-map.md` Phase Backlog `### P-355` marked `●` Complete, all four
acceptance criteria checked with citations — mirroring the established P-306/P-335/P-352
pattern exactly (see the corrected note below).

**Why:** the original P-355 phase-input premise (per-document bulk-write outcome reporting)
was stale — that already shipped in WO-044/P-276 (D-180 corrected this at Design time). The
genuinely new surface was narrower: `SearchBulkWriteOptions` + two additive 4-arg overloads,
which needed `09.Search`'s own `SK.09.Core` (C-51/C-52, shipped 2026-08-10) before this
domain's Tests phase could compile against real signatures.

**What was built (T-75–T-77, 2026-08-11):** extended the EXISTING
`SharedKernel.Testing.SelfTests/Search/InMemorySearchIndexTests.cs` additively (zero
pre-existing tests touched) with 8 new tests:
- T-75 (2 tests): `IndexManyAsync_FourArgOverloadWithDefaultOptions_ProducesIdenticalReceipt_ToThreeArgOverload`
  / `DeleteManyAsync_...` — re-run the pre-existing 3-arg scenarios
  (`IndexManyAsync_PartialInvalidIds_ReturnsSuccess_WithPerItemFailures`/
  `DeleteManyAsync_MixedPresence_CountsEveryRequestedIdAsSucceeded`) through the new 4-arg path
  on a SEPARATE fresh fake instance, comparing `SucceededCount`/`HasFailures`/`Failures`
  (`SequenceEqual`, not record `Equals` — see the `[[feedback_record_list_equality_pitfall]]`
  memory) and the full `SearchWriteReceipt` (direct record equality — both instances'
  independent token sequences both start at `"in-memory-token-1"`, so `ProviderToken` matches
  too). Explicitly scoped INTRA-PACKAGE — real-provider parity is `09.Search`'s own T-32–T-34.
- T-76 (6 tests): `LastBulkWriteOptions` — null-before-first-call; `Assert.Same` (not just
  value-equality) proving the exact caller-supplied instance is stored; a revert-to-`Default`
  sequencing proof (4-arg custom throttle, then a 3-arg call, proving the property is NOT
  sticky to whatever was set first); a cross-member shared-property proof (`IndexManyAsync`'s
  throttle overwritten by a subsequent 3-arg `DeleteManyAsync` call — proving one shared audit
  surface, not two independently-tracked ones); a `Stopwatch`-timed 50-doc bulk write with
  `MaxBatchesPerSecond = 1` completing well under 1 second (no real `Task.Delay`/pacing).
- T-77: full regression, 916/916 passing (908 pre-existing + 8 new), zero regressions.

**What was built (DO-37, 2026-08-11):** documentation-only, zero `.cs` changes. Discovered the
DO-37 task row's OWN carried-forward note ("CLAUDE.md status-marker removal/changelog entry
remains a future SK.16.Docs session's task") was itself stale — the `[STATUS: Blocked — pending
09.Search P-354]` marker had ALREADY been removed from `CLAUDE.md`'s `Search/` Interface
Contracts block during the prior `SK.16.Core` pass (grep-confirmed zero live occurrences,
only historical changelog narrative mentions it), and both new overloads/`LastBulkWriteOptions`
already had full XML doc coverage written at C-108/C-110 implementation time. The only genuine
remaining work: a stale forward-looking sentence in `CLAUDE.md`'s `Search/` Folder/Namespace
Map narrative paragraph ("only Docs (DO-37) remains...") needed correcting, plus the required
`CLAUDE.md`/state-map.md changelog entries. Lesson: a task row's own "remains a future session's
task" note can itself be stale by the time that future session runs — always re-verify against
the live file, never trust even this domain's own carried-forward task text at face value.

**CORRECTION to a prior version of this memory**: I had previously written that `state-map-phase`
Sub-map mode Step S8a Case 3 (standard lifecycle phase, no `Root Backlog ID` column) means the
root Phase Backlog `### P-NNN` entry is simply left `◐ Dispatched` until manually closed by some
other mechanism. That is not how this domain actually operates in practice — direct inspection of
P-306/P-335/P-352 (all WO-050/WO-053/WO-054's dependent `16.Testing` fake-update phases, all
mapping to a standard `Docs` phase key with no `Root Backlog ID` column) showed EVERY ONE was
manually marked `● Complete` with all acceptance-criteria checkboxes checked and cited, as part of
the SAME closing session that finished the domain's last task — never left dangling. Established
convention: when a `16.Testing`-only WO-specific Phase Backlog entry's last task closes, manually
edit that root `### P-NNN` entry's `**Status:**` line to `● Complete` and check every acceptance
criterion with a citation to the task/date that satisfied it, then append two root changelog
lines — one `{domain} → {phase} (●, unchanged)` line (Current Phase stays wherever it already was,
per `[[feedback_root_phase_left_as_is]]`) and one `Phase Backlog P-NNN → ● Complete` line. Do this
regardless of what Step S8a's literal Case 3 text says — S8a is scoped to the generic skill
mechanism; this domain's actual practice for its OWN dependent-fake-update phases goes further.
