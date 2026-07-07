# Memory Index

- [WO-003: Cross-Service Cache Invalidation Decision](project_wо003_cross_service_cache_invalidation.md) — ICacheInvalidationBus pattern, channel naming, FusionCache backplane vs cross-service distinction
- [WO-023: Caching Redis Topology Split](project_wo023_caching_redis_topology.md) — Redis split into Core + L2/Locking/HashStore/PubSub packages; PubSub stays in 02.Caching not 07.Messaging
- [WO-028: ServiceDefaults Gold-Standard Audit](project_wo028_servicedefaults_audit.md) — broken tenant-strategy name-mapping masked by a false-confidence test, blocking sync DB call, magic strings, undelivered health check adapters
- [Phase Numbering State](project_phase_numbering.md) — last P-NNN and WO-NNN written; starting point for next assignment
- [WO-031: Presentation Build-Out](project_wo031_presentation_buildout.md) — 14.Presentation P-192-199, correlation-id decoupled from 13.ServiceDefaults, ProblemDetails/Result-HTTP governance rule
- [WO-032: Integration Build-Out](project_wo032_integration_buildout.md) — 15.Integration P-200-204, accepted pre-drafted webhook brain, Core split into Signing/Dispatch sub-phases
- [WO-034: Cryptography Generalization](project_wo034_cryptography_generalization.md) — declined domain move + abstractions split for SharedKernel.Cryptography; accepted IPasswordHasher rename to secret-agnostic contract
- [WO-035: Application Build-Out](project_wo035_application_buildout.md) — 05.Application P-214-219, accepted existing 5-behavior design, added Authorization+Idempotency via local-seam pattern, seven-step pipeline order
- [WO-039: Application Deep-Dive Review](project_wo039_application_review.md) — 05.Application P-236-243; fire-and-forget self-blocking bug, incomplete reflection elimination, unwired governance tests, idempotency replay gap
- [Verify shipped code, not docs](feedback_verify_shipped_code_not_docs.md) — read actual .cs files + test workaround comments before trusting CLAUDE.md prose or checked acceptance boxes on a "Complete" domain
- [WO-040: Application DX Gap Audit](project_wo040_application_dx_audit.md) — P-244-248 across 16.Testing/05.Application/13.ServiceDefaults/00.Governance; fakes, public pipeline harness, redacted logging, OTel wiring, 3 new analyzers
