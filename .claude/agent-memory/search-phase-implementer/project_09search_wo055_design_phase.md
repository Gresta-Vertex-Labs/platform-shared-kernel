---
name: project_09search_wo055_design_phase
description: SK.09.Design + SK.09.Scaffold (WO-055) session pattern — arch-lead pre-writes design content at dispatch, implementer's job is verify+gap-fill, and the root Current-Phase-stays-Published convention for post-Published WO revisits (confirmed twice now)
type: project
---

**Session (2026-08-10): SK.09.Design D-29–D-35 (WO-055 hardening pass) closed, 35/35.**

**Pattern: for a hardening-WO Design phase, arch-lead may already have pre-written the full design
content into the domain's own `CLAUDE.md` at dispatch time** (visible via the root changelog entry
crediting "arch-lead, WO-055, P-353/P-354" for writing the queued-note prose directly into
`09.Search/CLAUDE.md`'s Interface Contracts/Models sections, before this agent ever opened the file).
**Why:** unlike the original WO-044 Design phase (which search-arch-planner also pre-wrote in full —
see the WO-044 precedent recorded in the file's own 2026-07-19 changelog entry), a small hardening WO's
design decisions are simple enough that arch-lead locks them inline while dispatching rather than
handing off a bare task list.
**How to apply:** when a Design-phase task table's acceptance criteria already appear satisfied by
existing CLAUDE.md prose, do not re-author from scratch — read the actual acceptance-criteria clauses
line by line against the existing prose and grep for the specific claims (exact record/member names,
"why X not Y" rationale sentences, explicit confirmations like "zero new PackageReference"). Genuine
gaps are usually narrow and specific, not wholesale absence. This session found exactly three real gaps
across seven tasks (D-33/D-34/D-35) — everything else was already complete and correct on first read.

**Root state-map convention, confirmed by direct precedent in the file (not inferred): once a domain's
root Domain Summary Board `Current Phase` column reaches `Published`, a later WO revisiting a
same-named standard-lifecycle phase key (e.g. `SK.09.Design` again) does NOT revert that column back to
`Design`.** Evidence: `06.Persistence` row explicitly narrates "SK.06.Docs closed (64/64, WO-053)" in
its Summary text while the Current Phase column stays `Published`; `07.Messaging` uses a
WO-specific phase-key name (`PackagingRecipes`) as the Current Phase value rather than a standard
lifecycle name. **How to apply:** when calling `state-map-phase` in Sub-map mode and the promotion
condition fires for a phase key whose "Maps to Root Phase" name is a standard lifecycle phase
(Design/Scaffold/Core/Tests/Docs/Published) on a domain whose root row is already `Published`, do NOT
mechanically overwrite Current Phase to the earlier name — keep it at `Published` (state unchanged,
`●`) and narrate the sub-phase completion in the Summary: Done text instead. This is a judgment
override of the skill's generic S8 mechanical instruction, justified by direct precedent already
present in the same file for the exact same scenario (see [[project_09search_published_phase]] and the
07.Messaging/06.Persistence rows in root `state-map.md` as of 2026-08-10).

**Case 3 (standard lifecycle phase name) in `state-map-phase` Step S8a correctly skips Phase Backlog
closing** even though P-353/P-354 exist as named backlog entries — because those P-NNN entries span
multiple phase keys each (Design + Scaffold + Core + Tests + Docs + Published tasks all rolled into one
P-NNN), a single phase key reaching `●` does not mean the P-NNN is done. Do not close P-353/P-354 until
every phase key's tasks belonging to them are `●`.

See also [[project_09search_t26_and_completion]] (state-map "annotate not rewrite" pattern, applied
again this session — Package Board rows were narrowed in place, not rewritten wholesale) and
[[project_09search_published_phase]] (the original Published-phase session this WO-055 pass follows).

---

**Session (2026-08-10, same day): SK.09.Scaffold S-14 (WO-055/P-354) closed, 14/14 — a single-task
Scaffold session.**

**On-disk state at session start:** all of S-01–S-13 already `●` from the original WO-044 pass; only
S-14 (add `SearchBulkWriteOptions.cs` as a namespace-only stub in `SharedKernel.Search.Abstractions/
Models/`) remained `○`. The phase spec itself flagged the one real risk worth checking before writing
anything: this package's `.csproj` landed `GenerateDocumentationFile=true`/`TreatWarningsAsErrors=true`
at the *Docs* phase (2026-07-20), a full phase *after* the original Scaffold pass (2026-07-19) that
established the "bare `namespace X;`, no class, no docs" stub convention — so a bare stub file added
this late in the pipeline needed to be verified clean against settings that did not exist when the
convention was set, not assumed clean by analogy.

**Verified, not assumed: a bare `namespace X;` file with zero members produces zero CS1591/other
warnings under `TreatWarningsAsErrors`,** because `GenerateDocumentationFile` only fires XML-doc
diagnostics against declared public members — an empty file has none. `dotnet build -c Release` on the
target project confirmed 0 errors/0 new warnings (the only two warnings present are pre-existing CS1574s
in the unrelated `SharedKernel.Core` dependency, not this package), and the full 155-test
`SharedKernel.Search.Abstractions.Tests` suite re-ran green with zero regression. **How to apply:** when
a phase spec explicitly flags "verify this doesn't break the build under settings that landed after the
original convention was set," don't skip the verification even for a one-line stub file — build the
specific target project (not just trust the pattern) and state the verification outcome explicitly
rather than silently assuming the old convention still holds.

**Root state-map "Current Phase stays Published" convention reconfirmed a second time, same day, same
WO:** promoting `SK.09.Scaffold` to 14/14 `●` again left the root Domain Summary Board's Current Phase
column at `Published` (not reverted to `Scaffold`), narrating the Scaffold-phase completion in the
Summary: Done text instead — identical mechanics to the `SK.09.Design` promotion earlier the same
session (see above). Two same-day promotions for the same domain is itself a useful data point: the
convention isn't a one-off judgment call, it's the mechanical, repeatable answer every time a
post-Published WO's phase key reaches `●` again. Root Changelog entries for a domain already at
`Published` accumulate as repeated `- [date] {NN} → Published (●, unchanged) — promoted from
{phase_key} (...)` lines rather than one line per domain per session — this is expected and matches the
append-only convention, not a formatting mistake to "clean up."

**Scope discipline reconfirmed** (see [[feedback_scaffold_scope_discipline]]): the phase spec was
explicit that the *real* `SearchBulkWriteOptions` record (guard clauses, XML docs, `.Default` static
instance) belongs to Core task C-51, not this Scaffold task — resisted the temptation to "just write the
real thing since the design is already fully locked in CLAUDE.md." A fully-designed type is still not
this phase's job to implement if the task table says stub.
