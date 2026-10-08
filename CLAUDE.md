# Platform.SharedKernel

A mono-repo of NuGet packages that form the **SharedKernel** of a .NET 10 microservice ecosystem. Every package is a reusable building block; no business logic lives here.
Each package targets one project of a consuming service (Domain, Application, Infrastructure or Api/Worker). Its **tier** says which, and the build enforces what it may reference.
All packages ship together at one version through a single release train.

**Philosophy:** Capability-Oriented, Tier-Enforced, AOT-Preferred, K8s-Native, Test-Adjacent.

> **AOT is a preference, not a rule.** Write AOT-clean code (no reflection, `dynamic`, `Assembly.Load`; STJ source-generated `JsonSerializerContext`; `[DynamicallyAccessedMembers]` over bare `Activator.CreateInstance`) when it costs nothing. Drop it where it would force awkward boilerplate. Never add `<IsAotCompatible>true</IsAotCompatible>` to a project. Non-AOT third-party packages are allowed, preferably behind an abstraction.

---

## Where Things Are

The source tree is grouped into **zones that mirror a consuming service's projects**, and each capability domain is one folder inside its zone:

```
src/Foundation/       every project (Primitives, Execution, Core, Cryptography, …)
src/Model/            the Domain project (Domain/, Contracts/)
src/Application/      the Application project (kernel CQRS contracts and pipeline)
src/Infrastructure/   the Infrastructure project, one folder per capability (Caching/, Persistence/, Messaging/, …)
src/Hosting/          the Api / Worker project (Security/, ServiceDefaults/, Presentation/)
src/Testing/          SharedKernel.Testing and the Testcontainers fixtures
tools/Governance/     analyzers, architecture tests, linter
```

The zone is where a capability mainly belongs, not a rule: a capability keeps its `.Abstractions` package (referenced by the Application project) and its providers together. **The tier decides references**, and the build enforces it. [`docs/packages.md`](docs/packages.md) (generated) lists every package with its tier and the service project that references it.

Each capability folder holds three files: **`CLAUDE.md`** (maintainer rules — read it before editing that domain), **`README.md`** (overview) and **`state-map.md`** (living board: Package Board, open Phase Keys, Open Work, Blocked, Cross-Domain Dependencies). Completed work is removed from the board; `git log` is the record.

**Domain ids.** A domain keeps its id (`06.Persistence`) even though its folder has no number: the id names the domain in work orders and phase keys (`SK.06.…`), and its number is the domain's EventId block (`6000`–`6999`, registry `LoggingEventIdRanges`). The table below is the **registry**: domain id ↔ slug ↔ folder. The slug names the agent pair (`{slug}-arch-planner`, `{slug}-phase-implementer`) and is what `/implement-phase` takes; skills and agents resolve every domain through this table, never their own copy.

Counts are **packable packages** (tests, `consumer-verify` harnesses, benchmarks and `SharedKernel.Testing.Internal` excluded). Tiers: **F** Foundation · **M** Model · **Ab** Abstractions · **Ad** Adapter · **H** Host · **T** Testing · **To** Tooling.

