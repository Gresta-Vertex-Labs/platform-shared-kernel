# Platform.SharedKernel — Root Brain

## What This Repo Is

A mono-repo of independently publishable NuGet packages that form the **SharedKernel** for a .NET 10 microservice ecosystem. Every package here is a reusable building block — no business logic lives here. Packages are consumed by downstream microservices, not by each other unless the dependency is explicitly allowed by the layering rules below.

Philosophy: **Capability-Oriented, AOT-Preferred, K8s-Native, Test-Adjacent.**

> **AOT Guidance (not a hard rule):** AOT compatibility is preferred where it costs nothing. The bar is pragmatic: if a feature can be written AOT-cleanly without reflection, `dynamic`, or `Assembly.Load`, do it that way. Prefer STJ source-generated contexts (`JsonSerializerContext`) over runtime serialization when the code stays readable. Avoid `Activator.CreateInstance` without `[DynamicallyAccessedMembers]` when a simple factory or constructor call is equally clear.
>
> **When to skip AOT:** If writing AOT-safe code requires significant boilerplate, awkward workarounds, or makes the code harder to understand and maintain, drop AOT for that area. Do not add `<IsAotCompatible>true</IsAotCompatible>` to project files — the tag activates trim/AOT analyzers globally and forces AOT compliance on the entire project, which is too coarse-grained.
>
> Third-party packages that are not AOT-safe are allowed; prefer placing them behind an abstraction interface where a swap is plausible, but this is a design preference, not a requirement.

---

## Folder Map

Each numbered folder is a capability domain. Each owns a `CLAUDE.md` with its internal technology and implementation rules. This file only owns the map and the cross-domain rules.

| # | Folder | What lives here |
|---|--------|-----------------|
| 00 | `00.Governance` | Roslyn analyzers, EditorConfig/CSharpier styling, Git hooks, benchmarking templates, architecture test suites (NetArchTest) |
| 01 | `01.Core` | Primitives (`Result<T>`, `Error`, `IClock`, SmartEnums), base exceptions, system extensions, Options-pattern validation, Feature Flags |
| 02 | `02.Caching` | FusionCache L1/L2 interfaces, stampede protection. Redis split by role: `.Redis.Core` (shared connection/health/resilience), `.Redis` (L2 backplane), `.Redis.DistributedLocking` (RedLock), `.Redis.HashStore`, `.Redis.PubSub` (cache invalidation signaling) |
| 03 | `03.Domain` | DDD building blocks: `Entity`, `AggregateRoot`, `ValueObject`, `IDomainEvent`, tenanted aggregate bases (`TenantedAggregateRoot`, `TenantedAuditableAggregateRoot`, `TenantedFullAuditableAggregateRoot`) |
| 04 | `04.Contracts` | Cross-service DTOs only: `PagedList`, `Envelope`, integration event payloads. No domain logic. |
| 05 | `05.Application` | MediatR base handlers and dispatchers, pipeline behaviors (Validation, Logging, Metrics, Transaction) |
| 06 | `06.Persistence` | Repository and UoW abstractions, EF Core interceptors (Audit/SoftDelete/Outbox/Concurrency), Npgsql/PostgreSQL defaults (SnakeCase, JSONB, pgvector via `Pgvector.EntityFrameworkCore`), Dapper type handlers and `DapperReadService` base |
| 07 | `07.Messaging` | `IMessageBus` / `IEventPublisher` abstractions (CloudEvents), MassTransit pre-configured bus (Retry, Outbox, RabbitMQ/ASB) |
| 08 | `08.Storage` | `IFileStorage` / `IBlobUriGenerator` abstractions, AWS S3 / MinIO implementation |
| 09 | `09.Search` | `ISearchIndex` / `IQueryBuilder` abstractions, Meilisearch (BFF/fast), ElasticSearch (analytics/heavy) |
| 10 | `10.Intelligence` | `ISemanticKernel`, `IEmbeddingGenerator` abstractions, Qdrant / Milvus vector db, LLM orchestration |
| 11 | `11.Communication` | `SharedKernel.Communication.Rest` (typed HttpClient + Polly v8 resilience, CorrelationId/TenantId delegation handlers, ProblemDetails deserialization), `SharedKernel.Communication.Grpc` (gRPC channel factory + OTel tracing interceptors + Protobuf helpers), `SharedKernel.Communication.GraphQL` (HotChocolate server-side conventions: snake_case, filtering, sorting, paging, error mapping), `SharedKernel.Communication.Internal` (K8s headless DNS resolver + static dev resolver, `IServiceEndpointResolver`) |
| 12 | `12.Security` | `IUserContext`, `ITenantProvider` abstractions, JWT mapping, Role/Policy logic, Azure B2C integration |
| 13 | `13.ServiceDefaults` | OpenTelemetry wiring, HealthChecks, Startup/Liveness probes, Tenant resolution (Header/Claim/DB isolation) |
| 14 | `14.Presentation` | ProblemDetails, API versioning, Swagger/Scalar, SignalR Hub filters and Redis Backplane config |
| 15 | `15.Integration` | Outbound Webhook dispatcher, signature verification |
| 16 | `16.Testing` | Testcontainers setup, Bogus faker factories, auth mocks — shared test helpers consumed by all `.Tests` projects |
| 17 | `17.Workflows` | Temporal durable orchestration state-machines |

