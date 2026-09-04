---
name: shared-file-protocol-concurrent-dispatch
description: Root state-map.md, root CLAUDE.md, and Platform.SharedKernel.slnx are off-limits during multi-domain concurrent dispatch — other domain implementers edit them simultaneously
type: project
---

When multiple domain phase-implementer agents are dispatched concurrently (observed during the
19.Scheduling P-464 session, alongside at least a 12.Security and a 01.Core/root session editing
`Directory.Packages.props` at the same time), the dispatching instructions impose a SHARED-FILE
PROTOCOL:

- Never edit `Platform.SharedKernel.slnx` — new projects build and test fine unregistered via direct
  `dotnet build`/`dotnet test` on the `.csproj` path; report the new project paths in the final
  summary for the dispatcher to register centrally.
- Never edit the root `state-map.md` or root `CLAUDE.md` — only your own domain's sub-files.
- A shared file like root `Directory.Packages.props` may need a single minimal, explicitly-authorized
  append (e.g. one new `<PackageVersion>` entry) — re-read it immediately before editing, since another
  concurrent session may have changed it since you last saw it, and make the smallest possible diff.

**How to apply:** when a domain's own state-map shows a task blocked on cross-domain infrastructure
(e.g. "needs a `Directory.Packages.props` pin, outside this domain's jurisdiction"), check whether the
CURRENT phase-command's dispatch instructions explicitly grant permission to do it directly anyway —
that grant can override a stale state-map note written by an earlier planning pass. Don't assume the
state-map's framing of "whose jurisdiction" is still current; the dispatcher's live instructions win.
When a domain's own state-map's Scaffold-equivalent phase includes `.slnx` registration as a task,
mark that specific task `⚑` (not `●`) if this session was told not to touch `.slnx`, and don't
propagate the domain to root "Published"/complete until that one task is confirmed to have landed —
report the exact project paths clearly instead so the dispatcher (or a human) can close the loop.
