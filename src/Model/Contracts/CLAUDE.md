# 04.Contracts — Domain Brain

> The wire-contract layer: shapes that cross a process boundary between services. Integration events and their
> CloudEvents 1.0 envelope, offset and cursor paged results, page requests, and the keyset cursor codec. A contract is
> a public API with a wire format: fixed member names, construction and deserialization enforce the same rules, no
> business logic, no domain types. This domain deliberately has **no** response envelope (handlers return `Result`,
> `14.Presentation` maps it to a body or RFC 9457 ProblemDetails), no domain types (`Money` etc. stay in `03.Domain`),
> no DI registration, and no cross-service propagation constants (those are `WellKnownHeaders` in `01.Core`). Consumers
> read [`SharedKernel.Contracts/README.md`](SharedKernel.Contracts/README.md); phase status is on the living board
> (`state-map.md`).

## Packages

| Package | Tier | Purpose |
| --- | --- | --- |
| `SharedKernel.Contracts` | Model | Integration events, `EventEnvelope<TEvent>`, paging contracts, cursor codec. References `SharedKernel.Primitives` only; zero third-party runtime dependencies |
| `SharedKernel.Contracts.ConsumerVerify` | — (consumer harness, no tier) | Test project that restores the **packed** package (`PackageReference`, never the project) and exercises its public API; run by CI's `packaging-verify` job |

Test project: `SharedKernel.Contracts/SharedKernel.Contracts.Tests/`.

## Public Entry Points

No DI registration and no configuration section. Members, factories and samples: `SharedKernel.Contracts/README.md`.

- `SharedKernel.Contracts.Events`: `IIntegrationEvent`, `[IntegrationEvent("name", Version = n)]`, `IntegrationEventDescriptor.For<TEvent>().Name`, `EventEnvelope<TEvent>` built only by `EventEnvelope.Wrap(...)`, `CloudEventAttributeNames`.
- `SharedKernel.Contracts.Pagination`: `PagedList<T>`, `CursorPagedList<T>` (`FromLookahead`), `PageRequest`/`CursorPageRequest` (`Create` → `ValidationResult<T>`), `PageCursor` (`Encode`/`Decode` → `Result<CursorPosition<TKey, TId>>`, `MaxLength` = 512), `PaginationErrorCodes`.

## Rules & Invariants

1. **Model tier.** Reference `SharedKernel.Primitives` only — never `SharedKernel.Domain` (`ContractsNeverReferencesDomain`,
   although both are Model tier), never `SharedKernel.Execution`, infrastructure, DI, logging or HTTP types. SKTIER001/003 enforce the rest.
2. **No domain logic.** Allowed behaviour: factories, validation of the type's own invariants, projection (`Map`),
   value equality and the cursor codec.
3. **Fixed wire names.** Every JSON member carries `[JsonPropertyName]`; envelope names come from `CloudEventAttributeNames`,
   page/request names are camelCase literals. A serializer naming policy must never change the wire shape.
4. **One rule set, two exception types.** A `[JsonConstructor]` validates exactly like the public factory and throws
   `JsonException`; the factory throws `ArgumentException`-family exceptions. The envelope shares one `FindProblem`
   check between `Wrap` and its JSON constructor. No state a factory rejects may be reachable by deserialization or `with`.
5. **`EventEnvelope.Wrap` is the only construction path** — the generic record has internal constructors and get-only
   properties. `Wrap` requires `typeof(TEvent) == evt.GetType()` (serializing as a base type drops members).
6. **Deserialization checks identity:** `type` must equal `IntegrationEventDescriptor.For<TEvent>().Name`, and `id`/`time`
   must match `Data`. Do **not** add a `dataversion` equality check — consumers must read older versions during a rollout.
7. **Absent attributes are omitted**, not `null` (`JsonIgnoreCondition.WhenWritingNull`) — CloudEvents rule.
8. **Extension attribute names** are lowercase alphanumeric, ≤ 20 characters; add new ones to `CloudEventAttributeNames`.
   Current extensions: `dataversion`, `tenantid`, `correlationid`, `causationid`.
