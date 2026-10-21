---
name: sharedkernel
description: Use for any infrastructure or cross-cutting need in a service built on Platform.SharedKernel: persistence (PostgreSQL), messaging and integration events, caching, idempotency, HTTP/gRPC/SignalR/GraphQL endpoints, authentication and authorization, multi-tenancy, outbound REST/gRPC calls, background and scheduled jobs, durable workflows, file storage, search, AI/embeddings, email/SMS/webhooks, report export, logging, telemetry, health checks, and unit/integration tests. Tells you which package to use, which project references it, and how to wire it.
---

# Platform.SharedKernel

This skill routes a need to the SharedKernel package that covers it, the service project that references
that package, and the entry point to call. For multi-package tasks it points to a recipe. API detail
(configuration keys, overloads, pitfalls) is never here: it is in each package's README.

## 1. Version and detail lookup

This skill matches SharedKernel **{{SharedKernelVersion}}**. Every package of a service must be on that
one version (`SharedKernelVersion` in `Directory.Packages.props`).

Before writing code against a package, read its README at the version the service restored:

```
~/.nuget/packages/<package id lowercased>/{{SharedKernelVersion}}/README.md
```

Use `$NUGET_PACKAGES` instead of `~/.nuget/packages` when it is set. If the folder is missing, run
`dotnet restore` first. The fallback is the README on GitHub at tag `v{{SharedKernelVersion}}`.
The README wins over anything you remember: names and options change between versions.

## 2. Non-negotiables

- **Four projects, fixed references.** Domain → `SharedKernel.Domain`. Application →
  `SharedKernel.Application` plus the `.Abstractions` packages it needs. Infrastructure → the provider
  (adapter) packages. Api/Worker → the host packages, plus its own Application and Infrastructure. A
  package's README Install table names its project; the service's architecture tests catch a wrong
  placement (write-tests.md).
- **Expected failures are values.** Return `Result`/`Result<T>` with an `Error`; throw only for bugs and
  misconfiguration. Wrap throwing third-party calls with `ResultTry.TryAsync`.
- **Logging is `[LoggerMessage]` only**, with an explicit `EventId` and PascalCase placeholders. Never
  log tenant ids, correlation ids, URLs or exception messages; they flow through the request context and
  OpenTelemetry.
- **Use cases go through `ISender`.** Commands and queries are `ICommand`/`ICommand<T>`/`IQuery<T>`.
  Never reference MediatR from your own code.
- **The caller comes from `IRequestContext`**: tenant, actor, correlation id, permissions. Tenant values
  are `TenantId` (or `TenantScope`), never a `string` or `Guid`. A missing tenant fails closed.
- **Options** are bound with `AddValidatedOptions<TOptions>(configuration)` on a type that implements
  `ISectionBoundOptions`; never `services.Configure`.
- **No magic strings** for header names, claim types, configuration or cache keys: use a named constant.
- **Recipes decide packages and APIs; the service's own project guidance decides folders and file
  placement.** Follow a recipe's package choices and calls, not its folder names.
- **Never hand-roll what a package covers.** If a need is not in the table below, say so and ask; do not
  invent a package or an API.
- **Analyzers.** Reference `SharedKernel.Analyzers` in every project (it ships every rule at Warning) and
  raise the rules you rely on to `error` in the service's `.editorconfig`. The analyzers and the
  service's architecture tests are the guardrail; this skill only steers.

## 3. Needs

"Reference from" names the service project. Open the recipe for anything that spans packages; every recipe
file is in the `recipes/` folder next to this file.

