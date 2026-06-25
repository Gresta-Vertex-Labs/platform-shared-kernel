---
name: interface_contracts_already_drafted
description: 14.Presentation/CLAUDE.md ships full Interface Contracts text (signatures, NOTE blocks) before any Design phase task existed
metadata:
  type: project
---

The domain's `CLAUDE.md` already contained complete, detailed Interface Contracts sections for both `SharedKernel.Presentation.WebApi` and `SharedKernel.Presentation.SignalR` (full method signatures, behavior NOTE blocks, the Swashbuckle/NSwag rejection rationale, the Redis-backplane-vs-PubSub distinction) — written during initial brain setup, before WO-031's P-192 Design phase ever ran.

**Why:** This is unusual relative to other domains where Design-phase tasks drive the contract drafting. Here the brain was front-loaded with the full intended shape at initialization time, and P-192's job was a formal sign-off/audit pass (confirm each contract, explicitly remove the incorrect `13.ServiceDefaults` coupling note that had been left in as a "Pending" cross-domain dependency) rather than originating new contract design from scratch.

**How to apply:** When processing a Design phase (or any phase) for this domain, always read the full current `CLAUDE.md` Interface Contracts section first — it may already describe the target shape. Don't re-derive contracts independently; check whether the task is "confirm/lock what's drafted" vs. "design something new." If `CLAUDE.md` says "planned public surface," a Design-phase sign-off should change that heading to "confirmed public surface" once tasks land, per [[project_wo031_phasing]].