| Domain | Slug | Folder | Pkgs | Scope | Tiers |
| --- | --- | --- | ---: | --- | --- |
| `00.Governance` | `governance` | [`tools/Governance`](tools/Governance/) | 3 | Roslyn analyzers (SK rules), NetArchTest architecture tests, EditorConfig/CSharpier linter | To |
| `01.Core` | `core` | [`src/Foundation`](src/Foundation/) | 13 | Primitives (`Result<T>`, `Error`, `IClock`, health probes, well-known headers), Execution (`IRequestContext`, tenancy, unit of work), Core, Configuration, FeatureManagement, Compression, Validation, DataPrivacy, Localization, Cryptography | F, Ad (Argon2, KeyVault.Azure, Validation.FluentValidation) |
| `02.Caching` | `caching` | [`src/Infrastructure/Caching`](src/Infrastructure/Caching/) | 7 | Cache and lock abstractions; FusionCache; Redis core + L2, locking, hash store, pub/sub | Ab, Ad |
| `03.Domain` | `domain` | [`src/Model/Domain`](src/Model/Domain/) | 1 | Entities, aggregates (audited/soft-delete/tenanted), value objects, ids, specifications, `Money` | M |
| `04.Contracts` | `contracts` | [`src/Model/Contracts`](src/Model/Contracts/) | 1 | Integration events, CloudEvents envelope, paging DTOs and cursors | M |
| `05.Application` | `application` | [`src/Application`](src/Application/) | 4 | Kernel CQRS contracts + markers; pipeline; query caching; MediatR adapter | Ab, H |
| `06.Persistence` | `persistence` | [`src/Infrastructure/Persistence`](src/Infrastructure/Persistence/) | 6 | PostgreSQL only: abstractions, Npgsql, EF Core, Dapper, audit ledger, field encryption | Ab, Ad |
| `07.Messaging` | `messaging` | [`src/Infrastructure/Messaging`](src/Infrastructure/Messaging/) | 5 | Message bus abstractions; MassTransit core + RabbitMQ, Azure Service Bus, EF Core outbox | Ab, Ad |
| `08.Storage` | `storage` | [`src/Infrastructure/Storage`](src/Infrastructure/Storage/) | 3 | Named/tenant file stores; S3 (and compatibles), Huawei OBS | Ab, Ad |
| `09.Search` | `search` | [`src/Infrastructure/Search`](src/Infrastructure/Search/) | 3 | Search index abstractions; Meilisearch, ElasticSearch | Ab, Ad |
| `10.Intelligence` | `intelligence` | [`src/Infrastructure/AI`](src/Infrastructure/AI/) | 3 | Embeddings, vector collections, orchestration; Qdrant, Semantic Kernel | Ab, Ad |
| `11.Communication` | `communication` | [`src/Infrastructure/Communication`](src/Infrastructure/Communication/) | 3 | Outbound calls: discovery/auth/mTLS base, REST client, gRPC client | Ad |
| `12.Security` | `security` | [`src/Hosting/Security`](src/Hosting/Security/) | 5 | `IUserContext`; OIDC/JWT, API key, mTLS, TOTP step-up | Ab, H |
| `13.ServiceDefaults` | `servicedefaults` | [`src/Hosting/ServiceDefaults`](src/Hosting/ServiceDefaults/) | 7 | Host composition: OTel, health, readiness, rate limiting, request context, multi-tenancy, Key Vault config, localization | H |
| `14.Presentation` | `presentation` | [`src/Hosting/Presentation`](src/Hosting/Presentation/) | 6 | Presentation.Core, WebApi, OpenApi, Grpc, SignalR, GraphQL (+ a Tooling source generator packed inside WebApi) | H |
| `15.Integration` | `integration` | [`src/Infrastructure/Integration`](src/Infrastructure/Integration/) | 4 | Webhooks; notification abstractions, SendGrid email, Twilio SMS | Ab, Ad |
| `16.Testing` | `testing` | [`src/Testing`](src/Testing/) | 20 | `SharedKernel.Testing` + 19 `SharedKernel.{Capability}.Testing` fakes (each lives in its capability folder; this domain keeps their rules and catalogue) + non-packable `Testing.Internal` fixtures | T |
| `17.Workflows` | `workflow` | [`src/Infrastructure/Workflows`](src/Infrastructure/Workflows/) | 1 | Temporal durable workflows (one package by design) | Ad |
| `18.Idempotency` | `idempotency` | [`src/Infrastructure/Idempotency`](src/Infrastructure/Idempotency/) | 3 | `IIdempotencyStore`; Redis, EF Core | Ab, Ad |
| `19.Scheduling` | `scheduling` | [`src/Infrastructure/Scheduling`](src/Infrastructure/Scheduling/) | 1 | Cron/recurring/one-shot jobs, single execution across replicas | Ad |
| `20.Reporting` | `reporting` | [`src/Infrastructure/Reporting`](src/Infrastructure/Reporting/) | 5 | Streaming CSV/Excel/PDF export, HTML → PDF (Gotenberg) | Ab, Ad |

Other top-level locations:

