---
name: wo044-design-phase
description: Status of WO-044 (P-272/P-273/P-274) — the full 09.Search domain plan, as of 2026-07-19
metadata:
  type: project
---

WO-044 dispatched three phases in one batch: P-272 (`SharedKernel.Search.Abstractions` contract
finalization), P-273 (`SharedKernel.Search.Meilisearch` provider), P-274
(`SharedKernel.Search.ElasticSearch` provider). All three were fully designed and written into
`09.Search/state-map.md` and `09.Search/CLAUDE.md` in one session (2026-07-19), before either
provider had a single line of implementation. This mirrors the `08.Storage` precedent (P-265) of
locking the abstraction ahead of its providers.

**State-map shape:** all six phase sections populated in one pass — 28 Design (D-01–D-28), 13
Scaffold (S-01–S-13), 48 Core (C-01–C-48), 26 Tests (T-01–T-26), 8 Docs (DO-01–DO-08), 8 Published
(P-01–P-08) = 131 tasks total, every one `○` Not started. Work order WO-044 is the first work this
domain has ever received — root state-map carried no prior phase/task for `09.Search` before this.

**Why:** the user/arch-lead handed me all three phases (P-272, P-273, P-274) as one batch rather
than one at a time, and the seam rule means the two providers cannot be honestly designed without
the neutral contract being locked first — so I designed all three in dependency order within the
same session rather than waiting for three separate dispatches.

**How to apply:** when a future session resumes this domain (e.g. dispatching Scaffold), the full
task breakdown is already in `state-map.md` — do not redesign it. Read the existing D-xx/S-xx/C-xx
notes first; they carry the actual engine-behavior citations (SDK method names, breaking-change
notes, response-shape sniffing logic) that a phase-implementer will need and that are expensive to
re-derive. If a new capability request arrives, extend the existing task numbering (D-29, C-49,
etc.) rather than renumbering.

**Known Design-phase open items deliberately deferred to Scaffold/Core (not oversights):**
- `SearchStreamException`'s exact base type in `SharedKernel.Primitives` — resolved at S-12 by
  reading the real exception hierarchy on disk.
- The exact ES 9.4.2 constructor/initializer shapes for `MatchQuery`/`TermQuery`/`NumberRangeQuery`
  (D-23 flags this explicitly as "unverified against the shipped assembly").
- Whether `getmeili/meilisearch`'s community image supports the full 0.20.0 SDK surface (flagged in
  Test Rules as unconfirmed).
