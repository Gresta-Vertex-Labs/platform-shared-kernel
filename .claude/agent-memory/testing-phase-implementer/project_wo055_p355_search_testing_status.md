---
name: project_wo055_p355_search_testing_status
description: WO-055/P-355 Search/ bulk-write-throttle testing status — Tests phase (T-75–T-77) closed 2026-08-11
metadata:
  type: project
---

WO-055/P-355 extended the already-`**implemented**` `Search/` folder with an opt-in bulk-write
throttle surface on `InMemorySearchIndex<TDocument>` — two new 4-arg `IndexManyAsync`/
`DeleteManyAsync` overloads carrying a new `SearchBulkWriteOptions` record, plus a
`LastBulkWriteOptions` audit property. Design/Scaffold/Core (D-180–D-183/S-46/C-108–C-110)
closed 2026-08-10/11 in prior sessions. This session closed Tests (T-75–T-77, 77/77 → `●`,
promoted to root) — only Docs (DO-37) remains to fully close WO-055/P-355's `16.Testing`
contribution.

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

Root `state-map.md`'s Domain Summary Board Current Phase left at `Published` (unchanged),
matching the established `06.Persistence`/`07.Messaging`/`09.Search` precedent for a
post-Published hardening WO revisiting a same-named phase key — see
`[[feedback_root_phase_left_as_is]]`. `Tests` is a standard lifecycle phase key with no
individual Phase Backlog entry to auto-close (`state-map-phase` S8a Case 3) — P-355 stays
`◐` Dispatched in the root Phase Backlog until DO-37 also closes.
