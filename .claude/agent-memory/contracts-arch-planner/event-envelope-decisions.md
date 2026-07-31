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

## AggregateId doc-drift defect (WO-051 / P-314, found 2026-07-29)

`EventEnvelope<TEvent>.Payload`'s shipped XML doc (`Events/EventEnvelope.cs`) claimed `where TEvent : IDomainEvent` guarantees `Payload` exposes `Id`, `OccurredOn`, **and `AggregateId`**. This was always false — `03.Domain/SharedKernel.Domain/Events/IDomainEvent.cs` has only ever declared `Id` and `OccurredOn`. `AggregateId` did not exist anywhere on the domain-event surface until `03.Domain` designed `IHasAggregateId<TId>` (WO-051/P-309, `SharedKernel.Domain.Abstractions`, `where TId : notnull`, single member `TId AggregateId { get; }`) as a deliberately opt-in marker — a concrete event may implement it in addition to `DomainEvent<TPayload>`, but nothing requires it.

Corrected contract: the bare `IDomainEvent` constraint on `EventEnvelope<TEvent>` guarantees only `Id`/`OccurredOn`. `AggregateId` is available on `Payload` only when the concrete `TEvent` also implements `IHasAggregateId<TId>` — callers must pattern-match (`Payload is IHasAggregateId<TId> h`), never assume the member is there.

**Why this matters beyond the one-line fix:** `IHasAggregateId<TId>`'s own 03.Domain design notes say it was added specifically to close "a gap `04.Contracts`'s `EventEnvelope<TEvent>` XML doc already assumed was filled" — i.e., the domain team designed the real feature *because* the contracts doc had already (wrongly) promised it existed. Good instinct to record for future doc reviews: a wrong doc claim about a not-yet-built capability can end up steering the design of the real thing, for better or worse here.

**How to apply:** Any future XML doc claim in `04.Contracts` about what an `03.Domain` interface constraint "guarantees" must be checked against that interface's actual shipped source file, never asserted from memory or from what would be architecturally convenient.

## TenantId addition (WO-052 / P-331, designed 2026-07-31)

`EventEnvelope<TEvent>` gains a 9th property: `TenantId` (`Guid?`), positioned after `CausationId`, before `SourceService`. `Wrap` gains a trailing optional `Guid? tenantId = null` parameter — purely additive, source/binary compatible with every existing call site.

**Why this property exists and what it is NOT:** `IDomainEvent` declares no tenant member (only `Id`/`OccurredOn`, same fact as the AggregateId defect above), and `07.Messaging`'s `IMessageHeaderPropagator` only carries tenant context as a *transient, broker-adapter-specific transport header* — it never survives into a durably-stored outbox row, a dead-letter-queue payload, or any protocol other than the one adapter that propagated it. `EventEnvelope<TEvent>.TenantId` is the durable, wire-format-level analogue: it makes the envelope itself self-describing for tenant without requiring a consumer to deserialize `Payload` first.

**Explicitly NOT the same thing as:**
- `03.Domain`'s `IHasTenant.TenantId` (aggregate/entity-level marker) — no compile-time relationship; `04.Contracts` intentionally stays decoupled from any per-event tenant marker interface.
- A guarantee derived from `Payload` or `IDomainEvent` — it is populated only when the publisher explicitly supplies it to `Wrap`. `null` is the default and is valid (mirrors `CorrelationId`/`CausationId`'s existing null semantics).

**How to apply:** When documenting `TenantId`, follow the same doc-accuracy discipline as the AggregateId fix above (WO-051/P-314) — state precisely what is/isn't guaranteed, don't imply a relationship to `IDomainEvent` or `IHasTenant` that doesn't exist in code.