| Need | Package(s) | Reference from | Entry point | Recipe |
| --- | --- | --- | --- | --- |
| Start a new service | `SharedKernel.Domain`, `SharedKernel.Application`, `SharedKernel.ServiceDefaults` | Domain; Application; Api | `AddServiceDefaults()`, `MapDefaultHealthCheckEndpoints()` | new-service.md |
| Register handlers and the request pipeline | `SharedKernel.Application.Pipeline`, `SharedKernel.Application.Mediator.MediatR` | Api | `AddSharedKernelApplication(assemblies, app => app.UseMediatR())` | new-service.md |
| A command or query and its handler | `SharedKernel.Application` | Application | `ICommand<T>`, `IQuery<T>`, `ICommandHandler<,>`, `IQueryHandler<,>` | new-service.md |
| Request validation | `SharedKernel.Application`, `SharedKernel.Validation.FluentValidation` | Application; Infrastructure | `IRequestValidator<T>`, `AddFluentValidationRequestValidators(assembly)` | new-service.md |
| Domain-event handlers | `SharedKernel.Application` | Application | `IDomainEventHandler<TEvent>` | new-service.md |
| Entities, aggregates, value objects, domain events | `SharedKernel.Domain` | Domain | `AggregateRoot<TId>`, `TenantedAggregateRoot<TId>`, `ValueObject`, `IDomainEvent` | new-service.md |
| Money and currencies | `SharedKernel.Domain` | Domain | `Money`, `Currency` | new-service.md |
| An integration event contract or paging DTO | `SharedKernel.Contracts` | Application (or a shared contracts project) | `IIntegrationEvent`, `[IntegrationEvent]`, `PageRequest`, `PagedList<T>` | add-messaging.md |
| EF Core on PostgreSQL | `SharedKernel.Persistence.EfCore`, `SharedKernel.Persistence.Abstractions` | Infrastructure; Application | `AddSharedKernelPostgres<TContext>("name", …)`, `IRepository<T, TId>` | add-persistence.md |
| Connections, TLS and database roles | `SharedKernel.Persistence.Npgsql` | Infrastructure | `AddSharedKernelNpgsql(…)` | add-persistence.md |
| Hand-written SQL | `SharedKernel.Persistence.Dapper` | Infrastructure | `AddSharedKernelDapper(…)`, `IDbSessionFactory` | add-persistence.md |
| One transaction per command | `SharedKernel.Application.Pipeline` | Api | `app.WithTransactions()`, `IUnitOfWork` | add-persistence.md |
| Tamper-evident audit trail | `SharedKernel.Persistence.EfCore.Auditing` | Infrastructure | `UseAuditTrail()`, `app.WithAuditing()`, `IAuditableRequest` | add-persistence.md |
| Encrypted columns | `SharedKernel.Persistence.EfCore.Encryption` | Infrastructure | `UseFieldEncryption()` | add-persistence.md |
| Database readiness check | `SharedKernel.ServiceDefaults.Persistence` | Api | `AddDatabaseReadinessCheck<TContext>()` | add-persistence.md |
| Row-level tenant isolation | `SharedKernel.Persistence.EfCore` | Infrastructure | `UseMultiTenancy(rowLevelSecurity: true)`, `TenantedDbContext` | add-multi-tenancy.md |
| Resolve the tenant of an HTTP request | `SharedKernel.MultiTenancy` | Api | `AddSharedKernelMultiTenancy()`, `TenantResolutionMiddleware` | add-multi-tenancy.md |
| Publish an integration event or send a message | `SharedKernel.Messaging.Abstractions`, `SharedKernel.Messaging.MassTransit` | Application; Infrastructure | `IEventPublisher`, `IMessageBus`, `AddSharedKernelMessaging(configuration)` | add-messaging.md |
| RabbitMQ transport | `SharedKernel.Messaging.MassTransit.RabbitMq` | Infrastructure | `UseRabbitMq(…)` | add-messaging.md |
| Azure Service Bus transport | `SharedKernel.Messaging.MassTransit.AzureServiceBus` | Infrastructure | `UseAzureServiceBus(…)` | add-messaging.md |
| Publish atomically with a database write | `SharedKernel.Messaging.MassTransit.EfCore` | Infrastructure | `WithEntityFrameworkOutbox<TDbContext>()` | add-messaging.md |
| Consume a message | `SharedKernel.Messaging.MassTransit` | Infrastructure | `ConsumerBase<T>`, `AddConsumer<T>()` | add-messaging.md |
| Cache contracts | `SharedKernel.Caching.Abstractions` | Application | `ICacheService`, `ITenantCacheService`, `CachePolicy` | add-caching.md |
| Cache implementation | `SharedKernel.Caching.FusionCache` | Infrastructure | `AddSharedKernelCaching(…)` | add-caching.md |
| Redis as the shared second-level cache | `SharedKernel.Caching.Redis`, `SharedKernel.Caching.Redis.Core` | Infrastructure | `AddRedisConnection(configuration)`, `AddRedisL2()` | add-caching.md |
| Cache a query result, evict after commit | `SharedKernel.Application`, `SharedKernel.Application.Pipeline.Caching` | Application; Api | `ICacheableQuery<T>`, `IInvalidatesCache`, `app.WithCaching()` | add-caching.md |
| Distributed lock or lease | `SharedKernel.Caching.Redis.DistributedLocking` | Infrastructure | `AddRedisDistributedLocking()`, `IDistributedLockService` | add-caching.md |
| Redis hashes as typed records | `SharedKernel.Caching.Redis.HashStore` | Infrastructure | `AddRedisHashService()`, `AddTypedHashStore<…>()` | add-caching.md |
| Redis pub/sub between instances | `SharedKernel.Caching.Redis.PubSub` | Infrastructure | `AddRedisChannelService()` | add-caching.md |
| Duplicate-submission protection | `SharedKernel.Idempotency.Abstractions`, `SharedKernel.Idempotency.Redis` | Application; Infrastructure | `IIdempotentRequest`, `app.WithIdempotency()`, `AddRedisIdempotency(…)` | add-idempotency.md |
| Idempotency store in PostgreSQL | `SharedKernel.Idempotency.EfCore` | Infrastructure | `AddEfCoreIdempotency(…)` | add-idempotency.md |
| HTTP API, problem details, typed results | `SharedKernel.Presentation.WebApi` | Api | `AddSharedKernelWebApi()`, `UseSharedKernelWebApi()`, `IEndpointModule`, `MapEndpoints()` | add-http-endpoint.md |
| Endpoint authorization without a command | `SharedKernel.Presentation.Core` | Api | `[RequireEndpointPermission]`, `[RequireRole]`, `[RequireFreshAuthentication]` | add-http-endpoint.md |
| OpenAPI document and versioning | `SharedKernel.Presentation.OpenApi` | Api | `AddSharedKernelOpenApi()`, `MapSharedKernelOpenApi()` | add-http-endpoint.md |
| gRPC server | `SharedKernel.Presentation.Grpc` | Api | `AddSharedKernelGrpc()` | add-http-endpoint.md |
| SignalR hubs | `SharedKernel.Presentation.SignalR` | Api | `AddSharedKernelSignalR()` | add-http-endpoint.md |
| GraphQL | `SharedKernel.Presentation.GraphQL` | Api | `AddSharedKernelGraphQL()` | add-http-endpoint.md |
| Request context in an HTTP host | `SharedKernel.ServiceDefaults.Security` | Api | `AddSharedKernelRequestContext()`, `UseSharedKernelRequestContext()` | add-authentication.md |
| Permission gate on a use case | `SharedKernel.Application` | Application | `[RequirePermission("…")]` | add-authentication.md |
| OIDC / JWT bearer | `SharedKernel.Security.Oidc` | Api | `AddOidcAuthentication(…)` | add-authentication.md |
| API keys | `SharedKernel.Security.ApiKey` | Api | `AddManagedApiKeyAuthentication<TStore>(…)` | add-authentication.md |
| Client certificates (mTLS) | `SharedKernel.Security.Mtls`, `SharedKernel.ServiceDefaults.Security.Mtls` | Api | `AddMtlsAuthentication<TValidator>(…)`, `AddMtlsClientCertificate(…)` | add-authentication.md |
| TOTP step-up | `SharedKernel.Security.Totp` | Api | `AddTotpStepUp<TStore, TRecoveryStore>()` | add-authentication.md |
| The authenticated principal (host code only) | `SharedKernel.Security.Abstractions` | Api | `IUserContext` (application code reads `IRequestContext`) | add-authentication.md |
| Outbound REST client | `SharedKernel.Communication`, `SharedKernel.Communication.Rest` | Infrastructure | `AddSharedKernelCommunication(configuration).AddRestClient<IClient, Client>("name")` | add-outbound-call.md |
| Outbound gRPC client | `SharedKernel.Communication.Grpc` | Infrastructure | `AddGrpcClient<TClient>("name")` | add-outbound-call.md |
| Publish my service's contracts and client | `SharedKernel.Contracts`, `SharedKernel.Communication.Rest` | A shared contracts project; Infrastructure | `[IntegrationEvent("{context}.{name}", Version = n)]`, `AddRestClient<IClient, Client>("name")` | add-service-contracts.md |
| Call or consume another service | `SharedKernel.Communication.Rest`, `SharedKernel.Communication.Grpc`, `SharedKernel.Messaging.Abstractions` | Application (port); Infrastructure (client, `ConsumerBase<T>`) | A port in Application, `AddRestClient<IClient, Client>("name")` or `AddGrpcClient<TClient>("name")`, `ConsumerBase<T>` | add-service-contracts.md |
| Recurring or one-shot job, once across replicas | `SharedKernel.Scheduling` | Infrastructure | `AddSharedKernelScheduling()`, `AddRecurring<TCommand>(…)`, `IScheduledJobRegistry` | add-background-job.md |
| Durable multi-step process with compensation | `SharedKernel.Workflows.Temporal` | Infrastructure | `AddSharedKernelTemporalWorkflows(…)`, `IWorkflowDispatcher`, `WorkflowBase` | add-long-running-workflow.md |
| Files in S3 or an S3-compatible store | `SharedKernel.Storage.Abstractions`, `SharedKernel.Storage.S3` | Application; Infrastructure | `IFileStorage`, `AddSharedKernelStorage().AddS3(configuration).AddStore("name")` | add-file-storage.md |
| Files in Huawei OBS | `SharedKernel.Storage.Obs` | Infrastructure | `AddObs(…)` | add-file-storage.md |
| Full-text search on Meilisearch | `SharedKernel.Search.Abstractions`, `SharedKernel.Search.Meilisearch` | Application; Infrastructure | `ISearchIndex<T>`, `AddSharedKernelMeilisearchSearch(…)` | add-search.md |
| Full-text search on Elasticsearch | `SharedKernel.Search.ElasticSearch` | Infrastructure | `AddSharedKernelElasticSearchSearch(…)` | add-search.md |
| Vector search | `SharedKernel.AI.Abstractions`, `SharedKernel.AI.Qdrant` | Application; Infrastructure | `IVectorCollection<T>`, `AddSharedKernelQdrant(…)` | add-ai.md |
| Embeddings and chat models | `SharedKernel.AI.SemanticKernel` | Infrastructure | `AddSharedKernelSemanticKernel(…)`, `IEmbeddingGenerator`, `ISemanticKernel` | add-ai.md |
| Email | `SharedKernel.Integration.Notifications.Abstractions`, `SharedKernel.Integration.Notifications.Email.SendGrid` | Application; Infrastructure | `INotificationSender`, `AddSharedKernelNotifications().AddSendGridEmailNotifications(…)` | add-notifications-webhooks.md |
| SMS | `SharedKernel.Integration.Notifications.Sms.Twilio` | Infrastructure | `AddTwilioSmsNotifications(…)` | add-notifications-webhooks.md |
| Outbound signed webhooks | `SharedKernel.Integration.Webhooks` | Infrastructure | `AddSharedKernelWebhooks(…)`, `IWebhookDispatcher` | add-notifications-webhooks.md |
| CSV, Excel or PDF export | `SharedKernel.Reporting.Abstractions`, `SharedKernel.Reporting.Csv`, `SharedKernel.Reporting.Spreadsheet`, `SharedKernel.Reporting.Pdf` | Application; Infrastructure | `IReportExporter<TRow>`, `AddSharedKernelReporting().AddCsv(…)` | add-report-export.md |
| HTML to PDF | `SharedKernel.Reporting.Gotenberg` | Infrastructure | `IHtmlToPdfConverter`, `AddGotenberg(…)` | add-report-export.md |
| Telemetry, health, readiness, rate limiting | `SharedKernel.ServiceDefaults` | Api | `AddServiceDefaults()`, `AddSharedKernelReadiness()`, `AddSharedKernelRateLimiting()` | new-service.md |
| A readiness check for your own dependency | `SharedKernel.Primitives`, `SharedKernel.ServiceDefaults` | Infrastructure; Api | `IReadinessProbe`, `AddReadinessProbe<T>()` | new-service.md |
| Configuration from Azure Key Vault | `SharedKernel.ServiceDefaults.Configuration.KeyVault` | Api | `AddSharedKernelKeyVaultConfiguration(…)` | new-service.md |
| Request culture | `SharedKernel.ServiceDefaults.Localization` | Api | `AddSharedKernelLocalization()` | new-service.md |
| Hashing, encryption, signing, HMAC | `SharedKernel.Cryptography` | Infrastructure | `AddSharedKernelCryptography(configuration)` | add-persistence.md |
| Password hashing with Argon2id | `SharedKernel.Cryptography.Argon2` | Infrastructure | `AddArgon2id()` | add-authentication.md |
| Keys held in Azure Key Vault | `SharedKernel.Cryptography.KeyVault.Azure` | Infrastructure | `AddAzureKeyVaultEncryption()`, `AddAzureKeyVaultSigning()` | add-persistence.md |
| Feature flags | `SharedKernel.FeatureManagement` | Application; Api | `FeatureFlag.Boolean(key, default)`, `AddSharedKernelFeatureManagement()` | new-service.md |
| IBAN, BIC, card, VAT and national-id values | `SharedKernel.Validation` | Domain; Application | `Iban.Create(…)` → `Result<Iban>` | new-service.md |
| Personal-data marking and redaction | `SharedKernel.DataPrivacy` | any | `AddRedaction()`, data-classification attributes | new-service.md |
| Unit and integration tests | `SharedKernel.Testing` and the capability `.Testing` packages | Tests | `FakeClock`, `TestRequestContext`, `ApplicationPipelineTestHarness` | write-tests.md |

