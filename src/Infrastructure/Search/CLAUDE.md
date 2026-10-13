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
| `SharedKernel.Search.Abstractions` | Abstractions | Neutral contracts, closed filter AST and scalar union, request/result/receipt models, `SearchIndexDefinition`, `SearchErrors`, `SearchErrorCodes`, `SearchWellKnown`, `SearchTagKeys`, `SearchOperations`, `SearchInstrumentNames`, `SearchIndexReadinessProbe`. References `SharedKernel.Primitives`, `SharedKernel.Execution` (`TenantScope`), `SharedKernel.Core` (the base of `SearchStreamException`) and `SharedKernel.Contracts` (`PagedList<T>`) only. Ships no DI extensions |
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
  `SearchStreamException`, `SearchWellKnown` (provider names, source and meter name, defaults), `SearchErrorCodes`,
  `SearchTagKeys`, `SearchOperations`, `SearchInstrumentNames`.

**`.Meilisearch`** — section `Search:Meilisearch` (`MeilisearchOptions.SectionName`):

```csharp
services.AddSharedKernelMeilisearchSearch(configuration)          // or (IConfigurationSection)
    .AddIndex<ProductSearchDocument>("products", i => i.PrimaryKey(…).TenantField(…).Field(…))
    .WithRankingRules("products", MeilisearchRankingRule.Words, …) // optional, replaces the engine default list
    .WithTenantTokens()                                            // ITenantSearchTokenIssuer; needs ApiKeyUid
    .AllowRawClientAccess()                                        // IMeilisearchRawClientAccessor; warns
    .Build();
```

Per index: scoped `ISearchIndex<T>`, `IInstantSearch<T>`. Codes: `MeilisearchErrorCodes` (the factory is internal).

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
(point-in-time + `search_after`). Codes: `ElasticSearchErrorCodes` (the factory is internal).

Each `Build()` registers one `SearchIndexReadinessProbe` per index, named `search-{provider}-{index}`
(`ProbeNameFor`). The host maps them with `AddSharedKernelReadiness()`; its `SharedKernel.*` telemetry wildcard (`AddServiceDefaults`, `13.ServiceDefaults`) subscribes to the
`SharedKernel.Search` source and meter with no further wiring.

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
12. All errors come from `SearchErrors` (codes in `SearchErrorCodes`), `MeilisearchErrors` or `ElasticSearchErrors` — no inline `Error`. An outage is
    `Error.Unavailable` (`search.unavailable`), a timeout `Error.Timeout` (`search.timeout`), the engine refusing the kernel's own key
    `Error.Unexpected` (`search.engine_unauthorized`) — identically on both providers even though Meilisearch's SDK throws and ElasticSearch's
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

**Error text, exceptions and observability**

29. `Error.Message` holds kernel-authored wording and bounded identifiers only
    (index, operation and provider names, an HTTP status, a bounded engine error code). Never provider debug
    information, a response body, a server `reason`, `ex.Message`, an endpoint or host, a cursor token, a query or a
    filter value. Engine detail reaches only the log, through the exception parameter. Logs never carry `Error.Message`,
    `ex.Message` or a URL, and every log on a caught-exception path passes the exception.
30. Classify-or-rethrow (as `07.Messaging`): only a recognised engine fault becomes a
    `Result` failure; an unrecognised exception (including a `SwitchExpressionException` from a translation switch)
    rethrows with a plain `throw;`. The caller's cancellation always propagates (rule 13); an HTTP-client timeout with the
    caller's token not cancelled is `search.timeout`. `CursorExpired` is returned only for the point-in-time-missing
    signal; any other cursor fault is classified like every other call.
31. `SearchStreamException` is a sealed `SharedKernelException` (and `IHasError`); `.Abstractions` therefore references
    `SharedKernel.Core` (a Foundation reference, still no third-party package). The ElasticSearch client wraps most
    exceptions it throws in an `UnexpectedTransportException`; the providers unwrap it before classifying and rethrow the
    client's exception as thrown.
32. Codes are public constants (`SearchErrorCodes`, `ElasticSearchErrorCodes`,
    `MeilisearchErrorCodes`), lowercase `search.{snake_reason}`; the `ErrorType` follows the condition (outage
    Unavailable `search.unavailable`, timeout Timeout, engine fault and the engine rejecting the kernel's own key
    Unexpected). `SearchErrors` stays public because three assemblies build it; the exclusive factories are internal.
    Messages are sentences ending in a period with quoted values, naming the remedy; an exception message names the nested
    error's code, never its text. The pinning tests enumerate every constant, so a new code fails until its `ErrorType` is asserted.
