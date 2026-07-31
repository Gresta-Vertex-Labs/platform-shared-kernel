# 04.Contracts — State Map

> **What this file is:** Phase and task tracker for all work within `04.Contracts`.
> **What it is not:** The root tracker — that lives at `state-map.md`.
> **Sync policy:** When all tasks under a Phase Key are `●`, run `/state-map-phase` with `phase_key: SK.04.{Phase}` to propagate that milestone to the root state-map.

---

## Legend

| Symbol | Meaning |
|--------|---------|
| `○` | Not started |
| `◐` | In progress |
| `●` | Complete |
| `⚑` | Blocked |
| `—` | N/A / Skipped |

---

## Phase Key Registry

> Phase keys are the sync bridge between this sub-state-map and the root `state-map.md`.
> Each key maps a local milestone to a root-level phase. When a key's Promotion Condition is met, the root is updated via `/state-map-phase`.

| Phase Key | Maps to Root Phase | Promotion Condition |
|-----------|-------------------|-------------------|
| `SK.04.Design` | Design | All tasks in Phase: Design are `●` |
| `SK.04.Scaffold` | Scaffold | All tasks in Phase: Scaffold are `●` |
| `SK.04.Core` | Core | All tasks in Phase: Core are `●` |
| `SK.04.Tests` | Tests | All tasks in Phase: Tests are `●` |
| `SK.04.Docs` | Docs | All tasks in Phase: Docs are `●` |
| `SK.04.Published` | Published | All tasks in Phase: Published are `●` |

---

## Active Work

_Nothing in progress — all phases in `04.Contracts` are complete._

<!--
Format when active — replace placeholder with table:
| Task | Phase Key | Package | State |
|------|-----------|---------|:-----:|
| Define EventEnvelope<TEvent> shape | SK.04.Design | SharedKernel.Contracts | ◐ |
-->

---

## Blocked

_No blockers._

<!--
Format when blocked — replace placeholder with table:
| Task | Phase Key | Blocker |
|------|-----------|---------|
| Example blocked task | SK.04.Core | Waiting on upstream decision |
-->

---

## Package Board

| Package | Current Phase | State | Notes |
|---------|--------------|:-----:|-------|
| `SharedKernel.Contracts` | Published | `●` | Currently shipped: `SharedKernel.Contracts 1.1.0` packed to `nupkgs/`; ResultEnvelopeExtensions added; consumer-verify passing (6 surfaces); 72 tests green. WO-052 (P-328/P-331/P-332) queued — target next release `2.0.0` (breaking `Envelope`→`Envelopes` namespace rename + `EventEnvelope<TEvent>.TenantId` + `CursorPagedList<T>`); not yet implemented |

---

## Cross-Domain Dependencies

| This Phase Key | Needs From Domain | What | Status |
|---------------|------------------|------|--------|
| `SK.04.Scaffold` | `01.Core` | `SharedKernel.Primitives` ProjectReference (`Result<T>`, `Error`) | Available |
| `SK.04.Scaffold` | `03.Domain` | `SharedKernel.Domain` ProjectReference (`IDomainEvent`, `DomainEventVersionHelper`) | Available (P-053 complete) |
| `SK.04.Core` | `03.Domain` | `DomainEventVersionHelper.GetVersion(Type)` for `EventEnvelope<TEvent>.EventVersion` | Available (P-053 complete) |
| `SK.04.Docs` | `03.Domain` | `IHasAggregateId<TId>` marker interface (WO-051/P-309) — referenced only as an explanatory `<see cref>` in `EventEnvelope<TEvent>.Payload`'s XML doc; not added to the public-API consumed-type list (mirrors the existing `DomainEventVersionAttribute`/`DomainEventVersionHelper` doc-only cross-reference precedent) | Available — verified directly against the shipped `03.Domain/SharedKernel.Domain/Abstractions/IHasAggregateId.cs` (namespace `SharedKernel.Domain.Abstractions`, `where TId : notnull`, single member `TId AggregateId { get; }`), part of `SharedKernel.Domain` v1.7.0 (P-309, shipped 2026-07-30) |

---

## Phase: Design <!-- phase-key: SK.04.Design -->

