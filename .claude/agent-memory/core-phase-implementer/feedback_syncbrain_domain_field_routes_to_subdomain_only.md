---
name: feedback-syncbrain-domain-field-routes-to-subdomain-only
description: sync-brain's mode detection is purely syntactic on the presence of a "domain:" field — calling it with "domain: 01.Core" only ever touches 01.Core/CLAUDE.md, never the root CLAUDE.md
metadata:
  type: feedback
---

The `sync-brain` skill picks Root mode vs. Sub-domain mode by a single syntactic check: "if the input contains `domain:`, use Sub-domain mode; otherwise Root mode." It does not infer mode from what the change actually is.

**Why this matters:** `core-phase-implementer`'s own system prompt says "call sync-brain with `domain: 01.Core`" after finishing a phase. But several 01.Core phases (P-292, P-293, P-294) require a **root** `CLAUDE.md` edit too — e.g. stripping a "design-locked, queued P-29X/WO-049" qualifier from the root "What Goes Where" table or the Magic String Convention section once the phase ships. A single call with `domain: 01.Core` will silently only touch `01.Core/CLAUDE.md` and never reach the root file, even if the prompt text explicitly asks about root sections — the mode is locked in before the prompt content is even considered.

**How to apply:** When a shipped phase's root-`CLAUDE.md` references (Folder Map summary line, "What Goes Where" row, Magic String Convention section, etc.) still read as "design-locked"/"queued"/"pending" after the phase completes, grep the root `CLAUDE.md` directly for the phase ID (`P-29X`) to confirm, then issue a **second, separate** sync-brain call with no `domain:` field at all (pure prose describing the root-level edit) — this is what actually reaches Root mode. Doing both calls back-to-back (sub-domain first, then root) is the correct two-step pattern for any phase that touches both files; this mirrors the pattern already established for P-292/P-293 in the root Changelog ("the What Goes Where row's ... qualifier removed").