9. **Event names:** lowercase ASCII letters/digits with single `.`/`-`/`_` separators, 1–128 characters; version ≥ 1;
   the attribute is not inherited. A name+version pair belongs to one type per process (the descriptor's `Claims` map).
10. **Pages snapshot their items** into a `ReadOnlyCollection<T>` and compare items by sequence (custom `Equals`/`GetHashCode`).
11. **`PagedList<T>` enforces only `Items.Count ≤ PageSize`**, never against `TotalCount` (count and page are separate queries).
12. **`PageRequest.MaxPageSize` = `CursorPageRequest.MaxLimit` = 1000 is the platform's only page-size ceiling**; `Offset`
    fits `int`, so a valid request is always accepted by `06.Persistence`'s `ListPagedAsync`/`ListKeysetAsync`.
13. Request factories return `ValidationResult<T>` with every error (page error first); an out-of-range `maxPageSize`/
    `maxLimit` **argument** throws (server misconfiguration is a bug, client input is a result).
14. **`PageCursor.Decode` never throws for input**; it catches only `FormatException`, `JsonException`,
    `NotSupportedException`, `InvalidOperationException`. Cursor format is `v1.` + base64url(`[key, id]` JSON).
15. **Cursor format changes** go behind a new prefix (`v2.`) and `Decode` keeps reading `v1.` for at least one release.
16. Error codes live in `PaginationErrorCodes`; never retype them.
17. Every public API change goes in `PublicAPI.Unshipped.txt` with XML docs (`CS1591` and the PublicAPI analyzer rules are
    errors). XML comments never contain work-order ids or change history.
18. Keep `README.md` sample outputs real — produced by running the snippets.

## Decisions

| Decision | Why |
| --- | --- |
| Envelope carries integration events (`IIntegrationEvent`), not domain events | Domain events on the wire leak internals and couple consumers to the producer's model; publishers map at the boundary |
| Event identity from a required `[IntegrationEvent(name, Version)]` | A class rename must not break consumers; no class-name fallback |
| CloudEvents 1.0 structured JSON | Interop; properties are `Type`/`Source`/`Data`, not platform-specific names |
| No response envelope (`Envelope<T>`/`ApiResponse<T>`) | ProblemDetails for errors, raw bodies for success; the REST client maps to `Result<T>` itself. Decline if proposed again |
| Reflection-based `System.Text.Json`, no `JsonSerializerContext` | A source-generated context could not cover consumers' generic types and fought naming policies. Not trim-safe; accepted |
| `EventEnvelope.TenantId` is `Guid?`, not `TenantId?` | Keeps the wire contract free of `SharedKernel.Execution`; publishers convert, consumers use `TenantId.FromNullable` |
| `TotalCount` is `long` | Large tables and search engines exceed `int` |
| Cursor is unsigned, versioned, strictly decoded | Signing would need key management in every service; queries must still apply tenant and authorization filters |
| No `MoneyDto` or other domain-value DTOs | Added only when a real cross-service need appears; services map `Money` themselves |
| Propagation header/baggage constants never live here | `Presentation.Grpc`/`Communication.Grpc` may not reference Contracts; they are `WellKnownHeaders` in `SharedKernel.Primitives` |

## Logging

EventId block `4000`–`4999` (`LoggingEventIdRanges.Contracts`) is reserved but unused: Model-tier packages never log
(`ILogger` is never injected into a contract type).

## Cross-Domain Couplings

| If you change… | Also check |
| --- | --- |
| `EventEnvelope.Wrap` parameters or envelope property names | `07.Messaging` `MassTransitEventPublisher` and `PublishContext` (`Subject`, `WithSubject`, `WithTenantId`); `16.Testing` `EventEnvelopeBuilder` (`SharedKernel.Testing`), `InMemoryEventPublisher` (`SharedKernel.Messaging.Testing`) |
| `EventEnvelope.TenantId` type | `MassTransitEventPublisher` converts `PublishContext.TenantId` (`TenantId?`) to `Guid?` |
| `IntegrationEventDescriptor` name rules or caching | `15.Integration` `WebhookDispatcher` routing key and subscription event types; `07.Messaging` telemetry tags |
| `IIntegrationEvent` | `07.Messaging` `IEventPublisher` constraint; `15.Integration` `IWebhookDispatcher`; `16.Testing` `IntegrationEventFaker` |
| `PagedList<T>` factories or `TotalCount` type | `06.Persistence` `EfReadRepository`; `09.Search` `SearchResults.ToPagedList`; `14.Presentation` `PagedResponseType` (GraphQL); `16.Testing` `FakeRepository`, `PagedListBuilder`, `PagedListAssertions` |
| `PageRequest`/`CursorPageRequest` shape or limits | `06.Persistence` `IReadRepository.ListPagedAsync`/`ListKeysetAsync`; `14.Presentation` `Paging`/`CursorPaging` parameters; `16.Testing` `FakeRepository` |
| `PageCursor` format | Every client holding a cursor — keep the old prefix decodable for a release |
| References or purity | `00.Governance` `ContractsPurityRules`, `SharedKernelLayeringRules.ContractsNeverReferencesDomain`; the tier check (`eng/SharedKernelTiers.targets`) |
| Any public API | `PublicAPI.Unshipped.txt`, `SharedKernel.Contracts.ConsumerVerify`, the package README |

## Testing

- `SharedKernel.Contracts.Tests` — Unit lane. References only the package, xUnit and FluentAssertions (no `16.Testing`),
  so it builds in seconds.
- Every rule above has a test on both paths: the factory (`ArgumentException`) and the JSON constructor (`JsonException`).
- Envelope JSON tests run under two naming policies (Web and default) to prove names are fixed.
- Test events declare unique name+version pairs — the descriptor cache is process-wide, so a duplicate in one test file
  breaks another.
- `SharedKernel.Contracts.ConsumerVerify` is updated with every public API change; it builds against a packed version
  (`-p:SharedKernelPackageVersion=<version>`); CI's `packaging-verify` job discovers it automatically.
- Fakes: `SharedKernel.Testing` (`EventEnvelopeBuilder`, `IntegrationEventFaker`, `PagedListBuilder`, `PagedListAssertions`) — catalogue in `src/Testing/CLAUDE.md`.

## Known Limitations

- Reflection-based serialization: the package is not trimming- or native-AOT-safe for generic envelopes and pages.
- Cursors are not signed; a client can forge a position, so every keyset query must still apply its own tenant and
  authorization filters.
- `PagedList<T>.TotalCount` may disagree with the items under concurrent writes; this is tolerated by design.
