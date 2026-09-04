---
name: project-core-domain
description: 01.Core implementation status — twelve packages Published, all phase keys ● as of P-487/WO-080
metadata:
  type: project
---

As of 2026-09-04, `01.Core` ships twelve published packages (Primitives, Core, Guards, Configuration,
FeatureManagement, Cryptography, Compression, Validation, Validation.FluentValidation,
Cryptography.KeyVault.Azure, DataPrivacy, Localization). Every phase key in `01.Core/state-map.md` is `●`
— the domain has no queued work of its own at time of writing. Before starting any session, re-check
`01.Core/state-map.md`'s Phase Key Registry and root `state-map.md`'s Phase Backlog for anything dispatched
since — this note goes stale fast, this domain gets small additive work orders frequently (WO-067 through
WO-080 all landed within about two weeks of each other).

**Why:** `01.Core` is the platform's most-depended-upon layer, so almost every cross-domain security/audit
review (WO-058, WO-060, WO-076, WO-078, WO-080) ends up dispatching a small, additive, single-package-scope
phase here even when the review's main subject is a different domain.

**How to apply:** Read the domain's own `state-map.md` Active Work paragraph first — it is kept accurate
and states plainly whether anything is queued, rather than making you diff the whole Phase Key Registry.
