# samples
| [`CatalogApi`](CatalogApi/) | `09.Search` (all three packages — both engines side by side against different document types, the neutral contracts plus each engine's exclusive ones), `13.ServiceDefaults` (+`.Search`), `14.Presentation` | Meilisearch and Elasticsearch (Docker, see its README) |

Runnable services built on the SharedKernel packages.

Unlike the per-domain `consumer-verify` harnesses — which prove a single package's
dependency graph resolves — a sample is a working service that composes several domains the
way a real microservice would.

| Sample | Domains exercised | External infrastructure |
|---|---|---|
| [`OrderApi`](OrderApi/) | `01.Core`, `03.Domain`, `05.Application`, `13.ServiceDefaults`, `14.Presentation` | none |
| [`BillingApi`](BillingApi/) | `06.Persistence` (all six packages + `SharedKernel.Persistence.Testing`), `01.Core`, `03.Domain`, `05.Application`, `12.Security`, `13.ServiceDefaults`, `14.Presentation` | PostgreSQL (Docker Compose, or Testcontainers in its tests) |
| [`DocumentsApi`](DocumentsApi/) | `08.Storage` (all three packages, two S3 connections + OBS, a tenant store), `13.ServiceDefaults`, `14.Presentation` | MinIO (Testcontainers); optionally real Amazon S3 and Huawei Cloud OBS (`SK_LIVE_*`) |
| [`CatalogApi`](CatalogApi/) | `09.Search` (all three packages — both engines side by side against different document types, the neutral contracts plus each engine's exclusive ones), `13.ServiceDefaults` (+`.Search`), `14.Presentation` | Meilisearch and Elasticsearch (Docker, see its README) |
| [`ShippingApi`](ShippingApi/) | `07.Messaging` (both packages — publish, send, delayed delivery, idempotency, inbound caller identity, retry, fault consumer, readiness), `04.Contracts`, `05.Application.Abstractions`, `13.ServiceDefaults`, `14.Presentation` | RabbitMQ (Testcontainers, `masstransit/rabbitmq` for the delayed-exchange plugin); Docker Compose for running it by hand |

## Why these use PackageReference

Every sample resolves `SharedKernel.*` from the local NuGet feed (`nupkgs/`) via
`PackageReference`, never `ProjectReference`. The point is to prove the **packed** packages
work for a consumer who has only the published artifacts — a `ProjectReference` would bypass
exactly the thing under test.

That has two consequences, both deliberate:

1. Samples are **excluded from `Platform.SharedKernel.slnx`**. They cannot build until the
   packages they consume have been packed, so a solution-wide build would fail on a clean
   checkout.
2. CI builds and runs them in the `packaging-verify` job, after `dotnet pack` — the same
   pattern the `consumer-verify` harnesses use.

To run one locally, pack first:

```bash
dotnet pack Platform.SharedKernel.slnx -c Release
dotnet run --project samples/OrderApi
```
