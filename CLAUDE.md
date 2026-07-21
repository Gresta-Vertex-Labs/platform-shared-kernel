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
| 01 | `01.Core` | Primitives (`Result<T>`, `Error`, `IClock`, SmartEnums), base exceptions, system extensions, Options-pattern validation, Feature Flags, dependency-free cryptographic primitives (password hashing, AES-GCM symmetric encryption, RSA/ECDSA + HMAC signing, secure random generation), platform-wide logging `EventId` range registry, well-known cross-domain propagation constants (HTTP header names, OTel baggage keys for correlation-id/tenant-id) |
| 02 | `02.Caching` | FusionCache L1/L2 interfaces, stampede protection. Redis split by role: `.Redis.Core` (shared connection/health/resilience), `.Redis` (L2 backplane), `.Redis.DistributedLocking` (RedLock), `.Redis.HashStore`, `.Redis.PubSub` (cache invalidation signaling) |
| 03 | `03.Domain` | DDD building blocks: `Entity`, `AggregateRoot`, `ValueObject`, `IDomainEvent`, tenanted aggregate bases (`TenantedAggregateRoot`, `TenantedAuditableAggregateRoot`, `TenantedFullAuditableAggregateRoot`) |
| 04 | `04.Contracts` | Cross-service DTOs only: `PagedList`, `Envelope`, integration event payloads. No domain logic. |
| 05 | `05.Application` | MediatR base handlers and dispatchers, pipeline behaviors (Validation, Logging, Metrics, Transaction) |
| 06 | `06.Persistence` | Repository and UoW abstractions, EF Core interceptors (Audit/SoftDelete/Outbox/Concurrency), Npgsql/PostgreSQL defaults (SnakeCase, JSONB, pgvector via `Pgvector.EntityFrameworkCore`), Dapper type handlers and `DapperReadService` base |
| 07 | `07.Messaging` | `IMessageBus` / `IEventPublisher` abstractions (CloudEvents), MassTransit pre-configured bus (Retry, Outbox, RabbitMQ/ASB) |
| 08 | `08.Storage` | `IFileStorage` / `IBlobUriGenerator` abstractions, AWS S3 / MinIO implementation, Huawei Cloud OBS implementation (over its S3-compatible endpoint) |
| 09 | `09.Search` | `ISearchIndex` / `IQueryBuilder` abstractions, Meilisearch (BFF/fast), ElasticSearch (analytics/heavy) |
| 10 | `10.Intelligence` | `SharedKernel.AI.Abstractions` (embedding generation, vector-collection, and `ISemanticKernel`-shaped orchestration contracts — zero third-party NuGet dependency, `Microsoft.Extensions.AI.Abstractions` deliberately not adopted as the surface), `SharedKernel.AI.Qdrant` (primary vector db, via `Qdrant.Client`), `SharedKernel.AI.Milvus` (secondary vector db, via `Milvus.Client`), `SharedKernel.AI.SemanticKernel` (LLM orchestration, via `Microsoft.SemanticKernel`) |
| 11 | `11.Communication` | `SharedKernel.Communication.Rest` (typed HttpClient + Polly v8 resilience, CorrelationId/TenantId delegation handlers, ProblemDetails deserialization), `SharedKernel.Communication.Grpc` (gRPC channel factory + OTel tracing interceptors + Protobuf helpers), `SharedKernel.Communication.GraphQL` (HotChocolate server-side conventions: snake_case, filtering, sorting, paging, error mapping), `SharedKernel.Communication.Internal` (K8s headless DNS resolver + static dev resolver, `IServiceEndpointResolver`) |
| 12 | `12.Security` | `IUserContext`, `ITenantProvider` abstractions, JWT mapping, Role/Policy logic, Azure B2C integration |
| 13 | `13.ServiceDefaults` | OpenTelemetry wiring (traces, metrics, and logs export with ambient TenantId/CorrelationId log enrichment), HealthChecks, Startup/Liveness probes, Tenant resolution (Header/Claim/DB isolation) |
| 14 | `14.Presentation` | `SharedKernel.Presentation.WebApi` (RFC 9457 ProblemDetails, global IExceptionHandler, API versioning, native OpenAPI + Scalar, correlation-id middleware, Result\<T\>→HTTP extensions), `SharedKernel.Presentation.SignalR` (Hub filters, tenant group naming, Redis scale-out backplane) |
| 15 | `15.Integration` | Outbound Webhook dispatcher, signature verification |
| 16 | `16.Testing` | Testcontainers setup, Bogus faker factories, auth mocks, structured log assertion test double — shared test helpers consumed by all `.Tests` projects |
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
14.Presentation     → may reference 01.Core, 04.Contracts, 12.Security (table allows 13.ServiceDefaults; not used in practice — see WO-031)
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

## Logging Conventions

All production logging platform-wide follows one mechanically-enforced shape so log output stays queryable and alertable in aggregate across every microservice that consumes this kernel (enforced by `00.Governance`).

