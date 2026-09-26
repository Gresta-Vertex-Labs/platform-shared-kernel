# Migrating to the tiered foundation (WO-086)

Code written against the kernel before WO-086 (P-562–P-575) needs the changes below. §8 adds what P-579 changed when it merged
`main`'s presentation redesign ("P-562 (presentation, main)") and application model ("P-563 (application model, merged from
main in P-579)") into this branch; where a row of §1–§3 and §8 disagree, §8 wins. Nothing was in production
use, so no compatibility shims were kept: every old name is gone, not obsolete. Derived from
`git diff 053e5612~1..e3387513` (P-564–P-574), the commit messages and the `PublicAPI.*.txt` diffs.

**Upgrade order that compiles soonest:** (1) package references (§1), (2) `using` directives (§2), (3) the
host's `Program.cs` registrations (§3), (4) tenant types (§4), (5) test projects (§6), (6) stored data (§5).

---

## 1. Packages

| Old package | New package(s) | Notes |
|---|---|---|
| `SharedKernel.Application.Abstractions` | `SharedKernel.Execution` | Deleted. Every type moved (§2). |
| `SharedKernel.Application.Behaviors` | `SharedKernel.Application.Pipeline` | Renamed; Host tier. Markers moved to `SharedKernel.Application` (§2). No FluentValidation dependency any more. |
| `SharedKernel.Application.Behaviors.Caching` | `SharedKernel.Application.Pipeline.Caching` | Renamed; `ICacheableQuery`/`IInvalidatesCache`/`CacheScope`/`CacheKeyRef` moved to `SharedKernel.Application`. |
| `SharedKernel.Application` (MediatR-based) | `SharedKernel.Application` (no MediatR) + `SharedKernel.Application.Mediator.MediatR` | The contracts package no longer references MediatR. A host that dispatches through MediatR adds the adapter package. |
| `SharedKernel.Communication.GraphQL` | `SharedKernel.Presentation.GraphQL` | Moved to `14.Presentation` (server-side GraphQL is presentation). |
| `SharedKernel.Messaging.MassTransit` (RabbitMQ, Azure Service Bus and the EF outbox inside) | `SharedKernel.Messaging.MassTransit` + `.RabbitMq` / `.AzureServiceBus` / `.EfCore` | Add the satellite for the transport/outbox you use. The fluent chain and namespaces are unchanged. |
| `SharedKernel.Presentation.WebApi` (authorization attributes and policies, error presentation, `ErrorTypeStatusCodeMap`) | + `SharedKernel.Presentation.Core` (referenced transitively) | What WebApi and Grpc share moved to Core (§8). |
| `SharedKernel.Presentation.Grpc` (referenced WebApi) | `SharedKernel.Presentation.Grpc` + `SharedKernel.Presentation.Core` | Grpc no longer pulls WebApi. |
| `SharedKernel.Presentation.SignalR` (Redis backplane inside) | `SharedKernel.Presentation.SignalR` | The backplane was removed (main's P-562 D15: unused, duplicated the framework); the short-lived WO-086 satellite `SharedKernel.Presentation.SignalR.Redis` is gone too. Use Microsoft's `AddSignalR().AddStackExchangeRedis(...)` directly. |
| — | `SharedKernel.Presentation.OpenApi` | New (main): API versioning, one OpenAPI document per version, Scalar (§8). |
| `SharedKernel.ServiceDefaults.AI`, `.Caching`, `.Caching.Redis`, `.Messaging`, `.Scheduling`, `.Search`, `.Storage`, `.Workflows.Temporal`, `.Cryptography.KeyVault` | none — `SharedKernel.ServiceDefaults`' `AddSharedKernelReadiness()` | Deleted; each provider registers its own `IReadinessProbe` (§3). |
| `SharedKernel.Testing` (one non-packable library) | `SharedKernel.Testing` (packable core) + 19 `SharedKernel.{Capability}.Testing` packages + `SharedKernel.Testing.Internal` (not packable) | §6. Namespaces are unchanged; only the package that holds them changed. |
| — | `SharedKernel.Idempotency.Abstractions` | New: the one idempotency store contract (§2). |
| — | `SharedKernel.Execution` | New Foundation package (§2). |

Consumers pin every kernel package to one `SharedKernelVersion` property (see `PLATFORM.md` → "Consuming the kernel").
Old package IDs stay on GitHub Packages at their last alpha version until P-578 retires them.

## 2. Types and namespaces

### Execution context, unit of work, audit trail (`SharedKernel.Application.Abstractions` → `SharedKernel.Execution`)

| Old | New |
|---|---|
| `SharedKernel.Application.Context.IRequestContext` | `SharedKernel.Execution.Context.IRequestContext` — `TenantId` is now `TenantId?`; gained `CorrelationId` |
| `SharedKernel.Application.Context.ActorKind` / `SystemRequestContext` / `AnonymousRequestContext` | `SharedKernel.Execution.Context.*` — `SystemRequestContext(IEnumerable<string> permissions, string identity = "system", Guid? tenantId = null)` |
| `SharedKernel.Application.Transactions.IUnitOfWork` / `CommitOutcomeUnknownException` / `TransactionRolledBackException` | `SharedKernel.Execution.Transactions.*` |
| `SharedKernel.Application.Auditing.IAuditTrailWriter` / `AuditEntry` / `AuditOutcome` | `SharedKernel.Execution.Auditing.*` |
| — | `SharedKernel.Execution.Context.IRequestContextAccessor` / `RequestContextAccessor`, `RequestContextScope.Begin(ctx)`, `PropagatedRequestContext`, `RequestContextPropagation`, `CorrelationIds` |
| — | `SharedKernel.Execution.Tenancy.TenantId` (record struct over a non-empty `Guid`), `TenantScope` |

### Tenant and caller identity

| Old | New |
|---|---|
| `SharedKernel.Security.Abstractions.ITenantProvider` (`TenantId`) | `IRequestContext.TenantId` (inject `IRequestContext`, or `IRequestContextAccessor.Current` in a singleton) |
| `UserContextTenantProvider`, `AmbientTenantProvider` | deleted — `AddSharedKernelRequestContext()` builds `IRequestContext` from `IUserContext` |
| `SharedKernel.Security.Abstractions.IdentityKind` / `IUserContext.IdentityKind` | `ActorKind` / `IUserContext.ActorKind` (`User`, `Service` (was `ServicePrincipal`), `System`, `Anonymous`) |
| `IUserContext.TenantId` (`Guid?`) | `TenantId?` |
| `UserContext(IdentityKind identityKind, …)` | `UserContext(ActorKind actorKind, …)`. `ActorKind` values are `User` = 0, `Service`, `System`, `Anonymous` = 3, so `default(ActorKind)` is `User` |
| `ApiKeyRecord.TenantId`, `ApiKeyValidationResult.TenantId`/`.Success(tenantId)`, `MtlsValidationResult.TenantId`/`.Success(tenantId)` (`Guid?`) | `TenantId?` (reject `default(TenantId)` instead of `Guid.Empty`) |
| `SharedKernel.Messaging.Abstractions.TenantContext.ITenantContextAccessor` | deleted — the tenant comes from `IRequestContextAccessor` |
| `MessagingBusBuilder.WithTenantContext<TAccessor>()` | `WithTenantContext()` |
| `SharedKernel.Messaging.Abstractions.Context.MessageRequestContext` | `SharedKernel.Execution.Context.PropagatedRequestContext` |
| `SharedKernel.Messaging.Abstractions.Context.MessageContextHeaders` | `SharedKernel.Primitives.Propagation.WellKnownHeaders` (same `x-sk-*` values) |
| `TenantBaggageKeys` (MultiTenancy) | `WellKnownBaggageKeys` |
| `SharedKernel.Search.Abstractions.Models.TenantScope`, `SharedKernel.AI.Abstractions.Models.TenantScope` (`Value`, `None`, `Of(string)`), `SharedKernel.Workflows.Temporal.Dispatch.TenantScope` and the Scheduling copy | `SharedKernel.Execution.Tenancy.TenantScope` (`Tenant`, `IsGlobal`, `Global` (the default), `For(TenantId)`, `FromNullable(TenantId?)`) |
| `ReportDestination.TenantId` (`string?`) | `TenantId?` (`default` rejected) |
| `IHasTenant.TenantId` / `Tenanted*` bases (`Guid`) | `TenantId` |
| `ITenantCacheService` / `CachePolicy.ForTenant` / `RemoveTenantAsync` (`string` tenant) | `TenantId` |
| `ITenantFileStorage.ForTenant(string)` | `ForTenant(TenantId)` |
| `FeatureTargetingContext(userId, string? tenantId, …)` / `ForTenant(string)` | `TenantId?` / `ForTenant(TenantId)` |
| MultiTenancy `ITenantResolutionStrategy.TryResolveAsync` → `Guid?`; `ITenantCatalog.GetByIdAsync(Guid)`; `ITenantStatusValidator.IsActiveAsync(Guid)` | `TenantId?` / `TenantId` |
| Persistence: `TenantedDbContext.CurrentTenantId`, `IDbSession.TenantId`/`RequireTenantId()`, `ITenantSessionBinder.BindAsync`, `TenantedRepository.GetByIdForTenant*Async`, `WhereEncryptedEquals(…, tenantId)`, `ITenantEncryptionKeyManager.*`, `AuditRecord.TenantId`, audit checkpoint APIs (`Guid`/`Guid?`) | `TenantId` / `TenantId?` (EF value converter and Dapper type handler are registered automatically) |

`EventEnvelope.TenantId` (`SharedKernel.Contracts`) stays `Guid?` — it is a wire contract.

### Mediator and pipeline (`SharedKernel.Application`, `SharedKernel.Application.Pipeline`)

| Old | New |
|---|---|
| `MediatR.IRequest<T>`, `IRequestHandler<,>`, `ISender`, `IPipelineBehavior<,>`, `RequestHandlerDelegate<T>` in kernel contracts | kernel-owned `SharedKernel.Application.Messaging` `IRequest<T>`, `IRequestHandler<,>`, `ISender`, `IPipelineBehavior<,>`, `RequestHandlerContinuation<T>` (`next()` call sites unchanged) |
| MediatR `IStreamRequest<T>` and stream behaviors | `SharedKernel.Application.Streaming` `IStreamQuery<T>`, `IStreamQueryHandler<,>`, `IStreamPipelineBehavior<,>` |
| FluentValidation `IValidator<T>` consumed by `ValidationBehavior` | `SharedKernel.Application.Validation.IRequestValidator<T>`; bridge existing validators with `AddFluentValidationRequestValidators()` |
| `INotificationHandler<DomainEventNotification<T>>`, `DomainEventNotification<T>`, `MediatRDomainEventDispatcher` | `IDomainEventHandler<T>` + the native dispatcher (internal); `AddDomainEventHandler` lives in `SharedKernel.Application.Pipeline` (§8) |
| `SharedKernel.Application.Behaviors.Authorization.IAuthorizeRequest` / `PermissionMatch` | deleted — `[RequirePermission]` (`SharedKernel.Application.Authorization.RequirePermissionAttribute`, §8) |
| `SharedKernel.Application.Behaviors.Idempotency.IIdempotentRequest` | `SharedKernel.Application.Idempotency.IIdempotentRequest` |
| `SharedKernel.Application.Behaviors.Auditing.IAuditableRequest<T>` | `SharedKernel.Application.Auditing.IAuditableRequest<T>` |
| `SharedKernel.Application.Behaviors.Logging.ILoggableRequest<T>` | `SharedKernel.Application.Logging.ILoggableRequest<T>` |
| `SharedKernel.Application.Behaviors.Commands.ICommandScope` | `SharedKernel.Application.Commands.ICommandScope` |
| `SharedKernel.Application.Behaviors.Caching.ICacheableQuery(<T>)` / `CacheScope`; `…CacheInvalidation.IInvalidatesCache` / `CacheKeyRef` | `SharedKernel.Application.Caching.*` |
| `SharedKernel.Application.Behaviors.{Auditing,Authorization,Idempotency,Logging,Tracing,Transaction,Validation}.*Behavior` | internal in `SharedKernel.Application.Pipeline` (§8) |
| `SharedKernel.Application.Behaviors.Extensions.ApplicationBehaviorsBuilder` / `ApplicationBehaviorsServiceCollectionExtensions` | deleted — `AddSharedKernelApplication(assemblies, app => …)` / `ApplicationPipelineBuilder` (§8) |
| `SharedKernel.Application.Behaviors.Extensions.PipelineStage` | `SharedKernel.Application.Pipeline.PipelineStage` |
| `SharedKernel.Application.Behaviors.Caching.Extensions.CachingBehaviorsExtensions` (`AddCachingBehaviors()`) | `SharedKernel.Application.Pipeline.Caching.CachingPipelineExtensions` (`app.WithCaching()`, §8) |
| `ApplicationLoggingOptions` | `SharedKernel.Application.Pipeline.ApplicationLoggingOptions` |
| — | `RequestPipeline<,>` / `StreamRequestPipeline<,>` (registered by `AddSharedKernelApplication`) |

### Idempotency (`SharedKernel.Idempotency.Abstractions`)

| Old | New |
|---|---|
| `SharedKernel.Application.Behaviors.Idempotency.IRequestIdempotencyStore`, `IdempotencyBeginResult`, `IdempotencyBeginStatus` | `IIdempotencyStore` registered for `IdempotencyPurpose.Request`; `IdempotencyReservation` / `IdempotencyReservationStatus` (`Started`, `InProgress`, `Completed`, `FingerprintMismatch`) |
| `SharedKernel.Messaging.Abstractions.Idempotency.IIdempotencyStore` / `IdempotencyReservation` / `IdempotencyReservationStatus` | `SharedKernel.Idempotency.Abstractions.*`, registered for `IdempotencyPurpose.Message` |
| `IdempotencyTenantAccessorStartupValidator` (required an `ITenantContextAccessor`) | deleted; the tenant is `IRequestContextAccessor.Current?.TenantId`, encoded by `IdempotencyTenantScope` (`"no-tenant"` when none) |
| Lease/retention on the store (store options `InFlightTtl`/`RetentionWindow`) | the caller: `IdempotencyBehaviorOptions.LeaseDuration` (default 30 s)/`RetentionWindow` (pipeline), `IdempotencyOptions.LeaseDuration`/`ExpiryWindow` (MassTransit; `Build()` requires `0 < LeaseDuration < ExpiryWindow`). Store options keep only `AllowExecutionOnStoreUnavailable`; configuration sections are unchanged |
| Messaging reservation status `AlreadyProcessed` | `Completed`; `FingerprintMismatch` is new |
| Store classes (one Request + one Message store per backend) | `RedisIdempotencyStore`, `EfCoreIdempotencyStore`; `HasIdempotencyStore(purpose)`/`GetRequiredIdempotencyStore(purpose)` resolve them |

### Readiness probes (`SharedKernel.Primitives.Health`)

| Old | New probe name (`IReadinessProbe`) |
|---|---|
| `IMessageBusProbe` / `MessageBusHealth` | `messaging` |
| `IRedisConnectionProbe` / `RedisConnectionHealth` | `redis` (plus `cache` from FusionCache) |
| `IEncryptionKeyProviderProbe` / `EncryptionKeyProviderHealth` | `encryption-key-provider` |
| `FieldEncryptionServiceKeys.KeyRingProbe` (keyed `IEncryptionKeyProviderProbe`) | `field-encryption` (`FieldEncryptionReadiness.ProbeName`) |
| `IAuditSealingProbe` / `AuditSealingHealth` | `audit-sealing` (`AuditSealingReadiness.*`; threshold `AuditSealerOptions.MaxReadyLag`, config `SharedKernel:Persistence:Auditing:Sealer:MaxReadyLag`, default 5 min) |
| `IFileStorageHealthProbe.ProbeAsync(storeName)` | `storage-{store}` (one per store) |
| `ISearchIndexProvisioner.ProbeAsync` | `search-{provider}-{index}` (one per index) |
| `IVectorCollectionProvisioner.ProbeAsync` | `vector-store-{provider}-{collection}` (`VectorCollectionReadinessProbe`, `*Key` data constants) |
| `IWorkflowServiceProbe` / `WorkflowServiceHealth` (incl. `TaskQueueBacklog`) | `workflows` (`WorkflowReadiness.ProbeName`; data `Reachable`, `NamespaceAddressable`, `WorkerPollersActive`) |
| `ISchedulerServiceProbe` / `SchedulerServiceHealth` | `scheduler` (`SchedulerReadiness.ProbeName`; data `IsRunning`, `RegisteredJobCount`, `LastTickUtc`) |

A service's own probe: implement `IReadinessProbe` and call `services.AddReadinessProbe<T>()`.

### Propagation, presentation, communication

| Old | New |
|---|---|
| `SharedKernel.Presentation.WebApi` `CorrelationIdMiddleware`, `CorrelationIdOptions` | `app.UseSharedKernelRequestContext()` (`SharedKernel.ServiceDefaults.Security`) — one fixed rule for accepted ids (≤128 chars, `[A-Za-z0-9-_:.]`) |
| `SharedKernel.Communication.Rest` `CorrelationIdDelegatingHandler`, `TenantIdDelegatingHandler` | `RequestContextDelegatingHandler` (reads `IRequestContextAccessor`; no `IHttpContextAccessor`) |
| Communication's internal `IdempotencyHeaders` (`x-idempotency-key`) | `WellKnownHeaders.IdempotencyKey` = `Idempotency-Key` — the header the WebApi side always read (defect fixed) |
| — | `WellKnownHeaders.ActorId` (`x-sk-actor-id`), `ActorKind` (`x-sk-actor-kind`), `ClientId` (`x-sk-client-id`) |
| `SharedKernel.Presentation.WebApi.Authorization.Require{Role,Permission,FreshAuthentication,AuthenticationMethod}Attribute` | `SharedKernel.Presentation.Authorization.*` (package `Presentation.Core`) |
| `SharedKernel.Presentation.WebApi.Errors.ErrorTypeStatusCodeMap` | `SharedKernel.Presentation.Errors.ErrorTypeStatusCodeMap` |
| `SharedKernel.Presentation.Grpc.Errors.GrpcStatusCodeMap` | `SharedKernel.Presentation.Errors.GrpcStatusCodeMap` |
| `SharedKernel.Communication.GraphQL.*` (`Types.FilterBase`/`SortBase`, `Pagination.PagedResponseType`, `Options.GraphQLOptions`, …) | `SharedKernel.Presentation.GraphQL.*` |
| Correlation from `Activity.Id`/`TraceId` on outbound calls (REST, gRPC, bus, Temporal headers) | the caller's `X-Correlation-Id` (`CorrelationIds.Current`), unchanged across every hop; Temporal headers now also carry actor and client, and activities run inside a `RequestContextScope(PropagatedRequestContext)` |
| Webhook sender-identity resolvers reading `ITenantProvider` | read `IRequestContext.TenantId`; deliveries send only `X-Correlation-Id` |
| `CommandActivity<>` / `ScheduledCommandJob<>` over MediatR `ISender` | the kernel `SharedKernel.Application.Messaging.ISender`; scheduled runs open `SystemRequestContext([], jobName, job tenant)` with `CorrelationIds.New()` |
| `HttpContext.Items["CorrelationId"]` (`CorrelationIdMiddleware.ItemsKey`), `CorrelationIdOptions.MaxLength`/`AllowedCharacterPattern` | `IRequestContext.CorrelationId`; the accepted-id rule is fixed. EventIds 14000/14006 are retired |
| `TenantContextHubFilter.ItemsKey`, `GrpcTenantContextInterceptor.ItemsKey` (`Items["TenantId"]`) | `IRequestContextAccessor.Current?.TenantId`; an unresolved tenant is `null`, not `Guid.Empty` |
| `PublishContext.TenantId` / `WithTenantId(Guid)` | `TenantId` |
| Message header propagators reading `ITenantContextAccessor`/`IHttpContextAccessor` | read `IRequestContextAccessor` |
| `RabbitMqBusOptions`, `AzureServiceBusOptions`, `OutboxOptions` in the core MassTransit package | the `.RabbitMq`, `.AzureServiceBus`, `.EfCore` satellites (same namespace `SharedKernel.Messaging.MassTransit.Options`, same config sections); new extension point `UseTransport(...)`/`ConfigureMassTransit(...)` |
| `AddSharedKernelAuthorizationFilters` / the endpoint filter | removed — the attributes are real `[Authorize]` attributes evaluated by native policies (§8) |

## 3. Registration methods

| Old | New |
|---|---|
| `services.AddMediatR(...)` + `services.AddSharedKernelApplication()` + `AddSharedKernelApplicationBehaviors()…Build()` | `services.AddSharedKernelApplication(typeof(Handler).Assembly, app => app.UseMediatR().With…())` — one call (§8) |
| `services.AddSharedKernelCorrelationId(...)` / `app.UseSharedKernelCorrelationId()` | `services.AddSharedKernelRequestContext()` / `app.UseSharedKernelRequestContext()` — **first** middleware, before `UseExceptionHandler()` |
| `AddMessagingReadinessCheck`, `AddRedisHealthCheck`, `AddCacheReadinessCheck`, `AddKeyVaultKeyProviderReadinessCheck`, `AddFieldEncryptionReadinessCheck`, `AddAuditSealingReadinessCheck`, `AddStorageReadinessCheck(store)`, `AddSearchReadinessCheck(...)`, `AddVectorStoreReadinessCheck(...)`, `AddWorkflowReadinessCheck`, `AddSchedulerReadinessCheck` | `services.AddHealthChecks().AddSharedKernelReadiness()` once. `ServiceDefaults.Persistence` keeps `AddDatabaseReadinessCheck<T>`, `AddDapperDatabaseReadinessCheck`, `AddPersistenceStartupReadinessCheck` |
| `AddSharedKernelKeyVaultKeyProvider(builder)` (ServiceDefaults.Cryptography.KeyVault) | `services.AddSharedKernelCryptography(configuration).AddAzureKeyVaultEncryption(configuration)` (`SharedKernel.Cryptography.KeyVault.Azure`), which registers the key provider and its probe |
| `AddSharedKernelRedisIdempotency(...)` | `AddRedisIdempotency(p => p.ForRequests().ForMessages(), o => …)` |
| `AddSharedKernelEfCoreIdempotency(...)` | `AddEfCoreIdempotency(db => …, p => p.ForRequests().ForMessages(), o => …)` |
| `services.AddScoped<IIdempotencyStore, MyStore>()` | `services.AddIdempotencyStore<MyStore>(IdempotencyPurpose.Message)` (per purpose) |
| FluentValidation validators picked up by `ValidationBehavior` | `services.AddFluentValidationRequestValidators(typeof(Validator).Assembly)` (registers the validators too) |
| `UseRabbitMq(...)` / `UseAzureServiceBus(...)` / `WithEntityFrameworkOutbox<T>()` from the core package | the same methods, from `Messaging.MassTransit.RabbitMq` / `.AzureServiceBus` / `.EfCore` |
| `WithTenantContext<TAccessor>()` | `WithTenantContext()` |
| `WithRedisBackplane()` from `Presentation.SignalR` | removed — Microsoft's `AddStackExchangeRedis(...)` on `ISignalRServerBuilder` |
| `SecurityTestContextBuilder.WithIdentityKind(IdentityKind.ServicePrincipal)` (testing) | `WithActorKind(ActorKind.Service)` |

Canonical HTTP order (§8): `app.UseSharedKernelRequestContext()` → `app.UseSharedKernelWebApi(...)` (security headers,
exception handler, routing, CORS, authentication, rate limiting, authorization, required headers — tenant resolution via
its `BeforeAuthorization` hook) → endpoints (`app.MapEndpoints()`).

## 4. Behaviour changes to check

- **"No tenant" is `null`, never `Guid.Empty`.** `new TenantId(Guid.Empty)` throws. Code that compared against `Guid.Empty` now checks `TenantId is null`.
- **Tenant strings are GUIDs.** Caching and storage took an arbitrary `string` tenant; they now take `TenantId`, formatted as the "D" GUID string. Cache keys (`@{tenant}`) and tenant storage prefixes (`tenants/{id}/`) written under a non-GUID tenant name are not reachable any more.
- **Consumers carry the publisher's tenant.** `WithInboundRequestContext()` opens a `RequestContextScope`, so outbound calls made from a consumer carry the tenant, actor and correlation id (defect 3 fixed).
- **The correlation id survives every hop** with its original value (defect 4 fixed); webhooks send only the correlation id.
- **Idempotency stores start without a tenant accessor** (defect 2 fixed) and key by `(tenant scope, purpose, key)`.
- **The request idempotency header is `Idempotency-Key` on both sides** (defect 1 fixed).
- **`IRequestContext` is registered transient** by `AddSharedKernelRequestContext()`: `RequestContextScope.Current` when a scope is open, else the scoped `IUserContext`-backed context.
- **The request culture's tenant default** (`ServiceDefaults.Localization`) now reads `IRequestContext.TenantId`, not ambient baggage.
- **Security.Oidc/.ApiKey/.Mtls/.Totp are Host tier** — reference them from the Api/Worker project, not Infrastructure.
- **Tier errors.** A project inside this repo that breaks the tier matrix fails the build (`SKTIER001`–`006`). Consuming services are not checked.

## 5. Persisted data

| Store | Change |
|---|---|
| `Idempotency.EfCore` | The request table keyed `(tenant_id uuid, key)` with a `…0001` sentinel for "no tenant", and the separate `idempotency_messages` table, become one `idempotency_keys` table keyed `(tenant_scope varchar(36), purpose varchar(16), key)`, with `"no-tenant"` for no tenant. Add a migration; in-flight keys are not carried over. |
| `Idempotency.Redis` | Request keys are byte-identical. Message entries changed from strings to hashes: an old message key read by the new store fails with `WRONGTYPE`, so flush the message keys (or let them expire) before rolling out. |
| Message idempotency keys (both stores, `IdempotencyPurpose.Message`) | Were the `MessageId` ("D") alone; now `{MessageId:D}:{sha256-hex("{receive-endpoint path}\|{consumer full type name}")}` (101 characters), one reservation per consumer — so a message consumed by two consumers or two endpoints of one service is no longer skipped by the second. Old message keys are never matched: a message whose only record is an old key is consumed once more if it is redelivered after the upgrade, within the old `ExpiryWindow`. Let old keys expire, or drain in-flight redeliveries, before rolling out. |
| PostgreSQL tenant columns, `app.tenant_id`, encryption key ids and associated data | Unchanged (the `TenantId` converter writes the same uuid / "D" string). |
| Audit ledger | Unchanged format (AUDITv3). |
| Cache / object storage tenant prefixes | See §4 — only affected when tenants were not GUIDs. |
| Qdrant tenant payload field | Now the `TenantId` "D" GUID string; points written under a free-form tenant string are not matched by tenant-scoped queries. |
| Temporal workflow ids | Composed ids embed the `TenantId` "D" string; ids of workflows started before the upgrade under a free-form tenant differ. |

## 6. Test projects

| Old | New |
|---|---|
| `SharedKernel.Testing` (everything, not packable) | `SharedKernel.Testing` core (`FakeClock`, logging, fakers, `TestRequestContext`, assertions) + the capability package: `SharedKernel.{Application, Caching, Caching.Redis, Communication, Cryptography, FeatureManagement, Idempotency, Integration, Messaging, Persistence, Presentation, Reporting, Scheduling, Search, AI, Security, ServiceDefaults, Storage, Workflows}.Testing`. Namespaces (`SharedKernel.Testing.Messaging`, …) are unchanged |
| Testcontainers fixtures (incl. `QdrantContainerFixture`), EF Core/Npgsql helpers, MassTransit `TestHarnessFactory` | `SharedKernel.Testing.Internal` (this repo only, not packable; namespaces unchanged) |
| `SharedKernel.Testing.SelfTests` | one `{Name}.Tests` per testing package |
| `SharedKernel.Persistence.Testing.TestRequestContext` | `SharedKernel.Testing.Execution.TestRequestContext` (core package) |
| `FakeTenantProvider`, `StaticTenantProvider` | `TestRequestContext` / `FakeUserContext.TenantId` |
| `FakeRequestIdempotencyStore` | `FakeIdempotencyStore` + `AddFakeIdempotencyStore(purposes)` (`SharedKernel.Idempotency.Testing`) |
| `AddFakeCachingServices()` registering the Redis fakes | `AddFakeCachingServices()` + `AddFakeRedisServices()` (`SharedKernel.Caching.Redis.Testing`) |
| `FakeUserContext.IdentityKind` | `FakeUserContext.ActorKind` |
| `FakeFeatureClient` in `SharedKernel.Testing` | package `SharedKernel.FeatureManagement.Testing` (namespace `SharedKernel.Testing.FeatureManagement`) |
| `ApplicationPipelineTestHarness` over MediatR | the same harness (namespace `SharedKernel.Testing.Application`, package `SharedKernel.Application.Testing`) over the kernel pipeline; no mediator needed |
| `FakeHttpContextAccessor` in `SharedKernel.Testing` | `SharedKernel.Presentation.Testing` |

## 7. Governance

| Old | New |
|---|---|
| Numbered layering rules (`SharedKernelLayeringRules` layer checks, `MessagingLayeringRules`, `CachingAbstractionRules`, `CompositionRootExclusivityRules`, `ServiceDefaultsSchedulingLayeringRules`, `ServiceDefaultsWorkflowLayeringRules`, `PersistenceNeverReferencesApplicationOrSecurity`, the 06/07/13 grants) | `<SharedKernelTier>` + `eng/SharedKernelTiers.targets` (SKTIER000–006, errors) + `DependencyGraphRulesTests`; the purity rules listed in root `CLAUDE.md` → "Tiers & Dependency Rules" |
| `eng/tier-baseline.txt` (warnings) | deleted — every SKTIER diagnostic is an error |
| `SK0015` (MediatR stream misregistration) | deleted |
| `SK0016`/`SK0017`/`SK0018`/`SK0040`/`SK0041` over MediatR/Behaviors types | the same rules over `SharedKernel.Application` types |
| Manual per-package publish (`publish-package.yml`) and republish closures | one `v*` tag publishes every package (`release.yml`); `publish-package.yml` is a dry run |

## 8. Merge of main's presentation redesign and application model (P-579)

`main` redesigned 14.Presentation ("P-562 (presentation, main)") and the application model ("P-563 (application model,
merged from main in P-579)") while WO-086 ran. P-579 kept WO-086's architecture and re-implemented main's features on
it. Records: `05.Application/docs/p563/design.md` (A1 and A3 superseded by WO-086), `14.Presentation/docs/p562/`.

### Application (`05.Application`)

| Old | New |
|---|---|
| `IAuthorizeRequest` (`RequiredPermissions`, `PermissionMatch`) + `AddAuthorizationBehavior()` | `[RequirePermission("a", "b")]` (`SharedKernel.Application.Authorization.RequirePermissionAttribute`) on the command or query. Values of one attribute are alternatives; several attributes all apply. Always enforced, streaming queries included. A request without it is not checked (the old "empty list denies" rule went with `IAuthorizeRequest`) |
| `services.AddSharedKernelMediatR(asm)` + `services.AddSharedKernelApplicationBehaviors().AddDefaultBehaviors()…Build()` | `services.AddSharedKernelApplication(asm, app => app.UseMediatR().WithIdempotency().WithTransactions().WithAuditing())` (`SharedKernel.Application.Pipeline`; `UseMediatR` from `.Mediator.MediatR`). One call; a second throws |
| `AddTracingBehavior()`, `AddLoggingBehavior()`, `AddMetricsBehavior()`, `AddValidationBehavior()`, `AddDefaultBehaviors()` | always on |
| `AddIdempotencyBehavior(o => …)` / `AddTransactionBehavior()` / `AddAuditingBehavior()` / `AddBehavior(type, stage, deps)` | `WithIdempotency(o => …)` / `WithTransactions()` / `WithAuditing()` / `WithBehavior(type, stage, deps)` |
| `AddCachingBehaviors()` (`…Pipeline.Caching.Extensions`) | `app.WithCaching()` (`SharedKernel.Application.Pipeline.Caching.CachingPipelineExtensions`) |
| `Build()` throwing for a seam that was not registered before it | seams are checked when the host starts (`ValidateOnStart`), one message naming every missing one; registration order does not matter. `ISender` is always required (a mediator must be plugged in); `IRequestContext` is required when a scanned request carries `[RequirePermission]`. A plain `ServiceProvider` in a test runs no check — call `IStartupValidator.Validate()` |
| `AddSharedKernelRequestPipeline()`, `AddSharedKernelDomainEvents()` | deleted — `AddSharedKernelApplication` registers `RequestPipeline<,>`, `StreamRequestPipeline<,>` and the domain-event dispatcher, and scans handlers, validators and domain-event handlers |
| `SharedKernel.Application.Pipeline.Extensions.*` (`PipelineStage`, `AddDomainEventHandler`), `…Pipeline.Logging.ApplicationLoggingOptions`, `…Pipeline.Idempotency.IdempotencyBehaviorOptions` | namespace `SharedKernel.Application.Pipeline` |
| Public behavior classes (`TracingBehavior<,>` …) and `DomainEventDispatcher` | internal |
| `AddFluentValidationRequestValidators()` + FluentValidation's own `AddValidatorsFromAssembly` | `AddFluentValidationRequestValidators(typeof(X).Assembly)` registers the validators too |
| `idempotency.key_missing` | `idempotency.key_required` — every idempotency code is `01.Core`'s `ErrorCodes.Idempotency` (`KeyRequired`, `KeyInvalid`, `InProgress`, `KeyReused`); `IdempotencyErrorCodes` is gone |
| `ApplicationPipelineTestHarness`'s `AddBehaviors()…Build()` | `harness.Configure(app => app.With…()).Build()` (no mediator) or `.Build<TMarker>()` (adds `UseMediatR()`); both run the start check |

**Persisted data:** request idempotency reservations are now keyed per tenant **and caller** (a 64-hex SHA-256 digest of
tenant, actor kind, user id, client id, impersonator and the raw key). Reservations stored before the upgrade are not
found, so a retry that spans the deploy runs again.

### Presentation (`14.Presentation`)

| Old | New |
|---|---|
| `[RequirePermission]` on an endpoint (`SharedKernel.Presentation.Authorization`, Core) | `[RequireEndpointPermission]` / `.RequireEndpointPermission(...)` — for endpoints that send no command, hubs and gRPC methods. An endpoint that sends a command relies on the command's `[RequirePermission]` |
| `[RequireRole]`, `[RequireFreshAuthentication]`, `[RequireAuthenticationMethod]` evaluated by endpoint filters | the same attributes (`SharedKernel.Presentation.Authorization`, package `SharedKernel.Presentation.Core`), now real `[Authorize]` attributes over native policies; `[RequireAuthenticationMethod(..., MaxAgeSeconds = n)]` uses the per-method verification time (`amr_time`) |
| Sub-namespaces `SharedKernel.Presentation.WebApi.Errors/.Http/.Idempotency/.Middleware/.Results/.Options/…` | one namespace `SharedKernel.Presentation.WebApi` |
| `AddSharedKernelCors`, `UseSharedKernelSecurityHeaders`, `AddSharedKernelPayloadLimits`, upload validation, version-lifecycle middleware, `AddSharedKernelApiVersioning`/`AddSharedKernelOpenApi` in WebApi | `builder.AddSharedKernelWebApi()` + `app.UseSharedKernelWebApi(p => …)` (configured from `SharedKernel:Presentation:WebApi`); versioning, OpenAPI and Scalar in the `SharedKernel.Presentation.OpenApi` add-on (`AddSharedKernelOpenApi`, `MapSharedKernelOpenApi`; documents are not published outside Development by default). Upload validation was removed (use presigned uploads) |
| `result.ToProblemDetailsResult(...)`, `ToActionResult` | typed results `ToOk`, `ToCreated`, `ToAccepted`, `ToNoContent`, `ToOkWithETag`, `ToHttpResult` (also in MVC) |
| `MapXxxEndpoints()` extension methods | `IEndpointModule` (`static void Map(IEndpointRouteBuilder)`) + the generated `app.MapEndpoints()` (a source generator shipped inside the WebApi package) |
| `page`/`pageSize`, `cursor`/`limit` parsed by hand | `Paging` / `CursorPaging` endpoint parameters (`paging.Request` is a validated `PageRequest`/`CursorPageRequest`); invalid input answers 400 with `pagination.*` codes before the handler |
| `[RequireIdempotencyKey]` + `TryGetIdempotencyKey` | `[RequireIdempotencyKey]`/`[AcceptIdempotencyKey]` + an `IdempotencyKey` parameter or `GetIdempotencyKey()`; `If-Match` likewise (`IfMatch<TVersion>`, 412 for a stale version) |
| Correlation id owned by WebApi's middleware (main) | still `UseSharedKernelRequestContext()` (WO-086), placed **before** `UseSharedKernelWebApi()`; WebApi's `GetCorrelationId()` and the problem `correlationId` member read the request context. Inbound W3C baggage is refused at that edge (`TrustInboundBaggage` moved to `AddSharedKernelRequestContext(o => …)`) |
| gRPC correlation/tenant/authorization interceptors | removed — gRPC calls run through the same HTTP pipeline, so the request context and the attributes apply; `Presentation.Grpc` references Core, never WebApi |
| `TenantContextHubFilter` (public) | an internal request-context hub filter registered by `AddSharedKernelSignalR()`; `HubCallerContext.GetTenantId()` returns `TenantId?` |
| `ErrorType.Unexpected` for an outage | `ErrorType.Unavailable` (503) / `ErrorType.Timeout` (504) — `01.Core` |
