---
name: project_p256_eventid_allocation
description: 14.Presentation's LoggingEventIdRanges sub-block allocation and the three assigned EventIds (WO-041, P-256)
metadata:
  type: project
---

WO-041 P-256 locked 14.Presentation's `EventId` allocation inside `LoggingEventIdRanges.Presentation` (14000, from `01.Core`'s registry, P-249):

- `SharedKernel.Presentation.WebApi` sub-block: 14000–14099 (declared first in the Packages table)
  - `CorrelationIdMiddleware` = 14000
  - `SharedKernelExceptionHandler` = 14001
- `SharedKernel.Presentation.SignalR` sub-block: 14100–14199 (declared second)
  - `HubExceptionMappingFilter` = 14100

These are the only three `[LoggerMessage]`-attributed methods that existed in this domain as of P-256. Any future `[LoggerMessage]` method added to either package continues sequentially within that package's sub-block (next WebApi id = 14002, next SignalR id = 14101) — never reuse a retired id, never renumber an existing one.

**Why:** Prior to this phase, both methods relied on compiler-auto-numbered `EventId`s, which silently renumber if a method is added/removed/reordered in the same class — a platform-wide hazard `01.Core`'s P-249 registry and `00.Governance`'s P-250 (`LoggingEventIdIntegrityAssertion`) exist to close.

**How to apply:** When any future phase adds a new `[LoggerMessage]` call site to `SharedKernel.Presentation.WebApi` or `.SignalR`, assign the next free id in that package's own 100-wide sub-block — check `14.Presentation/CLAUDE.md`'s "Logging EventId assignment & correlation verification" subsection for the current high-water mark before assigning.

**Cross-domain dependency note:** C-14–C-16 (the actual code change assigning these ids) are gated on `01.Core`'s P-249 phase landing `LoggingEventIdRanges.Presentation` as a real compiled `const int` — as of 2026-07-09 that phase (`01.Core/state-map.md` D-29/C-42/T-33/DO-15) is still `○` Pending. Don't assume the constant exists yet; verify `01.Core`'s state-map before dispatching the Core-phase implementer for this work.
