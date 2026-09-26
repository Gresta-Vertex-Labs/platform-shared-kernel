# 04.Contracts — Cross-Service Wire Contracts

> **Audience:** maintainers and AI agents changing code in this folder.
> **Consumers** read [`SharedKernel.Contracts/README.md`](SharedKernel.Contracts/README.md); the folder overview is
> [`README.md`](README.md). This brain holds only what the source does not make obvious: rules, traps,
> couplings and decisions. It never repeats the README.

## What This Domain Is

The wire-contract layer: shapes that cross a process boundary between services. Integration events and their
CloudEvents envelope; offset and cursor paged results; page requests; the keyset cursor codec.

**Philosophy:** a contract is a public API with a wire format; fixed names; construction and deserialization
enforce the same rules; no business logic; no domain types.

**Hard rules**

1. **Model tier** (`<SharedKernelTier>Model</SharedKernelTier>`): references `SharedKernel.Primitives` (Foundation) only
   and no third-party package; the build enforces it (SKTIER001/003). Never `SharedKernel.Domain`
   (`ContractsNeverReferencesDomain`, although both are Model tier), never `SharedKernel.Execution`,
   infrastructure, DI, logging or HTTP types.
2. No domain logic. Allowed behaviour: factories, validation of the type's own invariants, projection (`Map`),
   value equality, and the cursor codec.
3. Every JSON member name is fixed with `[JsonPropertyName]` (constants for envelope names). A serializer's naming
   policy must never change the wire shape.
4. A `[JsonConstructor]` validates exactly like the public factory and throws `JsonException`; the factory throws
   `ArgumentException`-family exceptions. No state a factory would reject may be reachable by deserialization or
   `with`.
5. Every public API change is recorded in `PublicAPI.Unshipped.txt`; every public member has XML docs.
6. Shipped docs, XML comments and release notes never contain WO/P IDs or change history.
7. AOT and trimming are **not** constraints (user ruling, 2026-09-15). Reflection-based `System.Text.Json` is the
   serializer; do not reintroduce a `JsonSerializerContext`.

---

## Packages

| Package | Role | References |
| --- | --- | --- |
| `SharedKernel.Contracts` (Model tier) | Integration events, CloudEvents envelope, paging contracts | `SharedKernel.Primitives` |

| Project | Purpose |
| --- | --- |
| `SharedKernel.Contracts/SharedKernel.Contracts.Tests/` | Unit tests; references only the package (not `16.Testing`) so it builds in seconds |
| `SharedKernel.Contracts.ConsumerVerify/` | Restores the **packed** package (never the project) and exercises its public API; run it against the feed before a publish |

Build settings: zero third-party dependencies; `Microsoft.CodeAnalysis.PublicApiAnalyzers` (private);
`nullable`, `CS1591` and RS0016/17/22/24/25/36/37 are errors; XML docs and `README.md` ship in the package.

## Technology Stack

| Concern | Choice |
| --- | --- |
| Serialization | Reflection-based `System.Text.Json`; internal `[JsonConstructor]` constructors |
| Event wire format | CloudEvents 1.0 structured JSON; extensions `dataversion`, `tenantid`, `correlationid`, `causationid` |
| Event identity | Required `[IntegrationEvent(name, Version)]`, resolved and cached by `IntegrationEventDescriptor` |
| Cursor format | `v1.` + base64url(`[key, id]` JSON), `System.Buffers.Text.Base64Url` |
| Validation results | `SharedKernel.Primitives` `ValidationResult<T>`, `Result<T>`, `Error.Validation` |

## DI Registration

None.

## AOT Compatibility

Not a design constraint. Reflection in use: attribute lookup in `IntegrationEventDescriptor` (cached per type);
reflection-based serialization of generic envelopes and pages.

---

## Interface Contracts

