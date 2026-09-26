# Plan: SharedKernel Foundation Refactor (WO-086)

## Context

The kernel's package graph has grown confusing. Three causes:

1. **Numbered folders double as dependency layers**, but kernel packages aren't layers of one app — they are libraries aimed at a specific project inside a consuming service (Domain / Application / Infrastructure / Host). The mismatch forced four hand-written layering "grants" (06→05, 07→05, 13→17, 13→19) plus "may reference 01–04 and, from 05, ONLY…" clauses.
2. **Contracts filed in the wrong package.** `IRequestContext`/`IUnitOfWork`/`IAuditTrailWriter` live in `SharedKernel.Application.Abstractions`, so Persistence and Messaging appear to depend "up" on Application. Pipeline markers and ports (`IIdempotentRequest`, `IAuthorizeRequest`, `IRequestIdempotencyStore`, `ICommandScope`…) live in `Application.Behaviors`, an implementation package dragging MediatR + FluentValidation, so `18.Idempotency` and every service's Application project pull the whole pipeline.
3. **MediatR is in every public contract** (`ICommand : IRequest<Result>`, behaviors take `RequestHandlerDelegate`), pinned at 12.4.1 (last MIT release). We must be able to swap it.

Deep analysis also found:
- about 12 shapes of tenant identity: `Guid`, `Guid?`, `string`, and four copies of `TenantScope`;
- two caller-identity models with mismatched enums (`IdentityKind` vs `ActorKind`);
- the correlation id is replaced at the first outbound hop, in three different formats;
- about 12 incompatible readiness-probe contracts;
- two idempotency store contracts;
- optional features forced as dependencies: RabbitMQ + Azure Service Bus + the EF outbox in one package, the Redis SignalR backplane, EF/gRPC telemetry in the ServiceDefaults base, Encryption/Auditing in ServiceDefaults.Persistence, gRPC hosts pulling all of WebApi;
- `SharedKernel.Testing` documented for consumers but not packable;
- manual per-package "republish closures";
- five layering rules that never execute.

It also found **four live defects**:
- **Idempotency header mismatch:** outbound REST sends `x-idempotency-key`, but inbound WebApi reads `Idempotency-Key`.
- **Missing accessor:** `ITenantContextAccessor` has no implementation, yet both idempotency stores refuse to start without one.
- **Consumer tenant gap:** in message consumers `ITenantProvider` is never set, so outbound calls from a consumer carry no tenant.
- **Correlation id lost:** it is not propagated end to end.

Nothing is in production use yet (alpha packages on GitHub Packages only), so breaking changes are acceptable.

**Outcome:** a tiered package architecture enforced by the build; one execution-context foundation (tenant, caller, correlation, unit of work); a kernel-owned mediator abstraction with MediatR behind an adapter; optional dependencies in satellite packages; per-capability packable test packages; a release train; and every doc, agent and command rewritten to match.

### Decisions (confirmed with user)
| Decision | Choice |
|---|---|
| Folder layout | Keep `00`–`20` capability folders. Numbers stop meaning layers; each csproj declares a **tier**, enforced at build time. |
| Tenant identity | `readonly record struct TenantId(Guid Value)` in the foundation; "no tenant" is `TenantId?` = `null`, never `Guid.Empty`. |
| Mediator | Kernel owns request/handler/sender/pipeline contracts. MediatR is used only inside `SharedKernel.Application.Mediator.MediatR`. |
| Releasing | Release train: one `v*` tag packs and publishes **all** packages. Manual per-package closures are removed. |
| Scope | Full foundation: all findings and defects, then governance, CI, docs and agents. |

---

## Target architecture

