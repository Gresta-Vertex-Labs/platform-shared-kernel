---
name: project_wo031_phasing
description: WO-031 six-phase dispatch (P-192–P-198) for 14.Presentation — Design sign-off through Published
metadata:
  type: project
---

WO-031 dispatched all six standard phases for `14.Presentation` in one batch (P-192 Design, P-193 Scaffold, P-194/P-195 Core split by package, P-196 Tests, P-197 Docs, P-198 Published). This is the first full-domain phase set processed for this domain — prior session only initialized the empty `state-map.md`/`CLAUDE.md` skeleton with zero tasks.

**Why:** Every other completed domain (11.Communication, 12.Security, 13.ServiceDefaults, 03.Domain) ran Design before Scaffold to lock the public surface first, given this domain's high blast radius (every consuming API/socket service derives error shape, versioning, and hub behavior from it).

**How to apply:** When P-192's successor work arrives (Scaffold dispatch, etc.), task IDs continue from where this batch left off: D-11, S-10, C-13, T-10, DO-05, P-05 were the last IDs issued as of WO-031 closeout. See [[interface_contracts_already_drafted]] for why P-192 was a sign-off pass rather than fresh contract design.

**Update (WO-041, P-256, 2026-07-09):** last IDs issued are now D-13, S-10 (unchanged), C-18, T-13, DO-07, P-06 — see [[project_p256_eventid_allocation]] for what that phase added. Always re-check the actual state-map.md for the true last ID before assuming this note is current — this is a navigational aid, not a live counter.

**Update (WO-074/P-468 + WO-078/P-484, 2026-08-26):** last IDs issued are now D-86, S-35, C-87, T-80, DO-34, P-28 (jumped a long way — WO-042/WO-058/WO-062/WO-063 all issued IDs between the 2026-07-09 update above and this one; this note does not reconstruct that intermediate history, just the current tail). This batch also introduced a THIRD package into this domain, `SharedKernel.Presentation.Grpc` (WO-074, design-locked only, Core not yet implemented) — see [[project_grpc_webapi_reference_decision]] for the durable architectural precedent that batch established. Still true: always re-check the actual state-map.md for the true last ID before trusting any note here.