| Namespace (`SharedKernel.Contracts.`) | Types |
| --- | --- |
| `Events` | `IIntegrationEvent` (`EventId`, `OccurredOn`); `IntegrationEventAttribute` (`Name`, `Version`); `IntegrationEventDescriptor` (`For<TEvent>()`, `For(Type)`); `EventEnvelope<TEvent>` (`where TEvent : class, IIntegrationEvent`); `EventEnvelope` (`Wrap`, `CloudEventsSpecVersion`, `JsonContentType`); `CloudEventAttributeNames` |
| `Pagination` | `PagedList<T>`; `CursorPagedList<T>`; `PageRequest`; `CursorPageRequest`; `PageCursor` (`Encode`, `Decode`, `MaxLength`); `CursorPosition<TKey, TId>`; `PaginationErrorCodes` |

---

## Implementation Rules

### Events

| Rule | Why |
| --- | --- |
| `EventEnvelope.Wrap` is the only construction path; the generic record has internal constructors and get-only properties | Metadata can never be assembled inconsistently; no architecture rule is needed to police construction |
| `Wrap` requires `typeof(TEvent) == evt.GetType()` | Serializing as a base type or interface silently drops the runtime type's members |
| Deserialization checks `type` against `IntegrationEventDescriptor.For<TEvent>().Name`, and `id`/`time` against `Data` | A misrouted or tampered message fails at the edge. Do **not** add a `dataversion` equality check: a consumer must be able to read an older version during a rollout |
| Optional envelope members use `JsonIgnoreCondition.WhenWritingNull` | CloudEvents requires absent attributes to be omitted, not `null` |
| Extension attribute names are lowercase alphanumeric, at most 20 characters | CloudEvents extension naming rule; add new ones to `CloudEventAttributeNames` |
| Event names: lowercase ASCII letters/digits, single `.`/`-`/`_` separators, 1–128 characters; version ≥ 1; attribute not inherited | Safe as broker routing keys, subscription filters and URL segments |
| A name+version pair belongs to one type per process (`Claims` map) | Two types with one identity would deserialize each other's messages |
| `FindProblem` returns `(Parameter, Message)` shared by `Wrap` (ArgumentException) and the JSON constructor (JsonException) | One rule set, two exception types |

### Pagination

| Rule | Why |
| --- | --- |
| Items are snapshotted (`PageItems.Snapshot`) into a `ReadOnlyCollection<T>` | A caller mutating its list later must not change a published page |
| `PagedList<T>` enforces only `Items.Count ≤ PageSize`, never against `TotalCount` | Count and page are separate queries; concurrent writes make them disagree, and throwing would fail real requests |
| `TotalCount`/`TotalPages` are `long` | Large tables and search engines exceed `int` |
| `CursorPagedList<T>.HasMore` is computed from `NextCursor` | Two stored fields could contradict each other |
| Custom `Equals`/`GetHashCode` compare items by sequence | Record equality compares the list reference |
| `PageRequest.MaxPageSize` = `CursorPageRequest.MaxLimit` = 1000 — the platform's only page-size ceiling since P-558 (`03.Domain` no longer has one); `Offset` fits `int` | A valid request is accepted by `06.Persistence`'s `ListPagedAsync`/`ListKeysetAsync` without a second failure |
| Request factories return `ValidationResult<T>` with every error, page error first; an out-of-range `maxPageSize`/`maxLimit` argument throws | Client input is a result; a server misconfiguration is a bug |
| `PageCursor.Decode` never throws for input; it catches only `FormatException`, `JsonException`, `NotSupportedException`, `InvalidOperationException` | Cursors come from clients; unexpected exception types still surface |
| Cursor format changes go behind a new prefix (`v2.`) and `Decode` keeps reading `v1.` for one release | Clients hold cursors across a deploy |

### General

- JSON names for pages and requests are camelCase literals; envelope names are `CloudEventAttributeNames` constants.
- Error codes live in `PaginationErrorCodes`; never retype them.
- Keep `README.md` outputs real: they were produced by running the snippets.

---

## Cross-Domain Couplings

Changes here that silently break another layer. Check the right column before merging.