---

## Layering Rules (Dependency Direction)

Dependencies flow **downward only** (lower number = more foundational). A package may only reference packages in layers with a **lower number** than its own. No circular references. No skipping layers without justification.

```
00.Governance       → references nothing (tooling only)
01.Core             → references nothing
02.Caching          → may reference 01.Core
03.Domain           → may reference 01.Core
04.Contracts        → may reference 01.Core, 03.Domain
05.Application      → may reference 01–04
06.Persistence      → may reference 01–05
07.Messaging        → may reference 01–04 (not 06.Persistence directly)
08.Storage          → may reference 01.Core
09.Search           → may reference 01.Core, 04.Contracts
10.Intelligence     → may reference 01.Core, 04.Contracts
11.Communication    → may reference 01.Core, 04.Contracts, 12.Security abstractions
12.Security         → may reference 01.Core
13.ServiceDefaults  → may reference 01–12 (host composition layer)
14.Presentation     → may reference 01.Core, 04.Contracts, 12.Security, 13.ServiceDefaults
15.Integration      → may reference 01.Core, 04.Contracts, 07.Messaging abstractions
16.Testing          → may reference any layer (test infrastructure only, never shipped)
17.Workflows        → may reference 01.Core, 04.Contracts, 05.Application
```

**Hard rules:**
- `03.Domain` must never reference `06.Persistence`, `07.Messaging`, or any infrastructure layer.
- `04.Contracts` must never contain domain logic — pure DTOs and event payloads only.
- `05.Application` must never reference a concrete infrastructure package — only abstractions.
- `16.Testing` packages are never referenced by production code.
- `07.Messaging` must never reference any `SharedKernel.Caching.*` package, and no `SharedKernel.Caching.*` package may reference any `SharedKernel.Messaging.*` package. Ephemeral/no-delivery-guarantee Redis Pub/Sub (cache invalidation signaling) stays in `02.Caching` as `SharedKernel.Caching.Redis.PubSub` — it is architecturally distinct from `07.Messaging`'s durable/outbox-backed delivery contract and must never be merged or relocated into `07.Messaging`.

---

## Package Naming Convention

```
SharedKernel.{Capability}                   → main package (interfaces + default impl if single-provider)
SharedKernel.{Capability}.Abstractions      → interfaces only, no implementation, minimal dependencies
SharedKernel.{Capability}.{Provider}        → concrete implementation for a specific technology
SharedKernel.{Capability}.Tests             → test project, nested inside the project folder it tests
```