33. One `ActivitySource` and one `Meter` named `SharedKernel.Search`
    (`TelemetryVersion`, `Meter` from `IMeterFactory`); spans `search {operation}` (Client), instruments
    `sharedkernel.search.operation.duration` (`s`) and `sharedkernel.search.documents` (`{document}`); tags only from
    `SearchTagKeys` plus `outcome` and `error.type`; failures recorded only through the shared recorder; cancellation is
    never an error; operation names come from `SearchOperations`. A request refused before any I/O is still a recorded
    failure (it has its own span and duration); the readiness probe starts no span. Both providers create the same two
    instruments on the meter `IMeterFactory` returns for the name (the registrations call `AddMetrics()`), so they share one
    meter and are told apart by `search.provider`; instrument names are `SearchInstrumentNames`.
34. Each provider owns its EventId sub-block through an internal
    `{Package}LoggingEventIds` class, one plain static partial `{Package}Log` class under `Diagnostics/`, L3-shaped
    templates, `ILogger<KernelType>` categories and required loggers. No EventId is renumbered.

**Meilisearch transport**

35. The registered Meilisearch client is built over a named `IHttpClientFactory` client whose primary handler is the
    SDK's `MeilisearchMessageHandler` wrapping the pooled `SocketsHttpHandler`. The SDK's `(HttpClient, key)` constructor
    does not install that handler; without it a rejected POST (search, add, swap, create) is deserialised as the success
    payload and no failure is ever raised. Verified by `MeilisearchNamedClientErrorHandlingTests`.

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
| `.Abstractions` references `SharedKernel.Core` so `SearchStreamException` derives `SharedKernelException` | Edges map by `SharedKernelException`, so a non-deriving exception answers 500 and loses its `Error`. A Foundation reference is tier-legal and `Core` adds no third-party package; the earlier "must not reference Core" remark is superseded |
| Engine free text (even the server `reason`) never enters an `Error`; only a bounded engine code does | The server `reason` repeats user query text and field values; `DebugInformation` carries the whole request. The detail is not lost: it reaches the log through the exception |
| Unclassified exceptions rethrow instead of becoming `search.engine_fault` | A catch-all hid defects (an untranslatable filter node), cancellation and misconfiguration behind a soft failure; `engine_fault` stays for a recognised reply with an unknown code |
| `search.engine_unauthorized` is `Unexpected`, `search.write_rejected` stays `Unexpected`, `search.tenant_scope_missing` stays `Unauthorized` | The engine refusing the kernel's own key is a deployment defect, not the end user's authentication; without engine text the kernel cannot tell a caller's bad document from mapping drift; a missing tenant scope is a fail-closed isolation fault |
| `SearchErrors` remains public although root rule C2 says the factory is internal | Both providers and the double construct the same vocabulary from separate assemblies; an `InternalsVisibleTo` from Abstractions would name its siblings (rule 4). Flagged to `arch-lead` for a C2 amendment |
| No span for the readiness probe | Probes run on a host timer at high frequency and the health request is already a span |

## Logging

Block **9000–9999** (`LoggingEventIdRanges.Search`), always written as `LoggingEventIdRanges.Search + n`.

| Sub-block | Package | In use |
| --- | --- | --- |
| 9000–9099 | `.Abstractions` | reserved, unused — the package does not log |
| 9100–9199 | `.Meilisearch` (`Diagnostics/MeilisearchLog.cs`, ids in `MeilisearchLoggingEventIds`) | 9100–9129 (9115 raw client access enabled, 9128 tenant token issuance failed, 9129 cutover step failed) |
| 9200–9299 | `.ElasticSearch` (`Diagnostics/ElasticSearchLog.cs`, ids in `ElasticSearchLoggingEventIds`) | 9200–9230 (9216 raw client access enabled, 9221 source serializer context missing, 9230 cursor read faulted) |

