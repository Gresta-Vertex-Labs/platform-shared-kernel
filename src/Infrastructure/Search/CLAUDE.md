# 09.Search — Domain Brain

> The full-text search abstraction and its two engine adapters. Services depend on
> `SharedKernel.Search.Abstractions` to provision indexes, write documents (single, bulk, by filter), run BFF-shaped
> search (free text + scalar filters + sort + facets + paging + highlighting), count, walk a corpus for reindex/export,
> cut over staging → live atomically, and report readiness — never on an engine SDK. `SharedKernel.Search.Meilisearch`
> and `SharedKernel.Search.ElasticSearch` translate the neutral models, own their configuration, and each declares —
> inside its own package — typed contracts for capabilities only that engine has. Philosophy: **intersection-only,
> fail-loud, typed escape at the package seam, no silent degradation.** This domain does not own document mapping
> from aggregates, change-feed ordering, vector search (`10.Intelligence`) or engine deployment topology.

## Packages

| Package | Tier | Purpose |
| --- | --- | --- |
| `SharedKernel.Search.Abstractions` | Abstractions | Neutral contracts, closed filter AST and scalar union, request/result/receipt models, `SearchIndexDefinition`, `SearchErrors`, `SearchWellKnown`, `SearchIndexReadinessProbe`. References `SharedKernel.Primitives`, `SharedKernel.Execution` (`TenantScope`) and `SharedKernel.Contracts` (`PagedList<T>`) only. Ships no DI extensions |
| `SharedKernel.Search.Meilisearch` | Adapter | Meilisearch provider over the `MeiliSearch` SDK (named `IHttpClientFactory` client); exclusive `IInstantSearch<T>`, `ITenantSearchTokenIssuer`, `MeilisearchRankingRule` |
| `SharedKernel.Search.ElasticSearch` | Adapter | ElasticSearch 9.x/10.x provider over `Elastic.Clients.Elasticsearch`; alias read/write split; exclusive `IAnalyticsSearch<T>`, `ICursorSearch<T>`, `ISuggestSearch<T>` |
| `consumer-verify/Meilisearch`, `/ElasticSearch`, `/BothProviders` | — (not packable) | Composition roots over the packed packages; prove each provider's surface and the two-provider keyed resolution |

## Public Entry Points

API detail, option keys and defaults: the `SharedKernel.Search.Abstractions`, `.Meilisearch` and `.ElasticSearch` READMEs
(`SharedKernel.Search.*/README.md`).

**`.Abstractions`** (inject these; never `MeilisearchClient`/`ElasticsearchClient`):

- `ISearchDocument` — `string DocumentId` (charset `A-Z a-z 0-9 - _`).
- `ISearchIndex<TDocument>` (scoped) — write, delete-by-filter, search, get, `CountAsync` → `SearchCount`,
  `EnumerateAsync` → `IAsyncEnumerable<TDocument>`.
- `ISearchIndexProvisioner` (singleton, keyed + unkeyed) — `EnsureIndexAsync`, `CutoverAsync(IndexCutoverRequest)`,
  `VerifyRegisteredIndexesAsync`. `EnsureIndexAsync` is safe from every replica at once: a lost create race continues
  as on an existing index, and a Meilisearch index another replica created but has not configured yet is configured.
- `ISearchProviderDescriptor` (singleton, keyed + unkeyed, zero I/O) — ceilings, registered indexes, `Validate`.
- `SearchQuery.New()` → `IQueryBuilder` (`Build` → `Result<SearchRequest>`), or `SearchRequest.Default with { … }`.
- `SearchFilter` (`Eq`, `Ne`, `In`, `Between`, `Exists`, `All`, `Any`, `Negate`) over `SearchValue`
  (`String`/`Int64`/`Double`/`Boolean`/`DateTimeOffset`).
