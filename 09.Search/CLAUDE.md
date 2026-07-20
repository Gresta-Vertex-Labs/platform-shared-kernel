# 09.Search — Full-Text Search Brain

## What This Domain Is

The full-text search abstraction and provider-wiring layer. Downstream microservices depend on `SharedKernel.Search.Abstractions` to provision indexes, upsert/delete/bulk-write documents, run BFF-shaped search (free text + scalar filters + field sort + facet counts + paging + highlighting), walk a corpus for reindex/export, perform an atomic staging→live cutover, and probe index readiness — never on a concrete engine SDK. Concrete provider packages (`SharedKernel.Search.Meilisearch`, `SharedKernel.Search.ElasticSearch`) wire the vendor client, translate the neutral models onto the engine's own query surface, and own all provider-specific configuration. Each provider additionally declares — **inside its own package, never in `.Abstractions`** — the typed contracts for the capabilities only that engine genuinely has.

Philosophy: **Intersection-only. Fail-loud. Typed escape at the package seam. No silent degradation.**

> `09.Search` may only reference `01.Core` and `04.Contracts`. It must never reference `03.Domain`, `05.Application`, `06.Persistence`, `07.Messaging`, `12.Security`, or any other capability domain. `SharedKernel.Search.Abstractions` contains **no type that either provider cannot implement completely and correctly** — if implementing a member would require one adapter to throw, degrade, approximate, or no-op, that member does not belong in `.Abstractions`. That single rule is the whole design and it is mechanically checkable in review.

---

## Packages

| Package | Role | References |
| --- | --- | --- |
| `SharedKernel.Search.Abstractions` | `ISearchDocument`, `ISearchIndex<TDocument>`, `ISearchIndexProvisioner`, `ISearchProviderDescriptor`, `IQueryBuilder<TDocument>` + the concrete `SearchQueryBuilder<TDocument>`, the closed 8-node `SearchFilter` AST, `SearchRequest`/`SearchResults<TDocument>`/`SearchHit<TDocument>`, `SearchIndexDefinition` + `SearchIndexDefinitionBuilder`, `SearchWellKnown`, `SearchErrors`, `SearchStreamException` — the only types application code should ever inject or construct. Ships **no** DI extension, **no** `ActivitySource`, **no** `[LoggerMessage]`, **no** `IHealthCheck` | `SharedKernel.Primitives` (01.Core), `SharedKernel.Contracts` (04.Contracts — for the guarded `ToPagedList()` bridge only) |
| `SharedKernel.Search.Meilisearch` *(BFF/fast)* | Concrete Meilisearch implementation of the three neutral contracts: `MeilisearchIndex<TDocument>`, `MeilisearchIndexProvisioner`, `MeilisearchProviderDescriptor`, `MeilisearchFilterCompiler`, `MeilisearchOptions`, `MeilisearchErrors`, `AddSharedKernelMeilisearchSearch()` DI extension. Additionally **declares** the Meilisearch-exclusive contracts `IInstantSearch<TDocument>`, `ITenantSearchTokenIssuer`, `IMeilisearchRawClientAccessor` | `SharedKernel.Search.Abstractions`, `SharedKernel.Primitives`, `SharedKernel.Configuration` (01.Core), `MeiliSearch`, `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Options`, `Microsoft.Extensions.Logging.Abstractions`, `Microsoft.Extensions.Http` |
| `SharedKernel.Search.ElasticSearch` *(analytics/heavy)* | Concrete ElasticSearch implementation of the same three neutral contracts: `ElasticSearchIndex<TDocument>`, `ElasticSearchIndexProvisioner`, `ElasticSearchProviderDescriptor`, `ElasticSearchFilterCompiler`, `ElasticSearchOptions`, `ElasticSearchErrors`, `AddSharedKernelElasticSearchSearch()` DI extension. Additionally **declares** the ElasticSearch-exclusive contracts `IAnalyticsSearch<TDocument>`, `ICursorSearch<TDocument>`, `IElasticSearchRawClientAccessor` | `SharedKernel.Search.Abstractions`, `SharedKernel.Primitives`, `SharedKernel.Configuration` (01.Core), `Elastic.Clients.Elasticsearch`, `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Options`, `Microsoft.Extensions.Logging.Abstractions` |

