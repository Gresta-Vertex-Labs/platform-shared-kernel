# 05.Application — Commands, Queries and the Pipeline

> **Audience:** maintainers and AI agents changing code in this folder.
> **Consumers** read [`README.md`](README.md) and the package READMEs. This brain holds only what the source does not
> make obvious: rules, traps, couplings and decisions. The P-563 design is [`docs/p563/design.md`](docs/p563/design.md)
> and how it ran is [`docs/p563/waves.md`](docs/p563/waves.md). Superseded brains (P-544–P-562, and WO-035–WO-080
> before it) are in [`CLAUDE.history.md`](CLAUDE.history.md): read them only for why something once existed.

## What This Domain Is

The use-case layer: command and query vocabulary over MediatR, one pipeline of behaviors in a fixed order, the
domain-event bridge, and the ports (`IRequestContext`, `IUnitOfWork`, `IAuditTrailWriter`) infrastructure implements.
Handlers return `Result`/`Result<T>`; there is no response envelope. MediatR 12.4.1 is the engine and stays visible
(`ISender`, `IRequest<T>`); there is no facade over it.

## Packages

| Package | Role | May reference |
| --- | --- | --- |
| `SharedKernel.Application.Abstractions` | The ports: `IRequestContext` (+ `ActorKind`, `SystemRequestContext`, `AnonymousRequestContext`), `IUnitOfWork` (+ `CommitOutcomeUnknownException`, `TransactionRolledBackException`), `IAuditTrailWriter`/`AuditEntry`/`AuditOutcome`. Services never reference it directly | `SharedKernel.Primitives` only. No MediatR, no ORM: `06.Persistence` and `13.ServiceDefaults` implement it |
| `SharedKernel.Application` | The vocabulary, `[RequirePermission]`, the markers, `ICommandScope`, every behavior, the domain-event bridge and the one registration call | `.Abstractions`, `SharedKernel.Primitives`, `SharedKernel.Domain`, `MediatR`, `FluentValidation`, first-party `Microsoft.Extensions.*` |
| `SharedKernel.Application.Caching` | `CachingBehavior` + `ICacheableQuery<T>`, `CacheInvalidationBehavior` + `IInvalidatesCache`, `WithCaching()` | `SharedKernel.Application`, `SharedKernel.Caching.Abstractions`. The only package here that may reference `02.Caching` |

Hard rules:

1. No package here references concrete infrastructure (`06`, `07`, `12`, a cache implementation). Infrastructure
   implements this domain's ports. Locked by `SharedKernelLayeringRules` (`ApplicationNeverReferencesConcreteInfrastructure`,
   `ApplicationNeverReferencesCachingPollyHostingOrCore`, `ApplicationCachingNeverReferencesConcreteInfrastructure`).
2. `06.Persistence` may reference only `SharedKernel.Application.Abstractions`, and `07.Messaging` only its context types
   (root CLAUDE.md grants). Keep MediatR and FluentValidation out of `.Abstractions`, or both grants break.