> Finalize all type shapes, interface contracts, and STJ context signatures before any implementation begins.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| D-01 | Define `PagedList<T>` sealed record shape — `Items`, `Page` (1-based), `PageSize`, `TotalCount`; computed `TotalPages` (zero-safe), `HasNextPage`, `HasPreviousPage`; `Create` factory signature with `ArgumentOutOfRangeException` guards (`page >= 1`, `pageSize >= 1`, `totalCount >= 0`) | SharedKernel.Contracts | `●` |
| D-02 | Define `Envelope` sealed record shape — `Ok()`, `Fail(Error)` factories, implicit `Error` operator, `Fail(Error.None)` guard; define `Envelope<T>` sealed record shape — `Ok(T)`, `Fail(Error)` factories, `Ok(null)` guard, two implicit operators; document `Result<T>` vs `Envelope<T>` boundary rule | SharedKernel.Contracts | `●` |
| D-03 | Define `IIntegrationEvent` marker interface shape — `EventId` (Guid), `OccurredOn` (DateTimeOffset); document sealed record/class constraint and no-domain-logic rule; document `EventId` traceability to `IDomainEvent.Id` | SharedKernel.Contracts | `●` |
| D-04 | Define `EventEnvelope<TEvent>` sealed record shape (WO-011/P-055) — 8 properties: `EventId`, `OccurredOn`, `EventType`, `EventVersion`, `CorrelationId` (string?), `CausationId` (string?), `SourceService`, `Payload`; `Wrap` factory signature; `TEvent : IDomainEvent` constraint; `EventVersion` sourced from `DomainEventVersionHelper.GetVersion(typeof(TEvent))` defaulting to 1 | SharedKernel.Contracts | `●` |
| D-05 | Define `ContractsJsonContext` partial `JsonSerializerContext` layout — `[JsonSourceGenerationOptions]`, `[JsonSerializable]` entries for all package types (`PagedList<object>`, `Envelope`, `Envelope<object>`, `IIntegrationEvent`, `EventEnvelope<DomainEvent>`); `internal` visibility; consumer extension pattern via `TypeInfoResolverChain` documented | SharedKernel.Contracts | `●` |
| D-06 | Finalize `SharedKernel.Contracts` dependency graph — confirm `SharedKernel.Primitives` + `SharedKernel.Domain` as the only project references; zero external NuGet; document rationale for `IDomainEvent` and `DomainEventVersionHelper` imports from `03.Domain` | SharedKernel.Contracts | `●` |
| D-07 | (WO-026/P-166) Design `ResultEnvelopeExtensions` static class in namespace `SharedKernel.Contracts.Mapping` — four extension method signatures: `ToEnvelope<T>(this Result<T>) → Envelope<T>`, `ToEnvelope(this Result) → Envelope`, `ToResult<T>(this Envelope<T>) → Result<T>`, `ToResult(this Envelope) → Result`; confirm placement in new `Mapping/` subfolder; confirm no new NuGet dependencies (both `Result<T>` via `SharedKernel.Primitives` and `Envelope<T>` via existing `SharedKernel.Contracts` types are already in scope); confirm namespace `SharedKernel.Contracts.Mapping` does not collide with existing `SharedKernel.Contracts.Envelope` namespace collision pattern; define purity contract (no allocations beyond output type, no side effects, no logging) | SharedKernel.Contracts | `●` |
| D-08 | (WO-052/P-328) Design the `Envelope`→`Envelopes` namespace/folder rename — confirm `Envelope`/`Envelope<T>` type shapes remain byte-identical (no member, factory, or implicit-operator changes); finalize new namespace `SharedKernel.Contracts.Envelopes` and folder `Envelopes/`; enumerate every internal reference requiring update (`ContractsJsonContext`'s `[JsonSerializable]` entries, `ResultEnvelopeExtensions`' `using` statement, README examples, test-level `TestJsonContext`); confirm SemVer impact is major (breaking source change) — target release version `2.0.0` | SharedKernel.Contracts | `○` |
| D-09 | (WO-052/P-331) Design nullable `TenantId` (`Guid?`) property placement on `EventEnvelope<TEvent>` — positioned after `CausationId`, before `SourceService`; confirm `Wrap` factory signature gains `Guid? tenantId = null` as a new trailing optional parameter (preserves every existing positional/named call site); draft precise XML doc language distinguishing "populated only when the publisher supplies one" from any implied guarantee about `IDomainEvent`/`03.Domain`'s `IHasTenant` (no such guarantee exists — `TenantId` is envelope-level routing metadata, never derived from `Payload`); confirm zero new project reference is required | SharedKernel.Contracts | `○` |
| D-10 | (WO-052/P-332) Design new sealed record DTO for cursor/keyset-paginated responses — name `CursorPagedList<T>`, folder `Pagination/` (sibling to `PagedList<T>`), namespace `SharedKernel.Contracts.Pagination`; property shape: `Items` (`IReadOnlyList<T>`), `NextCursor` (`string?`, opaque forward cursor, null when no further page), `HasMore` (`bool`); `Create(IReadOnlyList<T> items, string? nextCursor, bool hasMore)` factory mirroring `PagedList<T>.Create`'s guard discipline (reject null `items` with `ArgumentNullException`; no numeric guards since there is no page/pageSize/totalCount); confirm no `TotalCount`/`Page`/`PageSize` members exist and document why; doc-only cross-reference (no compile dependency) to `03.Domain`'s `KeysetSpecification<T, TKey>` (v1.7.0, P-308/WO-051) | SharedKernel.Contracts | `○` |

---

## Phase: Scaffold <!-- phase-key: SK.04.Scaffold -->