All packages target `net10.0`, `ImplicitUsings` enabled, `Nullable` enabled. Test sub-folders live inside each project folder (never in a top-level `tests/`). `SharedKernel.Search.Abstractions` has **zero `PackageReference` entries of any kind** — not even `Microsoft.Extensions.DependencyInjection.Abstractions`, because no DI extension lives there; `System.Security.Cryptography` (SHA-256 for `SearchIndexDefinition.Fingerprint`) and `System.Text.Json` are in-box on `net10.0` and add no dependency. The four `Microsoft.Extensions.*` packages on each provider package (`DependencyInjection.Abstractions`, `Options`, `Logging.Abstractions`, plus `Http` on `.Meilisearch` only) are pinned at **`10.0.9`** — confirmed at Scaffold-phase implementation time (2026-07-19) to match the version already pinned by the most recently-implemented sibling capability packages (`08.Storage`'s `Logging.Abstractions`, `11.Communication.Rest`'s `Http`, `16.Testing`'s `Http`/`Logging.Abstractions`) rather than floating to the newer `10.0.10` available on nuget.org at the same date — deliberate version-skew avoidance across the repo, not an oversight.

> **Provider role note:** `SharedKernel.Search.Meilisearch` and `SharedKernel.Search.ElasticSearch` are sibling `.{Provider}` packages, not a `.{Provider}.Core` / `.{Provider}.{Role}` split, and they must never reference each other. Shared implementation shape — the options-validation flow, the `SearchFilter` walker skeleton, receipt mapping, probe sequencing — is **duplicated deliberately**, mirroring the `08.Storage` `.S3`/`.Obs` precedent. A `SharedKernel.Search.Core` was considered and rejected: the `.{Provider}.Core` pattern exists for one technology serving multiple roles (`02.Caching`'s Redis five-package split), not two technologies serving one role. A Meilisearch filter-string emitter and an ElasticSearch `BoolQuery` builder share nothing beyond the `SearchFilter` walk shape, which already lives in `.Abstractions`.

---

## Technology Stack

| Concern | Technology |
| --- | --- |
| Search abstractions | Pure C# 13 interfaces + `sealed record` / `readonly record struct` models — zero third-party NuGet dependencies |
| Outcome type | `Result<T>` / `Result` / `Error` from `SharedKernel.Primitives` — expected failures (not-found, unauthorized, invalid request, undeclared field, pagination ceiling) are `Error` values, never thrown exceptions |
| Filter model | Closed 8-node `SearchFilter` AST (`Equal`/`NotEqual`/`In`/`Range`/`Exists`/`And`/`Or`/`Not`) over a closed five-kind `SearchValue` scalar union — no `object`, no `dynamic`, no expression trees, no `IQueryable` |
| Pagination model | 1-based `Page`/`PageSize` with a provider-declared `MaxTotalHits` ceiling validated client-side before any I/O; relevance-ordered deep paging is ElasticSearch-only via `ICursorSearch<TDocument>` (`search_after` + PIT), corpus walking is neutral via `ISearchIndex<TDocument>.EnumerateAsync` |
| Meilisearch provider | `MeiliSearch` pinned **`0.20.0`** (latest stable on nuget.org, published 2026-06-23, official Meilisearch org, MIT). Targets `netstandard2.0`; the GitHub README's ".NET Standard 2.1" claim is stale relative to its own csproj. Treated as a non-AOT-safe third party placed behind an abstraction |
| ElasticSearch provider | `Elastic.Clients.Elasticsearch` pinned **`9.4.2`** (latest stable on nuget.org, published 2026-06-01, Apache-2.0). Ships a real `net10.0` target and sets `<IsAotCompatible>true</IsAotCompatible>` for net8+. Requires an **ES 9.x or 10.x server** — a 9.x client does not support an 8.x server. Transitive `Elastic.Transport` resolves to `8.0.1` (latest stable on nuget.org, confirmed at Scaffold-phase implementation time) — verified against `Platform.SharedKernel.slnx`'s full project graph to be the **first and only** reference to `Elastic.Transport` anywhere in the solution, so there is no version conflict to resolve. `Elastic.Esql` is a separate, independent package (latest `0.12.0`) that `Elastic.Clients.Elasticsearch` 9.4.2 does **not** pull in transitively — it is out of scope for this domain and is not referenced |
| Prohibited legacy ES clients | `NEST` and `Elasticsearch.Net` — deprecated on nuget.org, last shipped `7.17.5` in October 2022, support window closed end-2025. Prohibited platform-wide; enforced by `SK0025` (`00.Governance`) |
| Configuration | Options-pattern via `SharedKernel.Configuration.AddValidatedOptions<TOptions>(IConfigurationSection)` — the single existing 01.Core overload, wiring `Bind` → `ValidateDataAnnotations` → `ValidateOnStart`; misconfiguration fails at `IHost.StartAsync()`, not at first query |
| DI composition | Per-provider `AddSharedKernelMeilisearchSearch()` / `AddSharedKernelElasticSearchSearch()` returning a fluent builder (`.AddIndex<TDocument>(...)`, `.Build()`); `SharedKernel.Search.Abstractions` ships no DI extension |
| Logging | `[LoggerMessage]` source-generated pattern with explicit `EventId`s, range **9000–9999** (`LoggingEventIdRanges.Search`, from `01.Core` — the registry constant already exists and equals `9000`); 100-wide sub-blocks per package in declaration order (Abstractions **9000–9099** *reserved and permanently unused*, Meilisearch **9100–9199**, ElasticSearch **9200–9299**) |
| Diagnostics | Each provider declares its **own** `internal static class SearchDiagnostics` holding an `ActivitySource` and `Meter` named from `SearchWellKnown.ActivitySourceName` / `SearchWellKnown.MeterName` (both `"SharedKernel.Search"`) — two instances, one byte-identical name. `13.ServiceDefaults` wires them string-name-only via `WithSearchTelemetry()` with no `ProjectReference` to `09.Search` |
| Testing containers | `Testcontainers.Elasticsearch` **`4.13.0`**, image pinned to **`docker.elastic.co/elasticsearch/elasticsearch:9.4.2`** (matching this domain's client version exactly — the module default `elasticsearch:8.6.1` is an unsupported pairing with a 9.x client). **There is no `Testcontainers.Meilisearch` package** — nuget.org returns 404 — so Meilisearch uses a hand-rolled fixture on the generic `ContainerBuilder`, image pinned to **`getmeili/meilisearch:v1.20.0`**. Both fixtures live in `16.Testing/SharedKernel.Testing/Containers/` (shipped in `16.Testing`'s `SK.16.Core`, P-275) and both images are CONFIRMED (`SK.09.Tests` real-backend session, 2026-07-20) to support this domain's pinned SDK versions end-to-end across all 26 `SK.09.Tests` tasks — the community `getmeili/meilisearch:v1.20.0` image's support for the full `MeiliSearch` `0.20.0` SDK surface, flagged as unconfirmed at Design time, is no longer an open question |
| Test mocking | `NSubstitute` `5.3.0` and `FluentAssertions` `8.4.0`, added directly by each `.Tests` project (never by a production package); mocking is confined to engine status-code error-mapping and DI-shape assertions — behavioural coverage runs against real containers |
| XML doc enforcement / NuGet packaging | `<GenerateDocumentationFile>true</GenerateDocumentationFile>` + `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` + a full NuGet metadata block (`PackageId`/`Version`/`Authors`/`Company`/`Product`/`Description`/`PackageTags`/`PackageLicenseExpression`/`RepositoryType`/`RepositoryUrl`/`PackageProjectUrl`/`Copyright`/`IncludeSymbols`/`SymbolPackageFormat=snupkg`) on all three production `.csproj` files. **`<PackageReadmeFile>README.md</PackageReadmeFile>` + `<None Include="README.md" Pack="true" PackagePath="\" />` must be added in the same edit** — `08.Storage` omitted this pair at Docs phase and paid for it with an `NU5039` pack warning at Published phase |

> **Why `Elastic.Clients.Elasticsearch` 9.4.2 (not NEST, not the 8.x maintenance line):** NEST is EOL — deprecated on nuget.org, feature-frozen since client 8.13, support window closed at end-2025 — so it is not a candidate. The remaining choice is the 8.19.x maintenance line versus 9.4.2. 9.4.2 is chosen because it ships a first-class `net10.0` target and an affirmative AOT position (`IsAotCompatible` for net8+, reflection-based STJ disabled by default, a documented `JsonSerializerContext` seam). The trade-off is real and must be respected: the 9.x client **does not support an 8.x server**, and Elastic explicitly states the client does not strictly follow semantic versioning — breaking changes can land in a minor or patch release. Consequently `ValidateEngineVersionOnStart` defaults to `true`, `SearchErrors.EngineVersionUnsupported` exists as a startup guard, and the version is pinned exactly rather than floated.

> **Why `MaxTotalHits` defaults to 1000 on *both* providers:** Meilisearch's `maxTotalHits` ceiling defaults to 1000 and hard-caps `limit + offset`; ElasticSearch's `index.max_result_window` defaults to 10 000. This design deliberately hobbles the stronger engine to the weaker engine's ceiling and has `EnsureIndexAsync` push `index.max_result_window` down to the same value, so a query proven legal on one provider is guaranteed legal on the other. The alternative — letting ElasticSearch accept queries Meilisearch would reject — converts every provider swap into a bug hunt whose failure surfaces at page 51 in production. The ceiling is per-index configurable via `SearchIndexDefinition.MaxTotalHits` for services that will never swap. This is the decision most likely to generate "the abstraction broke my search" friction, and it is taken deliberately.

---

## Interface Contracts

### `SharedKernel.Search.Abstractions` — public surface

> Zero third-party NuGet dependencies. References only `SharedKernel.Primitives` and `SharedKernel.Contracts`.
> Every operation returns `Result` / `Result<T>` — expected failures (not-found, unauthorized, undeclared field, pagination ceiling, tenant-scope missing) are `Error` values, never exceptions. The **only two** non-`Result` surfaces in the entire domain are the two streaming reads, which follow the established `06.Persistence` P-149 / `08.Storage` P-265 precedent.
> `CancellationToken cancellationToken = default` is always the trailing parameter, matching `IFileStorage` (08.Storage) — the closest structural sibling.

#### Document contract (`Abstractions/`)

```text
ISearchDocument
    .DocumentId                                                                → string { get; }
    NOTE: Every TDocument in this domain is constrained `where TDocument : class, ISearchDocument`.
          The document supplies its own key rather than the package discovering one by reflection or
          attribute scan — the AOT-preferred, SK0012-clean choice, and the same self-supplied-surface
          pattern the platform already uses for ILoggableRequest<TResponse>, ICacheableQuery.CacheKey,
          and IInvalidatesCache.CacheKeysToInvalidate. It feeds Meilisearch's primaryKey and
          ElasticSearch's _id from one string.

          CHARSET — THE STRICTEST ENGINE IS THE CONTRACT: DocumentId must match Meilisearch's
          constraint of A-Z, a-z, 0-9, '-' and '_' only. A violating id returns
          SearchErrors.InvalidDocumentId from the write path BEFORE any I/O, on BOTH providers.

          STABILITY: DocumentId must be stable and identical across rebuilds — it is the upsert key
          on both engines, so an unstable id silently produces duplicates instead of updates.

          PRIMITIVE MEMBERS ONLY (hard contract rule): an ISearchDocument implementation exposes
          primitive-typed members only (string, numeric, bool, DateTimeOffset, Guid, and collections
          thereof). The Meilisearch SDK declares its JsonSerializerOptions as `internal`, so there is
          NO seam to register a JsonSerializerContext or a custom converter — a StronglyTypedId<TValue>
          member round-trips on the ElasticSearch path and does NOT on the Meilisearch path. This
          asymmetry cannot be hidden by the abstraction, so it is forbidden at the document-type level
          instead. Enforcement is documentation + code review only; a downstream consumer's document
          types are outside this repo's architecture-test reach.
```

#### Search index (`Abstractions/`)

```text
ISearchIndex<TDocument>   where TDocument : class, ISearchDocument
    .IndexName                                                                 → string { get; }

    — write —
    .IndexAsync(TDocument document, SearchWriteConsistency consistency,
                CancellationToken ct)                                          → Task<Result<SearchWriteReceipt>>
    .IndexManyAsync(IReadOnlyCollection<TDocument> documents,
                    SearchWriteConsistency consistency, CancellationToken ct)  → Task<Result<SearchBulkReceipt>>
    .DeleteAsync(string documentId, SearchWriteConsistency consistency,
                 CancellationToken ct)                                         → Task<Result<SearchWriteReceipt>>
    .DeleteManyAsync(IReadOnlyCollection<string> documentIds,
                     SearchWriteConsistency consistency, CancellationToken ct) → Task<Result<SearchBulkReceipt>>
    .DeleteByFilterAsync(SearchFilter filter, TenantScope tenantScope,
                         SearchWriteConsistency consistency, CancellationToken ct)
                                                                               → Task<Result<SearchWriteReceipt>>
    .ClearAsync(SearchWriteConsistency consistency, CancellationToken ct)      → Task<Result<SearchWriteReceipt>>
    .WaitUntilSearchableAsync(SearchWriteReceipt receipt, TimeSpan timeout,
                              CancellationToken ct)                            → Task<Result>

    — read —
    .SearchAsync(SearchRequest request, TenantScope tenantScope,
                 CancellationToken ct)                                         → Task<Result<SearchResults<TDocument>>>
    .GetAsync(string documentId, TenantScope tenantScope, CancellationToken ct) → Task<Result<TDocument>>
    .CountAsync(SearchFilter? filter, TenantScope tenantScope,
                CancellationToken ct)                                          → Task<Result<long>>

    — corpus walk —
    .EnumerateAsync(SearchFilter? filter, TenantScope tenantScope, int batchSize,
                    [EnumeratorCancellation] CancellationToken ct)             → IAsyncEnumerable<TDocument>

    NOTE (WRITE CONSISTENCY IS REQUIRED AND NON-DEFAULTABLE — the single most important honesty
          decision on the write side): a bare `Task IndexAsync(doc, ct)` is dishonest on both engines,
          in two different ways. ElasticSearch returns once the document is durable in the translog
          but NOT searchable until the next refresh (index.refresh_interval, default 1s). Meilisearch
          returns only a taskUid in state `enqueued`, processed by a single GLOBAL sequential queue —
          under load the delay to searchability is effectively unbounded, and a DIFFERENT index's
          backlog delays yours.
            Accepted   → ES refresh=false;    Meilisearch returns the enqueued taskUid.
            Searchable → ES refresh=wait_for; Meilisearch polls the task to `succeeded`, bounded by
                         MeilisearchOptions.TaskWaitTimeoutSeconds.
          ES refresh=true is NEVER exposed — it forces an immediate cluster-wide refresh that disturbs
          other in-flight requests and has no Meilisearch analogue. There is deliberately no nullable
          SearchWriteOptions parameter: an enum value is one token, self-documenting, and strictly
          better than typing `null`. Per-call timeout tuning is reached through WaitUntilSearchableAsync.

    NOTE (UPSERT ONLY): IndexAsync is an upsert keyed on ISearchDocument.DocumentId. There is no
          Create-vs-Update split, because Meilisearch has no create-if-absent primitive and because
          at-least-once delivery from any upstream sync pipeline makes the distinction meaningless.

    NOTE (NO OPTIMISTIC CONCURRENCY, AND NO FAKE): ElasticSearch has external versioning and
          if_seq_no/if_primary_term; Meilisearch has nothing comparable and is last-write-by-arrival-
          order. Rather than carry a Version member that one adapter silently ignores, this contract
          carries NONE and states the requirement instead: a sync pipeline feeding these methods MUST
          guarantee ordering upstream by partitioning the change stream on DocumentId. Documented, not
          enforced, and the domain's sharpest sharp edge.

    NOTE (BULK PARTIAL FAILURE IS NOT COLLAPSED): IndexManyAsync/DeleteManyAsync return
          Result<SearchBulkReceipt>, and the Result is SUCCESS even when Failures is non-empty.
          Partial failure is normal operation in an ES _bulk response; the per-item errors are the
          only actionable output. Result.Failure is reserved for "the request itself did not execute".
          Callers requiring all-or-nothing check SearchBulkReceipt.HasFailures.

    NOTE (DeleteByFilterAsync TAKES TenantScope AS A MANDATORY SEPARATE PARAMETER): it is never part
          of the SearchFilter tree. A dropped tenant clause on a DELETE is cross-tenant data
          destruction.

    NOTE (WaitUntilSearchableAsync): the deferred barrier for callers who issued Accepted writes — an
          integration test, or a bulk import wanting one barrier after N batches instead of N barriers.
          Meilisearch maps it to WaitForTaskAsync(receipt.ProviderToken); ElasticSearch to a targeted
          refresh-poll. Returns SearchErrors.WriteTimeout on expiry. It MUST NOT be used to implement
          read-your-writes on a request path — solve that at the BFF by reading the entity from the
          source of truth, or optimistically merging it into the result set.

    NOTE (FAIL LOUD, NEVER DEGRADE): every request is validated against the registered
          SearchIndexDefinition BEFORE any I/O. A filter on an undeclared field returns
          SearchErrors.FieldNotFilterable; a sort returns FieldNotSortable; a facet returns
          FieldNotFacetable; an over-ceiling page returns PaginationLimitExceeded. The adapter MUST
          NOT drop the clause, coerce it to a text match, or post-filter in memory. The ElasticSearch
          adapter thereby enforces restrictions ElasticSearch itself does not have — deliberate, see
          the MaxTotalHits blockquote in Technology Stack.

    NOTE (FACETING IS NOT A SEPARATE INTERFACE): SearchRequest.Facets / .NumericFacetStats are plain
          members on the request the caller already built, and the counts come back on the
          SearchResults<TDocument> the caller already has. The common case — index an aggregate, then
          run a filtered + faceted + paged search — therefore costs exactly ONE injected dependency
          and ZERO extra types.

    NOTE (GetAsync IS TENANT-CHECKED): a get-by-id without a tenant predicate lets a caller read any
          tenant's document by guessing an id. The adapter implements it as a filtered single-hit
          search, not a raw get, whenever the index definition declares a TenantField. A missing
          document returns SearchErrors.DocumentNotFound — never null, never a thrown exception.

    NOTE (CountAsync IS EXACT ON BOTH ENGINES): implemented as ES _count and as a Meilisearch
          page/hitsPerPage search reading the exact totalHits. It exists separately from
          SearchResults.TotalHits precisely because that value carries an accuracy qualifier and this
          one does not.

    NOTE (EnumerateAsync IS NOT Result-WRAPPED — the one documented exception in this domain): follows
          the established streaming precedent, 06.Persistence P-149 and 08.Storage P-265. A transport
          failure mid-stream surfaces as SearchStreamException from MoveNextAsync.
          Result<IAsyncEnumerable<T>> only reports the failure that happens before the first
          MoveNext, and IAsyncEnumerable<Result<T>> is unusable at the call site. Do not "fix" this by
          wrapping it in Result — that would diverge from the platform precedent instead of following it.

    NOTE (EnumerateAsync IS A DOCUMENT WALK, NOT A DEEP SEARCH): implemented over Meilisearch's
          GET /indexes/{uid}/documents offset+limit endpoint and over ElasticSearch's search_after +
          PIT. Because it does not go through Meilisearch's search endpoint it is NOT subject to the
          maxTotalHits ceiling — which is why "iterate every matching document" is portable here while
          relevance-ordered deep pagination is not. It is for reindex, export, and reconciliation.

    NOTE (EnumerateAsync ORDERING IS UNSPECIFIED — weakened deliberately, contract unchanged despite a
          confirmed finding): the enumeration is stable within a single unmodified corpus, and ordering
          MUST NOT be relied upon. VERIFIED against real containers (T-26, 2026-07-20): both engines
          currently yield in INSERTION order, NOT document-id-ascending order — proven by seeding a
          batch with ids deliberately out of numeric order (e.g. prod-010, prod-003, prod-007, ...) and
          observing the walk return them in SEED order on both Meilisearch's /documents (offset+limit)
          and ElasticSearch's search_after+PIT walk. This is recorded as a confirmed CURRENT-BEHAVIOUR
          finding, not a promoted contractual guarantee — insertion order is an observed implementation
          detail of each engine's own storage/iteration model, not a documented API promise on either
          engine, so a future engine version could change it without notice. The contract's "unspecified,
          do not rely on it" language is therefore deliberately RETAINED rather than strengthened to
          "insertion order" — consumers needing a stable resumable order should use
          ICursorSearch<TDocument> (ElasticSearch-only, ordered by search_after) instead.
```

#### Index provisioning (`Abstractions/`)

```text
ISearchIndexProvisioner   (non-generic — exactly one registration per provider)
    .EnsureIndexAsync(SearchIndexDefinition definition, CancellationToken ct)  → Task<Result>
    .IndexExistsAsync(string indexName, CancellationToken ct)                  → Task<Result<bool>>
    .DeleteIndexAsync(string indexName, CancellationToken ct)                  → Task<Result>
    .CutoverAsync(IndexCutoverRequest request, CancellationToken ct)           → Task<Result>
    .ProbeAsync(string indexName, CancellationToken ct)                        → Task<Result<SearchIndexHealth>>

    NOTE (WHY INDEX FIELD DECLARATIONS ARE ON THE NEUTRAL SURFACE AT ALL — the one place the "thin
          abstraction" premise is compromised, and it is unavoidable): Meilisearch REJECTS a filter on
          any attribute absent from filterableAttributes and a sort on any attribute absent from
          sortableAttributes; both are index SETTINGS applied ahead of time. ElasticSearch filters any
          mapped field at query time. If field roles were a provider-private detail, the identical
          SearchRequest would succeed on ES and 400 on Meilisearch. SearchIndexDefinition is the
          neutral declaration that makes a neutral query legal on both engines.

    NOTE (EnsureIndexAsync IS IDEMPOTENT AND ADDITIVE-ONLY): it creates the index if absent and applies
          the field declarations. It NEVER drops a field and NEVER rewrites an incompatible mapping —
          ElasticSearch cannot change an existing field's type in place, so a truly convergent "make it
          match" is unimplementable on one engine. A definition conflicting with the live mapping
          returns SearchErrors.IndexDefinitionConflict; the remedy is staging → bulk-load →
          CutoverAsync. It also writes the definition's SchemaFingerprint into index metadata (ES:
          index meta; Meilisearch: a reserved settings entry) so ProbeAsync can detect drift.

    NOTE (CutoverAsync — one neutral name over two mechanics, with the asymmetry normalised rather
          than hidden): ElasticSearch issues a SINGLE _aliases request containing both the remove and
          the add, applied atomically, so the read alias is never undefined — LiveIndexName is
          therefore an ALIAS. Meilisearch has no aliases and calls POST /swap-indexes, which atomically
          swaps documents, settings AND task history — LiveIndexName is a real index name, and after
          the swap the STAGING name still exists holding the OLD data.
          IndexCutoverRequest.DeleteStagingAfterCutover (default true) exists precisely to normalise
          that; without it, every Meilisearch rebuild silently doubles storage.

    NOTE (WHAT THIS DOES NOT OWN): the rebuild itself. There is no IIndexRebuilder driving a data
          source, because the aggregate → search-document mapping is business logic belonging to the
          owning service, and a rebuild source would force a 06.Persistence or 07.Messaging reference
          this layer may not take. The consumer sequences four calls itself:
          EnsureIndexAsync(staging) → IndexManyAsync(from its own IAsyncEnumerable) → CutoverAsync →
          (DeleteIndexAsync if it opted out of automatic staging cleanup).

    NOTE (ProbeAsync IS A PRIMITIVE, NOT A HEALTH CHECK): 09.Search ships NO IHealthCheck
          implementation and neither provider references Microsoft.Extensions.Diagnostics.HealthChecks.
          Wiring into AddHealthChecks() is 13.ServiceDefaults's concern, mirroring the 06.Persistence
          DB-readiness split and 08.Storage's AddStorageReadinessCheck exactly. That adapter resolves
          only ISearchIndexProvisioner and takes indexName as an explicit caller-supplied parameter —
          never a concrete MeilisearchOptions/ElasticSearchOptions — which is what lets one adapter
          work unmodified against either provider.
```

#### Provider descriptor (`Abstractions/`)

```text
ISearchProviderDescriptor   (singleton, zero I/O)
    .ProviderName                                                              → string { get; }
    .MaxTotalHits                                                              → int { get; }
    .MaxFacetValues                                                            → int { get; }
    .RegisteredIndexes                                            → IReadOnlyList<string> { get; }
    .Validate(string indexName, SearchRequest request)                         → Result

    NOTE: ProviderName is SearchWellKnown.MeilisearchProviderName or
          SearchWellKnown.ElasticSearchProviderName, and is also the value of the OTel
          `search.provider` tag.

    NOTE (THIS TYPE CARRIES NO CAPABILITY-FLAGS ENUM, DELIBERATELY): an `if (caps.HasFlag(...))` branch
          at an application call site is a platform violation — degradation in search is silent
          wrongness, not a downgraded UX. An aggregation that falls back to facet counts returns a
          WRONG NUMBER with a confident type; a filter that falls back to unfiltered returns ANOTHER
          TENANT'S DATA. The consumer-facing capability mechanism is the COMPILE ERROR a provider swap
          produces against the provider-package-declared contracts, never a runtime flag check. The
          four members retained here each have a concrete, non-branching consumer.

    NOTE (MaxTotalHits IS A READABLE PROPERTY, not only a failure): a BFF caps its pager UI at
          ceil(MaxTotalHits / pageSize) up front instead of discovering the ceiling as a
          PaginationLimitExceeded on page 51. Both providers default it to 1000.

    NOTE (Validate IS THE ZERO-I/O, ZERO-CONTAINER PRE-FLIGHT): it runs exactly the same legality
          checks the executor runs — pagination ceiling, field filterability/sortability/facetability
          against the registered SearchIndexDefinition, facet-count cap, structural request invariants
          — without touching the network. This is what makes "fail at composition time, not query time"
          actionable rather than aspirational: a consuming service asserts at startup, or in a plain
          unit test with no Docker, that every SearchRequest shape it constructs is legal on the
          configured provider. It is the single cheapest way to prove a provider swap is safe before
          deploying it.
```

#### Query builder (`Querying/`)

```text
IQueryBuilder<TDocument>   where TDocument : class, ISearchDocument
    .Matching(string? freeText)                                                → IQueryBuilder<TDocument>
    .MatchAllTerms(bool matchAll)                                              → IQueryBuilder<TDocument>
    .SearchingIn(params string[] fields)                                       → IQueryBuilder<TDocument>
    .Where(SearchFilter filter)                                                → IQueryBuilder<TDocument>
    .OrderBy(string field)                                                     → IQueryBuilder<TDocument>
    .OrderByDescending(string field)                                           → IQueryBuilder<TDocument>
    .Page(int page, int pageSize)                                              → IQueryBuilder<TDocument>
    .RequireExactTotalHits()                                                   → IQueryBuilder<TDocument>
    .Faceting(params string[] facetFields)                                     → IQueryBuilder<TDocument>
    .WithNumericFacetStats(params string[] facetFields)                        → IQueryBuilder<TDocument>
    .Highlighting(HighlightRequest highlight)                                  → IQueryBuilder<TDocument>
    .Returning(params string[] fields)                                         → IQueryBuilder<TDocument>
    .Build()                                                                   → Result<SearchRequest>

SearchQuery   (static entry point)
    .For<TDocument>()                                                          → IQueryBuilder<TDocument>
    where TDocument : class, ISearchDocument

    NOTE (ROOT-BRAIN FIDELITY): the root brain names ISearchIndex and IQueryBuilder. BOTH names survive
          verbatim, generic-ised. The only deviation is that IQueryBuilder<TDocument> is NOT a
          DI-registered service and has exactly one provider-free implementation, the sealed
          SearchQueryBuilder<TDocument> shipped in .Abstractions. A per-provider builder resolved from
          DI was considered and rejected: it would let application code construct a query shaped by the
          engine it happens to run against, reintroducing at the call site the exact coupling the
          abstraction exists to remove — and the coupling would be invisible until a swap.

    NOTE (IMMUTABLE — enforced, not merely asserted): every method returns a NEW
          SearchQueryBuilder<TDocument> instance. A partially-built query may be safely shared, cached,
          fanned out, or used as a template. The implementation holds only readonly fields; there is no
          mutable accumulation and no thread-safety caveat.

    NOTE (REPEATED Where(...) CALLS AND TOGETHER — the single most important ergonomic decision here):
          the alternative, last-call-wins, is exactly the silent-clause-dropping defect class that leaks
          tenant data, and there is a documented sibling defect in the ElasticSearch .NET client itself
          (issue #8471, chained SortOptionsDescriptor<T> silently keeping only the last field). For OR,
          compose explicitly: Where(SearchFilter.Any(a, b)).

    NOTE (Page IS 1-BASED), matching PagedList<T>.Page — which is why ToPagedList() needs no
          offset-alignment guard.

    NOTE (Build VALIDATES ONLY PROVIDER-INDEPENDENT INVARIANTS): page < 1, pageSize < 1,
          pageSize > SearchWellKnown.MaxPageSize, empty field name, duplicate sort field, empty
          highlight field list → SearchErrors.InvalidSearchRequest. It CANNOT validate filterability,
          sortability, or the pagination ceiling — those are engine-configuration facts, checked by
          ISearchProviderDescriptor.Validate and, ultimately, by the executor.

    NOTE (BOTH CONSTRUCTION PATHS STAY LEGAL): the builder is optional sugar. SearchRequest is a plain
          sealed record with public init members, sensible defaults, and a static Default, so
          `SearchRequest.Default with { FreeText = "x", Facets = ["status"] }` is fully supported. The
          executor validates EVERY request with the same validator the builder uses, so a
          hand-constructed invalid request fails with SearchErrors.InvalidSearchRequest before any I/O.

    NOTE (NO EXPRESSION TREES, NO IQueryable): fields are strings, not
          Expression<Func<TDocument, object>>. An IQueryable surface promises completeness no search
          engine delivers and lands its failures at runtime (the Sitecore LINQ-to-Sitecore model);
          09.Search also may not reference 03.Domain's specification machinery. Refactor safety comes
          from the mandatory per-document field-constants class plus nameof() — see SK0024 under
          Implementation Rules.
```

#### Filter AST and scalar union (`Models/`)

```text
SearchValue   (readonly record struct — a CLOSED scalar union, no `object`, no `dynamic`)
    .Kind                                                             → SearchValueKind { get; }
    .AsString / .AsInt64 / .AsDouble / .AsBoolean / .AsDateTimeOffset  (kind-checked accessors)
    .From(string) / .From(long) / .From(double) / .From(bool) / .From(DateTimeOffset)  → SearchValue
    .From(Guid value)                                                          → SearchValue
    — implicit operators from string, int, long, double, bool, DateTimeOffset, Guid

SearchValueKind   (enum)
    String = 0, Int64 = 1, Double = 2, Boolean = 3, DateTimeOffset = 4

    NOTE: Meilisearch's SDK types its filter property as `dynamic`, which pulls in Microsoft.CSharp
          binder machinery, is hostile to trimming and AOT, and is unvalidated at compile time. A
          closed five-kind struct union is the direct antidote and gives each translator a TOTAL switch.
          Reading the wrong accessor for the current Kind throws InvalidOperationException — an adapter
          programming error, never a consumer-facing expected failure.
          .From(Guid) normalises to the canonical "D" string form — SK0011-safe and identical on both
          engines.

    NOTE (ToString() IS OVERRIDDEN — VERIFIED Tests-phase, do not remove): a plain `readonly record
          struct` with no positional primary constructor synthesizes ToString()/PrintMembers to print
          EVERY public property unconditionally, including the kind-checked As* accessors — which means
          the compiler-generated ToString() on SearchValue would ALWAYS throw InvalidOperationException
          (at most one accessor matches the held Kind). SearchValue therefore declares an explicit,
          Kind-aware `public override string ToString()` that never throws. This was a real Core-phase
          defect, found via a FluentAssertions failure-message crash while writing SK.09.Tests, not a
          hypothetical: any record containing a SearchValue (EqualFilter, RangeFilter, InFilter) inherits
          the same crash on ToString() without this override.

    NOTE (TIMESTAMP ENCODING): DateTimeOffset serialises to Unix epoch SECONDS for Meilisearch (whose
          filter DSL compares numerics, not ISO-8601 strings) and to strict ISO-8601 for ElasticSearch
          date fields. Each provider does this in its own translator; the divergence is invisible to
          the caller but MUST be matched by the document mapping — a Meilisearch document stores such a
          field as a number.

SearchFilter   (abstract record, CLOSED hierarchy — private protected base ctor; the eight sealed
                subtypes are PUBLIC with INTERNAL constructors and public get-only properties)
    EqualFilter      { Field: string, Value: SearchValue }
    NotEqualFilter   { Field: string, Value: SearchValue }
    InFilter         { Field: string, Values: IReadOnlyList<SearchValue> }
    RangeFilter      { Field: string, From: SearchValue?, FromInclusive: bool,
                       To: SearchValue?, ToInclusive: bool }
    ExistsFilter     { Field: string }
    AndFilter        { Operands: IReadOnlyList<SearchFilter> }
    OrFilter         { Operands: IReadOnlyList<SearchFilter> }
    NotFilter        { Operand: SearchFilter }

    — the ONLY sanctioned construction path, static factories on the base —
    .Eq(string field, SearchValue value)                                       → SearchFilter
    .Ne(string field, SearchValue value)                                       → SearchFilter
    .In(string field, params SearchValue[] values)                             → SearchFilter
    .Between(string field, SearchValue? from, SearchValue? to,
             bool fromInclusive = true, bool toInclusive = true)               → SearchFilter
    .Exists(string field)                                                      → SearchFilter
    .All(params SearchFilter[] operands)                                       → SearchFilter
    .Any(params SearchFilter[] operands)                                       → SearchFilter
    .Negate(SearchFilter operand)                                              → SearchFilter

    NOTE (CLOSED BY CONSTRUCTION, NOT BY CONVENTION): the base has a private protected constructor and
          every subtype's constructor is INTERNAL, so no assembly outside .Abstractions can build a
          node — but the TYPES are public, so both providers can pattern-match and read them. This is
          what makes each translator an exhaustive C# switch with NO default arm and structurally zero
          NotSupportedException path. HARD RULE: neither provider's translation switch may carry a
          discard (`_ =>`) arm. Compiler exhaustiveness plus the platform-wide TreatWarningsAsErrors is
          the ONLY thing preventing a future ninth node from silently dropping on the lagging adapter.

    NOTE (Between REJECTS String AND Boolean BOUNDS): ElasticSearch supports lexicographic ranges on
          keyword fields; Meilisearch's TO operator works on numbers only. A string range is therefore a
          core-surface member that works on one provider and fails on the other — the disqualifying
          pattern. Between throws ArgumentException for a String or Boolean SearchValue (a programming
          error caught at first test run, mirroring TenantScope.Of and PagedList<T>.Create). Only Int64,
          Double, and DateTimeOffset are legal range bounds.

    NOTE (WHAT IS DELIBERATELY ABSENT, and why each): no Fuzzy/TypoTolerance node (Meilisearch applies
          typo tolerance by default with word-length thresholds, ES requires explicit fuzziness with
          different edit-distance behaviour and real cost — a boolean that is a no-op on one side and a
          query-plan change on the other). No Boost (per-query in ES, per-index-settings in Meilisearch;
          "boost title 3x for this one query" is NOT EXPRESSIBLE in Meilisearch at all). No IsNull /
          IsEmpty (IS EMPTY has no faithful ES equivalent; IS NULL conflates null-value with
          field-absent against must_not exists). No GeoRadius (present on both but with divergent
          distance semantics and unit handling; deferred to a later phase gated on real container
          verification, not guessed at now). No Prefix/Wildcard/Regex (analysis-time concerns expressed
          through tokenisation). No raw-string escape clause. No Nested/HasChild — see next NOTE.

    NOTE (NO NESTED / OBJECT-ARRAY FILTER — rejected as a CORRECTNESS hazard, not a feature gap):
          ElasticSearch's `nested` mapping preserves intra-element field correlation; Meilisearch
          flattens. Filtering size == "M" AND colour == "red" over variants: [{M, blue}, {L, red}]
          MATCHES on Meilisearch and does NOT match on ES-with-nested. A neutral node would hide a
          silent wrong-answer divergence. The MANDATED portable technique is to flatten at
          document-mapping time into a precomputed composite filterable field
          (variant_size_colour: ["M|blue", "L|red"]) and filter it with In(...) — exact and identical on
          both engines.

    NOTE (FREE TEXT IS NOT IN THE TREE): SearchFilter carries only structured predicates. Nesting a
          full-text match inside a filtered sub-expression is expressible in an ES bool tree and is not
          expressible in Meilisearch's filter DSL. Free text lives on SearchRequest.FreeText and nowhere
          else.
```

#### Request / result models (`Models/`)

```text
TenantScope   (readonly record struct)
    .Value                                                                     → string { get; }
    .None                                                        → static TenantScope { get; }
    .Of(string value)                                                          → TenantScope

    NOTE: Value is the tenant discriminator VALUE. The FIELD name lives on
          SearchIndexDefinition.TenantField, never here and never caller-supplied at query time.
          None is the explicit single-tenant/global-index sentinel (Value == string.Empty).
          Of throws ArgumentException on null/whitespace — a programming error, not an expected failure.

    NOTE (MANDATORY, NON-NULLABLE, NON-DEFAULTED — the highest-severity decision in the domain):
          TenantScope is a separate method parameter on every read and every filtered write, not a
          member of SearchRequest. Two reasons, both structural. First, a tenant predicate travelling
          through the same filter tree as business predicates can be dropped by a translation bug; a
          dropped business clause is a bug, a dropped tenant clause is a cross-tenant data leak. Second,
          a member on a request object can be lost when that object crosses a layer boundary; a required
          parameter cannot. Adapters inject it as the OUTERMOST AND clause after translating the
          caller's filter, so no translation path can omit it.

    NOTE (FAIL CLOSED, DRIVEN BY THE INDEX DEFINITION — not by a builder flag): if the registered
          SearchIndexDefinition declares a TenantField and the caller passes TenantScope.None, the
          provider returns SearchErrors.TenantScopeMissing and performs NO I/O. A separate
          .RequireTenantScope() opt-in was considered and rejected: a flag can be forgotten, whereas the
          TenantField declaration cannot — it is required to make the field filterable at all, so the
          guard arms itself automatically the moment an index is genuinely multi-tenant.

SortDirection   (enum)
    Ascending = 0, Descending = 1

SearchSort   (readonly record struct)
    .Field                                                                     → string { get; }
    .Direction                                                          → SortDirection { get; }
    .Ascending(string field)                                                   → SearchSort
    .Descending(string field)                                                  → SearchSort

    NOTE (DELIBERATE DUPLICATION): 03.Domain models ordering too. 09.Search may reference only 01.Core
          and 04.Contracts, so PagedSpecification<T> / ReadOnlySpecification<T> and 03.Domain's ordering
          API are behind a layering wall. Do NOT "fix" this by adding a 03.Domain reference.

    NOTE (NO RELEVANCE SORT): there is no SortDirection.Relevance and no ScoreSort. An EMPTY Sort
          collection IS relevance order on both engines. Naming it as an option would imply the two
          relevance models are comparable across a swap. They are not.

HighlightRequest   (sealed record)
    .Fields                                                    → IReadOnlyList<string> { get; init; }
    .PreTag                            → string { get; init; }   (default SearchWellKnown "<em>")
    .PostTag                           → string { get; init; }   (default SearchWellKnown "</em>")
    .FragmentSize                      → int?   { get; init; }   (default null → engine default)
    .MaxFragments                      → int?   { get; init; }   (default null → engine default)

    NOTE: FragmentSize/MaxFragments map to ES fragment_size/number_of_fragments and to Meilisearch's
          cropLength/attributesToCrop. The mapping is APPROXIMATE — Meilisearch crops in words, ES in
          characters — so identical numbers produce visibly different fragments across a swap.
          Documented, not normalised, because normalising would require guessing an average word length.
          On Meilisearch, highlighted values arrive in a per-hit `_formatted` object; the provider reads
          it and projects into SearchHit.Highlights, so a caller's document POCO does NOT need a
          _formatted member. On ES the same projection comes from hit.highlight. Both land in an
          identical IReadOnlyDictionary<string, IReadOnlyList<string>>.

SearchRequest   (sealed record — public init members, sensible defaults, static Default)
    .FreeText                          → string? { get; init; }                    (default null)
    .MatchAllTerms                     → bool    { get; init; }                    (default false)
    .SearchFields                      → IReadOnlyList<string> { get; init; }      (default [] = all searchable)
    .Filter                            → SearchFilter? { get; init; }              (default null)
    .Sort                              → IReadOnlyList<SearchSort> { get; init; }  (default [])
    .Page                              → int     { get; init; }        (1-BASED, default 1)
    .PageSize                          → int     { get; init; }        (default SearchWellKnown.DefaultPageSize)
    .Facets                            → IReadOnlyList<string> { get; init; }      (default [])
    .NumericFacetStats                 → IReadOnlyList<string> { get; init; }      (default [])
    .Highlight                         → HighlightRequest? { get; init; }          (default null)
    .ReturnFields                      → IReadOnlyList<string> { get; init; }      (default [] = all retrievable)
    .RequireExactTotalHits             → bool    { get; init; }                    (default false)

    NOTE (NO TenantScope MEMBER — by design). See TenantScope.

    NOTE (RequireExactTotalHits — the honest exposure of a real, unavoidable cost/accuracy trade-off
          both engines have): false → Meilisearch uses offset/limit and reports estimatedTotalHits
          (Accuracy = Estimated), ES leaves track_total_hits at its 10 000 default (Accuracy = Exact
          when relation is eq, LowerBound when gte). true → Meilisearch uses page/hitsPerPage for an
          exact totalHits, ES sets track_total_hits (Accuracy = Exact); both cost materially more. SET
          THIS WHEN YOU INTEND TO CALL ToPagedList() — SearchErrors.TotalHitsNotExact names this member
          explicitly as the remedy.

    NOTE (NO UNBOUNDED SKIP, 1-BASED PAGE/SIZE ONLY): the neutral page model is Page/PageSize with a
          provider-declared ceiling, validated client-side as Page * PageSize <= MaxTotalHits before any
          I/O.

    NOTE (NO Boost, NO ScoreThreshold, NO MinimumShouldMatch, NO Fuzziness, NO Aggregations, NO
          NestedPath, NO ScriptSort): each is a capability the two engines cannot both express.

TotalHitsAccuracy   (enum)
    Exact = 0        // ES hits.total.relation == "eq"; Meilisearch page/hitsPerPage totalHits
    LowerBound = 1   // ES hits.total.relation == "gte" (track_total_hits ceiling reached)
    Estimated = 2    // Meilisearch offset/limit estimatedTotalHits

    NOTE (THREE VALUES, NOT TWO): labelling Meilisearch's estimatedTotalHits as LowerBound would itself
          be a small lie — the estimate can be over OR under, so it is neither exact nor a bound.
          Estimated is its own value.

SearchHit<TDocument>   (sealed record)  where TDocument : class, ISearchDocument
    .Document                                                              → TDocument { get; init; }
    .Rank                                                                        → int { get; init; }
    .Highlights                        → IReadOnlyDictionary<string, IReadOnlyList<string>> { get; init; }

    NOTE (THERE IS NO Score — the design's loudest single omission): Meilisearch bucket-sorts through
          ordered ranking rules; ElasticSearch computes BM25. The numbers share no scale, no range, and
          no monotonicity guarantee. Sitecore's own documentation warns of scoring divergence between
          Lucene and Solr, which SHARE a scoring lineage; these two do not even share that. Exposing a
          Score invites thresholding, cross-provider comparison, and persistence — all of which break
          silently on a swap in ways no test catches. Rank (the 0-based ordinal within this result page,
          as the engine returned it) carries every portable property a caller actually needs. A consumer
          who genuinely needs a score is, by definition, on the provider-exclusive path.

FacetValue          (readonly record struct)   .Value → string { get; }   .Count → long { get; }
FacetNumericStats   (readonly record struct)   .Min → double { get; }     .Max → double { get; }

FacetResult   (sealed record)
    .Field                                                                    → string { get; init; }
    .Values                                              → IReadOnlyList<FacetValue> { get; init; }
    .Stats                                                     → FacetNumericStats? { get; init; }
    .Truncated                                                                  → bool { get; init; }

    NOTE (Truncated IS NOT OPTIONAL GARNISH): facet values truncate at the engine's per-facet cap
          (Meilisearch maxValuesPerFacet, default 100). Without this flag a caller cannot distinguish
          "exactly N facet values exist" from "there were more and they were cut", and will present a
          partial distribution as complete. Set true when the engine returned exactly MaxFacetValues
          entries. Stats is null when the field was not listed in SearchRequest.NumericFacetStats or is
          not numeric — min/max is the ENTIRE portable numeric-aggregation surface.

SearchResults<TDocument>   (sealed record)  where TDocument : class, ISearchDocument
    .Hits                                          → IReadOnlyList<SearchHit<TDocument>> { get; init; }
    .TotalHits                                                                  → long { get; init; }
    .Accuracy                                                      → TotalHitsAccuracy { get; init; }
    .Page                                                                        → int { get; init; }
    .PageSize                                                                    → int { get; init; }
    .Facets                                → IReadOnlyDictionary<string, FacetResult> { get; init; }
    .Duration                                                               → TimeSpan { get; init; }
    .Empty                                                → static SearchResults<TDocument> { get; }
    .ToPagedList()                                              → Result<PagedList<TDocument>>

    NOTE (TotalHits IS long, NOT int): ElasticSearch hit counts routinely exceed int.MaxValue on
          analytics indices, and PagedList<T>.TotalCount is an int treated as EXACT.

    NOTE (WHY 09.Search DECLARES ITS OWN RESULT TYPE rather than returning PagedList<T>): verified
          against source — PagedList<T> is `sealed`, so SearchResults cannot extend it; every property
          is `private init` with an `internal` constructor, so facets, highlights, rank, duration, and
          an accuracy qualifier have nowhere to live; its TotalCount is an int treated as exact by
          TotalPages => Math.Ceiling(TotalCount / PageSize); and Create hard-throws
          ArgumentOutOfRangeException when page < 1. Routing a Meilisearch estimate through it would
          publish an estimate as fact.

    NOTE (ToPagedList IS A GUARDED, LOSSY BOUNDARY PROJECTION — the ONLY sanctioned bridge from
          09.Search to 04.Contracts, and it exists so consumers do not hand-roll the mapping, which is
          exactly the inline-mapping violation WO-026 P-166/P-167 outlawed for Result↔Envelope and P-165
          solved for PagedList→GraphQL). Guards, all returning a Result failure:
              Accuracy != Exact            → SearchErrors.TotalHitsNotExact
              TotalHits > int.MaxValue     → SearchErrors.TotalHitsOverflow
              PageSize < 1 or Page < 1     → SearchErrors.InvalidSearchRequest
          No offset-alignment guard is needed (it would be, for a Skip/Take model) because this contract
          is 1-based Page/PageSize throughout. Facets, Rank, and Highlights are DROPPED by the
          projection — documented on the method itself.
```

#### Write receipts (`Models/`)

```text
SearchWriteConsistency   (enum)
    Accepted = 0     // durable (ES) / enqueued (Meilisearch); NOT yet guaranteed searchable
    Searchable = 1   // the provider waits until the write is visible to search

SearchWriteReceipt   (sealed record)
    .IndexName                                                                → string { get; init; }
    .ProviderToken                                                            → string { get; init; }
    .AffectedCount                                                               → int { get; init; }
    .RequestedConsistency                                     → SearchWriteConsistency { get; init; }
    .AcceptedAt                                                       → DateTimeOffset { get; init; }

    NOTE: AcceptedAt is sourced from IClock (01.Core) in both adapters — never DateTimeOffset.UtcNow
          (SK0001).

    NOTE: ProviderToken is OPAQUE — a Meilisearch taskUid as a string, or an ElasticSearch
          "{index}:{seqNo}:{primaryTerm}". Consumers must NEVER parse it. Its only legal use is being
          handed back to WaitUntilSearchableAsync on the SAME ISearchIndex<TDocument> instance within
          the same process. Modelling it as one opaque string rather than two typed engine-specific
          fields is what keeps two fundamentally different acknowledgement models behind one honest
          contract.

SearchItemFailure   (sealed record)
    .DocumentId                                                               → string { get; init; }
    .Error                                                                     → Error { get; init; }

SearchBulkReceipt   (sealed record)
    .Receipt                                                     → SearchWriteReceipt { get; init; }
    .SucceededCount                                                              → int { get; init; }
    .Failures                                       → IReadOnlyList<SearchItemFailure> { get; init; }
    .HasFailures                                                       → bool { get; }

    NOTE: One SearchItemFailure per failed document, carrying a SharedKernel.Primitives Error — never
          collapsed into one opaque error, mirroring the FileDeleteOutcome precedent in 08.Storage.
```

#### Index definition and probe (`Models/`)

```text
SearchFieldKind   (enum)
    Text = 0, Keyword = 1, Integer = 2, Decimal = 3, Boolean = 4, DateTimeOffset = 5

SearchFieldDefinition   (sealed record)
    .Name                                                                     → string { get; init; }
    .Kind                                                            → SearchFieldKind { get; init; }
    .Searchable                                                → bool { get; init; }   (default false)
    .Filterable                                                → bool { get; init; }   (default false)
    .Sortable                                                  → bool { get; init; }   (default false)
    .Facetable                                                 → bool { get; init; }   (default false)

    NOTE (SIX KINDS, FOUR BOOLEANS, AND NOTHING FINER): there is no analyzer, tokenizer, normalizer, or
          language knob, and there never will be on the neutral surface — that is where a neutral
          mapping DSL lies most convincingly and most harmfully. Text = full-text analysed (ES `text` /
          Meilisearch searchableAttributes); Keyword = exact-match, unanalysed. Anything finer is a
          per-provider settings document applied at deploy time, versioned and fingerprinted. THIS TYPE
          IS THE ONE TO GUARD HARDEST IN REVIEW: every future "just one more knob" request is a lie
          about the other engine.

SearchIndexDefinition   (sealed record)
    .Name                                                                     → string { get; init; }
    .PrimaryKeyField          → string { get; init; }    (default SearchWellKnown.DefaultPrimaryKeyField)
    .TenantField              → string? { get; init; }   (default null → single-tenant/global index)
    .Fields                                    → IReadOnlyList<SearchFieldDefinition> { get; init; }
    .MaxTotalHits             → int { get; init; }       (default SearchWellKnown.DefaultMaxTotalHits)
    .MaxFacetValues           → int { get; init; }       (default SearchWellKnown.DefaultMaxFacetValues)
    .Create(string name, IReadOnlyList<SearchFieldDefinition> fields)
                                                              → Result<SearchIndexDefinition>
    .Fingerprint                                                              → string { get; }

    NOTE (TenantField LIVES HERE, not in provider options): a service may legitimately have one tenanted
          index and one global one. Per-index placement is what lets the fail-closed TenantScopeMissing
          guard arm itself exactly where it should, and nowhere else.

    NOTE (Fingerprint — ALGORITHM PINNED AT DESIGN TIME, not improvised in Core): SHA-256 over a
          canonical UTF-8 string, rendered as lowercase hex of the 32 bytes. The canonical string is,
          with '\n' after every line, no whitespace elsewhere, and no default-value elision:
              Name
              PrimaryKeyField
              TenantField ?? ""
              MaxTotalHits            (invariant culture)
              MaxFacetValues          (invariant culture)
              then, for each field sorted by Name using StringComparer.Ordinal:
              Name|{(int)Kind}|{Searchable:0|1}|{Filterable:0|1}|{Sortable:0|1}|{Facetable:0|1}
          Ordinal sorting makes the value independent of declaration order; explicit ints make it
          independent of enum member renames. Uses System.Security.Cryptography — in-box on net10.0, so
          .Abstractions keeps its zero-PackageReference guarantee. An undefined canonicalisation would
          either report a mismatch on every deploy or never detect real drift; both silently defeat the
          readiness gate.

SearchIndexDefinitionBuilder   (sealed class)
    .PrimaryKey(string field)                                     → SearchIndexDefinitionBuilder
    .TenantField(string field)                                    → SearchIndexDefinitionBuilder
    .Field(string name, SearchFieldKind kind, bool searchable = false, bool filterable = false,
           bool sortable = false, bool facetable = false)         → SearchIndexDefinitionBuilder
    .MaxTotalHits(int value)                                      → SearchIndexDefinitionBuilder
    .MaxFacetValues(int value)                                    → SearchIndexDefinitionBuilder
    .Build()                                                      → Result<SearchIndexDefinition>

IndexCutoverRequest   (sealed record)
    .StagingIndexName                                                         → string { get; init; }
    .LiveIndexName                                                            → string { get; init; }
    .DeleteStagingAfterCutover                                 → bool { get; init; }   (default true)

SearchIndexHealth   (sealed record)
    .Reachable                                                                  → bool { get; init; }
    .IndexAddressable                                                           → bool { get; init; }
    .Searchable                                                                 → bool { get; init; }
    .DocumentCount                                                              → long { get; init; }
    .PendingWriteCount                                                         → long? { get; init; }
    .EngineVersion                                                            → string { get; init; }
    .SchemaFingerprint                                                       → string? { get; init; }
    .Latency                                                                → TimeSpan { get; init; }

    NOTE (IndexAddressable IS SEPARATE FROM Reachable, and is the single most commonly OMITTED readiness
          assertion): a green ElasticSearch cluster with a missing or misnamed read alias passes every
          cluster-health check and returns 100% production failures. Likewise Meilisearch's /health is
          instance-wide and says nothing about whether THIS service's API key is scoped to THIS index.
          Both probes therefore address the index/alias the service actually queries, USING THE
          SERVICE'S OWN CREDENTIALS — an expired or mis-scoped key must fail readiness, not every
          request. Searchable is asserted with a real zero-row search, not assumed.

    NOTE (PendingWriteCount IS NULLABLE AND THAT IS PERMANENT, not a TODO): Meilisearch exposes a global
          sequential task queue whose depth predicts user-visible staleness; ElasticSearch has NO
          equivalent scalar. Nullability is the honest way to model a capability gap — returning 0 for
          ES would make a "backlog is zero" alert meaningless. 13.ServiceDefaults MUST NOT treat null as
          unhealthy or as a value to be filled in later. A deep backlog is a METRIC/ALERT, NEVER a
          readiness failure: it means results are STALE, not UNAVAILABLE, and failing readiness would
          remove serving capacity exactly when it is most needed.
```

#### Well-known constants (`Constants/`)

```text
SearchWellKnown   (public static class — SK0022 named-constant holder)
    DefaultPrimaryKeyField      const string = "documentId"
    DefaultHighlightPreTag      const string = "<em>"
    DefaultHighlightPostTag     const string = "</em>"
    DefaultPageSize             const int    = 20
    MaxPageSize                 const int    = 1000
    DefaultMaxTotalHits         const int    = 1000
    DefaultMaxFacetValues       const int    = 100
    ActivitySourceName          const string = "SharedKernel.Search"
    MeterName                   const string = "SharedKernel.Search"
    MeilisearchProviderName     const string = "meilisearch"
    ElasticSearchProviderName   const string = "elasticsearch"
    ProviderTagName             const string = "search.provider"
    IndexTagName                const string = "search.index"

    NOTE: This is the domain-local constants class the platform's magic-string convention mandates,
          mirroring SecurityClaimTypes (12.Security.Abstractions), WebhookSignatureHeaders
          (15.Integration), and HubGroupNaming (14.Presentation.SignalR). It lives in .Abstractions
          specifically so both sibling providers read the BYTE-IDENTICAL ActivitySource/Meter name and
          OTel tag values — that identity is the whole reason 13.ServiceDefaults can wire one string
          name and cover both providers with no ProjectReference to 09.Search. The ActivitySource and
          Meter INSTANCES are created per-provider; only the names live here.
```

#### Search errors (`Errors/`)

```text
SearchErrors   (public static class — canonical Error factory; provider implementations return these
                and never construct ad-hoc Error values inline)

    — Error.NotFound —
    .IndexNotFound(indexName)                            "search.index_not_found"
    .DocumentNotFound(indexName, documentId)             "search.document_not_found"

    — Error.Validation —
    .InvalidSearchRequest(reason)                        "search.invalid_request"
    .InvalidFilter(reason)                               "search.invalid_filter"
    .InvalidIndexDefinition(reason)                      "search.invalid_index_definition"
    .InvalidDocumentId(documentId)                       "search.invalid_document_id"
    .FieldNotSearchable(indexName, field)                "search.field_not_searchable"
    .FieldNotFilterable(indexName, field)                "search.field_not_filterable"
    .FieldNotSortable(indexName, field)                  "search.field_not_sortable"
    .FieldNotFacetable(indexName, field)                 "search.field_not_facetable"
    .PaginationLimitExceeded(page, pageSize,
                             ceiling, providerName)      "search.pagination_limit_exceeded"
    .FacetLimitExceeded(requested, ceiling)              "search.facet_limit_exceeded"
    .TotalHitsNotExact()                                 "search.total_hits_not_exact"
    .TotalHitsOverflow(totalHits)                        "search.total_hits_overflow"
    .UnsupportedCapability(capability, providerName)     "search.unsupported_capability"

    — Error.Conflict —
    .IndexAlreadyExists(indexName)                       "search.index_already_exists"
    .IndexDefinitionConflict(indexName, field)           "search.index_definition_conflict"
    .CutoverFailed(stagingIndexName, liveIndexName,
                   reason)                               "search.cutover_failed"
    .SchemaFingerprintMismatch(indexName, expected,
                               actual)                   "search.schema_fingerprint_mismatch"

    — Error.Unauthorized —
    .Unauthorized(indexName, operation)                  "search.unauthorized"
    .TenantScopeMissing(indexName)                       "search.tenant_scope_missing"

    — Error.Unexpected —
    .Unreachable(providerName, endpoint)                 "search.unreachable"
    .Timeout(operation, elapsed)                         "search.timeout"
    .WriteRejected(indexName, reason)                    "search.write_rejected"
    .WriteTimeout(indexName, elapsed)                    "search.write_timeout"
    .BulkPartiallyFailed(failedCount, totalCount)        "search.bulk_partially_failed"
    .ProbeFailed(indexName, reason)                      "search.probe_failed"
    .EngineVersionUnsupported(actual, supportedRange)    "search.engine_version_unsupported"
    .EngineFault(providerName, operation, detail)        "search.engine_fault"

    NOTE (ONLY THE SIX REAL Error FACTORIES ARE USED): SharedKernel.Primitives' Error exposes exactly
          Unexpected, Validation, NotFound, Conflict, Unauthorized, and BusinessRule — each
          (string code, string message) — plus the Error.None sentinel field. There is NO Error.Failure,
          no Error.Forbidden, no single-argument overload, and no exception-accepting overload. Wherever
          a generic "the operation failed" is meant, Error.Unexpected is used, matching
          ErrorCodes.Unexpected.Default. Every code literal is a private const string on the holder
          class — never retyped at a call site (SK0022).

    NOTE (Error.BusinessRule IS USED ZERO TIMES IN THIS DOMAIN, deliberately): per ErrorType's own XML
          doc it maps to HTTP 422 Unprocessable Entity and denotes a DOMAIN-RULE violation; nothing in a
          capability package is a domain rule. TotalHitsNotExact and TotalHitsOverflow are Validation —
          the failing input to ToPagedList() is the result's own Accuracy/TotalHits value, and "the
          input to this operation is not acceptable" is precisely what Validation denotes.
          TenantScopeMissing is Unauthorized — it is an isolation/authorization failure, and 401/403 is
          the honest boundary status; 422 would frame a would-be cross-tenant leak as an unprocessable
          business request.

    NOTE (Error.None IS NEVER RETURNED from any method in this domain): Envelope.Fail throws
          ArgumentException on it, so returning it would detonate at the service boundary.

    NOTE (WriteTimeout IS DISTINCT FROM WriteRejected): on a write timeout the document is durable (ES)
          or enqueued (Meilisearch) — only the stronger consistency the caller asked for was not
          achieved in time. THE WRITE MAY STILL LAND; the message says so explicitly, so callers do not
          treat it as "the write did not happen" and retry blindly.

    NOTE (BulkPartiallyFailed IS NOT RETURNED BY IndexManyAsync): partial failure is reported through
          SearchBulkReceipt on a SUCCESS Result. This factory exists for consumers and rebuild
          orchestrators that treat any item failure as fatal and need one canonical Error to surface.

    NOTE (UnsupportedCapability IS RESERVED AND CURRENTLY UNREACHABLE BY CONSTRUCTION): the closed
          8-node filter hierarchy has no untranslatable node today. It exists so that adding a node in a
          future phase produces a LOUD failure on the lagging adapter rather than a dropped predicate.

    NOTE (EngineFault IS THE LAST-RESORT MAPPING for an unclassifiable 4xx/5xx). A RISE IN THIS CODE IS
          THE SIGNAL that a translator has drifted from the engine's current API surface.
```

#### Streaming exception (`Exceptions/`)

```text
SearchStreamException   (sealed exception, derives directly from System.Exception)
    .Error                                                                     → Error { get; }

    NOTE: Thrown from ISearchIndex.EnumerateAsync and ICursorSearch.StreamAsync — the only two
          non-Result surfaces in the domain. Constructed ONLY from a SharedKernel.Primitives Error
          (plus an optional inner Exception overload), never from a bare string, so SK0003 and SK0005
          are satisfied.

    NOTE (BASE-TYPE DECISION — RESOLVED AT SCAFFOLD TIME, S-12, do not re-litigate in Core): read
          directly against SharedKernel.Primitives' shipped source
          (01.Core/SharedKernel.Primitives/**/*.cs) confirms Primitives ships NO exception type at
          all — no SharedKernelException, no Error-carrying base. The only Error-carrying exception
          hierarchy in the platform (SharedKernelException → DomainException/ValidationException/
          NotFoundException/ConflictException/UnauthorizedException) lives in 01.Core/SharedKernel.Core,
          a SEPARATE package that SharedKernel.Search.Abstractions does not and must not reference —
          09.Search.Abstractions' locked reference set is exactly SharedKernel.Primitives +
          SharedKernel.Contracts (see Packages table), and adding SharedKernel.Core would be an
          unreviewed, undesigned new ProjectReference introduced through a side door. Per this
          contract's own documented fallback ("if no base permits an Error payload it derives from
          Exception and carries Error directly"), SearchStreamException derives directly from
          System.Exception and declares its own Error property. (Checked for a precedent first:
          06.Persistence's IReadRepository.StreamAsync and 08.Storage's IFileStorage.ListAsync — the
          two prior platform streaming-read precedents this contract was modelled on — both let the
          underlying provider exception (EF Core's own exception type; AmazonS3Exception) propagate
          un-wrapped from MoveNextAsync rather than defining a dedicated custom exception type at all,
          so there is no existing base-type precedent to mirror; SearchStreamException carrying a
          structured Error is a genuinely new pattern for this domain, justified by 09.Search's
          platform-wide Result-first/Error-value convention, which the other two domains do not carry
          as strictly on their streaming paths.) This is now the final, locked decision — Core (C-14)
          implements it exactly as stated here, no further base-type search is needed.
```

---

### `SharedKernel.Search.Meilisearch` — public surface

> BFF/fast. Implements the three neutral contracts and **declares** the Meilisearch-exclusive ones.
> `IAnalyticsSearch<TDocument>` and `ICursorSearch<TDocument>` do not exist in this package — a call
> site referencing them fails to COMPILE against a Meilisearch-only composition root. That compile
> error is the capability mechanism.

```text
MeilisearchIndex<TDocument>   (sealed class, implements ISearchIndex<TDocument>)  — scoped
    — wraps Meilisearch.MeilisearchClient / Meilisearch.Index.
    — every write returns TaskInfo (an ENQUEUE receipt, not a completed result) →
      ProviderToken = TaskUid.ToString(). Searchable calls WaitForTaskAsync(taskUid, timeoutMs,
      intervalMs) with EXPLICIT values from options — never the SDK defaults (5000 ms timeout, fixed
      50 ms poll interval with no backoff), which a bulk import routinely exceeds while hammering the
      server.
    — IndexManyAsync uses AddDocumentsInBatchesAsync, which returns IEnumerable<TaskInfo>; the adapter
      awaits all of them under Searchable and folds them into one SearchBulkReceipt whose ProviderToken
      is the LAST taskUid (tasks process in enqueue order, so waiting on the last implies the earlier
      ones completed).
    — SearchAsync: RequireExactTotalHits = false → Offset/Limit, yielding SearchResult<T> with
      EstimatedTotalHits → Accuracy = Estimated. true → Page/HitsPerPage, yielding
      PaginatedSearchResult<T> with exact TotalHits → Accuracy = Exact. The SDK's SearchAsync<T> returns
      ISearchable<T> whose converter SNIFFS the response body; the adapter knows which form it requested
      and pattern-matches accordingly, returning SearchErrors.EngineFault on a mismatch rather than
      casting blindly.
    — VERIFIED (Core-phase): the SDK's typed `SearchAsync<TDocument>` generic path does NOT capture the
      per-hit `_formatted` sibling object Meilisearch returns for highlights — there is no hook for it on
      that overload. MeilisearchResultMapper therefore always searches with T = System.Text.Json.JsonElement
      instead of the caller's TDocument, manually deserializes the document via
      `JsonSerializerOptions { PropertyNameCaseInsensitive = true }`, and separately extracts the
      `_formatted` sibling object for SearchHit.Highlights. This is load-bearing: any future change to
      MeilisearchIndex's search path must preserve the JsonElement search, not "simplify" it back to a
      typed SearchAsync<TDocument> call, or highlighting silently stops populating.
    — VERIFIED (Tests-phase, real container, was a genuine Core-phase production bug, now fixed):
      MeilisearchResultMapper.MapFacets threw NullReferenceException on EVERY search that did not
      request facets — the SDK returns null (not an empty dictionary) for facetDistribution/facetStats
      whenever SearchRequest.Facets/.NumericFacetStats are empty, which is almost every ordinary
      search. Fixed: MapFacets' facetDistribution/facetStats parameters are now nullable, with an
      early-return producing SearchResults.Empty's Facets shape when either is null or empty.
    — VERIFIED (Tests-phase, real container, was a genuine Core-phase production bug, now fixed):
      GetAsync only caught MeilisearchApiError for a missing document, but the SDK's
      GetDocumentAsync<T> throws HttpRequestException (with StatusCode == NotFound) for THIS specific
      404 path instead — the same SDK exception-type inconsistency documented on ProbeAsync above.
      Fixed by adding the missing HttpRequestException catch clause alongside the existing
      MeilisearchApiError one.
    — EnumerateAsync walks GET /indexes/{uid}/documents (offset+limit), NOT the search endpoint — so it
      is not subject to the maxTotalHits ceiling.

MeilisearchFilterCompiler   (internal sealed class)
    — walks the closed 8-node SearchFilter with an exhaustive property-pattern switch and NO discard
      arm, emitting the Meilisearch filter-expression STRING.
    — every String value is escaped and quoted; numerics/booleans emitted bare; DateTimeOffset → Unix
      epoch seconds.
    — PARENTHESES ARE EMITTED AROUND EVERY Or AND Not OPERAND UNCONDITIONALLY rather than relying on
      Meilisearch's NOT > AND > OR precedence — precedence-dependent output is the classic
      filter-injection bug.
    — the SDK's Filter property is typed `dynamic`; the compiler assigns a plain `string` and nothing
      else may ever be assigned to it.
    — TenantScope is prepended as the OUTERMOST AND after compilation, structurally beyond the caller's
      reach.
    — VERIFIED (Tests-phase, real container, T-26, 2026-07-20 — genuine cross-provider semantic
      divergence, not a bug): SearchFilter.Any() with ZERO operands compiles to the literal string
      "()", which Meilisearch's filter parser REJECTS as invalid syntax (raw HTTP: 400,
      code: invalid_search_filter). The adapter correctly maps this to SearchErrors.EngineFault rather
      than silently degrading to unfiltered — this is the CORRECT fail-loud behaviour for this engine.
      ElasticSearch's equivalent (an empty BoolQuery.Should array) is valid syntax and evaluates as
      MATCH-ALL instead — see ElasticSearchFilterCompiler's own note below. Both are faithful
      translations of each engine's native semantics; a caller composing SearchFilter.Any() with a
      potentially-empty operand list must guard against the empty case itself if identical behaviour
      across a provider swap is required — see the corresponding Test Rules bullet for the full finding.

MeilisearchIndexProvisioner   (sealed class, implements ISearchIndexProvisioner)  — singleton
    — EnsureIndexAsync: CreateIndexAsync(uid, primaryKey) then UpdateSettingsAsync with
      SearchableAttributes (ORDER IS SIGNIFICANT — it is the relevance priority order),
      FilterableAttributes (Filterable ∪ Facetable ∪ TenantField), SortableAttributes,
      Pagination.MaxTotalHits, Faceting.MaxValuesPerFacet; SchemaFingerprint written to a reserved
      settings entry (VERIFIED Core-phase mechanism: Meilisearch has no native index-metadata field
      analogous to ElasticSearch's index `_meta`, so the fingerprint is stored as a sentinel-prefixed
      entry — `"__sk_schema_fingerprint__:{hash}"` — inside Settings.Dictionary, which is otherwise the
      custom-tokenizer dictionary-words list. A deliberate repurposing, not a Meilisearch feature).
    — CutoverAsync: SwapIndexesAsync → wait the task → DeleteIndexAsync(staging) when
      DeleteStagingAfterCutover; logs Warning 9113 if the caller opted out, because the staging name now
      holds the old data and silently doubles storage.
      VERIFIED (Tests-phase, real container, was a genuine Core-phase production bug, now fixed):
      Meilisearch's SwapIndexesAsync genuinely requires BOTH index names to already exist — confirmed
      via raw HTTP that swapping against a never-created name fails the task with error.code
      "index_not_found" — but the documented stage→bulk-load→cutover consumer flow never separately
      creates the live index, so a first-ever cutover always failed. Fixed: CutoverAsync now checks
      IndexExistsAsync(request.LiveIndexName) first and, if absent, calls CreateIndexAsync + waits the
      task to create an empty placeholder live index BEFORE issuing the swap.
    — ProbeAsync: IsHealthyAsync (GET /health — the only route unprotected by the master key) →
      Reachable; Index(uid).GetSettingsAsync() WITH THE SERVICE'S OWN SCOPED KEY → IndexAddressable
      (Meilisearch keys are per-index, so a mis-scoped key is invisible to /health); a
      {"q":"","limit":0} search → Searchable; GetTasksAsync(statuses: enqueued) → PendingWriteCount;
      GetStatsAsync → DocumentCount; the total elapsed probe duration → Latency; GetVersionAsync →
      EngineVersion; reserved settings entry → SchemaFingerprint. Every one of SearchIndexHealth's
      eight members is populated by this sequence — none is left at its default.
      VERIFIED (Tests-phase, real container, was a genuine Core-phase production bug, now fixed): the
      SDK's GetIndexAsync(uid) NEVER throws for any uid, real or nonexistent — confirmed via a scratch
      console project that it silently returns a synthetic Index object even for a uid that was never
      created, while raw curl against the same REST endpoint correctly returns 404. This made
      IndexExistsAsync/ProbeAsync's IndexAddressable check always report true. Fixed: both now call
      Index(uid).GetSettingsAsync(ct) instead, which genuinely throws, with a dual catch
      (MeilisearchApiError via .IsNotFound, and HttpRequestException where StatusCode == NotFound —
      the SDK is internally inconsistent about which exception type a given 404 path throws).

MeilisearchProviderDescriptor   (sealed class, implements ISearchProviderDescriptor)  — singleton

IInstantSearch<TDocument>   (MEILISEARCH-ONLY CONTRACT, declared here)  — scoped
    .InstantAsync(InstantSearchRequest request, TenantScope tenantScope, CancellationToken ct)
                                                                → Task<Result<SearchResults<TDocument>>>
    .SearchFacetValuesAsync(string facetField, string facetQuery, SearchFilter? filter,
                            TenantScope tenantScope, CancellationToken ct)
                                                            → Task<Result<IReadOnlyList<FacetValue>>>
    NOTE (this is what keeps the split honest rather than an "ElasticSearch is the superset" story):
          Meilisearch applies typo tolerance AUTOMATICALLY with word-length thresholds and ships prefix
          matching, crop markers, and a dedicated facet-value-search endpoint as first-class primitives.
          The ElasticSearch equivalents (fuzziness, match_phrase_prefix, a terms agg with an include
          regex) have materially different edit-distance behaviour and cost profiles, so a neutral
          `TypoTolerant = true` boolean would be a no-op on one engine and a query-plan change on the
          other. Not neutralised.
          SearchFacetValuesAsync is type-ahead WITHIN a facet's values (POST
          /indexes/{uid}/facet-search). It is NOT the facet distribution returned by SearchAsync and
          must not be conflated with it.

ITenantSearchTokenIssuer   (MEILISEARCH-ONLY CONTRACT; registered only by .WithTenantTokens())
    .IssueAsync(TenantScope tenantScope, string tenantField,
                IReadOnlyCollection<string> indexNames, TimeSpan ttl, CancellationToken ct)
                                                              → Task<Result<TenantSearchToken>>
    NOTE (WHY THERE IS NO NEUTRAL EQUIVALENT): Meilisearch tenant tokens are HS256 JWTs carrying
          searchRules that the ENGINE enforces below the application — the strongest tenant-isolation
          primitive either engine offers. ElasticSearch's equivalent, document-level security, is a
          commercial-tier feature; the OSS substitute is a filtered alias plus role privileges denying
          direct access to the concrete index name, which is DEPLOYMENT configuration, not a runtime
          API. A neutral "issue a scoped token" contract whose ES adapter returned a locally-signed
          token that NOTHING enforces would be actively dangerous — identical at the type level,
          opposite in effect.

    NOTE (THE RULE DICTIONARY IS CLOSED ON PURPOSE): the implementation constructs searchRules itself
          from TenantScope and tenantField. The SDK's TenantTokenRules constructor takes
          IReadOnlyDictionary<string, object> — untyped and unvalidated, where a mistake is a SILENT
          cross-tenant leak. Callers can never hand-write it.

    NOTE (REVOCATION IS IMPOSSIBLE): tokens are not stored or tracked server-side and CANNOT be revoked
          before expiry. A ttl above MeilisearchOptions.TenantTokenMaxTtlMinutes returns
          MeilisearchErrors.TenantTokenTtlOutOfRange.

    NOTE (SEARCH ONLY): tenant tokens scope SEARCH. They do NOT scope document writes. Write-path tenant
          isolation remains the caller's responsibility through the TenantScope parameter on
          ISearchIndex.

IMeilisearchRawClientAccessor   (LAST-RESORT ESCAPE HATCH — NOT REGISTERED BY DEFAULT)
    .Client                                                → Meilisearch.MeilisearchClient { get; }
    .IndexHandle(string indexName)                                        → Meilisearch.Index
    NOTE: See "Raw client accessors" under Implementation Rules — three gates and a capitalised
          tenant-bypass warning apply.

InstantMatchingStrategy   (enum)   Last = 0 | All = 1 | Frequency = 2

InstantSearchRequest   (sealed record)
    .FreeText → string          .Filter → SearchFilter?      .Limit → int  (default 10, [Range(1,50)])
    .AttributesToSearchOn → IReadOnlyList<string>            .Highlight → HighlightRequest?
    .CropLength → int?          .CropMarker → string? (default "…")
    .MatchingStrategy → InstantMatchingStrategy  (default Last)

TenantSearchToken   (sealed record)
    .Value → string             .ExpiresAt → DateTimeOffset  .ScopedIndexes → IReadOnlyList<string>

MeilisearchErrors   (public static class)
    .IndexingTaskFailed(providerToken, engineErrorCode)   Error.Unexpected
                                                          "search.meilisearch.indexing_task_failed"
    .TenantTokenIssuanceFailed(reason)                    Error.Unexpected
                                                          "search.meilisearch.tenant_token_issuance_failed"
    .TenantTokenTtlOutOfRange(requested, maxMinutes)      Error.Validation
                                                          "search.meilisearch.tenant_token_ttl_out_of_range"
    NOTE: IndexingTaskFailed has NO ElasticSearch analogue — there is no task queue there.

MeilisearchOptions   (sealed class — Options-pattern, validated at startup)
    public const string SectionName = "Search:Meilisearch"
    .Url                          string   ([Required], [Url])
    .ApiKey                       string   ([Required])
    .ApiKeyUid                    string?  (required only when .WithTenantTokens() is used)
    .HttpTimeoutSeconds           int      ([Range(1,300)],    default 30)
    .TaskWaitTimeoutSeconds       int      ([Range(1,3600)],   default 120)
    .TaskPollIntervalMilliseconds int      ([Range(25,5000)],  default 250)
    .MaxTotalHits                 int      ([Range(1,100000)], default 1000)
    .MaxFacetValues               int      ([Range(1,10000)],  default 100)
    .DefaultBatchSize             int      ([Range(1,100000)], default 1000)
    .TenantTokenMaxTtlMinutes     int      ([Range(1,60)],     default 15)
    .ValidateIndexSettingsOnStart bool     (default true)
    NOTE: SectionName is the single source for the config path — never a bare "Search:Meilisearch"
          literal at a GetSection call site (SK0022).

AddSharedKernelMeilisearchSearch(IConfiguration configuration)      → MeilisearchSearchBuilder
AddSharedKernelMeilisearchSearch(IConfigurationSection section)     → MeilisearchSearchBuilder
    .AddIndex<TDocument>(string indexName, Action<SearchIndexDefinitionBuilder> configure)
                                                                    → MeilisearchSearchBuilder
    .WithTenantTokens()                                             → MeilisearchSearchBuilder
    .AllowRawClientAccess()                                         → MeilisearchSearchBuilder
    .Build()                                                        → IServiceCollection
    NOTE: The root call registers MeilisearchClient (SINGLETON, constructed via the
          MeilisearchClient(HttpClient, string apiKey) overload from a NAMED IHttpClientFactory client —
          never `new HttpClient()`, per P-159/SK0013), ISearchIndexProvisioner, and
          ISearchProviderDescriptor. AddIndex<TDocument> registers scoped ISearchIndex<TDocument> and
          IInstantSearch<TDocument>, and records the SearchIndexDefinition for pre-flight validation.
          The SDK ships no IMeilisearchClient interface, only the concrete class, so the abstraction
          seam is entirely ours; adapters take MeilisearchClient, never HttpClient.

    NOTE (VERIFIED Core-phase DI PATTERN — singleton lazy-resolution): the MeilisearchClient singleton
          factory delegate is registered once, but the FINAL registered-index set is only known once the
          fluent `.AddIndex<TDocument>(...)` chain finishes and `.Build()` runs. The factory lambda
          captures a reference to the builder ITSELF and reads its (internal) registered-index count
          lazily, at first container resolution — which always happens after `.Build()` has completed.
          No reflection, no two-phase DI registration trick, no post-hoc mutation of an already-built
          ServiceProvider. ElasticSearchServiceCollectionExtensions uses the identical pattern.

MeilisearchLog   (internal static partial class — [LoggerMessage], EventId sub-block 9100–9199)
    9100 Information  MeilisearchClientConfigured        {Url} {IndexCount}
    9101 Information  MeilisearchIndexEnsured            {IndexName} {FieldCount}
    9102 Information  MeilisearchIndexSettingsApplied    {IndexName} {FilterableCount} {SortableCount} {FacetableCount}
    9103 Debug        MeilisearchDocumentsEnqueued       {IndexName} {DocumentCount} {ProviderToken}
    9104 Debug        MeilisearchDocumentsDeleted        {IndexName} {DocumentCount} {ProviderToken}
    9105 Debug        MeilisearchTaskCompleted           {ProviderToken} {TaskStatus} {ElapsedMs}
    9106 Warning      MeilisearchTaskWaitTimedOut        {ProviderToken} {IndexName} {ElapsedMs}
    9107 Error        MeilisearchTaskFailed              {ProviderToken} {IndexName} {EngineErrorCode}
    9108 Warning      MeilisearchBulkPartialFailure      {IndexName} {FailedCount} {TotalCount}
    9109 Debug        MeilisearchSearchExecuted          {IndexName} {HitCount} {TotalHits} {Accuracy} {ProcessingTimeMs}
    9110 Warning      MeilisearchRequestRejected         {IndexName} {Reason}
    9111 Warning      MeilisearchTenantScopeMissing      {IndexName}
    9112 Information  MeilisearchIndexesSwapped          {StagingIndexName} {LiveIndexName}
    9113 Warning      MeilisearchStagingIndexRetained    {StagingIndexName}
    9114 Debug        MeilisearchTenantTokenIssued       {IndexCount} {TtlMinutes}
    9115 Warning      MeilisearchRawClientAccessEnabled  (startup)
    9116 Warning      MeilisearchProbeDegraded           {IndexName} {Reachable} {IndexAddressable} {Searchable}
    9117 Warning      MeilisearchWriteBacklogDeep        {PendingWriteCount}
    9118 Warning      MeilisearchSchemaFingerprintMismatch  {IndexName} {Expected} {Actual}
    9119 Error        MeilisearchEngineFault             {Operation} {IndexName}
    9120 Debug        MeilisearchDocumentWalkStarted     {IndexName} {BatchSize}
```

---

### `SharedKernel.Search.ElasticSearch` — public surface

> Analytics/heavy. Implements the same three neutral contracts and **declares** the
> ElasticSearch-exclusive ones. `IInstantSearch<TDocument>` and `ITenantSearchTokenIssuer` do not exist
> in this package.

```text
ElasticSearchIndex<TDocument>   (sealed class, implements ISearchIndex<TDocument>)  — scoped
    — wraps Elastic.Clients.Elasticsearch.ElasticsearchClient (registered SINGLETON — thread-safe and
      pools resources); async methods only.
    — EVERY response is checked via response.IsValidResponse. This client does NOT throw by default, so
      a missed check silently swallows failures.
    — IndexAsync(doc, i => i.Index(name).Id(doc.DocumentId).Refresh(...)) — Accepted → Refresh.False,
      Searchable → Refresh.WaitFor. Refresh.True IS NEVER EMITTED.
      ProviderToken = "{index}:{seqNo}:{primaryTerm}".
    — IndexManyAsync → BulkAsync(b => b.Index(name).IndexMany(...)) batched by PAYLOAD BYTES (target
      5–15 MB) and document count, whichever hits first; ItemsWithErrors project per-item into
      SearchItemFailure. BulkAllObservable ships but has been undocumented since the 8.18 doc deletion —
      the adapter uses plain BulkAsync with its own batching rather than depending on an undocumented
      helper.
    — EnumerateAsync walks search_after + PIT, always closing the PIT in a finally.

ElasticSearchFilterCompiler   (internal sealed class)
    — OBJECT-INITIALIZER FORM THROUGHOUT, matching the 9.0 breaking change that moved container types
      (Query, Aggregation, SortOptions) from static factory methods to plain settable properties:
      `new Query { Bool = new BoolQuery { ... } }`, never `Query.Bool(...)`.
    — the same exhaustive 8-node switch with NO discard arm, emitting TermQuery, TermsQuery, RangeQuery,
      ExistsQuery, and BoolQuery.MustNot into BoolQuery.Filter — filter context, non-scoring, cacheable,
      and semantically identical to Meilisearch's filter (which has no scoring notion at all).
    — the client has NO conditionless-query support, so clause collections are built conditionally in C#
      and only non-empty ones are assigned; the client serializes empty objects rather than eliding them.
    — Sort is built as a full ICollection<SortOptions> and assigned ONCE — never via chained descriptor
      calls (issue #8471 silently keeps only the last field). FieldSort has no parameterless ctor in 9.0.
    — TenantScope is injected as the OUTERMOST filter clause after translation.

    VERIFIED AT CORE-PHASE (2026-07-19, closes the prior Design-phase open risk): reflected directly
    against the compiled Elastic.Clients.Elasticsearch 9.4.2 assembly. Leaf query types (TermQuery,
    TermsQuery, RangeQuery/NumberRangeQuery, ExistsQuery) use the SAME parameterless-constructor-plus-
    object-initializer form as the container types — `new TermQuery { Field = ..., Value = ... }`. Each
    leaf type also ships a convenience constructor (e.g. `TermQuery(Field field)`) but it is
    obsolete-annotated and does NOT satisfy the type's `required` members without `[SetsRequiredMembers]`,
    so `new TermQuery(f.Field) { Value = ... }` fails to compile (CS9035, required member `Field` not
    set) even though `Field` is passed positionally. ALWAYS use the parameterless-ctor-plus-full-
    initializer form for every leaf query type; never the single-argument convenience constructor.

    VERIFIED (Tests-phase, real container, T-26, 2026-07-20 — genuine cross-provider semantic
    divergence, not a bug): SearchFilter.Any() with ZERO operands compiles to an empty
    BoolQuery.Should array, which is valid ES query syntax and evaluates as MATCH-ALL (every document
    in the tenant-scoped set is returned, IsSuccess = true). This is the OPPOSITE of Meilisearch's
    behaviour for the identical neutral-surface call — see MeilisearchFilterCompiler's own note above,
    where the same construction is rejected as invalid filter syntax (SearchErrors.EngineFault). Both
    are faithful, correct translations of each engine's native empty-Or semantics; the divergence is
    a portability hazard for any caller composing SearchFilter.Any() with a potentially-empty operand
    list, not an implementation defect on either side.

ElasticSearchIndexProvisioner   (sealed class, implements ISearchIndexProvisioner)  — singleton
    — EnsureIndexAsync creates the index with an explicit TypeMapping derived from SearchFieldKind
      (Text → text, Keyword → keyword, Integer → integer/long, Decimal → double, Boolean → boolean,
      DateTimeOffset → date), sets index.max_result_window to MaxTotalHits, and stores SchemaFingerprint
      in index metadata.
    — CutoverAsync issues a SINGLE Indices.UpdateAliasesAsync whose actions array contains BOTH the
      remove and the add — ES applies the whole array atomically, so the alias is never undefined.
      LiveIndexName is an ALIAS.
    — ProbeAsync: Cluster.HealthAsync with local=true, wait_for_status=yellow, timeout=1s — YELLOW IS
      HEALTHY (a single-node cluster is permanently yellow, and gating readiness on green is a
      documented defect class in Elastic's own Helm charts); fail only on red or timeout. Then a
      size:0, terminate_after:1 search AGAINST THE ALIAS THE SERVICE ACTUALLY QUERIES, WITH THE
      SERVICE'S REAL CREDENTIALS → IndexAddressable + Searchable. Indices.StatsAsync (or that same
      size:0 search's hits.total) → DocumentCount; the total elapsed probe duration → Latency;
      index metadata → SchemaFingerprint; the cluster info response → EngineVersion.
      PendingWriteCount is NULL — permanently, see SearchIndexHealth. Every other SearchIndexHealth
      member is populated by this sequence. The result is cached for ProbeCacheSeconds so the probe
      does not become the load.
      VERIFIED (Tests-phase, real container, was a genuine Core-phase production bug, now fixed):
      ProbeAsync originally constructed an INDEX-SCOPED `new HealthRequest(indexName)`, which fails
      outright when the named index/alias does not exist — collapsing Reachable and IndexAddressable
      into one signal instead of the two the design calls for. Fixed by switching to the parameterless
      cluster-wide `new HealthRequest()` for the Reachable check, leaving the subsequent size:0 search
      against the real alias as the sole source of IndexAddressable/Searchable, matching the design's
      "cluster health is one axis, alias addressability is a separate axis" intent exactly.

ElasticSearchProviderDescriptor   (sealed class, implements ISearchProviderDescriptor)  — singleton

    NOTE (DELIBERATELY ENFORCED RESTRICTIONS ES DOES NOT IMPOSE): a filter/sort/facet field absent from
          the registered SearchIndexDefinition is rejected before I/O, and Page * PageSize >
          MaxTotalHits is rejected even where index.max_result_window would allow it. See the
          MaxTotalHits blockquote in Technology Stack.

IAnalyticsSearch<TDocument>   (ELASTICSEARCH-ONLY CONTRACT, declared here)  — scoped
    .AggregateAsync(SearchFilter? filter, IReadOnlyCollection<AggregationRequest> aggregations,
                    TenantScope tenantScope, CancellationToken ct)
                                                          → Task<Result<AggregationResultSet>>
    NOTE (THE HARDEST WALL IN THE DOMAIN): Meilisearch offers facetDistribution (document COUNTS) plus
          facetStats (min/max on numeric facets) and NOTHING else — no sum, no avg, no cardinality, no
          percentiles, no date_histogram, no nested or pipeline aggregations. The gap is ABSENCE, not
          degree. Forcing a neutral aggregation model would produce something that is simultaneously a
          bad ElasticSearch client and an unimplementable Meilisearch adapter.

    NOTE (WHY DECLARED HERE AND NOT IN .Abstractions): declaring it in the abstractions package would
          let a call site reference it while referencing only Abstractions, so a provider swap would
          surface as a STARTUP resolution error. Declaring it in the ElasticSearch package makes that
          same call site take a COMPILE-TIME dependency on ElasticSearch, so the swap surfaces as a
          BUILD ERROR enumerating every non-portable site. That is strictly earlier and strictly more
          informative. It also preserves the seam rule: .Abstractions contains nothing either provider
          cannot implement.
          tenantScope is a mandatory separate parameter for the same reason it is on the neutral read
          path — it must not be smuggled into the aggregation body.

    NOTE (VERIFIED Core-phase NAMING COLLISION with the SDK — binding convention for all future
          ElasticSearch analytics code): this domain's own SharedKernel.Search.ElasticSearch.Analytics
          namespace deliberately reuses the SAME type names as Elastic.Clients.Elasticsearch.Aggregations
          (TermsAggregation, RangeBucket, DateHistogramBucket, CalendarInterval, etc.) — the neutral
          model intentionally mirrors the engine's own vocabulary. Consequently ElasticSearchAnalytics<T>
          (the implementation) fully qualifies EVERY SDK aggregation type as
          `global::Elastic.Clients.Elasticsearch.Aggregations.*` throughout and takes NO `using` for that
          namespace, rather than aliasing or renaming either side. Any future addition to this file or a
          sibling ElasticSearch analytics type must follow the same fully-qualified convention — adding a
          `using Elastic.Clients.Elasticsearch.Aggregations;` here would make every unqualified reference
          ambiguous at best and silently bind to the wrong type at worst.

ICursorSearch<TDocument>   (ELASTICSEARCH-ONLY CONTRACT, declared here)  — scoped
    .StreamAsync(SearchRequest request, TenantScope tenantScope, TimeSpan keepAlive,
                 [EnumeratorCancellation] CancellationToken ct)
                                                          → IAsyncEnumerable<SearchHit<TDocument>>
    .OpenCursorAsync(SearchRequest request, TenantScope tenantScope, TimeSpan keepAlive,
                     CancellationToken ct)                → Task<Result<SearchCursor>>
    .ReadCursorAsync(SearchCursor cursor, CancellationToken ct)
                                                          → Task<Result<CursorPage<TDocument>>>
    .CloseCursorAsync(SearchCursor cursor, CancellationToken ct)                     → Task<Result>
    NOTE (RELEVANCE-ORDERED DEEP PAGINATION — distinct from ISearchIndex.EnumerateAsync, which is an
          unordered corpus walk): Meilisearch has NO search_after and NO point-in-time at any price, and
          its limit+offset is hard-capped by maxTotalHits. Paging it in a loop to fake a cursor would
          silently stop at 1000 documents, so it is not faked.

    NOTE (StreamAsync IS NOT Result-WRAPPED): same streaming precedent as EnumerateAsync; transport
          failure throws SearchStreamException. It opens a PIT, relies on the implicit _shard_doc
          tiebreaker, pages with search_after, and ALWAYS closes the PIT in a finally — including on
          abandoned enumeration. request.Page > 1 is rejected: cursor iteration and offset paging are
          mutually exclusive; PageSize is reinterpreted as the per-round-trip batch size.

    NOTE (THE EXPLICIT Open/Read/Close TRIPLE) exists for the resumable case — a long-running export
          that checkpoints its cursor across process restarts, which IAsyncEnumerable cannot express.
          SearchCursor.Token is OPAQUE and must never be parsed.

    NOTE (BUILT ON PIT, NEVER SCROLL): Elastic explicitly de-recommends the scroll API for deep
          pagination in favour of search_after + PIT.

IElasticSearchRawClientAccessor   (LAST-RESORT ESCAPE HATCH — NOT REGISTERED BY DEFAULT)
    .Client                          → Elastic.Clients.Elasticsearch.ElasticsearchClient { get; }

DateHistogramInterval   (enum)   Minute | Hour | Day | Week | Month | Quarter | Year
AggregationBucketRange  (readonly record struct)   Key: string, From: double?, To: double?

AggregationRequest   (abstract record, CLOSED hierarchy, internal subtype ctors)
    TermsAggregation          { Name, Field, Size, SubAggregations: IReadOnlyList<AggregationRequest> }
    CardinalityAggregation    { Name, Field }
    StatsAggregation          { Name, Field }
    DateHistogramAggregation  { Name, Field, Interval, SubAggregations }
    RangeAggregation          { Name, Field, Ranges: IReadOnlyList<AggregationBucketRange> }
    — static factories: Terms(...), Cardinality(...), Stats(...), DateHistogram(...), Range(...)

AggregationResult   (abstract record, CLOSED hierarchy)
    TermsResult          { Name, Buckets: IReadOnlyList<TermsBucket>, OtherDocCount: long }
    CardinalityResult    { Name, Value: long }
    StatsResult          { Name, Count: long, Min, Max, Average, Sum: double }
    DateHistogramResult  { Name, Buckets: IReadOnlyList<DateHistogramBucket> }
    RangeResult          { Name, Buckets: IReadOnlyList<RangeBucket> }

TermsBucket          (sealed record)          { Key: string, DocCount: long, SubAggregations: AggregationResultSet }
DateHistogramBucket  (sealed record)          { Key: DateTimeOffset, DocCount: long, SubAggregations: AggregationResultSet }
RangeBucket          (readonly record struct) { Key: string, DocCount: long }

AggregationResultSet   (sealed record)
    .Results                        → IReadOnlyDictionary<string, AggregationResult> { get; init; }
    .TryGetTerms(string name, out TermsResult result)                                → bool
    .TryGetCardinality(string name, out CardinalityResult result)                    → bool
    .TryGetStats(string name, out StatsResult result)                                → bool
    .TryGetDateHistogram(string name, out DateHistogramResult result)                → bool
    .TryGetRange(string name, out RangeResult result)                                → bool
    NOTE: The Try* accessors mirror the ElasticSearch client's own name-keyed, type-specific read model
          (GetStringTerms / GetAverage) rather than pretending one generic accessor can preserve bucket
          type. They return bool, not Result — "I asked for a terms agg and read it as terms" is a
          caller programming error surfaced at first test run, not an expected runtime failure.

SearchCursor          (readonly record struct)  { Token: string (OPAQUE — PIT id + last sort tuple),
                                                  ExpiresAt: DateTimeOffset }
    NOTE (Token ENCODING — VERIFIED Core-phase design, needed because the caller's original SearchFilter
          tree cannot be round-tripped by the cursor: its node constructors are internal to .Abstractions):
          Token is Base64 over a JSON `CursorState { PitId, KeepAliveSeconds, PageSize, QueryBase64,
          SearchAfter }`. QueryBase64 is the ALREADY-COMPILED ElasticSearch Query object (the output of
          ElasticSearchFilterCompiler, tenant scope included), serialized via the ES client's OWN
          `RequestResponseSerializer.Serialize`/`Deserialize<Query>` — never plain System.Text.Json —
          because Query's Union/container types depend on the SDK's own custom JSON converters to
          round-trip correctly. Reopening a cursor deserializes QueryBase64 back into a Query the same way.
CursorPage<TDocument> (sealed record)           { Hits: IReadOnlyList<SearchHit<TDocument>>,
                                                  NextCursor: SearchCursor?,
                                                  IsExhausted: bool  // NextCursor is null }

ElasticSearchErrors   (public static class)
    .InvalidCursor(reason)                       Error.Validation
                                                 "search.elasticsearch.invalid_cursor"
    .CursorExpired(expiredAt)                    Error.Conflict
                                                 "search.elasticsearch.cursor_expired"
    .AggregationFailed(aggregationName, reason)  Error.Unexpected
                                                 "search.elasticsearch.aggregation_failed"
    .SourceSerializerContextMissing(documentTypeName)
                                                 Error.Unexpected
                                                 "search.elasticsearch.source_serializer_context_missing"
    NOTE: CursorExpired is Conflict, not Validation — the token was well-formed and the PIT keep-alive
          lapsed; the caller did nothing wrong, the world moved. Maps to retry-from-scratch at the
          presentation layer.

ElasticSearchOptions   (sealed class — Options-pattern, validated at startup)
    public const string SectionName = "Search:ElasticSearch"
    .Nodes                        string[] ([Required], [MinLength(1)])
    .ApiKey                       string?
    .Username                     string?
    .Password                     string?
    .CertificateFingerprint       string?
    .AllowInvalidCertificates     bool     (default false; logs Warning 9217 at startup when true)
    .RequestTimeoutSeconds        int      ([Range(1,300)],            default 30)
    .PingTimeoutSeconds           int      ([Range(1,30)],             default 2)
    .MaxTotalHits                 int      ([Range(1,1000000)],        default 1000)   ← PARITY, not 10000
    .MaxFacetValues               int      ([Range(1,10000)],          default 100)
    .BulkMaxBytes                 int      ([Range(1048576,52428800)], default 10485760)
    .BulkMaxDocuments             int      ([Range(1,50000)],          default 2000)
    .PointInTimeKeepAliveSeconds  int      ([Range(10,3600)],          default 300)
    .ProbeCacheSeconds            int      ([Range(0,60)],             default 5)
    .NumberOfShards               int      ([Range(1,100)],            default 1)
    .NumberOfReplicas             int      ([Range(0,10)],             default 1)
    .RefreshIntervalSeconds       int      ([Range(-1,3600)],          default 1)
    .ValidateEngineVersionOnStart bool     (default true)

AddSharedKernelElasticSearchSearch(IConfiguration configuration)   → ElasticSearchBuilder
AddSharedKernelElasticSearchSearch(IConfigurationSection section)  → ElasticSearchBuilder
    .AddIndex<TDocument>(string readAlias, string writeAlias,
                         Action<SearchIndexDefinitionBuilder> configure)  → ElasticSearchBuilder
    .WithSourceSerializerContext(JsonSerializerContext context)           → ElasticSearchBuilder
    .AllowRawClientAccess()                                               → ElasticSearchBuilder
    .Build()                                                              → IServiceCollection
    NOTE: AddIndex<TDocument> registers scoped ISearchIndex<TDocument>, IAnalyticsSearch<TDocument>, and
          ICursorSearch<TDocument>.
    NOTE (WithSourceSerializerContext IS NOT OPTIONAL FOR TRIMMED/AOT CONSUMERS): the client sets
          IsAotCompatible for net8+ and disables reflection-based STJ by default, so document types must
          be covered by a source-generated JsonSerializerContext wired through DefaultSourceSerializer.
          Omitting it logs Warning 9221 at startup and yields
          ElasticSearchErrors.SourceSerializerContextMissing at first use.

ElasticSearchLog   (internal static partial class — [LoggerMessage], EventId sub-block 9200–9299)
    9200 Information  ElasticSearchClientConfigured           {NodeCount} {IndexCount}
    9201 Information  ElasticSearchEngineVersionVerified      {EngineVersion}
    9202 Error        ElasticSearchEngineVersionUnsupported   {EngineVersion} {SupportedRange}
    9203 Information  ElasticSearchIndexEnsured               {IndexName} {FieldCount} {MaxResultWindow}
    9204 Debug        ElasticSearchDocumentsIndexed           {IndexName} {DocumentCount} {RefreshMode}
    9205 Debug        ElasticSearchBulkCompleted              {IndexName} {DocumentCount} {ElapsedMs}
    9206 Warning      ElasticSearchBulkPartialFailure         {IndexName} {FailedCount} {TotalCount}
    9207 Warning      ElasticSearchRefreshWaitTimedOut        {IndexName} {ElapsedMs}
    9208 Debug        ElasticSearchSearchExecuted             {IndexName} {HitCount} {TotalHits} {Accuracy} {TookMs}
    9209 Warning      ElasticSearchRequestRejected            {IndexName} {Reason}
    9210 Warning      ElasticSearchTenantScopeMissing         {IndexName}
    9211 Information  ElasticSearchAliasCutoverCompleted      {StagingIndexName} {LiveIndexName}
    9212 Information  ElasticSearchStagingIndexDeleted        {StagingIndexName}
    9213 Debug        ElasticSearchPointInTimeOpened          {IndexName} {KeepAliveSeconds}
    9214 Debug        ElasticSearchPointInTimeClosed          {IndexName} {BatchCount}
    9215 Warning      ElasticSearchPointInTimeCloseFailed     {IndexName}
    9216 Warning      ElasticSearchRawClientAccessEnabled     (startup)
    9217 Warning      ElasticSearchCertificateValidationDisabled  (startup)
    9218 Information  ElasticSearchClusterYellowAccepted      {IndexName}
    9219 Warning      ElasticSearchProbeDegraded              {IndexName} {ClusterStatus} {IndexAddressable} {Searchable}
    9220 Warning      ElasticSearchSchemaFingerprintMismatch  {IndexName} {Expected} {Actual}
    9221 Warning      ElasticSearchSourceSerializerContextMissing  {DocumentTypeName}
    9222 Error        ElasticSearchEngineFault                {Operation} {IndexName} {StatusCode}
    9223 Debug        ElasticSearchAggregationExecuted        {IndexName} {AggregationCount} {TookMs}
```

---

## Implementation Rules

### The seam rule (the whole design in one line)

- **`SharedKernel.Search.Abstractions` contains no type that either provider cannot implement completely and correctly.** If implementing a member would require one adapter to throw, degrade, approximate, or no-op, that member does not belong in `.Abstractions`. Every proposed addition to the neutral surface is checked against this rule in review, and `SearchFieldDefinition` is the single type to guard hardest — every future "just one more knob" request (analyzer, normalizer, tokenizer, ranking rule) is a lie about the other engine.

### Hard violations (never do these)

- `SharedKernel.Search.Abstractions` taking **any `PackageReference`** — it references only `SharedKernel.Primitives` and `SharedKernel.Contracts` as `ProjectReference`s. Adding an engine SDK, a `Microsoft.Extensions.*` package, or a hashing package here is a hard violation; `System.Security.Cryptography` and `System.Text.Json` are in-box on `net10.0`.
- `SharedKernel.Search.Meilisearch` and `SharedKernel.Search.ElasticSearch` referencing each other, in either direction, at project or type level. Shared shape is duplicated deliberately; extracting a shared base or a `.Core` would couple the two providers and defeat the swap-independence the split exists to protect.
- Either provider package exposing a type from the **other** engine's SDK on its public surface.
- Referencing `03.Domain`, `05.Application`, `06.Persistence`, `07.Messaging`, `12.Security`, or any other capability domain from any `09.Search` package.
- Declaring `IAnalyticsSearch<TDocument>`, `ICursorSearch<TDocument>`, `IInstantSearch<TDocument>`, or `ITenantSearchTokenIssuer` in `.Abstractions` — provider-package placement is what turns a provider swap into a compile error rather than a startup resolution error.
- A **discard (`_ =>`) arm** in either provider's `SearchFilter` translation switch (nor in the structurally identical `AggregationRequest` naming switch, nor in either `SearchValueKind` rendering switch). The hierarchy is closed precisely so the switch is exhaustive by inspection; see "Reconciling switch exhaustiveness with `TreatWarningsAsErrors`" below for what actually backstops a future ninth node once Docs-phase established that the compiler cannot prove exhaustiveness here regardless of how the switch is written.
- **Silently degrading, dropping, coercing, or post-filtering in memory** any clause the engine cannot express. Every rejection is a `Result` failure returned **before any I/O**. A dropped filter clause in a multi-tenant system is a data breach, not a degraded UX.
- Adding `Score` (or any numeric relevance value) to `SearchHit<TDocument>` or `SearchResults<TDocument>`. `Rank` is the only portable ordering signal. Likewise no neutral `Boost`, no `ScoreThreshold`, no `MinimumShouldMatch`, no `Fuzziness`, no `TypoTolerant` flag.
- Making `TenantScope` optional, nullable, defaulted, or a member of `SearchRequest`. It is a required separate parameter on every read and every filtered write, injected by the adapter as the **outermost** `AND` clause after the caller's filter has been translated.
- Passing a tenant predicate through the caller-supplied `SearchFilter` tree instead of `TenantScope`.
- Making `SearchWriteConsistency` optional or defaulted on any write method, or exposing ElasticSearch's `refresh=true` as a third enum value — it forces an immediate cluster-wide refresh that disturbs other in-flight requests and has no Meilisearch analogue.
- Adding an optimistic-concurrency `Version` member that one adapter ignores. Ordering must be guaranteed upstream by partitioning the change stream on `DocumentId`; that requirement is documented, not faked in the type system.
- Collapsing bulk per-item failures into one opaque `Error`. `IndexManyAsync`/`DeleteManyAsync` return a **success** `Result` carrying a `SearchBulkReceipt` whose `Failures` list is the actionable output; `Result.Failure` is reserved for "the request itself did not execute".
- Wrapping `EnumerateAsync` or `StreamAsync` in `Result` — both return `IAsyncEnumerable<T>` directly, per the `06.Persistence` P-149 / `08.Storage` P-265 precedent, and surface mid-stream faults as `SearchStreamException` from `MoveNextAsync`.
- Using `EnumerateAsync`'s ordering as a correctness assumption, or building a resumable export on positional state. Ordering is **unspecified** until a Tests-phase task verifies it against a real container on both engines.
- Adding a nested / object-array path filter node to `SearchFilter`. The portable technique is flattening into a precomputed composite filterable field at document-mapping time; a neutral nested node is silently wrong on one engine.
- Adding a string- or boolean-bounded range. `Between` accepts `Int64`, `Double`, and `DateTimeOffset` bounds only and throws `ArgumentException` otherwise.
- Constructing an ad-hoc `Error` inline in either provider — all errors come from `SearchErrors`, `MeilisearchErrors`, or `ElasticSearchErrors`. Naming a non-existent factory (`Error.Failure`, `Error.Forbidden`) or returning `Error.None` from any method are both violations.
- Using `Error.BusinessRule` anywhere in this domain — it maps to HTTP 422 and denotes a domain-rule violation; nothing in a capability package is a domain rule.
- Implementing `IHealthCheck`, or referencing `Microsoft.Extensions.Diagnostics.HealthChecks`, anywhere in `09.Search`. `ProbeAsync` returning `Result<SearchIndexHealth>` is the primitive; the adapter is `13.ServiceDefaults`'s responsibility.
- `13.ServiceDefaults`'s eventual adapter treating `SearchIndexHealth.PendingWriteCount == null` as unhealthy, or failing readiness on a deep write backlog. A deep backlog means results are **stale**, not **unavailable**.
- Config section paths as bare literals at a `GetSection` call site — always the `public const string SectionName` on the options type (`SK0022`).
- Raw string literals for field names, provider names, or OTel tag keys — always `SearchWellKnown` or a per-document field-constants class (`SK0024`).
- Injecting raw `HttpClient` in a production constructor, or calling `new HttpClient()` — the Meilisearch client is constructed from a named `IHttpClientFactory` client (`P-159`/`SK0013`).
- Using `NEST` or `Elasticsearch.Net` anywhere in the platform (`SK0025`).
- Reflection of any kind in this domain's own code — no `Activator.CreateInstance`, no `Assembly.Load`, no `Type.GetProperty`/`GetMethod`, no `MakeGenericMethod`/`MakeGenericType`, no `dynamic`. Document keys come from `ISearchDocument.DocumentId`, filter values from the closed `SearchValue` union.
- Any static mutable state.
- Registering both providers against the **same** `TDocument`. The last registration silently wins for the neutral interfaces. A service needing both registers them against **different** document types.
- Adding `<IsAotCompatible>true</IsAotCompatible>` to any `09.Search` `.csproj` — per root policy the tag is too coarse-grained.

### Reconciling switch exhaustiveness with `TreatWarningsAsErrors` (resolved at Docs phase, 2026-07-20)

`SK.09.Core`'s changelog flagged two/three "intentional CS8509/CS8524 no-discard-arm warnings" on the closed-hierarchy switches in both providers (the `SearchFilter` translation switch and the `SearchValueKind` rendering switch in each of `MeilisearchFilterCompiler`/`ElasticSearchFilterCompiler`, plus the `AggregationRequest` naming switch in `ElasticSearchAnalytics<TDocument>`), explicitly noting they were expected "to become a hard error once Docs-phase `TreatWarningsAsErrors` lands." Docs phase landed `TreatWarningsAsErrors=true` on all three production `.csproj` files and had to resolve that exact collision. The premise behind the Core-phase note turned out to be only half right, and the full finding is recorded here so it is never re-litigated or "fixed" with a discard arm in a future session.

**What was verified empirically (a minimal scratch repro, not guessed at):** a switch expression over an `abstract record` base with every known `sealed` subtype covered by a type-pattern arm still produces CS8509 ("the pattern `_` is not covered"). Adding an explicit `null => throw ...` arm does **not** resolve it — it merely shifts the compiler's reported missing pattern from `_` to `not null`. This proves the diagnostic is not about nullability at all: the C# compiler does not perform closed-world exhaustiveness analysis over a sealed-subtype class hierarchy, no matter how many subtypes are covered, how `private protected`/`internal` their constructors are, or how "closed by construction" the hierarchy is by this domain's own design intent. There is no pattern-arm combination that satisfies the compiler here — this is a permanent C# language limitation, not a gap in how `SearchFilter`/`AggregationRequest`'s switches were written. `SearchValueKind`'s CS8524 is the enum analogue of the same underlying gap: an enum's underlying integral representation permits any value, named or not, so covering all five declared members can never read as "exhaustive" to the compiler either.

**The resolution — narrowly downgrade, never discard, never blanket-suppress:** both provider `.csproj` files (never `SharedKernel.Search.Abstractions`, which owns no translation switch) carry:

```xml
<WarningsNotAsErrors>$(WarningsNotAsErrors);CS8509;CS8524</WarningsNotAsErrors>
```

This is deliberately **not** `<NoWarn>` and deliberately **not** a `#pragma warning disable` wrapped around each switch. `WarningsNotAsErrors` keeps both diagnostics fully **visible** in every build log — nobody can forget these five switches are not compiler-provably exhaustive — while preventing them alone from failing the build; every other warning in either project remains fatal under `TreatWarningsAsErrors`. Each of the five affected switch sites (`MeilisearchFilterCompiler.Compile`/`.FormatValue`, `ElasticSearchFilterCompiler.Compile`/`.ToFieldValue`, `ElasticSearchAnalytics<TDocument>.GetName`) carries an inline comment cross-referencing this section rather than leaving the `.csproj` entry as an unexplained artifact.

**What actually backstops a future ninth node (correcting the Core-phase premise):** it was never going to be a compile-time catch — that was an untested assumption. The real backstop is the **runtime `SwitchExpressionException`** every C# switch expression throws automatically when a value matches no arm. A ninth `SearchFilter`/`AggregationRequest` subtype, or a genuinely out-of-range `SearchValueKind` value, introduced without updating the corresponding switch fails **loud** the very first time that code path executes — never silently, never coerced, never dropped — which is the actual property "fail loud, never degrade" requires, even though it surfaces at first execution rather than at `dotnet build`. This is why a discard arm remains prohibited even though it would also "throw loudly" if written as `_ => throw new NotSupportedException(...)`: a discard arm is behaviourally identical to the implicit `SwitchExpressionException` at runtime while additionally hiding the compiler's own honest "I cannot prove this is exhaustive" signal from every future reader of the switch — the `WarningsNotAsErrors` entry preserves that signal, a discard arm would erase it for no runtime benefit.

### Raw client accessors

`IMeilisearchRawClientAccessor` and `IElasticSearchRawClientAccessor` are the genuine last resort (ES percolators and `function_score`; Meilisearch hybrid/vector and federated multi-search). The **primary** provider-exclusive mechanism is the four typed contracts above, over neutral models. The accessors are gated three ways and none may be relaxed:

1. Registered **only** when the composition root calls `.AllowRawClientAccess()` on the provider builder — an explicit, greppable, reviewable act.
2. That call logs a startup `Warning` (`9115` / `9216`), mirroring the `AddStaticServiceDiscovery()` and `AllowInvalidCertificates` dev-only-warning precedents.
3. A `00.Governance` architecture test asserts no type inside this repo consumes either accessor.

**THE HATCH BYPASSES TENANT SCOPING.** `TenantScope` injection happens inside the neutral translator; a raw client call receives none of it. A multi-tenant service using an accessor **must** apply its own tenant predicate. This warning is stated in capitals on both accessors' XML docs.

### Cross-domain work this design requires

- **`13.ServiceDefaults`** — `HealthCheckNames.Search = "search"`, `HealthCheckTags.Search = "search"`, and `HealthChecks/SearchReadinessHealthCheckExtensions.AddSearchReadinessCheck(this IHealthChecksBuilder builder, string indexName, string name = HealthCheckNames.Search)` wrapping an `internal sealed class SearchReadinessHealthCheck(ISearchIndexProvisioner provisioner, string indexName)`, tags `[Ready, Search]`. It resolves **only `ISearchIndexProvisioner`** and takes `indexName` as an explicit caller-supplied parameter — never a concrete provider options type, exactly the rule that lets `AddStorageReadinessCheck` work unmodified against `.S3` or `.Obs`. Healthy iff `Reachable && IndexAddressable && Searchable`; `PendingWriteCount` is emitted as a gauge, never a failure. Plus `Telemetry/SearchTelemetryExtensions.WithSearchTelemetry(this IHostApplicationBuilder)` — a fourth sibling to the Messaging/Caching/Application telemetry extensions — doing string-name-only `AddSource`/`AddMeter` wiring against a `private const string SearchInstrumentationName = "SharedKernel.Search";` declared on the extension class, matching `ApplicationTelemetryExtensions`'s `SharedKernelApplicationInstrumentationName` and `CachingTelemetryExtensions`'s `CachingMeterName`, never a retyped literal at each call site. That constant must be byte-identical to `SearchWellKnown.ActivitySourceName` / `SearchWellKnown.MeterName`, which `13.ServiceDefaults` cannot reference because it deliberately takes **no `ProjectReference` to `09.Search`** — the identity is held by convention and a code-review check, which is exactly why it must be one named constant on each side rather than four hand-retyped literals.
- **`16.Testing`** — a `MeilisearchContainerFixture` and an `ElasticsearchContainerFixture` in `Containers/`, plus a new `Search/` folder holding `InMemorySearchIndex<TDocument>`, `InMemorySearchIndexProvisioner`, and `InMemorySearchProviderDescriptor`. See Test Rules for the exact shape and constraints.
- **`00.Governance`** — `SearchTopologyRules` (NetArchTest, no SK ID) modelled one-for-one on `StorageTopologyRules`, restating both documented gotchas in its header comment: `NotHaveDependencyOn(term)` compares `term` against each scanned type's dependency **namespaces** with a `StartsWith` match and **no trailing dot on either side**; and a package must never be checked against its own identifying term, which produces a guaranteed false positive against every type it declares. Plus a new `SharedKernelLayeringRules.SearchReferencesOnlyCoreAndContracts(Assembly)` — no such method exists today and Search sibling independence has zero layering coverage — and an architecture test asserting no in-repo type consumes either raw-client accessor. Two new analyzers: **`SK0024`** ("Raw string literal in a search field-name position") and **`SK0025`** ("NEST or Elasticsearch.Net usage"). Both take the next sequential IDs and **do not open an `09xx` block**, following the `SK0023` precedent that explicitly declined to open an `08xx` block for the platform's first storage-domain rule.

---

## DI Registration (expected shape)

```csharp
// Meilisearch (BFF / instant-search service):
services
    .AddSharedKernelMeilisearchSearch(configuration)
    .AddIndex<ProductSearchDocument>("products", index => index
        .PrimaryKey(ProductSearchFields.DocumentId)
        .TenantField(ProductSearchFields.TenantId)
        .Field(ProductSearchFields.Name,   SearchFieldKind.Text,    searchable: true)
        .Field(ProductSearchFields.Status, SearchFieldKind.Keyword, filterable: true, facetable: true)
        .Field(ProductSearchFields.Price,  SearchFieldKind.Decimal, filterable: true, sortable: true))
    .WithTenantTokens()          // opt-in: engine-enforced per-tenant search tokens
    .Build();

// ElasticSearch (reporting / analytics / export service):
services
    .AddSharedKernelElasticSearchSearch(configuration)
    .AddIndex<OrderSearchDocument>(readAlias: "orders", writeAlias: "orders-write", index => index
        .TenantField(OrderSearchFields.TenantId)
        .Field(OrderSearchFields.Reference, SearchFieldKind.Keyword, filterable: true)
        .Field(OrderSearchFields.PlacedOn,  SearchFieldKind.DateTimeOffset, filterable: true, sortable: true))
    .WithSourceSerializerContext(OrderSearchJsonContext.Default)   // required for trimmed/AOT consumers
    .Build();

// In application code, inject the abstractions — never an engine SDK type:
//   ISearchIndex<TDocument>      → index / delete / bulk / search / get / count / enumerate
//   ISearchIndexProvisioner      → ensure / exists / delete / cutover / probe
//   ISearchProviderDescriptor    → ceilings + zero-I/O SearchRequest pre-flight validation
//   SearchQuery.For<TDocument>() → build a SearchRequest (static, not DI-registered)

// Provider-exclusive capabilities are injected by their PROVIDER-PACKAGE-DECLARED contract.
// Referencing one of these takes a compile-time dependency on that provider package — which is the
// point: a provider swap becomes a BUILD ERROR listing every non-portable call site.
//   IAnalyticsSearch<TDocument> / ICursorSearch<TDocument>   → SharedKernel.Search.ElasticSearch only
//   IInstantSearch<TDocument> / ITenantSearchTokenIssuer     → SharedKernel.Search.Meilisearch only
```

`SharedKernel.Search.Abstractions` ships **no DI extensions** — it is a pure abstraction library. All registration lives in the provider packages.

`ISearchIndex<TDocument>` and the provider-exclusive per-document contracts are **scoped**; the engine client (`MeilisearchClient` / `ElasticsearchClient`), `ISearchIndexProvisioner`, and `ISearchProviderDescriptor` are **singletons** — both engine clients are thread-safe and pool their own resources, so scoped or transient registration is a violation.

**GOTCHA (VERIFIED Tests-phase — was a real Core-phase DI-wiring bug, now fixed): `MeilisearchIndexProvisioner`, `ElasticSearchIndexProvisioner`, and `MeilisearchTenantTokenIssuer` all take a raw `TOptions` constructor parameter, not `IOptions<TOptions>`.** `AddValidatedOptions` only ever registers `IOptions<TOptions>` in the container, never the unwrapped type — so registering these three via the plain `services.AddSingleton<TInterface, TImplementation>()` shorthand fails to resolve `ISearchIndexProvisioner`/`ITenantSearchTokenIssuer` in **every** consuming service, not just tests. Both provider builders' `Build()` methods now register these three via an explicit factory lambda that unwraps `sp.GetRequiredService<IOptions<TOptions>>().Value` — the same pattern `AddIndex<TDocument>()` already used correctly. Any future type in either provider package whose constructor takes a raw options type must be registered the same way; the open-generic shorthand is only safe for types that accept `IOptions<TOptions>` directly.

A service may register **both** providers, but only against **different `TDocument` types** — e.g. Meilisearch for the customer-facing catalogue index and ElasticSearch for the reporting index. Registering both providers for the **same** `TDocument` is a hard violation: `ISearchIndex<TDocument>` is an unkeyed registration, so the second call silently wins and the first provider becomes unreachable. **CONFIRMED against real compiled code at Published phase (`consumer-verify.BothProviders`):** the collision is not limited to `ISearchIndex<TDocument>` — the non-generic `ISearchIndexProvisioner`/`ISearchProviderDescriptor` singletons collide too, independent of `TDocument`, so provisioning and pre-flight-validation calls silently target the last-registered provider as well. **Neither `MeilisearchSearchBuilder` nor `ElasticSearchBuilder` offers a keyed-registration overload** (unlike `08.Storage`'s `AddKeyedSingleton` pattern for `IFileStorage`/`IBlobUriGenerator`) — there is currently no supported side-by-side path for two providers against one `TDocument`; the only safe pattern is one `TDocument` (and index) per provider.

`IMeilisearchRawClientAccessor` / `IElasticSearchRawClientAccessor` are registered **only** when the composition root calls `.AllowRawClientAccess()`, which logs a startup `Warning`. Neither is registered by default.

---

## AOT Compatibility

- `ISearchDocument`, `ISearchIndex<TDocument>`, `ISearchIndexProvisioner`, `ISearchProviderDescriptor`, and `IQueryBuilder<TDocument>` are interfaces — AOT-safe by definition.
- All `Models/` types are `sealed record` / `readonly record struct` over BCL primitives — AOT-safe.
- `SearchFilter`'s closed hierarchy is walked by exhaustive C# pattern matching resolved entirely at compile time — no visitor registry, no `dynamic`, no reflection, no `NotSupportedException` path.
- `SearchValue`'s closed five-kind union replaces the `object`/`dynamic` filter-value shape the Meilisearch SDK itself uses — no boxing-plus-runtime-type-switch, no `Microsoft.CSharp` binder machinery.
- `SearchQueryBuilder<TDocument>` is an immutable sealed class returning new instances from readonly fields — no expression trees, no `IQueryable`, no runtime code generation.
- `SearchIndexDefinition.Fingerprint` uses `System.Security.Cryptography` SHA-256 over a deterministically-built UTF-8 string — in-box on `net10.0`, no reflection, AOT-safe.
- `SearchErrors` / `MeilisearchErrors` / `ElasticSearchErrors` are static factories returning `Error` values — AOT-safe.
- `MeilisearchOptions` / `ElasticSearchOptions` bind via `Microsoft.Extensions.Options` — AOT-compatible; verify on each upgrade.
- **`MeiliSearch` 0.20.0 is a documented non-AOT-safe dependency.** Its csproj sets no `IsAotCompatible`/`IsTrimmable`; it targets `netstandard2.0` so it cannot carry modern trim annotations; its `JsonSerializerOptions` are `internal` (no `JsonSerializerContext` seam, no custom converter, hard-coded camelCase); its `ISearchableJsonConverterFactory` uses `MakeGenericType` + `Activator.CreateInstance` on **every** `SearchAsync<T>` call; and its filter property is `dynamic`. This is exactly the "non-AOT-safe third party placed behind an abstraction" case the root brain's AOT guidance sanctions — encapsulating it behind `ISearchIndex<TDocument>` limits the AOT blast radius to the registration + provider-implementation path. Note that `MakeGenericType` is **invisible to `SK0012`**, which matches `MakeGenericMethod` only, so this is not analyzer-detectable and must be held by documentation.
- **`Elastic.Clients.Elasticsearch` 9.4.2 takes the opposite position and is already annotated** — its csproj sets `<IsAotCompatible>true</IsAotCompatible>` for net8+ and configures STJ so reflection-based serialization is off by default. Consuming it therefore does not fight this repo's policy: we inherit an AOT-annotated dependency without adopting the coarse-grained flag ourselves. The consequence is that `.WithSourceSerializerContext(...)` is a **required** seam for trimmed/AOT consumers — omitting it breaks document serialization at runtime, not at build time, which is why it also warns at startup (`9221`) and has a dedicated `Error` factory.
- No `Activator.CreateInstance`, no `Assembly.Load`, no `MakeGenericMethod`/`MakeGenericType`, no `Type.GetProperty`/`GetMethod`, no `dynamic` in this domain's own code. Document keys come from `ISearchDocument.DocumentId` (a self-supplied surface), never from an attribute scan or convention-based property lookup.
- No `<IsAotCompatible>true</IsAotCompatible>` tag on any `09.Search` `.csproj`, per root policy — the tag activates trim/AOT analyzers globally and forces AOT compliance on the entire project, which is too coarse-grained.

---

## Test Rules

- Unit tests for each package live in its own nested `*.Tests` folder (e.g. `09.Search/SharedKernel.Search.Abstractions/SharedKernel.Search.Abstractions.Tests/`).
- **Standard test package set** (all test `.csproj` files): `xunit` 2.9.3, `xunit.runner.visualstudio` 2.8.2, `Microsoft.NET.Test.Sdk` 17.13.0, `coverlet.collector` 6.0.4, `FluentAssertions` 8.4.0, `NSubstitute` 5.3.0. Every test project references `16.Testing/SharedKernel.Testing` and includes a `GlobalUsings.cs` with `global using Xunit;` — `ImplicitUsings` does not auto-import xUnit attributes.
- `SharedKernel.Search.Abstractions.Tests`: `SearchErrors` returns the correct `Error` kind and code for every factory (and no factory returns `Error.None` or uses `Error.BusinessRule`); `SearchFilter` factories build the expected node shapes and `Between` throws `ArgumentException` for `String`/`Boolean` bounds; `SearchValue` kind-checked accessors throw on a kind mismatch and `.From(Guid)` produces the canonical `"D"` form; `SearchQueryBuilder<TDocument>` is immutable (every method returns a new instance and the source builder is unmutated) and repeated `Where(...)` calls **AND** rather than replace; `Build()` rejects each provider-independent invariant violation; `TenantScope.Of` throws on null/whitespace and `TenantScope.None` is `string.Empty`; `SearchIndexDefinition.Fingerprint` is stable across field **declaration order** and changes when any field name, kind, role, `TenantField`, or ceiling changes; `ToPagedList()` fails with `TotalHitsNotExact` / `TotalHitsOverflow` / `InvalidSearchRequest` for each guard and succeeds only for an `Exact` in-range result; reflection-based `ContractShapeTests` lock `EnumerateAsync`'s `IAsyncEnumerable<TDocument>` return shape and the mandatory non-defaulted `SearchWriteConsistency` / `TenantScope` parameters against silent regression (mirroring `06.Persistence`'s and `08.Storage`'s `ContractShapeTests` precedent).
- **A shared behavioural conformance suite is the real contract, not the interface.** The closed hierarchy makes *adding a node* a compile error on both providers; it does **not** make semantic drift a compile error. Both provider test projects run the same fixed-corpus suite against their own real container and must produce identical result sets for: inclusive vs exclusive range bounds, empty-operand `And`/`Or`, `In` with a single value, `Not` nesting and precedence, quote/backslash escaping in string values, `DateTimeOffset` bounds, and tenant-filtered facet counts. This suite is what keeps the two independently-written filter compilers honest.
- Provider behavioural tests run against a **real engine container** via `16.Testing` — never a mocked engine client for behavioural coverage. Assert: index provisioning plus idempotent re-`EnsureIndexAsync`; upsert → search round-trip under **both** `SearchWriteConsistency` values; bulk with a deliberately-invalid document producing a **success** `Result` with a populated `Failures` list; delete-by-id, delete-by-filter, and clear; filtered + sorted + faceted + paged search; highlighting projection into `SearchHit.Highlights`; `GetAsync` tenant isolation (tenant B cannot read tenant A's document by id); `CountAsync` exactness; `EnumerateAsync` yielding the full corpus via `await foreach` without materializing an intermediate list, with cancellation mid-enumeration stopping further yields; staging → bulk-load → `CutoverAsync` → live-index-serves-new-data, including `DeleteStagingAfterCutover` both ways; and `ProbeAsync` healthy plus each degraded axis.
- **Fail-loud tests are mandatory, not optional.** For every rejection path — filter/sort/facet on an undeclared field, over-ceiling pagination, over-cap facet count, invalid `DocumentId` charset, `TenantScope.None` against a `TenantField`-declaring index — assert both that the correct `Error` is returned **and that no I/O occurred**. A test that only asserts the error would pass against an implementation that silently degrades and then reports.
- **Sanctioned mocking exception:** engine status-code → `SearchErrors` mapping assertions (404/401/403/409/5xx) may substitute the engine client via `NSubstitute` instead of a real backend — inducing a real 403 or a cluster fault without live credential/cluster setup is impractical. Behavioural, round-trip, and conformance coverage is never mocked.
- **`16.Testing`'s search fixtures do not exist yet and must be built** — verified on disk at design time: `16.Testing/SharedKernel.Testing/Containers/` holds exactly four fixtures (PostgreSQL, Redis, RabbitMQ, MinIO), and neither `16.Testing/CLAUDE.md` nor its `state-map.md` mentions search in any state. A future session must **re-verify this on disk** before assuming it either way. Both new fixtures follow the existing four's template exactly: `sealed`, `IAsyncLifetime`-only (never a constructor blocking on `.Result`/`.Wait()`), a private `_started` guard, connection properties throwing `InvalidOperationException` before `InitializeAsync` completes, a **pinned image tag** (never `:latest`), and **flat scalar connection properties with no `ProjectReference` to `SharedKernel.Search.Abstractions`**.
- `ElasticsearchContainerFixture` uses `Testcontainers.Elasticsearch` `4.13.0` and **must explicitly `.WithImage(...)` a 9.x server image** — the module default is `elasticsearch:8.6.1` and a 9.x client does not support an 8.x server; using the module default is an unsupported pairing that produces confusing partial failures rather than a clean rejection. The module runs ES 8-secure-by-default (HTTPS + self-signed cert + basic auth) with no `xpack.security.enabled=false`, so the documented path is `CertificateValidations.AllowAll` client-side plus an explicit ping-poll wait strategy (known readiness race, testcontainers-dotnet#955).
- `MeilisearchContainerFixture` is **hand-rolled on the generic `ContainerBuilder`** — there is no `Testcontainers.Meilisearch` package (nuget.org returns 404 and Meilisearch is absent from the official .NET module list). Port 7700, `MEILI_MASTER_KEY` (≥16 bytes), `MEILI_NO_ANALYTICS=true`, and a wait strategy on `GET /health` — the only route unprotected by the master key, which makes it correct regardless of master-key configuration. **CONFIRMED (`SK.09.Tests` real-backend session, 2026-07-20, and re-confirmed at Docs phase):** the community `getmeili/meilisearch:v1.20.0` image supports the full `MeiliSearch` `0.20.0` SDK surface end-to-end — all 92 `SharedKernel.Search.Meilisearch.Tests` (T-10–T-17) pass against it, including provisioning, cutover, tenant tokens, and instant search. The Design-time "unconfirmed, the SDK repo's own CI has moved to an enterprise image" caveat is resolved and no longer an open question.
- **Version-alignment decision required at Scaffold phase:** the four existing `16.Testing` fixtures pin `Testcontainers.*` at `4.1.0`; adding `Testcontainers.Elasticsearch` `4.13.0` lifts the transitive `Testcontainers` floor. Scaffold must either bump all four existing pins to `4.13.0` or pin the Elasticsearch module to a `4.1.x` release — explicitly, not incidentally.
- Both fixtures are consumed per test collection via a `[CollectionDefinition]` + `ICollectionFixture<T>` pair with a `const string Name` (never a retyped literal at each `[Collection(...)]` site), matching `16.Testing`'s container-fixture rule of one instance per test collection, never per test method — `08.Storage`'s `MinioCollection` is the exact template.
- `16.Testing` additionally gains `InMemorySearchIndex<TDocument>`, `InMemorySearchIndexProvisioner`, and `InMemorySearchProviderDescriptor` in a new `Search/` folder — the only `16.Testing` types that reference `SharedKernel.Search.Abstractions`, mirroring the `InMemoryFileStorage`/`InMemoryBlobUriGenerator` precedent. `sealed`, `ConcurrentDictionary`-backed (xUnit parallelizes collections), no mocking framework, no `Task.Delay`/`Thread.Sleep`, and evaluating the **full** `SearchFilter` tree in LINQ so consuming services can unit-test their query construction and tenant scoping with zero containers.
- Options-validation tests: valid config registers without throw; a missing required field fails at startup. Resolving `IOptions<TOptions>.Value` directly is sufficient to trigger `ValidateDataAnnotations()` — no `IHost` needed — because `AddValidatedOptions`'s `IValidateOptions<T>` runs on first `.Value`/`.CurrentValue` access regardless of whether `.ValidateOnStart()`'s eager host-startup check ever fires.
- DI registration tests: `ISearchIndex<TDocument>` / `ISearchIndexProvisioner` / `ISearchProviderDescriptor` resolve; the engine client resolves as a singleton; provider-exclusive contracts resolve **only** from their own provider's builder; raw-client accessors do **not** resolve unless `.AllowRawClientAccess()` was called. Use `ServiceCollection` + `BuildServiceProvider()` — no web host required. Note that the provider DI extensions deliberately do not register `ILogger<T>` themselves (that is the consuming host's responsibility), so a DI-only test must additionally register `services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))` or the resolve throws `InvalidOperationException` for the unresolved `ILogger<T>` — the exact gotcha `08.Storage` hit.
- `ISearchProviderDescriptor.Validate` tests run with **zero containers** — that is the point of the method. Every rejection path is asserted here as well as in the container suite, so a consuming service can prove a provider swap is safe in a plain unit test.
- Each provider's test project carries a lightweight sibling-independence check (no `using SharedKernel.Search.{OtherProvider}` anywhere in its own source/tests). Implement it as a `[CallerFilePath]`-anchored scan of the package folder for `using`-directive **lines** specifically (trimmed-line prefix match, not a whole-file substring search) — a naive `string.Contains` over full file text false-positives on the test's own descriptive XML-doc prose. The authoritative enforcement is `00.Governance`'s `SearchTopologyRules`, outside this domain's own suite.
- **Two behaviours were verified against real containers by T-26 (2026-07-20) — both CONFIRMED, findings recorded, contract deliberately unchanged for (1):** (1) `EnumerateAsync` ordering on both engines is **insertion order, not id-ascending** — confirmed by seeding a deliberately out-of-id-order batch and observing both engines' walks return it in seed order; the contract's "unspecified, do not rely on it" language is **retained**, not strengthened, since this is an observed implementation detail rather than a documented engine API promise (see the full finding on `ISearchIndex<TDocument>.EnumerateAsync`'s NOTE above); (2) facet-count correctness under a shared-index tenant model — the tenant filter **is** applied before faceting on both engines, confirmed by a tenant-scoped faceted search whose counts exclude the other tenant's documents on both Meilisearch and ElasticSearch. No cross-tenant cardinality leak found.
- **Genuine cross-provider semantic divergence found and documented, not papered over (T-26, 2026-07-20):** `SearchFilter.Any()` with zero operands compiles to `"()"` in Meilisearch's filter-expression DSL, which the engine's parser rejects as invalid syntax — the adapter correctly surfaces this as `SearchErrors.EngineFault` (`IsSuccess = false`). ElasticSearch's equivalent, an empty `BoolQuery.Should` array, is valid and evaluates as **match-all** (`IsSuccess = true`, unfiltered result set). Both behaviours are correct translations of each engine's own native semantics — this is not a bug on either side, but a caller composing `SearchFilter.Any()` with a potentially-empty operand list must not assume identical behaviour across a provider swap; guard against an empty operand list before calling `.Any()` if portable behaviour is required.
- **Tests-phase complete — all 26 tasks, 345 tests, 0 failures** (155 `SharedKernel.Search.Abstractions.Tests`, 92 `SharedKernel.Search.Meilisearch.Tests`, 98 `SharedKernel.Search.ElasticSearch.Tests`). The 11 real-backend tasks (T-13–T-17, T-21–T-26) ran against real Docker containers via `16.Testing`'s `MeilisearchContainerFixture`/`ElasticsearchContainerFixture` (P-275) once that blocker cleared, and surfaced **five genuine production defects, all fixed, none deferred:** (1) `MeilisearchIndexProvisioner.IndexExistsAsync`/`ProbeAsync`'s `IndexAddressable` check called `GetIndexAsync`, which the SDK never throws from for any uid, real or fake (confirmed via raw HTTP that the REST layer correctly 404s but the SDK method silently returns a synthetic `Index` object) — fixed by switching both to `_client.Index(uid).GetSettingsAsync(ct)`, which genuinely throws, with a dual catch (`MeilisearchApiError` via `IsNotFound`, and `HttpRequestException` where `StatusCode == NotFound`); (2) `MeilisearchResultMapper.MapFacets` threw `NullReferenceException` on every search that did not request facets, because the SDK returns `null` (not an empty dictionary) for `facetDistribution`/`facetStats` in that case — fixed with nullable parameters plus a null/empty guard; (3) `MeilisearchIndexProvisioner.CutoverAsync` failed on a first-ever cutover because Meilisearch's `SwapIndexesAsync` genuinely requires **both** index names to pre-exist (confirmed via raw HTTP: swapping against a never-created name fails the task with `index_not_found`), while the documented stage→bulk-load→cutover consumer flow never separately creates the live index — fixed by pre-creating an empty placeholder live index via `CreateIndexAsync` before the swap when `IndexExistsAsync(liveIndexName)` reports absent; (4) `MeilisearchIndex.GetAsync` only caught `MeilisearchApiError` for a missing document, but the SDK's `GetDocumentAsync<T>` throws `HttpRequestException` (404) instead for this specific case — fixed by adding the missing catch clause; (5) `ElasticSearchIndexProvisioner.ProbeAsync` constructed an index-scoped `HealthRequest(indexName)`, which fails outright for a non-existent index and collapses `Reachable`/`IndexAddressable` into one signal instead of two — fixed with the parameterless cluster-wide `HealthRequest()`, matching the design's own "yellow cluster is healthy, index-addressability is a separate check" intent. No ad-hoc in-`.Tests`-project container setup was hand-rolled — both fixtures are consumed via `16.Testing` per the platform convention.
- **`InternalsVisibleTo` white-box-tests each provider's own internal types.** Both `SharedKernel.Search.Meilisearch.csproj` and `SharedKernel.Search.ElasticSearch.csproj` grant `InternalsVisibleTo` to their own nested `.Tests` project — mirroring the `06.Persistence.EfCore`/`13.ServiceDefaults`/`15.Integration.Webhooks` precedent — so `MeilisearchFilterCompiler`/`ElasticSearchFilterCompiler`, the `*RequestValidator` pre-flight validators, and the `*ProviderDescriptor`/`*RawClientAccessor` types stay `internal` (no public API surface expansion) while remaining directly unit-testable.
- **No-I/O proof technique for pre-flight validation tests (VERIFIED, load-bearing for T-11/T-19 and their real-backend successors):** `MeilisearchClient`/`ElasticsearchClient` ship no interface and cannot be substituted via `NSubstitute`. Instead, construct `MeilisearchIndex<TDocument>`/`ElasticSearchIndex<TDocument>` directly (reachable via `InternalsVisibleTo`) with a `client: null!` reference. `SearchAsync` validates the request via the internal `*RequestValidator` before ever dereferencing `_client`, so a clean rejection (no `NullReferenceException`) is structural proof no I/O was attempted. Pair every such test with one companion case that supplies a *passing* precondition (e.g. a valid `TenantScope` on a tenanted index) and asserts the **opposite** — that a `NullReferenceException` IS thrown once the guard is satisfied — proving the guard itself, not an unrelated short-circuit, is what stopped I/O in the rejection case.
- **Negative-compile probe — the mechanically honest way to prove "type X cannot be named from this compilation unit" (Published phase, `consumer-verify`):** to prove the capability-segregation claim (a Meilisearch-only composition root cannot name `IAnalyticsSearch<>`/`ICursorSearch<>`, and vice versa) as a genuine build-time fact rather than an assertion in prose, temporarily append a disallowed `using` plus a bare field declaration of the other provider's exclusive type directly into the real `consumer-verify` `Program.cs` — with **no** matching `<ProjectReference>` added to that project's `.csproj` — run `dotnet build`, capture the real compiler diagnostics, then immediately revert the probe. This produces a genuine `CS0234` ("the type or namespace name '{Provider}' does not exist in the namespace 'SharedKernel.Search'") plus `CS0246` ("the type or namespace name could not be found") pair, confirmed in both directions. **Rejected alternative:** building a scratch copy of the project in an unrelated directory with a broken relative `<ProjectReference>` path produces the *same* `CS0246` for every reference in the file, not just the intentionally disallowed one — it does not isolate the claim. The in-place, no-added-reference approach is the only one that proves the specific claim rather than a broken build in general.
- **ES SDK leaf-value types need their own reflection pass before writing filter-compiler assertions.** `Elastic.Clients.Elasticsearch.Number` has no public properties (FluentAssertions mis-renders failures as `Number{ }`) and distinguishes a long-backed value from a double-backed value for equality **even at an equal numeric value** (`(Number)10L != (Number)10.0`) — since `ElasticSearchFilterCompiler`'s numeric range path always produces a `double`-backed `Number`, range-bound assertions must compare against `(Number)10.0`, never `(Number)10L`. `FieldValue.String/Long/Double/Boolean` DOES have working value equality via `==`/`Equals`. Verify unfamiliar SDK leaf-value-type equality/property shape by reflecting the real compiled assembly (see the Core-phase SDK-shape-verification technique) before trusting an assertion — do not assume equality "just works" on an SDK type with no visible properties.

---

## Changelog

> Maintained by the search domain agent. One line per significant change.

- [2026-07-19] Domain brain initialized — packages (Abstractions + Meilisearch + ElasticSearch), technology stack (`MeiliSearch` 0.20.0 for BFF/fast, `Elastic.Clients.Elasticsearch` 9.4.2 for analytics/heavy, NEST/Elasticsearch.Net prohibited as EOL), the intersection-only neutral contract surface (`ISearchDocument`, `ISearchIndex<TDocument>`, `ISearchIndexProvisioner`, `ISearchProviderDescriptor`, `IQueryBuilder<TDocument>` over a closed 8-node `SearchFilter` AST and a closed five-kind `SearchValue` union), provider-package-declared exclusive contracts (`IInstantSearch`/`ITenantSearchTokenIssuer` on Meilisearch, `IAnalyticsSearch`/`ICursorSearch` on ElasticSearch) so a provider swap fails at compile time rather than at startup, mandatory non-defaultable `SearchWriteConsistency` and `TenantScope` parameters, three-valued `TotalHitsAccuracy` with a guarded `ToPagedList()` bridge to `04.Contracts`, no `Score` anywhere on the neutral surface, `MaxTotalHits` forced to 1000 parity on both providers, `ProbeAsync` as a probe primitive with no `IHealthCheck` (13.ServiceDefaults's concern), `SearchErrors` restricted to the six real `SharedKernel.Primitives` `Error` factories with `BusinessRule` deliberately unused, EventId sub-blocks (Abstractions 9000–9099 reserved and permanently unused, Meilisearch 9100–9199, ElasticSearch 9200–9299), three-way-gated raw-client escape hatches, plus AOT and test rules; contract locked as the Scaffold-phase basis — no implementation exists yet (root, user request)
- [2026-07-19] SK.09.Design (D-01–D-28) verified complete against this already-authored contract — one genuine gap found and closed: `ElasticSearchFilterCompiler`'s design carried no explicit "open Design risk" note for the unverified 9.4.2 `MatchQuery`/`TermQuery`/`NumberRangeQuery` constructor/initializer shape (D-23 required one); added directly under the `ElasticSearchFilterCompiler` block in Interface Contracts. No other task required a brain edit — every other D-01–D-28 decision was already present and load-bearing. All three packages' Design-phase state promoted to `●` in `state-map.md` (search-phase-implementer)
- [2026-07-19] SK.09.Scaffold (S-01–S-13) complete — all six `.csproj` files fleshed out to the locked reference/package shape; the four `Microsoft.Extensions.*` packages pinned to `10.0.9` on both providers (matching `08.Storage`/`11.Communication.Rest`/`16.Testing`'s most recent pins rather than the newer `10.0.10` on nuget.org, deliberately avoiding repo-wide version skew) — Technology Stack row updated from the prior "pinned at Scaffold-phase implementation time" placeholder to the confirmed version; `Elastic.Transport` transitive dependency confirmed at `8.0.1` with no conflict (first reference in the solution graph); `SearchStreamException`'s base-type question resolved and locked — `SharedKernel.Primitives` ships no exception hierarchy at all (only the separate `SharedKernel.Core` package does, which `.Abstractions` does not reference), so it derives directly from `System.Exception` per the contract's own documented fallback, not from any platform base exception. 99 namespace-only stub `.cs` files created (42 Abstractions + 20 Meilisearch + 37 ElasticSearch) with zero logic, matching the Interface Contracts folder headers exactly; net-new `SharedKernel.Search.Abstractions.Tests` created and registered in `Platform.SharedKernel.slnx`. All six projects build 0 errors/0 new warnings. No interface, model, DI-registration, or test-pattern changes — pure project-wiring and two Technology-Stack-table confirmations (search-phase-implementer)
- [2026-07-19] SK.09.Core (C-01–C-48) complete — full implementation of Abstractions/.Meilisearch/.ElasticSearch, 0 build errors. Both Design-phase open SDK-shape risks resolved against real compiled assemblies and their notes updated in place: ElasticSearchFilterCompiler's leaf-query object-initializer shape (with the `[SetsRequiredMembers]`/CS9035 gotcha on convenience constructors) and MeiliSearch's `dynamic` Filter/`ISearchable<T>` shapes. Five new Core-phase implementation findings documented in place: the Meilisearch `_formatted`-highlight/JsonElement search workaround (MeilisearchIndex note), the schema-fingerprint-via-Settings.Dictionary sentinel-entry mechanism (MeilisearchIndexProvisioner note), the ElasticSearch cursor Token's QueryBase64/RequestResponseSerializer encoding (SearchCursor note), the builder-closure-capture DI singleton lazy-resolution pattern (both providers' AddSharedKernel*Search NOTE), and the mandatory `global::`-qualification convention for ElasticSearch aggregation types to avoid colliding with this domain's own same-named Analytics types (IAnalyticsSearch note). No interface/model/DI-signature changes — documentation-only, capturing verified engine behaviour and implementation patterns for future Core-phase or Tests-phase sessions (search-phase-implementer)
- [2026-07-19] SK.09.Tests container-free tasks complete (T-01–T-12, T-18–T-20; 15/26, 248 tests green) — re-verified the `16.Testing` container-fixture blocker on disk (still absent), then implemented and passed every genuinely container-free task; T-13–T-17/T-21–T-26 (11 tasks) marked `⚑` Blocked, no ad-hoc container setup hand-rolled. Fixed two genuine Core-phase production defects surfaced by test-writing (not deferred): `SearchValue.ToString()` unconditionally threw `InvalidOperationException` (compiler-synthesized `PrintMembers` calls every kind-checked accessor regardless of `Kind`) — fixed with an explicit override, documented on `SearchValue`; and `MeilisearchIndexProvisioner`/`ElasticSearchIndexProvisioner`/`MeilisearchTenantTokenIssuer` could never resolve via DI in any consuming service because their constructors take a raw `TOptions`, not `IOptions<TOptions>`, while `Build()` registered them via the plain-shorthand `AddSingleton<TInterface, TImplementation>()` — fixed with an explicit unwrapping factory, documented in DI Registration. Added `InternalsVisibleTo` from each provider package to its own `.Tests` project (white-box testing internals, no public surface growth) and established the null-client no-I/O-proof technique for pre-flight validation tests, both documented in Test Rules (search-phase-implementer)
- [2026-07-20] SK.09.Tests real-backend tasks complete (T-13–T-17, T-21–T-26; 26/26, 345 tests green: 155 Abstractions + 92 Meilisearch + 98 ElasticSearch) — re-verified the `16.Testing` container-fixture blocker on disk and found it cleared (`MeilisearchContainerFixture`/`ElasticsearchContainerFixture` shipped in `16.Testing`'s `SK.16.Core`); implemented both `Containers/{Provider}Collection.cs`+`{Provider}ProviderFactory.cs` pairs, a shared 15-document/2-tenant `TestProductCorpus`, and every real-backend test against real Docker containers (image `getmeili/meilisearch:v1.20.0`; `docker.elastic.co/elasticsearch/elasticsearch:9.4.2`). **Five genuine production defects found and fixed, none deferred** — `MeilisearchIndexProvisioner.IndexExistsAsync`/`ProbeAsync` (SDK's `GetIndexAsync` never throws for any uid, switched to `GetSettingsAsync`), `MeilisearchResultMapper.MapFacets` (null, not empty-dict, facet response when none requested — NRE on almost every ordinary search), `MeilisearchIndexProvisioner.CutoverAsync` (Meilisearch's swap requires both indexes to pre-exist — now pre-creates an empty placeholder live index), `MeilisearchIndex.GetAsync` (missing `HttpRequestException` catch for the SDK's inconsistent 404-exception-type behaviour on this one path), `ElasticSearchIndexProvisioner.ProbeAsync` (index-scoped `HealthRequest` fails for a nonexistent index, collapsing Reachable/IndexAddressable — switched to the parameterless cluster-wide form) — all five documented inline on their respective Interface Contracts blocks above. **T-26 real-evidence findings, both requested by the phase spec:** `EnumerateAsync` is confirmed insertion-order (not id-ascending) on both engines — the "unspecified, do not rely on it" contract language is deliberately RETAINED rather than strengthened, since this is an observed implementation detail, not a documented engine guarantee; tenant-scoped facet counts are confirmed correct on both engines (tenant filter applied before faceting, no cross-tenant cardinality leak). One genuine cross-provider semantic divergence found and documented, not treated as a bug: `SearchFilter.Any()` with zero operands is rejected by Meilisearch (invalid filter syntax) but evaluates as match-all on ElasticSearch (valid empty `BoolQuery.Should`) — both are correct per-engine translations. State-map fully corrected: Blocked section and both Cross-Domain Dependencies rows annotated resolved (retained for history, not silently rewritten), all 26 `SK.09.Tests` tasks `●`, Overall Progress and Package Board recalculated, promoted to root (search-phase-implementer)
- [2026-07-20] SK.09.Docs complete (DO-01–DO-08, 8/8) — `GenerateDocumentationFile`/`TreatWarningsAsErrors`/full NuGet metadata block (incl. `PackageReadmeFile` + packed `README.md`, learning `08.Storage`'s Published-phase lesson up front instead of repeating its miss) landed on all three production `.csproj` files; zero CS1591/CS1574 across all three packages, fixing two genuine doc defects surfaced only once `TreatWarningsAsErrors` was enabled (`MeilisearchResultMapper`'s class-level `<typeparamref name="TDocument"/>` referencing a type parameter that belongs to its `Map<TDocument>` method, not the class; `ElasticSearchResultMapper`'s `cref="SearchResponse{TDocument}.IsValidResponse"` failing to resolve because the member is inherited from a non-generic base rather than declared directly on the generic type — both fixed by dropping to plain `<c>`-tagged prose, since neither needed a cross-reference link badly enough to fight the resolver). `SearchFilter`'s class-level remarks gained the "what is deliberately absent, and why" plus the mandated nested/object-array flattening-technique paragraphs that DO-01 named explicitly and that Core/Tests phases had never actually written into the type's own XML doc (only into `09.Search/CLAUDE.md`'s prose) — closing a genuine doc-completeness gap, not a false-positive. **The CS8509/CS8524 collision flagged at Core phase as "to become a hard error once Docs-phase `TreatWarningsAsErrors` lands" was resolved, not silenced**: a scratch repro proved the Core-phase premise half-wrong — adding an explicit `null =>` arm to the `SearchFilter`/`AggregationRequest` switches does not clear CS8509, it only shifts the compiler's reported gap from `_` to `not null`, proving the compiler cannot perform closed-world exhaustiveness analysis over a sealed-subtype hierarchy of an abstract base at all, regardless of how the switch is written (a permanent C# limitation, not a fixable gap in these five switches); `SearchValueKind`'s CS8524 is the structurally identical enum analogue. Resolution: both provider `.csproj` files carry `<WarningsNotAsErrors>CS8509;CS8524</WarningsNotAsErrors>` — narrowly scoped to these two diagnostic IDs, keeping them **visible** in every build log rather than hidden by `NoWarn`/`#pragma warning disable`, while every other warning stays fatal — with an inline comment at each of the five affected switch sites (`MeilisearchFilterCompiler.Compile`/`.FormatValue`, `ElasticSearchFilterCompiler.Compile`/`.ToFieldValue`, `ElasticSearchAnalytics<TDocument>.GetName`) cross-referencing the new "Reconciling switch exhaustiveness with `TreatWarningsAsErrors`" section under Implementation Rules. That section also corrects the Core-phase premise on record: the real backstop against a silently-dropped ninth node was never going to be a compile-time catch (that was untested), it is the runtime `SwitchExpressionException` every switch expression throws automatically on an unmatched value — strictly better than a `_ => throw` discard arm, which would produce the identical runtime behaviour while erasing the compiler's own honest "not provably exhaustive" signal for no benefit. Three README.md files written (`SharedKernel.Search.Abstractions`, `SharedKernel.Search.Meilisearch`, `SharedKernel.Search.ElasticSearch`), each covering its DO-05/06/07-mandated content in full, including the two ElasticSearch hard warnings (`MaxTotalHits` 1000-not-10000 parity default; document-level security as a commercial-tier gap versus Meilisearch's engine-enforced tenant tokens) and the two Meilisearch hard warnings (tenant tokens cannot be revoked before expiry; the global sequential task queue's cross-workload/instance-wide-`PendingWriteCount` blast radius). DO-08 drift check: read every provider-exclusive contract, options type, DI builder, error factory list (29 `SearchErrors` + 3 `MeilisearchErrors` + 4 `ElasticSearchErrors`, tallies re-confirmed against source), and `[LoggerMessage]` `EventId` table (Meilisearch 9100–9120/21 entries, ElasticSearch 9200–9223/24 entries, both confirmed gap-free and duplicate-free against source) against the brain's Interface Contracts section — zero drift found in any interface shape or DI registration pattern; two genuinely stale Technology-Stack-adjacent notes corrected: the "Testing containers" row now states the actual pinned image tags (`getmeili/meilisearch:v1.20.0`, `docker.elastic.co/elasticsearch/elasticsearch:9.4.2`) instead of only the NuGet package version, and the Test Rules bullet flagging the community Meilisearch image's SDK-surface support as "unconfirmed" is updated to CONFIRMED per the Tests-phase real-backend evidence (92/92 `SharedKernel.Search.Meilisearch.Tests` green against exactly that image). All 345 tests still green after every edit (155 Abstractions + 92 Meilisearch + 98 ElasticSearch); all three projects build 0 errors with only the two/three intentionally-downgraded-but-visible CS8509/CS8524 warnings. Propagated to root (search-phase-implementer)
- [2026-07-20] SK.09.Published complete (P-01–P-08, 8/8) — **09.Search domain (WO-044) complete end to end, all six phases `●` for all three packages, 139/139 tasks.** P-01 re-verified all three `.csproj` files already carried the full NuGet metadata block incl. `PackageReadmeFile`+packed `README.md` — **the first domain where this specific `08.Storage`-flagged gap did not recur**, confirming this domain's own Docs-phase discipline held. P-02: all three packed clean to `.nupkg`+`.snupkg`, zero `NU5039`/`NU5128`. P-03: new `09.Search/consumer-verify/` area — **three** console harnesses (`Meilisearch/`, `ElasticSearch/`, `BothProviders/`), a deliberate split from every prior domain's single-project `consumer-verify` precedent (`08.Storage`/`13.ServiceDefaults`/`14.Presentation`/`15.Integration`/`04.Contracts`) because the P-06 capability-segregation proof structurally requires two **disjoint** compilation closures — a single project referencing both providers could never prove segregation, since both exclusive interfaces would simply be nameable together. P-04/P-05: real `Host.CreateApplicationBuilder()` → `IHost.StartAsync()` composition resolves every neutral and provider-exclusive contract with zero DI exceptions; `ElasticsearchClient` proven singleton; both raw-client accessors proven gated. **One DI gap re-confirmed at the real-host level, not new:** `AddSharedKernelMeilisearchSearch()`/`AddSharedKernelElasticSearchSearch()` do not self-register `IClock` — every harness surface resolving `ISearchIndex<TDoc>` registers `SystemClock` itself, same as the `ILogger<T>` precedent. ElasticSearch surfaces set `ValidateEngineVersionOnStart=false` deliberately so DI-composition pass/fail never depends on Docker/a live cluster (the live-engine guard is `SK.09.Tests` T-22's job). P-06: capability segregation proved as a **genuine, captured build-time compiler failure** via the new negative-compile-probe technique (documented in Test Rules above) — real `CS0234`+`CS0246` diagnostics captured in both directions, transcripts permanently recorded in both `Program.cs` header comments. P-07: missing config throws `OptionsValidationException` at `IHost.StartAsync()` naming the specific property; `consumer-verify.BothProviders` demonstrates the same-`TDocument` dual-registration hard violation against real compiled code (documented in DI Registration above). All 16 consumer-verify surfaces pass; full regression re-run confirms 345/345 tests still green, zero production `.cs` changes this session. Propagated to root (search-phase-implementer)
