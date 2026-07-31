# 04.Contracts — Cross-Service DTO Layer

## What This Domain Is

The pure data transfer layer. Every cross-service DTO, integration event payload, and transport wrapper that must be shared between microservices lives here. This domain may reference `01.Core` and `03.Domain` — but it must never contain domain logic, behavior, or infrastructure concerns. It is the lingua franca between services.

Philosophy: **Pure DTOs. No Logic. AOT-Safe. STJ-First.**

---

## Packages

| Package | Role | References |
|---------|------|-----------|
| `SharedKernel.Contracts` | All cross-service DTOs: `PagedList<T>`, `CursorPagedList<T>`, `Envelope<T>`, `IIntegrationEvent`, `EventEnvelope<TEvent>`, STJ serialization context base | `SharedKernel.Primitives`, `SharedKernel.Domain` |

`SharedKernel.Contracts` has **zero external NuGet dependencies** beyond `System.Text.Json` (in-box with `net10.0`). It references `SharedKernel.Primitives` from `01.Core` and `SharedKernel.Domain` from `03.Domain`. The `03.Domain` reference is strictly limited to `IDomainEvent` (constraint on `EventEnvelope<TEvent>`) and `DomainEventVersionHelper.GetVersion(Type)` (populating `EventVersion`). No domain types appear in the public surface of any contracts type.

All packages target `net10.0`. Test sub-folders live inside each project folder (never in a top-level `tests/`).

---

## Technology Stack

| Concern | Technology |
|---------|-----------|
| DTO types | Pure C# 13 — zero external NuGet dependencies |
| JSON serialization | `System.Text.Json` source-generated contexts (`JsonSerializerContext`) — no reflection, AOT-safe |
| Pagination | Pure C# 13 — no NuGet dependencies |
| Integration event transport | Pure C# 13 — no NuGet dependencies |

---

## Interface Contracts

### `SharedKernel.Contracts` — public surface

#### Pagination (`Pagination/`)

```
PagedList<T>  (sealed record)
    .Items                                                  → IReadOnlyList<T>
    .Page                                                   → int  (1-based; page 1 = first page)
    .PageSize                                               → int
    .TotalCount                                             → int  (total records across all pages)
    .TotalPages                                             → int  (= (int)Math.Ceiling((double)TotalCount / PageSize); returns 0 when TotalCount = 0)
    .HasNextPage                                            → bool  (= Page < TotalPages)
    .HasPreviousPage                                        → bool  (= Page > 1)
    .Create(IReadOnlyList<T> items, int page, int pageSize, int totalCount) → PagedList<T>
    NOTE: Page is 1-based (page 1 = first page), consistent with PagedSpecification<T> in 03.Domain.
          TotalPages and HasNextPage/HasPreviousPage are computed properties — no stored state.
          Create rejects: page < 1, pageSize < 1, totalCount < 0 with ArgumentOutOfRangeException.
          PageSize == 0 is rejected by the guard, preventing divide-by-zero in TotalPages.
          The primary record constructor is internal (not private) and annotated with [JsonConstructor]
          to enable STJ source-generated deserialization — this does NOT make direct construction valid;
          Create is still the only externally intended construction path.

CursorPagedList<T>  (sealed record)  (WO-052/P-332)
    .Items                                                  → IReadOnlyList<T>
    .NextCursor                                              → string?  (opaque forward cursor; null when there is no further page)
    .HasMore                                                → bool  (true when at least one more page is available beyond NextCursor)
    .Create(IReadOnlyList<T> items, string? nextCursor, bool hasMore) → CursorPagedList<T>
    NOTE: The cursor/keyset-pagination counterpart to PagedList<T>. Deliberately carries no TotalCount,
          Page, or PageSize — keyset pagination structurally cannot support random-access page numbers or
          a reliable total count without defeating its own performance purpose (the reason to reach for
          keyset pagination is to avoid the COUNT(*)/OFFSET cost that grows with table size). Use
          PagedList<T> for small/random-access/UI-paged result sets that need a total count. Use
          CursorPagedList<T> for large, actively-written, or infinite-scroll result sets where a stable
          total count is expensive or meaningless.
          Doc-only cross-reference (no compile dependency): the specification-side counterpart is
          03.Domain's KeysetSpecification<T, TKey> (shipped SharedKernel.Domain v1.7.0, P-308/WO-051).
          06.Persistence's EF Core translation of that specification into a real seek query remains
          design-locked and queued (P-317/WO-051) — this DTO ships ahead of that translation, mirroring
          the platform's accepted "design-ahead-of-Core" pattern (e.g. 16.Testing's provider-folder
          precedent).
          Construction is Create-only, mirroring PagedList<T>.Create's guarded, non-public-constructor
          pattern. The primary record constructor is internal and annotated with [JsonConstructor] for
          STJ source-generated deserialization only — not a valid external construction path.
          Create rejects: items == null, with ArgumentNullException.
```

