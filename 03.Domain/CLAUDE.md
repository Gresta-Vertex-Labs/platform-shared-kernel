# 03.Domain — DDD Building Blocks

## What This Domain Is

The DDD primitives layer. Every aggregate, entity, value object, and domain event in a downstream microservice derives from the abstractions defined here. This domain may only reference `01.Core` — it must never reference infrastructure, persistence, or messaging layers.

Philosophy: **Pure domain model. No side effects. No I/O. AOT-preferred. Railway-friendly.**

---

## Packages

| Package | Role | References |
|---------|------|-----------|
| `SharedKernel.Domain` | `Entity<TId>`, `AggregateRoot<TId>`, `ValueObject`, `IDomainEvent`, `DomainEvent` base | `SharedKernel.Primitives` |

All packages target `net10.0`. Test sub-folders live inside each project folder (never in a top-level `tests/`).

---

## Technology Stack

| Concern | Technology |
|---------|-----------|
| Domain primitives | Pure C# 13 — no NuGet dependencies |
| Domain events | Pure C# 13 — no NuGet dependencies |

---

## Interface Contracts

### `SharedKernel.Domain` — public surface

```
Entity<TId>  (abstract class — base for all domain entities)
    .Id                                                     → TId
    .Equals(object? obj)                                    → bool  (identity equality — by Id only)
    .GetHashCode()                                          → int

AggregateRoot<TId>  (abstract class, extends Entity<TId>)
    .DomainEvents                                           → IReadOnlyList<IDomainEvent>  (accumulated events)
    .RaiseDomainEvent(IDomainEvent domainEvent)             → void  (protected — called from within the aggregate)
    .ClearDomainEvents()                                    → void  (called by infrastructure after dispatch)

ValueObject  (abstract class — structural equality via component comparison)
    .GetEqualityComponents()                                → IEnumerable<object?>  (abstract — implementors supply components)
    .Equals(object? obj)                                    → bool  (compares all components)
    .GetHashCode()                                          → int

IDomainEvent  (marker interface)
    .OccurredOnUtc                                          → DateTimeOffset

DomainEvent  (abstract record, implements IDomainEvent)
    .OccurredOnUtc                                          → DateTimeOffset  (set at construction via IClock or UTC now)
```

---

## Implementation Rules

- `SharedKernel.Domain` has **zero NuGet dependencies** — references only `SharedKernel.Primitives`.
- `Entity<TId>` equality is **identity-based** — two entities are equal if and only if their `Id` values are equal.
- `ValueObject` equality is **structural** — all components returned by `GetEqualityComponents()` must be equal.
- `AggregateRoot` is the **only** type permitted to call `RaiseDomainEvent` — plain entities must not accumulate events.
- `DomainEvents` on `AggregateRoot` is **cleared by infrastructure** after successful dispatch — aggregates must not clear their own events.
- `IDomainEvent` is a **marker interface** — it carries only `OccurredOnUtc`; routing, serialization, and dispatch live in infrastructure layers.
- `DomainEvent` base record sets `OccurredOnUtc` at construction — concrete events must not expose a public setter for this property.
- `IClock` from `SharedKernel.Primitives` is the only permitted time source — `DateTime.UtcNow` / `DateTimeOffset.UtcNow` direct usage is a hard violation.
- No static mutable state anywhere in this domain.
- No persistence concerns (no `DbContext`, no repository interfaces) — those live in `06.Persistence`.
- No messaging concerns (no `IMessageBus`, no `IEventPublisher`) — those live in `07.Messaging`.

---

## DI Registration (expected shape)

`SharedKernel.Domain` ships **no DI extensions** — it is a pure library with no runtime services to register.

---

## AOT Compatibility

- `Entity<TId>`, `AggregateRoot<TId>`, `ValueObject` are abstract classes — no reflection, AOT-safe.
- `IDomainEvent` is a marker interface — AOT-safe.
- `DomainEvent` is an abstract record — sealed concrete records in consuming services are AOT-safe by default.
- `GetEqualityComponents()` uses `IEnumerable<object?>` — implementors returning simple value tuples or primitives are AOT-safe.

---

## Test Rules

- Unit tests for each package live in the nested `.Tests/` folder inside that package's folder.
- `SharedKernel.Domain.Tests/` — Entity equality, AggregateRoot event accumulation and clearing, ValueObject structural equality, DomainEvent timestamp.
- Entity tests must verify that two instances with the same Id are equal and that two with different Ids are not.
- ValueObject tests must verify that structural equality holds across all components and that a changed component breaks equality.
- AggregateRoot tests must verify: event raised appears in `DomainEvents`, `ClearDomainEvents` empties the list, events do not leak across instances.

---

## Changelog

> Maintained by the domain agent. One line per significant change.

- [2026-05-22] Domain brain initialized — packages, interfaces, rules, AOT notes