- Every production log statement uses the `[LoggerMessage]` source-generated partial-method pattern (`Microsoft.Extensions.Logging.Abstractions`). Direct `ILogger.LogInformation/LogWarning/LogError/LogCritical/LogTrace/LogDebug(...)` extension-method calls and hand-written `LoggerMessage.Define<>()` static delegates are prohibited in shipped production code.
- Every `[LoggerMessage]` method sets an explicit `EventId` — never rely on compiler auto-numbering, which is unstable across edits to the same class.
- `EventId` ranges are reserved per capability domain using `{two-digit domain number} * 1000` through `+999` — e.g. `01.Core`=1000-1999, `02.Caching`=2000-2999, `05.Application`=5000-5999, `07.Messaging`=7000-7999, `11.Communication`=11000-11999, `13.ServiceDefaults`=13000-13999, `14.Presentation`=14000-14999, `15.Integration`=15000-15999. The registry of domain base values lives in `01.Core`. A domain with multiple packages subdivides its 1000-wide block into 100-wide sub-blocks, one per package, in declaration order.
- Message template placeholders are PascalCase named properties matching the call's named arguments (e.g. `{RequestName}`, `{CorrelationId}`) — never positional placeholders, never string-interpolated into the template.
- CorrelationId, distributed-trace context (TraceId/SpanId), and TenantId are never passed as explicit message-template placeholders — they flow ambiently through the OpenTelemetry logging pipeline (`13.ServiceDefaults`) and `14.Presentation`'s correlation-id middleware; call sites never repeat this boilerplate.
- Logging a structured payload that may carry sensitive fields requires a self-supplied loggable-field surface on the type being logged (mirroring `ILoggableRequest<TResponse>` in `05.Application.Behaviors`) — never a reflection-based property walk, never message-template destructuring (`{@Object}`) of a raw domain/DTO object.
- `03.Domain` and `04.Contracts` remain logging-free — `ILogger` is an infrastructure-facing concern and must never be injected into domain or contract types.

---

## Magic String / Named Constants Convention

No raw string literal may be used at a call site for an identifier that is (a) referenced from more than one call site, or (b) part of a cross-service/cross-process wire contract — HTTP/gRPC header names, `Activity`/OTel baggage or tag keys, `IConfiguration` section/key names, claim-type names, cache-key components, and similar. Always a named constant (a `static class` of `const string` / `static readonly` fields), never a retyped literal. This mirrors the `EventId`-range-registry discipline (`LoggingEventIdRanges`, WO-041) applied to strings instead of numbers.