#### Response Envelope (`Envelopes/`)

```
Envelope  (sealed record — void operations, no typed value payload)
    .IsSuccess                                              → bool
    .Error                                                  → Error?  (null when IsSuccess = true)
    .Ok()                                                   → Envelope
    .Fail(Error error)                                      → Envelope  (rejects Error.None with ArgumentException)
    implicit operator Envelope(Error error)                 → Envelope.Fail(error)

Envelope<T>  (sealed record — operation result with typed value)
    .IsSuccess                                              → bool
    .Value                                                  → T?  (null when IsSuccess = false)
    .Error                                                  → Error?  (null when IsSuccess = true)
    .Ok(T value)                                            → Envelope<T>  (rejects null value with ArgumentNullException)
    .Fail(Error error)                                      → Envelope<T>  (rejects Error.None with ArgumentException)
    implicit operator Envelope<T>(T value)                  → Envelope<T>.Ok(value)
    implicit operator Envelope<T>(Error error)              → Envelope<T>.Fail(error)
    NOTE: Envelope<T> is the cross-service transport counterpart to Result<T> from SharedKernel.Primitives.
          Result<T> is used within a single service for railway-oriented programming.
          Envelope<T> is used at service boundaries — HTTP client responses, gRPC payloads, or message bus acknowledgements.
          Never return Result<T> across a service boundary; serialize to Envelope<T> at the presentation/communication layer.
          Envelope<T> must only be constructed at service boundaries (presentation layer, HTTP client adapters).
          Never return Envelope<T> from application layer methods — application layer returns Result<T>.
```

#### Integration Event Contracts (`Events/`)

