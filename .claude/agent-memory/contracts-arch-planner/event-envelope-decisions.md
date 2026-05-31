---
name: event-envelope-decisions
description: Design decisions for EventEnvelope<TEvent> in 04.Contracts — constraint, property semantics, nullable fields
metadata:
  type: project
---

## EventEnvelope<TEvent> Design Decisions (WO-011 / P-055)

**Constraint is `TEvent : IDomainEvent`, not `IIntegrationEvent`.**
The envelope wraps domain events directly. `IIntegrationEvent` is for the outbound public projection used by consuming services. The messaging layer (07.Messaging) is responsible for the domain-event → integration-event projection before external publication.

**`EventId` is copied from `TEvent.Id` — there is no `EnvelopeId`.**
The envelope does not introduce a new transport-level identity. Deduplication at the transport layer uses the domain event's own `Id`. This was a correction from the initial CLAUDE.md draft which had `EnvelopeId = Guid.NewGuid()`.

**`EventVersion` (not `SchemaVersion`).**
The property is named `EventVersion` per WO-011 spec. Initial CLAUDE.md had `SchemaVersion`. WO-011 is authoritative.

**`CorrelationId` is `string?` (nullable).**
Null is valid when no ambient OTel trace context is available. Publishers propagate from `ActivityContext` when available but must not throw if it is absent. Initial CLAUDE.md had it as non-nullable with a fallback to `Guid.NewGuid().ToString("N")` — this was moved to publisher responsibility, not enforced in the Wrap factory.

**`EventVersion` is populated automatically by `Wrap` via `DomainEventVersionHelper.GetVersion(typeof(TEvent))`.**
The publisher does not compute version numbers. Defaults to 1 when `DomainEventVersionAttribute` is absent on the event type.

**Why:** Transport concerns (correlation, causation, version routing) must not pollute the domain event model. The envelope cleanly separates domain data from transport metadata.

**How to apply:** When designing any new envelope variant or reviewing event types, keep domain event types free of transport fields.