**Examples:**
- `SharedKernel.Messaging.Abstractions` — `IMessageBus`, `IEventPublisher` only
- `SharedKernel.Messaging.MassTransit` — concrete MassTransit wiring
- `SharedKernel.Persistence.Abstractions` — `IRepository`, `IUnitOfWork`, `IConnectionFactory`
- `SharedKernel.Persistence.EfCore` — EF Core implementation of the above

When a capability has more than one provider (Search, Persistence, Caching, Storage, etc.), **always split into `.Abstractions` + `.{Provider}`**. Microservices reference the abstraction and inject the provider of their choice.

**Provider role-split variant:** when a single underlying technology serves multiple distinct architectural roles within one capability domain, split further:

```
SharedKernel.{Capability}.{Provider}.Core      → shared infrastructure (connection mgmt, health, resilience) for sibling role-packages
SharedKernel.{Capability}.{Provider}.{Role}    → role-specific extension package, depends on .{Provider}.Core + .Abstractions only
```

Sibling role-packages must never reference each other — only the shared `.Core` package. Example (`02.Caching`): `SharedKernel.Caching.Redis.Core` (connection/health/resilience), `SharedKernel.Caching.Redis` (L2 FusionCache backplane), `SharedKernel.Caching.Redis.DistributedLocking`, `SharedKernel.Caching.Redis.HashStore`, `SharedKernel.Caching.Redis.PubSub`.

---

## Test Project Rules

- Test projects are **nested inside the project folder they test**, not in a separate top-level `tests/` folder.
- Example: `06.Persistence/SharedKernel.Persistence.EfCore/SharedKernel.Persistence.EfCore.Tests/`
- Every test project references `16.Testing/SharedKernel.Testing` for shared helpers (Testcontainers, Bogus, auth mocks).
- Test projects are `classlib` targeting `net10.0` — the test runner (xUnit) is added as a package reference by the developer.

---

## "What Goes Where" Decision Guide

