---
name: boundary-rules
description: Key boundary rules for 04.Contracts — Result<T> vs Envelope<T>, IDomainEvent surface rules, ContractsJsonContext visibility
metadata:
  type: project
---

## Key Boundary Rules for 04.Contracts

**Result<T> vs Envelope<T> boundary rule (formalized WO-012 / P-059):**
- `Result<T>` lives within a single service — railway-oriented programming for application layer methods.
- `Envelope<T>` is serialized at service boundaries — presentation layer, HTTP client adapters, gRPC payloads.
- Application layer methods must return `Result<T>`, never `Envelope<T>`.
- The conversion from `Result<T>` to `Envelope<T>` happens at the communication layer (14.Presentation, 11.Communication).
- These two must never be conflated. `Result<T>` must never be a serialized payload.

**IDomainEvent in contracts surface rules:**
- `IDomainEvent` from `03.Domain` appears ONLY as the generic constraint on `EventEnvelope<TEvent>`.
- It must never appear as a property type, method parameter type, or return type on any public surface of contracts types.
- `DomainEventVersionHelper` from `03.Domain` is used only inside the `Wrap` factory — it is an implementation detail, not part of the public API.
- No other types from `03.Domain` (`Entity<TId>`, `AggregateRoot<TId>`, `ValueObject`, etc.) may appear in this package.

**ContractsJsonContext is `internal`:**
- Consuming services must not reference `ContractsJsonContext` directly.
- The correct pattern is: define own `partial JsonSerializerContext` with `[JsonSerializable]` for your event types, then merge via `JsonSerializerOptions.TypeInfoResolverChain`.
- This prevents consuming services from being coupled to the internal serialization layout of SharedKernel.Contracts.

**Why:** These rules prevent infrastructure concerns leaking into the domain layer and prevent the contracts package from becoming a domain-logic sink.

**How to apply:** Any PR or implementation task touching 04.Contracts must be checked against these three rules before approval.