| If you change… | Also check |
| --- | --- |
| `EventEnvelope.Wrap` parameters or envelope property names | `07.Messaging` `MassTransitEventPublisher`, `PublishContext.Subject`; MassTransit harness tests; `16.Testing` `SharedKernel.Testing` `EventEnvelopeBuilder`, `SharedKernel.Messaging.Testing` `InMemoryEventPublisher` |
| `IntegrationEventDescriptor` name rules or caching | `15.Integration` `WebhookDispatcher` routing key and `WebhookSubscription` event types; `07.Messaging` telemetry tags |
| `IIntegrationEvent` | `07.Messaging` `IEventPublisher` constraint; `15.Integration` `IWebhookDispatcher`; `16.Testing` `SharedKernel.Testing` `IntegrationEventFaker` |
| `PagedList<T>` factories or `TotalCount` type | `06.Persistence` `EfReadRepository.ListPaged*`; `09.Search` `SearchResults.ToPagedList`; `14.Presentation` `SharedKernel.Presentation.GraphQL` `PagedResponseType`; `16.Testing` `SharedKernel.Persistence.Testing` `FakeRepository`, `SharedKernel.Testing` `PagedListBuilder`/`PagedListAssertions` |
| `PageRequest`/`CursorPageRequest` shape or limits | `06.Persistence` `IReadRepository.ListPagedAsync`/`ListKeysetAsync` (take them directly) and `16.Testing` `SharedKernel.Persistence.Testing` `FakeRepository` |
| `PageCursor` format | Every client holding a cursor; keep the old prefix decodable for a release |
| Contracts purity or references | `00.Governance` `ContractsPurityRules`, `SharedKernelLayeringRules.ContractsNeverReferencesDomain`; the tier check (`eng/SharedKernelTiers.targets`) |
| `EventEnvelope.TenantId` type | `07.Messaging` `MassTransitEventPublisher` converts `PublishContext.TenantId` (`TenantId?`, set by `WithTenantId`) to `Guid?` when wrapping |
| Any public API | `PublicAPI.Unshipped.txt`, `SharedKernel.Contracts.ConsumerVerify`, the package README |

---

## Decision Records

