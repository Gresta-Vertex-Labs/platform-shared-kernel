# 09.Search — State Map

> Living board for this domain: what exists, what is open. Completed phase detail is archived outside the repository; `git log` records every change.

## Legend

○ Not started · ◐ In progress · ● Done · ⚑ Blocked · ⊘ Declined / superseded

## Package Board

| Package | Tier | Status | Notes |
| --- | --- | --- | --- |
| `SharedKernel.Search.Abstractions` | Abstractions | ● | Zero third-party: `ISearchIndex<TDocument>`, `ISearchIndexProvisioner`, `ISearchProviderDescriptor`, `IQueryBuilder`, closed `SearchFilter` AST, `SearchIndexDefinition`; `TenantScope` (from Execution) is a mandatory parameter on every read and filtered write. |
| `SharedKernel.Search.Meilisearch` | Adapter | ● | Engine-only contracts (`IInstantSearch<T>`, `ITenantSearchTokenIssuer`, ranking rules); one `search-meilisearch-{index}` probe per index. |
| `SharedKernel.Search.ElasticSearch` | Adapter | ● | Engine-only contracts (`IAnalyticsSearch<T>`, `ICursorSearch<T>`, `ISuggestSearch<T>`); one `search-elasticsearch-{index}` probe per index. |

## Phase Key Registry

| Phase key | Phase | Status |
| --- | --- | --- |
| `SK.09.Design` | Design | ● |
| `SK.09.Scaffold` | Scaffold | ● |
| `SK.09.Core` | Core | ● |
| `SK.09.Tests` | Tests | ● |
| `SK.09.Docs` | Docs | ● |
| `SK.09.Published` | Published | ● |

## Open Work

None — every phase in this domain is complete. The first public release ships with root P-577 (release train).

## Blocked

None.

## Cross-Domain Dependencies

None open.

## Completed Phases

- WO-086 ● Foundation refactor — search-local `TenantScope` deleted in favour of `SharedKernel.Execution.Tenancy.TenantScope`; per-index readiness probes; `SharedKernel.Search.Testing`; tiers declared (P-565, P-569, P-571, P-575) (2026-09-26)
- GS-01–GS-18 ● Pre-publish gold-standard pass of all three packages, then `samples/CatalogApi` run against real engines (four composed-host defects fixed) (2026-09-23)
- P-353, P-354 ● WO-055 — bulk-indexing serialization/result discipline, bulk-write outcome reporting and opt-in backpressure (2026-08-11)
- P-272–P-274 ● WO-044 domain build — abstractions contract, Meilisearch provider, ElasticSearch provider (2026-07-20)

## Changelog

- [2026-09-28] State map slimmed to a living board; completed phase detail archived outside the repository — public-release cleanup
- [2026-09-26] WO-086 foundation refactor (P-565, P-569, P-571, docs P-575) — Execution `TenantScope`, `search-{provider}-{index}` readiness probes, test double moved to `SharedKernel.Search.Testing`
- [2026-09-23] `samples/CatalogApi` built and run against Meilisearch 1.20.0 and Elasticsearch 9.4.2 — four defects found and fixed (duplicate readiness names, two-provider resolution of the non-generic contracts, an unprovisioned write alias, suggester score)
- [2026-09-23] Pre-publish gold-standard pass (GS-01–GS-18) complete — all three packages
- [2026-08-11] SK.09.Published complete (P-01–P-10) — WO-055 shipped, 158/158 tasks across the domain
