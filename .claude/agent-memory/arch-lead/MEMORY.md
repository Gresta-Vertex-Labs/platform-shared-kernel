# Memory Index

- [WO-003: Cross-Service Cache Invalidation Decision](project_wо003_cross_service_cache_invalidation.md) — ICacheInvalidationBus pattern, channel naming, FusionCache backplane vs cross-service distinction
- [WO-023: Caching Redis Topology Split](project_wo023_caching_redis_topology.md) — Redis split into Core + L2/Locking/HashStore/PubSub packages; PubSub stays in 02.Caching not 07.Messaging
- [WO-028: ServiceDefaults Gold-Standard Audit](project_wo028_servicedefaults_audit.md) — broken tenant-strategy name-mapping masked by a false-confidence test, blocking sync DB call, magic strings, undelivered health check adapters
- [Phase Numbering State](project_phase_numbering.md) — last P-NNN and WO-NNN written; starting point for next assignment
- [WO-031: Presentation Build-Out](project_wo031_presentation_buildout.md) — 14.Presentation P-192-199, correlation-id decoupled from 13.ServiceDefaults, ProblemDetails/Result-HTTP governance rule
- [WO-032: Integration Build-Out](project_wo032_integration_buildout.md) — 15.Integration P-200-204, accepted pre-drafted webhook brain, Core split into Signing/Dispatch sub-phases