Take the next free id in the package's sub-block (next free today: 9130 Meilisearch, 9231 ElasticSearch). Every log on a
caught-exception path passes the exception (last parameter); no template carries `{Reason}`, `{Url}`, `{Error}` or an
`Error.Message`, and a classified failure logs once at Warning. An unclassified exception that rethrows is not logged by
the provider. No id is renumbered; the sub-blocks stay as above. Templates are one sentence ending in a period with quoted
identifiers and the standard placeholder names (`{ElapsedMilliseconds}`, `{ErrorType}`, `{ErrorCode}`); the drift lines (9125, 9227)
log one line per index with a bounded `{DriftKind}` instead of free text.
Telemetry: `ActivitySource`/`Meter` `SharedKernel.Search` (`SearchWellKnown.ActivitySourceName`/`MeterName`) — span
`search {operation}` (Client; `issue_tenant_token` Internal); instruments `sharedkernel.search.operation.duration` (`s`) and
`sharedkernel.search.documents` (`{document}`); tags `search.index`, `search.operation`, `search.provider` (`SearchTagKeys`),
`outcome` and, on a failure, `error.type` (the `ErrorType` name or the exception full name).

## Cross-Domain Couplings

| Domain | Seam |
| --- | --- |
| `01.Core` | `Primitives` (`Result`, `Error`, `IReadinessProbe`, `LoggingEventIdRanges`); `Execution` (`TenantScope`, `TenantId`); `Configuration` (`AddValidatedOptions`) in the providers |
| `04.Contracts` | `ToPagedList()` → `PagedList<T>` (`TotalHits` → `TotalCount`, both `long`) |
| `13.ServiceDefaults` | `AddServiceDefaults` subscribes to `SharedKernel.*` by wildcard (and gives `SharedKernel.*` duration histograms the seconds view), so the source and meter need no per-capability wiring and `13.ServiceDefaults` has **no reference** to `09.Search`; `AddSharedKernelReadiness()` maps the probes |
| `16.Testing` | `SharedKernel.Search.Testing` doubles `ISearchIndex<T>` (full `SearchFilter` evaluation) — any change to `ISearchIndex<T>` needs the matching change there |
| `00.Governance` | `SearchTopologyRules`; analyzers SK0013, SK0024, SK0025 |
| `10.Intelligence` | Shares the `TenantScope` convention and `IReadinessProbe` naming shape; no reference either way |
| `samples/Shop/Catalog` | End-to-end reference service (the Shop's Catalog: both engines) |

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
- Error-safety suites run without Docker (they live in the Integration-lane projects): ElasticSearch tests put a
  scripted `HttpMessageHandler` beneath the real `HttpRequestInvoker` (`ScriptedRequestInvoker`, `EsFaultHarness`) and
  Meilisearch tests put one beneath the SDK's `MeilisearchMessageHandler` (`ScriptedHttpHandler`, `MeiliFaultHarness`), so
  status codes, transport failures, timeouts and cancellation take the shapes production sees. Each runs a sentinel
  matrix over every operation family (no sentinel, host or URL in any `Error`, `Details` or log message), a
  rethrow/telemetry case, an in-flight cancellation case per operation, and a log-shape reflection check.
- Observability conformance (same stubs, no Docker): `ActivityRecorder`/`MetricRecorder` assertions per operation family on both
  providers (span name and kind, only the three `search.*` tags plus `error.code`/`error.type`, failure, exception and
  cancellation shapes, instrument names, units and tag sets, a sentinel that must reach no tag); `*ErrorCodesTests` pin every
  code with its `ErrorType` and assert the same fault-class table on both providers; `*LoggingConformanceTests` run
  `LoggingEventIdIntegrityAssertion` over the real assembly and check ids, templates, parameter order and logger categories;
  `*BuilderMessageTests` pin the startup exception wording. Telemetry tests use a unique index name per test because the
  recorders listen process-wide. The test projects reference `00.Governance`'s `SharedKernel.ArchitectureTests` for the
  EventId assertion.
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
- Omitting `.WithSourceSerializerContext` on ElasticSearch fails at runtime, not build time, as
  `search.elasticsearch.source_serializer_context_missing` on the first (de)serialization of a document type the
  registered context does not cover.
- The Meilisearch SDK does not expose the HTTP status of an API error, so a gateway fault (502/503/504) answered with an
  empty or HTML body is `search.engine_fault` rather than `search.unavailable`; only transport-level failures
  (refused, DNS, timeout) are classified as an outage. Verified against 0.20.0.
- The ElasticSearch `EnumerateAsync`/`StreamAsync` iterators classify thrown faults only around the search call (an
  `await` inside an iterator cannot sit in a try block that yields); point-in-time open/close faults are responses.
- Options types use `public const string SectionName` rather than `ISectionBoundOptions`.
- No `00.Governance` rule asserts that no in-repo type consumes `IMeilisearchRawClientAccessor` /
  `IElasticSearchRawClientAccessor` (only the Temporal accessor has one).