> Wire up .csproj NuGet references, intra-domain project references, folder structure, solution registration, and empty test stubs — no logic yet.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| S-01 | Create `SharedKernel.Contracts.csproj` targeting `net10.0`; add `SharedKernel.Primitives` and `SharedKernel.Domain` project references; add all NuGet packaging metadata (`PackageId`, `Version 1.0.0`, `Description`, `Authors`, `PackageTags`, `PackageLicenseExpression`); enable XML documentation generation | SharedKernel.Contracts | `●` |
| S-02 | Create empty placeholder subfolders inside `04.Contracts/SharedKernel.Contracts/`: `Pagination/`, `Envelope/`, `Events/`, `Serialization/` | SharedKernel.Contracts | `●` |
| S-03 | Create `SharedKernel.Contracts.Tests/` nested test project as `classlib` targeting `net10.0`; add `SharedKernel.Contracts` and `SharedKernel.Testing` project references; add xUnit and FluentAssertions NuGet references; create one compilable placeholder test class | SharedKernel.Contracts | `●` |
| S-04 | Register `SharedKernel.Contracts.csproj` and `SharedKernel.Contracts.Tests.csproj` in `Platform.SharedKernel.slnx` under solution folder `04.Contracts`; verify `dotnet build` passes with zero errors and zero warnings | SharedKernel.Contracts | `●` |
| S-05 | (WO-026/P-166) Create `Mapping/` subfolder inside `04.Contracts/SharedKernel.Contracts/`; create empty `ResultEnvelopeExtensions.cs` placeholder in that folder; confirm `dotnet build` still passes with zero errors | SharedKernel.Contracts | `●` |
| S-06 | (WO-052/P-328) Rename folder `04.Contracts/SharedKernel.Contracts/Envelope/` to `Envelopes/` (`git mv` to preserve history); confirm the `.csproj` has no hardcoded folder-path globs requiring update (implicit compile items only); confirm `dotnet build` still passes with zero errors before any namespace edits land (folder rename alone does not change compiled namespaces) | SharedKernel.Contracts | `○` |
| S-07 | (WO-052/P-332) Create empty `CursorPagedList.cs` placeholder in `04.Contracts/SharedKernel.Contracts/Pagination/`, alongside the existing `PagedList.cs`; confirm `dotnet build` still passes with zero errors | SharedKernel.Contracts | `○` |

---

## Phase: Core <!-- phase-key: SK.04.Core -->

