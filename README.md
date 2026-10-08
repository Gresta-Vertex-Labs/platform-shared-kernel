<div align="center">

# Platform.SharedKernel

### The building blocks of a .NET 10 microservice platform — 104 NuGet packages, one version, an architecture the build enforces.

Write the business. The kernel brings the request context and tenancy, CQRS, PostgreSQL, messaging, caching,
storage, search, AI, security, APIs, workflows, jobs and reports — tested, observable and Kubernetes-ready.

[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)](global.json)
[![License: MIT](https://img.shields.io/badge/license-MIT-green)](LICENSE)
[![Packages](https://img.shields.io/badge/packages-104-blue)](#-the-package-catalogue)
[![Capabilities](https://img.shields.io/badge/capabilities-21-blue)](#-the-package-catalogue)
[![Analyzer rules](https://img.shields.io/badge/analyzer%20rules-45-8250df)](tools/Governance/README.md)
[![Release](https://img.shields.io/badge/release-1.0.0--rc.3-orange)](#-status)
[![CI](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/actions/workflows/ci.yml/badge.svg)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/actions/workflows/ci.yml)

[Why](#-why-sharedkernel) · [Architecture](#-architecture) · [Packages](#-the-package-catalogue) ·
[Quick start](#-quick-start) · [Samples](#-sample-services) · [Governance](#-governance-built-in) ·
[Build](#-building-this-repository) · [Contribute](#-contributing)

</div>

---

## ✨ Why SharedKernel

Every service in a microservice platform needs the same plumbing: who is calling and for which tenant, how a
command is validated, authorized and committed, how an event reaches another service, how an error becomes an HTTP
response, how a dependency reports that it is ready. **Platform.SharedKernel writes that plumbing once**, as small
packages with narrow jobs, so each service only writes its own business.

<table>
<tr>
<td width="50%" valign="top">

**🎯 Results, not exceptions**<br/>
Expected failures are `Result<T>` values with typed `Error`s, mapped once to RFC 9457 ProblemDetails, a gRPC
status or a message fault — the same answer on every channel.

</td>
<td width="50%" valign="top">

**🏢 Multi-tenant end to end**<br/>
One `TenantId` flows from the HTTP edge through the pipeline, PostgreSQL row-level security, the cache, storage,
search and the message bus. A missing tenant fails closed.

</td>
</tr>
<tr>
<td valign="top">

**🧱 Architecture the build enforces**<br/>
Every package has a tier. MSBuild rejects an illegal reference before compile (`SKTIER001`–`006`), and
architecture tests check the rules tiers cannot express.

</td>
<td valign="top">

**🧪 A test double for every contract**<br/>
20 `*.Testing` packages fake each capability faithfully — failure modes and tenant scope included — so a
service's unit tests need no containers.

</td>
</tr>
<tr>
<td valign="top">

**☸️ Kubernetes-native**<br/>
OpenTelemetry built in, `/health/live` and `/health/ready`, a startup gate, and every provider registers its own
readiness probe.

</td>
<td valign="top">

**⚖️ Licence-conscious**<br/>
MassTransit pinned to 8.5.x (Apache-2.0) and MediatR to 12.4.1 (MIT), behind the kernel's own contracts.
EPPlus, QuestPDF and iText7 were declined on licensing.

</td>
</tr>
</table>

---

## 🏛️ Architecture

A consuming service has four projects, and each references only the **tier** made for it. The source tree is
grouped the same way: each **zone** mirrors a service project and holds one folder per capability.

```mermaid
flowchart LR
    subgraph service["Your service"]
        direction TB
        Api["Api / Worker"]
        Infra["Infrastructure"]
        App["Application"]
        Dom["Domain"]
    end

    subgraph kernel["Platform.SharedKernel tiers"]
        direction TB
        Host["<b>Host</b> · 20<br/>ASP.NET Core, composition"]
        Adapter["<b>Adapter</b> · 38<br/>PostgreSQL, Redis, MassTransit, S3…"]
        Abs["<b>Abstractions</b> · 11<br/>provider-neutral contracts"]
        Model["<b>Model</b> · 2<br/>Domain, Contracts"]
        Found["<b>Foundation</b> · 10<br/>Result, request context, crypto"]
        Host --> Adapter --> Abs --> Model --> Found
    end

    Testing["<b>Testing</b> · 20<br/>fakes for test projects"] -.-> Host
    Tooling["<b>Tooling</b> · 3<br/>analyzers, arch tests, linter"]

    Api --> Host
    Infra --> Adapter
    App --> Abs
    Dom --> Model
```

| Tier | Packages | May reference | Referenced from |
|------|---:|---------------|-----------------|
| **Foundation** | 10 | Foundation | any project |
| **Model** | 2 | Foundation, Model · `Microsoft.Extensions.*.Abstractions` only | the **Domain** project |
| **Abstractions** | 11 | Foundation, Model, Abstractions · `Microsoft.Extensions.*.Abstractions` only | the **Application** project |
| **Adapter** | 38 | the tiers above + declared adapter edges · never ASP.NET Core | the **Infrastructure** project |
| **Host** | 20 | everything except Testing and Tooling | the **Api / Worker** project |
| **Testing** | 20 | everything except Tooling | test projects only |
| **Tooling** | 3 | nothing | the build |

```text
src/Foundation/       → any project        primitives, request context, configuration, crypto, validation
src/Model/            → Domain             Domain/ (DDD building blocks) · Contracts/ (wire contracts)
src/Application/      → Application        kernel-owned CQRS and the request pipeline
src/Infrastructure/   → Infrastructure     Caching · Persistence · Messaging · Storage · Search · AI ·
                                           Communication · Integration · Workflows · Idempotency · Scheduling · Reporting
src/Hosting/          → Api / Worker       Security · ServiceDefaults · Presentation
src/Testing/          → test projects      SharedKernel.Testing + Testcontainers fixtures for this repo
tools/Governance/     → the build          analyzers, architecture tests, linter
samples/                                   eight reference services built from the packed packages
```

A capability keeps its contracts, providers and test doubles side by side — `src/Infrastructure/Caching/` holds
`SharedKernel.Caching.Abstractions`, the FusionCache and Redis providers, and `SharedKernel.Caching.Testing`. The
zone says where a capability mainly belongs; the **tier** of each package decides what may reference it. Full rules:
[`CONTRIBUTING.md`](CONTRIBUTING.md#tiers) · what depends on what: [`docs/dependency-graph.md`](docs/dependency-graph.md).

---

## 📦 The package catalogue

**104 packages in 21 capability areas.** Pick a capability, open its page, take the packages you need. Every
capability page explains how its packages fit, how to start and what it guarantees.

### Every project

| Capability | Pkgs | What it gives a service |
|------------|---:|-------------------------|
| 🧩 [**Foundation**](src/Foundation/README.md) | 15 | `Result<T>` and `Error`, the request context and tenancy, validated options, feature flags, cryptography, validation of IBAN/VAT/card numbers, personal-data redaction, localized errors |

### The Domain project

| Capability | Pkgs | What it gives a service |
|------------|---:|-------------------------|
| 🏗️ [**Domain**](src/Model/Domain/README.md) | 1 | Entities, aggregates (audited, soft-delete, tenanted), value objects, strongly typed ids, specifications, `Money` |
| 📨 [**Contracts**](src/Model/Contracts/README.md) | 1 | Versioned integration events in a CloudEvents envelope, offset and cursor paging |

### The Application project

| Capability | Pkgs | What it gives a service |
|------------|---:|-------------------------|
| ⚙️ [**Application**](src/Application/README.md) | 5 | Kernel-owned commands and queries behind `ISender`, and a pipeline that traces, logs, authorizes, validates — and on request makes idempotent, transactional, audited and cached |

### The Infrastructure project

| Capability | Pkgs | What it gives a service |
|------------|---:|-------------------------|
| 🗄️ [**Persistence**](src/Infrastructure/Persistence/README.md) | 7 | PostgreSQL through EF Core and Dapper: one transaction, row-level security, optimistic concurrency, an HMAC-chained audit ledger, field encryption |
| ⚡ [**Caching**](src/Infrastructure/Caching/README.md) | 9 | Hybrid L1 + Redis caching with stampede protection, tenant-isolated keys, locks with fencing tokens, Redis hashes and Pub/Sub |
| 📬 [**Messaging**](src/Infrastructure/Messaging/README.md) | 6 | Integration events over MassTransit on RabbitMQ or Azure Service Bus, with a transactional outbox, retries, idempotent consumers and payload encryption |
| 🔁 [**Idempotency**](src/Infrastructure/Idempotency/README.md) | 4 | One atomic reservation contract for duplicate requests and messages, on Redis or PostgreSQL |
| 🪣 [**Storage**](src/Infrastructure/Storage/README.md) | 4 | Named and tenant file stores on S3, MinIO or Huawei OBS: streaming, presigned URLs, conditional writes |
| 🔎 [**Search**](src/Infrastructure/Search/README.md) | 4 | One search contract and filter language over Meilisearch and Elasticsearch, with safe index provisioning |
| 🤖 [**AI**](src/Infrastructure/AI/README.md) | 4 | Embeddings, tenant-scoped vector collections on Qdrant, and LLM orchestration on Semantic Kernel |
| 🔌 [**Communication**](src/Infrastructure/Communication/README.md) | 4 | Typed REST and gRPC clients with service discovery, outbound auth, mutual TLS, safe retries and `Result<T>` answers |
| 🌐 [**Integration**](src/Infrastructure/Integration/README.md) | 5 | Signed, retried, SSRF-guarded webhooks; email over SendGrid and SMS over Twilio |
| 🕰️ [**Workflows**](src/Infrastructure/Workflows/README.md) | 2 | Durable, tenant-scoped Temporal workflows whose activities send kernel commands |
| ⏱️ [**Scheduling**](src/Infrastructure/Scheduling/README.md) | 2 | Cron, recurring and one-shot jobs that run once across replicas |
| 📊 [**Reporting**](src/Infrastructure/Reporting/README.md) | 6 | Streaming CSV, Excel and PDF exports in constant memory, and HTML → PDF through Gotenberg |

### The Api / Worker project

| Capability | Pkgs | What it gives a service |
|------------|---:|-------------------------|
| 🔐 [**Security**](src/Hosting/Security/README.md) | 6 | OIDC/JWT with DPoP, managed API keys, client certificates and TOTP step-up, all behind one `IUserContext` |
| 🩺 [**ServiceDefaults**](src/Hosting/ServiceDefaults/README.md) | 8 | OpenTelemetry, health and readiness, rate limiting, the HTTP request context, tenant resolution, Key Vault configuration, request culture |
| 🖥️ [**Presentation**](src/Hosting/Presentation/README.md) | 7 | Minimal APIs with one ProblemDetails shape, ETags and endpoint modules; OpenAPI and versioning; gRPC, SignalR and GraphQL |

### Test projects and the build

| Capability | Pkgs | What it gives a service |
|------------|---:|-------------------------|
| 🧪 [**Testing**](src/Testing/README.md) | 1 + 19 | `FakeClock`, `TestRequestContext`, fakers — plus the catalogue of all 20 test-double packages kept beside their capabilities |
| 🛡️ [**Governance**](tools/Governance/README.md) | 3 | 45 Roslyn analyzer rules, ready-made architecture tests, and the shared `.editorconfig` + CSharpier format check |

<details>
<summary><b>Every package, one line each</b></summary>

#### `src/Foundation/` — referenced from every project

- 📁 **[Foundation](src/Foundation/README.md)** — primitives, the execution context and cross-cutting utilities · *15 packages*
  - [SharedKernel.Primitives](src/Foundation/SharedKernel.Primitives/README.md) `Foundation` — `Result<T>`, `Error`, `IClock`, `IIdGenerator`, SmartEnum, well-known headers, readiness probes
  - [SharedKernel.Execution](src/Foundation/SharedKernel.Execution/README.md) `Foundation` — `IRequestContext`, `TenantId`/`TenantScope`, `IUnitOfWork`: the caller on every channel
  - [SharedKernel.Core](src/Foundation/SharedKernel.Core/README.md) `Foundation` — railway extensions for `Result`, `ResultTry`, guard clauses, base exceptions
  - [SharedKernel.Configuration](src/Foundation/SharedKernel.Configuration/README.md) `Foundation` — options validated at startup, not at first use
  - [SharedKernel.FeatureManagement](src/Foundation/SharedKernel.FeatureManagement/README.md) `Foundation` — typed feature flags on OpenFeature, per-tenant rollouts
  - [SharedKernel.Compression](src/Foundation/SharedKernel.Compression/README.md) `Foundation` — framed Brotli/gzip with a decompression-size cap
  - [SharedKernel.Cryptography](src/Foundation/SharedKernel.Cryptography/README.md) `Foundation` — AES-256-GCM, envelope encryption, signatures, HMAC, password hashing, TOTP
  - [SharedKernel.Cryptography.Argon2](src/Foundation/SharedKernel.Cryptography.Argon2/README.md) `Adapter` — Argon2id password hashing as PHC strings
  - [SharedKernel.Cryptography.KeyVault.Azure](src/Foundation/SharedKernel.Cryptography.KeyVault.Azure/README.md) `Adapter` — Azure Key Vault keys for encryption and signing
  - [SharedKernel.DataPrivacy](src/Foundation/SharedKernel.DataPrivacy/README.md) `Foundation` — personal-data taxonomy, log redaction, masking, GDPR/KVKK data-subject requests
  - [SharedKernel.Localization](src/Foundation/SharedKernel.Localization/README.md) `Foundation` — translated error messages with typed arguments
  - [SharedKernel.Validation](src/Foundation/SharedKernel.Validation/README.md) `Foundation` — parsed value types: IBAN, BIC, card number, VAT, LEI, phone, national id…
  - [SharedKernel.Validation.FluentValidation](src/Foundation/SharedKernel.Validation.FluentValidation/README.md) `Adapter` — FluentValidation rules for those types
  - [SharedKernel.Cryptography.Testing](src/Foundation/SharedKernel.Cryptography.Testing/README.md) `Testing` — recording crypto fakes with failure simulation
  - [SharedKernel.FeatureManagement.Testing](src/Foundation/SharedKernel.FeatureManagement.Testing/README.md) `Testing` — a feature client with per-test flags

#### `src/Model/` — the Domain project

- 📁 **[Domain](src/Model/Domain/README.md)** — domain-driven design building blocks · *1 package*
  - [SharedKernel.Domain](src/Model/Domain/SharedKernel.Domain/README.md) `Model` — entities, aggregates, value objects, strongly typed ids, specifications, `Money`

- 📁 **[Contracts](src/Model/Contracts/README.md)** — cross-service wire contracts · *1 package*
  - [SharedKernel.Contracts](src/Model/Contracts/SharedKernel.Contracts/README.md) `Model` — versioned integration events in a CloudEvents envelope, offset and cursor paging

#### `src/Application/` — the Application project

- 📁 **[Application](src/Application/README.md)** — CQRS and the request pipeline · *5 packages*
  - [SharedKernel.Application](src/Application/SharedKernel.Application/README.md) `Abstractions` — commands, queries, handlers, `ISender` and pipeline markers, owned by the kernel
  - [SharedKernel.Application.Pipeline](src/Application/SharedKernel.Application.Pipeline/README.md) `Host` — one registration call: tracing, logging, metrics, authorization, validation, idempotency, auditing, transactions
  - [SharedKernel.Application.Pipeline.Caching](src/Application/SharedKernel.Application.Pipeline.Caching/README.md) `Host` — query caching and post-commit eviction
  - [SharedKernel.Application.Mediator.MediatR](src/Application/SharedKernel.Application.Mediator.MediatR/README.md) `Host` — MediatR 12.4.1 behind `ISender`, swappable
  - [SharedKernel.Application.Testing](src/Application/SharedKernel.Application.Testing/README.md) `Testing` — runs a request through the real pipeline, no mediator needed

#### `src/Infrastructure/` — the Infrastructure project

- 📁 **[Caching](src/Infrastructure/Caching/README.md)** — hybrid caching, distributed locks and Redis · *9 packages*
  - [SharedKernel.Caching.Abstractions](src/Infrastructure/Caching/SharedKernel.Caching.Abstractions/README.md) `Abstractions` — `ICacheService`, `ITenantCacheService`, `IDistributedLockService`
  - [SharedKernel.Caching.FusionCache](src/Infrastructure/Caching/SharedKernel.Caching.FusionCache/README.md) `Adapter` — the cache: stampede protection, fail-safe, optional encryption
  - [SharedKernel.Caching.Redis.Core](src/Infrastructure/Caching/SharedKernel.Caching.Redis.Core/README.md) `Adapter` — the one shared Redis connection, TLS and readiness
  - [SharedKernel.Caching.Redis](src/Infrastructure/Caching/SharedKernel.Caching.Redis/README.md) `Adapter` — Redis L2 and backplane for FusionCache
  - [SharedKernel.Caching.Redis.DistributedLocking](src/Infrastructure/Caching/SharedKernel.Caching.Redis.DistributedLocking/README.md) `Adapter` — locks, leases and fencing tokens
  - [SharedKernel.Caching.Redis.HashStore](src/Infrastructure/Caching/SharedKernel.Caching.Redis.HashStore/README.md) `Adapter` — typed Redis hash storage
  - [SharedKernel.Caching.Redis.PubSub](src/Infrastructure/Caching/SharedKernel.Caching.Redis.PubSub/README.md) `Adapter` — loss-tolerant Redis Pub/Sub signals
  - [SharedKernel.Caching.Testing](src/Infrastructure/Caching/SharedKernel.Caching.Testing/README.md) `Testing` — fake cache, tenant cache and distributed locks
  - [SharedKernel.Caching.Redis.Testing](src/Infrastructure/Caching/SharedKernel.Caching.Redis.Testing/README.md) `Testing` — fake Redis hashes and Pub/Sub

- 📁 **[Persistence](src/Infrastructure/Persistence/README.md)** — PostgreSQL through EF Core and Dapper · *7 packages*
  - [SharedKernel.Persistence.Abstractions](src/Infrastructure/Persistence/SharedKernel.Persistence.Abstractions/README.md) `Abstractions` — ORM-free repositories, paging, bulk mutations, cross-tenant scope
  - [SharedKernel.Persistence.Npgsql](src/Infrastructure/Persistence/SharedKernel.Persistence.Npgsql/README.md) `Adapter` — data sources, TLS, database roles, SQLSTATE classification
  - [SharedKernel.Persistence.EfCore](src/Infrastructure/Persistence/SharedKernel.Persistence.EfCore/README.md) `Adapter` — EF Core 10 in one call: conventions, unit of work, tenant filter, row-level security
  - [SharedKernel.Persistence.Dapper](src/Infrastructure/Persistence/SharedKernel.Persistence.Dapper/README.md) `Adapter` — hand-written SQL that joins the same transaction and tenant
  - [SharedKernel.Persistence.EfCore.Auditing](src/Infrastructure/Persistence/SharedKernel.Persistence.EfCore.Auditing/README.md) `Adapter` — tamper-evident, HMAC-chained audit ledger
  - [SharedKernel.Persistence.EfCore.Encryption](src/Infrastructure/Persistence/SharedKernel.Persistence.EfCore.Encryption/README.md) `Adapter` — field-level encryption, blind indexes, key rotation, crypto-shredding
  - [SharedKernel.Persistence.Testing](src/Infrastructure/Persistence/SharedKernel.Persistence.Testing/README.md) `Testing` — fake repositories and unit of work, PostgreSQL test servers

- 📁 **[Messaging](src/Infrastructure/Messaging/README.md)** — integration events over MassTransit · *6 packages*
  - [SharedKernel.Messaging.Abstractions](src/Infrastructure/Messaging/SharedKernel.Messaging.Abstractions/README.md) `Abstractions` — `IMessageBus`, `IEventPublisher`, scheduling and version translation
  - [SharedKernel.Messaging.MassTransit](src/Infrastructure/Messaging/SharedKernel.Messaging.MassTransit/README.md) `Adapter` — one fluent chain: retry, circuit breaker, idempotency, ordering, payload encryption
  - [SharedKernel.Messaging.MassTransit.RabbitMq](src/Infrastructure/Messaging/SharedKernel.Messaging.MassTransit.RabbitMq/README.md) `Adapter` — RabbitMQ transport with delayed delivery
  - [SharedKernel.Messaging.MassTransit.AzureServiceBus](src/Infrastructure/Messaging/SharedKernel.Messaging.MassTransit.AzureServiceBus/README.md) `Adapter` — Azure Service Bus transport
  - [SharedKernel.Messaging.MassTransit.EfCore](src/Infrastructure/Messaging/SharedKernel.Messaging.MassTransit.EfCore/README.md) `Adapter` — transactional outbox on the service's own DbContext
  - [SharedKernel.Messaging.Testing](src/Infrastructure/Messaging/SharedKernel.Messaging.Testing/README.md) `Testing` — in-memory bus and publisher with assertions

- 📁 **[Storage](src/Infrastructure/Storage/README.md)** — object storage · *4 packages*
  - [SharedKernel.Storage.Abstractions](src/Infrastructure/Storage/SharedKernel.Storage.Abstractions/README.md) `Abstractions` — named and tenant stores, streaming, presigned URLs, conditional writes
  - [SharedKernel.Storage.S3](src/Infrastructure/Storage/SharedKernel.Storage.S3/README.md) `Adapter` — Amazon S3, MinIO and S3-compatible storage
  - [SharedKernel.Storage.Obs](src/Infrastructure/Storage/SharedKernel.Storage.Obs/README.md) `Adapter` — Huawei Cloud OBS
  - [SharedKernel.Storage.Testing](src/Infrastructure/Storage/SharedKernel.Storage.Testing/README.md) `Testing` — in-memory named and tenant stores

- 📁 **[Search](src/Infrastructure/Search/README.md)** — full-text search · *4 packages*
  - [SharedKernel.Search.Abstractions](src/Infrastructure/Search/SharedKernel.Search.Abstractions/README.md) `Abstractions` — `ISearchIndex<T>`, a provider-neutral filter AST, index provisioning
  - [SharedKernel.Search.Meilisearch](src/Infrastructure/Search/SharedKernel.Search.Meilisearch/README.md) `Adapter` — Meilisearch: instant search, tenant search tokens
  - [SharedKernel.Search.ElasticSearch](src/Infrastructure/Search/SharedKernel.Search.ElasticSearch/README.md) `Adapter` — Elasticsearch: aggregations, cursor export, suggestions
  - [SharedKernel.Search.Testing](src/Infrastructure/Search/SharedKernel.Search.Testing/README.md) `Testing` — an in-memory search index that evaluates the filter AST

- 📁 **[AI](src/Infrastructure/AI/README.md)** — embeddings, vectors and LLMs · *4 packages*
  - [SharedKernel.AI.Abstractions](src/Infrastructure/AI/SharedKernel.AI.Abstractions/README.md) `Abstractions` — embedding generation, tenant-scoped vector collections, orchestration
  - [SharedKernel.AI.Qdrant](src/Infrastructure/AI/SharedKernel.AI.Qdrant/README.md) `Adapter` — Qdrant vector database
  - [SharedKernel.AI.SemanticKernel](src/Infrastructure/AI/SharedKernel.AI.SemanticKernel/README.md) `Adapter` — LLM orchestration on Microsoft Semantic Kernel
  - [SharedKernel.AI.Testing](src/Infrastructure/AI/SharedKernel.AI.Testing/README.md) `Testing` — deterministic embeddings and an in-memory vector store

- 📁 **[Communication](src/Infrastructure/Communication/README.md)** — outbound service-to-service calls · *4 packages*
  - [SharedKernel.Communication](src/Infrastructure/Communication/SharedKernel.Communication/README.md) `Adapter` — the shared base: per-client settings, service discovery, outbound auth, mutual TLS
  - [SharedKernel.Communication.Rest](src/Infrastructure/Communication/SharedKernel.Communication.Rest/README.md) `Adapter` — typed `HttpClient`s: safe retries, caller propagation, `Result<T>` instead of exceptions
  - [SharedKernel.Communication.Grpc](src/Infrastructure/Communication/SharedKernel.Communication.Grpc/README.md) `Adapter` — gRPC clients: deadline, retry policy, load balancing, rich status → `Result<T>`
  - [SharedKernel.Communication.Testing](src/Infrastructure/Communication/SharedKernel.Communication.Testing/README.md) `Testing` — a stub HTTP handler for REST clients and gRPC call fakes

- 📁 **[Integration](src/Infrastructure/Integration/README.md)** — delivery to destinations outside the platform · *5 packages*
  - [SharedKernel.Integration.Webhooks](src/Infrastructure/Integration/SharedKernel.Integration.Webhooks/README.md) `Adapter` — signed, retried, SSRF-guarded webhooks with secret rotation
  - [SharedKernel.Integration.Notifications.Abstractions](src/Infrastructure/Integration/SharedKernel.Integration.Notifications.Abstractions/README.md) `Abstractions` — `INotificationSender` for email and SMS
  - [SharedKernel.Integration.Notifications.Email.SendGrid](src/Infrastructure/Integration/SharedKernel.Integration.Notifications.Email.SendGrid/README.md) `Adapter` — SendGrid email over REST
  - [SharedKernel.Integration.Notifications.Sms.Twilio](src/Infrastructure/Integration/SharedKernel.Integration.Notifications.Sms.Twilio/README.md) `Adapter` — Twilio SMS over REST
  - [SharedKernel.Integration.Testing](src/Infrastructure/Integration/SharedKernel.Integration.Testing/README.md) `Testing` — in-memory webhook dispatcher and notification sender

- 📁 **[Workflows](src/Infrastructure/Workflows/README.md)** — durable execution · *2 packages*
  - [SharedKernel.Workflows.Temporal](src/Infrastructure/Workflows/SharedKernel.Workflows.Temporal/README.md) `Adapter` — Temporal workflows and activities, tenant-scoped dispatch, payload encryption
  - [SharedKernel.Workflows.Testing](src/Infrastructure/Workflows/SharedKernel.Workflows.Testing/README.md) `Testing` — in-memory workflow dispatcher

- 📁 **[Idempotency](src/Infrastructure/Idempotency/README.md)** — duplicate-request and duplicate-message protection · *4 packages*
  - [SharedKernel.Idempotency.Abstractions](src/Infrastructure/Idempotency/SharedKernel.Idempotency.Abstractions/README.md) `Abstractions` — one atomic reservation contract, `IIdempotencyStore`
  - [SharedKernel.Idempotency.Redis](src/Infrastructure/Idempotency/SharedKernel.Idempotency.Redis/README.md) `Adapter` — Redis store with atomic Lua
  - [SharedKernel.Idempotency.EfCore](src/Infrastructure/Idempotency/SharedKernel.Idempotency.EfCore/README.md) `Adapter` — PostgreSQL store with `INSERT … ON CONFLICT`
  - [SharedKernel.Idempotency.Testing](src/Infrastructure/Idempotency/SharedKernel.Idempotency.Testing/README.md) `Testing` — an in-memory idempotency store

- 📁 **[Scheduling](src/Infrastructure/Scheduling/README.md)** — background jobs · *2 packages*
  - [SharedKernel.Scheduling](src/Infrastructure/Scheduling/SharedKernel.Scheduling/README.md) `Adapter` — cron, recurring and one-shot jobs that run once across replicas
  - [SharedKernel.Scheduling.Testing](src/Infrastructure/Scheduling/SharedKernel.Scheduling.Testing/README.md) `Testing` — a recording job registry

- 📁 **[Reporting](src/Infrastructure/Reporting/README.md)** — exports and documents · *6 packages*
  - [SharedKernel.Reporting.Abstractions](src/Infrastructure/Reporting/SharedKernel.Reporting.Abstractions/README.md) `Abstractions` — streaming `IReportExporter<TRow>` and `IHtmlToPdfConverter`
  - [SharedKernel.Reporting.Csv](src/Infrastructure/Reporting/SharedKernel.Reporting.Csv/README.md) `Adapter` — RFC 4180 CSV in constant memory, formula-injection guard
  - [SharedKernel.Reporting.Spreadsheet](src/Infrastructure/Reporting/SharedKernel.Reporting.Spreadsheet/README.md) `Adapter` — streamed Excel (.xlsx) on SpreadCheetah
  - [SharedKernel.Reporting.Pdf](src/Infrastructure/Reporting/SharedKernel.Reporting.Pdf/README.md) `Adapter` — tabular PDF on PDFsharp/MigraDoc
  - [SharedKernel.Reporting.Gotenberg](src/Infrastructure/Reporting/SharedKernel.Reporting.Gotenberg/README.md) `Adapter` — HTML → PDF through a Gotenberg container
  - [SharedKernel.Reporting.Testing](src/Infrastructure/Reporting/SharedKernel.Reporting.Testing/README.md) `Testing` — in-memory exporters and HTML-to-PDF converter

#### `src/Hosting/` — the Api / Worker project

- 📁 **[Security](src/Hosting/Security/README.md)** — authentication · *6 packages*
  - [SharedKernel.Security.Abstractions](src/Hosting/Security/SharedKernel.Security.Abstractions/README.md) `Abstractions` — `IUserContext`: subject, tenant, roles, permissions, step-up signals
  - [SharedKernel.Security.Oidc](src/Hosting/Security/SharedKernel.Security.Oidc/README.md) `Host` — JWT bearer for any OIDC provider, DPoP, certificate-bound tokens, revocation
  - [SharedKernel.Security.ApiKey](src/Hosting/Security/SharedKernel.Security.ApiKey/README.md) `Host` — managed API keys for machine clients
  - [SharedKernel.Security.Mtls](src/Hosting/Security/SharedKernel.Security.Mtls/README.md) `Host` — client-certificate authentication, private CA trust
  - [SharedKernel.Security.Totp](src/Hosting/Security/SharedKernel.Security.Totp/README.md) `Host` — TOTP enrollment, step-up and recovery codes
  - [SharedKernel.Security.Testing](src/Hosting/Security/SharedKernel.Security.Testing/README.md) `Testing` — fake user context, test certificates and DPoP proofs

- 📁 **[ServiceDefaults](src/Hosting/ServiceDefaults/README.md)** — host composition · *8 packages*
  - [SharedKernel.ServiceDefaults](src/Hosting/ServiceDefaults/SharedKernel.ServiceDefaults/README.md) `Host` — OpenTelemetry, health endpoints, readiness, startup gate, rate limiting
  - [SharedKernel.ServiceDefaults.Security](src/Hosting/ServiceDefaults/SharedKernel.ServiceDefaults.Security/README.md) `Host` — the HTTP request context and correlation id middleware
  - [SharedKernel.ServiceDefaults.Persistence](src/Hosting/ServiceDefaults/SharedKernel.ServiceDefaults.Persistence/README.md) `Host` — database readiness checks
  - [SharedKernel.ServiceDefaults.Security.Mtls](src/Hosting/ServiceDefaults/SharedKernel.ServiceDefaults.Security.Mtls/README.md) `Host` — Kestrel client-certificate negotiation
  - [SharedKernel.ServiceDefaults.Configuration.KeyVault](src/Hosting/ServiceDefaults/SharedKernel.ServiceDefaults.Configuration.KeyVault/README.md) `Host` — Azure Key Vault as a configuration source
  - [SharedKernel.ServiceDefaults.Localization](src/Hosting/ServiceDefaults/SharedKernel.ServiceDefaults.Localization/README.md) `Host` — request culture from user, tenant or `Accept-Language`
  - [SharedKernel.MultiTenancy](src/Hosting/ServiceDefaults/SharedKernel.MultiTenancy/README.md) `Host` — tenant resolution (claim → header → database) and the tenant catalog
  - [SharedKernel.ServiceDefaults.Testing](src/Hosting/ServiceDefaults/SharedKernel.ServiceDefaults.Testing/README.md) `Testing` — in-memory tenant catalog and health-check assertions

- 📁 **[Presentation](src/Hosting/Presentation/README.md)** — inbound APIs · *7 packages*
  - [SharedKernel.Presentation.Core](src/Hosting/Presentation/SharedKernel.Presentation.Core/README.md) `Host` — authorization attributes and error presentation shared by HTTP and gRPC
  - [SharedKernel.Presentation.WebApi](src/Hosting/Presentation/SharedKernel.Presentation.WebApi/README.md) `Host` — minimal APIs: one ProblemDetails shape, typed results, endpoint modules, ETags, idempotency keys
  - [SharedKernel.Presentation.OpenApi](src/Hosting/Presentation/SharedKernel.Presentation.OpenApi/README.md) `Host` — API versioning, one OpenAPI document per version, Scalar
  - [SharedKernel.Presentation.Grpc](src/Hosting/Presentation/SharedKernel.Presentation.Grpc/README.md) `Host` — gRPC services with the same errors and authorization
  - [SharedKernel.Presentation.SignalR](src/Hosting/Presentation/SharedKernel.Presentation.SignalR/README.md) `Host` — hub error contract, request context and rate limiting
  - [SharedKernel.Presentation.GraphQL](src/Hosting/Presentation/SharedKernel.Presentation.GraphQL/README.md) `Host` — HotChocolate conventions
  - [SharedKernel.Presentation.Testing](src/Hosting/Presentation/SharedKernel.Presentation.Testing/README.md) `Testing` — gRPC and GraphQL test helpers

#### `src/Testing/` — test projects

- 📁 **[Testing](src/Testing/README.md)** — test doubles for every capability · *1 package*
  - [SharedKernel.Testing](src/Testing/SharedKernel.Testing/README.md) `Testing` — `FakeClock`, in-memory logger, `TestRequestContext`, fakers, assertions

#### `tools/Governance/` — the build

- 📁 **[Governance](tools/Governance/README.md)** — the rules the rest of the repo is held to · *3 packages*
  - [SharedKernel.Analyzers](tools/Governance/SharedKernel.Analyzers/README.md) `Tooling` — 45 Roslyn rules for the platform conventions, compiler-only
  - [SharedKernel.ArchitectureTests](tools/Governance/SharedKernel.ArchitectureTests/README.md) `Tooling` — prebuilt NetArchTest rules for dependency purity, provider isolation and secure defaults
  - [SharedKernel.Linter](tools/Governance/SharedKernel.Linter/README.md) `Tooling` — CSharpier format check for CI plus the shared `.editorconfig`

The same list by tier, with the service project that references each package, is generated in
[`docs/packages.md`](docs/packages.md).

</details>

---

## ⚡ Quick start

**1. Pin one version for every package** in your `Directory.Packages.props`:

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <SharedKernelVersion>1.0.0-rc.3</SharedKernelVersion>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="SharedKernel.Domain" Version="$(SharedKernelVersion)" />
    <PackageVersion Include="SharedKernel.Application" Version="$(SharedKernelVersion)" />
    <PackageVersion Include="SharedKernel.Application.Pipeline" Version="$(SharedKernelVersion)" />
    <PackageVersion Include="SharedKernel.ServiceDefaults" Version="$(SharedKernelVersion)" />
    <!-- one line per SharedKernel package you reference, always $(SharedKernelVersion) -->
  </ItemGroup>
</Project>
```

The feed and its `NuGet.Config` are in [`CONTRIBUTING.md` → Consuming the packages](CONTRIBUTING.md#consuming-the-packages).

**2. Reference each tier from its project:**

| Project | Takes | For example |
|---------|-------|-------------|
| `Orders.Domain` | Model | `SharedKernel.Domain` |
| `Orders.Application` | Abstractions | `SharedKernel.Application` |
| `Orders.Infrastructure` | Adapter | `SharedKernel.Persistence.EfCore`, `SharedKernel.Messaging.MassTransit.RabbitMq` |
| `Orders.Api` | Host | `SharedKernel.ServiceDefaults`, `SharedKernel.Presentation.WebApi`, `SharedKernel.Application.Pipeline` |

**3. Write the use case — the pipeline does the rest.** From [`samples/OrderApi`](samples/OrderApi/):

```csharp
// Application: the permission belongs to the use case, and is checked on every path it is sent from.
[RequirePermission(OrderPermissions.Cancel)]
public sealed record CancelOrderCommand(Guid Id) : ICommand;

public sealed class CancelOrderHandler(IOrderRepository repository) : ICommandHandler<CancelOrderCommand>
{
    public async Task<Result> Handle(CancelOrderCommand request, CancellationToken cancellationToken)
    {
        var order = await repository.GetAsync(request.Id, cancellationToken);
        if (order is null)
            return Result.Failure(Error.NotFound("order.notFound", $"Order {request.Id} was not found."));

        var cancelled = order.Cancel();
        if (cancelled.IsFailure)
            return cancelled;

        await repository.UpdateAsync(order, cancellationToken);
        return Result.Success();
    }
}
```

```csharp
// Api: telemetry, health, the caller, the pipeline and the HTTP boundary — each in one call.
builder.AddServiceDefaults();
builder.Services.AddSharedKernelRequestContext();
builder.Services.AddSharedKernelApplication(typeof(PlaceOrderCommand).Assembly, app => app.UseMediatR());
builder.Services.AddHealthChecks().AddSharedKernelReadiness();
builder.AddSharedKernelWebApi();

var app = builder.Build();
app.UseSharedKernelRequestContext();
app.UseSharedKernelWebApi();
app.MapDefaultHealthCheckEndpoints();
app.MapEndpoints();
```

Tracing, logging, metrics, authorization and validation run on every request. Idempotency, transactions, auditing
and caching are one `With…()` each, and the host refuses to start when a stage's dependency is missing. An anonymous
caller gets a 401, a caller without `orders.cancel` a 403, a missing order a 404 — all as RFC 9457 problem details.
The [samples guide](samples/README.md) walks through a whole service.

---

## 🚀 Sample services

Eight runnable services, built only from the **packed** packages and run in CI against real infrastructure.
Start with [`samples/README.md`](samples/README.md).

| Sample | Shows | Runs against |
|--------|-------|--------------|
| [**OrderApi**](samples/OrderApi/) | The four-project service shape, pinned by an architecture test | nothing external |
| [**BillingApi**](samples/BillingApi/) | The full persistence stack: EF Core + Dapper, row-level security, field encryption, audit ledger | PostgreSQL |
| [**ShippingApi**](samples/ShippingApi/) | Messaging: publish/send, delayed delivery, idempotent consumers, the caller across the bus | RabbitMQ |
| [**DocumentsApi**](samples/DocumentsApi/) | Object storage and reports: named and tenant stores, presigned links, CSV/Excel/PDF | MinIO, Gotenberg |
| [**CatalogApi**](samples/CatalogApi/) | Search on Meilisearch and Elasticsearch side by side | Meilisearch, Elasticsearch |
| [**CheckoutApi**](samples/CheckoutApi/) → [**InventoryApi**](samples/InventoryApi/) | Two services talking: typed REST and gRPC clients, an API key, safe retries, errors returned as `Result` | nothing external |
| [**Shop**](samples/Shop/) | The kernel as a real platform: an Aspire-orchestrated system with OIDC, mutual-TLS gRPC, caching across replicas, locks, scheduled jobs, search and AI | containers via .NET Aspire |

---

## 🛡️ Governance, built in

The conventions are not a wiki page — the build and the test suites hold them.

| Layer | What it catches |
|-------|-----------------|
| **Tier check** (`eng/SharedKernelTiers.targets`) | An illegal reference fails the build before compile: `SKTIER000`–`SKTIER006`, ASP.NET Core below Host included |
| **45 analyzer rules** ([`SharedKernel.Analyzers`](tools/Governance/SharedKernel.Analyzers/README.md)) | `[LoggerMessage]`-only logging, no discarded `Result`, `IClock` instead of `DateTime.UtcNow`, no raw SDK clients, no magic strings, deterministic workflows |
| **Architecture tests** ([`SharedKernel.ArchitectureTests`](tools/Governance/SharedKernel.ArchitectureTests/README.md)) | Purity rules tiers cannot express — provider isolation, no Contracts ↔ Domain, secure defaults — reusable from your own test suite |
| **Public API tracking** | Every public member is recorded in `PublicAPI.*.txt`; an unrecorded change fails the build |
| **One README standard** ([`docs/package-readme-standard.md`](docs/package-readme-standard.md)) | Every package README has the same shape, absolute links and current API, checked by a test |
| **Release train** | One `vX.Y.Z` tag runs every gate — tier check, both test lanes, every consumer harness and sample — then publishes all 104 packages together |

---

## 📍 Status

> [!NOTE]
> **Release candidate.** All 104 packages are published to GitHub Packages at **`1.0.0-rc.3`**. The public API is
> settling; breaking changes are still possible before `1.0.0`.

- ✅ Seven tiers enforced by the build; one execution context across HTTP, gRPC, messages, workflows and jobs
- ✅ Kernel-owned CQRS with a mediator-agnostic pipeline
- ✅ A release train that gates on every suite, consumer harness and sample before publishing the whole set
- ✅ Eight sample services, including the Aspire-orchestrated `Shop` platform
- 🔜 Pre-publish reviews of the remaining capabilities, then `1.0.0`

---

## 🛠️ Building this repository

You need the .NET 10 SDK ([`global.json`](global.json)); the Integration lane also needs Docker.

```bash
dotnet build Platform.SharedKernel.slnx -c Release
dotnet test  Platform.SharedKernel.Unit.slnf -c Release --no-build          # no Docker
dotnet test  Platform.SharedKernel.Integration.slnf -c Release --no-build   # Testcontainers
dotnet pack  Platform.SharedKernel.slnx -c Release --no-build               # into nupkgs/
```

<details>
<summary><b>Build-free checks CI runs (seconds each)</b></summary>

| Check | What it catches |
|-------|-----------------|
| `bash eng/verify-solution-filters.sh` | a test project in no lane or both, a project in the wrong solution folder |
| `bash eng/verify-path-lengths.sh` | a path too long for a Windows checkout (250 characters at `C:\Github\platform-shared-kernel\`) |
| `bash eng/verify-markdown-links.sh` | a Markdown link pointing at a moved or renamed file |
| `dotnet run eng/generate-package-index.cs -- --check` | a stale `docs/packages.md`, `docs/dependency-graph.md` or tier filter (run without `--check` to regenerate) |
| `bash eng/verify-tier-errors.sh` | the tier check no longer failing the build |
| `bash eng/verify-packages.sh nupkgs` | a packed set that is not exactly the release set |

</details>

Build output goes to `artifacts/`, packages to `nupkgs/`. To work on one tier, open its solution filter in
[`eng/solution-filters/`](eng/solution-filters/). **Releasing** is pushing a `v<major>.<minor>.<patch>[-prerelease]`
tag on `main` — [`CONTRIBUTING.md` → Versioning and releases](CONTRIBUTING.md#versioning-and-releases).

| Where | What |
|-------|------|
| [`CONTRIBUTING.md`](CONTRIBUTING.md) | Build, test, add a package, open a pull request |
| [`eng/README.md`](eng/README.md) | Build internals: tier check, package checks, versioning, CI workflows |
| [`docs/packages.md`](docs/packages.md) · [`docs/dependency-graph.md`](docs/dependency-graph.md) | Generated: every package by tier · Mermaid dependency graphs |
| `src/{Zone}/{Capability}/README.md` | The page of one capability |
| [`CLAUDE.md`](CLAUDE.md) · `src/{Zone}/{Capability}/CLAUDE.md` · `state-map.md` | Maintainer rules and the living work board |

---

## 🤝 Contributing

Contributions are welcome. Read [`CONTRIBUTING.md`](CONTRIBUTING.md) first: the tier rules the build enforces, the
conventions the analyzers check, and what must pass before a pull request can merge. Everyone taking part follows
the [Code of Conduct](CODE_OF_CONDUCT.md).

**AI-assisted development.** The repository is set up for [Claude Code](https://claude.com/claude-code): every
capability has a `CLAUDE.md` with its rules, and `.claude/` holds an architecture lead, a DevOps lead, and a planner
and implementer per capability, driven by skills such as `/arch`, `/dispatch-phase`, `/implement-phase <domain>` and
`/sync-brain`. It is optional — the rules it follows are the ones in `CONTRIBUTING.md`.

---

<div align="center">

**🔒 Security** — report vulnerabilities privately, as described in [`SECURITY.md`](SECURITY.md).

Released under the [MIT License](LICENSE) · © 2026 Gresta-Vertex-Labs

</div>