| Decision | Chosen | Rejected | Accepted cost |
| --- | --- | --- | --- |
| Envelope payload | Integration events (`IIntegrationEvent`) | Domain events on the wire (leaks internals, couples consumers to the producer's model) | Publishers map domain → integration event |
| Event identity | Required attribute name + version | Class name (a rename breaks consumers); attribute with class-name fallback | Every event declares an attribute |
| Envelope JSON | CloudEvents 1.0 structured mode | Platform-specific member names | Property names `Type`/`Source`/`Data` instead of `EventType`/`SourceService`/`Payload` |
| Response envelope `Envelope<T>` | Removed; ProblemDetails for errors, raw bodies for success | Keeping a second, competing wire format that no server produced | REST client maps to `Result<T>` itself |
| Serialization | Reflection STJ | Source-generated `ContractsJsonContext` (could not cover consumer generic types, ignored its own naming policy through a resolver chain) | Not trimming-safe |
| `03.Domain` reference | Removed | Keep for `Money` mapping or domain-event wrapping | Services map `Money` themselves |
| `TotalCount` | `long` | `int` | Consumers adjust types |
| Cursor | Unsigned, versioned, strictly decoded | HMAC-signed (key management in every service) | Queries must enforce tenant and authorization filters |
| Money DTO | Not included | `MoneyDto` | Added when a real cross-service need appears |
| `EventEnvelope.TenantId` type | `Guid?` on the wire contract | `SharedKernel.Execution.Tenancy.TenantId?` (would add a Foundation reference beyond `Primitives` and tie the wire format to a kernel type) | Publishers convert `TenantId?` → `Guid?`; consumers wrap it back with `TenantId.FromNullable` |

---

## Test Rules

- Every rule in "Implementation Rules" has a test, including the JSON path (`JsonException`) and the factory path.
- Envelope JSON tests run with two naming policies (Web and default) to prove names are fixed.
- Test events declare unique name+version pairs; the descriptor cache is process-wide, so a duplicate in one test
  file breaks another.
- No `16.Testing` reference: tests use only the package, xUnit and FluentAssertions.
- `SharedKernel.Contracts.ConsumerVerify` is updated with every public API change.

---

## Changelog

> Maintained by the contracts domain agent. One line per significant change. Entries before 2026-09-15 are historical: they describe what was true when written (`Envelope`, `IDomainEvent` payloads, source-generated serialization, per-package versions), not current behaviour.

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
- [2026-07-31] SK.04.Published complete (P-06/P-07/P-08; 8/8) — `SharedKernel.Contracts` re-packed and shipped at `2.0.0` (breaking `Envelope`→`Envelopes` rename + additive `TenantId`/`CursorPagedList<T>` all now live); `consumer-verify` extended from 6 to 7 surfaces (namespace fix, `Wrap` with/without `tenantId` + STJ round-trip, `CursorPagedList<T>` construction + STJ round-trip); 87/87 tests green. All six phases now `●` — WO-052 v2.0.0 cycle complete end to end. GOVERNANCE FINDING recorded for future sessions (NOT a `04.Contracts` defect, NOT fixed here — wrong jurisdiction): a throwaway harness pointing `00.Governance`'s `ContractsPurityRules` at the real compiled `SharedKernel.Contracts.dll` for the first time (its own `ContractsPurityRulesTests.cs` tests only contrived fixtures, never the real assembly, at any version) found `CursorPagedList<T>` passes the domain-type/`Result`-type public-surface rules cleanly with zero exemption — but `ContractsAssembliesHaveNoNonTrivialMethods` fails against the real assembly for every type carrying a static factory or extension method (`PagedList<T>.Create`, `Envelope.Ok`/`.Fail`, `Wrap`, `ResultEnvelopeExtensions`'s methods, and now `CursorPagedList<T>.Create` too) — its "trivial method" allowlist has never included factory/extension methods, a pre-existing `00.Governance` predicate gap dating to `PagedList<T>`'s 1.0.0 introduction, not something this phase introduced. Also found: the domain-type rule's exemption only names the generic `EventEnvelope\`1`, never the sibling non-generic static `EventEnvelope` class that also carries an `IDomainEvent` constraint via `Wrap<TEvent>` — independently fails the same rule, also pre-existing since WO-011. Candidate follow-up for `00.Governance` (governance-arch-planner/governance-phase-implementer) — no `00.Governance` file touched (contracts-phase-implementer)
- [2026-09-15] Pre-first-publish redesign by user ruling. Removed: `Envelope`/`Envelope<T>`, `ResultEnvelopeExtensions`, `ContractsJsonContext`/`ContractsSerializerDefaults`, the `03.Domain` reference. `EventEnvelope<TEvent>` now wraps `IIntegrationEvent`, serializes as CloudEvents 1.0 (`Type`/`Source`/`Data`/`DataVersion`/`Subject`), and is constructible only through `Wrap`; event identity comes from a required `[IntegrationEvent]` attribute via `IntegrationEventDescriptor`. `PagedList<T>.TotalCount` is `long`; pages snapshot items, compare by value and validate on deserialization; `CursorPagedList<T>.HasMore` is derived. Added `PageRequest`, `CursorPageRequest`, `PageCursor`, `CursorPosition<TKey, TId>`, `PaginationErrorCodes`, `Map`/`Empty`/`FromLookahead`. Public API tracked; 105 tests; brain, README and landing page rewritten. Cross-domain migrations in 06/07/09/11/15/16/00 (coordinator)
- [2026-09-21] P-558: no API change; docs point paging at 06.Persistence ListPagedAsync/ListKeysetAsync (Paged/KeysetSpecification removed from 03.Domain) (agent)
- [2026-09-26] WO-086 (P-565, P-574, P-575): Model tier; `EventEnvelope.TenantId` deliberately stays `Guid?` (wire contract; the package references `SharedKernel.Primitives` only) while the rest of the platform moved to `TenantId`; numbered-layer `ContractsLayeringRules` replaced by the tier check plus `ContractsNeverReferencesDomain`; coupling table updated for the split `16.Testing` packages and `Presentation.GraphQL` (agent)