```
IIntegrationEvent  (marker interface)
    .EventId                                                → Guid  (event identity — maps to IDomainEvent.Id from the originating domain event)
    .OccurredOn                                             → DateTimeOffset  (when the domain event occurred)
    NOTE: Integration events are the public contract projection of domain events.
          Implementations must be sealed record or sealed class.
          They are immutable DTOs — no behavior, no domain logic.
          Consumers must never cast IIntegrationEvent back to a domain type.
          The EventId maps to IDomainEvent.Id from the originating domain event, preserving traceability.

EventEnvelope<TEvent>  (sealed record)  where TEvent : IDomainEvent
    .EventId                                                → Guid  (= TEvent.Id; copied from the domain event at wrapping time)
    .OccurredOn                                             → DateTimeOffset  (= TEvent.OccurredOn; copied from the domain event)
    .EventType                                              → string  (= typeof(TEvent).Name; used for routing and deserialization)
    .EventVersion                                           → int  (from DomainEventVersionHelper.GetVersion(typeof(TEvent)); defaults to 1 if DomainEventVersionAttribute absent)
    .CorrelationId                                          → string?  (set from ambient OTel ActivityContext or caller-provided; null if unavailable)
    .CausationId                                            → string?  (ID of the command or event that caused this event; null for root events)
    .TenantId                                               → Guid?  (WO-052/P-331 — optional tenant-identity routing metadata; null for non-tenanted/root events)
    .SourceService                                          → string  (name of the service that raised this event; set at composition root)
    .Payload                                                → TEvent  (the wrapped domain event; required init)
    static EventEnvelope.Wrap<TEvent>(
        TEvent domainEvent,
        string sourceService,
        string? correlationId = null,
        string? causationId = null,
        Guid? tenantId = null)                              → EventEnvelope<TEvent>
    NOTE: Wrap lives on the non-generic static class EventEnvelope (not on EventEnvelope<TEvent>).
          This avoids the need for callers to specify the type argument explicitly; it is inferred.
    NOTE: EventEnvelope<TEvent> is the messaging transport wrapper. It carries routing metadata alongside
          the domain event payload so consumers can route, trace, and deserialize correctly.
          The messaging layer (07.Messaging) uses EventEnvelope<TEvent> as the wire format.
          TEvent is constrained to IDomainEvent — the constraint is satisfied by domain event types from 03.Domain.
          EventId is NOT a new envelope-level identity — it is copied from the domain event (TEvent.Id).
          EventVersion is populated by Wrap via DomainEventVersionHelper; the publisher does not compute it manually.
          CorrelationId and CausationId are nullable — null is valid for root events with no ambient trace context.
    NOTE: (WO-052/P-331) TenantId is envelope-level wire-format routing metadata, added so a message-bus
          consumer, dead-letter-queue inspector, or audit/replay tool can answer "which tenant does this
          event belong to" without deserializing Payload. It carries NO guarantee derived from TEvent or
          IDomainEvent — IDomainEvent declares no tenant member, and TenantId is populated only when the
          publisher explicitly supplies one to Wrap; null is otherwise the default and is valid. This is
          distinct from 07.Messaging's IMessageHeaderPropagator, which is a transient, broker-adapter-
          specific transport header that never survives into a durably-stored outbox row or any protocol
          other than the one adapter that propagated it — TenantId on EventEnvelope<TEvent> is the
          durable, wire-format-level analogue. A publisher bridging IMessageHeaderPropagator's tenant
          header into this field at composition-root/publish time is the intended integration pattern,
          not automatic behavior of this package. Do not conflate with 03.Domain's IHasTenant.TenantId —
          there is no compile-time relationship between the two; this property exists so 04.Contracts
          stays decoupled from any per-event tenant marker interface.
    NOTE: The bare `where TEvent : IDomainEvent` constraint guarantees Payload exposes ONLY Id and
          OccurredOn — IDomainEvent has never declared an AggregateId member. AggregateId is available
          on Payload only when the concrete TEvent additionally implements 03.Domain's opt-in
          IHasAggregateId<TId> marker (SharedKernel.Domain.Abstractions.IHasAggregateId<TId>,
          WO-051/P-309 — where TId : notnull, single member TId AggregateId { get; }). Consumers must
          type-check (`Payload is IHasAggregateId<TId> hasAggregateId`) rather than assume the member
          exists. (Corrected WO-051/P-314 — the source XML doc previously and incorrectly claimed the
          bare IDomainEvent constraint alone guaranteed AggregateId.)
```

#### Mapping Extensions (`Mapping/`)

```
ResultEnvelopeExtensions  (static class — namespace SharedKernel.Contracts.Mapping)
    ToEnvelope<T>(this Result<T> result)    → Envelope<T>
        success: Envelope<T>.Ok(result.Value!)
        failure: Envelope<T>.Fail(result.Error!)
    ToEnvelope(this Result result)          → Envelope
        success: Envelope.Ok()
        failure: Envelope.Fail(result.Error!)
    ToResult<T>(this Envelope<T> envelope)  → Result<T>
        IsSuccess: Result<T>.Success(envelope.Value!)
        !IsSuccess: Result<T>.Failure(envelope.Error!)
    ToResult(this Envelope envelope)        → Result
        IsSuccess: Result.Success()
        !IsSuccess: Result.Failure(envelope.Error!)
    NOTE: All four methods are pure — no allocations beyond the output type, no side effects, no logging.
          Placement: SharedKernel.Contracts/Mapping/ResultEnvelopeExtensions.cs
          Namespace:  SharedKernel.Contracts.Mapping
          These extensions are the platform-standard bridge that enforces the Result<T>/Envelope<T> boundary
          rule at service boundaries without ad-hoc inline boilerplate in typed clients, controller actions,
          or gRPC server handlers.
          No new NuGet dependencies are introduced — both Result<T> (SharedKernel.Primitives) and Envelope<T>
          (this package) are already in scope.
```