<!-- not-routed: SharedKernel.Caching.Redis.Core — reached through the Redis caching, locking, hash and pub/sub rows; listed there -->

## 4. Package catalog

Every package at this version, its tier and the project that references it. Purpose is the first
sentence of the package README.

<!-- BEGIN GENERATED: package-catalog -->
| Package | Tier | Reference from | Purpose |
| --- | --- | --- | --- |
| `SharedKernel.Compression` | Foundation | any | Brotli and gzip compression for byte arrays, spans and streams behind one IPayloadCompressor — where a cut-short payload fails instead of silently returning part of the data, and a tiny malicious payload cannot expand until the process runs out of memory. |
| `SharedKernel.Configuration` | Foundation | any | Options-pattern registration that fails at startup, not at first use: AddValidatedOptions binds a section, validates it, and makes IHost.StartAsync() throw when it is wrong. |
| `SharedKernel.Core` | Foundation | any | Result chaining, guard clauses and exception boundaries for the Result<T>, Result and Error types of SharedKernel.Primitives — the operations you use them with every day, with no registration. |
| `SharedKernel.Cryptography` | Foundation | any | Encryption, signing, password hashing, secure randomness and one-time passwords for .NET services, with safe defaults and nothing to tune by accident. |
| `SharedKernel.DataPrivacy` | Foundation | any | Mark personal data once, and it is masked in every log line — plus masking helpers, pseudonymization, and the contract each service implements to export or erase a person's data under GDPR and KVKK. |
| `SharedKernel.Execution` | Foundation | any | The execution context every package of a service shares: who is calling, which tenant the work belongs to, which correlation id follows it, how work commits, and how an audited action is recorded. |
| `SharedKernel.FeatureManagement` | Foundation | any | Feature flags on the OpenFeature standard. |
| `SharedKernel.Localization` | Foundation | any | Translated error messages with typed, named arguments. |
| `SharedKernel.Primitives` | Foundation | any | The base every SharedKernel package depends on: failures as values (Result<T>, Error), a testable clock, a richer enum, the readiness-probe contract, and the registries that stop two packages disagreeing about a wire identifier. |
| `SharedKernel.Validation` | Foundation | any | Validated value types for financial and identity data — Iban, Bic, CardNumber, VatNumber, NationalId, CountryCode, CurrencyCode, PhoneNumber, Lei, AbaRoutingNumber, SepaCreditorId. |
| `SharedKernel.Contracts` | Model | Domain | Cross-service wire contracts for .NET services: integration events in a validated CloudEvents 1.0 envelope, and paged results with validated requests and opaque cursors — shapes that cross a process boundary, with no business logic and no I/O. |
| `SharedKernel.Domain` | Model | Domain | Domain-driven design building blocks for .NET services — aggregates, value objects, strongly-typed identifiers, business rules, specifications, policies and money — as pure domain code with no I/O, no system clock and no infrastructure dependency. |
| `SharedKernel.AI.Abstractions` | Abstractions | Application | The contracts your application code uses for embeddings, tenant-scoped vector retrieval and chat completion. |
| `SharedKernel.Application` | Abstractions | Application | The CQRS vocabulary every service speaks — the kernel's own request, handler, sender and pipeline-behavior contracts, commands, queries, validators, domain-event handlers and the markers the pipeline keys off — with no mediator library in it, so an Application project references this and nothing else. |
| `SharedKernel.Caching.Abstractions` | Abstractions | Application | Caching and distributed-locking contracts for .NET services: a hybrid cache that computes each value once, keeps tenants apart by construction, and locks that never mistake an outage for a busy resource. |
| `SharedKernel.Idempotency.Abstractions` | Abstractions | Application | One reservation contract, IIdempotencyStore, behind every duplicate-execution guard in a service: command idempotency in the application pipeline and consumer deduplication in messaging. |
| `SharedKernel.Integration.Notifications.Abstractions` | Abstractions | Application | One provider-neutral contract for human-facing notifications (a receipt by email, a one-time passcode by SMS): application code sends a templated message through INotificationSender and never touches a vendor API. |
| `SharedKernel.Messaging.Abstractions` | Abstractions | Application | The messaging contracts your application code injects: publish events and send commands as Result values, schedule broker-held messages, and read the publishing caller inside a consumer, with no transport in any signature. |
| `SharedKernel.Persistence.Abstractions` | Abstractions | Application | The persistence contracts application code depends on — repositories, opaque entity versions, bulk mutations and explicit cross-tenant access — with no ORM, no database driver and no infrastructure. |
| `SharedKernel.Reporting.Abstractions` | Abstractions | Application | Stream rows into CSV, Excel or PDF — or render HTML to PDF — and deliver the file to a named storage store with a presigned download link, or to any stream. |
| `SharedKernel.Search.Abstractions` | Abstractions | Application | The provider-neutral full-text search contract: index, search, count, walk and rebuild documents with an explicit tenant scope on every read, without your application code ever touching a Meilisearch or Elasticsearch SDK. |
| `SharedKernel.Security.Abstractions` | Abstractions | Application | One model of the caller for every .NET service — who is calling, for which tenant, with what rights, and how recently and strongly they signed in — so application code never reads raw claims. |
| `SharedKernel.Storage.Abstractions` | Abstractions | Application | Provider-neutral object-storage contracts: named stores, tenant isolation by construction, conditional writes, and presigned uploads that clients cannot abuse. |
| `SharedKernel.AI.Qdrant` | Adapter | Infrastructure | The Qdrant provider for SharedKernel.AI.Abstractions: tenant-scoped vector collections, payload filters and alias-based cutover over the official gRPC client, with model, dimension and tenant checks before every call. |
| `SharedKernel.AI.SemanticKernel` | Adapter | Infrastructure | The embedding and chat-completion provider for SharedKernel.AI.Abstractions, over any OpenAI-compatible endpoint. |
| `SharedKernel.Caching.FusionCache` | Adapter | Infrastructure | The production implementation of the SharedKernel caching contracts, built on FusionCache: stampede protection, fail-safe and eager refresh, validated configuration, tenant-safe telemetry, and opt-in compression, encryption and startup warmup. |
| `SharedKernel.Caching.Redis` | Adapter | Infrastructure | Redis as the distributed layer and backplane of the SharedKernel cache: every instance of a service shares its entries, every removal reaches every instance, and an unreachable Redis never slows every request. |
| `SharedKernel.Caching.Redis.Core` | Adapter | Infrastructure | The one Redis connection every SharedKernel Redis package shares: configured once, validated at startup, with timeouts, fail-fast behaviour during outages, TLS and mutual TLS, connection logs and a readiness probe. |
| `SharedKernel.Caching.Redis.DistributedLocking` | Adapter | Infrastructure | Redis locks and leases for IDistributedLockService: fencing tokens issued in the same atomic step as the acquisition, locks reported lost before another replica can take them, and outages that throw instead of looking like a busy resource. |
| `SharedKernel.Caching.Redis.HashStore` | Adapter | Infrastructure | Redis hashes for sessions, settings snapshots and counters: read and change one field at a time, as JSON, with the key's expiry set in the same atomic step as the write and lookups that tell a missing field from a zero. |
| `SharedKernel.Caching.Redis.PubSub` | Adapter | Infrastructure | Redis Pub/Sub for loss-tolerant, in-the-moment signals between service instances: text or JSON messages, independent disposable subscriptions that handle messages in order, restored automatically after a reconnect. |
| `SharedKernel.Communication` | Adapter | Infrastructure | The shared base of every outbound client: service discovery, outbound credentials, mutual TLS and the caller's context, configured per client and validated at startup — so no service hand-writes token handlers, certificate loading or endpoint lookup. |
| `SharedKernel.Communication.Grpc` | Adapter | Infrastructure | Generated gRPC clients registered in one line over Grpc.Net.ClientFactory: address, deadline, retry policy, keepalive and credentials from configuration, the caller on every call, and ToResultAsync() to read a failure back into the Error the other service returned. |
| `SharedKernel.Communication.Rest` | Adapter | Infrastructure | Typed REST clients in one line each: address, timeouts, retries and credentials from configuration, the caller on every call, and methods that return Result<T> — the other service's own Error, or a coded failure when it did not answer — instead of throwing. |
| `SharedKernel.Cryptography.Argon2` | Adapter | Infrastructure | Argon2id password hashing for SharedKernel.Cryptography's IOneWayHasher: one line to register, one setting to switch, and existing hashes upgrade themselves as users sign in. |
| `SharedKernel.Cryptography.KeyVault.Azure` | Adapter | Infrastructure | Azure Key Vault behind SharedKernel.Cryptography: encryption keys wrapped by a master key that never leaves the vault, and signing keys that sign inside it. |
| `SharedKernel.Idempotency.EfCore` | Adapter | Infrastructure | The PostgreSQL implementation of IIdempotencyStore, for services that run PostgreSQL and do not want Redis just for deduplication: one INSERT … ON CONFLICT … RETURNING classifies a duplicate command or message atomically, scoped by tenant. |
| `SharedKernel.Idempotency.Redis` | Adapter | Infrastructure | The Redis implementation of IIdempotencyStore: every reservation, completion and release is one Lua script, so a duplicate command or message is classified in one atomic round trip, scoped by tenant, over the service's shared Redis connection. |
| `SharedKernel.Integration.Notifications.Email.SendGrid` | Adapter | Infrastructure | The email provider behind INotificationSender: sends SendGrid dynamic-template emails over the v3 Mail Send REST API (no vendor SDK), with attachments streamed from object storage and failures returned as results. |
| `SharedKernel.Integration.Notifications.Sms.Twilio` | Adapter | Infrastructure | The SMS provider behind INotificationSender: sends Twilio Content-template messages over the Messages REST API (no vendor SDK), with the caller's NotificationDeliveryId as Twilio's Idempotency-Key, so a retried send never texts the customer twice. |
| `SharedKernel.Integration.Webhooks` | Adapter | Infrastructure | Deliver integration events to external HTTP subscribers as signed, retried, SSRF-guarded webhooks — and verify them on the receiving side. |
| `SharedKernel.Messaging.MassTransit` | Adapter | Infrastructure | A configured message bus in one fluent chain: retry, circuit breaker, broker-held deferral, at-most-once consumption, the publisher's tenant and actor travelling with the message, payload compression and encryption, tracing, metrics and a readiness probe — all behind SharedKernel.Messaging.Abstractions. |
| `SharedKernel.Messaging.MassTransit.AzureServiceBus` | Adapter | Infrastructure | The Azure Service Bus transport for SharedKernel.Messaging.MassTransit: one UseAzureServiceBus(...) call, managed identity by default, native scheduled enqueue and session-based ordering behind the builder's transport-neutral options. |
| `SharedKernel.Messaging.MassTransit.EfCore` | Adapter | Infrastructure | MassTransit's EF Core transactional outbox for SharedKernel.Messaging.MassTransit, over your service's own DbContext: a message published inside a unit of work is written in the same transaction as your data and delivered after the commit. |
| `SharedKernel.Messaging.MassTransit.RabbitMq` | Adapter | Infrastructure | The RabbitMQ transport for SharedKernel.Messaging.MassTransit: one UseRabbitMq(...) call on the bus builder, with the delayed-message exchange, dead-letter queue TTL and routing-key ordering behind the builder's transport-neutral options. |
| `SharedKernel.Persistence.Dapper` | Adapter | Infrastructure | Hand-written SQL that plays by the platform's rules: a Dapper session that joins the command's transaction, runs under the caller's tenant and picks the right database role — and stays plain Dapper. |
| `SharedKernel.Persistence.EfCore` | Adapter | Infrastructure | EF Core on PostgreSQL the way a multi-tenant production service needs it — registered with one call, correct by convention, and safe under retries, concurrency and row-level security. |
| `SharedKernel.Persistence.EfCore.Auditing` | Adapter | Infrastructure | An append-only, tamper-evident audit ledger on PostgreSQL: every command records who did what to which resource in one INSERT, a background sealer chains the records with keyed MACs, and anyone can verify the chain — while personal data in the records stays erasable. |
| `SharedKernel.Persistence.EfCore.Encryption` | Adapter | Infrastructure | Column-level encryption for EF Core on PostgreSQL: mark a property with .Encrypt("purpose") and it is stored as AES-256-GCM ciphertext — searchable by value, rotatable without downtime, and erasable per tenant. |
| `SharedKernel.Persistence.Npgsql` | Adapter | Infrastructure | The PostgreSQL connection layer under every SharedKernel persistence package: one configuration shape, TLS VerifyFull by default, the canonical database roles, row-level security binding with a startup privilege check, advisory locks and SQLSTATE → Error classification. |
| `SharedKernel.Reporting.Csv` | Adapter | Infrastructure | RFC 4180 CSV exports behind IReportExporter<TRow>, written one row at a time so a million-row export costs no more memory than a ten-row one, with a CSV-injection guard on by default and no third-party dependency. |
| `SharedKernel.Reporting.Gotenberg` | Adapter | Infrastructure | The IHtmlToPdfConverter implementation, rendered by Gotenberg, a stateless container running headless Chromium. |
| `SharedKernel.Reporting.Pdf` | Adapter | Infrastructure | Tabular PDF exports behind IReportExporter<TRow> (statements, lists, extracts): an optional title and one table that always fits the page, with repeated headers and page numbers. |
| `SharedKernel.Reporting.Spreadsheet` | Adapter | Infrastructure | Excel (.xlsx) exports behind IReportExporter<TRow>, streamed in constant memory with typed cells, so numbers, dates and booleans stay sortable and summable in Excel. |
| `SharedKernel.Scheduling` | Adapter | Infrastructure | Cron and one-shot jobs that send a kernel command through ISender — once per occurrence across every replica of the service, with explicit misfire and overlap policies and no job database. |
| `SharedKernel.Search.ElasticSearch` | Adapter | Infrastructure | The Elasticsearch provider for SharedKernel.Search.Abstractions: ISearchIndex<TDocument> over read and write aliases, plus aggregations, point-in-time deep pagination and completion suggestions for analytics-heavy work. |
| `SharedKernel.Search.Meilisearch` | Adapter | Infrastructure | The Meilisearch provider for SharedKernel.Search.Abstractions: fast, typo-tolerant user-facing search behind ISearchIndex<TDocument>, plus instant search and engine-enforced tenant tokens a browser can hold. |
| `SharedKernel.Storage.Obs` | Adapter | Infrastructure | Huawei Cloud OBS behind SharedKernel.Storage.Abstractions, over OBS's S3-compatible API — with the OBS behaviour that would silently corrupt data handled for you. |
| `SharedKernel.Storage.S3` | Adapter | Infrastructure | Amazon S3, MinIO and any S3-compatible service behind SharedKernel.Storage.Abstractions: IAM-role credentials, KMS encryption, streaming multipart uploads, conditional writes and presigned uploads, with nothing S3-specific in your application code. |
| `SharedKernel.Validation.FluentValidation` | Adapter | Infrastructure | FluentValidation rules for IBANs, card numbers, VAT numbers, national IDs and every other SharedKernel.Validation identifier — RuleFor(x => x.Iban).MustBeValidIban() — plus the bridge that runs FluentValidation validators in the kernel request pipeline. |
| `SharedKernel.Workflows.Temporal` | Adapter | Infrastructure | Durable, crash-proof workflows on Temporal: start, signal, query and await them from application code, author them on platform base types, and host the worker in the generic host — with tenant-scoped workflow ids, caller propagation into activities, Result-to-failure mapping and payload encryption already wired. |
| `SharedKernel.Application.Mediator.MediatR` | Host | Api / Worker | MediatR as the transport behind the kernel's ISender — one builder call, app.UseMediatR(), and every command, query and stream of the scanned assemblies is sent through the kernel pipeline, with no MediatR type in your application code. |
| `SharedKernel.Application.Pipeline` | Host | Api / Worker | The application layer of a service in one call — handlers, validators and domain-event handlers found, and the cross-cutting half of every request (tracing, logging, metrics, authorization, validation, idempotency, transactions, auditing) composed in one fixed order, so a handler only decides business outcomes. |
| `SharedKernel.Application.Pipeline.Caching` | Host | Api / Worker | Cache a query by adding one interface to it, and let the command that changes the data evict it — after the transaction commits, never before. |
| `SharedKernel.MultiTenancy` | Host | Api / Worker | Per-request tenant resolution: decides which tenant an HTTP request belongs to — from the verified credential, a header or a tenant directory — and puts it on the request context, so persistence, caching, idempotency and outbound calls all see the same tenant. |
| `SharedKernel.Presentation.Core` | Host | Api / Worker | What every inbound boundary shares — HTTP, gRPC and SignalR: declarative authorization attributes over IUserContext, and one set of error rules, so an error or a refusal reads the same on every protocol and a gRPC host needs no HTTP API stack. |
| `SharedKernel.Presentation.GraphQL` | Host | Api / Worker | HotChocolate v16 server conventions in one call: snake_case filter operations, capped paging, errors that carry the members of an RFC 9457 problem (status, error code, trace id, correlation id) like a REST error does, an introspection switch, and a paged response type that matches SharedKernel.Contracts' PagedList<T>. |
| `SharedKernel.Presentation.Grpc` | Host | Api / Worker | gRPC services with the same error contract and authorization as a SharedKernel HTTP API, in one call. |
| `SharedKernel.Presentation.OpenApi` | Host | Api / Worker | API versioning and one OpenAPI 3.1 document per version for a SharedKernel HTTP API, in two calls. |
| `SharedKernel.Presentation.SignalR` | Host | Api / Worker | SignalR on the platform's error contract: hub errors carry the error code and the same message an HTTP problem would, hub methods may return Result, the shared authorization attributes guard hubs and hub methods, and every hub method runs in the caller's request context. |
| `SharedKernel.Presentation.WebApi` | Host | Api / Worker | The HTTP boundary of a SharedKernel service: one call to register it, one to add it to the pipeline, and one RFC 9457 application/problem+json error shape for every failure — with security headers, CORS, request limits and the Idempotency-Key, ETag and If-Match headers handled at the edge. |
| `SharedKernel.Security.ApiKey` | Host | Api / Worker | API key authentication for machine clients: generated, hashed at rest, expiring and revocable keys — or your own validator — surfaced as the same IUserContext as every other caller. |
| `SharedKernel.Security.Mtls` | Host | Api / Worker | Client-certificate (mutual TLS) authentication for partners and services: the framework checks the chain, your validator decides who the client is, and the caller becomes a Service IUserContext. |
| `SharedKernel.Security.Oidc` | Host | Api / Worker | JWT bearer authentication for access tokens from any OpenID Connect provider — validation rules pinned, sender-constrained tokens enforced, and the caller exposed as IUserContext. |
| `SharedKernel.Security.Totp` | Host | Api / Worker | Authenticator-app enrollment, recovery codes and session-bound step-up: a verified one-time code adds amr=otp to one sign-in session for a bounded window, so sensitive endpoints can demand a fresh second factor. |
| `SharedKernel.ServiceDefaults` | Host | Api / Worker | The composition base every service starts from: one call wires OpenTelemetry, a startup gate and the health-check infrastructure; one more maps every dependency's readiness probe onto /health/ready. |
| `SharedKernel.ServiceDefaults.Configuration.KeyVault` | Host | Api / Worker | Azure Key Vault secrets as an IConfiguration source in one call — so connection strings, client secrets and API keys come from the vault instead of appsettings.json, and an unreachable vault stops the host at startup. |
| `SharedKernel.ServiceDefaults.Localization` | Host | Api / Worker | Decides which culture a request gets — the user's stored preference, then the tenant's default, then Accept-Language — on top of ASP.NET Core's own RequestLocalizationMiddleware. |
| `SharedKernel.ServiceDefaults.Persistence` | Host | Api / Worker | Database readiness for the Persistence packages: /health/ready stays unhealthy until startup migrations and seeders have finished, and while the database cannot be reached — for EF Core, Dapper, or both. |
| `SharedKernel.ServiceDefaults.Security` | Host | Api / Worker | One request context for every layer that asks who is calling, and the HTTP middleware that fills it: the caller, the tenant and one correlation id — from the first middleware to the last log line and every outbound call. |
| `SharedKernel.ServiceDefaults.Security.Mtls` | Host | Api / Worker | Mutual-TLS host wiring: accept client certificates negotiated by Kestrel, or forwarded as a header by a TLS-terminating ingress — and only from the proxies you trust. |
| `SharedKernel.AI.Testing` | Testing | Tests | In-memory doubles for every SharedKernel.AI.Abstractions contract, so embedding, retrieval and completion code runs in a unit test without a model, a network or a vector database — deterministically, and with the same model, dimension and tenant checks as production. |
| `SharedKernel.Application.Testing` | Testing | Tests | Run a command or query through the real SharedKernel application pipeline in a unit test — the same AddSharedKernelApplication call, the same behaviors in the same order, the same start-time seam check — and assert on the result, the spans and the metrics. |
| `SharedKernel.Caching.Redis.Testing` | Testing | Tests | In-memory doubles for the Redis-specific caching contracts — IRedisHashService, ITypedHashStore<T> and IRedisChannelService — so code that uses Redis hashes or Pub/Sub is unit-tested without Redis and without AddRedisConnection. |
| `SharedKernel.Caching.Testing` | Testing | Tests | In-memory doubles for SharedKernel.Caching.Abstractions — the cache, the tenant cache, distributed locks and leases with fencing tokens, key providers and a recording warmup strategy — so caching code is unit-tested without FusionCache or Redis. |
| `SharedKernel.Communication.Testing` | Testing | Tests | Test doubles for a service's own REST and gRPC clients: a stub server that runs a typed client's whole pipeline with no network, fake unary calls for a mocked gRPC client, and a gRPC ServerCallContext. |
| `SharedKernel.Cryptography.Testing` | Testing | Tests | Doubles for SharedKernel.Cryptography that run the real algorithms and add call recording, in-test key rotation and failure simulation — nothing is stubbed to "always true", so a passing test proves the wiring. |
| `SharedKernel.FeatureManagement.Testing` | Testing | Tests | FakeFeatureClient is an OpenFeature IFeatureClient whose flags a test sets directly — fixed values or per-context rules — so code built on SharedKernel.FeatureManagement is tested without configuration, a provider or a host. |
| `SharedKernel.Idempotency.Testing` | Testing | Tests | FakeIdempotencyStore is an in-memory IIdempotencyStore with the same reservation protocol as the Redis and EF Core stores, so a duplicate command or a redelivered message is tested without Redis or PostgreSQL. |
| `SharedKernel.Integration.Testing` | Testing | Tests | In-memory doubles for outbound webhooks and customer notifications (email, SMS), so the code that sends them is tested without HTTP, SendGrid or Twilio. |
| `SharedKernel.Messaging.Testing` | Testing | Tests | In-memory IMessageBus and IEventPublisher that record every publish and send, so a handler that talks to the bus can be unit-tested without a broker, MassTransit or containers. |
| `SharedKernel.Persistence.Testing` | Testing | Tests | Test services built on the SharedKernel persistence packages at both speeds: in-memory fakes that follow the production rules for unit tests, and a real PostgreSQL with the production role split for integration tests. |
| `SharedKernel.Presentation.Testing` | Testing | Tests | Test helpers for the inbound API boundary: a gRPC ServerCallContext that carries a real HttpContext, a HotChocolate request executor with small paging defaults, and a fixed IHttpContextAccessor. |
| `SharedKernel.Reporting.Testing` | Testing | Tests | In-memory doubles for SharedKernel.Reporting.Abstractions, so export and HTML-to-PDF code is tested without a format provider, a Gotenberg container or object storage. |
| `SharedKernel.Scheduling.Testing` | Testing | Tests | InMemoryScheduledJobRegistry implements SharedKernel.Scheduling's IScheduledJobRegistry without a clock, a hosted loop or a lock store: registrations are recorded, and your test fires each tick with TriggerAsync. |
| `SharedKernel.Search.Testing` | Testing | Tests | In-memory ISearchIndex<TDocument>, provisioner and provider descriptor, so indexing and search code runs in a unit test without Meilisearch or Elasticsearch — with the same definition checks and tenant fail-closed rule as the real engines. |
| `SharedKernel.Security.Testing` | Testing | Tests | Test doubles for the SharedKernel security packages: a settable IUserContext, a builder for the principal an OIDC token produces, in-memory API-key, DPoP-replay, TOTP step-up and recovery-code stores, and builders for real signed DPoP proofs and mTLS client certificates. |
| `SharedKernel.ServiceDefaults.Testing` | Testing | Tests | Test helpers for the SharedKernel host packages: an in-memory ITenantCatalog, a scriptable ITenantResolutionStrategy, and assertions on health-check registrations. |
| `SharedKernel.Storage.Testing` | Testing | Tests | An in-memory object store registered exactly like a real storage provider, so file-handling code runs in a unit test with named stores, tenant views, validation, conditional writes and presigned URLs — without MinIO or a cloud account. |
| `SharedKernel.Testing` | Testing | Tests | The lightweight core of the SharedKernel test packages: a controllable clock, structured-log capture, a configurable caller, deterministic fakers and framework-free assertions for the domain and contract types — with no infrastructure and no test-framework dependency. |
| `SharedKernel.Workflows.Testing` | Testing | Tests | An in-memory IWorkflowDispatcher that records starts, signals, queries, cancellations and terminations per tenant, so code that dispatches Temporal workflows is unit-tested without a Temporal server — and the test decides how each workflow ends. |
| `SharedKernel.Analyzers` | Tooling | the build | 47 Roslyn rules that turn the Platform.SharedKernel conventions into compiler warnings in your own build. |
| `SharedKernel.ArchitectureTests` | Tooling | the build | Ready-made NetArchTest and Mono.Cecil rules you assert from your own test suite: domain purity, provider isolation, forbidden injection patterns and IL-level secure-default checks that no compiler warning can express. |
| `SharedKernel.Linter` | Tooling | the build | A CSharpier format check that fails CI on unformatted code and stays silent locally, a one-command local format, and the platform's shared .editorconfig. |
<!-- END GENERATED: package-catalog -->