> Full implementation of all DTO types, records, and STJ context.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| C-01 | Implement `PagedList<T>` sealed record in `Pagination/` — `required init` properties, `private init` primary constructor, `Create` static factory with `ArgumentOutOfRangeException` guards; computed `TotalPages` (handles `PageSize == 0` without divide-by-zero), `HasNextPage` (`Page < TotalPages`), `HasPreviousPage` (`Page > 1`); strictly 1-based page convention | SharedKernel.Contracts | `●` |
| C-02 | Implement `Envelope` sealed record in `Envelope/` — `IsSuccess`, `Error?` properties; `Ok()` and `Fail(Error)` static factories; `Fail(Error.None)` guard throws `ArgumentException`; implicit `operator Envelope(Error error)` | SharedKernel.Contracts | `●` |
| C-03 | Implement `Envelope<T>` sealed record in `Envelope/` — `IsSuccess`, `Value T?`, `Error?` properties; `Ok(T value)` factory (rejects null with `ArgumentNullException`); `Fail(Error)` factory (rejects `Error.None` with `ArgumentException`); `implicit operator Envelope<T>(T value)` and `implicit operator Envelope<T>(Error error)`; boundary contract XML doc | SharedKernel.Contracts | `●` |
| C-04 | Implement `IIntegrationEvent` marker interface in `Events/` — `Guid EventId { get; }` and `DateTimeOffset OccurredOn { get; }`; XML doc stating implementations must be `sealed record` or `sealed class`, must be immutable DTOs, and must never carry domain logic | SharedKernel.Contracts | `●` |
| C-05 | Implement `EventEnvelope<TEvent>` sealed record in `Events/` (WO-011/P-055) — `TEvent : IDomainEvent` constraint; 8 `required init` properties (`EventId`, `OccurredOn`, `EventType`, `EventVersion`, `CorrelationId string?`, `CausationId string?`, `SourceService`, `Payload TEvent`); static `EventEnvelope.Wrap<TEvent>(TEvent domainEvent, string sourceService, string? correlationId, string? causationId)` factory populating all fields; `EventVersion` via `DomainEventVersionHelper.GetVersion(typeof(TEvent))` defaulting to 1; `EventType` via `typeof(TEvent).Name` | SharedKernel.Contracts | `●` |
| C-06 | Implement `ContractsJsonContext` as `internal partial class` in `Serialization/` — decorate with `[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]` and `[JsonSerializable]` entries for all package types; no reflection-based fallback; consumer extension pattern documented in XML doc | SharedKernel.Contracts | `●` |
| C-07 | (WO-026/P-166) Implement `ResultEnvelopeExtensions` static class in `Mapping/ResultEnvelopeExtensions.cs`, namespace `SharedKernel.Contracts.Mapping` — four pure extension methods: (1) `ToEnvelope<T>(this Result<T> result) → Envelope<T>`: if `result.IsSuccess` return `Envelope<T>.Ok(result.Value!)` else `Envelope<T>.Fail(result.Error!)`; (2) `ToEnvelope(this Result result) → Envelope`: if `result.IsSuccess` return `Envelope.Ok()` else `Envelope.Fail(result.Error!)`; (3) `ToResult<T>(this Envelope<T> envelope) → Result<T>`: if `envelope.IsSuccess` return `Result<T>.Success(envelope.Value!)` else `Result<T>.Failure(envelope.Error!)`; (4) `ToResult(this Envelope envelope) → Result`: if `envelope.IsSuccess` return `Result.Success()` else `Result.Failure(envelope.Error!)`; all methods are pure (no side effects, no logging, no allocations beyond output type); XML doc with usage examples on all four methods; `Result.Success()` / `Result.Failure(Error)` factory names must match the actual `SharedKernel.Primitives` `Result` API — verify before coding | SharedKernel.Contracts | `●` |
| C-08 | (WO-052/P-328) Move `Envelope`/`Envelope<T>` record definitions from namespace `SharedKernel.Contracts.Envelope` to `SharedKernel.Contracts.Envelopes` in the renamed `Envelopes/` folder; update the `using` statement in `Mapping/ResultEnvelopeExtensions.cs` (and any other file referencing the old namespace) to the new namespace; recompile `ContractsJsonContext`'s `[JsonSerializable(typeof(Envelope))]`/`[JsonSerializable(typeof(Envelope<object>))]` entries against the new namespace (no attribute syntax change needed); zero change to type members, factory methods, or implicit operators | SharedKernel.Contracts | `○` |
| C-09 | (WO-052/P-331) Add `TenantId` (`Guid?`, `init`) property to `EventEnvelope<TEvent>` in `Events/EventEnvelope.cs`, positioned after `CausationId`; update `EventEnvelope.Wrap<TEvent>` static factory to accept `Guid? tenantId = null` as a new trailing optional parameter and assign it to the new property; all 8 pre-existing properties/parameters unchanged; verify existing call sites with no `tenantId` argument still compile (optional parameter, source-compatible) | SharedKernel.Contracts | `○` |
| C-10 | (WO-052/P-332) Implement `CursorPagedList<T>` sealed record in `Pagination/CursorPagedList.cs` — `internal` primary constructor annotated `[JsonConstructor]` (mirroring `PagedList<T>`'s STJ-deserialization pattern), `required init` properties `Items`, `NextCursor`, `HasMore`; `Create(IReadOnlyList<T> items, string? nextCursor, bool hasMore)` static factory throwing `ArgumentNullException` on null `items`; add `[JsonSerializable(typeof(CursorPagedList<object>))]` entry to `ContractsJsonContext` mirroring the existing open-generic `PagedList<object>` registration pattern | SharedKernel.Contracts | `○` |

---

## Phase: Tests <!-- phase-key: SK.04.Tests -->

> Unit test coverage for all packages. No integration tests needed — this domain has no external dependencies.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| T-01 | Unit tests for `PagedList<T>` — `TotalPages` computation (100/10=10, 101/10=11, 5/10=1, TotalCount=0 returns 0); `HasNextPage`/`HasPreviousPage` on first/last/middle/single/empty page; `Create` factory guard clauses using `[Theory]` (page=0, page=-1, pageSize=0, totalCount=-1); record structural equality; STJ round-trip for `PagedList<string>` using source-generated context | SharedKernel.Contracts.Tests | `●` |
| T-02 | Unit tests for `Envelope` — `Ok()` sets `IsSuccess=true`, `Error=null`; `Fail(error)` sets `IsSuccess=false`, `Error=error`; `Fail(Error.None)` throws `ArgumentException`; implicit `Error` operator produces failure; record equality; STJ round-trip using source-generated context | SharedKernel.Contracts.Tests | `●` |
| T-03 | Unit tests for `Envelope<T>` — `Ok(value)` sets `IsSuccess=true`, `Value=value`, `Error=null`; `Fail(error)` sets `IsSuccess=false`, `Value=null`, `Error=error`; `Ok(null)` throws `ArgumentNullException`; `Fail(Error.None)` throws `ArgumentException`; both implicit operators; record equality; STJ round-trip for `Envelope<string>` using source-generated context | SharedKernel.Contracts.Tests | `●` |
| T-04 | Unit tests for `IIntegrationEvent` — concrete `sealed record` implementing `IIntegrationEvent` is assignable to the interface; `EventId` and `OccurredOn` are accessible from interface reference | SharedKernel.Contracts.Tests | `●` |
| T-05 | Unit tests for `EventEnvelope<TEvent>` — `Wrap` factory populates all 8 fields correctly; `EventVersion` defaults to 1 when `DomainEventVersionAttribute` absent; `EventVersion` uses declared version when attribute present; `CorrelationId` and `CausationId` are null when not provided; `EventType` equals `typeof(TEvent).Name`; record equality; STJ round-trip using source-generated context; all STJ tests use source-generated contexts — no reflection-based serialization | SharedKernel.Contracts.Tests | `●` |
| T-06 | Cross-cutting test quality gate — verify all STJ round-trip tests use source-generated contexts (zero reflection-based `JsonSerializer.Serialize` overloads); verify all guard-clause tests use `[Theory]` with boundary data; run `dotnet test` and confirm zero failures, zero skipped tests | SharedKernel.Contracts.Tests | `●` |
| T-07 | (WO-026/P-166) Unit tests for `ResultEnvelopeExtensions` — generic variants: (a) `Result<T>.Success(value).ToEnvelope()` round-trip: `IsSuccess=true`, `Value=value`; (b) `Result<T>.Failure(error).ToEnvelope()` round-trip: `IsSuccess=false`, `Error=error`; (c) `Envelope<T>.Ok(value).ToResult()` → success, value preserved; (d) `Envelope<T>.Fail(error).ToResult()` → failure, error preserved; (e) full `Result<T>.Success` → `ToEnvelope` → `ToResult` double round-trip preserves value identity; (f) full `Result<T>.Failure` → `ToEnvelope` → `ToResult` double round-trip preserves error identity; non-generic variants: (g) `Result.Success().ToEnvelope()` → `IsSuccess=true`; (h) `Result.Failure(error).ToEnvelope()` → `IsSuccess=false`, `Error=error`; (i) `Envelope.Ok().ToResult()` → success; (j) `Envelope.Fail(error).ToResult()` → failure, error preserved; all tests use `[Fact]` or `[Theory]` as appropriate; no reflection-based serialization involved (extension methods are pure mapping) | SharedKernel.Contracts.Tests | `●` |
| T-08 | (WO-052/P-328) Update all existing `Envelope`/`Envelope<T>` tests (T-02/T-03) to reference the new `SharedKernel.Contracts.Envelopes` namespace; update the test-level `TestJsonContext` if it directly references the old namespace; add a regression test proving the ambiguity is gone — a plain `using SharedKernel.Contracts.Envelopes;` immediately followed by an unqualified `Envelope` reference, with no alias; run full `dotnet test` and confirm zero regressions across all pre-existing tests | SharedKernel.Contracts.Tests | `○` |
| T-09 | (WO-052/P-331) Unit tests for `EventEnvelope<TEvent>.TenantId` — `Wrap` without a `tenantId` argument leaves `TenantId = null` (backward-compatible default); `Wrap` with `tenantId: someGuid` sets `TenantId` correctly; STJ round-trip covering both the null case and the populated case; record structural equality includes `TenantId` | SharedKernel.Contracts.Tests | `○` |
| T-10 | (WO-052/P-332) Unit tests for `CursorPagedList<T>` — `Create` factory guard clause (`items = null` throws `ArgumentNullException`); `HasMore = true` with populated `NextCursor`; `HasMore = false` with `NextCursor = null` (terminal page); empty `Items` with `HasMore = false`; record structural equality; STJ round-trip for `CursorPagedList<string>` using the test-level source-generated context | SharedKernel.Contracts.Tests | `○` |

---

## Phase: Docs <!-- phase-key: SK.04.Docs -->

> XML doc comments on all public APIs, README with usage examples.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| DO-01 | XML doc on `PagedList<T>` — `<summary>` (cross-service paged result DTO), `<typeparam>` for T, `<remarks>` (1-based page convention, `Create`-only construction path, `TotalPages` divide-by-zero handling), per-property and `<param>` on `Create` factory | SharedKernel.Contracts | `●` |
| DO-02 | XML doc on `Envelope` and `Envelope<T>` — `<summary>` (cross-service transport counterparts to `Result<T>`), `<remarks>` (boundary contract: construct only at service boundaries — presentation/HTTP client adapters, never from application layer), `<seealso cref="Result{T}"/>` cross-reference | SharedKernel.Contracts | `●` |
| DO-03 | XML doc on `IIntegrationEvent` — `<summary>` (public contract projection of a domain event), `<remarks>` (implementations must be `sealed record` or `sealed class`; immutable DTOs; no behavior, no domain logic; consumers must never cast back to domain type; `EventId` maps to `IDomainEvent.Id`), cross-reference to `IDomainEvent` | SharedKernel.Contracts | `●` |
| DO-04 | XML doc on `EventEnvelope<TEvent>` — `<summary>` and per-property `<remarks>` for all 8 properties: `EventId` vs domain event `Id` distinction, `OccurredOn` sourcing, `EventType` routing purpose, `EventVersion` attribute-sourcing and default-1 behaviour, `CorrelationId` null semantics, `CausationId` causal chain intent, `SourceService` publisher identity, `Payload` as the wrapped domain event | SharedKernel.Contracts | `●` |
| DO-05 | XML doc on `ContractsJsonContext` — `<summary>` and `<remarks>` instructing consumers to not reference this context directly; instruct on creating own `partial JsonSerializerContext` with `[JsonSerializable(typeof(EventEnvelope<YourEvent>))]` and merging via `JsonSerializerOptions.TypeInfoResolverChain` | SharedKernel.Contracts | `●` |
| DO-06 | Author `README.md` at `04.Contracts/SharedKernel.Contracts/README.md` with five sections: (1) purpose and what belongs / does not belong; (2) quick-start code examples for all five surfaces; (3) `Result<T>` vs `Envelope<T>` boundary rule; (4) STJ usage pattern for consuming services; (5) `EventEnvelope<TEvent>` composition pattern as used by `07.Messaging` | SharedKernel.Contracts | `●` |
| DO-07 | (WO-026/P-166) XML doc on all four `ResultEnvelopeExtensions` methods — each method must have: `<summary>` stating direction of mapping (e.g. "Maps a Result&lt;T&gt; to an Envelope&lt;T&gt; for serialization at a service boundary"); `<remarks>` with a two-line usage example showing the call site pattern (typed client method, controller action, or gRPC server handler); `<seealso cref="Envelope{T}"/>` / `<seealso cref="Result{T}"/>` cross-references; update `README.md` section 3 (`Result<T>` vs `Envelope<T>` boundary rule) to include a subsection showing how `result.ToEnvelope()` and `envelope.ToResult()` replace inline mapping boilerplate at typed client call sites and controller actions | SharedKernel.Contracts | `●` |
| DO-08 | (WO-051/P-314) Correct `EventEnvelope<TEvent>.Payload`'s XML doc `<remarks>` in `Events/EventEnvelope.cs` — remove the false claim that `where TEvent : IDomainEvent` guarantees `Payload` exposes `Id`, `OccurredOn`, **and `AggregateId`** (`IDomainEvent` has never declared an `AggregateId` member — verified against the shipped `03.Domain/SharedKernel.Domain/Events/IDomainEvent.cs`, which exposes only `Id`/`OccurredOn`); replace with an accurate statement that the bare constraint guarantees only `Id`/`OccurredOn`, and that `AggregateId` is available **only** when the concrete `TEvent` additionally implements `03.Domain`'s `IHasAggregateId<TId>` opt-in marker (`SharedKernel.Domain.Abstractions.IHasAggregateId<TId>`, WO-051/P-309 — `where TId : notnull`, single member `TId AggregateId { get; }`); add a short usage note recommending `Payload is IHasAggregateId<TId> hasAggregateId` pattern-matching rather than assuming the member exists unconditionally; may cross-reference via `<see cref="IHasAggregateId{TId}"/>` once P-309 has shipped (mirrors the existing doc-only `DomainEventVersionAttribute`/`DomainEventVersionHelper` cross-reference precedent — does not add `IHasAggregateId<TId>` to `04.Contracts`'s consumed/re-exported public-API type list); documentation-only — no change to `EventEnvelope<TEvent>`'s shape, constructor, or `Wrap` factory; **sequence after** 03.Domain's P-309 ships so the `<see cref>` resolves against a real compiled type | SharedKernel.Contracts | `●` |
| DO-09 | (WO-052/P-328) Remove the "Namespace/type name collision" bullet from `CLAUDE.md`'s Implementation Rules (superseded — already done in this planning pass); update every XML doc `<seealso>`/cross-reference tag anywhere in the package that mentions the old `SharedKernel.Contracts.Envelope` namespace; update `README.md`'s Envelope usage examples to the new namespace and delete the `using EnvelopeNs = ...` alias workaround snippet | SharedKernel.Contracts | `○` |
| DO-10 | (WO-052/P-331) XML doc on `EventEnvelope<TEvent>.TenantId` — `<summary>` stating it is nullable envelope-level routing metadata for multi-tenant event correlation; `<remarks>` stating explicitly what is and is not guaranteed (populated only when the publisher supplies it via `Wrap`; `null` is valid and expected for non-tenanted/root events; NOT derived from `Payload`; carries no guarantee about the domain event's own tenant awareness, since `IDomainEvent` itself declares no tenant member); update `Wrap`'s `<param name="tenantId">` doc; update README's `EventEnvelope<TEvent>` composition-pattern section with a short note on when a publisher should populate it (e.g., bridging `07.Messaging`'s `IMessageHeaderPropagator` tenant header into the durable wire format) | SharedKernel.Contracts | `○` |
| DO-11 | (WO-052/P-332) XML doc on `CursorPagedList<T>` — `<summary>` (cross-service cursor/keyset-paginated result DTO, counterpart to `PagedList<T>` for offset pagination), `<remarks>` explaining the deliberate absence of `TotalCount`/`Page`/`PageSize` and cross-referencing `03.Domain`'s `KeysetSpecification<T, TKey>` (doc-only, no compile dependency) and `06.Persistence`'s queued EF Core translation (P-317/WO-051); add a new README Quick-Start section for `CursorPagedList<T>` stating explicitly when to use it over `PagedList<T>` (large/actively-written/infinite-scroll result sets where a stable total count is expensive or meaningless) vs. when to keep using `PagedList<T>` (small/random-access/UI-paged sets needing a total count) | SharedKernel.Contracts | `○` |

---

## Phase: Published <!-- phase-key: SK.04.Published -->

> NuGet packaging metadata, pack, publish, and consumer verification.

| ID | Task | Package(s) | State |
|----|------|-----------|:-----:|
| P-01 | Harden `.csproj` NuGet metadata — verify all required fields present: `PackageId`, `Version 1.0.0`, `Description`, `Authors`, `PackageTags` (contracts;dtos;integration-events;paged-list;envelope;shared-kernel), `PackageLicenseExpression MIT`, `RepositoryUrl`, `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` | SharedKernel.Contracts | `●` |
| P-02 | Run `dotnet pack` on `SharedKernel.Contracts.csproj`; confirm `SharedKernel.Contracts.1.0.0.nupkg` is produced in `nupkgs/`; confirm the generated `.xml` documentation file is included in the package alongside the DLL | SharedKernel.Contracts | `●` |
| P-03 | Create `consumer-verify` console project referencing `SharedKernel.Contracts`; exercise all five surfaces (create `PagedList<string>`, wrap `Envelope<string>`, define `IIntegrationEvent` record, call `EventEnvelope.Wrap`, serialize via consuming `JsonSerializerContext` extending `ContractsJsonContext`); run `dotnet run` and confirm zero reflection fallback | SharedKernel.Contracts | `●` |
| P-04 | Final gate — confirm all existing tests pass with zero regressions; update Package Board in this state-map to reflect `Published` state with `.nupkg` manifest entry (`SharedKernel.Contracts 1.0.0`) | SharedKernel.Contracts | `●` |
| P-05 | (WO-026/P-166) Re-pack `SharedKernel.Contracts` after `ResultEnvelopeExtensions` ships — bump version to `1.1.0`; update `PackageTags` to include `result-envelope-mapping`; run `dotnet pack` and confirm `SharedKernel.Contracts.1.1.0.nupkg` produced in `nupkgs/`; extend `consumer-verify` console project to call `result.ToEnvelope()` and `envelope.ToResult()` with both generic and non-generic variants and assert the mappings at runtime; run `dotnet test` and confirm all tests (including T-07) pass with zero regressions; update Package Board entry to `1.1.0` | SharedKernel.Contracts | `●` |
| P-06 | (WO-052/P-328) Bump `SharedKernel.Contracts` to `2.0.0` (major — breaking namespace rename); update `PackageReleaseNotes` (or equivalent) explicitly calling out the `SharedKernel.Contracts.Envelope` → `SharedKernel.Contracts.Envelopes` rename as a breaking source change requiring consumers to update `using` statements; run `dotnet pack` and confirm `SharedKernel.Contracts.2.0.0.nupkg`; update the `consumer-verify` project's `using` statements to the new namespace and confirm it still builds/runs against the packed version | SharedKernel.Contracts | `○` |
| P-07 | (WO-052/P-331) Extend `consumer-verify` to call `EventEnvelope.Wrap` both with and without the new `tenantId` argument and assert `TenantId` is set/null correctly; if released in the same pass as P-06/P-08, do not double-bump the version — a single `2.0.0` pack already covers this additive change (a major bump subsumes any additive change released in the same pass) | SharedKernel.Contracts | `○` |
| P-08 | (WO-052/P-332) Extend `consumer-verify` to construct a `CursorPagedList<string>` via `Create`, serialize/deserialize it through the consumer's merged `TypeInfoResolverChain`, and assert round-trip fidelity; confirm `00.Governance`'s `ContractsPurityRules` architecture test suite passes against the new type with zero exemption; final gate — `dotnet build`/`dotnet test` fully green; update Package Board to the final shipped version (`2.0.0`, consolidating P-328/P-331/P-332 if released together, per the sequencing note in P-06/P-07) | SharedKernel.Contracts | `○` |

---

## Overall Progress

> Counts updated whenever a task state changes.

| Phase Key | Phase | Total | ● Done | ○ Pending | State |
|-----------|-------|:-----:|:------:|:---------:|:-----:|
| `SK.04.Design` | Design | 10 | 7 | 3 | `◐` |
| `SK.04.Scaffold` | Scaffold | 7 | 5 | 2 | `◐` |
| `SK.04.Core` | Core | 10 | 7 | 3 | `◐` |
| `SK.04.Tests` | Tests | 10 | 7 | 3 | `◐` |
| `SK.04.Docs` | Docs | 11 | 8 | 3 | `◐` |
| `SK.04.Published` | Published | 8 | 5 | 3 | `◐` |

---

## Changelog

> One line per session. Format: `[YYYY-MM-DD] {what changed} — {trigger}`.

- [2026-05-30] Sub state-map initialized — phase key registry, 6 phases scaffolded at ○, no tasks yet
- [2026-05-30] All 32 tasks added across 6 phases (D-01–D-06, S-01–S-04, C-01–C-06, T-01–T-06, DO-01–DO-06, P-01–P-04) — WO-011 (P-055 EventEnvelope) + WO-012 (P-058 Scaffold, P-059 Core, P-060 Tests, P-061 Docs, P-062 Published)
- [2026-05-30] D-01–D-06 → ● in SK.04.Design — all 5 DTO types defined and implemented; 62 tests green; Scaffold next (state-map-phase)
- [2026-05-30] S-01–S-04 → ● in SK.04.Scaffold — csproj, subfolders, test project with SharedKernel.Testing ref, solution registered; 62 tests green (state-map-phase)
- [2026-05-30] C-01–C-06 → ● in SK.04.Core — PagedList, Envelope, Envelope<T>, IIntegrationEvent, EventEnvelope, ContractsJsonContext all implemented; 62 tests green (state-map-phase)
- [2026-05-30] T-01–T-06 → ● in SK.04.Tests — 62 tests green; all DTO types covered; STJ round-trips via source-generated context; Docs next (state-map-phase)
- [2026-05-30] DO-01–DO-06 → ● in SK.04.Docs — XML doc on all public APIs; seealso Result{T} cross-refs; per-property remarks on EventEnvelope; README.md authored (state-map-phase)
- [2026-05-30] P-01–P-04 → ● in SK.04.Published — csproj metadata hardened; nupkg produced with XML docs; consumer-verify console harness exercises all 5 surfaces with source-generated STJ; 62 tests green; ContractsSerializerDefaults added for consumer resolver chain access (state-map-phase)
- [2026-06-18] WO-026/P-166: 6 tasks added (D-07, S-05, C-07, T-07, DO-07, P-05) for ResultEnvelopeExtensions static class in SharedKernel.Contracts.Mapping — four pure extension methods bridging Result<T>/Result ↔ Envelope<T>/Envelope; namespace SharedKernel.Contracts.Mapping; Mapping/ subfolder; version bump to 1.1.0 on completion; all phases set to ◐ (contracts-arch-planner)
- [2026-06-18] D-07, S-05, C-07, T-07, DO-07, P-05 → ● — ResultEnvelopeExtensions implemented; 72 tests green; SharedKernel.Contracts 1.1.0.nupkg produced; consumer-verify passing 6 surfaces; all phases now ● (P-166/WO-026)
- [2026-07-29] WO-051/P-314: DO-08 added to SK.04.Docs — `EventEnvelope<TEvent>.Payload`'s shipped XML doc was found to falsely claim `where TEvent : IDomainEvent` guarantees `AggregateId` (verified against the real `IDomainEvent` source: only `Id`/`OccurredOn` are declared); corrected wording specified, pointing to `03.Domain`'s new opt-in `IHasAggregateId<TId>` marker (WO-051/P-309) as the actual, real (not hypothetical) source of that member; documentation-only, no shape/behavior change; Cross-Domain Dependencies gained a `SK.04.Docs` row on `03.Domain`'s P-309; SK.04.Docs set to ◐ (8 total, 7 done, 1 pending) pending sequencing after P-309 ships (contracts-arch-planner)
- [2026-07-30] DO-08 → ● in SK.04.Docs — verified `IHasAggregateId<TId>` shipped in `SharedKernel.Domain` v1.7.0 (`SharedKernel.Domain.Abstractions.IHasAggregateId<TId>`, `where TId : notnull`, single member `TId AggregateId { get; }`) before adding the `<see cref>`; corrected `EventEnvelope<TEvent>.Payload`'s XML doc `<remarks>` in `Events/EventEnvelope.cs`; `dotnet build SharedKernel.Contracts.csproj -c Release` 0 errors; Cross-Domain Dependencies `SK.04.Docs` row updated from Pending to Available with verified specifics; SK.04.Docs now 8/8 `●` — all six phases of `04.Contracts` are `●` complete (state-map-phase)
- [2026-07-31] WO-052 (P-328/P-331/P-332, all `Depends on: None`, no ordering constraint between them): 18 tasks added across all six phases — Design D-08/D-09/D-10, Scaffold S-06/S-07 (no Scaffold task needed for P-331 — `EventEnvelope<TEvent>` already exists), Core C-08/C-09/C-10, Tests T-08/T-09/T-10, Docs DO-09/DO-10/DO-11, Published P-06/P-07/P-08. P-328 renames the `Envelope`/`Envelope<T>` namespace from `SharedKernel.Contracts.Envelope` to `SharedKernel.Contracts.Envelopes` (folder `Envelope/` → `Envelopes/`), eliminating the namespace-type collision documented since inception — type shapes unchanged, namespace-only move, target version `2.0.0` (breaking). P-331 adds nullable `TenantId` (`Guid?`) to `EventEnvelope<TEvent>` plus a trailing optional `Guid? tenantId = null` parameter on `Wrap` — purely additive. P-332 adds `CursorPagedList<T>` (`Items`/`NextCursor`/`HasMore`, no `TotalCount`/`Page`/`PageSize`) in `Pagination/` as the cursor/keyset-pagination counterpart to `PagedList<T>`, doc-cross-referenced (no compile dependency) to `03.Domain`'s shipped `KeysetSpecification<T, TKey>` (P-308/WO-051) — also additive. All three are expected to ship together in one release; target consolidated version `2.0.0` (the major bump from P-328 subsumes P-331/P-332's additive changes in the same pass). All six phases set to `◐` In Progress; Package Board note updated to record the queued release without altering the currently-shipped `1.1.0` state (contracts-arch-planner)