#### STJ Serialization Context (`Serialization/`)

```
ContractsJsonContext  (JsonSerializerContext, internal partial)
    — base STJ source-generated context covering all types in this package
    — decorated with [JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
    — covers: PagedList<object>, CursorPagedList<object>, Envelope, Envelope<object>, IIntegrationEvent, EventEnvelope<DomainEvent>
    — internal — consuming services do not reference this context directly
    — consuming services extend via [JsonSerializable(typeof(MyIntegrationEvent))] in their own partial JsonSerializerContext
      and merge via JsonSerializerOptions.TypeInfoResolverChain
    NOTE: Strongly-typed EventEnvelope<OrderPlacedEvent> requires the consuming service's STJ context.
          Do not use ContractsJsonContext directly — always merge it into your service's resolver chain.
          See README.md for the canonical context composition pattern.
```

---

## Implementation Rules

- `SharedKernel.Contracts` references `SharedKernel.Primitives` (from `01.Core`) and `SharedKernel.Domain` (from `03.Domain`). No other project or NuGet references are permitted.
- **`Envelope`/`Envelope<T>` live in namespace `SharedKernel.Contracts.Envelopes`** (plural), folder `Envelopes/` (WO-052/P-328). This namespace previously collided with its own contained type name (`SharedKernel.Contracts.Envelope` containing `Envelope`) — a well-known C# ambiguity footgun that required a using-alias workaround at every consuming call site. The rename eliminates the ambiguity outright; no using-alias workaround is needed or should be used against this namespace going forward.
- **`InternalsVisibleTo`:** `AssemblyInfo.cs` declares `[assembly: InternalsVisibleTo("SharedKernel.Contracts.Tests")]` so `ContractsJsonContext.Default` is accessible in tests without making the context public.
- The `03.Domain` reference is used exclusively for `IDomainEvent` (generic constraint on `EventEnvelope<TEvent>`) and `DomainEventVersionHelper.GetVersion(Type)` (computing `EventVersion` in `Wrap`). No other types from `03.Domain` are consumed or re-exported.
- **No domain logic** — types in this package must be pure DTOs. No methods other than factory methods and computed properties that derive from stored state (e.g., `TotalPages`, `HasNextPage`). No validation rules, no invariants, no business methods.
- **No domain types as public API** — types from `03.Domain` (`Entity<TId>`, `AggregateRoot<TId>`, `ValueObject`) must never appear in this package's public surface. `IDomainEvent` appears only as a generic constraint on `EventEnvelope<TEvent>` — not as a property type or parameter type on any public surface.
- **XML doc claims about `IDomainEvent` must match the real, shipped interface — never assume a member that isn't there.** `IDomainEvent` declares only `Id` and `OccurredOn`; it has never declared `AggregateId`. Any doc comment describing what the `where TEvent : IDomainEvent` constraint guarantees must be checked against `03.Domain/SharedKernel.Domain/Events/IDomainEvent.cs` itself, not against what the constraint "should" guarantee. `AggregateId` on `Payload` is available only when the concrete `TEvent` additionally implements `03.Domain`'s opt-in `IHasAggregateId<TId>` marker (WO-051/P-309) — a doc comment may mention `IHasAggregateId<TId>` for explanatory cross-reference (mirroring the existing `DomainEventVersionAttribute`/`DomainEventVersionHelper` precedent) without that addition making `IHasAggregateId<TId>` a consumed/re-exported public-API type of this package. This rule exists because exactly this drift shipped once already (WO-051/P-314) — `EventEnvelope<TEvent>.Payload`'s XML doc claimed `AggregateId` was guaranteed by the bare `IDomainEvent` constraint years before `IHasAggregateId<TId>` was even designed.
- `PagedList<T>` is a **sealed record** — structural equality across all properties, AOT-safe.
- `Envelope` and `Envelope<T>` are **sealed records** — consistent with `Result` / `Result<T>` pattern in `01.Core`. They are the cross-service transport counterparts.
- `Envelope<T>` and `Result<T>` must never be conflated — `Result<T>` is for intra-service railway operations; `Envelope<T>` is for inter-service boundary serialization. Convert at the boundary: map a `Result<T>` to `Envelope<T>` in the communication layer, never pass `Result<T>` as a serialized payload.
- `IIntegrationEvent` is a **marker interface** — it has no behavioral members beyond identity (`EventId`) and timestamp (`OccurredOn`). Implementations must be sealed records or sealed classes.
- `EventEnvelope<TEvent>` is a **sealed record** — immutable once created. The `Wrap` factory is the only permitted construction path for outbound events. The generic constraint is `TEvent : IDomainEvent`.
- `EventEnvelope<TEvent>.EventId` is **copied from the domain event** (`TEvent.Id`) — it is not a new envelope-level identity. There is no separate `EnvelopeId` property. Deduplication at the transport layer uses `EventId` from the payload.
- `EventEnvelope<TEvent>.EventVersion` is populated by `Wrap` via `DomainEventVersionHelper.GetVersion(typeof(TEvent))` — this defaults to 1 when `DomainEventVersionAttribute` is absent on the event type. The publisher does not compute version numbers manually.
- `CorrelationId` on `EventEnvelope<TEvent>` is `string?` — null is a valid value when no ambient trace context is available. Publishers should propagate from OTel `ActivityContext` when available.
- **STJ source-generated context** — all serialization in this package must be AOT-safe. The `ContractsJsonContext` partial class provides the base context. No `JsonSerializer.Serialize(obj)` calls using reflection-based overloads in this package.
- `PagedList<T>.Page` is **1-based** — page 1 is the first page. This is consistent with `PagedSpecification<T>` in `03.Domain`.
- `PagedList<T>.Create` must guard `pageSize >= 1` — PageSize of 0 causes divide-by-zero in `TotalPages` and must be rejected with `ArgumentOutOfRangeException`.
- **`CursorPagedList<T>` is a sealed record** (WO-052/P-332) — `Create`-only construction, same null-guard discipline as `PagedList<T>.Create` (`items == null` throws `ArgumentNullException`). It deliberately carries no `TotalCount`/`Page`/`PageSize` — never add these to a keyset-pagination DTO; doing so re-introduces the total-count/offset cost the type exists to avoid. Use `PagedList<T>` when a total count and random-access page numbers are needed; use `CursorPagedList<T>` for large, actively-written, or infinite-scroll result sets.
- **`EventEnvelope<TEvent>.TenantId` is `Guid?`** (WO-052/P-331) — optional envelope-level routing metadata, populated only when the publisher supplies it to `Wrap`; defaults to `null`. It is never inferred from `Payload` and carries no guarantee from `IDomainEvent` (which declares no tenant member). Do not conflate this with `03.Domain`'s `IHasTenant.TenantId` — there is no compile-time relationship between the two; this property exists precisely so `04.Contracts` stays decoupled from any per-event tenant marker interface.
- **`ResultEnvelopeExtensions` is a pure static class** — all four methods (`ToEnvelope<T>`, `ToEnvelope`, `ToResult<T>`, `ToResult`) must be free of side effects, logging, and allocations beyond the output type. They are the platform-standard bridge for enforcing the `Result<T>` / `Envelope<T>` boundary rule. No logic or branching beyond the `IsSuccess` check is permitted in these methods.
- **`ResultEnvelopeExtensions` namespace** is `SharedKernel.Contracts.Mapping` — distinct from `SharedKernel.Contracts.Envelopes` (WO-052/P-328 renamed this from `SharedKernel.Contracts.Envelope`; the historical collision this bullet used to reference no longer exists). The `Mapping/` subfolder holds a single file: `ResultEnvelopeExtensions.cs`.
- No static mutable state anywhere in this domain.
- No persistence concerns (`DbContext`, EF annotations) — those live in `06.Persistence`.
- No messaging concerns (`IMessageBus`, consumer registration) — those live in `07.Messaging`.

