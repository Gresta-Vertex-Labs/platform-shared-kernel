# Contracts Arch Planner — Memory Index

- [EventEnvelope design decisions](event-envelope-decisions.md) — IDomainEvent constraint, EventId copy semantics, nullable CorrelationId, EventVersion; WO-051 AggregateId doc-drift defect
- [Phase sequencing and work orders](phase-sequencing.md) — WO-011/012/026/051; WO-051 DO-08 depends on unshipped 03.Domain P-309 (`IHasAggregateId<TId>`)
- [Key boundary rules](boundary-rules.md) — Result<T> vs Envelope<T>, IDomainEvent in contracts surface rules, ContractsJsonContext internal visibility
