---
name: project-core-domain
description: 01.Core implementation status — twelve packages Published, all phase keys ● as of WO-083/P-514 (2026-09-09)
metadata:
  type: project
---

As of 2026-09-09, `01.Core` ships twelve published packages (Primitives, Core, Guards, Configuration,
FeatureManagement, Cryptography, Compression, Validation, Validation.FluentValidation,
Cryptography.KeyVault.Azure, DataPrivacy, Localization). Every phase key in `01.Core/state-map.md` is `●`
— the domain has no queued work of its own at time of writing, including the five WO-083 security-cluster
phases (`SK.01.P510`→`SK.01.P514`, the last of the `01.Core` gold-standard audit's follow-on findings).
Before starting any session, re-check `01.Core/state-map.md`'s Phase Key Registry and root
`state-map.md`'s Phase Backlog for anything dispatched since — this note goes stale fast, this domain gets
small additive work orders frequently (WO-067 through WO-083 all landed within about a month of each other).

WO-083/P-514 (the `ITotpReplayGuard` atomic-replay-guard breaking change) left two OUT-OF-DOMAIN companion
migrations queued at time of writing: `16.Testing`'s `FakeTotpReplayGuard.cs` (P-527) and
`12.Security.Totp`'s own test-local `FakeTotpReplayGuard.cs` PLUS its production `Challenge/
TotpChallengeService.cs` call site, which passes `ct` positionally and will fail to compile once P-514
ships (P-528, this production-source finding was new — not in the original design). Check whether those
have landed before assuming a full-solution build is green.

**Why:** `01.Core` is the platform's most-depended-upon layer, so almost every cross-domain security/audit
review (WO-058, WO-060, WO-076, WO-078, WO-080) ends up dispatching a small, additive, single-package-scope
phase here even when the review's main subject is a different domain.

**How to apply:** Read the domain's own `state-map.md` Active Work paragraph first — it is kept accurate
and states plainly whether anything is queued, rather than making you diff the whole Phase Key Registry.
