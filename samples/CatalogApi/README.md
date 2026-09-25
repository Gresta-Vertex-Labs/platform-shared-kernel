# CatalogApi

A product catalogue that runs **both** search engines at once: Meilisearch serves the storefront, and
ElasticSearch serves the back office. It exists to prove the `09.Search` packages work as packed NuGet
artifacts against real engines — not to be copied wholesale, since most services need one engine.

| Domain | What this sample uses it for |
|---|---|
| `09.Search` (all three packages) | Both providers side by side; the neutral contracts, plus each engine's exclusive ones |
| `13.ServiceDefaults` | OpenTelemetry, health endpoints, `AddSharedKernelReadiness()` over the per-index probes each provider registers, `UseSharedKernelRequestContext()` (correlation id) |
| `14.Presentation` | `Result<T>` → RFC 9457 ProblemDetails |

## Running it

```bash
docker run -d --name sk-search-meili -p 7700:7700 \
  -e MEILI_MASTER_KEY=sk-sample-master-key -e MEILI_NO_ANALYTICS=true \
  getmeili/meilisearch:v1.20.0

docker run -d --name sk-search-es -p 9200:9200 \
  -e discovery.type=single-node -e xpack.security.enabled=false \
  -e "ES_JAVA_OPTS=-Xms1g -Xmx1g" \
  docker.elastic.co/elasticsearch/elasticsearch:9.4.2

dotnet pack Platform.SharedKernel.slnx -c Release
dotnet run --project samples/CatalogApi --urls http://localhost:5199 -p:SharedKernelPackageVersion=<the packed version>
```

Then provision and seed, in that order:

```bash
curl -X POST localhost:5199/ops/provision
curl -X POST localhost:5199/ops/seed
```

> **Pass the exact version.** The `*-*` float in `Directory.Packages.props` compares prerelease labels
> alphabetically, so a stale local build such as `1.0.0-p559.local.3` outranks a real MinVer
> `1.0.0-alpha.0.1172` and you will silently build against the wrong packages.

## What to look at

**The count tells you how much it can be trusted.** The products index is provisioned with
`MaxTotalHits(8)` deliberately, and the north tenant holds ten products. Tenants are GUIDs (`TenantId`,
never a slug); the two seeded ones are `Catalog.TenantNorth` and `Catalog.TenantSouth`, and a route
value that is not a GUID is a 400:

```bash
NORTH=6f1c2a4e-0b7d-4c3e-9a51-3d2e8f7b1a01   # Catalog.TenantNorth
SOUTH=6f1c2a4e-0b7d-4c3e-9a51-3d2e8f7b1a02   # Catalog.TenantSouth

curl localhost:5199/storefront/$NORTH/products/count
# {"value":8,"accuracy":"LowerBound","isExact":false,"display":">=8"}

curl localhost:5199/storefront/$NORTH/products/count?category=stationery
# {"value":3,"accuracy":"Exact","isExact":true,"display":"3"}

curl localhost:5199/back-office/$NORTH/order-lines/count
# {"value":8,"accuracy":"Exact","isExact":true,"display":"8"}
```

Meilisearch has no count endpoint and reads `totalHits` off a paginated search, which the engine caps
at the index ceiling — so it reports a lower bound rather than a number it cannot vouch for.
ElasticSearch answers from `_count` and is always exact. Both are honest about which they are.

**A down engine is a `Result`, not an exception.** Stop Meilisearch and search again:

```bash
docker stop sk-search-meili
curl -i localhost:5199/storefront/$NORTH/products?q=mouse
# HTTP 500 — {"title":"search.unreachable","errorCode":"search.unreachable", …}
```

ElasticSearch keeps serving. Restart the container and the storefront recovers with no intervention.

**Telemetry is real.** `GET /diagnostics/telemetry` shows the spans and measurements the packages
emitted, subscribed by the same `SharedKernel.Search` source and meter name `WithSearchTelemetry()`
uses — including `error.type` on the failures above.

**Synonyms are one-way, on both engines.** `?q=rodent` finds the Wireless Mouse; `?q=mouse` is
unaffected, because the declaration is `Synonym("rodent", "mouse")`. Getting the direction backwards is
silent — both engines accept it and it simply never fires.

