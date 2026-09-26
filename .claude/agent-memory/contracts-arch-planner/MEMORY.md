# Contracts Arch Planner — Memory Index

- [EventEnvelope design decisions](event-envelope-decisions.md) — (historical; constraint is IIntegrationEvent since P-543) original IDomainEvent constraint, EventId copy semantics, nullable CorrelationId/TenantId, EventVersion; WO-051 AggregateId doc-drift defect; WO-052 TenantId addition
- [Phase sequencing and work orders](phase-sequencing.md) — WO-011/012/026/051/052; WO-051 DO-08 depended on unshipped 03.Domain P-309; WO-052 versioning-consolidation pattern for multi-phase same-WO releases
- [Envelope namespace rename + pagination DTO pattern](envelope-namespace-rename.md) — WO-052 Envelope→Envelopes collision fix (target v2.0.0); CursorPagedList<T> Create-only/no-TotalCount design discipline