### Tiers (replace numbered layering)
| Tier | May reference (ProjectReference) | Third-party rule | Packages |
|---|---|---|---|
| **Foundation** | Foundation | any | Primitives, Core, Configuration, **Execution (new)**, Compression, Cryptography, Localization, Validation, DataPrivacy, FeatureManagement |
| **Model** | Foundation, Model | none | Domain, Contracts (Contracts ↛ Domain stays an explicit purity rule) |
| **Abstractions** | Foundation, Model, Abstractions | only `Microsoft.Extensions.*.Abstractions` (allow-list) | Caching.Abstractions, **Application** (contracts only), Persistence.Abstractions, Messaging.Abstractions, Storage.Abstractions, Search.Abstractions, AI.Abstractions, Security.Abstractions, **Idempotency.Abstractions (new)**, Integration.Notifications.Abstractions, Reporting.Abstractions |
| **Adapter** | Foundation, Model, Abstractions, plus adapters named in its `SharedKernelAllowedAdapterReferences` | any | Cryptography.Argon2/.KeyVault.Azure, Validation.FluentValidation, Caching.FusionCache/.Redis.Core/.Redis/.DistributedLocking/.HashStore/.PubSub, Persistence.Npgsql/.EfCore/.Dapper/.EfCore.Auditing/.EfCore.Encryption, Messaging.MassTransit (+ .RabbitMq/.AzureServiceBus/.EfCore new), Storage.S3/.Obs, Search.Meilisearch/.ElasticSearch, AI.Qdrant/.SemanticKernel, Communication.Internal/.Rest/.Grpc, Security.Oidc/.ApiKey/.Mtls/.Totp, Integration.Webhooks, Notifications.Email.SendGrid/.Sms.Twilio, Workflows.Temporal, Scheduling, Idempotency.EfCore/.Redis, Reporting.Csv/.Spreadsheet/.Pdf |
| **Host** | anything except Testing/Tooling | any | **Application.Pipeline** (was Behaviors), **Application.Pipeline.Caching**, **Application.Mediator.MediatR (new)**, ServiceDefaults + ServiceDefaults.*, MultiTenancy, **Presentation.Core (new)**, Presentation.WebApi/.Grpc/.SignalR/**.SignalR.Redis (new)**, **Presentation.GraphQL** (moved from Communication.GraphQL: server-side, so it is presentation) |
| **Testing** | anything except Tooling; referenced only by test projects | any | Persistence.Testing, new per-capability `*.Testing`, `SharedKernel.Testing.Internal` (not packable) |
| **Tooling** | nothing | any | Analyzers, ArchitectureTests, Linter |

**Global rules:**
- Nothing references a Host package except another Host package.
- Nothing in production references Testing.
- No cycles.
- Named adapter-to-adapter edges are declared in the csproj. Examples: Obs→S3, EfCore→Npgsql, Dapper→Npgsql, EfCore.Auditing/Encryption→EfCore, Redis.*→Redis.Core, Idempotency.EfCore→Persistence.EfCore, Idempotency.Redis→Redis.Core, Rest/Grpc→Communication.Internal, MassTransit.*→MassTransit.

**What a consuming service references:**
- **Domain project:** Domain.
- **Application project:** Application (+ Idempotency/Caching abstractions if it uses those markers).
- **Infrastructure project:** adapters.
- **Api/Worker project:** Host packages plus its own Application and Infrastructure. Api → Application is correct and expected.

### New and reshaped contracts
- **`SharedKernel.Execution`** (01.Core, Foundation, references Primitives only):
  - `TenantId`, `TenantScope` (the single copy; `Global` / `For(TenantId)`).
  - `ActorKind`: one enum, replacing `IdentityKind`.
  - `IRequestContext`, now with `TenantId? TenantId`, `CorrelationId`, `ActorKind`, `ClientId`, `SessionId`.
  - `SystemRequestContext`, `AnonymousRequestContext`.
  - `IRequestContextAccessor`: AsyncLocal ambient context, set by every inbound adapter and read by every outbound one; replaces `IHttpContextAccessor` in client packages.
  - `IUnitOfWork` + `CommitOutcomeUnknownException` / `TransactionRolledBackException`.
  - `IAuditTrailWriter` / `AuditEntry` / `AuditOutcome`.
- **`SharedKernel.Primitives.Health`**: `IReadinessProbe { string Name; Task<ReadinessReport> ProbeAsync(ct) }` and `ReadinessReport(Status, Latency?, Description?, Data)`. Every provider implements it and registers it itself. A per-target provider (storage store, search index) registers one probe per configured target.
- **`WellKnownHeaders`** gains `IdempotencyKey = "Idempotency-Key"` plus actor/client headers. Outbound code sends the caller's `X-Correlation-Id` value, never `Activity.Id`.
- **`SharedKernel.Application`** (Abstractions tier, **no MediatR**):
  - Kernel-owned `IRequest<TResponse>`, `ICommand`/`ICommand<T>`/`IQuery<T>`/`IStreamQuery<T>` and their handlers.
  - `ISender` (Send, CreateStream), `IPipelineBehavior<TRequest,TResponse>` + `RequestHandlerContinuation<TResponse>`.
  - `IDomainEventHandler<T>`.
  - All markers: `IAuthorizeRequest`, `IIdempotentRequest`, `IAuditableRequest`, `ILoggableRequest`, `ICacheableQuery`, `IInvalidatesCache`, `CacheScope`/`CacheKeyRef` (references Caching.Abstractions, which is dependency-free).
  - `ICommandScope`, and the validation port `IRequestValidator<TRequest>`.
- **`SharedKernel.Application.Pipeline`** (Host):
  - Behaviors on the kernel `IPipelineBehavior`, with the `PipelineStage` order and `Build()` seam checks kept.
  - `RequestPipeline<TRequest,TResponse>`, which composes behaviors plus the handler.
  - Native `DomainEventDispatcher`, which resolves `IDomainEventHandler<T>` through DI. MediatR is not needed for notifications.
  - No FluentValidation dependency.
- **`SharedKernel.Application.Mediator.MediatR`** (Host): `AddSharedKernelMediatR(assemblies)`. It implements `ISender` via MediatR using internal wrapper request/handler types; the MediatR handler invokes `RequestPipeline`. Swapping the mediator later means writing another `ISender` adapter; application code is untouched.
- **FluentValidation adapter:** `SharedKernel.Validation.FluentValidation` gains `AddFluentValidationRequestValidators()`, bridging `IValidator<T>` → `IRequestValidator<T>`.
- **`SharedKernel.Idempotency.Abstractions`** (18.Idempotency, new): one reservation contract, `IIdempotencyStore`.
  - `TryBeginAsync(IdempotencyPurpose, key, fingerprint, ttl)` → `IdempotencyReservation{Status, StoredResponse?, Token}`, plus `CompleteAsync` and `ReleaseAsync`.
  - Stores are registered keyed by purpose (`Request` / `Message`), so a service can use different backends per purpose.
  - The tenant comes from `IRequestContextAccessor`, with one "no tenant" encoding.
  - Replaces both `IRequestIdempotencyStore` (05) and Messaging's `IIdempotencyStore`.

---

## Execution mechanics

- **Tracking:** root `state-map.md` gets **WO-086**, with phases **P-562 onward** (one per step below).
- **Working copy:** the plan is copied to `docs/refactor/FOUNDATION-PLAN.md`.
- **Pull requests:** one branch/PR per phase into `main`. Every PR must keep `dotnet build Platform.SharedKernel.slnx -c Release` and the Unit test filter green.
- **Parallel work:** parallel phases run in short-path worktrees under `C:\wt\…`, per the MAX_PATH memory. Packing uses a temporary `NUGET_PACKAGES`.
- **Who implements:** until Step 14, the domain agents and brains still describe the old rules. Steps are therefore implemented by general-purpose agents, or directly, using this plan as the spec; domain phase-implementers are not used.
  - Step 0 adds a banner to root `CLAUDE.md`: *"WO-086 foundation refactor in progress — `docs/refactor/FOUNDATION-PLAN.md` supersedes the Layering Rules section."*
- **Package-name length rule (from WO-084/P-532):** a new project's test-assembly path `…\{Name}\{Name}.Tests\obj\Release\net10.0\{Name}.Tests.dll` must stay within 245 characters at `C:\Github\platform-shared-kernel`. Check every new or renamed package against this before scaffolding it.
- **Tier-violation baseline:** Step 1 records the current violations as a baseline file. Each later step must delete the entries it fixes. The program ends with an empty baseline.

---

## Steps (in order)

### Step 0: Preparation (P-562)
1. Create branch `refactor/wo-086-foundation`, and add `docs/refactor/FOUNDATION-PLAN.md` (this plan) and the tier table.
2. Record the baseline: build warning count, Unit and Integration test counts, and the pack output list. Save it as `docs/refactor/baseline.md`.
3. Add WO-086 and phases P-562 to P-578 to root `state-map.md`, and add the banner to root `CLAUDE.md`.
4. Delete the leftover `05.Application/SharedKernel.Application.Caching/` folder (bin/obj only).

### Step 1: Tier enforcement infrastructure (P-563)
1. Add `<SharedKernelTier>` to every production csproj (about 95), and `<SharedKernelAllowedAdapterReferences>` where needed.
2. Add `Directory.Build.targets` target `ValidateSharedKernelTier`. For each ProjectReference, it queries the referenced project's tier (via an MSBuild call to a `GetSharedKernelTier` target), checks it against the matrix, and raises `SKTIER001` (tier violation), `SKTIER002` (undeclared adapter edge) or `SKTIER003` (third-party package in the Abstractions tier outside the allow-list). Violations listed in `eng/tier-baseline.txt` are downgraded to warnings.
3. `SharedKernel.ArchitectureTests`:
   - Add `DependencyGraphRules`, which parses every csproj and asserts: the tier matrix, no Host/Testing leaks, no cycles, every production csproj declares a tier, and the baseline contains only still-existing edges.
   - Add a test for it in `ArchitectureTests.Tests`.
4. Also enforce PackageReference-based harnesses and samples by name convention. Samples are exempt; they are consumers.
5. **Verify:** the build is green with baseline warnings only. Measured in P-562: the baseline holds exactly three edges: `Application`→`MediatR` (SKTIER003) and `Idempotency.EfCore`/`.Redis`→`Application.Behaviors` (SKTIER001). The old numbered-layer grants (06/07/13→Application.Abstractions, 13→17/19) are legal under the tiers. Their removal is driven by Steps 2 and 7 (contract placement), not by the tier check. Grpc→WebApi is Host→Host, so Step 8 removes it for dependency weight, not legality.

### Step 2: `SharedKernel.Execution` (P-564)
1. Create `01.Core/SharedKernel.Execution` (+ `.Tests`, README, PublicAPI files).
2. Move the ten `Application.Abstractions` types into it (new namespaces `SharedKernel.Execution.*`).
3. Add `TenantId`, `TenantScope`, `ActorKind`, `CorrelationId` on `IRequestContext`, `IRequestContextAccessor` + `RequestContextAccessor` (AsyncLocal) + `RequestContextScope.Begin(ctx)`.
4. Repoint Persistence.Abstractions, Messaging.Abstractions, ServiceDefaults.Security, Application, Behaviors, 16.Testing and the samples. Delete `SharedKernel.Application.Abstractions` and its type forwards.
5. Governance:
   - Delete `MessagingLayeringRules.OnlyReachesApplicationContextTypes` + predicate and the 06 grant.
   - Retarget `UnitOfWorkSeamRules` to the Execution namespace.
   - Remove the resolved baseline entries.
6. Also update: `Directory.Packages.props`, the `.slnx`, both `.slnf`, and the consumer-verify harnesses.

### Step 3: Tenant and caller unification (P-565)
1. **Security.Abstractions → Execution:** `IUserContext` uses `ActorKind` and `TenantId?`. Delete `IdentityKind`, `ITenantProvider`, `UserContextTenantProvider`, `AmbientTenantProvider`.
2. **Domain:** `IHasTenant.TenantId` becomes `TenantId`, as do the `Tenanted*` bases. In Persistence.EfCore, a value-converter convention maps `TenantId` to the uuid column; the RLS binding, write guard, `ICrossTenantScope` and the Dapper session binder are all updated.
3. **Messaging:** delete `ITenantContextAccessor` (fixes defect 2 by construction). `TenantHeaderPropagator` reads `IRequestContextAccessor`.
4. **Explicit tenant parameters:**
   - Search, AI, Workflows and Scheduling use `Execution.TenantScope`; delete the four local copies.
   - Caching (`ITenantCacheService`, key provider), Storage (`ForTenant`) and FeatureManagement take `TenantId`. Formatting happens once, via `TenantId.ToString()`.
5. **MultiTenancy:**
   - Resolution strategies return `TenantId?`.
   - Delete `TenantBaggageKeys` (use `WellKnownBaggageKeys`).
   - The catalog and status validator take `TenantId`.
6. **Presentation:** the SignalR hub filter and gRPC tenant interceptor read the accessor, and drop their private `"TenantId"` item keys.
7. **16.Testing:** collapse the fake tenant providers into one fake request context.

### Step 4: Correlation and context propagation (P-566). Fixes defects 1, 3 and 4.
1. **Inbound, HTTP:** one middleware, `UseSharedKernelRequestContext()` (ServiceDefaults.Security, Host). It reads or creates `X-Correlation-Id`, takes the user from `IUserContext` and the tenant from MultiTenancy resolution, then opens a `RequestContextScope` and sets baggage. `CorrelationIdMiddleware` in Presentation.WebApi is folded into it or delegates to it.
2. **Inbound, other channels:** gRPC server interceptor (Presentation.Grpc); MassTransit consume filter (the existing `InboundRequestContextFilter` now opens a scope, so outbound calls made from a consumer carry the tenant); Temporal activity inbound interceptor; the Scheduler job runner (`SystemRequestContext` with the job's `TenantScope`).
3. **Outbound:** REST handlers, gRPC client interceptors, MassTransit propagators, Temporal outbound and Webhooks all read the accessor and write the `WellKnownHeaders` correlation, tenant, actor and client headers. Remove the `Microsoft.AspNetCore.Http` reference from Communication.Rest/.Grpc.
4. **Idempotency header:** use `WellKnownHeaders.IdempotencyKey` everywhere. Delete Communication's internal `IdempotencyHeaders`.
5. **Tests:** end-to-end propagation tests covering HTTP→REST, HTTP→gRPC, HTTP→bus→consumer→REST and job→REST. Correlation must survive every hop with its original value.

### Step 5: Application contracts and mediator abstraction (P-567)
1. `SharedKernel.Application`:
   - Replace the MediatR base types with kernel-owned `IRequest<T>`, `ISender`, `IPipelineBehavior<,>`, `RequestHandlerContinuation<T>` and the handler interfaces.
   - Move in the markers and ports from Behaviors and Behaviors.Caching (listed above), plus `IRequestValidator<T>`.
   - Remove the MediatR PackageReference.
   - Remove the internal default method on `ICacheableQuery` that took a MediatR delegate; the behavior now does that work.
2. Rename Behaviors → `SharedKernel.Application.Pipeline`:
   - Behaviors implement the kernel `IPipelineBehavior`.
   - Add `RequestPipeline<TRequest,TResponse>`, which composes behaviors in `Build()` order, and a stream variant.
   - Replace `MediatRDomainEventDispatcher` with a native `DomainEventDispatcher` (it implements the Domain `IDomainEventDispatcher`).
   - ValidationBehavior uses `IRequestValidator<T>`; remove FluentValidation.
3. Rename Behaviors.Caching → `SharedKernel.Application.Pipeline.Caching`.
4. New `SharedKernel.Application.Mediator.MediatR`: MediatR 12.4.1 is referenced only here. `AddSharedKernelMediatR(params Assembly[])` scans kernel handler types and registers wrapper MediatR handlers that invoke `RequestPipeline`; its `ISender` implementation delegates to MediatR.
5. `Validation.FluentValidation`: add the `IRequestValidator<T>` bridge and its registration.
6. Workflows `CommandActivity` and Scheduling `ScheduledCommandJob`/registry use the kernel `ISender`. Their references stay Adapter→Abstractions (Application contracts).
7. Governance:
   - SK0015 is deleted (MediatR stream misregistration no longer applies).
   - SK0016, SK0017, SK0018, SK0040, SK0041 and `MarkerInterfaceHelpers` are retargeted to the new namespaces and the kernel `IRequest`.
   - `ApplicationPipelineRules` and `PipelineOrderAssertion` are retargeted to the kernel `IPipelineBehavior`.
   - Add a rule: *MediatR is referenced only by `Application.Mediator.MediatR`*.
8. 16.Testing: `ApplicationPipelineTestHarness` builds via the kernel pipeline. It can run with no mediator at all (a direct `RequestPipeline`) and optionally with the MediatR adapter. Update `InMemoryScheduledJobRegistry`.
9. Update the samples: OrderApi and BillingApi use `AddSharedKernelMediatR`; BillingApi's raw `INotificationHandler` becomes `IDomainEventHandler`.

### Step 6: Unified idempotency (P-568)
1. Create `18.Idempotency/SharedKernel.Idempotency.Abstractions` (contract above).
2. Rewrite `Idempotency.EfCore` and `Idempotency.Redis` against it, with purpose-keyed registration (`AddEfCoreIdempotency(p => p.ForRequests().ForMessages())`). Remove their references to Behaviors and Messaging.Abstractions. Replace the startup validator with a request-context check.
3. `IdempotencyBehavior` (Pipeline) and MassTransit's consumer idempotency consume `IIdempotencyStore` keyed by purpose. Delete `IRequestIdempotencyStore` and Messaging's `IIdempotencyStore`.
4. Update `FakeRequestIdempotencyStore` → `FakeIdempotencyStore`, the READMEs and the tests (existing atomicity tests are carried over).

### Step 7: Readiness-probe contract (P-569)
1. Add `IReadinessProbe`/`ReadinessReport` to Primitives.
2. Each provider implements it and registers it (`TryAddEnumerable`). Delete the local probe interfaces: `IMessageBusProbe`, `IRedisConnectionProbe`, `IEncryptionKeyProviderProbe`, `IFileStorageHealthProbe`, `IWorkflowServiceProbe`, `ISchedulerServiceProbe`, `IAuditSealingProbe`, and the `ProbeAsync` on the search and vector provisioners (these keep their rich detail in `Data`).
3. ServiceDefaults base (the WO-084 rule changes to "Foundation only") gets `AddSharedKernelReadiness()`, which maps every registered probe to a `ready`-tagged health check. Delete the roughly 12 `*ReadinessHealthCheck` adapters.
4. Collapse `ServiceDefaults.*` packages whose only content was a probe adapter or telemetry wiring by name; the wiring moves into the base. Candidates: Scheduling, Workflows.Temporal, Storage, Search, AI, Messaging, Caching, Caching.Redis, Cryptography.KeyVault. Verify each package's contents before deleting it.
5. This deletes the 13→17 and 13→19 grants and their rule classes (`ServiceDefaultsSchedulingLayeringRules`, `ServiceDefaultsWorkflowLayeringRules`).

### Step 8: Optional-dependency satellites (P-570)
1. **Messaging.MassTransit:** keep the core. Add `.RabbitMq` (`UseRabbitMq`), `.AzureServiceBus` (`UseAzureServiceBus` + Azure.Identity) and `.EfCore` (outbox; `.EntityFrameworkCore` would exceed the 245-character path limit). The core drops the Azure, EF and RabbitMQ packages.
2. **Presentation:** new `Presentation.Core`, holding the authorization attributes (`RequireRole`/`RequirePermission`/`RequireFreshAuthentication`/`RequireAuthenticationMethod`) and the ErrorType status maps. WebApi and Grpc reference it, and Grpc no longer references WebApi.
3. **SignalR:** new `Presentation.SignalR.Redis` (`WithRedisBackplane`).
4. **GraphQL:** move `Communication.GraphQL` → `14.Presentation/SharedKernel.Presentation.GraphQL`.
5. **ServiceDefaults:** ServiceDefaults.Persistence drops its Encryption/Auditing references (their probes self-register since Step 7). The base keeps its EF Core and gRPC-client OpenTelemetry instrumentation: WO-084/P-531 measured that neither package depends on EF Core or gRPC, so moving them would shrink nothing.
6. Update the ShippingApi sample and the consumer-verify harnesses to the split packages.

### Step 9: Per-capability testing packages (P-571)
1. Split `SharedKernel.Testing` into packable `16.Testing/SharedKernel.{Capability}.Testing` packages. Each references only its capability's Abstractions (+ Foundation). Use `Persistence.Testing` as the template.
   - Packages: `SharedKernel.Testing` (core: FakeClock, InMemoryLogger, FakeRequestContext, fakers, assertions — now packable and lightweight), `.Application.Testing`, `.Caching.Testing`, `.Cryptography.Testing`, `.FeatureManagement.Testing`, `.Messaging.Testing`, `.Storage.Testing`, `.Search.Testing`, `.AI.Testing`, `.Security.Testing`, `.Workflows.Testing`, `.Scheduling.Testing`, `.Integration.Testing`, `.Reporting.Testing`, `.Idempotency.Testing`.
2. Container fixtures and internal helpers go to the non-packable `SharedKernel.Testing.Internal`.
3. Repoint every `.Tests` project. `TestingNeverReferencedByProduction` covers the new package names.
4. Resolve the CLAUDE.md contradiction about the package being "internal".

### Step 10: Build, release train and CI (P-572)
1. `release.yml`:
   - A `v*` tag runs the full Unit and Integration suites, the packaging-verify matrix and all samples, then packs and publishes **every** package at one version. The job fails if any package is missing.
2. `publish-package.yml` becomes dry-run only (pack + dependency gate). Remove the closure procedure from PLATFORM.md.
3. Update `Directory.Packages.props` for new, renamed and removed packages. Update the `.slnx` solution folders and both `.slnf` files, and the `Directory.Build.props` comment ("50 packages").
4. CI (`ci.yml`): wire in CatalogApi, the new satellites' harnesses and the OrderApi 4-project sample. Add a tier-check job that fails if the baseline is non-empty after Step 12.
5. Consumer documentation: one `SharedKernelVersion` property in the consumer's `Directory.Packages.props` (pattern documented in PLATFORM.md and the root README).
6. Fix the stale PLATFORM.md `global.json` text.

### Step 11: Samples as the reference architecture (P-573)
1. Split `samples/OrderApi` into `OrderApi.Domain` / `.Application` / `.Infrastructure` / `.Api`, referencing exactly the tiers described under "What a consuming service references". Add an arch test in the sample asserting that shape.
2. Update BillingApi, ShippingApi, DocumentsApi and CatalogApi to the new packages. Rewrite `samples/README.md` as the "how to consume the kernel" guide.

### Step 12: Governance cleanup (P-574)
1. Delete the numbered-layer rules that tier rules now cover: most of `SharedKernelLayeringRules`, `MessagingLayeringRules`, `CommunicationLayeringRules.*Application*`, and the persistence namespace lists. Keep the purity rules tier rules cannot express (Contracts ↛ Domain, Grpc ↛ Contracts, Domain logging-free), and wire all kept rules into tests so none is left unexecuted.
2. Retarget the analyzers with hard-coded names (SK0001, SK0028, SK0030, SK0042…) where the namespaces moved. Update `AnalyzerReleases.Unshipped.md`.
3. `eng/tier-baseline.txt` must be empty. Remove the baseline-downgrade mechanism so `SKTIER*` diagnostics are errors.

### Step 13: Documentation (P-575)
1. Root `CLAUDE.md`:
   - Folder Map: update the rows for 01, 05, 07, 11, 13, 14, 16 and 18.
   - Replace "Layering Rules" with a "Tiers & Dependency Rules" section.
   - Rewrite every affected "What Goes Where" row (context, tenant, idempotency, probes, mediator, pipeline, testing, transports).
   - Update the Abstractions table and "Solution Format".
   - Remove the banner.
   - Fix the stale publish claims.
2. `PLATFORM.md`: versioning, release train, CPM, CI topology, build configuration map.
3. For every touched domain (00, 01, 02, 05, 06, 07, 09, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20): update `CLAUDE.md`, the package READMEs and `state-map.md`. Fill in the empty READMEs for domains 10, 11, 14, 15 and 17.
4. Add a `CLAUDE.changelog.md` entry. Archive `P-558-SESSION-HANDOFF.md`. Write a migration note, `docs/refactor/MIGRATION.md` (old type → new type table).

### Step 14: Agents and commands (P-576)
1. `.claude/agents/*` (44 files):
   - Replace the numbered-layer rules with a tier section.
   - Update package names (Execution, Application contracts/Pipeline/Mediator, Idempotency.Abstractions, the satellites, the Testing packages).
   - Rewrite the MediatR guidance in the application-* agents: MediatR only in the adapter.
   - `arch-lead.md`: "no layering rule is violated" becomes "the tier check passes".
2. `.claude/commands/*` (28 files):
   - `dispatch-phase.md`: "lower numbers first" becomes tier order.
   - `sync-brain.md`: required layering section → tier section.
   - `commit.md` scopes; `implement-phase-*` and `implement-next-phase.md` tables for new packages.
3. Clean up stale `.claude/agent-memory/**` entries that name removed types. Update the user memory `project_shared_kernel.md`.

### Step 15: First release train (P-577) and retirement (P-578)
1. Merge, tag `v1.0.0-alpha.<n>` (confirm the exact tag with the user), and let `release.yml` publish everything. Verify every package restores from GitHub Packages in a clean consumer project.
2. Retire old package IDs on GitHub Packages (deprecate or unlist `SharedKernel.Application.Abstractions`, `.Application.Behaviors`, `.Behaviors.Caching`, `Communication.GraphQL`). **Ask the user before this outward-facing step.**
3. Close WO-086 in the state-maps.

### Ordering and parallelism
Order: 0 → 1 → 2 → {3, 7 in parallel} → 4 → {5, 8 in parallel} → 6 → 9 → 10 → 11 → 12 → 13 → 14 → 15.
- Step 6 needs Step 4 (accessor) and Step 5 (pipeline).
- Step 9 needs the final contracts from Steps 5–8.
- Docs and agents come last, as requested, because they describe the final state.

---

## Critical files
- **Build:** `Directory.Build.props`, `Directory.Build.targets` (tier target), `Directory.Packages.props`, `Platform.SharedKernel.slnx`, `Platform.SharedKernel.Unit.slnf`, `Platform.SharedKernel.Integration.slnf`, `.github/workflows/{ci,release,publish-package}.yml`, `.github/labeler.yml`
- **Contracts:** `05.Application/SharedKernel.Application.Abstractions/**` (moved), `05.Application/SharedKernel.Application/Messaging/*.cs`, `05.Application/SharedKernel.Application.Behaviors/Extensions/ApplicationBehaviorsBuilder.cs`, `.../Idempotency/IRequestIdempotencyStore.cs`, `07.Messaging/SharedKernel.Messaging.Abstractions/{Idempotency,TenantContext,Context}/*`, `12.Security/SharedKernel.Security.Abstractions/{IUserContext,ITenantProvider}.cs`, `03.Domain/SharedKernel.Domain/Abstractions/IHasTenant.cs`
- **Propagation:** `11.Communication/SharedKernel.Communication.Rest/Handlers/*`, `11.Communication/SharedKernel.Communication.Grpc/Interceptors/*`, `14.Presentation/SharedKernel.Presentation.WebApi/Idempotency/HttpContextIdempotencyExtensions.cs`, the correlation middleware, `13.ServiceDefaults/SharedKernel.ServiceDefaults.Security/RequestContext/SecurityRequestContext.cs`, `13.ServiceDefaults/SharedKernel.MultiTenancy/**`
- **Governance:** `00.Governance/SharedKernel.ArchitectureTests/Rules/*.cs` + `Predicates/*`, `PipelineOrderAssertion.cs`, `00.Governance/SharedKernel.Analyzers/Diagnostics/SK00{15,16,17,18,40,41}*.cs`, `MarkerInterfaceHelpers.cs`
- **Testing:** `16.Testing/SharedKernel.Testing/**` (split), `16.Testing/SharedKernel.Persistence.Testing` (template)
- **Docs and agents:** root and domain `CLAUDE.md`/`README.md`/`state-map.md`, `PLATFORM.md`, `.claude/agents/*.md`, `.claude/commands/*.md`

**Reuse, don't reinvent:**
- `ApplicationBehaviorsBuilder`'s stage ordering and `Build()` seam validation: keep the logic, retarget the types.
- The existing idempotency atomic Lua and `ON CONFLICT` implementations and their tests.
- `InboundRequestContextFilter` / `MessageRequestContext` as the model for inbound scopes.
- `Persistence.Testing` as the model for testing packages.
- `CompositionBaseIsolationTests` as the model for the tier project-file test.
- `WellKnownHeaders`/`WellKnownBaggageKeys` for all header names.

## Verification
Per step:
1. `dotnet build Platform.SharedKernel.slnx -c Release` with zero new warnings, and `SKTIER` baseline entries only decreasing.
2. `dotnet test Platform.SharedKernel.Unit.slnf`; Docker-backed `dotnet test Platform.SharedKernel.Integration.slnf` for steps touching persistence, messaging, caching or idempotency.
3. Local packaging-verify: `dotnet pack Platform.SharedKernel.slnx -o nupkgs` with a temporary `NUGET_PACKAGES`, then restore and run the affected consumer-verify harnesses and samples (OrderApi smoke run, BillingApi.Tests, ShippingApi.Tests, DocumentsApi.Tests, CatalogApi).

End of program:
- The tier baseline is empty and `SKTIER*` are errors.
- `grep -r "MediatR"` in production code finds only `SharedKernel.Application.Mediator.MediatR`.
- No production csproj references `Microsoft.AspNetCore.Http` outside the Host tier.
- The end-to-end propagation tests prove correlation, tenant and idempotency key survive HTTP→REST/gRPC→bus→consumer→job.
- A clean consumer project restores every package from GitHub Packages at the release-train version.
- `grep` for removed type names across docs, agents and commands returns nothing.
