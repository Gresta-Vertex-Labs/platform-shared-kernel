<div align="center">

# Shop

**The kernel in a real system: a multi-service commerce platform on real infrastructure in containers, orchestrated by a .NET Aspire AppHost.**

<sub>📂 <code>samples/Shop</code> · <a href="../README.md">all samples</a> · needs Docker (about 8 GB of memory)</sub>

</div>

## What it shows

- **Many capabilities in one service.** Catalog combines persistence under row-level security, a two-level cache with a
  Redis backplane, both search engines, semantic search and a chat model, object storage, feature flags, localization,
  OIDC sign-in, OpenAPI and GraphQL.
- **Service-to-service calls over mutual TLS.** Inventory serves gRPC to Ordering behind a certificate allow-list, and
  REST to merchants.
- **Coordination across replicas.** Two replicas of each service share cache entries and invalidations, never oversell
  under SKU locks, and run a scheduled reconciliation job once per occurrence.
- **Governance as a consumer gets it.** The kernel's analyzers and linter on every project, and Catalog's four-project
  shape pinned by an architecture test built on the kernel's own rules.
- **End-to-end flows against the running platform** — sign-in, tenant isolation, permissions, mutual TLS, concurrency.

**Packages it uses** (directly, by project):

- Catalog — [SharedKernel.Domain](../../src/Model/Domain/SharedKernel.Domain/README.md) ·
  [SharedKernel.Application](../../src/Application/SharedKernel.Application/README.md) ·
  [.Pipeline](../../src/Application/SharedKernel.Application.Pipeline/README.md) ·
  [.Pipeline.Caching](../../src/Application/SharedKernel.Application.Pipeline.Caching/README.md) ·
  [.Mediator.MediatR](../../src/Application/SharedKernel.Application.Mediator.MediatR/README.md) ·
  [SharedKernel.Persistence.EfCore](../../src/Infrastructure/Persistence/SharedKernel.Persistence.EfCore/README.md) ·
  [.Npgsql](../../src/Infrastructure/Persistence/SharedKernel.Persistence.Npgsql/README.md) ·
  [SharedKernel.Caching.FusionCache](../../src/Infrastructure/Caching/SharedKernel.Caching.FusionCache/README.md) ·
  [SharedKernel.Caching.Redis](../../src/Infrastructure/Caching/SharedKernel.Caching.Redis/README.md) ·
  [.Redis.Core](../../src/Infrastructure/Caching/SharedKernel.Caching.Redis.Core/README.md) ·
  [.Redis.PubSub](../../src/Infrastructure/Caching/SharedKernel.Caching.Redis.PubSub/README.md) ·
  [SharedKernel.Search.Meilisearch](../../src/Infrastructure/Search/SharedKernel.Search.Meilisearch/README.md) ·
  [.ElasticSearch](../../src/Infrastructure/Search/SharedKernel.Search.ElasticSearch/README.md) ·
  [SharedKernel.AI.Qdrant](../../src/Infrastructure/AI/SharedKernel.AI.Qdrant/README.md) ·
  [.SemanticKernel](../../src/Infrastructure/AI/SharedKernel.AI.SemanticKernel/README.md) ·
  [SharedKernel.Storage.S3](../../src/Infrastructure/Storage/SharedKernel.Storage.S3/README.md) ·
  [SharedKernel.FeatureManagement](../../src/Foundation/SharedKernel.FeatureManagement/README.md) ·
  [SharedKernel.Localization](../../src/Foundation/SharedKernel.Localization/README.md) ·
  [SharedKernel.Cryptography](../../src/Foundation/SharedKernel.Cryptography/README.md) ·
  [SharedKernel.Validation.FluentValidation](../../src/Foundation/SharedKernel.Validation.FluentValidation/README.md) ·
  [SharedKernel.Security.Oidc](../../src/Hosting/Security/SharedKernel.Security.Oidc/README.md) ·
  [SharedKernel.ServiceDefaults](../../src/Hosting/ServiceDefaults/SharedKernel.ServiceDefaults/README.md) ·
  [.Security](../../src/Hosting/ServiceDefaults/SharedKernel.ServiceDefaults.Security/README.md) ·
  [.Localization](../../src/Hosting/ServiceDefaults/SharedKernel.ServiceDefaults.Localization/README.md) ·
  [SharedKernel.Presentation.WebApi](../../src/Hosting/Presentation/SharedKernel.Presentation.WebApi/README.md) ·
  [.OpenApi](../../src/Hosting/Presentation/SharedKernel.Presentation.OpenApi/README.md) ·
  [.GraphQL](../../src/Hosting/Presentation/SharedKernel.Presentation.GraphQL/README.md)