**Changing text analysis on a live index is refused.** Edit a synonym, restart, and
`POST /ops/provision` returns `search.index_definition_conflict` from *both* providers. ElasticSearch
cannot change an open index's analysis settings at all; Meilisearch could, and refuses anyway so the two
stay observably identical. `DELETE /ops/indexes` then re-provision is the sample's stand-in for the
staging → bulk-load → `CutoverAsync` rebuild a real service performs.

**A forgotten rebuild is caught.** `GET /ops/verify` compares every live index against the definition
this build declares, and returns 503 with the specific drift when they differ.

**Tenant isolation holds everywhere**, including the ElasticSearch suggester — which ignores query
filters entirely, so the tenant travels as a completion category context declared at provisioning time:

```bash
curl "localhost:5199/back-office/$NORTH/order-lines/suggest?prefix=Gaming"   # []
curl "localhost:5199/back-office/$SOUTH/order-lines/suggest?prefix=Gaming"   # Gaming Monitor
```

## Why both engines in one host

Most services register one. This one registers both to exercise both packages, which is legal because
they serve **different document types** — `ISearchIndex<ProductDocument>` and
`ISearchIndex<OrderLineDocument>` never collide.

The two non-generic contracts do collide, though: `ISearchIndexProvisioner` and
`ISearchProviderDescriptor` have no type parameter to tell them apart, so an unkeyed resolution returns
whichever provider was registered last. Each provider package therefore also registers them **keyed by
provider name**, and this sample addresses them that way (`/ops/probe/{provider}/{index}`).

Readiness needs no key at all. Each provider registers one `IReadinessProbe` per index it was given,
named after the provider and the index, and the host maps every probe in one call:

```csharp
builder.Services.AddHealthChecks().AddSharedKernelReadiness();
// ready checks: search-meilisearch-products, search-elasticsearch-order-lines
```

Because the provider registered its own probe, it can never ask ElasticSearch about a Meilisearch index —
the defect this sample found in the earlier design, where the host named the index and had to pass the
right provider key or report a healthy service unready forever.

## Endpoints

| | |
|---|---|
| `GET /storefront/{tenant}/products` | free text, filters, sort, facets, highlighting, paging |
| `GET /storefront/{tenant}/products/count` | qualified `SearchCount` |
| `GET /storefront/{tenant}/products/{id}` | tenant-checked get |
| `GET /storefront/{tenant}/products/export` | `EnumerateAsync` corpus walk |
| `GET /storefront/{tenant}/products/instant` | **Meilisearch-only** typo-tolerant instant search |
| `POST /storefront/{tenant}/products/search-token` | **Meilisearch-only** engine-enforced tenant token |
| `GET /back-office/{tenant}/order-lines/count` | the same neutral contract, other engine |
| `GET /back-office/{tenant}/order-lines/revenue-by-region` | **ElasticSearch-only** aggregations |
| `GET /back-office/{tenant}/order-lines/cursor` \| `/stream` | **ElasticSearch-only** deep pagination |
| `GET /back-office/{tenant}/order-lines/suggest` | **ElasticSearch-only** completion suggester |
| `POST /ops/provision` · `POST /ops/seed` · `DELETE /ops/indexes` | deployment operations |
| `GET /ops/verify` · `GET /ops/probe/{provider}/{index}` | drift and readiness |
| `GET /diagnostics/telemetry` | what the packages emitted |

The Meilisearch-exclusive and ElasticSearch-exclusive endpoints take a compile-time dependency on their
provider package. Swap this service to one engine and the other engine's endpoints become **build
errors naming themselves** — which is the point of declaring exclusive capabilities in the provider
package instead of behind a runtime capability flag.

## Known gaps

`POST /storefront/{tenant}/products/search-token` returns
`search.meilisearch.tenant_token_issuance_failed` until `Search:Meilisearch:ApiKeyUid` is set. Meilisearch
will not sign a tenant token with the master key, so that needs the uid of a separately-provisioned
search key (`GET /keys` on the engine). Left unset here deliberately: the actionable failure message is
itself worth seeing.