| Path | What |
| --- | --- |
| `samples/` | Eight reference services: `OrderApi` (the four-project shape with per-project architecture tests), `BillingApi` (persistence), `ShippingApi` (messaging over RabbitMQ), `DocumentsApi` (storage), `CatalogApi` (search), `CheckoutApi` → `InventoryApi` (11.Communication, REST + gRPC), and `Shop` (an Aspire-orchestrated platform composing most of the kernel). `samples/README.md` is the "how to consume the kernel" guide. |
| `eng/` | Build internals: `SharedKernelTiers.targets` (tier check), `PackageInventory.proj`, `verify-*.sh` scripts, test settings. See [`eng/README.md`](eng/README.md). |
| `docs/` | [`packages.md`](docs/packages.md) (generated: every package by tier and the service project that references it), [`dependency-graph.md`](docs/dependency-graph.md) (generated Mermaid graphs), `package-readme-standard.md` (the shape every package `README.md` follows). |
| `.claude/` | Commands, agents and settings for Claude Code (see "Working in This Repo with Claude Code"). |
| [`CONTRIBUTING.md`](CONTRIBUTING.md) | Contributor guide: build, test lanes, CI, release. |

---

## Tiers & Dependency Rules

Every production `.csproj` declares `<SharedKernelTier>`. `eng/SharedKernelTiers.targets` (imported by `Directory.Build.targets`) checks every direct `ProjectReference` before compile; `00.Governance`'s `DependencyGraphRulesTests` checks the same graph from the test side (no cycles, every production project tiered, no Host/Testing leaks).

| Tier | May reference (ProjectReference) | Third-party rule | Consumed by (in a service) |
| --- | --- | --- | --- |
| **Foundation** | Foundation | any | every project |
| **Model** | Foundation, Model | `Microsoft.Extensions.*.Abstractions` only | Domain project |
| **Abstractions** | Foundation, Model, Abstractions | `Microsoft.Extensions.*.Abstractions` only | Application project |
| **Adapter** | Foundation, Model, Abstractions, plus adapters named in its `<SharedKernelAllowedAdapterReferences>` | any except ASP.NET Core | Infrastructure project |
| **Host** | everything except Testing and Tooling | any | Api/Worker project |
| **Testing** | everything except Tooling | any | test projects only |
| **Tooling** | nothing | any | build / analyzers |

**Build errors:** `SKTIER000` unknown tier · `SKTIER001` a reference this tier may not take · `SKTIER002` an undeclared Adapter → Adapter edge · `SKTIER003` a Model/Abstractions package taking a runtime NuGet package outside the allow-list · `SKTIER004` a reference to an untiered project · `SKTIER005` a packable project without a tier · `SKTIER006` ASP.NET Core (`Microsoft.AspNetCore.App` or `Microsoft.AspNetCore.*`, transitively too) below Host/Testing. CI's `tier-check` also builds two probe projects that must fail with SKTIER001/006 (`eng/verify-tier-errors.sh`). Test projects, consumer-verify harnesses and samples declare no tier — they are consumers.

**Declared adapter edges** (a provider built on its own base):
- Storage: Obs → S3
- Persistence: EfCore, Dapper → Npgsql · EfCore.Auditing, EfCore.Encryption → EfCore
- Caching: Redis, Redis.DistributedLocking, Redis.HashStore, Redis.PubSub → Redis.Core
- Messaging: MassTransit.RabbitMq, .AzureServiceBus, .EfCore → Messaging.MassTransit
- Communication: Rest, Grpc → Communication
- Idempotency: EfCore → Persistence.EfCore · Redis → Caching.Redis.Core

A new edge is a csproj declaration reviewed like any API change; sibling role packages never reference each other.

**Purity rules the tiers cannot express** (architecture tests in `00.Governance`):
- `SharedKernel.Contracts` and `SharedKernel.Domain` never reference each other.
- Model packages (`Domain`, `Contracts`) never reference logging.
- MediatR is referenced only by `SharedKernel.Application.Mediator.MediatR`.
- `Presentation.Grpc` and `Communication.Grpc` never reference `SharedKernel.Contracts` — protobuf messages are the gRPC wire contract.
- `SharedKernel.Messaging.*` and `SharedKernel.Caching.*` never reference each other (Redis Pub/Sub is caching, not messaging).
- Redis role packages reference only `Redis.Core`; MassTransit satellites only the MassTransit core.
- Testing-tier packages are never referenced by production code, including the packable ones.
- Plus the topology, cryptography-isolation, persistence, pipeline-order and health-tag rules in `tools/Governance/CLAUDE.md`.