- `SearchIndexDefinitionBuilder` — primary key, `TenantField`, typed fields, synonyms, stop words, ceilings.
- `SearchResults<T>.ToPagedList()` → `Result<PagedList<T>>` — the only bridge to `04.Contracts`.
- `SearchWriteConsistency`, `SearchBulkReceipt`, `SearchCount`/`TotalHitsAccuracy`, `SearchErrors`,
  `SearchStreamException`, `SearchWellKnown` (provider names, instrument names, tag keys, defaults).

**`.Meilisearch`** — section `Search:Meilisearch` (`MeilisearchOptions.SectionName`):

```csharp
services.AddSharedKernelMeilisearchSearch(configuration)          // or (IConfigurationSection)
    .AddIndex<ProductSearchDocument>("products", i => i.PrimaryKey(…).TenantField(…).Field(…))
    .WithRankingRules("products", MeilisearchRankingRule.Words, …) // optional, replaces the engine default list
    .WithTenantTokens()                                            // ITenantSearchTokenIssuer; needs ApiKeyUid
    .AllowRawClientAccess()                                        // IMeilisearchRawClientAccessor; warns
    .Build();
```

Per index: scoped `ISearchIndex<T>`, `IInstantSearch<T>`. Errors: `MeilisearchErrors`.

**`.ElasticSearch`** — section `Search:ElasticSearch` (`ElasticSearchOptions.SectionName`):

```csharp
services.AddSharedKernelElasticSearchSearch(configuration)
    .AddIndex<OrderSearchDocument>(readAlias: "orders", writeAlias: "orders-write", i => …)
    .WithCompletionField<OrderSearchDocument>("orders", "nameSuggest")   // ISuggestSearch<T>
    .WithSourceSerializerContext(OrderSearchJsonContext.Default)          // required
    .AllowRawClientAccess()                                               // IElasticSearchRawClientAccessor; warns
    .Build();
await host.Services.VerifyElasticSearchEngineVersionAsync(ct);           // explicit, from a startup task
```

Per index: scoped `ISearchIndex<T>`, `IAnalyticsSearch<T>` (closed `AggregationRequest`), `ICursorSearch<T>`
(point-in-time + `search_after`). Errors: `ElasticSearchErrors`.

Each `Build()` registers one `SearchIndexReadinessProbe` per index, named `search-{provider}-{index}`
(`ProbeNameFor`). The host maps them with `AddSharedKernelReadiness()` and wires telemetry with
`WithSearchTelemetry()` (both `13.ServiceDefaults`).

## Rules & Invariants

**The seam**

1. `.Abstractions` contains nothing either provider cannot implement completely and correctly. If a member would force
   one adapter to throw, degrade, approximate or no-op, it does not belong there. Guard `SearchFieldDefinition` hardest:
   no analyzer, normalizer, tokenizer, boost or ranking knob.
2. Provider-exclusive contracts (`IInstantSearch`, `ITenantSearchTokenIssuer`, `IAnalyticsSearch`, `ICursorSearch`,
   `ISuggestSearch`, `MeilisearchRankingRule`) live only in their provider package, so a provider swap is a build error.
   No capability-flags enum on the neutral surface.
3. `.Abstractions` takes no third-party package (`SearchTopologyRules.AbstractionsHasNoThirdPartyDependencies`).
4. The two providers never reference each other nor expose the other engine's SDK types
   (`SearchTopologyRules.ProviderPackagesNeverReferenceEachOther`). Shared shape is duplicated deliberately — no shared
   base or `.Core`.
5. No `09.Search` package references `SharedKernel.Domain`, application/mediator, persistence, messaging, security or
   any other capability domain.

**Tenancy (a dropped tenant clause is a data breach)**

6. `TenantScope` is a mandatory, separate parameter on every read and every filtered write — never optional,
   defaulted, a `SearchRequest` member, or a clause in the caller's filter. Never declare a search-local tenant type.
7. Adapters inject the tenant predicate as the **outermost** `AND` after translating the caller's filter. An index whose
   definition declares `TenantField` called with `TenantScope.Global` fails with `SearchErrors.TenantScopeMissing` and
   performs no I/O. `GetAsync` must not return another tenant's document by id.
8. The completion suggester ignores query filters: on a tenanted index the tenant field is a category context on the
   completion mapping, and `ISuggestSearch` still requires `TenantScope`.
