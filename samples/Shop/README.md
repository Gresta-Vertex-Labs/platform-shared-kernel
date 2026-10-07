# Shop — the kernel in a real system

A commerce platform built only from SharedKernel packages, consumed the way an outside service consumes them:
`PackageReference` to the packages packed from this checkout. Every service runs against real infrastructure in
containers, local stand-ins only, orchestrated by a .NET Aspire AppHost. Its purpose is to prove the packages work
together, and it has already found kernel bugs that unit tests did not.

> **Status.** The platform is being built service by service. It contains the AppHost, Catalog, Inventory, Ordering
> and the end-to-end harness; Billing, Notify, Reports and Merchant follow, and then the older samples are retired.
> `Shop.Coverage.Tests` reports which kernel packages are not used yet.

## What runs

| Resource | What it is | Kernel packages it exercises |
| --- | --- | --- |
| `catalog-api`, `catalog-api-2` | The Catalog service, two replicas (four-project shape) | Domain, Application + Pipeline + Pipeline.Caching + MediatR, Persistence.EfCore/Npgsql (RLS), Caching.FusionCache + Redis L2 + Redis.PubSub, Search.Meilisearch + Search.ElasticSearch, AI.Qdrant + AI.SemanticKernel, Storage.S3, FeatureManagement, Localization, Security.Oidc, Presentation.WebApi + OpenApi + GraphQL, ServiceDefaults (+ Localization, Security) |
| `inventory-api`, `inventory-api-2` | The Inventory service, two replicas (one project): gRPC over mutual TLS for Ordering, REST for merchants, a reconciliation job | Persistence.Npgsql + Dapper under RLS (schema created under the migration lock), Caching.Redis.Core + HashStore + DistributedLocking, Scheduling, Security.Mtls + ServiceDefaults.Security.Mtls, Security.Oidc, MultiTenancy (with a service-only header strategy), Presentation.Grpc + WebApi, Application + Pipeline + MediatR |
| `ordering-api` | The Ordering service (four-project shape): orders placed once however often they are submitted, fulfilled by a Temporal workflow that reserves stock in Inventory, status pushed over SignalR, cancelling behind an authenticator step-up | Persistence.EfCore + Auditing + Encryption under RLS, ServiceDefaults.Persistence, Messaging.MassTransit + RabbitMq + EfCore outbox, Idempotency.Redis (requests) + Idempotency.EfCore (messages), Workflows.Temporal, Communication.Grpc over mutual TLS, Caching.Redis.HashStore, Cryptography + Argon2, Security.Oidc + Totp, MultiTenancy, Presentation.WebApi + SignalR, Application + Pipeline + MediatR, Validation.FluentValidation |
| `rabbitmq` | RabbitMQ 4.3 with the delayed-exchange plugin (`masstransit/rabbitmq`) | |
| `temporal` | The Temporal development server (gRPC 7233, UI 8233) | |
| `postgres` | PostgreSQL 16 with pgvector; the production role split (`postgres/`) | |
| `redis` | Redis 7.4 | |
| `meilisearch`, `elasticsearch` | The storefront and back-office search engines | |
| `qdrant`, `ollama` | Vectors, and the embedding (`all-minilm`) and chat (`qwen2.5:0.5b`) models behind an OpenAI-compatible endpoint | |
| `minio` | S3 (and later the stand-in for Huawei OBS) | |
| `keycloak` | The identity provider: realm `shop`, tenants Contoso and Fabrikam, users `alice`, `bruno`, `carol` (`keycloak/shop-realm.json`) | |

Every secret in the AppHost is a development-only value for a local container.

## Run it

Docker with about 8 GB of memory is needed. The first start pulls the images and the two Ollama models.

```bash
samples/Shop/build.sh                      # pack the kernel, build the Shop  (build.ps1 on Windows)
dotnet run --project samples/Shop/Shop.AppHost --launch-profile http
```

The Aspire dashboard (http://localhost:15300) shows every resource, its logs and the traces across services. Sign in
as a test user with a password grant against Keycloak's `shop` realm (client `shop-web`, password `Shop-dev-1!`).

## Test it

```bash
samples/Shop/build.sh --test               # unit tests: architecture, the use cases over the kernel's fakes
samples/Shop/build.sh --e2e                # end-to-end flows against the running platform (SHOP_E2E=1)
```

| Project | What it proves |
| --- | --- |
| `Shop.Catalog.Tests` | The four-project shape over the real restore graph, the kernel's architecture rules, the use cases through the real pipeline with the `*.Testing` fakes |
| `Shop.Inventory.Tests` | Inventory's dependency graph, the service-only tenant strategy, the certificate allow-list, the SKU locks and the job schedule, over the kernel's Security, Caching and Scheduling fakes |
| `Shop.Ordering.Tests` | Ordering's four-project shape, idempotent placement, auditing, validation and cancellation through the real pipeline over the Persistence, Messaging and Application fakes, the gRPC adapter's error mapping (Communication fakes), the TOTP stores over the fake Redis services |
| `Shop.E2E` | Whole flows through the real AppHost: sign-in, cache and backplane across replicas, Redis Pub/Sub, search, semantic search, the chat model, presigned downloads, tenant isolation, permissions, localization, GraphQL; reservations over mutual TLS (allow-list, rogue certificate, no certificate), no overselling under concurrency across replicas, a job that runs once per occurrence on two replicas; an order placed once per idempotency key, fulfilled or rejected by the Temporal workflow through the outbox and RabbitMQ, SignalR status pushes, another tenant's orders invisible, ciphertext at rest and the audit ledger, cancelling only after a TOTP step-up. Skipped unless `SHOP_E2E=1`; CI builds it but does not run it |
| `Shop.Coverage.Tests` | Which kernel packages a Shop project references directly (strict once every service exists) |

CI builds every Shop project against the packages of the same run and runs the unit tests; the end-to-end flows are
local only for now.

## Development PKI

Services that call each other over mutual TLS use a development CA the AppHost creates on first start
(`%TEMP%/shop-pki`, `ShopPki.cs`): a `localhost` server certificate Kestrel serves, and client certificates for
`ordering-api` and `rogue-service` (signed by the CA, on no allow-list). Delete the folder to issue a fresh set.