**What a consuming service references** (enforced for `samples/OrderApi` by its `ArchitectureTests`): **Domain** → `SharedKernel.Domain`; **Application** → `SharedKernel.Application` (+ `Idempotency.Abstractions`/`Caching.Abstractions` when it uses those markers); **Infrastructure** → adapters; **Api/Worker** → Host packages plus its own Application and Infrastructure projects.

---

## Conventions

### Logging (enforced by `00.Governance`)
- Only the `[LoggerMessage]` source-generated partial-method pattern. No `ILogger.LogXxx(...)` calls, no `LoggerMessage.Define<>()`.
- Every `[LoggerMessage]` sets an explicit `EventId` from its domain's block: `{domain number} * 1000` to `+999` (registry: `LoggingEventIdRanges` in `SharedKernel.Primitives`). A multi-package domain splits its block into 100-wide sub-blocks, recorded in its `CLAUDE.md`.
- Placeholders are PascalCase named properties (`{RequestName}`), never positional or interpolated.
- Never pass CorrelationId, TraceId/SpanId or TenantId as placeholders — they flow ambiently from the request context through OpenTelemetry.
- Structured payloads that may carry sensitive fields expose their own loggable fields (`ILoggableRequest<TResponse>`); never `{@Object}` destructuring or reflection walks. Personal data is marked with `SharedKernel.DataPrivacy` attributes and redacted.
- `SharedKernel.Domain` and `SharedKernel.Contracts` stay logging-free.

### Magic strings / named constants (`SK0022`)
No raw literal at a call site for an identifier used from more than one place or on the wire (header names, baggage/tag keys, configuration keys, claim types, cache-key parts). Use a named constant:
- **Package-local** — a small constants class in the package (`SecurityClaimTypes`, `WebhookSignatureHeaders`, `HubGroupNaming`, `CacheKeyFormat`).
- **Cross-package wire names** — `WellKnownHeaders`, `WellKnownBaggageKeys`, `WellKnownTagKeys` in `SharedKernel.Primitives`, mapped by every adapter through `SharedKernel.Execution`'s `RequestContextPropagation`. Never in `SharedKernel.Contracts`.
- **Configuration sections** — declared on the options type via `ISectionBoundOptions` (`public static string SectionName => "..."`), registered with `AddValidatedOptions<TOptions>(configuration)`.