- **Domain-local magic strings** (used only within one package) live in a small constants class colocated in that package. The platform already has gold-standard exemplars of this — no changes needed to any of them: `SecurityClaimTypes` (`12.Security.Abstractions`), `WebhookSignatureHeaders` (`15.Integration`), `HubGroupNaming` (`14.Presentation.SignalR`), and `ICacheKeyProvider`/`CacheKeyProvider` (`02.Caching`).
- **Cross-domain wire-format constants** that must be byte-identical across independently-layered packages (HTTP header names and OTel baggage keys used for correlation-id/tenant-id propagation) live in `01.Core` as `WellKnownHeaders` / `WellKnownBaggageKeys` — **not** `04.Contracts`. `04.Contracts` was considered and rejected: `SharedKernel.Communication.Grpc` carries a hard, mechanically-enforced rule (P-163) forbidding a `04.Contracts` reference, so routing propagation constants through `04.Contracts` would reintroduce exactly the coupling that rule removed. `01.Core` is the only layer every consumer (`11.Communication`, `13.ServiceDefaults`, `14.Presentation`, `07.Messaging`) already references unconditionally.
- **Configuration section names / Options binding paths** are a `public const string SectionName = "..."` colocated on the Options type itself — never a bare string literal passed to `GetSection(...)` at each call site.
- **Motivating defect:** `14.Presentation`'s `CorrelationIdMiddleware` writes `Activity` baggage under `"correlation.id"`, while `13.ServiceDefaults`'s `BaggageLogRecordProcessor` test suite independently hardcoded `"CorrelationId"` for the same concept — a confirmed mismatch (flagged as DO-07 in WO-041, left unfixed until WO-042/P-261) that a shared constant would have made structurally impossible. The tenant/correlation header name was likewise independently redeclared three times (`11.Communication.Rest`, `11.Communication.Grpc`, `13.ServiceDefaults.MultiTenancy`) with no shared source.
- Enforced by `00.Governance`'s `SK0022` analyzer, which flags a raw literal at recognized magic-string call-site shapes (header indexers/setters, `Activity.SetBaggage`/`.SetTag`, `IConfiguration.GetSection`, `ClaimsPrincipal`/`Claim` comparisons) — it passes any named-constant reference regardless of which class declares it, so domain-local constant holders remain fully valid, not just the `01.Core` cross-domain ones.

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
| A permission/policy gate on a MediatR command or query before its handler runs | `05.Application/SharedKernel.Application.Behaviors` — implement `IAuthorizeRequest` on the request type; `AuthorizationBehavior<TRequest,TResponse>` short-circuits with `Result.Failure(Error.Unauthorized(...))` (never throws); evaluated against a local `IAuthorizationContext` seam — never inject `SharedKernel.Security.Abstractions.IUserContext` directly into this package; the consuming service bridges `IAuthorizationContext` to its real `IUserContext`/`ITenantProvider` at the composition root, mirroring the existing `IUnitOfWork`/`TransactionBehavior` bridge |
| Duplicate-submission protection for an in-process MediatR command (double-click, client retry) | `05.Application/SharedKernel.Application.Behaviors` — implement `IIdempotentRequest` on the command type (commands only, never queries); `IdempotentCommandBehavior<TRequest,TResponse>` short-circuits on a duplicate key without invoking `next()` twice; backed by a local idempotency-key seam — never reference `07.Messaging.Abstractions.IIdempotencyStore` directly from this package. A store may additionally opt in to response replay (WO-039 P-242): retrieving/persisting the original response for a key so a genuine retry-after-ambiguous-outcome returns the original result instead of always `Error.Conflict`; default (non-opted-in) behavior is unchanged |
| A zero-prerequisite onboarding default for a new microservice's MediatR pipeline (logging, metrics, tracing, validation with no infrastructure bridge required) | `05.Application/SharedKernel.Application.Behaviors` — a single `ApplicationBehaviorsBuilder` preset method, exactly equivalent to calling the four individual `.AddXBehavior()` methods that carry no `Build()`-time missing-dependency guard; infrastructure-gated behaviors (Transaction/Authorization/Caching/CacheInvalidation/Idempotency/Resilience/FireAndForget/Streaming) are never bundled into this preset — each remains a deliberate, individual opt-in (WO-039 P-243) |
| Opt-in structured request/response payload logging with redaction on a command or query | `05.Application/SharedKernel.Application.Behaviors` — the request/query type supplies its own redacted/loggable field set (a self-supplied marker, mirroring `ICacheableQuery.CacheKey`/`IInvalidatesCache.CacheKeysToInvalidate`'s existing pattern); never a reflection-based property walk over an arbitrary `TRequest`; a request that does not opt in logs identically to today (WO-040 P-246) |
| A new structured log statement in production code | The log statement's own domain package — author via the `[LoggerMessage]` source-generated pattern with an explicit `EventId` inside that domain's reserved range from the `01.Core` registry; never a direct `ILogger` extension-method call or hand-written `LoggerMessage.Define` (WO-041) |
| An `EventId` range reservation for a domain/package that starts logging | `01.Core` — read the domain-number-based registry (`{domain number} * 1000`..`+999`, 100-wide sub-blocks per package in declaration order); never invent an ad hoc numeric range (WO-041, P-249) |
| A test assertion on structured log output (`EventId`, level, structured property value) in a consuming service's own test suite | `16.Testing/SharedKernel.Testing` — use the in-memory `ILogger`/`ILoggerFactory` test double; never hand-roll an `ILogger` mock or assert on rendered message strings (WO-041, P-258) |
| Ad hoc `ILogger.LogXxx()` extension-method call or hand-written `LoggerMessage.Define` delegate in production code | Prohibited platform-wide — always the `[LoggerMessage]` source-generated pattern; enforced by a `00.Governance` analyzer + architecture test, mirroring the raw-`HttpClient` (P-159) and inline-`ProblemDetails` (P-199) precedents (WO-041, P-250) |
| A magic string used in more than one place, or part of a cross-service/wire contract (HTTP header name, OTel baggage key, config section name, claim type) | A domain-local static constants class in the owning package (e.g. `SecurityClaimTypes`, `WebhookSignatureHeaders`, `HubGroupNaming`); a cross-domain wire-propagation constant shared across `11.Communication`/`13.ServiceDefaults`/`14.Presentation` (correlation-id/tenant-id header or baggage key) → `01.Core`'s `WellKnownHeaders`/`WellKnownBaggageKeys` — never `04.Contracts` (blocked by the existing Grpc/P-163 no-`04.Contracts`-reference rule) (WO-042, P-259) |
| A hand-rolled `IConfiguration.GetSection("...")` string literal repeated at more than one call site | A `public const string SectionName` colocated on the corresponding Options type — never a bare literal at each call site (WO-042) |
| A raw string literal for an HTTP header name, OTel `Activity` baggage/tag key, or claim-type comparison | Prohibited — always a named constant; enforced platform-wide by `SK0022` (`00.Governance`), mirroring the raw-`HttpClient` (P-159), inline-`ProblemDetails` (P-199), and ad hoc-logging (P-250) enforcement precedents (WO-042, P-264) |
| A `Result<T>` change or new primitive type | `01.Core/SharedKernel.Primitives` |
| A new extension method on BCL types | `01.Core/SharedKernel.Core` |
| An Options-pattern validator | `01.Core/SharedKernel.Configuration` |
| A feature flag abstraction | `01.Core/SharedKernel.FeatureManagement` |
| Password hashing, symmetric (AES-GCM) or asymmetric (RSA/ECDSA) encryption and signing, HMAC signing, or cryptographically secure random/token generation | `01.Core/SharedKernel.Cryptography` — use `IPasswordHasher`, `ISymmetricEncryptionService`, `IAsymmetricSignatureService`, `IHmacSigner`, `ISecureRandomGenerator` via `AddSharedKernelCryptography()`; zero third-party NuGet dependencies, zero ASP.NET Core/JWT/OIDC dependency — safe for worker/non-web services that need crypto without an identity stack |
| General-purpose encrypt/decrypt of arbitrary payloads outside an EF Core column (e.g., before publishing to a queue, writing to blob storage, or returning from an API) | `01.Core/SharedKernel.Cryptography` — `ISymmetricEncryptionService`. Distinct from `06.Persistence`'s `EncryptedValueConverter` (P-019), which remains the dedicated path for transparent EF Core column-level encryption — do not route column encryption through `SharedKernel.Cryptography` directly or vice versa |
| Hand-rolled password hashing (raw `SHA256`/`MD5`), `System.Random`/`Guid.NewGuid()` for security tokens or keys, or unauthenticated symmetric encryption (AES-CBC without a MAC) | Prohibited anywhere in the platform — always use `SharedKernel.Cryptography`'s `IPasswordHasher` / `ISecureRandomGenerator` / `ISymmetricEncryptionService` (AEAD-only) |
| A new architecture enforcement rule | `00.Governance/SharedKernel.ArchitectureTests` |
| A platform-wide reflection-based generic method invocation (`Type.GetMethod`/`GetMethods` + `MakeGenericMethod` + `Invoke`) | Forbidden — use typed dispatch or expression trees instead; enforced platform-wide by an SK0xxx rule in `00.Governance/SharedKernel.ArchitectureTests`, with an explicit documented-exception mechanism for rare justified cases |
| A new Roslyn analyzer | `00.Governance/SharedKernel.Analyzers` |
| Shared test fakers or container setup | `16.Testing/SharedKernel.Testing` |
| In-process test double for `IMessageBus` in unit tests | `16.Testing/SharedKernel.Testing` — use `InMemoryMessageBus`; assert via `ShouldHavePublished<T>()`, `ShouldHaveSent<T>()`, `ShouldHavePublishedOnce<T>()`, `ShouldNotHavePublished<T>()`; register via `AddInMemoryMessageBus()`; references only `SharedKernel.Messaging.Abstractions` |
| In-process test double for `IEventPublisher` in unit tests | `16.Testing/SharedKernel.Testing` — use `InMemoryEventPublisher`; assert via `PublishedOf<TEvent>()` and assertion helpers; register via `AddInMemoryEventPublisher()`; references only `SharedKernel.Messaging.Abstractions` |
| In-process test doubles for `05.Application.Behaviors`' local-seam interfaces (`IUnitOfWork`, `IAuthorizationContext`, `IIdempotencyKeyStore`/`IIdempotencyResponseStore`) in unit tests | `16.Testing/SharedKernel.Testing` — mirrors the `InMemoryMessageBus`/`InMemoryEventPublisher` and `FakeUserContext`/`FakeTenantProvider` precedent; references only `SharedKernel.Application.Behaviors`, never the real cross-domain interfaces those seams bridge to in production (WO-040 P-244) |
| A publicly consumable MediatR pipeline test harness for asserting a consuming service's own command/query behavior composition (Logging/Metrics/Tracing/Validation/Authorization/etc.) in its own test suite | `16.Testing/SharedKernel.Testing` — built on `05.Application.Behaviors`' `ApplicationBehaviorsBuilder`; promotes the previously `SharedKernel.Application.Behaviors.Tests`-internal-only pipeline harness into a first-class, packaged capability, since `16.Testing` may reference any layer including `05.Application.Behaviors` (WO-040 P-245) |
| A standalone testing-infrastructure helper with no consuming-domain interface to anchor against (fluent builder, assertion-helper class, faker-seeding convention) | `16.Testing/SharedKernel.Testing.SelfTests` — proven there; a fake/double that implements another domain's interface (e.g. `FakeCacheService` → `ICacheService`) still proves itself via that owning domain's own contract tests, unchanged (WO-029) |
| A Temporal workflow activity or state machine base | `17.Workflows/SharedKernel.Workflows.Temporal` |
| A typed HttpClient with Polly v8 resilience, CorrelationId/TenantId header injection, and ProblemDetails deserialization | `11.Communication/SharedKernel.Communication.Rest` — use `AddSharedKernelRestCommunication().AddRestClient<TClient>()`; never inject raw `HttpClient` directly in a production constructor |
| K8s headless DNS resolution or static dev/test service address resolution | `11.Communication/SharedKernel.Communication.Internal` — inject `IServiceEndpointResolver`; use `AddK8sServiceDiscovery()` (production) or `AddStaticServiceDiscovery()` (dev/test only — logs `Warning` at startup); never construct `Uri` from config values directly inside typed clients |
| A gRPC typed client with OTel tracing interceptor and CorrelationId/TenantId metadata injection | `11.Communication/SharedKernel.Communication.Grpc` — use `AddSharedKernelGrpcCommunication().AddGrpcClient<TClient>()`; interceptors are registered globally (not per-call); never inject `GrpcChannel` directly |
| Protobuf well-known type conversion (`Money` ↔ `decimal`, `Timestamp` ↔ `DateTimeOffset`) | `11.Communication/SharedKernel.Communication.Grpc` — use `MoneyProtoExtensions` / `TimestampProtoExtensions`; pure static, allocation-minimal |
| HotChocolate server-side GraphQL conventions (snake_case naming, filtering, sorting, paging, error mapping) | `11.Communication/SharedKernel.Communication.GraphQL` — call `AddSharedKernelGraphQL()` before any service-specific `AddGraphQL()`/`AddTypes()` calls; never inherit `FilterInputType<T>` or `SortInputType<T>` directly — always extend `FilterBase<T>` / `SortBase<T>` |
| Raw `HttpClient` injection in a production constructor | Prohibited — always use `IHttpClientFactory`-managed typed clients via `AddRestClient<TClient>()`; enforced platform-wide by SK0xxx governance rule (P-159) |
| `Result<T>` → `Envelope<T>` boundary mapping | `04.Contracts/SharedKernel.Contracts` — use `result.ToEnvelope()` / `envelope.ToResult()` from `SharedKernel.Contracts.Mapping.ResultEnvelopeExtensions`; inline `if (result.IsSuccess) Envelope<T>.Ok(...) else Envelope<T>.Fail(...)` at service boundaries is a platform violation (WO-026 P-166/P-167) |
| A GraphQL `PagedResponseType<T>` from a `PagedList<T>` source | `11.Communication/SharedKernel.Communication.GraphQL` — call `PagedResponseType<T>.FromPagedList(pagedList)`; never unpack `.Items`/`.TotalCount` manually in a resolver (WO-026 P-165) |
| K8s service-discovery endpoint resolution caching | `11.Communication/SharedKernel.Communication.Internal` — configure via `K8sServiceDiscoveryOptions.EndpointCacheTtlSeconds` (default 30s, `0` disables); never add an ad-hoc `TimeSpan`/`int` TTL field on a typed client or DI registration (WO-026 P-164) |
| Multiple typed REST clients sharing service discovery | `11.Communication/SharedKernel.Communication.Rest` — pass the resolved base address from `IServiceEndpointResolver` into each `AddRestClient<TClient>()` registration via the inline factory pattern; never register `ServiceDiscoveryResolvingHandler` directly as a named `DelegatingHandler` from consuming service code (WO-026 P-162) |
| OTel, health check, or probe wiring | `13.ServiceDefaults/SharedKernel.ServiceDefaults` |
| Tenant resolution logic | `13.ServiceDefaults/SharedKernel.MultiTenancy` |
| JWT / OIDC / B2C wiring | `12.Security/SharedKernel.Security.Oidc` |
| `IUserContext` or `ITenantProvider` interface | `12.Security/SharedKernel.Security.Abstractions` |
| RFC 9457 ProblemDetails error mapping, global IExceptionHandler, API versioning, native OpenAPI + Scalar, correlation-id middleware, or `Result<T>`→HTTP boundary extensions | `14.Presentation/SharedKernel.Presentation.WebApi` — use `Error.ToProblemDetails()` / `ResultHttpExtensions`; correlation-id middleware owns its own `Activity` baggage key directly against `System.Diagnostics.Activity` (BCL) — no `13.ServiceDefaults` reference needed (WO-031) |
| A SignalR hub filter (tenant context attachment, exception-to-HubException mapping), tenant group naming convention, or Redis-backed SignalR scale-out backplane | `14.Presentation/SharedKernel.Presentation.SignalR` — register both filters globally via `AddSharedKernelSignalR()`; backplane is opt-in via `WithRedisBackplane()`; never shares an `IConnectionMultiplexer` with `02.Caching.Redis.Core` (WO-031) |
| Hand-rolled `ProblemDetails` construction or inline `Result.IsSuccess`/`IsFailure` branching immediately before returning an HTTP result type | Prohibited outside `SharedKernel.Presentation.WebApi` — always use `Error.ToProblemDetails()` / `ResultHttpExtensions`; enforced platform-wide by SK0xxx governance rules, mirroring the raw-`HttpClient` (P-159) and `Result`/`Envelope` (WO-026 P-166/167) precedents (WO-031 P-199) |
| Uploading, downloading, deleting, or listing a binary blob (document, image, export, attachment) | `08.Storage/SharedKernel.Storage.Abstractions` — inject `IFileStorage`; never a concrete cloud SDK type (e.g. `IAmazonS3`) in application code (WO-043) |
| Generating a presigned upload/download URL for direct client-to-storage transfer | `08.Storage/SharedKernel.Storage.Abstractions` — inject `IBlobUriGenerator` (WO-043) |
| A server-side object copy or a batch/multi-key delete over stored blobs | `08.Storage/SharedKernel.Storage.Abstractions` — `IFileStorage`'s copy/batch-delete operations; never a hand-rolled download+upload round-trip or an N-call delete loop (WO-043, P-265) |
| AWS S3 or MinIO as the object-storage backend | `08.Storage/SharedKernel.Storage.S3` — `AddSharedKernelS3Storage()`; MinIO via `ServiceUrl` + `ForcePathStyle` on the same code path (WO-043, P-266) |
| Huawei Cloud OBS as the object-storage backend | `08.Storage/SharedKernel.Storage.Obs` — `AddSharedKernelObsStorage()`, over OBS's S3-compatible endpoint via `AWSSDK.S3` (the native Huawei SDK was rejected as stale/.NET Standard 2.0/personal-account-maintained); never references `SharedKernel.Storage.S3` (WO-043, P-267) |
| An object-storage readiness probe for K8s health checks | `08.Storage` provides the connectivity-probe primitive on `IFileStorage`; `08.Storage` ships no `IHealthCheck` implementation — wiring into `AddHealthChecks()` remains a `13.ServiceDefaults` concern, mirroring the existing `06.Persistence` DB-readiness-probe split (WO-043, P-265/P-270) |
| An in-process test double for `IFileStorage`/`IBlobUriGenerator` in unit tests | `16.Testing/SharedKernel.Testing` — use the in-memory `IFileStorage`/`IBlobUriGenerator` double; mirrors the `InMemoryMessageBus`/`InMemoryEventPublisher` precedent; references only `SharedKernel.Storage.Abstractions` (WO-043, P-269) |
| Indexing, deleting, searching, or corpus-walking documents in a full-text index | `09.Search/SharedKernel.Search.Abstractions` — inject `ISearchIndex<TDocument>`; never a concrete engine SDK type, mirroring the `IFileStorage`/no-raw-SDK precedent (WO-044, P-272) |
| Provisioning a search index, running a staging→live cutover, or probing index readiness | `09.Search/SharedKernel.Search.Abstractions` — inject `ISearchIndexProvisioner`; index creation/settings are idempotent and additive-only, never a silent mapping rewrite (WO-044, P-272) |
| Building a filtered/faceted/paged search query | `09.Search/SharedKernel.Search.Abstractions` — use `SearchQuery.For<TDocument>()`/`IQueryBuilder<TDocument>` (string field names, no `IQueryable`/expression trees) or construct `SearchRequest` directly; both paths validate through the same executor (WO-044, P-272) |
| A structured filter predicate on a search query (equality, range, in-list, boolean composition) | `09.Search/SharedKernel.Search.Abstractions` — compose via `SearchFilter`'s static factories (`Eq`/`Ne`/`In`/`Between`/`Exists`/`All`/`Any`/`Negate`); a closed, compiler-exhaustive 8-node AST, never hand-implemented (WO-044, P-272) |
| A tenant-scoped search or filtered write | `09.Search/SharedKernel.Search.Abstractions` — pass `TenantScope` as its own mandatory method parameter, never folded into the filter tree or the request object; a missing scope on a tenant-declared index fails closed before any I/O (WO-044, P-272) |
| BFF-fast search needing typo tolerance, prefix/instant search, or an engine-enforced per-tenant search token | `09.Search/SharedKernel.Search.Meilisearch` — Meilisearch-exclusive contracts declared only in that package (`IInstantSearch<TDocument>`, `ITenantSearchTokenIssuer`); referencing them against an ElasticSearch-only composition root fails to compile — the platform's chosen capability-mismatch mechanism instead of a runtime flag check (WO-044, P-273) |
| Analytics/heavy search needing structured aggregations or cursor-based deep-pagination export | `09.Search/SharedKernel.Search.ElasticSearch` — ElasticSearch-exclusive contracts declared only in that package (`IAnalyticsSearch<TDocument>`, `ICursorSearch<TDocument>`) (WO-044, P-274) |
| A search-index readiness probe for K8s health checks | `09.Search` provides the probe primitive on `ISearchIndexProvisioner`; `09.Search` ships no `IHealthCheck` implementation — wiring into `AddHealthChecks()` remains a `13.ServiceDefaults` concern, mirroring the existing `06.Persistence`/`08.Storage` readiness-probe split (WO-044, P-277) |
| An in-process test double for `ISearchIndex`/`ISearchIndexProvisioner`/`ISearchProviderDescriptor` in unit tests | `16.Testing/SharedKernel.Testing` — mirrors the `InMemoryMessageBus`/`InMemoryFileStorage` precedent; references only `SharedKernel.Search.Abstractions` (WO-044, P-276) |
| Generating text embeddings (single or batched, text-to-vector) | `10.Intelligence/SharedKernel.AI.Abstractions` — inject the embedding-generation contract; never a raw model SDK type; every write/query validates the collection's declared embedding-model identity, vector dimension, and distance metric before any I/O and rejects on mismatch (WO-045, P-279) |
| Vector collection provisioning, upsert/delete, similarity query, get-by-id, count, or a large-result streaming scroll | `10.Intelligence/SharedKernel.AI.Abstractions` — mandatory non-defaulted tenant-scope parameter on every read and filtered write, injected as the outermost filter conjunction, never a member of the request object or the caller-supplied filter (WO-045, P-279) |
| LLM prompt/chat invocation, streaming invocation, or tool/function invocation | `10.Intelligence/SharedKernel.AI.Abstractions` (`ISemanticKernel`-shaped contract), implemented by `SharedKernel.AI.SemanticKernel`; token usage is a first-class result output; no retry is silently applied to a completion call (re-bills/re-rolls); prompt/completion text is never a log-message parameter (WO-045, P-282) |
| Qdrant as the vector-database backend | `10.Intelligence/SharedKernel.AI.Qdrant` — `Qdrant.Client` (WO-045, P-280) |
| Milvus as the vector-database backend | `10.Intelligence/SharedKernel.AI.Milvus` — `Milvus.Client`; maintenance status verified before implementation, mirroring the `NEST`/`09.Search` staleness check (WO-045, P-281) |
| A vector-DB-provider-exclusive capability (sparse/hybrid vectors, quantization profiles, partition-key models, consistency-level tuning) | Declared only inside the owning provider package (`SharedKernel.AI.Qdrant` or `SharedKernel.AI.Milvus`), never in `.Abstractions` — a provider swap referencing it fails at compile time, not at runtime, mirroring the `09.Search` Meilisearch/ElasticSearch-exclusive-contract precedent (WO-045, P-280/P-281) |
| A vector-store or LLM-orchestration readiness probe for K8s health checks | `10.Intelligence` provides the `ProbeAsync`-shaped probe primitive on the provider-descriptor surface; `10.Intelligence` ships no `IHealthCheck` implementation — wiring into `AddHealthChecks()` remains a `13.ServiceDefaults` concern, mirroring the `06.Persistence`/`08.Storage`/`09.Search` readiness-probe split (WO-045, P-285) |
| An in-process test double for the embedding-generation, vector-collection, or LLM-orchestration contracts in unit tests | `16.Testing/SharedKernel.Testing` — the default embedding double is a deterministic hash-derived vector (no model/network needed); mirrors the `InMemoryFileStorage`/`InMemorySearchIndex<TDocument>` precedent; references only `SharedKernel.AI.Abstractions` (WO-045, P-284) |
| Raw vector-DB client (`QdrantClient`, a Milvus client) or raw LLM SDK type (`Kernel`, a model client) injected into application code | Prohibited — always the neutral `10.Intelligence` abstraction; a raw-client escape hatch, if offered at all, follows the `09.Search` three-gate pattern (opt-in builder call, startup `Warning`, governance architecture test) with its XML doc stating in capitals that it bypasses tenant scoping (WO-045, P-280/P-281/P-286) |

---

## Abstractions Packages (Interface Contracts)

These are the packages microservices should depend on — never on the concrete provider:

| Abstraction package | Implemented by |
|---------------------|---------------|
| `SharedKernel.Caching.Abstractions` | `.FusionCache`, `.Redis` (L2, via `.Redis.Core`), `.Redis.DistributedLocking`, `.Redis.HashStore`, `.Redis.PubSub` |
| `SharedKernel.Persistence.Abstractions` | `.EfCore` (write + read repos, `IUnitOfWork` + `ITransactionalUnitOfWork`), `.PostgreSQL` (Npgsql + conventions, `NpgsqlConnectionFactory` implementing `IDbConnectionFactory`), `.Dapper` (read-side `DapperReadService`; references `.PostgreSQL` for the connection factory) |
| `SharedKernel.Messaging.Abstractions` | `.MassTransit` |
| `SharedKernel.Storage.Abstractions` | `.S3`, `.Obs` |
| `SharedKernel.Search.Abstractions` | `.Meilisearch`, `.ElasticSearch` |
| `SharedKernel.AI.Abstractions` | `.Qdrant`, `.Milvus`, `.SemanticKernel` |
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
- [2026-06-19] WO-026 closeout: four "What Goes Where" rows added — Result<T>↔Envelope<T> mapping via ResultEnvelopeExtensions (P-166), PagedResponseType<T>.FromPagedList (P-165), K8sServiceDiscoveryOptions.EndpointCacheTtlSeconds (P-164), inline factory pattern for multi-client service discovery (P-162); SharedKernel.Communication.Grpc → SharedKernel.Contracts dead reference removed and locked via NetArchTest GrpcNeverReferencesContracts rule; SharedKernel.Contracts re-packed to 1.1.0 (arch-lead, P-160–P-167)
- [2026-06-22] WO-029: 14 long-pending, never-dispatched 16.Testing phases consolidated into 9 (P-179–P-187); "What Goes Where" row added for standalone testing-helper self-tests via new SharedKernel.Testing.SelfTests project (arch-lead, WO-029)
- [2026-06-25] WO-031: Folder Map 14 updated with both package names; layering table narrowed (14.Presentation does not use its allowed 13.ServiceDefaults reference — correlation-id middleware is self-contained); three "What Goes Where" rows added for WebApi conventions, SignalR hub filters/backplane, and the new ProblemDetails/Result-HTTP governance prohibition (arch-lead, P-192–P-199)
- [2026-06-26] WO-033: Folder Map 01 updated with cryptographic primitives; new `SharedKernel.Cryptography` package added to `01.Core` — password hashing (`IPasswordHasher`), AES-256-GCM symmetric encryption (`ISymmetricEncryptionService`), RSA/ECDSA + HMAC signing (`IAsymmetricSignatureService`/`IHmacSigner`), and secure random generation (`ISecureRandomGenerator`), all pure BCL `System.Security.Cryptography` with zero third-party NuGet dependencies; deliberately decoupled from `12.Security` (identity/JWT/OIDC) so non-web worker services can consume crypto primitives without an identity stack — `12.Security.Oidc` may depend on `01.Core/SharedKernel.Cryptography`, never the reverse; three "What Goes Where" rows added disambiguating it from `06.Persistence`'s existing EF Core column-encryption path and prohibiting hand-rolled crypto platform-wide (arch-lead, P-205–P-209)
- [2026-06-29] WO-035: 05.Application gold-standard build-out dispatched (P-214–P-219) — two new opt-in pipeline behaviors added to the existing five (Validation/Logging/Metrics/Transaction/Caching): `AuthorizationBehavior`+`IAuthorizeRequest` and `IdempotentCommandBehavior`+`IIdempotentRequest`, both backed by locally-owned seam interfaces (never a direct `12.Security`/`07.Messaging` reference, mirroring the existing `IUnitOfWork` bridge pattern); canonical pipeline order now seven steps; two "What Goes Where" rows added (arch-lead, P-214–P-219)
- [2026-07-03] WO-039: 05.Application gold-standard architecture review dispatched (P-236–P-244 across 01.Core/05.Application/00.Governance) — fixed a confirmed functional bug where `AddFireAndForgetDispatch()`'s globally-registered guard behavior silently blocks its own background consumer's dispatch (P-238); found P-232's claimed reflection elimination in `FailureResponseFactory` was only partial (still uses `Type.GetInterfaces`/`MakeGenericType`/`GetMethod`/`Invoke`, invisible to SK0012, no AOT annotation) and dispatched a genuine fix via a new self-referential static-abstract-interface contract in `01.Core` (P-236/P-237); dispatched the `MetricsBehavior<,>` outcome-tag retrofit and governance real-assembly wiring that 00.Governance's WO-038 audit had already flagged but left undispatched (P-239–P-241); two "What Goes Where" rows added/updated for opt-in idempotency response replay and a zero-prerequisite behavior preset (arch-lead, P-236–P-244)
- [2026-07-07] WO-040: 05.Application developer-experience gap audit dispatched (P-244–P-248 across 16.Testing/05.Application/13.ServiceDefaults/00.Governance) — closes gaps verified against shipped source, not prose: zero 16.Testing fakes exist for 05.Application.Behaviors' own local-seam interfaces, and its reusable pipeline test harness is internal-only and unusable by consumers; 13.ServiceDefaults never closed the "SharedKernel.Application" OTel-wiring forward reference 05.Application's own brain documents; three consumer-facing marker-interface misuse patterns remain code-review-only despite the SK0014-16 precedent for the identical class of gap; three "What Goes Where" rows added (arch-lead, P-244–P-248)
- [2026-07-08] WO-041: platform-wide logging standard dispatched (P-249–P-258 across 01.Core/00.Governance/13.ServiceDefaults/02.Caching/05.Application/07.Messaging/11.Communication/14.Presentation/15.Integration/16.Testing) — codebase audit found three incompatible logging-authoring styles ([LoggerMessage] attribute, hand-written LoggerMessage.Define, direct ILogger calls) coexisting with zero mechanical enforcement, plus two confirmed live EventId collisions (Caching.Redis.Core vs Redis.PubSub both using 4001/4002; three internal collisions inside Messaging.MassTransit); new "Logging Conventions" section added mandating [LoggerMessage]-only authoring, explicit EventIds inside a per-domain `{domain number}*1000..+999` range registry (01.Core), PascalCase structured templates, ambient (never explicit-placeholder) Correlation/Trace/Tenant context, and self-supplied loggable-field redaction mirroring ILoggableRequest; four "What Goes Where" rows added; all ten phases are `○` Pending in state-map.md — rule is documented now, retrofit work is queued (arch-lead, P-249–P-258)
- [2026-07-14] WO-042: platform-wide magic-string elimination dispatched (P-259–P-264 across 01.Core/11.Communication/13.ServiceDefaults/14.Presentation/07.Messaging/00.Governance) — audit found a confirmed live defect (13.ServiceDefaults's BaggageLogRecordProcessor test suite hardcoding the wrong OTel baggage-key literal "CorrelationId" vs. 14.Presentation's actual "correlation.id", previously flagged as DO-07 in WO-041 but left unfixed) plus three independent redeclarations of the tenant-header-name literal across 11.Communication.Rest/.Grpc and 13.ServiceDefaults.MultiTenancy; new "Magic String / Named Constants Convention" section added mandating named constants over raw literals, with cross-domain propagation constants placed in 01.Core (not 04.Contracts, to avoid reintroducing the P-163 Grpc-Contracts coupling); three "What Goes Where" rows added; SK0022 analyzer dispatched to 00.Governance for mechanical enforcement (arch-lead, WO-042, P-259–P-264)
- [2026-07-16] WO-043: 08.Storage gap-filled in root brain — Abstractions table corrected to list both `.S3` and `.Obs` providers; Folder Map row 08 updated to mention Huawei Cloud OBS; seven new "What Goes Where" rows added for blob upload/download/list, presigned URLs, copy/batch-delete, S3/MinIO provider, OBS provider, storage readiness probe split with 13.ServiceDefaults, and the new 16.Testing in-memory `IFileStorage` double (arch-lead, P-265–P-271)
- [2026-07-19] WO-044: 09.Search gap-filled in root brain (Folder Map row 09 and the Abstractions table already listed `.Meilisearch`/`.ElasticSearch` correctly) — nine new "What Goes Where" rows added for index write/read/corpus-walk, provisioning/cutover/probe, query building, the closed filter-predicate AST, mandatory tenant-scope parameter, Meilisearch-exclusive instant-search/tenant-token contracts, ElasticSearch-exclusive aggregation/cursor-streaming contracts, the search readiness health-check split with 13.ServiceDefaults, and the new 16.Testing in-memory search-index double (arch-lead, P-272–P-278)
- [2026-07-21] WO-045: 10.Intelligence full build-out dispatched (P-279–P-286). Domain's pre-drafted brain left two architectural-authority questions open; arch-lead ratified both — package split as Shape C (`.Abstractions` + `.Qdrant` + `.Milvus` + `.SemanticKernel` sibling providers) over the as-scaffolded single `.VectorDb` package, and declined `Microsoft.Extensions.AI.Abstractions` adoption for `.Abstractions` to preserve the platform's zero-`PackageReference`-in-Abstractions precedent. Folder Map row 10 and the Abstractions table updated to the three-provider split; eight new "What Goes Where" rows added for embedding generation, vector-collection CRUD/query, LLM orchestration, Qdrant/Milvus providers, provider-exclusive vector-DB capabilities, the readiness-probe split with 13.ServiceDefaults, the 16.Testing in-memory doubles, and the raw-SDK-client-injection prohibition (arch-lead, P-279–P-286)