3. The shared contracts are declared once, here. `00.Governance`'s `UnitOfWorkSeamRules` refuses a redeclaration.
4. MediatR stays pinned to 12.4.x, the last MIT major. Never bump it incidentally.
5. AOT and trimming are not constraints (owner ruling, 2026-09-15). Reflection is allowed where it is the clearest
   code, cached once per closed type (see [Reflection](#reflection)).

## Public surface

- **One namespace for services: `SharedKernel.Application`.** Everything a service writes against lives there:
  `ICommand`, `ICommand<T>`, `IQuery<T>`, `ICommandBase`, `IQueryBase`, the handler interfaces, `IStreamQuery<T>`,
  `IStreamQueryHandler<,>`, `RequirePermissionAttribute`, `IIdempotentRequest`, `IAuditableRequest<T>`,
  `ILoggableRequest<T>`, `ICommandScope`, `PipelineStage`, `ApplicationPipelineBuilder`,
  `ApplicationServiceCollectionExtensions`, `ApplicationLoggingOptions`, `IDomainEventHandler<T>`,
  `DomainEventNotification<T>`.
- **`SharedKernel.Application.Idempotency`** is for store implementers only (`18.Idempotency`):
  `IRequestIdempotencyStore`, `IdempotencyBeginResult`, `IdempotencyBeginStatus`.
- **`SharedKernel.Application.Context`, `.Transactions`, `.Auditing`** are `.Abstractions`' namespaces. There are no
  type forwarders: never add one back.
- **`SharedKernel.Application.Pipeline` is internal**: every behavior, `CommandScope`, `MediatRDomainEventDispatcher`,
  `ApplicationMetrics`, `ApplicationDiagnostics`, `FailureResponse`, `IdempotencyKeyScope`, `PipelineRequirements`. A
  new behavior is internal too; a consumer only ever sees the marker it reacts to and the `With…` call that enables it.
- `InternalsVisibleTo` goes to the package's own test project only.
- Every public change is recorded in the package's `PublicAPI.Unshipped.txt` (RS0016/RS0017 are errors).

## Registration

`AddSharedKernelApplication(assemblies, configure)` is the only entry point. It registers, in this order: logging and
metrics, MediatR over the given assemblies, their FluentValidation validators (`AssemblyScanner`, internal types
included, scoped, `TryAddEnumerable`), `IDomainEventDispatcher`, and then `ApplicationPipelineBuilder.Register()`.

- **Seams are checked at host start, never at registration.** `With…` calls record a `PipelineRequirement` (the
  feature as written, `"WithTransactions()"`, and the service type). `Register()` adds `PipelineRequirements`, an
  `IValidateOptions<PipelineRequirementsOptions>` that uses `IServiceProviderIsService`, and `ValidateOnStart()`. The
  host start then fails with one message naming every missing service. Nothing may throw at registration time for a
  missing seam: that reintroduces the ordering trap the old `Build()` had.
- **A new opt-in behavior declares every service it resolves** through `Require(...)` (built-in) or the
  `requiredServices` of `WithBehavior` (a sibling package). A behavior that resolves an undeclared service fails at
  the first request instead of at start.
- **A second call throws.** The presence of `PipelineRequirements` is the marker; the message names the fix.
- **The builder methods are idempotent** (a flag per built-in, `Contains` for custom types). `WithBehavior` validates
  the type (open generic, implements `IPipelineBehavior<,>`) and the stage.
- **Sibling packages extend the builder** with an extension method (`WithCaching()`) that registers what its behaviors
  need through `builder.Services` and calls `WithBehavior`. `SharedKernel.Application` never learns they exist.
- `AddDomainEventHandler<TEvent, THandler>()` may be called before or after; it uses closed generics only.

## Pipeline order

Registration order is execution order (outermost first). It is fixed in `ApplicationPipelineBuilder.Register()`:

```text
Observability   Tracing, Logging, Metrics, [custom]
Authorization   AuthorizationBehavior (WithAuthorization), [custom]
Validation      ValidationBehavior, [custom]
Query           [custom — CachingBehavior]
Command         CommandScopeBehavior (only when a command-stage behavior is on)
                IdempotencyBehavior
                AuditingBehavior          (outer half: Failed, after the rollback)
                TransactionBehavior       (the rest runs inside ExecuteInTransactionAsync)
                AuditingCommitBehavior    (inner half: Succeeded, queued on OnBeforeCommit)
                [custom — CacheInvalidationBehavior]
handler
```

- Authorization precedes validation: an unauthorized caller must not learn validation rules, and a validator may hit
  the database.
- **The first-registered behavior's post-`next()` code runs last.** `CommandScopeBehavior` is first in the command
  stage so its callbacks run after the commit and after idempotency completed. `00.Governance`'s
  `PipelineOrderAssertion` and `ApplicationBehaviorsCacheInvalidationOrderingLockTests` pin the order against the real
  assemblies; a change to `Register()` updates them.
- Command-stage behaviors are constrained to `ICommandBase`, caching to `IQueryBase`: the container never resolves them
  for the other kind. Keep new behaviors constrained the same way rather than branching at runtime.
- `ICommandScope` is always registered, whether or not the command stage is active.

## Authorization

- Declared on the use case with `[RequirePermission]` (`AttributeUsage` class/struct, `AllowMultiple`, `Inherited`).
  Values of one attribute are alternatives, several attributes all apply — the semantics of `14.Presentation`'s
  `[RequireEndpointPermission]`.
- `PermissionRequirements<TRequest>.AllOf` reads the attributes once per closed request type. An invalid attribute
  (no value, a blank one) throws in the attribute constructor, so the static initializer throws and every send of that
  request fails. Never catch it: failing every send is the fail-closed behavior.
- Anonymous → `Error.Unauthorized(ErrorCodes.Unauthorized.Default)`; a missing permission →
  `Error.Forbidden(ErrorCodes.Forbidden.InsufficientPermission)` with a message that names no permission. These are
  the codes the HTTP edge answers with; keep them identical.
- **A request without the attribute is not checked.** There is no "declared nothing" denial any more (the old
  `authorization.no_permissions_declared` belonged to `IAuthorizeRequest`).
- **Known gap:** nothing detects a request carrying `[RequirePermission]` in a service that never called
  `WithAuthorization()`; such a request runs unchecked. Documented as a pitfall; a start-time check is a candidate
  follow-up.
- **Edge vs use case.** Permissions go on the use case, with `[RequirePermission]`. `14.Presentation`'s
  `[RequireEndpointPermission]` is only for what sends no command (hubs, gRPC services and methods, endpoints that call
  nothing through `ISender`); its step-up attributes (`RequireFreshAuthentication`, `RequireAuthenticationMethod`) stay
  at the edge because authentication strength is an HTTP concern. An endpoint that sends a command does not repeat its
  permissions.
- The names differ on purpose (owner decision, `52975eed`): the edge attribute was also called `RequirePermission`
  until then, which made a file importing both namespaces fail with CS0104. Keep this attribute's name, and never add a
  second type named `RequirePermissionAttribute` anywhere in the platform.
- `SK0040` (`00.Governance`) flags `[RequirePermission]` or `IIdempotentRequest` on a request whose response is not
  `Result`/`Result<T>`; it matches the attribute by name, on the type or a base type.

## Idempotency

- `IRequestIdempotencyStore` is a minimal port, deliberately not `07.Messaging`'s `IIdempotencyStore`.
  `TryBeginAsync(key, fingerprint)` reserves atomically and returns `Started` (with a reservation token), `InProgress`,
  `Completed` (with the stored response) or `FingerprintMismatch`. `CompleteAsync`/`ReleaseAsync` take the token and
  return `false`, never throw, when the reservation was lost; a lost `CompleteAsync` after success logs 5120 and still
  returns the response. A reservation that is never settled expires after a store-defined TTL.
- Codes are `ErrorCodes.Idempotency` in `01.Core`: `KeyRequired` (blank key, Validation), `InProgress` and `KeyReused`
  (Conflict), and `KeyInvalid` (malformed header, used only by `14.Presentation`). One source; no local constants, no
  drift test.
- **The key scope is a stored format.** `IdempotencyKeyScope.Create(context, key)` hands the store SHA-256 (64 lowercase
  hex) over: the label, `TenantId` (`"D"`), `ActorKind` (numeric), `UserId`, `ClientId`, `ImpersonatorId`, the raw key;
  each field `0x00` when absent, else `0x01` + big-endian int32 UTF-16 length + UTF-16LE code units. `SessionId` is left
  out so a retry after a fresh sign-in replays.
- **The label is frozen: `SharedKernel.Application.Behaviors.Idempotency.KeyScope.v1`.** It names the package that no
  longer exists, on purpose: changing any byte orphans every reservation already stored in `18.Idempotency`'s stores. A
  new layout gets a new label and an operational note. `Handle_ScopedKey_MatchesThePublishedV1Layout` pins it with a
  value computed outside .NET.
- Anonymous callers of one tenant share one scope; only the fingerprint separates them (documented residual risk,
  pinned by the `Handle_AnonymousCallersOfOneTenant_*` tests). `WithIdempotency()` requires `IRequestContext` for this
  reason: an anonymous fallback would put every caller in one scope.
- The fingerprint is `IIdempotentRequest.Fingerprint` when set (trimmed, non-empty), else SHA-256 of the default JSON
  serialization; a serialization failure becomes an `InvalidOperationException` naming the command type.
- A nested command skips idempotency; the outermost command owns the key.

## Transactions, auditing and the command scope

- `TransactionBehavior` runs the rest of the pipeline inside `IUnitOfWork.ExecuteInTransactionAsync`. The delegate may
  run more than once (retrying strategy): handlers must be re-runnable, and callbacks queued by a discarded attempt are
  dropped. A nested command with an active transaction joins it (a failure makes it rollback-only); nested with none
  calls `next()` directly.
- Auditing has two halves around the transaction: `AuditingCommitBehavior` queues `Succeeded` on `OnBeforeCommit` (or
  writes directly without a transaction); `AuditingBehavior` writes `Failed` after the rollback on the writer's own
  connection. A `RecordAsync` failure on the success or failed-`Result` path propagates; on the exception path it is
  logged (5130) and the original exception propagates.
- `CommandScope` keeps a stack of callback frames per DI scope. Every dispatch pushes one; a nested success merges its
  callbacks into the parent; any failure discards them; the outermost success runs them. A throwing callback is logged
  (5110) and does not change the response. `OnCompleted` outside a command throws.
- MediatR sends a nested `ISender.Send` through the same DI scope, which is what makes `IsNested` observable.

## Caching (`SharedKernel.Application.Caching`)

- `CachingBehavior` caches the `TValue` of a successful `Result<TValue>`, never the `Result` (private constructors;
  FusionCache's L2 serializer throws on it). It is constrained to the non-generic `ICacheableQuery`, whose internal
  member is implemented by `ICacheableQuery<TValue>`, so the typed cache call needs no reflection and a consumer cannot
  implement the base alone.
- The handler runs inside `ICacheService.GetOrSetAsync` (context overload); a failed `Result` calls `SkipCaching()` and
  travels out in a closure local. **That is only correct because a skipped value is not handed to concurrent waiters**
  (`CachingBehaviorConcurrencyTests` pins it against a real FusionCache; `16.Testing`'s `FakeCacheService` reproduces
  it). Eager refresh and factory timeouts are forced off: both run the handler after the request scope is disposed.
- Keys go through `ITenantCacheKeyProvider`: `{service}[:@{tenant}]:{QueryType}:{CacheKey}[:u:{userId}]`. The query
  type segment prevents payload confusion; SK0041 catches duplicate simple names. Commands name the query through
  `CacheKeyRef.For<TQuery>`.
- `CacheScope.Tenant` is the zero value. A missing identity skips the cache (Warning), never widens the key.
- Eviction is queued with `ICommandScope.OnCompleted`, runs with `CancellationToken.None`, and isolates each key and tag.
  Targets are built and validated before the handler runs.

## Reflection

Every site is cached once per closed type or runtime `Type`:

| Site | What | Why |
| --- | --- | --- |
| `Shared/FailureResponse.cs` | `TResponse.GetMethod("Failure")` + `CreateDelegate` | a behavior builds a failed `Result`/`Result<T>` knowing only `TResponse` |
| `Shared/ResponseOutcome.cs` | `TResponse.GetProperty("Error")` + `CreateDelegate` | read the error of any failed response |
| `Authorization/AuthorizationBehavior.cs` | `GetCustomAttributes<RequirePermissionAttribute>(inherit: true)` in `PermissionRequirements<TRequest>` | the attributes of a request type |
| `DomainEvents/MediatRDomainEventDispatcher.cs` | `MakeGenericType` + a compiled `Expression`, in a `ConcurrentDictionary<Type, …>` | the event type is known only at runtime; no `MakeGenericMethod`, so `ReflectionExemptionRegistry` needs no entry |
| `Idempotency/IdempotencyResponseSerializer.cs` | two `JsonConverter`s and a factory for `Result`/`Result<T>` | `Result` does not round-trip through STJ's default contract |
| `Extensions/ApplicationPipelineBuilder.cs` | `IsGenericTypeDefinition`, `GetInterfaces()` | validates `WithBehavior`'s type once, at registration |

## Technology

| Concern | Choice |
| --- | --- |
| Mediator | MediatR 12.4.1 |
| Validation | FluentValidation; validators run sequentially (a scoped `DbContext` is not concurrency-safe); discovered with `AssemblyScanner` inside the registration call |
| Tracing | static `ActivitySource` `SharedKernel.Application` (the one sanctioned static instrument) |
| Metrics | `Meter` `SharedKernel.Application` from `IMeterFactory`; histogram `sharedkernel.application.request.duration` (seconds) |
| Options | `ApplicationLoggingOptions` with `[Range]` + `ValidateDataAnnotations().ValidateOnStart()`; `Microsoft.Extensions.Options.DataAnnotations`, never a `SharedKernel.Configuration` reference |
| Logging | `[LoggerMessage]`, explicit EventIds: 5100–5199 `SharedKernel.Application` (5100–5104 logging, 5110 command scope, 5120 idempotency, 5130 auditing), 5200–5299 `.Caching`. 5000–5099 is unused |

## Decisions

| Decision | Chosen | Rejected | Cost accepted |
| --- | --- | --- | --- |
| Package shape (P-563 A3) | Three packages: ports, application, caching | A separate behaviors package | A service that wants only the vocabulary takes FluentValidation and the behaviors along (they register nothing without the call) |
| Registration (P-563 A4) | One call, seams checked at host start through `ValidateOnStart` | `AddSharedKernelApplicationBehaviors()…Build()` throwing at registration | A missing seam surfaces at start, not at the registration line |
| Who registers MediatR (P-563) | The call does, with the handlers and validators of the given assemblies | The service calls `AddMediatR` itself | A service no longer scans its own assemblies; it passes them to the call, and never calls `AddMediatR` for the same assemblies again |
| Authorization (P-563 A2) | `[RequirePermission]` on the use case, enforced on every path | `IAuthorizeRequest`/`PermissionMatch`; the endpoint as the only check | OpenAPI cannot see a use-case permission; 14's edge attribute was renamed `RequireEndpointPermission` so the two never share a name |
| Behavior visibility (P-563 A5) | Internal, in `SharedKernel.Application.Pipeline` | Public behavior classes | Tests reach them through `InternalsVisibleTo` |
| Idempotency codes (P-563 A6) | `ErrorCodes.Idempotency` in `01.Core` | Local constants plus a drift test against 14 | — |
| Key scope label | Frozen at `…Behaviors…KeyScope.v1` after the rename | A label naming the new package | The label names a package that no longer exists |
| Idempotency reservation scope (P-562 X3) | Tenant + actor kind + subject + client + impersonator + key, always hashed | Tenant-only; passing structured scope to the store; `SessionId` in the scope | Anonymous callers of a tenant share a scope guarded by the fingerprint |
| `CachingBehavior` miss path (P-547) | `GetOrSetAsync` + `SkipCaching()`, eager refresh and factory timeouts forced off | get-then-set (no stampede protection) | No eager refresh or soft timeout for cached queries |
| Short-circuit shape | `FailureResponse.Create<TResponse>` | A second failure shape | Requests with a denial path must return `Result`/`Result<T>` (SK0040) |

## Cross-Domain Couplings

| If you change… | Also check |
| --- | --- |
| `IUnitOfWork`'s contract (retry, rollback-only, `OnBeforeCommit`) | `06.Persistence`'s `EfUnitOfWork`/`UnitOfWorkCoordinator`; `16.Testing`'s `FakeUnitOfWork`; this test project's `Support/FakeUnitOfWork` |
| `IRequestContext`'s shape | `13.ServiceDefaults.Security`'s request context; `06.Persistence` (actor, tenant, ledger); `07.Messaging`'s `MessageRequestContext`; `16.Testing`'s `TestRequestContext` |
| `IAuditTrailWriter`/`AuditEntry` | `06.Persistence.EfCore.Auditing`; `16.Testing`'s fakes |
| `IRequestIdempotencyStore`'s contract | `18.Idempotency`'s Redis and EF Core stores; `16.Testing`'s `FakeRequestIdempotencyStore` |
| `IdempotencyKeyScope`'s layout or label | every reservation stored by `18.Idempotency` (operational note); its EF Core `key` column (512) |
| `ErrorCodes.Idempotency`, the 401/403 codes | `14.Presentation` answers with the same codes; `11.Communication.Rest` reads them back |
| `RequirePermissionAttribute`'s name or namespace | `00.Governance`'s SK0040 (matches by name); `14.Presentation`'s `RequireEndpointPermissionAttribute`, whose semantics it mirrors |
| `ApplicationPipelineBuilder.Register()`'s order | `00.Governance`'s `PipelineOrderAssertion`, `ApplicationPipelineRules`, `ApplicationBehaviorsCacheInvalidationOrderingLockTests` |
| `ApplicationPipelineBuilder`'s surface | `16.Testing`'s `ApplicationPipelineTestHarness.Configure`; `17.Workflows`, `19.Scheduling` and the samples' registrations |
| `ICacheableQuery<T>`/`IInvalidatesCache`/key format | `02.Caching.Abstractions` (`ICacheService`, `CachePolicy`, `CacheFactoryContext`, `CacheKeyFormat`); SK0017/SK0018/SK0041 |
| Any public API | the package's `PublicAPI.Unshipped.txt` and README; the samples |

## Test Rules

- Each test project references only the package it tests, never `16.Testing` or `00.Governance`; doubles live in
  `Support/`.
- Prove order and interaction through a real `ServiceCollection` and `AddSharedKernelApplication`, and run the
  start-time checks (`IStartupValidator`) where a seam matters — never a hand-rolled `RequestHandlerDelegate` chain.
- Every behavior has a success, failure and (where it applies) exception test; the command scope has merge, discard and
  "outside a command" tests; idempotency covers every store status, release on failure and throw, per-caller scoping
  and the golden vector.
- Registration has tests for the start-time seam message (every missing service at once), the double-call guard,
  `WithBehavior`'s validation and stage placement, and assembly scanning of handlers and internal validators.

## Changelog

> Entries before P-563 are in [`CLAUDE.history.md`](CLAUDE.history.md) with the brain they belong to.

- [2026-09-24] **P-563 — one application model with a thin HTTP edge (A1–A6).** `SharedKernel.Application.Behaviors`
  merged into `SharedKernel.Application`; `.Behaviors.Caching` renamed `SharedKernel.Application.Caching`. One
  `AddSharedKernelApplication(assemblies, app => app.With…())` registers MediatR, validators, the bridge and the
  pipeline, checks seams at host start and refuses a second call. `[RequirePermission]` on the use case replaces
  `IAuthorizeRequest`/`PermissionMatch`; the pipeline's 401 is `unauthorized.default`. Behaviors internal in
  `SharedKernel.Application.Pipeline`; type forwarders to `.Abstractions` removed; `IdempotencyErrorCodes` replaced by
  `01.Core`'s `ErrorCodes.Idempotency`. Brain rewritten to rules; the previous one archived. Republish needed (breaking)