| I need to add… | It belongs in… |
|----------------|---------------|
| A new domain concept (entity, value object, domain event) | `03.Domain` |
| A tenant-scoped aggregate (multi-tenant SaaS) | `03.Domain` — extend `TenantedAggregateRoot<TId>`, `TenantedAuditableAggregateRoot<TId>`, or `TenantedFullAuditableAggregateRoot<TId>`; supply `tenantId` as a `Guid` parameter from the application layer |
| A cross-service DTO or integration event payload | `04.Contracts` |
| An `EventEnvelope<TEvent>` transport wrapper (CorrelationId, CausationId, SourceService, schema version) | `04.Contracts` |
| A MediatR command/query base class or pipeline behavior | `05.Application` or `05.Application.Behaviors` |
| A new persistence abstraction (interface) | `06.Persistence/SharedKernel.Persistence.Abstractions` |
| A new EF Core interceptor or convention | `06.Persistence/SharedKernel.Persistence.EfCore` |
| An EF Core repository for an aggregate | `06.Persistence/SharedKernel.Persistence.EfCore` — extend `EfRepository<T,TId>` (write) and `EfReadRepository<T,TId>` (read) |
| A strongly-typed ID EF Core value converter | `06.Persistence/SharedKernel.Persistence.EfCore` — use `StronglyTypedIdValueConverter<TId, TValue>` |
| A multi-tenant EF Core DbContext with global tenant filter | `06.Persistence/SharedKernel.Persistence.EfCore` — extend `TenantedDbContext`; tenant filter applied automatically via `ICurrentTenantService` |
| PostgreSQL snake_case naming, JSONB column, or pgvector column | `06.Persistence/SharedKernel.Persistence.PostgreSQL` — use `UsePostgreSQL()`, `HasJsonbColumn()`, or `HasVectorColumn()` |
| A Dapper read-side query service | `06.Persistence/SharedKernel.Persistence.Dapper` — extend `DapperReadService`; inject `IDbConnectionFactory` (provided by `SharedKernel.Persistence.PostgreSQL`); use parameterized queries only |
| A write-side fetch by business key (not by primary key) | `06.Persistence/SharedKernel.Persistence.EfCore` — call `IRepository.GetBySpecAsync(spec, ct)`; returns a tracked entity; do not use `IReadRepository` for write-path fetches |
| Background service or hosted-service DbContext (outside HTTP request scope) | `06.Persistence/SharedKernel.Persistence.EfCore` — register via `EfCorePersistenceBuilder.WithDbContextFactory()`; injects `IDbContextFactory<TContext>`; audit fields default to `"system"` (no HTTP user context) |
| A service-specific EF Core interceptor alongside platform interceptors | `06.Persistence/SharedKernel.Persistence.EfCore` — register via `EfCorePersistenceBuilder.AddInterceptor<TInterceptor>()`; platform three (Audit/SoftDelete/Concurrency) always fire first |
| A deep multi-level navigation include path (beyond two ThenInclude levels) | `03.Domain/SharedKernel.Domain` — call `AddStringInclude("Navigation.Child.GrandChild")` on the specification; `SpecificationEvaluator` applies string includes at step 2b between expression includes and ordering |
| STJ JSON serialization for `StronglyTypedId<TValue>`-derived types (Guid/int/long/string) | `03.Domain/SharedKernel.Domain` — `StronglyTypedIdJsonConverterFactory`, registered via `JsonSerializerOptions.Converters.Add(...)`; opt-in, zero new NuGet dependency. Supersedes the prior guidance that consuming services must provide their own converter |
| An EF Core compiled model for AOT / startup performance | `06.Persistence/SharedKernel.Persistence.EfCore` — pass via `EfCorePersistenceBuilder.WithCompiledModel(IModel)`; produced by `dotnet ef dbcontext optimize`; disables runtime model-building scans |
| An explicit database transaction (multi-repo saga, two-phase write) | `06.Persistence/SharedKernel.Persistence.Abstractions` — inject `ITransactionalUnitOfWork`; call `BeginTransactionAsync` → returns `IPersistenceTransaction`; commit or rollback via that handle; never inject `IDbContextTransaction` directly |
| A paged list of DTOs (projected, with total-count metadata) | `06.Persistence/SharedKernel.Persistence.EfCore` — call `IReadRepository.ListPagedProjectedAsync<TResult>(spec, ct)`; spec must implement both `ISpecification<TAggregate>` (paging/ordering) and `IProjectionSpecification<TAggregate,TResult>` (selector); returns `PagedList<TResult>` |
| A set-based bulk update/delete over rows matching a specification (no entity materialization) | `06.Persistence/SharedKernel.Persistence.EfCore` — new bulk-update/bulk-delete methods on the write-side repository contract backed by `ExecuteUpdateAsync`/`ExecuteDeleteAsync`; bypasses `AuditInterceptor`/`SoftDeleteInterceptor`/`ConcurrencyInterceptor` and domain event dispatch (documented bypass); only criteria/`IncludeDeleted` spec properties honored — `Includes`/ordering/paging are rejected |
| A constant-memory, server-side-streamed read over a large result set | `06.Persistence/SharedKernel.Persistence.EfCore` — `IAsyncEnumerable<TAggregate>` / `IAsyncEnumerable<TResult>` methods on `IReadRepository<TAggregate,TId>` for `ISpecification<T>` / `IProjectionSpecification<T,TResult>`; always forces `AsNoTracking` regardless of `spec.AsNoTracking` (documented deviation) |
| A database-readiness probe for K8s health checks (DB connectivity + latency) | `06.Persistence` provides the probe primitive — via `SharedKernelDbContext` for EF Core, and an `IDbConnectionFactory`-based probe for Dapper/PostgreSQL-only services; `06.Persistence` ships no `IHealthCheck` implementation — wiring into `AddHealthChecks()` remains a `13.ServiceDefaults` concern |
| Startup orchestration that applies pending EF Core migrations and runs idempotent data seeders, race-safe across replicas | `06.Persistence/SharedKernel.Persistence.EfCore` — opt-in via `EfCorePersistenceBuilder.WithMigrationsOnStartup()` / `.WithSeeders(...)`; consumers implement `IDataSeeder<TContext>`; guarded by an advisory/distributed lock (optional `02.Caching.Redis.DistributedLocking` integration, not a hard reference); not a migration-authoring tool and not a compiled-model replacement |
| Field-level column encryption on an EF Core entity property | `06.Persistence/SharedKernel.Persistence.EfCore` — call `.Encrypt()` on `PropertyBuilder<T>` inside `IEntityTypeConfiguration<TEntity>.Configure(builder)`; `EncryptionModelConvention` applies `EncryptedValueConverter<string>` automatically at model finalization; enable via `EfCorePersistenceBuilder.WithEncryption()`; never add encryption attributes to domain entities |
| Configuring the unauthenticated audit fallback string (replacing "system") | `06.Persistence/SharedKernel.Persistence.EfCore` — call `EfCorePersistenceBuilder.WithServiceName("my-service")`; registers `PersistenceServiceOptions.ServiceName`; `AuditInterceptor` reads this value instead of the hardcoded `"system"` literal |
| Rotating an AES encryption key to a new version across all encrypted rows | `06.Persistence/SharedKernel.Persistence.EfCore` — inject `IEncryptionRotationJob` in a hosted service, Hangfire job, or Temporal activity; call `RotateAsync(fromVersion, toVersion, ct)`; add the new key to `EncryptionOptions.Keys` before rotating; do not remove the old key until rotation completes |
| Encryption attributes on a domain entity class | Prohibited — use `PropertyBuilder<T>.Encrypt()` in `IEntityTypeConfiguration<TEntity>` instead; placing encryption attributes on domain types leaks infrastructure concerns into the domain layer (SK0302) |
| A new cache interface or policy | `02.Caching/SharedKernel.Caching.Abstractions` |
| A FusionCache L1 provider implementation or option | `02.Caching/SharedKernel.Caching.FusionCache` |
| A Redis L2 cache backplane implementation (FusionCache) | `02.Caching/SharedKernel.Caching.Redis` |
| A distributed lock / renewable lock over Redis | `02.Caching/SharedKernel.Caching.Redis.DistributedLocking` — implements `IDistributedLockService`/`IRenewableLock` from `SharedKernel.Caching.Abstractions` |
| Structured Redis Hash storage (sessions, counters, typed DTOs) | `02.Caching/SharedKernel.Caching.Redis.HashStore` — implements `IRedisHashService`/`ITypedHashStore<T>` |
| Ephemeral Redis Pub/Sub or cross-service cache invalidation signaling | `02.Caching/SharedKernel.Caching.Redis.PubSub` — implements `IRedisChannelService`/`ICacheInvalidationBus`; never `07.Messaging` (no delivery guarantees) |
| Shared Redis `IConnectionMultiplexer`, connection health, or circuit breaker pipeline | `02.Caching/SharedKernel.Caching.Redis.Core` |
| A new message bus abstraction | `07.Messaging/SharedKernel.Messaging.Abstractions` |
| A MassTransit consumer base or configuration | `07.Messaging/SharedKernel.Messaging.MassTransit` |
| An idempotent consumer deduplication hook | `07.Messaging/SharedKernel.Messaging.Abstractions` — implement `IIdempotencyStore` (`HasProcessedAsync`/`MarkProcessedAsync`) in the consuming service; enable via `MessagingBusBuilder.WithIdempotency()`; never implement custom deduplication inside `ConsumeAsync` body |
| A cross-cutting message header propagator (tenant ID, correlation ID, feature flags) | `07.Messaging/SharedKernel.Messaging.Abstractions` — implement `IMessageHeaderPropagator.Propagate(PublishContext)` in the consuming service; register via `MessagingBusBuilder.WithHeaderPropagator<T>()`; explicit `PublishContext` overrides win over propagated values |
| A per-consumer endpoint/retry filter definition with platform defaults | `07.Messaging/SharedKernel.Messaging.MassTransit` — extend `ConsumerDefinitionBase<TConsumer>`; override `NonRetryableExceptions` to declare exception types that skip retry (e.g., `ValidationException`); register via `AddConsumer<TConsumer, TDefinition>()` |
| A message schema version translator for rolling upgrades | `07.Messaging/SharedKernel.Messaging.Abstractions` — implement `IMessageVersionTranslator<TOld, TNew>`; register via `MessagingBusBuilder.WithVersionTranslator<TOld, TNew, TTranslator>()`; `Translate` must be a synchronous pure projection — no I/O |
| A routing slip activity for stateless multi-step distributed coordination | `07.Messaging/SharedKernel.Messaging.MassTransit` — extend `RoutingSlipActivityBase<TArguments, TLog>`; override `ExecuteAsync` and `CompensateAsync`; register via `MessagingBusBuilder.AddRoutingSlipActivity<TActivity>()`; use routing slips for stateless chains, sagas for stateful persistent workflows |
| A routing slip builder or dispatcher | `07.Messaging/SharedKernel.Messaging.Abstractions` — use `IRoutingSlipBuilder` to compose the activity sequence; dispatch via `IMessageBus.ExecuteRoutingSlipAsync(slip, ct)`; never reference MassTransit `RoutingSlip` types directly in application code |
| A `Result<T>` change or new primitive type | `01.Core/SharedKernel.Primitives` |
| A new extension method on BCL types | `01.Core/SharedKernel.Core` |
| An Options-pattern validator | `01.Core/SharedKernel.Configuration` |
| A feature flag abstraction | `01.Core/SharedKernel.FeatureManagement` |
| A new architecture enforcement rule | `00.Governance/SharedKernel.ArchitectureTests` |
| A platform-wide reflection-based generic method invocation (`Type.GetMethod`/`GetMethods` + `MakeGenericMethod` + `Invoke`) | Forbidden — use typed dispatch or expression trees instead; enforced platform-wide by an SK0xxx rule in `00.Governance/SharedKernel.ArchitectureTests`, with an explicit documented-exception mechanism for rare justified cases |
| A new Roslyn analyzer | `00.Governance/SharedKernel.Analyzers` |
| Shared test fakers or container setup | `16.Testing/SharedKernel.Testing` |
| In-process test double for `IMessageBus` in unit tests | `16.Testing/SharedKernel.Testing` — use `InMemoryMessageBus`; assert via `ShouldHavePublished<T>()`, `ShouldHaveSent<T>()`, `ShouldHavePublishedOnce<T>()`, `ShouldNotHavePublished<T>()`; register via `AddInMemoryMessageBus()`; references only `SharedKernel.Messaging.Abstractions` |
| In-process test double for `IEventPublisher` in unit tests | `16.Testing/SharedKernel.Testing` — use `InMemoryEventPublisher`; assert via `PublishedOf<TEvent>()` and assertion helpers; register via `AddInMemoryEventPublisher()`; references only `SharedKernel.Messaging.Abstractions` |
| A Temporal workflow activity or state machine base | `17.Workflows/SharedKernel.Workflows.Temporal` |
| A typed HttpClient with Polly v8 resilience, CorrelationId/TenantId header injection, and ProblemDetails deserialization | `11.Communication/SharedKernel.Communication.Rest` — use `AddSharedKernelRestCommunication().AddRestClient<TClient>()`; never inject raw `HttpClient` directly in a production constructor |
| K8s headless DNS resolution or static dev/test service address resolution | `11.Communication/SharedKernel.Communication.Internal` — inject `IServiceEndpointResolver`; use `AddK8sServiceDiscovery()` (production) or `AddStaticServiceDiscovery()` (dev/test only — logs `Warning` at startup); never construct `Uri` from config values directly inside typed clients |
| A gRPC typed client with OTel tracing interceptor and CorrelationId/TenantId metadata injection | `11.Communication/SharedKernel.Communication.Grpc` — use `AddSharedKernelGrpcCommunication().AddGrpcClient<TClient>()`; interceptors are registered globally (not per-call); never inject `GrpcChannel` directly |
| Protobuf well-known type conversion (`Money` ↔ `decimal`, `Timestamp` ↔ `DateTimeOffset`) | `11.Communication/SharedKernel.Communication.Grpc` — use `MoneyProtoExtensions` / `TimestampProtoExtensions`; pure static, allocation-minimal |
| HotChocolate server-side GraphQL conventions (snake_case naming, filtering, sorting, paging, error mapping) | `11.Communication/SharedKernel.Communication.GraphQL` — call `AddSharedKernelGraphQL()` before any service-specific `AddGraphQL()`/`AddTypes()` calls; never inherit `FilterInputType<T>` or `SortInputType<T>` directly — always extend `FilterBase<T>` / `SortBase<T>` |
| Raw `HttpClient` injection in a production constructor | Prohibited — always use `IHttpClientFactory`-managed typed clients via `AddRestClient<TClient>()`; enforced platform-wide by SK0xxx governance rule (P-159) |
| OTel, health check, or probe wiring | `13.ServiceDefaults/SharedKernel.ServiceDefaults` |
| Tenant resolution logic | `13.ServiceDefaults/SharedKernel.MultiTenancy` |
| JWT / OIDC / B2C wiring | `12.Security/SharedKernel.Security.Oidc` |
| `IUserContext` or `ITenantProvider` interface | `12.Security/SharedKernel.Security.Abstractions` |

