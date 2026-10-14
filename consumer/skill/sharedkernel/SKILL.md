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
  package's README Install table names its project; the build rejects a wrong placement.
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
- **Never hand-roll what a package covers.** If a need is not in the table below, say so and ask; do not
  invent a package or an API.
- **Analyzers.** Reference `SharedKernel.Analyzers` in every project (it ships every rule at Warning) and
  raise the rules you rely on to `error` in the service's `.editorconfig`. The analyzers and the
  service's architecture tests are the guardrail; this skill only steers.

## 3. Needs

"Reference from" names the service project. Open the recipe for anything that spans packages.

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
<!-- END GENERATED: package-catalog -->

## 5. Recipes

Open the one that matches the task, then read the READMEs it names.

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
| add-file-storage.md | uploading, downloading or presigning files, per tenant |
| add-search.md | full-text search indexes |
| add-ai.md | embeddings, vector search and chat models |
| add-notifications-webhooks.md | sending email or SMS, sending or receiving signed webhooks |
| add-report-export.md | streaming CSV, Excel or PDF exports, HTML to PDF |
| write-tests.md | unit tests with the fakes, integration tests with real dependencies |