9. Raw-client accessors bypass tenant scoping. They are registered only by `.AllowRawClientAccess()`, which logs a
   startup warning (9115 / 9216); their XML docs say so in capitals. Callers apply their own tenant predicate.

**Fail loud**

10. Never silently drop, coerce or post-filter in memory a clause the engine cannot express. Every rejection is a
    `Result` failure returned before any I/O (undeclared field, over-ceiling page, over-cap facets, invalid document id,
    missing tenant scope).
11. No discard (`_ =>`) arm in either provider's `SearchFilter` translation switch, `SearchValueKind` rendering switch or
    the ElasticSearch `AggregationRequest` naming switch (see Decisions).
12. All errors come from `SearchErrors`, `MeilisearchErrors` or `ElasticSearchErrors` — no inline `Error`. An outage is
    `Error.Unavailable` (`search.unreachable`), a timeout `Error.Timeout` (`search.timeout`), auth
    `search.unauthorized` — identically on both providers even though Meilisearch's SDK throws and ElasticSearch's
    returns an invalid response. Never `Error.BusinessRule`, never `Error.None`.
13. Caller-requested cancellation propagates as `OperationCanceledException` and is never reported as a search failure.
14. Bulk writes return a **success** `Result` with `SearchBulkReceipt.Failures` for per-item failures; `Result.Failure`
    means the request itself did not execute.
15. `EnumerateAsync` and `ICursorSearch.StreamAsync` return `IAsyncEnumerable<T>` directly; mid-stream faults surface as
    `SearchStreamException` from `MoveNextAsync`. `EnumerateAsync` ordering is unspecified — never build resumable
    state on it.

**Contract shape**

16. `SearchWriteConsistency` (`Accepted`, `Searchable`) is mandatory and non-defaulted on every write; no third
    `refresh=true` value.
17. No `Score`, `Boost`, `ScoreThreshold`, `MinimumShouldMatch`, `Fuzziness` or typo flag on neutral types.
    `SearchHit<T>.Rank` (0-based ordinal in the page) is the only portable ordering signal.
18. No optimistic-concurrency `Version` on documents; ordering is the caller's job (partition the change stream by
    `DocumentId`).
19. No nested/object-array filter node — the portable technique is a precomputed composite field filtered with `In`.
    `Between` accepts only `Int64`, `Double`, `DateTimeOffset` bounds (throws `ArgumentException` otherwise).
20. Repeated `Where(...)` calls AND together; the builder is immutable (every call returns a new instance).
21. `ToPagedList()` fails with `SearchErrors.TotalHitsNotExact` unless the request set `RequireExactTotalHits` and the
    provider returned `TotalHitsAccuracy.Exact`. `CountAsync` on Meilisearch returns `LowerBound` at `MaxTotalHits`.
22. Synonyms (one-way) and stop words are part of the schema fingerprint; changing them — or an incompatible field — on a
    live index returns `SearchErrors.IndexDefinitionConflict`; the remedy is staging → bulk load → `CutoverAsync`.

**Registration and hygiene**

23. Engine clients, provisioners and descriptors are singletons; `ISearchIndex<T>` and per-document exclusive contracts
    are scoped. Types taking a raw `TOptions` constructor parameter are registered via a factory that unwraps
    `IOptions<TOptions>.Value` (`AddValidatedOptions` registers only `IOptions<T>`).
24. `ISearchIndexProvisioner`/`ISearchProviderDescriptor` are registered keyed by `SearchWellKnown.*ProviderName` and
    unkeyed resolving to the same instance. `ISearchIndex<T>` is never keyed; registering both providers for the same
    `TDocument` is forbidden (the last wins silently).
25. No `IHealthCheck`/`Microsoft.Extensions.Diagnostics.HealthChecks` here; readiness is the per-index
    `SearchIndexReadinessProbe`. A null or deep `PendingWriteCount` means stale, not unavailable — never unhealthy.