### Package naming
```
SharedKernel.{Capability}                 main package (interfaces + default impl if single-provider)
SharedKernel.{Capability}.Abstractions    interfaces only, Abstractions tier
SharedKernel.{Capability}.{Provider}      a technology implementation, Adapter tier
SharedKernel.{Capability}.Testing         fakes, Testing tier, in the capability folder (rules: 16.Testing)
SharedKernel.{Capability}.Tests           test project, nested in the project it tests
```
- More than one provider → split into `.Abstractions` + `.{Provider}`.
- **Role split:** one technology serving several roles → `.{Provider}.Core` (connection, health, resilience) + `.{Provider}.{Role}` packages depending only on `.Core` and `.Abstractions` (e.g. `Caching.Redis.*`).
- **Satellite:** an optional feature with a heavy dependency extends the core's builder from its own package, keeping the core's namespaces (e.g. `Messaging.MassTransit.RabbitMq`).
- **MAX_PATH:** every tracked path must stay within 250 characters when the repo is cloned at `C:\Github\platform-shared-kernel\` (`eng/verify-path-lengths.sh`, run by CI). Build output does not count: it goes to `artifacts/{bin,obj}/{project}/`, so its length depends on the project name only. Check every new or renamed package before scaffolding.

### Versioning and release
One version for every package, derived from a git tag by MinVer — no `<Version>` in any `.csproj` (CI fails on one). A `vX.Y.Z` tag on `main` runs every gate, packs all packages and publishes them together to GitHub Packages. Consumers pin one `SharedKernelVersion`. Details: [`CONTRIBUTING.md`](CONTRIBUTING.md).

### Test projects
- Nested inside the project folder they test (e.g. `src/Infrastructure/Persistence/SharedKernel.Persistence.EfCore/SharedKernel.Persistence.EfCore.Tests/`).
- Reference `SharedKernel.Testing`, the capability's `SharedKernel.{Capability}.Testing`, and `SharedKernel.Testing.Internal` for Testcontainers fixtures.
- `classlib`, `net10.0`, in exactly one lane: `Platform.SharedKernel.Unit.slnf` (no Docker) or `Platform.SharedKernel.Integration.slnf` (Testcontainers).

### Solution
- `Platform.SharedKernel.slnx`; each capability folder (for example `src/Infrastructure/Caching`) is a solution folder of the same path. `eng/verify-solution-filters.sh` keeps every test project in exactly one lane filter.
- `net10.0` everywhere; Central Package Management in `Directory.Packages.props`; build-wide settings in `Directory.Build.props`/`.targets`, `global.json`, `NuGet.Config` (see [`eng/README.md`](eng/README.md)).
- A new project goes into the `.slnx`, the right `.slnf` and, if packable, `Directory.Packages.props`; then run `dotnet run eng/generate-package-index.cs` (CI fails on stale generated views). Per-tier IDE views: `eng/solution-filters/Platform.SharedKernel.{Tier}.slnf`.

---

## What Goes Where

Routing only — the domain `CLAUDE.md` holds the rules and details.

| I need… | Package / entry point | Domain |
| --- | --- | --- |
| **Execution & context** | | |
| The caller (tenant, actor, correlation id, permissions) | Inject `IRequestContext` (or `IRequestContextAccessor.Current` in a singleton), `SharedKernel.Execution`. `TenantId` is `TenantId?`; `null` fails closed | 01 |
| A tenant value in any API | `TenantId`; tenant-wide-or-global operations take `TenantScope` (`Global`, `For`, `FromNullable`). Never `string`/`Guid` | 01 |
| A caller with no inbound request (job, startup task, test) | `RequestContextScope.Begin(new SystemRequestContext(permissions, "job-name", tenantId))` | 01 |
| Supplying the request context in an HTTP host | `AddSharedKernelRequestContext()` + `app.UseSharedKernelRequestContext()` as the first middleware (`ServiceDefaults.Security`) | 13 |
| Propagating context on outbound calls | Automatic in Communication, MassTransit, Temporal, Webhooks via `RequestContextPropagation` | 01 |
| Tenant resolution for HTTP | `AddSharedKernelMultiTenancy()` + `TenantResolutionMiddleware` (Claim → Header → Database); `ITenantCatalog` | 13 |
| **Core primitives** | | |
| `Result<T>`, `Error`, a new primitive | `SharedKernel.Primitives`; `Error.Forbidden` (403) vs `Error.Unauthorized` (401) | 01 |
| Wrapping throwing calls / combining results | `ResultTry.Try`/`TryAsync`, `ResultCombine.Combine` (`SharedKernel.Core`) | 01 |
| Guard clauses | `Guard.Against` (`SharedKernel.Guards`) | 01 |
| Time-ordered ids | `IIdGenerator`/`UuidV7IdGenerator` | 01 |
| An options type | `AddValidatedOptions<TOptions>(configuration)` + `ISectionBoundOptions`; never `services.Configure` | 01 |
| A feature flag | `FeatureFlag.Boolean/…(key, default)` over OpenFeature `IFeatureClient`; `AddSharedKernelFeatureManagement` (SK0002 bans `IFeatureManager`) | 01 |
| Hashing, encryption, signing, HMAC, TOTP, secure random | `SharedKernel.Cryptography` — `AddSharedKernelCryptography(configuration)` + opt-ins; Key Vault in `Cryptography.KeyVault.Azure`, Argon2 in `Cryptography.Argon2` | 01 |
| Payload compression | `IPayloadCompressor` (`SharedKernel.Compression`); compress-then-encrypt | 01 |
| IBAN, BIC, card, VAT, national id… | `SharedKernel.Validation` (`Iban.Create` → `Result<Iban>`); FluentValidation rules in `Validation.FluentValidation` | 01 |
| Personal-data marking, redaction, GDPR requests | `SharedKernel.DataPrivacy` attributes, `IDataSubjectRequestHandler` | 01 |
| Localized error messages | `LocalizedMessage.Define<…>`, `AddLocalizationCatalog` (`SharedKernel.Localization`) | 01 |
| Object mapping | Hand-written or Mapperly; AutoMapper/Mapster runtime banned (SK0033) | — |
| **Domain & contracts** | | |
| Entity, aggregate, value object, domain event, specification | `SharedKernel.Domain`; tenant-scoped → `Tenanted…AggregateRoot<TId>` bases | 03 |
| Money and currencies | `Money` (`SharedKernel.Domain.Monetary`); `IExchangeRateProvider` is the service's port | 03 |
| An integration event | In the publishing service: `sealed record` : `IIntegrationEvent` with `[IntegrationEvent("{context}.{name}", Version = n)]` | 04 |
| Event envelope / event wire name | `EventEnvelope.Wrap(...)` (only path); `IntegrationEventDescriptor.For<T>().Name` | 04 |
| Paging DTOs | `PageRequest`/`CursorPageRequest`, `PagedList<T>`, `CursorPagedList<T>`, `PageCursor` | 04 |
| **Application pipeline** | | |
| A command/query and its handler | `ICommand`/`ICommand<T>`/`IQuery<T>`/`IStreamQuery<T>` (`SharedKernel.Application`, no MediatR) | 05 |
| Sending one | Inject `ISender` | 05 |
| Registering handlers and the pipeline | `AddSharedKernelApplication(assemblies, app => app.UseMediatR().WithIdempotency().WithTransactions().WithAuditing())` — once per host | 05 |
| A custom pipeline behavior | Kernel `IPipelineBehavior<,>` + `app.WithBehavior(typeof(B<,>), PipelineStage.X, …)` | 05 |
| Validation | `IRequestValidator<T>`, or FluentValidation via `AddFluentValidationRequestValidators(assembly)`; always on | 05 |
| Domain-event handlers | `IDomainEventHandler<TEvent>` | 05 |
| Permission gate on a use case | `[RequirePermission("x")]` — always enforced on every path | 05 |
| Duplicate-submission protection | `IIdempotentRequest` + `app.WithIdempotency()` over a `Request`-purpose `IIdempotencyStore` | 05, 18 |
| Query caching / post-commit eviction | `ICacheableQuery<T>`, `IInvalidatesCache` + `app.WithCaching()` (`Application.Pipeline.Caching`) | 05 |
| **Persistence (PostgreSQL)** | | |
| EF Core registration | `builder.AddSharedKernelPostgres<TContext>("name", p => p.UseMultiTenancy(rowLevelSecurity: true).UseAuditTrail().UseFieldEncryption().MigrateOnStartup())` | 06 |
| A DbContext | Extend `SharedKernelDbContext` or `TenantedDbContext` | 06 |
| Repositories, specs, paging | Open-generic `IRepository<T,TId>`/`IReadRepository<T,TId>`; `ListPagedAsync`, `ListKeysetAsync`, `StreamAsync` | 06 |
| One transaction | `IUnitOfWork.ExecuteInTransactionAsync` (retry-safe delegate) or `app.WithTransactions()` | 01, 06 |
| Optimistic concurrency | `EntityVersion` (`xmin`), `ConcurrencyVersion.Get` | 06 |
| Bulk update/delete | `IBulkMutationRepository<T,TId>` | 06 |
| Cross-tenant work | `ICrossTenantScope.Enter("reason")` | 06 |
| Hand-written SQL | `IDbSessionFactory` (`Persistence.Dapper`) | 06 |
| Connections, TLS, roles | `SharedKernel.Persistence.Npgsql` (role script in its README) | 06 |
| DbContext outside a request | `ICallerDbContextFactory<TContext>` | 06 |
| Audit trail / field encryption | `UseAuditTrail()` + `IAuditableRequest` (`EfCore.Auditing`); `UseFieldEncryption()` (`EfCore.Encryption`) | 06 |
| **Messaging** | | |
| Publishing / sending | `IMessageBus`, `IEventPublisher` (`Messaging.Abstractions`); every verb returns `Result` | 07 |
| MassTransit setup | `AddSharedKernelMessaging(configuration).UseRabbitMq(…)` / `.UseAzureServiceBus(…)` `…Build()`; outbox `WithEntityFrameworkOutbox<TDbContext>` | 07 |
| Consumers, dedup, caller context | `ConsumerBase<T>`, `WithIdempotency()`, `WithInboundRequestContext()` | 07 |
| Multi-step process with compensation | `17.Workflows` — no sagas in messaging; no request/response over the bus | 17 |
| **Caching** | | |
| Cache contracts | `ICacheService`, `ITenantCacheService`, `CachePolicy` (`Caching.Abstractions`) | 02 |
| Cache implementation / L2 | `Caching.FusionCache`; `AddRedisL2()` (`Caching.Redis`) | 02 |
| Redis connection | `AddRedisConnection(configuration)` once (`Caching.Redis.Core`) | 02 |
| Distributed lock / lease | `IDistributedLockService` (`Caching.Redis.DistributedLocking`) | 02 |
| **Idempotency** | | |
| A production store | `AddRedisIdempotency(p => p.ForRequests().ForMessages())` or `AddEfCoreIdempotency(…)` | 18 |
| **Storage, search, AI** | | |
| Files | `AddSharedKernelStorage().AddS3(configuration).AddStore("name")`, `[FromKeyedServices] IFileStorage`; tenant files `ITenantFileStorage.ForTenant` | 08 |
| Full-text search | `ISearchIndex<T>`, `ISearchIndexProvisioner` (`Search.Abstractions`); engine-only features in `Search.Meilisearch`/`.ElasticSearch` | 09 |
| Embeddings, vectors, LLMs | `AI.Abstractions`; `AI.Qdrant`, `AI.SemanticKernel` (raw clients banned, SK0026) | 10 |
| **Communication (outbound)** | | |
| Typed REST client | `AddSharedKernelCommunication(configuration).AddRestClient<IClient, Client>("name")`; `GetResultAsync`/`PostResultAsync` → `Result<T>` | 11 |
| gRPC client | `.AddGrpcClient<T>("name")`, `call.ToResultAsync()` | 11 |
| **Security** | | |
| Authentication | `AddOidcAuthentication` (`Security.Oidc`), `AddManagedApiKeyAuthentication` (`.ApiKey`), `AddMtlsAuthentication` (`.Mtls`), step-up in `.Totp` | 12 |
| The authenticated principal | `IUserContext` (`Security.Abstractions`) — application code reads `IRequestContext` instead | 12 |
| **Host & presentation** | | |
| OTel, health, readiness, rate limiting | `AddServiceDefaults`, `MapDefaultHealthCheckEndpoints()`, `AddHealthChecks().AddSharedKernelReadiness()`, `AddSharedKernelRateLimiting()` | 13 |
| A readiness check for my dependency | Implement `IReadinessProbe` + `AddReadinessProbe<T>()` | 01, 13 |
| A host integration needing another kernel package | A `SharedKernel.ServiceDefaults.{Capability}` package — never the base | 13 |
| HTTP API setup, ProblemDetails, typed results | `builder.AddSharedKernelWebApi()` + `app.UseSharedKernelWebApi(…)`; `ToOk`/`ToCreated`/`ToOkWithETag` | 14 |
| An HTTP endpoint | `IEndpointModule` mapped by the generated `app.MapEndpoints()`; sends through `ISender` | 14 |
| Endpoint authorization (no command sent) | `[RequireEndpointPermission]`, `[RequireRole]`, `[RequireFreshAuthentication]` (`Presentation.Core`) | 14 |
| OpenAPI / versioning | `AddSharedKernelOpenApi()` + `MapSharedKernelOpenApi()` | 14 |
| gRPC server / SignalR / GraphQL | `AddSharedKernelGrpc()`, `AddSharedKernelSignalR()`, `AddSharedKernelGraphQL()` | 14 |
| **Integration, workflows, jobs, reports** | | |
| Webhooks | `SharedKernel.Integration.Webhooks` | 15 |
| Email / SMS | `INotificationSender` keyed by channel; SendGrid, Twilio | 15 |
| Durable long-running process | `IWorkflowDispatcher`, `WorkflowBase`/`ActivityBase`, `CommandActivity<T>` (`Workflows.Temporal`) | 17 |
| Recurring/one-shot job | `IScheduledJobRegistry` + `ScheduledCommandJob<TCommand>` | 19 |
| Streaming export / HTML → PDF | `AddSharedKernelReporting().AddCsv(c)…`, `IReportExporter<TRow>`, `IReportExporterFactory`; `IHtmlToPdfConverter` via `AddGotenberg` | 20 |
| **Governance & testing** | | |
| An architecture rule | `SharedKernel.ArchitectureTests` — only what tiers cannot express; every rule needs a test | 00 |
| A Roslyn analyzer | `SharedKernel.Analyzers` | 00 |
| Fakes for a service's unit tests | `SharedKernel.{Capability}.Testing`; caller: `TestRequestContext`; pipeline: `ApplicationPipelineTestHarness` | 16 |
| Testcontainers fixtures (this repo) | `SharedKernel.Testing.Internal` | 16 |
| Wiring a new service | `samples/README.md`, `samples/OrderApi` | — |

---

## Abstractions Packages (Interface Contracts)

What a service's Application and Domain projects depend on — never the concrete provider:

| Contract package | Implemented by |
| --- | --- |
| `SharedKernel.Execution` (`IRequestContext`, `IRequestContextAccessor`, `IUnitOfWork`, `IAuditTrailWriter`) — Foundation | `ServiceDefaults.Security` (`IRequestContext` over `IUserContext`), inbound adapters (scopes), `Persistence.EfCore` (`IUnitOfWork`), `Persistence.EfCore.Auditing` (`IAuditTrailWriter`) |
| `SharedKernel.Primitives.Health` (`IReadinessProbe`) — Foundation | every provider with an external dependency; mapped by `AddSharedKernelReadiness()` |
| `SharedKernel.Application` (`ISender`, `IPipelineBehavior<,>`, markers) | `Application.Mediator.MediatR` (`ISender`), `Application.Pipeline` (+ `.Caching`) |
| `SharedKernel.Idempotency.Abstractions` | `Idempotency.Redis`, `Idempotency.EfCore` |
| `SharedKernel.Caching.Abstractions` | `Caching.FusionCache` (+ `.Redis` L2), `Caching.Redis.DistributedLocking` |
| `SharedKernel.Persistence.Abstractions` | `Persistence.EfCore` (+ `.Auditing`, `.Encryption`), `Persistence.Npgsql`, `Persistence.Dapper` |
| `SharedKernel.Messaging.Abstractions` | `Messaging.MassTransit` (+ `.RabbitMq`, `.AzureServiceBus`, `.EfCore`) |
| `SharedKernel.Storage.Abstractions` | `Storage.S3`, `Storage.Obs` |
| `SharedKernel.Search.Abstractions` | `Search.Meilisearch`, `Search.ElasticSearch` |
| `SharedKernel.AI.Abstractions` | `AI.Qdrant`, `AI.SemanticKernel` |
| `SharedKernel.Security.Abstractions` | `Security.Oidc`, `.ApiKey`, `.Mtls`, `.Totp` |
| `SharedKernel.Integration.Notifications.Abstractions` | `Notifications.Email.SendGrid`, `Notifications.Sms.Twilio` |
| `SharedKernel.Reporting.Abstractions` | `Reporting.Csv`, `.Spreadsheet`, `.Pdf` (`IReportExporter<TRow>`), `Reporting.Gotenberg` (`IHtmlToPdfConverter`) |

---

## Working in This Repo with Claude Code

Work flows from intent → work order → domain phase → code, with `state-map.md` as the hand-off point.

| Skill | What it does |
| --- | --- |
| `/arch <request>` | `arch-lead` evaluates the request against the architecture and writes a work order (WO + P-entries) into the root `state-map.md`. Plans; never writes code. |
| `/dispatch-phase` | Fans a work order out to the affected domains' `{domain}-arch-planner` agents, which author phases in their `state-map.md` and refresh their `CLAUDE.md`. |
| `/implement-phase <domain> [phase]` | Runs `{domain}-phase-implementer` on a phase: code, tests, state-map update. |
| `/implement-next-phase` | Picks the next ready phase across domains and implements it. |
| `/state-map-phase` | Records a phase's outcome on its domain's board. |
| `/sync-brain` | Brings the `CLAUDE.md` files back in line with the code. |
| `/commit` | Reviews the working tree and writes a conventional commit. |
| `/devops <request>` | `devops-lead`: CI, packaging, versioning, containers, build configuration. |

- Skills live in `.claude/skills/{name}/SKILL.md`; the side-effecting ones (`/commit`, `/dispatch-phase`, `/implement-*`, `/state-map-phase`, `/sync-brain`) run only when you invoke them.
- Agents: `arch-lead`, `devops-lead`, and one `{slug}-arch-planner` + `{slug}-phase-implementer` pair per domain (in `.claude/agents/`), the slug taken from the domain table above.
- Rules shared by every agent live in `.claude/agents/_common.md`.
- Agent memory under `.claude/agent-memory/` is local to each developer and gitignored.
