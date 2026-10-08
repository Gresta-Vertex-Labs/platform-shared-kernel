# 09.Search — State Map

> Living board for this domain: what exists and what is open. Completed work is not kept here; `git log` is the record.

## Legend

○ Not started · ◐ In progress · ● Done · ⚑ Blocked · ⊘ Declined / superseded

## Package Board

| Package | Tier | Status | Notes |
| --- | --- | --- | --- |
| `SharedKernel.Search.Abstractions` | Abstractions | ● | Zero third-party: `ISearchIndex<TDocument>`, `ISearchIndexProvisioner`, `ISearchProviderDescriptor`, `IQueryBuilder`, closed `SearchFilter` AST, `SearchIndexDefinition`; `TenantScope` (from Execution) is a mandatory parameter on every read and filtered write. |
| `SharedKernel.Search.Meilisearch` | Adapter | ● | Engine-only contracts (`IInstantSearch<T>`, `ITenantSearchTokenIssuer`, ranking rules); one `search-meilisearch-{index}` probe per index. |
| `SharedKernel.Search.ElasticSearch` | Adapter | ● | Engine-only contracts (`IAnalyticsSearch<T>`, `ICursorSearch<T>`, `ISuggestSearch<T>`); one `search-elasticsearch-{index}` probe per index. |

## Phase Key Registry

No open phase keys. Closed keys live in `git log`: check a new key is unused with `git log --oneline -S"SK.NN.Key"`.

## Open Work

None — every phase in this domain is complete. The first public release ships with root P-577 (release train).

## Blocked

None.

## Cross-Domain Dependencies

None open.