26. Field names come from `SearchWellKnown` or a per-document field-constants class used with `nameof` (SK0024). No
    raw `HttpClient` (SK0013). Never `NEST`/`Elasticsearch.Net` (SK0025).
27. No static mutable state in this domain's production code.
28. Telemetry never records query text, filter values or document ids.

## Decisions

| Decision | Why |
| --- | --- |
| Two providers: Meilisearch (BFF/fast) and ElasticSearch (analytics/heavy) as independent siblings | Different engines for different workloads; a shared base would couple swap-independence away |
| Field names are strings, not expressions; no `IQueryable` | An `IQueryable` promises completeness no engine delivers; refactor safety comes from field-constants classes + SK0024 |
| `IQueryBuilder` is non-generic | A `SearchRequest` is document-type-independent; a `TDocument` parameter would constrain nothing |
| `SearchCount` instead of `long` | Meilisearch has no count endpoint; its total is capped at `maxTotalHits`, so it must be qualified |
| ElasticSearch `MaxTotalHits` defaults to 1000 (as Meilisearch) and `EnsureIndexAsync` lowers `max_result_window` | Parity: a query legal on one provider is legal on the other. Raise per index with `.MaxTotalHits(n)` if never swapping |
| Synonyms are one-way only | Meilisearch has no two-way equivalence; asymmetry stays visible in the caller's configuration |
| ElasticSearch reads and writes through separate aliases; `CutoverAsync` flips the read alias in one aliases request | Rebuilds without downtime. ES auto-creates an index on write, so `VerifyRegisteredIndexesAsync` checks the write alias resolves and carries the fingerprint |
| Meilisearch cutover pre-creates an empty live index before the swap | `SwapIndexesAsync` requires both names to exist |
| `VerifyElasticSearchEngineVersionAsync` is an explicit async startup call | A check in the client DI factory would run at first resolve, block a thread and could only log |
| `.WithSourceSerializerContext` for ES | The ES client disables reflection-based STJ; omission warns at startup (9221) and fails with `SourceSerializerContextMissing` at first (de)serialization |
| Meilisearch `PooledConnectionLifetime` on the named client | The singleton client never returns its handler to the factory, so DNS would never refresh after a pod reschedule |
| Tenant tokens are short-lived (`TenantTokenMaxTtlMinutes`, default 15) and built only from `TenantScope` | Meilisearch tokens cannot be revoked; a hand-written rule dictionary is a silent cross-tenant leak |
| No ES document-level security equivalent | It is a commercial-tier feature; swapping Meilisearch-with-tokens → ES downgrades to application-enforced isolation (documented) |
| Both provider csproj files carry `<WarningsNotAsErrors>…;CS8509;CS8524</WarningsNotAsErrors>` instead of discard arms | C# cannot prove exhaustiveness over a sealed record hierarchy or an enum. The warnings stay visible; the runtime `SwitchExpressionException` is the backstop for a new node. A discard arm would hide the signal; `NoWarn`/pragmas are not used |
| `MeiliSearch` SDK accepted despite being non-AOT-safe (`netstandard2.0`, `dynamic` filter, `MakeGenericType` per search) | Contained behind `ISearchIndex<T>`; the ES client is AOT-annotated instead |
| Declined: neutral `Score`, nested filter node, neutral analyzers, string/bool ranges, capability flags | Each is silently wrong or meaningless on one engine |

## Logging

Block **9000–9999** (`LoggingEventIdRanges.Search`), always written as `LoggingEventIdRanges.Search + n`.

| Sub-block | Package | In use |
| --- | --- | --- |
| 9000–9099 | `.Abstractions` | reserved, unused — the package does not log |
| 9100–9199 | `.Meilisearch` (`Logging/MeilisearchLog.cs`) | 9100–9127 (9115 raw client access enabled) |
| 9200–9299 | `.ElasticSearch` (`Logging/ElasticSearchLog.cs`) | 9200–9229 (9216 raw client access enabled, 9221 source serializer context missing) |