---

## DI Registration (expected shape)

`SharedKernel.Contracts` ships **no DI extensions** — it is a pure DTO library with no runtime services to register.

STJ context composition is the caller's responsibility:
```csharp
// In consuming service's JsonSerializerContext:
[JsonSerializable(typeof(EventEnvelope<OrderPlacedEvent>))]
[JsonSerializable(typeof(PagedList<OrderDto>))]
internal partial class MyServiceJsonContext : JsonSerializerContext { }

// In service startup, merge the resolver chain:
var options = new JsonSerializerOptions();
options.TypeInfoResolverChain.Add(MyServiceJsonContext.Default);
options.TypeInfoResolverChain.Add(ContractsJsonContext.Default);
```

---

## AOT Compatibility

- `PagedList<T>` is a sealed record — no reflection, AOT-safe.
- `Envelope` and `Envelope<T>` are sealed records — no reflection, AOT-safe.
- `IIntegrationEvent` is a marker interface — AOT-safe.
- `EventEnvelope<TEvent>` is a sealed record with a `TEvent : IDomainEvent` constraint — AOT-safe.
- `ContractsJsonContext` is a `[JsonSourceGenerationOptions]`-decorated internal partial `JsonSerializerContext` — fully AOT-safe; no reflection-based serialization in this package.
- `typeof(TEvent).Name` in `EventEnvelope<TEvent>.EventType` uses `Type.Name` — trimmer-safe; `Type.Name` is preserved by the trimmer.
- `DomainEventVersionHelper.GetVersion(typeof(TEvent))` uses `Type` passed explicitly — this is trimmer-safe as `typeof(TEvent)` is a static token known at compile time.
- `EventEnvelope<TEvent>.TenantId` (WO-052/P-331) is a plain `Guid?` — no reflection, no trimmer concerns; identical AOT profile to `CorrelationId`/`CausationId`.
- `CursorPagedList<T>` (WO-052/P-332) is a sealed record — no reflection, AOT-safe; identical construction/serialization profile to `PagedList<T>`.
- No `Activator.CreateInstance`, no `Assembly.Load`, no reflection in hot paths.

