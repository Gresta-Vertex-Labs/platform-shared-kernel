---
name: project_p256_eventid_allocation
description: 14.Presentation's LoggingEventIdRanges sub-block allocation and the three assigned EventIds (WO-041, P-256)
metadata:
  type: project
---

> WO-086 (2026-09): `CorrelationIdMiddleware` (EventId 14000) was deleted. The domain now has six packages (`.Core`, `.WebApi`, `.Grpc`, `.SignalR`, `.SignalR.Redis`, `.GraphQL`); shipped code uses `LoggingEventIdRanges.Presentation + n` with WebApi at +1…, SignalR at +100…, Grpc at +200…. Read `14.Presentation/CLAUDE.md` for the current sub-block table before assigning an id.

WO-041 P-256 locked 14.Presentation's `EventId` allocation inside `LoggingEventIdRanges.Presentation` (14000, from `01.Core`'s registry, P-249):

- `SharedKernel.Presentation.WebApi` sub-block: 14000–14099 (declared first in the Packages table)
  - `CorrelationIdMiddleware` = 14000
  - `SharedKernelExceptionHandler` = 14001
- `SharedKernel.Presentation.SignalR` sub-block: 14100–14199 (declared second)
  - `HubExceptionMappingFilter` = 14100

These are the only three `[LoggerMessage]`-attributed methods that existed in this domain as of P-256. Any future `[LoggerMessage]` method added to either package continues sequentially within that package's sub-block (next WebApi id = 14002, next SignalR id = 14101) — never reuse a retired id, never renumber an existing one.

**Why:** Prior to this phase, both methods relied on compiler-auto-numbered `EventId`s, which silently renumber if a method is added/removed/reordered in the same class — a platform-wide hazard `01.Core`'s P-249 registry and `00.Governance`'s P-250 (`LoggingEventIdIntegrityAssertion`) exist to close.

**How to apply:** When any future phase adds a new `[LoggerMessage]` call site to `SharedKernel.Presentation.WebApi` or `.SignalR`, assign the next free id in that package's own 100-wide sub-block — check `14.Presentation/CLAUDE.md`'s "Logging EventId assignment & correlation verification" subsection for the current high-water mark before assigning.

**Cross-domain dependency note (RESOLVED 2026-07-14):** C-14–C-16 were gated on `01.Core`'s P-249 phase landing `LoggingEventIdRanges.Presentation` as a real compiled `const int`. `01.Core` shipped it and the full remaining P-256 task set (C-14–C-18, T-11–T-13, DO-06/DO-07, P-06) closed in one session once confirmed. See [[project_cross_domain_gating_pattern]] for the general pattern this and P-262/WO-042 both follow.