## 5. Recipes (in `recipes/` next to this file)

Open the one that matches the task, for example `recipes/add-caching.md`, then read the READMEs it names.

| Recipe | Open it when |
| --- | --- |
| new-service.md | creating a service: the four projects, `Program.cs`, the pipeline, health and telemetry |
| add-persistence.md | storing aggregates in PostgreSQL with EF Core or Dapper, transactions, audit, encryption |
| add-http-endpoint.md | exposing a use case over HTTP, gRPC, SignalR or GraphQL |
| add-messaging.md | publishing or consuming integration events, with or without the outbox |
| add-caching.md | caching queries or data, Redis L2, locks, hashes, pub/sub |
| add-authentication.md | OIDC, API keys, mTLS, TOTP step-up, permissions |
| add-multi-tenancy.md | resolving the tenant and isolating its data |
| add-idempotency.md | making commands or consumers safe to retry |
| add-background-job.md | cron or one-shot jobs that run once across replicas |
| add-long-running-workflow.md | multi-step processes with retries, timers and compensation |
| add-outbound-call.md | calling another service over REST or gRPC |
| add-service-contracts.md | publishing a service's events, DTOs and client, or consuming another service's client or events behind a port |
| add-file-storage.md | uploading, downloading or presigning files, per tenant |
| add-search.md | full-text search indexes |
| add-ai.md | embeddings, vector search and chat models |
| add-notifications-webhooks.md | sending email or SMS, sending or receiving signed webhooks |
| add-report-export.md | streaming CSV, Excel or PDF exports, HTML to PDF |
| write-tests.md | unit tests with the fakes, integration tests with real dependencies |