---

## Test Rules

- Unit tests for this package live in `04.Contracts/SharedKernel.Contracts/SharedKernel.Contracts.Tests/`.
- `PagedList<T>`: `TotalPages` computation (typical inputs, zero TotalCount), `HasNextPage`/`HasPreviousPage` boundary conditions (first page, last page, middle page, single page, empty list), `Create` factory guard clauses (`[Theory]` with boundary data sets), record structural equality, STJ round-trip using source-generated context.
- `Envelope` / `Envelope<T>`: success path sets `IsSuccess = true` and correct value; failure path sets `IsSuccess = false` and correct `Error`; implicit operators; `Fail(Error.None)` is not permitted (guard test); `Ok(null)` is not permitted on `Envelope<T>` (guard test); STJ round-trip.
- `IIntegrationEvent`: concrete `sealed record` implementation assignability; property accessibility from interface reference.
- `EventEnvelope<TEvent>`: `Wrap` factory sets all fields correctly; `EventId` is copied from `TEvent.Id`; `CorrelationId` is null when not provided; `CausationId` is null when not provided; `TenantId` is null when not provided (WO-052/P-331) and correctly set when provided; `EventVersion` defaults to 1 when attribute absent; `EventVersion` uses declared version when attribute present; `EventType` equals `typeof(TEvent).Name`; record equality (including `TenantId` in the structural comparison); STJ round-trip using source-generated context, covering both the null-`TenantId` and populated-`TenantId` cases.
- `CursorPagedList<T>` (WO-052/P-332): `Create` factory guard clause (`items == null` throws `ArgumentNullException`); `HasMore = true` with a populated `NextCursor`; `HasMore = false` with `NextCursor = null` (terminal page); empty `Items` with `HasMore = false`; record structural equality; STJ round-trip for `CursorPagedList<string>` using source-generated context.
- All STJ round-trip tests must use source-generated contexts — no reflection-based `JsonSerializer.Serialize(obj)` overloads in tests.
- STJ round-trip tests must define a **test-level `partial JsonSerializerContext`** (e.g. `TestJsonContext`) that registers the concrete type arguments used in tests (e.g. `PagedList<string>`, `Envelope<string>`, `EventEnvelope<TestOrderCreatedEvent>`). Merge it with `ContractsJsonContext.Default` via `JsonSerializerOptions.TypeInfoResolverChain`. Set `PropertyNamingPolicy = JsonNamingPolicy.CamelCase` directly on the `JsonSerializerOptions` instance — the `[JsonSourceGenerationOptions]` attribute on a context does not auto-apply naming policy to the options object used in the serializer call.
- All guard-clause tests use `[Theory]` with boundary data sets.
- `ResultEnvelopeExtensions`: round-trip tests for all four methods — generic and non-generic, success path and failure path. Double round-trip tests (e.g., `Result<T>.Success` → `ToEnvelope` → `ToResult`) must verify value and error identity is preserved. No STJ serialization is involved — these are pure mapping tests using `[Fact]` or `[Theory]`.

