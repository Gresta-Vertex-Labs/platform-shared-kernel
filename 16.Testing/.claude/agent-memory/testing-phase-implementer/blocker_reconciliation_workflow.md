---
name: blocker_reconciliation_workflow
description: How to reconcile stale ⚑ Blocked entries in 16.Testing/state-map.md against real on-disk state before implementing — the established, repeated pattern in this domain's history.
type: project
---

`16.Testing/state-map.md` has a long, repeated history (14+ occurrences by 2026-08) of phases being
designed AHEAD of the upstream domain actually shipping the interface they fake. The state-map's
own convention, reinforced every time: **never trust a domain's "design-locked"/"○ Pending"
self-report or another domain's `CLAUDE.md` prose — always re-verify the target `.cs` file exists
on disk with a direct `Glob`/`grep`/`cat` before either marking something `⚑` Blocked or clearing an
existing blocker.**

**Why:** Multiple false "still blocked" claims were found stale across sessions (e.g. 2026-08-24
Caching pass, 2026-08-13 Security pass) simply because nobody re-checked disk before trusting the
last blocker note.

**How to apply:**
1. Grep the target package's `.csproj`/interface file directly — never assume from a `[STATUS:
   Planned]` marker or another domain's own state-map claim.
2. **Clearing a blocker means "no longer blocked," not "implemented."** If you clear a blocker but
   don't have time/scope to implement the code this pass, flip `⚑`→`○`, never `⚑`→`●`. This
   distinction is explicitly called out multiple times in `16.Testing/state-map.md`'s own history
   (e.g. the 2026-08-28 Design-phase pass on Money).
2026-09-01 update: also check whether a DIFFERENT domain's phase already migrated shared
`16.Testing` code out-of-band as a side effect of its own breaking change (see
[[cross_domain_shared_fake_migration]]) — a blocker can be "already resolved in code" even when
`state-map.md` still shows it `⚑`, because the domain that resolved it wasn't `16.Testing`'s own
session and had no reason to update this domain's tracking file.
3. When you DO implement, always re-verify the exact interface member signatures/algorithm details
   against the real shipped `.cs` source (never from the planner's `CLAUDE.md` prose alone, which
   can drift or be imprecise) before writing the fake.