- Inventory — [SharedKernel.Persistence.Npgsql](../../src/Infrastructure/Persistence/SharedKernel.Persistence.Npgsql/README.md) ·
  [.Dapper](../../src/Infrastructure/Persistence/SharedKernel.Persistence.Dapper/README.md) ·
  [SharedKernel.Caching.Redis.Core](../../src/Infrastructure/Caching/SharedKernel.Caching.Redis.Core/README.md) ·
  [.Redis.HashStore](../../src/Infrastructure/Caching/SharedKernel.Caching.Redis.HashStore/README.md) ·
  [.Redis.DistributedLocking](../../src/Infrastructure/Caching/SharedKernel.Caching.Redis.DistributedLocking/README.md) ·
  [SharedKernel.Scheduling](../../src/Infrastructure/Scheduling/SharedKernel.Scheduling/README.md) ·
  [SharedKernel.Security.Mtls](../../src/Hosting/Security/SharedKernel.Security.Mtls/README.md) ·
  [SharedKernel.ServiceDefaults.Security.Mtls](../../src/Hosting/ServiceDefaults/SharedKernel.ServiceDefaults.Security.Mtls/README.md) ·
  [SharedKernel.MultiTenancy](../../src/Hosting/ServiceDefaults/SharedKernel.MultiTenancy/README.md) ·
  [SharedKernel.Presentation.Grpc](../../src/Hosting/Presentation/SharedKernel.Presentation.Grpc/README.md),
  plus the Application, Oidc, ServiceDefaults and WebApi packages above
- Every project — [SharedKernel.Analyzers](../../tools/Governance/SharedKernel.Analyzers/README.md) ·
  [SharedKernel.Linter](../../tools/Governance/SharedKernel.Linter/README.md); tests add
  [SharedKernel.ArchitectureTests](../../tools/Governance/SharedKernel.ArchitectureTests/README.md),
  [SharedKernel.Testing](../../src/Testing/SharedKernel.Testing/README.md) and the capability fakes
  (see [Testing](../../src/Testing/README.md))

## About it

A commerce platform built only from SharedKernel packages, consumed the way an outside service consumes them:
`PackageReference` to the packages packed from this checkout. Every service runs against real infrastructure in
containers, local stand-ins only. Its purpose is to prove the packages work together, and it has already found kernel
bugs that unit tests did not.

> **Status.** Every service is in: Catalog, Inventory, Ordering, Billing, Merchant, Notify and Reports reference 103 of
> the kernel's 104 packages directly, and `Shop.Coverage.Tests` fails if a new package has no home here. The one known
> gap is `Messaging.MassTransit.AzureServiceBus` (see the coverage row below). The older samples are retired next.

## What runs