---

## Abstractions Packages (Interface Contracts)

These are the packages microservices should depend on — never on the concrete provider:

| Abstraction package | Implemented by |
|---------------------|---------------|
| `SharedKernel.Caching.Abstractions` | `.FusionCache`, `.Redis` (L2, via `.Redis.Core`), `.Redis.DistributedLocking`, `.Redis.HashStore`, `.Redis.PubSub` |
| `SharedKernel.Persistence.Abstractions` | `.EfCore` (write + read repos, `IUnitOfWork` + `ITransactionalUnitOfWork`), `.PostgreSQL` (Npgsql + conventions, `NpgsqlConnectionFactory` implementing `IDbConnectionFactory`), `.Dapper` (read-side `DapperReadService`; references `.PostgreSQL` for the connection factory) |
| `SharedKernel.Messaging.Abstractions` | `.MassTransit` |
| `SharedKernel.Storage.Abstractions` | `.S3` |
| `SharedKernel.Search.Abstractions` | `.Meilisearch`, `.ElasticSearch` |
| `SharedKernel.AI.Abstractions` | `.VectorDb` |
| `SharedKernel.Security.Abstractions` | `.Oidc` |

---

## Solution Format

- Solution file: `Platform.SharedKernel.slnx` (.NET 10 new XML solution format)
- Target framework: `net10.0` across all projects
- Each numbered folder maps to a solution folder of the same name inside the `.slnx`
- No Directory.Build.props or NuGet references are managed here yet — see `PLATFORM.md` for build-wide configuration decisions

