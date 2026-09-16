---
name: feedback_claudemd_historical_vs_live
description: Which parts of 16.Testing/CLAUDE.md to correct vs. leave alone when a fake is redesigned/removed
metadata:
  type: feedback
---

`16.Testing/CLAUDE.md` is ~4000 lines and mixes two kinds of content: **live reference**
(Folder/Namespace Map table, per-folder Interface Contracts code blocks, Implementation Rules
bullets, DI Registration worked code samples, Test Rules bullets) and a **dated changelog**
appended at the end of the file (`- [YYYY-MM-DD] ...` entries, one per session).

**Rule:** when a fake/interface is removed or redesigned, fix every *live reference* occurrence
— including worked examples inside Implementation Rules bullets that name the removed type as
"the reference example for this rule" (these are load-bearing, not just prose) and DI
Registration code samples that would no longer compile if pasted verbatim. Do **NOT** rewrite
dated changelog entries describing what was true at the time they were written, even when they
now describe a removed type — mirrors root `CLAUDE.md`'s own explicit precedent ("per-package
version numbers quoted... are historical record, not current state").

**Why this matters:** a naive `grep` for a removed type name after an edit pass will still
return dozens of hits in this file — most are legitimate historical record or a bullet's own
explicit "REMOVED at P-nnn" callout (which correctly *mentions* the old name to explain the
removal). Don't treat every grep hit as a bug to fix; read each hit's context first. In this
session (P-544), genuinely-stale live hits were: the Folder/Namespace Map row, two
Implementation Rules bullets (local-seam disambiguation example, optional-second-interface
worked example), one DI Registration code sample (two lines + their comments), and one Test
Rules bullet. Everything under the "REMOVED P-544 (..." bullets and every `- [YYYY-MM-DD]`
line was correctly left untouched.

**How to apply:** After a redesign migration, grep the domain's CLAUDE.md for removed
type/interface names, then triage each hit: dated changelog line → leave; bullet already
framed as "REMOVED/CHANGED at P-nnn" → leave (it's the correct documentation of the removal);
anything else presented as current-state fact or a runnable code sample → fix.
