---
name: cross-domain-gated-scaffold-tasks
description: How to judge whether a Scaffold/Core task that depends on another domain's phase is genuinely actionable
type: feedback
---

Some `06.Persistence` phase tasks are worded as "confirm X resolves cleanly against [another
domain]'s new contract" (e.g. S-20/P-448: confirm `SharedKernel.Cryptography`'s ProjectReference
resolves against `01.Core`'s re-published ASYNC `IEncryptionKeyProvider` once P-446 ships).

**Why this matters:** the sub state-map's own Cross-Domain Dependencies table can be STALE —
it may say "Pending" for a dependency that has since shipped (was true for 03.Domain's Money/
P-439 by 2026-09-02), or still say "Pending" correctly for one that hasn't (01.Core's P-446/async
`IEncryptionKeyProvider`, still `◐` Dispatched as of 2026-09-02, not `●` Complete). Never trust
the table at face value — it is a snapshot that goes stale as soon as another domain's session
runs.

**How to apply:** Before marking a cross-domain-gated task complete:
1. Grep the root `state-map.md`'s `### P-NNN` entry for the dependency and read its
   `**Status:**` line directly — `◐ Dispatched` means DESIGNED but not implemented;
   only `● Complete` means the real contract exists on disk.
2. Additionally verify on disk (grep the actual source file/interface signature) rather than
   trusting the status line alone — status lines can also lag reality.
3. If the dependency is still `◐`/`○`, leave the gated task `○` and say so plainly in the
   report — do NOT fabricate a verification against a contract that doesn't exist yet, even if
   the ProjectReference itself already compiles against the OLD (still-synchronous) version.
4. Consider filing the gated task into the sub state-map's `## Blocked` section (Task | Phase
   Key | Blocker format) rather than leaving it silently `○` with no explanation — makes the
   real cross-domain dependency visible to the next session without re-deriving it.
5. When a dependency DOES ship (case in point: 03.Domain's Money, P-439, closed 2026-09-02),
   update the Cross-Domain Dependencies table row status from `Pending` to `Available` as part
   of the same session that consumes it — don't leave it stale for the next reader.