| Resource | What it is | Kernel packages it exercises |
| --- | --- | --- |
| `catalog-api`, `catalog-api-2` | The Catalog service, two replicas (four-project shape) | Domain, Application + Pipeline + Pipeline.Caching + MediatR, Persistence.EfCore/Npgsql (RLS), Caching.FusionCache + Redis L2 + Redis.PubSub, Search.Meilisearch + Search.ElasticSearch, AI.Qdrant + AI.SemanticKernel, Storage.S3, FeatureManagement, Localization, Security.Oidc, Presentation.WebApi + OpenApi + GraphQL, ServiceDefaults (+ Localization, Security) |
| `inventory-api`, `inventory-api-2` | The Inventory service, two replicas (one project): gRPC over mutual TLS for Ordering, REST for merchants, a reconciliation job | Persistence.Npgsql + Dapper under RLS (schema created under the migration lock), Caching.Redis.Core + HashStore + DistributedLocking, Scheduling, Security.Mtls + ServiceDefaults.Security.Mtls, Security.Oidc, MultiTenancy (with a service-only header strategy), Presentation.Grpc + WebApi, Application + Pipeline + MediatR |
| `ordering-api` | The Ordering service (four-project shape): orders placed once however often they are submitted, fulfilled by a Temporal workflow that reserves stock in Inventory and takes payment in Billing (releasing the stock when the card is declined), status pushed over SignalR, cancelling behind an authenticator step-up | Persistence.EfCore + Auditing + Encryption under RLS, ServiceDefaults.Persistence, Messaging.MassTransit + RabbitMq + EfCore outbox, Idempotency.Redis (requests) + Idempotency.EfCore (messages), Workflows.Temporal, Communication.Grpc over mutual TLS, Communication.Rest with an API key, Caching.Redis.HashStore, Cryptography + Argon2, Security.Oidc + Totp, MultiTenancy, Presentation.WebApi + SignalR, Application + Pipeline + MediatR, Validation.FluentValidation |
| `billing-api` | The Billing service (one project): payments for Ordering's workflow, invoices signed inside Key Vault and compressed, merchant billing profiles with the IBAN envelope-encrypted under a Key Vault master key, signed webhooks to the merchant, receipts owed published through its outbox, the payment provider's callbacks, GDPR export and erasure | ServiceDefaults.Configuration.KeyVault (API key hashes and webhook secrets as configuration), Cryptography + Cryptography.KeyVault.Azure (signing, envelope encryption), Compression, Validation + Validation.FluentValidation (IBAN, BIC, VAT), DataPrivacy (marking, log redaction, data-subject requests), Security.ApiKey + Security.Oidc + Security.Abstractions, MultiTenancy (a key-only header strategy), Integration.Webhooks, Messaging.MassTransit + RabbitMq + EfCore outbox, Persistence.EfCore under RLS, Presentation.Core + WebApi, Application + Pipeline + MediatR, Configuration, Core, Execution, Domain |
| `merchant-api` | A merchant's own system: receives Billing's webhooks and accepts only those whose signature verifies | Integration.Webhooks (the receiver-side verifier), ServiceDefaults |
| `keyvault` | Lowkey Vault, an Azure Key Vault emulator, serving the Shop's development certificate; the AppHost provisions its keys and secrets | |
| `notify-worker` | The Notify worker (HTTP only for its probes): emails the customer a receipt and texts the merchant for every payment | Integration.Notifications.Abstractions + Email.SendGrid + Sms.Twilio (at WireMock through their `BaseAddress`), Messaging.MassTransit + RabbitMq with the publisher's caller, Validation (the merchant's phone), Configuration, Execution |
| `wiremock` | SendGrid and Twilio, stubbed (`wiremock/mappings`): they answer like the real APIs and record every request for the tests | |
| `reports-api` | The Reports service (one project): each tenant's sales, fed by Billing's receipts, exported as CSV, Excel or PDF straight into object storage behind a presigned download, and a sales statement rendered from HTML to PDF | Reporting.Abstractions + Csv + Spreadsheet + Pdf + Gotenberg, Storage.S3 (downloads) + Storage.Obs (the archive), Persistence.Npgsql + Dapper under RLS, Messaging.MassTransit + RabbitMq, Security.Oidc, MultiTenancy, Application + Pipeline + MediatR, Presentation.WebApi |
| `gotenberg` | Gotenberg 8 (headless Chromium): HTML to PDF over HTTP | |
| `rabbitmq` | RabbitMQ 4.3 with the delayed-exchange plugin (`masstransit/rabbitmq`) | |
| `temporal` | The Temporal development server (gRPC 7233, UI 8233) | |
| `postgres` | PostgreSQL 16 with pgvector; the production role split (`postgres/`) | |
| `redis` | Redis 7.4 | |
| `meilisearch`, `elasticsearch` | The storefront and back-office search engines | |
| `qdrant`, `ollama` | Vectors, and the embedding (`all-minilm`) and chat (`qwen2.5:0.5b`) models behind an OpenAI-compatible endpoint | |
| `minio` | S3, and the stand-in for Huawei OBS: the OBS provider (its own compatibility profile) runs against it path-style | |
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
| `Shop.Inventory.Tests` | Inventory's dependency graph, the service-only tenant strategy, the certificate allow-list, the SKU locks and the job schedule, over the kernel's Security, Caching and Scheduling fakes, and the gRPC service over Presentation.Testing's `TestServerCallContext` |
| `Shop.Ordering.Tests` | Ordering's four-project shape, idempotent placement, auditing, validation and cancellation (with its refund), the charge step through the real pipeline over the Persistence, Messaging and Application fakes, the gRPC adapter's error mapping (Communication fakes), the TOTP stores over the fake Redis services | the fulfilment consumer over the Workflows fakes |
| `Shop.Billing.Tests` | Billing's dependency graph, the API-key store and the key-only tenant strategy, invoices signed with the Cryptography fakes and read back verified (and a forged one refused), profile validation (IBAN checksum, VAT per country), payment rules, webhook subscriptions per tenant and the payment announcement over the Integration fakes |
| `Shop.Notify.Tests` | Notify's dependency graph and the receipt consumer over the Integration fakes: who is emailed and texted, with which template, under which delivery id, and what a refusing provider does |
| `Shop.Reports.Tests` | Reports' dependency graph, the export and statement use cases over the Reporting fakes (every sale, into the caller's tenant view, with a presigned download; unknown stores and formats refused; merchant names HTML-encoded), and a Gotenberg outage taking Reports out of readiness but not liveness (ServiceDefaults.Testing) |
| `Shop.E2E` | Whole flows through the real AppHost: sign-in, cache and backplane across replicas, Redis Pub/Sub, search, semantic search, the chat model, presigned downloads, tenant isolation, permissions, localization, GraphQL; reservations over mutual TLS (allow-list, rogue certificate, no certificate), no overselling under concurrency across replicas, a job that runs once per occurrence on two replicas; an order placed once per idempotency key, fulfilled or rejected by the Temporal workflow through the outbox and RabbitMQ, SignalR status pushes, another tenant's orders invisible, ciphertext at rest and the audit ledger, cancelling only after a TOTP step-up (and refunded); payment over REST with an API key, an invoice signed in Key Vault, the merchant's signed webhook, a declined card compensated, the provider's key enforced (401 without, 403 with the wrong client), the billing profile validated and its IBAN only ever shown masked, GDPR export and erasure; a receipt emailed through SendGrid and the merchant texted through Twilio, read back from WireMock; sales exported as CSV, Excel and PDF to S3 and to the OBS archive and downloaded through presigned URLs, and a statement rendered by Gotenberg. Skipped unless `SHOP_E2E=1`; CI builds it but does not run it |
| `Shop.Coverage.Tests` | Strict: every kernel package is referenced directly by some Shop project, except the known gaps, which must stay unreferenced. The one gap: `Messaging.MassTransit.AzureServiceBus` cannot run against the Service Bus emulator (it serves AMQP and its management API on different ports, and MassTransit 8.5 reaches both through one connection string), so Billing and Notify use RabbitMQ |

CI builds every Shop project against the packages of the same run and runs the unit tests; the end-to-end flows are
local only for now.

## Development PKI

Services that call each other over mutual TLS use a development CA the AppHost creates on first start
(`%TEMP%/shop-pki`, `ShopPki.cs`): a `localhost` server certificate Kestrel serves, and client certificates for
`ordering-api` and `rogue-service` (signed by the CA, on no allow-list). Delete the folder to issue a fresh set.