---

## Changelog

> Maintained by the contracts domain agent. One line per significant change.

- [2026-05-30] Domain brain initialized — packages, interfaces, rules, AOT notes, test rules; Design phase in progress
- [2026-05-30] CLAUDE.md refreshed for WO-011 + WO-012: EventEnvelope<TEvent> spec updated to IDomainEvent constraint (not IIntegrationEvent); EventId is copied from domain event (no EnvelopeId); EventVersion replaces SchemaVersion; CorrelationId is nullable string?; SharedKernel.Domain added as second project reference; 32 tasks added across all 6 phases in state-map
- [2026-05-30] SK.04.Design complete — PagedList internal+[JsonConstructor] pattern; Envelope namespace/type collision rule; EventEnvelope non-generic static Wrap class; InternalsVisibleTo for test context; STJ test-level context + CamelCase options pattern (contracts-phase-implementer)
- [2026-06-18] WO-026/P-166: ResultEnvelopeExtensions static class added to Interface Contracts (Mapping/ section); Implementation Rules updated with purity contract and namespace rules; Test Rules updated with round-trip and double-round-trip requirements; CLAUDE.md reflects 1.1.0 surface (contracts-arch-planner)
- [2026-07-29] WO-051/P-314: found and corrected a live doc-accuracy defect — `EventEnvelope<TEvent>.Payload`'s shipped XML doc claimed `where TEvent : IDomainEvent` guarantees `Id`, `OccurredOn`, AND `AggregateId`; verified against the real `IDomainEvent` source that only `Id`/`OccurredOn` were ever declared. Interface Contracts' `EventEnvelope<TEvent>` block gained a NOTE stating the accurate contract and pointing to `03.Domain`'s new opt-in `IHasAggregateId<TId>` marker (WO-051/P-309) as the real source of `AggregateId`; Implementation Rules gained a standing rule requiring `IDomainEvent`-related doc claims to be checked against the shipped interface, not assumed. DO-08 added to `state-map.md`'s Docs phase (the actual `.cs` XML doc edit is implementer work, out of this planning agent's jurisdiction); sequenced to run after P-309 ships so the `IHasAggregateId<TId>` cross-reference resolves (contracts-arch-planner)
- [2026-07-31] WO-052/P-328: designed the fix for the long-documented `Envelope`/`Envelope` namespace-type collision — `Envelope`/`Envelope<T>` move from namespace `SharedKernel.Contracts.Envelope` to `SharedKernel.Contracts.Envelopes` (folder `Envelope/` → `Envelopes/`), matching the sibling `Events/`/`Pagination/`/`Mapping/`/`Serialization/` folders' naming convention; zero change to type members, factory methods, or implicit operators — namespace-only move. Implementation Rules' "Namespace/type name collision" workaround bullet removed outright and replaced with a bullet documenting the fix; `ResultEnvelopeExtensions` namespace bullet updated to reference the new namespace. Target package version on release: `2.0.0` (breaking source change — consumers must update `using` statements/remove the old alias workaround). 6 tasks added across Design/Scaffold/Core/Tests/Docs/Published (D-08, S-06, C-08, T-08, DO-09, P-06) (contracts-arch-planner)
- [2026-07-31] WO-052/P-331: designed an optional nullable `TenantId` (`Guid?`) addition to `EventEnvelope<TEvent>`, positioned after `CausationId`; `Wrap` gains a trailing optional `Guid? tenantId = null` parameter — purely additive, source- and binary-compatible with every existing call site (no Scaffold task needed; `EventEnvelope<TEvent>` already exists in `Events/EventEnvelope.cs`). XML doc discipline follows the WO-051/P-314 precedent: the property's doc states precisely what is and is not guaranteed — populated only when the publisher supplies it, never inferred from `Payload`, no guarantee from `IDomainEvent` (which declares no tenant member), and explicitly distinct from `07.Messaging`'s transient `IMessageHeaderPropagator` header and from `03.Domain`'s `IHasTenant.TenantId`. 5 tasks added (D-09, C-09, T-09, DO-10, P-07) (contracts-arch-planner)
- [2026-07-31] WO-052/P-332: designed a new `CursorPagedList<T>` sealed record — the cursor/keyset-pagination counterpart to `PagedList<T>`, placed in `Pagination/` alongside it — carrying `Items`/`NextCursor`/`HasMore`, deliberately no `TotalCount`/`Page`/`PageSize`; `Create`-only construction mirroring `PagedList<T>.Create`'s guard discipline. Ships ahead of `06.Persistence`'s queued EF Core keyset-specification translation (P-317/WO-051), with a doc-only cross-reference to `03.Domain`'s already-shipped `KeysetSpecification<T, TKey>` (P-308/WO-051) — no new compile dependency, no `03.Domain` reference change. 6 tasks added (D-10, S-07, C-10, T-10, DO-11, P-08) (contracts-arch-planner)
- [2026-07-31] WO-052 sequencing note: all three phases (P-328/P-331/P-332) have `Depends on: None` and no ordering constraint between them; they are expected to ship together in a single `SharedKernel.Contracts` release. Target version `2.0.0` — P-328's breaking namespace rename dominates SemVer for the release even though P-331/P-332 are individually additive; do not double-bump to `2.1.0` in the same pass (contracts-arch-planner)
- [2026-07-31] SK.04.Core and SK.04.Tests implemented in full (C-08–C-10, T-08–T-10; 10/10 each) with zero drift from this file's own design — `Envelope`/`Envelope<T>` now live in `Envelopes/`/namespace `SharedKernel.Contracts.Envelopes` (the `ResultEnvelopeExtensions`/test `EnvelopeNs` alias workaround removed outright, replaced with a plain `using`); `EventEnvelope<TEvent>.TenantId` (`Guid?`) and `Wrap`'s trailing optional `tenantId` parameter shipped; `CursorPagedList<T>` fully implemented (`Create`-only, `[JsonConstructor]`+`[SetsRequiredMembers]` internal ctor) and registered in `ContractsJsonContext`. The breaking rename's platform-wide blast radius surfaced immediately: `16.Testing/SharedKernel.Testing` (referenced by every `.Tests` project in the repo) failed to build on its own still-old `EnvelopeNs` alias — fixed via `16.Testing`'s own already-pre-planned, purely mechanical C-101 task (its `CLAUDE.md`/`state-map.md` are that domain's own files and were left untouched; only the two `.cs` files were edited here to unblock this session's required `dotnet test` gate). 87/87 `SharedKernel.Contracts.Tests` green (was 72); `SharedKernel.Testing`/`.SelfTests` 0 build errors, 768/768 non-container tests green, zero regressions anywhere. Docs (DO-09/DO-10/DO-11) and Published (P-06/P-07/P-08, version bump to `2.0.0`) remain (contracts-phase-implementer)
