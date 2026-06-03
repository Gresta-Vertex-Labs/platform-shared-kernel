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
| 02 | `02.Caching` | FusionCache L1/L2 interfaces, stampede protection, Redis distributed cache, RedLock distributed locking |
| 03 | `03.Domain` | DDD building blocks: `Entity`, `AggregateRoot`, `ValueObject`, `IDomainEvent`, tenanted aggregate bases (`TenantedAggregateRoot`, `TenantedAuditableAggregateRoot`, `TenantedFullAuditableAggregateRoot`) |
| 04 | `04.Contracts` | Cross-service DTOs only: `PagedList`, `Envelope`, integration event payloads. No domain logic. |
| 05 | `05.Application` | MediatR base handlers and dispatchers, pipeline behaviors (Validation, Logging, Metrics, Transaction) |
| 06 | `06.Persistence` | Repository and UoW abstractions, EF Core interceptors (Audit/SoftDelete/Outbox/Concurrency), Npgsql/PostgreSQL defaults (SnakeCase, JSONB, pgvector via `Pgvector.EntityFrameworkCore`), Dapper type handlers and `DapperReadService` base |
| 07 | `07.Messaging` | `IMessageBus` / `IEventPublisher` abstractions (CloudEvents), MassTransit pre-configured bus (Retry, Outbox, RabbitMQ/ASB) |
| 08 | `08.Storage` | `IFileStorage` / `IBlobUriGenerator` abstractions, AWS S3 / MinIO implementation |
| 09 | `09.Search` | `ISearchIndex` / `IQueryBuilder` abstractions, Meilisearch (BFF/fast), ElasticSearch (analytics/heavy) |
| 10 | `10.Intelligence` | `ISemanticKernel`, `IEmbeddingGenerator` abstractions, Qdrant / Milvus vector db, LLM orchestration |
| 11 | `11.Communication` | K8s headless service discovery, gRPC (tracing interceptors, Protobuf common types), typed HttpClients (Polly v8), HotChocolate GraphQL filters |
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
| A Dapper read-side query service | `06.Persistence/SharedKernel.Persistence.Dapper` — extend `DapperReadService`; inject `IDbConnectionFactory`; use parameterized queries only |
| An explicit database transaction (multi-repo saga, two-phase write) | `06.Persistence/SharedKernel.Persistence.Abstractions` — inject `ITransactionalUnitOfWork`; call `BeginTransactionAsync` → returns `IPersistenceTransaction`; commit or rollback via that handle; never inject `IDbContextTransaction` directly |
| A paged list of DTOs (projected, with total-count metadata) | `06.Persistence/SharedKernel.Persistence.EfCore` — call `IReadRepository.ListPagedProjectedAsync<TResult>(spec, ct)`; spec must implement both `ISpecification<TAggregate>` (paging/ordering) and `IProjectionSpecification<TAggregate,TResult>` (selector); returns `PagedList<TResult>` |
| A new cache interface or policy | `02.Caching/SharedKernel.Caching.Abstractions` |
| A FusionCache L1 provider implementation or option | `02.Caching/SharedKernel.Caching.FusionCache` |
| A Redis-specific cache implementation | `02.Caching/SharedKernel.Caching.Redis` |
| A new message bus abstraction | `07.Messaging/SharedKernel.Messaging.Abstractions` |
| A MassTransit consumer base or configuration | `07.Messaging/SharedKernel.Messaging.MassTransit` |
| A `Result<T>` change or new primitive type | `01.Core/SharedKernel.Primitives` |
| A new extension method on BCL types | `01.Core/SharedKernel.Core` |
| An Options-pattern validator | `01.Core/SharedKernel.Configuration` |
| A feature flag abstraction | `01.Core/SharedKernel.FeatureManagement` |
| A new architecture enforcement rule | `00.Governance/SharedKernel.ArchitectureTests` |
| A new Roslyn analyzer | `00.Governance/SharedKernel.Analyzers` |
| Shared test fakers or container setup | `16.Testing/SharedKernel.Testing` |
| A Temporal workflow activity or state machine base | `17.Workflows/SharedKernel.Workflows.Temporal` |
| OTel, health check, or probe wiring | `13.ServiceDefaults/SharedKernel.ServiceDefaults` |
| Tenant resolution logic | `13.ServiceDefaults/SharedKernel.MultiTenancy` |
| JWT / OIDC / B2C wiring | `12.Security/SharedKernel.Security.Oidc` |
| `IUserContext` or `ITenantProvider` interface | `12.Security/SharedKernel.Security.Abstractions` |

---

## Abstractions Packages (Interface Contracts)

These are the packages microservices should depend on — never on the concrete provider:

| Abstraction package | Implemented by |
|---------------------|---------------|
| `SharedKernel.Caching.Abstractions` | `.FusionCache`, `.Redis` |
| `SharedKernel.Persistence.Abstractions` | `.EfCore` (write + read repos, `IUnitOfWork` + `ITransactionalUnitOfWork`, outbox), `.PostgreSQL` (Npgsql + conventions layer over EfCore), `.Dapper` (read-side NpgsqlConnectionFactory + DapperReadService) |
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