---

## Changelog

> Maintained by `/sync-brain`. Each entry is one line: what changed and who triggered it.

- [2026-05-13] Initial architecture brain written — folder map, layering rules, naming conventions, abstractions table (root)
- [2026-05-22] AOT policy relaxed — removed `<IsAotCompatible>true</IsAotCompatible>` from all 37 production csproj files; hard rule replaced with pragmatic AOT-preferred guidance
- [2026-05-21] "What Goes Where" and Abstractions table updated: SharedKernel.Caching.Abstractions is now the canonical abstraction; SharedKernel.Caching.FusionCache row added (arch-lead, WO-007 analysis)
- [2026-05-27] Folder Map 03 updated: tenanted aggregate bases added; "What Goes Where" row added for tenant-scoped aggregates (arch-lead, WO-010)
- [2026-05-27] WO-011: "What Goes Where" row added for EventEnvelope<TEvent> → 04.Contracts; domain brain updated with IHasDomainEvents, DomainService, IHasVersion, DomainException hierarchy, SingleValueObject, PagedSpecification, specification sentinels, DomainEventVersion, IAggregateFactory (arch-lead)
- [2026-06-01] WO-013: Folder Map 06 updated with pgvector/Dapper detail; Abstractions table 06 expanded to list all three implementors; five "What Goes Where" rows added for EfRepository, StronglyTypedIdValueConverter, TenantedDbContext, PostgreSQL conventions, DapperReadService (arch-lead)
- [2026-06-03] WO-017: Abstractions table 06 updated with ITransactionalUnitOfWork; two "What Goes Where" rows added for explicit transaction scope and paged DTO projection (arch-lead)
- [2026-06-03] WO-018: Abstractions table 06 corrected — NpgsqlConnectionFactory lives in .PostgreSQL not .Dapper; five "What Goes Where" rows added for write-side spec fetch, IDbContextFactory, custom interceptors, string includes, and EfCore compiled model; Dapper "What Goes Where" row updated (arch-lead)
- [2026-06-04] WO-019: four "What Goes Where" rows added for field-level encryption, key rotation job, service identity options, and encryption-attribute prohibition; AES-256-GCM converter + versioned ciphertext + IModelFinalizingConvention pattern documented in 06.Persistence brain; audit fallback rule updated to use configurable PersistenceServiceOptions.ServiceName instead of hardcoded "system" (arch-lead)
- [2026-06-09] WO-022: ten "What Goes Where" rows added for IIdempotencyStore, IMessageHeaderPropagator, ConsumerDefinitionBase, IMessageVersionTranslator, RoutingSlipActivityBase/IRoutingSlipBuilder, and InMemoryMessageBus/InMemoryEventPublisher test doubles (arch-lead)
- [2026-06-11] WO-023: 02.Caching Redis package split into Core + 4 role-specific packages (L2/.Redis, DistributedLocking, HashStore, PubSub); new .{Provider}.Core/.{Provider}.{Role} naming pattern documented; hard rule added forbidding 02.Caching <-> 07.Messaging cross-references; four "What Goes Where" rows added (arch-lead, P-140-P-146)
- [2026-06-12] WO-024: 06.Persistence gap-fill — encryption rotation reflection/version-coupling fix (P-147), set-based bulk mutations via ExecuteUpdate/ExecuteDelete (P-148), IAsyncEnumerable streaming reads (P-149), DB readiness probe contract for 13.ServiceDefaults health checks (P-150), idempotent migration/seed runner via EfCorePersistenceBuilder (P-151); 03.Domain gains StronglyTypedIdJsonConverterFactory for StronglyTypedId<TValue> (P-152), superseding prior "no STJ converter" note; 00.Governance gains platform-wide SK0xxx rule forbidding MakeGenericMethod/Invoke reflection outside documented exceptions (P-153) (arch-lead, P-147-P-153)
- [2026-06-15] WO-023 closeout (P-145): 00.Governance ships RedisTopologyRules (NetArchTest) mechanically enforcing the 02.Caching <-> 07.Messaging exclusion and the five-package Redis topology (Redis.Core never references capability packages, capability packages never reference each other, Redis.PubSub never references SharedKernel.Messaging.*, SharedKernel.Messaging.* never references SharedKernel.Caching.*, Caching.Abstractions re-verified dependency-free); 9/9 + 75/75 architecture tests passing (arch-lead, P-145)
- [2026-06-16] WO-025: Folder Map 11 expanded with all 4 packages; six "What Goes Where" rows added for Rest, Internal, Grpc, GraphQL, Protobuf helpers, and raw HttpClient prohibition (arch-lead, P-154–P-159)