Take the next free id in the package's sub-block. Telemetry: `ActivitySource`/`Meter` `SharedKernel.Search`
(`SearchWellKnown.ActivitySourceName`/`MeterName`) — span `search {operation}`; `search.client.operation.duration`
(s), `search.client.documents`; tags `search.index`, `search.operation`, `search.provider`, `error.type`.

## Cross-Domain Couplings

| Domain | Seam |
| --- | --- |
| `01.Core` | `Primitives` (`Result`, `Error`, `IReadinessProbe`, `LoggingEventIdRanges`); `Execution` (`TenantScope`, `TenantId`); `Configuration` (`AddValidatedOptions`) in the providers |
| `04.Contracts` | `ToPagedList()` → `PagedList<T>` (`TotalHits` → `TotalCount`, both `long`) |
| `13.ServiceDefaults` | `WithSearchTelemetry()` wires the source/meter by name with **no reference** to `09.Search` — its constant must stay byte-identical to `SearchWellKnown.ActivitySourceName`/`MeterName`; `AddSharedKernelReadiness()` maps the probes |
| `16.Testing` | `SharedKernel.Search.Testing` doubles `ISearchIndex<T>` (full `SearchFilter` evaluation) — any change to `ISearchIndex<T>` needs the matching change there |
| `00.Governance` | `SearchTopologyRules`; analyzers SK0013, SK0024, SK0025 |
| `10.Intelligence` | Shares the `TenantScope` convention and `IReadinessProbe` naming shape; no reference either way |
| `samples/CatalogApi` | End-to-end reference service |

## Testing

- Unit lane: `SharedKernel.Search.Abstractions.Tests` (incl. `ContractShapeTests` locking `EnumerateAsync`'s shape and
  the mandatory `SearchWriteConsistency`/`TenantScope` parameters), the three `consumer-verify` projects,
  `SharedKernel.Search.Testing.Tests`.
- Integration lane: `SharedKernel.Search.Meilisearch.Tests` and `SharedKernel.Search.ElasticSearch.Tests` on
  `MeilisearchContainerFixture` (`getmeili/meilisearch:v1.20.0`) and `ElasticsearchContainerFixture` (ES 9.4.2) from
  `SharedKernel.Testing.Internal`, shared per collection. Both run the same fixed-corpus conformance suite (range
  bounds, empty `All`/`Any`, single-value `In`, `Negate` nesting, string escaping, `DateTimeOffset` bounds,
  tenant-filtered facet counts) — that suite, not the interface, keeps the two filter compilers honest.
- Every rejection path asserts the `Error` **and** that no I/O happened: construct the index with `client: null!` (via
  `InternalsVisibleTo`) and pair it with a passing case that does hit the null client.
- Mocking is allowed only for engine status → `SearchErrors` mapping; behavioural coverage uses real engines.
- DI-only tests must register `ILogger<>` themselves (`NullLogger<>`); the provider extensions do not.
- Fakes: `SharedKernel.Search.Testing` — catalogue in `src/Testing/CLAUDE.md`.

## Known Limitations

- `SearchFilter.Any()` with zero operands: Meilisearch rejects it (`search.engine_fault`), ElasticSearch matches all.
  Guard against an empty operand list when portability matters.
- Meilisearch's task queue is global and sequential per instance: a bulk import on one index delays writes on every
  other index, and `PendingWriteCount` is instance-wide. Use separate instances, or pace imports with
  `SearchBulkWriteOptions.MaxBatchesPerSecond`.
- Meilisearch fails a whole write task (not one document) when a document lacks the primary key.
- Tenant search tokens scope search only, not writes, and cannot be revoked before expiry.
- ElasticSearch has no engine-enforced tenant isolation here; OSS deployments need per-tenant filtered aliases plus role
  privileges configured outside this package.
- Omitting `.WithSourceSerializerContext` on ElasticSearch fails at runtime, not build time.
- Options types use `public const string SectionName` rather than `ISectionBoundOptions`.
- No `00.Governance` rule asserts that no in-repo type consumes `IMeilisearchRawClientAccessor` /
  `IElasticSearchRawClientAccessor` (only the Temporal accessor has one).
