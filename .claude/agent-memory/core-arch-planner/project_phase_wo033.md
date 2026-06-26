---
name: project_phase_wo033
description: WO-033 SharedKernel.Cryptography phase sequence — P-205/206 locked design, P-207/208/209 are pure execution tracking with zero contract drift
type: project
---

WO-033 adds `SharedKernel.Cryptography` (6th package in 01.Core) for password hashing, AES-GCM
encryption, RSA/ECDSA + HMAC signing, and secure random generation — all pure BCL
`System.Security.Cryptography`, zero third-party NuGet, deliberately decoupled from `12.Security`
(identity/JWT/OIDC). `12.Security.Oidc` may depend on it; never the reverse.

Phase sequence observed:
- P-205/P-206 (Design + Scaffold) — fully locked every interface, type shape, and DI extension
  signature in `01.Core/CLAUDE.md` *and* pre-populated the state-map task rows C-33→C-38 (Core),
  T-23→T-28 (Tests), DO-10→DO-11 (Docs), P-10→P-12 (Published) — all at `○`.
- P-207/P-208/P-209 (this round) — labeled as "Core"/"Tests"/"Docs+Published" implementation
  phases, but their acceptance criteria are verbatim restatements of rules and contracts already
  written into `01.Core/CLAUDE.md` by P-205/P-206. There was nothing left to design.

**Why this matters:** when a WO arrives split into Design-phase IDs (P-20x) and Implementation-phase
IDs (P-20x+2) for the *same* package, check whether the design phase already locked the full
contract before treating the later phase as needing new architectural decisions. If so, the
correct planner action is execution-tracking only: add a state-map phase section that
cross-references the existing C-/T-/DO-/P- task IDs (don't duplicate them with new IDs), bump
the Package Board's Current Phase, and add a Changelog line in both files noting "no contract
changes — pure execution." Do not re-derive or restate the interface contracts in CLAUDE.md;
they're already correct from the design phase. See [[project_phase_p042]] for the precedent of
adding a phase-key block under an existing package without duplicating task IDs.

**How to apply:** Before designing anything for a new P-xxx phase, grep `01.Core/CLAUDE.md` for
the package/interface names named in the phase's "What is needed" section. If they're already
fully specified (interface signatures, implementation rules, AOT notes, test rules all present),
the phase is execution-only — skip Step 2 (Phase Design) and go straight to recording it as a
cross-referencing phase-key block in state-map.md plus a forward-looking Changelog line.
