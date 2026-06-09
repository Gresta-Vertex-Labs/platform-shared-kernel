# 07.Messaging — State Map

> **What this file is:** Phase and task tracker for all work within `07.Messaging`.
> **What it is not:** The root tracker — that lives at `state-map.md`.
> **Sync policy:** When all tasks under a Phase Key are `●`, run `/state-map-phase` with `phase_key: SK.07.{Phase}` to propagate that milestone to the root state-map.

---

## Legend

| Symbol | Meaning |
| --- | --- |
| `○` | Not started |
| `◐` | In progress |
| `●` | Complete |
| `⚑` | Blocked |
| `—` | N/A / Skipped |

---

## Phase Key Registry

> Phase keys are the sync bridge between this sub-state-map and the root `state-map.md`.
> Each key maps a local milestone to a root-level phase. When a key's Promotion Condition is met, the root is updated via `/state-map-phase`.

| Phase Key | Maps to Root Phase | Promotion Condition |
| --- | --- | --- |
| `SK.07.Design` | Design | All tasks in Phase: Design are `●` |
| `SK.07.Scaffold` | Scaffold | All tasks in Phase: Scaffold are `●` |
| `SK.07.Core` | Core | All tasks in Phase: Core are `●` |
| `SK.07.Tests` | Tests | All tasks in Phase: Tests are `●` |
| `SK.07.Docs` | Docs | All tasks in Phase: Docs are `●` |
| `SK.07.Published` | Published | All tasks in Phase: Published are `●` |
| `SK.07.Resilience` | Resilience | All tasks in Phase: Resilience are `●` |
| `SK.07.Scheduling` | Scheduling | All tasks in Phase: Scheduling are `●` |
| `SK.07.Saga` | Saga | All tasks in Phase: Saga are `●` |
| `SK.07.Batch` | Batch | All tasks in Phase: Batch are `●` |
| `SK.07.Routing` | Routing | All tasks in Phase: Routing are `●` |
| `SK.07.OTel` | OTel | All tasks in Phase: OTel are `●` |

---

## Active Work

_Nothing in progress._

<!--
Format when active — replace placeholder with table:
| Task | Phase Key | Package | State |
|------|-----------|---------|:-----:|
| Implement IMessageBus | SK.07.Core | SharedKernel.Messaging.Abstractions | ◐ |
-->

---

## Blocked

_No blockers._

<!--
Format when blocked — replace placeholder with table:
| Task | Phase Key | Blocker |
|------|-----------|---------|
| Example blocked task | SK.07.Core | Waiting on upstream decision |
-->

---

## Package Board

| Package | Current Phase | State | Notes |
| --- | --- | --- | --- |
| `SharedKernel.Messaging.Abstractions` | Published | `●` | NuGet metadata set; nupkgs/SharedKernel.Messaging.Abstractions.1.0.0.{nupkg,snupkg} verified; XML docs embedded; 33 tests pass |
| `SharedKernel.Messaging.MassTransit` | Published | `●` | NuGet metadata set; nupkgs/SharedKernel.Messaging.MassTransit.1.0.0.{nupkg,snupkg} verified; XML docs embedded; 43 tests pass |

---

## Cross-Domain Dependencies

| This Phase Key | Needs From Domain | What | Status |
| --- | --- | --- | --- |
| `SK.07.Scaffold` | `01.Core` | `SharedKernel.Primitives` ProjectReference (`Error`, `IClock`) | Available |
| `SK.07.Core` | `04.Contracts` | `SharedKernel.Contracts` ProjectReference (`EventEnvelope<TEvent>` — used in `MassTransitEventPublisher`) | Available |
| `SK.07.Core` | `03.Domain` | `DomainEventVersionHelper.GetVersion(Type)` — used to populate `SchemaVersion` in published envelope | Available |
| `SK.07.Saga` | `06.Persistence` (consuming service) | `TDbContext : DbContext` generic parameter for `WithEntityFrameworkSagaRepository` — no compile-time reference; consuming service bridges at composition root | Available (pattern mirrored from outbox) |

---

## Phase: Design <!-- phase-key: SK.07.Design -->

> Finalize all interface shapes, builder API, transport option contracts, outbox wiring, CloudEvents envelope mapping, and consumer base semantics before implementation begins.

| ID | Task | Work Order | Package(s) | State |
| --- | --- | --- | --- | --- |
| D-01 | Define IMessageBus, IEventPublisher, PublishContext, IMessagingBuilder, MessagingOptions contracts in CLAUDE.md | WO-020 | SharedKernel.Messaging.Abstractions | `●` |
| D-02 | Define `ConsumerBase<T>`, MassTransitMessageBus, MassTransitEventPublisher contracts in CLAUDE.md | WO-020 | SharedKernel.Messaging.MassTransit | `●` |
| D-03 | Define MessagingBusBuilder API, RabbitMqBusOptions, AzureServiceBusOptions, RetryOptions, OutboxOptions in CLAUDE.md | WO-020 | SharedKernel.Messaging.MassTransit | `●` |
| D-04 | Define CloudEvents envelope mapping rules (CorrelationId, CausationId, SourceService, SchemaVersion, TimestampUtc) | WO-020 | SharedKernel.Messaging.MassTransit | `●` |
| D-05 | Define consumer endpoint naming convention (kebab-case with ServiceName prefix) | WO-020 | SharedKernel.Messaging.MassTransit | `●` |
| D-06 | Define outbox ownership boundary (07.Messaging owns outbox; 06.Persistence must not define any outbox types) | WO-020 | SharedKernel.Messaging.MassTransit | `●` |
| D-07 | Define test rules: TestHarness for in-memory integration, SQLite for outbox unit tests, Testcontainers for broker integration | WO-020 | SharedKernel.Messaging.Abstractions | `●` |
| D-08 | Document hard violation rules (no raw IBus injection, no IEventPublisher in domain, no singleton registration, no hardcoded queue URIs) | WO-020 | SharedKernel.Messaging.MassTransit | `●` |
| D-09 | Write root state-map.md phases P-115 through P-121 (WO-020) covering all Messaging domain work | WO-020 | SharedKernel.Messaging.Abstractions | `●` |
| D-10 | Design `IFaultConsumer<TMessage>` contract — method signature, context members (FaultId, FaultTimestamp, FaultedMessage, Exceptions), zero-dependency constraint | WO-021 | SharedKernel.Messaging.Abstractions | `●` |
| D-11 | Design `FaultExceptionInfo` sealed record — ExceptionType (string), Message (string); value equality; AOT-safe (record struct vs record class decision) | WO-021 | SharedKernel.Messaging.Abstractions | `●` |
| D-12 | Design `CircuitBreakerOptions` sealed POCO — TripThreshold, ActiveThreshold, ResetInterval, TrackingPeriod, SectionName constant; mapping to MassTransit UseCircuitBreaker fields | WO-021 | SharedKernel.Messaging.Abstractions | `●` |
| D-13 | Design `FaultConsumerAdapter<TMessage, TFaultConsumer>` internal adapter — how `IConsumer<Fault<TMessage>>` maps to `IFaultConsumer<TMessage>`; Exceptions array translation; ConsumerBase logging contract parity | WO-021 | SharedKernel.Messaging.MassTransit | `●` |
| D-14 | Design `WithCircuitBreaker()` builder method — retry-inner/circuit-breaker-outer ordering rule; interaction with existing `WithRetry()` call; global-only policy (no per-consumer override via IConsumerDefinition) | WO-021 | SharedKernel.Messaging.MassTransit | `●` |
| D-15 | Design `IMessageScheduler` interface contract — `ScheduleAsync<T>` returns Guid token; CancelAsync no-op semantics; zero-dependency in Abstractions | WO-021 | SharedKernel.Messaging.Abstractions | `●` |
| D-16 | Design `SchedulingOptions` POCO — Provider enum (InMemory/Quartz/Hangfire), SectionName; `QuartzSchedulerOptions` sub-options (ConnectionString, Schema) | WO-021 | SharedKernel.Messaging.Abstractions | `●` |
| D-17 | Design `MassTransitMessageScheduler` internal implementation — token-to-MassTransit-ScheduledMessage.TokenId mapping; scoped lifetime enforcement | WO-021 | SharedKernel.Messaging.MassTransit | `●` |
| D-18 | Design `SagaStateBase` abstract record — CorrelationId, CurrentState, CreatedAt, UpdatedAt, Version (ISagaVersion); EF Core mappability; lives in MassTransit package (ISagaVersion dependency) | WO-021 | SharedKernel.Messaging.MassTransit | `●` |
| D-19 | Design `SagaStateMachineBase<TSaga>` thin wrapper — which protected helper surface to expose; which advanced MassTransit APIs pass-through vs hide; explicit "thin ergonomic wrapper" caveat | WO-021 | SharedKernel.Messaging.MassTransit | `●` |
| D-20 | Design `BatchConsumerBase<TMessage>` — `IConsumer<Batch<TMessage>>` implementation contract; `ConsumeAsync(IReadOnlyList<TMessage>, CancellationToken)`; batch-level CorrelationId logging; exception rethrow rule | WO-021 | SharedKernel.Messaging.MassTransit | `●` |
| D-21 | Design `BatchOptions` POCO — MessageLimit, TimeLimit, ConcurrencyLimit defaults; SectionName; mapping to MassTransit batch configurator | WO-021 | SharedKernel.Messaging.Abstractions | `●` |
| D-22 | Design Build() anti-pattern fix — how captured `Action<MessagingOptions>?` delegate avoids BuildServiceProvider(); sentinel/deferred path for config-section binding; ValidateOnStart() interplay | WO-021 | SharedKernel.Messaging.MassTransit | `●` |
| D-23 | Design `ISendEndpointResolver` interface — `Resolve<T>()` → string; ConventionSendEndpointResolver default; per-type route dictionary shape; `WithSendEndpointRoute<T>(string)` builder method | WO-021 | SharedKernel.Messaging.Abstractions | `●` |

---

## Phase: Scaffold <!-- phase-key: SK.07.Scaffold -->

> Create project files, solution folder registrations, directory structure, and empty stub test files.

| ID | Task | Work Order | Package(s) | State |
| --- | --- | --- | --- | --- |
| S-01 | Create `SharedKernel.Messaging.Abstractions.csproj` — net10.0, ImplicitUsings, Nullable, DI.Abstractions reference only | WO-020 | SharedKernel.Messaging.Abstractions | `●` |
| S-02 | Create `SharedKernel.Messaging.MassTransit.csproj` — net10.0, all required package references (MassTransit 8.x, RabbitMQ, ASB, EfCoreIntegration, EfCore, Logging.Abstractions) | WO-020 | SharedKernel.Messaging.MassTransit | `●` |
| S-03 | Scaffold `SharedKernel.Messaging.Abstractions.Tests.csproj` — nested inside Abstractions project folder; standard test package set; `GlobalUsings.cs` with `global using Xunit;` | WO-020 | SharedKernel.Messaging.Abstractions | `●` |
| S-04 | Scaffold `SharedKernel.Messaging.MassTransit.Tests.csproj` — nested inside MassTransit project folder; standard test package set + `MassTransit.Testing` 8.x; `GlobalUsings.cs` with `global using Xunit;` | WO-020 | SharedKernel.Messaging.MassTransit | `●` |
| S-05 | Register all 4 projects in `Platform.SharedKernel.slnx` under `07.Messaging` solution folder | WO-020 | SharedKernel.Messaging.Abstractions | `●` |
| S-06 | Verify `dotnet build` on solution succeeds with zero errors and zero warnings after scaffolding | WO-020 | SharedKernel.Messaging.Abstractions | `●` |
| S-07 | Add `MassTransit.Quartz` 9.1.2 conditional NuGet reference to `SharedKernel.Messaging.MassTransit.csproj` for `WithQuartzScheduler()` wiring (P-127) | WO-021 | SharedKernel.Messaging.MassTransit | `●` |
| S-08 | Verify no new NuGet references added to `SharedKernel.Messaging.Abstractions.csproj` after P-125 and P-127 additions — still zero transport dependencies | WO-021 | SharedKernel.Messaging.Abstractions | `●` |

---

## Phase: Core <!-- phase-key: SK.07.Core -->

> Implement all production types: IMessageBus, IEventPublisher, PublishContext, MessagingOptions, IMessagingBuilder, ConsumerBase\<T\>, MassTransitMessageBus, MassTransitEventPublisher, MessagingBusBuilder, transport adapters, retry, and outbox wiring.

| ID | Task | Work Order | Package(s) | State |
| --- | --- | --- | --- | --- |
| C-01 | Implement `IMessageBus` interface with all four methods and XML docs (P-116) | WO-020 | SharedKernel.Messaging.Abstractions | `●` |
| C-02 | Implement `IEventPublisher` interface with both publish overloads and XML docs including domain-event dispatcher distinction (P-116) | WO-020 | SharedKernel.Messaging.Abstractions | `●` |
| C-03 | Implement `PublishContext` sealed class — three properties, three fluent methods, null/empty key guard on WithHeader (P-116) | WO-020 | SharedKernel.Messaging.Abstractions | `●` |
| C-04 | Implement `MessagingOptions` sealed POCO — ServiceName required, Options validation throws OptionsValidationException on null/whitespace (P-116) | WO-020 | SharedKernel.Messaging.Abstractions | `●` |
| C-05 | Implement `IMessagingBuilder` interface with Services property (P-116) | WO-020 | SharedKernel.Messaging.Abstractions | `●` |
| C-06 | Implement `AddSharedKernelMessaging` IServiceCollection extension — registers MessagingOptions, returns MessagingBusBuilder (P-116) | WO-020 | SharedKernel.Messaging.Abstractions | `●` |
| C-07 | Implement `ConsumerBase<TMessage>` abstract class — sealed Consume entry, ConsumeAsync abstract, CorrelationId propagation, exception log-then-rethrow, protected ILogger (P-117) | WO-020 | SharedKernel.Messaging.MassTransit | `●` |
| C-08 | Implement `MassTransitMessageBus` — all four IMessageBus methods; publish delegates to IPublishEndpoint; send resolves endpoint by convention; request uses `IRequestClient<TRequest>` (P-117) | WO-020 | SharedKernel.Messaging.MassTransit | `●` |
| C-09 | Implement `MassTransitEventPublisher` — wraps TEvent in `EventEnvelope<TEvent>`; populates all five envelope fields; delegates to IPublishEndpoint (P-117) | WO-020 | SharedKernel.Messaging.MassTransit | `●` |
| C-10 | Implement `RabbitMqBusOptions` sealed class — Host, Username, Password, VirtualHost, Prefetch, RequestedHeartbeat with documented defaults (P-117) | WO-020 | SharedKernel.Messaging.MassTransit | `●` |
| C-11 | Implement `MessagingBusBuilder` — UseRabbitMq(string) and UseRabbitMq(Action) overloads; KebabCaseEndpointNameFormatter with ServiceName prefix (P-117) | WO-020 | SharedKernel.Messaging.MassTransit | `●` |
| C-12 | Implement `MessagingBusBuilder.Build()` — registers IMessageBus, IEventPublisher, MassTransit bus types, IHostedService bus lifecycle; throws InvalidOperationException if no transport (P-117) | WO-020 | SharedKernel.Messaging.MassTransit | `●` |
| C-13 | Implement `AzureServiceBusOptions` — ConnectionString?, FullyQualifiedNamespace?, MaxConcurrentCalls, TransportType; mutual-exclusion validation (P-118) | WO-020 | SharedKernel.Messaging.MassTransit | `●` |
| C-14 | Implement `MessagingBusBuilder.UseAzureServiceBus` overloads — string and Action; DefaultAzureCredential when FullyQualifiedNamespace set; transport exclusivity guard (P-118) | WO-020 | SharedKernel.Messaging.MassTransit | `●` |
| C-15 | Implement `MessagingBusBuilder.AddConsumer<TConsumer>()` and `AddConsumer<TConsumer, TDefinition>()` (P-118) | WO-020 | SharedKernel.Messaging.MassTransit | `●` |
| C-16 | Implement `RetryOptions` sealed class — Attempts, InitialInterval, IntervalIncrement, MaxInterval, ImmediateAttempts with documented defaults (P-119) | WO-020 | SharedKernel.Messaging.MassTransit | `●` |
| C-17 | Implement `MessagingBusBuilder.WithRetry()` — wires MassTransit UseRetry with incremental back-off from RetryOptions; ImmediateAttempts before interval retries (P-119) | WO-020 | SharedKernel.Messaging.MassTransit | `●` |
| C-18 | Implement `OutboxOptions` sealed class — BatchSize, QueryDelay, DuplicateDetectionWindow with documented defaults (P-119) | WO-020 | SharedKernel.Messaging.MassTransit | `●` |
| C-19 | Implement `MessagingBusBuilder.WithEntityFrameworkOutbox<TDbContext>()` — calls MassTransit AddEntityFrameworkOutbox; constraint is DbContext only; no SharedKernel.Persistence.* reference; XML doc migration warning (P-119) | WO-020 | SharedKernel.Messaging.MassTransit | `●` |
| C-20 | Verify `dotnet build` clean for both packages; no compile errors; no SharedKernel.Persistence.* reference in MassTransit csproj | WO-020 | SharedKernel.Messaging.MassTransit | `●` |
| C-21 | Fix `MessagingBusBuilder.Build()` — remove `Services.BuildServiceProvider()` call; capture `Action<MessagingOptions>?` delegate at `AddSharedKernelMessaging` time; instantiate `MessagingOptions`, apply delegate, call `MessagingOptionsValidator.Validate()` inline (P-130) | WO-021 | SharedKernel.Messaging.MassTransit | `●` |
| C-22 | Populate `KebabCaseEndpointNameFormatter` prefix from captured/validated `ServiceName` at Build() time — no DI container required (P-130) | WO-021 | SharedKernel.Messaging.MassTransit | `●` |
| C-23 | Implement deferred-validation path in Build() — when MessagingOptions was configured via `services.Configure<T>(section)` and no inline action was supplied, Build() does not throw; logs a warning that naming prefix cannot be validated at build time; ValidateOnStart() handles the deferred check (P-130) | WO-021 | SharedKernel.Messaging.MassTransit | `●` |
| C-24 | Verify `dotnet build` clean after P-130 fix; all existing MessagingBusBuilderGuardTests pass; no duplicate singleton registration side-effects | WO-021 | SharedKernel.Messaging.MassTransit | `●` |

---

## Phase: Tests <!-- phase-key: SK.07.Tests -->

> Unit and integration tests for all packages. IMessageBus/IEventPublisher mocks, ConsumerBase retry/error semantics, outbox round-trip, CloudEvents envelope validation, MessagingBusBuilder guard tests.

| ID | Task | Work Order | Package(s) | State |
| --- | --- | --- | --- | --- |
| T-01 | PublishContext fluent builder tests — WithCorrelationId, WithCausationId, WithHeader; null/empty key ArgumentException; duplicate key overwrite (P-120) | WO-020 | SharedKernel.Messaging.Abstractions | `●` |
| T-02 | MessagingOptions validation tests — OptionsValidationException on null ServiceName; OptionsValidationException on whitespace ServiceName (P-120) | WO-020 | SharedKernel.Messaging.Abstractions | `●` |
| T-03 | AddSharedKernelMessaging DI test — IMessagingBuilder returned; MessagingOptions resolvable from DI (P-120) | WO-020 | SharedKernel.Messaging.Abstractions | `●` |
| T-04 | IMessageBus NSubstitute mock tests — `PublishAsync<T>` called once with correct type; `SendAsync<T>` called once (P-120) | WO-020 | SharedKernel.Messaging.MassTransit | `●` |
| T-05 | IEventPublisher NSubstitute mock tests — `PublishAsync<TEvent>` called with correct integration event type (P-120) | WO-020 | SharedKernel.Messaging.MassTransit | `●` |
| T-06 | MessagingBusBuilder guard tests — Build() throws with no transport; UseRabbitMq+UseAzureServiceBus throws at second call; ASB both/neither options throws (P-120) | WO-020 | SharedKernel.Messaging.MassTransit | `●` |
| T-07 | `ConsumerBase<TMessage>` TestHarness tests — correct message delivery; exception propagation without swallowing; fault published on exception (P-120) | WO-020 | SharedKernel.Messaging.MassTransit | `●` |
| T-08 | CloudEvents envelope tests — all five fields verified: SourceService, CorrelationId non-empty, SchemaVersion, TimestampUtc recent (P-120) | WO-020 | SharedKernel.Messaging.MassTransit | `●` |
| T-09 | Consumer endpoint naming convention test — queue name is {service-name}-{consumer-type} kebab-case (P-120) | WO-020 | SharedKernel.Messaging.MassTransit | `●` |
| T-10 | RequestAsync<TRequest, TResponse> timeout test — no responder registered; cancellation token expires; does not hang (P-120) | WO-020 | SharedKernel.Messaging.MassTransit | `●` |
| T-11 | Retry policy tests — 3-attempt incremental back-off scenario; ImmediateAttempts fast-retry scenario (P-120) | WO-020 | SharedKernel.Messaging.MassTransit | `●` |
| T-12 | Outbox round-trip test on SQLite — row inserted before SaveChangesAsync; delivered after commit; harness.Consumed contains event (P-120) | WO-020 | SharedKernel.Messaging.MassTransit | `●` |
| T-13 | RabbitMQ Testcontainers integration test [Trait("Category","Integration")] — end-to-end publish and consume across real broker (P-120) | WO-020 | SharedKernel.Messaging.MassTransit | `●` |
| T-14 | Verify `dotnet test` exits 0; zero compilation warnings; integration tests can be skipped in unit-only CI | WO-020 | SharedKernel.Messaging.MassTransit | `●` |
| T-15 | CircuitBreakerOptions default values test — instantiate with no config; assert TripThreshold=5, ActiveThreshold=10, ResetInterval=60s, TrackingPeriod=60s (P-125) | WO-021 | SharedKernel.Messaging.Abstractions | `●` |
| T-16 | FaultExceptionInfo record equality test — two records with same ExceptionType+Message are equal; different values are not equal; with-expression produces new instance (P-125) | WO-021 | SharedKernel.Messaging.Abstractions | `●` |
| T-17 | Build() second-root-provider test — call Build() after AddSharedKernelMessaging; assert no "second root IServiceProvider" diagnostic; assert IMessageBus and IEventPublisher resolve to same instances in the real container (P-130) | WO-021 | SharedKernel.Messaging.MassTransit | `●` |

---

## Phase: Docs <!-- phase-key: SK.07.Docs -->

> Ensure all public types carry XML doc comments. Update CLAUDE.md with any implementation-phase discoveries. Write README.md for each package.

| ID | Task | Work Order | Package(s) | State |
| --- | --- | --- | --- | --- |
| DO-01 | XML docs on IMessageBus — all four methods; RequestAsync `<remarks>` must include temporal-coupling and timeout-CancellationToken warning (P-121) | WO-020 | SharedKernel.Messaging.Abstractions | `●` |
| DO-02 | XML docs on IEventPublisher — both overloads; `<remarks>` must include domain-event dispatcher distinction (P-121) | WO-020 | SharedKernel.Messaging.Abstractions | `●` |
| DO-03 | XML docs on PublishContext — all three properties and three fluent methods (P-121) | WO-020 | SharedKernel.Messaging.Abstractions | `●` |
| DO-04 | XML docs on MessagingOptions, IMessagingBuilder, AddSharedKernelMessaging extension (P-121) | WO-020 | SharedKernel.Messaging.Abstractions | `●` |
| DO-05 | XML docs on `ConsumerBase<TMessage>` — ConsumeAsync remarks must state exceptions must not be swallowed (P-121) | WO-020 | SharedKernel.Messaging.MassTransit | `●` |
| DO-06 | XML docs on MessagingBusBuilder — all fluent methods; WithEntityFrameworkOutbox `<remarks>` must include migration prerequisite warning (P-121) | WO-020 | SharedKernel.Messaging.MassTransit | `●` |
| DO-07 | XML docs on all Options classes — RabbitMqBusOptions, AzureServiceBusOptions, RetryOptions, OutboxOptions (P-121) | WO-020 | SharedKernel.Messaging.MassTransit | `●` |
| DO-08 | Update CLAUDE.md changelog with implementation-phase discoveries (if any corrections or additions were made) (P-121) | WO-020 | SharedKernel.Messaging.Abstractions | `●` |
| DO-09 | XML docs on all P-125 types — `IFaultConsumer<TMessage>`, FaultExceptionInfo, CircuitBreakerOptions; remarks on IFaultConsumer must note registration via AddFaultConsumer only (P-125/P-126) | WO-021 | SharedKernel.Messaging.Abstractions | `●` |
| DO-10 | XML docs on all P-127 types — IMessageScheduler (both methods), SchedulingOptions, QuartzSchedulerOptions; ScheduleAsync `<remarks>` must warn in-memory tokens do not survive restart (P-127) | WO-021 | SharedKernel.Messaging.Abstractions | `●` |
| DO-11 | XML docs on all P-128 types — SagaStateBase, `SagaStateMachineBase<TSaga>`; remarks on SagaStateMachineBase must document thin-wrapper caveat and direct MassTransit reference guidance for advanced features (P-128) | WO-021 | SharedKernel.Messaging.MassTransit | `●` |
| DO-12 | XML docs on all P-129 types — `BatchConsumerBase<TMessage>`, BatchOptions, AddBatchConsumer; remarks on ConsumeAsync must state exceptions are logged and rethrown (P-129) | WO-021 | SharedKernel.Messaging.MassTransit | `●` |
| DO-13 | XML docs on all P-131 types — ISendEndpointResolver, `WithSendEndpointRoute<T>`; remarks on WithSendEndpointRoute must state the hardcoded-URI violation rule (P-131) | WO-021 | SharedKernel.Messaging.Abstractions | `●` |
| DO-14 | XML docs on all new builder methods — WithCircuitBreaker, AddFaultConsumer, WithInMemoryScheduler, WithQuartzScheduler, AddSaga, WithEntityFrameworkSagaRepository, AddBatchConsumer, WithSendEndpointRoute (P-126–P-131) | WO-021 | SharedKernel.Messaging.MassTransit | `●` |

---

## Phase: Published <!-- phase-key: SK.07.Published -->

> Set NuGet metadata, pack, verify manifests, and publish to internal feed.

| ID | Task | Work Order | Package(s) | State |
| --- | --- | --- | --- | --- |
| P-01 | Set NuGet metadata on SharedKernel.Messaging.Abstractions.csproj — PackageId, Version 1.0.0, Authors, Description, PackageTags, PackageLicenseExpression MIT, GenerateDocumentationFile, IncludeSymbols, SymbolPackageFormat snupkg (P-121) | WO-020 | SharedKernel.Messaging.Abstractions | `●` |
| P-02 | Set NuGet metadata on SharedKernel.Messaging.MassTransit.csproj — same required fields as P-01 (P-121) | WO-020 | SharedKernel.Messaging.MassTransit | `●` |
| P-03 | Run `dotnet pack` for SharedKernel.Messaging.Abstractions; verify .nupkg and .snupkg land in nupkgs/; verify XML docs embedded (P-121) | WO-020 | SharedKernel.Messaging.Abstractions | `●` |
| P-04 | Run `dotnet pack` for SharedKernel.Messaging.MassTransit; verify .nupkg and .snupkg land in nupkgs/; verify XML docs embedded (P-121) | WO-020 | SharedKernel.Messaging.MassTransit | `●` |
| P-05 | Write consumer-verify test — instantiate AddSharedKernelMessaging from Abstractions in isolation (no MassTransit reference); verify IMessagingBuilder resolves (P-121) | WO-020 | SharedKernel.Messaging.Abstractions | `●` |

---

## Phase: Resilience <!-- phase-key: SK.07.Resilience -->

> Circuit breaker and fault consumer contracts for structured dead-letter handling and sustained-failure protection. Covers P-125 (Abstractions) and P-126 (MassTransit).

| ID | Task | Work Order | Package(s) | State |
| --- | --- | --- | --- | --- |
| R-01 | Implement `FaultExceptionInfo` sealed record — `ExceptionType` (string), `Message` (string); value equality via record semantics; full XML docs; lives in `Faults/` folder (P-125) | WO-021 | SharedKernel.Messaging.Abstractions | `●` |
| R-02 | Implement `IFaultConsumer<TMessage>` interface — `HandleAsync(FaultId, FaultTimestamp, FaultedMessage, Exceptions, CancellationToken)` contract; XML docs including registration-via-AddFaultConsumer note (P-125) | WO-021 | SharedKernel.Messaging.Abstractions | `●` |
| R-03 | Implement `CircuitBreakerOptions` sealed POCO — `SectionName = "SharedKernel:Messaging:CircuitBreaker"`; TripThreshold=5, ActiveThreshold=10, ResetInterval=60s, TrackingPeriod=60s defaults; full XML docs (P-125) | WO-021 | SharedKernel.Messaging.Abstractions | `●` |
| R-04 | Implement internal `FaultConsumerAdapter<TMessage, TFaultConsumer>` class — implements `IConsumer<Fault<TMessage>>`; maps `Fault<TMessage>.Exceptions` array to `FaultExceptionInfo[]`; propagates CorrelationId; applies ConsumerBase-equivalent log-then-rethrow semantics; NOT a subclass of ConsumerBase (P-126) | WO-021 | SharedKernel.Messaging.MassTransit | `●` |
| R-05 | Implement `MessagingBusBuilder.WithCircuitBreaker(Action<CircuitBreakerOptions>? configure = null)` — reads `CircuitBreakerOptions`, calls MassTransit `UseCircuitBreaker` with `tripThreshold`, `activeThreshold`, `resetInterval`, `trackingPeriod`; when both `WithRetry()` and `WithCircuitBreaker()` are called, retry is registered inner (first), circuit breaker outer (second); returns `MessagingBusBuilder` for fluent chaining (P-126) | WO-021 | SharedKernel.Messaging.MassTransit | `●` |
| R-06 | Implement `MessagingBusBuilder.AddFaultConsumer<TMessage, TFaultConsumer>()` — registers internal `FaultConsumerAdapter<TMessage, TFaultConsumer>` for `Fault<TMessage>`; TFaultConsumer must implement `IFaultConsumer<TMessage>`; returns `MessagingBusBuilder` (P-126) | WO-021 | SharedKernel.Messaging.MassTransit | `●` |
| R-07 | TestHarness circuit breaker test — configure consumer that always throws; assert circuit opens after TripThreshold consecutive failures; assert subsequent messages receive `CircuitBreakerException` without invoking consumer body (P-126) | WO-021 | SharedKernel.Messaging.MassTransit | `●` |
| R-08 | TestHarness fault consumer test — wire AddFaultConsumer; publish message that causes unhandled exception past retry budget; assert fault consumer HandleAsync invoked with correct FaultId and FaultedMessage payload (P-126) | WO-021 | SharedKernel.Messaging.MassTransit | `●` |
| R-09 | Verify `dotnet build` clean; `dotnet test` passes; zero new NuGet references added to SharedKernel.Messaging.Abstractions.csproj (P-125/P-126) | WO-021 | SharedKernel.Messaging.Abstractions | `●` |

---

## Phase: Scheduling <!-- phase-key: SK.07.Scheduling -->

> Deferred message delivery via `IMessageScheduler` abstraction and MassTransit scheduler integrations. Covers P-127.

| ID | Task | Work Order | Package(s) | State |
| --- | --- | --- | --- | --- |
| SC-01 | Implement `SchedulingProvider` enum — values: `InMemory`, `Quartz`, `Hangfire`; default `InMemory` (P-127) | WO-021 | SharedKernel.Messaging.Abstractions | `●` |
| SC-02 | Implement `SchedulingOptions` sealed POCO — `SectionName = "SharedKernel:Messaging:Scheduling"`; `Provider` (SchedulingProvider, default InMemory); full XML docs (P-127) | WO-021 | SharedKernel.Messaging.Abstractions | `●` |
| SC-03 | Implement `QuartzSchedulerOptions` sealed POCO (nested within Scheduling namespace) — `ConnectionString` (required string), `Schema` (string, default "quartz"); full XML docs (P-127) | WO-021 | SharedKernel.Messaging.Abstractions | `●` |
| SC-04 | Implement `IMessageScheduler` interface — `ScheduleAsync<T>(T message, DateTimeOffset deliverAt, CancellationToken ct) → Task<Guid>`; `CancelAsync(Guid scheduleToken, CancellationToken ct) → Task`; zero NuGet dependencies in Abstractions; full XML docs including process-restart durability caveat on ScheduleAsync (P-127) | WO-021 | SharedKernel.Messaging.Abstractions | `●` |
| SC-05 | Implement internal `MassTransitMessageScheduler` sealed class in MassTransit package — delegates `ScheduleAsync` to MassTransit's `IMessageScheduler` (via `MessageSchedulerExtensions`); maps returned `ScheduledMessage.TokenId` to `Guid` token; `CancelAsync` calls `CancelScheduledSend` if available (P-127) | WO-021 | SharedKernel.Messaging.MassTransit | `●` |
| SC-06 | Implement `MessagingBusBuilder.WithInMemoryScheduler()` — calls MassTransit `cfg.UseInMemoryScheduler()`; registers `IMessageScheduler → MassTransitMessageScheduler` as scoped; XML doc warns tokens do not survive process restart (P-127) | WO-021 | SharedKernel.Messaging.MassTransit | `●` |
| SC-07 | Implement `MessagingBusBuilder.WithQuartzScheduler(Action<QuartzSchedulerOptions>? configure = null)` — wires `MassTransit.Quartz` integration; reads `QuartzSchedulerOptions` for ConnectionString and Schema; registers `IMessageScheduler → MassTransitMessageScheduler` as scoped; throws at Build() if ConnectionString is null/empty (P-127) | WO-021 | SharedKernel.Messaging.MassTransit | `●` |
| SC-08 | TestHarness scheduling test — wire `WithInMemoryScheduler()`; schedule a message via `IMessageScheduler.ScheduleAsync` with a near-future `deliverAt`; advance TestHarness time; assert message consumed after `deliverAt` elapsed (P-127) | WO-021 | SharedKernel.Messaging.MassTransit | `●` |
| SC-09 | TestHarness cancel test — schedule a message; call `CancelAsync` before `deliverAt`; assert message never consumed (P-127) | WO-021 | SharedKernel.Messaging.MassTransit | `●` |
| SC-10 | Verify `dotnet build` clean; zero new NuGet references added to `SharedKernel.Messaging.Abstractions.csproj` (P-127) | WO-021 | SharedKernel.Messaging.Abstractions | `●` |

---

## Phase: Saga <!-- phase-key: SK.07.Saga -->

> Saga state machine base for durable orchestration workflows. Covers P-128.

| ID | Task | Work Order | Package(s) | State |
| --- | --- | --- | --- | --- |
| SA-01 | Implement `SagaStateBase` abstract record — `CorrelationId` (Guid), `CurrentState` (string), `CreatedAt` (DateTimeOffset), `UpdatedAt` (DateTimeOffset); implements `ISagaVersion` from MassTransit (`Version` int); EF Core-mappable with no EF attributes on the record itself; full XML docs (P-128) | WO-021 | SharedKernel.Messaging.MassTransit | `●` |
| SA-02 | Implement `SagaStateMachineBase<TSaga>` abstract class — derives from MassTransit `MassTransitStateMachine<TSaga> where TSaga : SagaStateBase`; protected helpers: `TransitionTo(State state)`, `Finalize()`; explicit XML remarks documenting thin-wrapper nature and direct MassTransit reference guidance for advanced features (P-128) | WO-021 | SharedKernel.Messaging.MassTransit | `●` |
| SA-03 | Implement `MessagingBusBuilder.AddSaga<TSaga>()` — registers saga by type; default in-memory saga repository; returns `MessagingBusBuilder` for fluent chaining (P-128) | WO-021 | SharedKernel.Messaging.MassTransit | `●` |
| SA-04 | Implement `MessagingBusBuilder.AddSaga<TSaga, TDefinition>()` — companion overload accepting a saga definition equivalent to `ISagaDefinition<TSaga>`; registers with custom endpoint/retry configuration (P-128) | WO-021 | SharedKernel.Messaging.MassTransit | `●` |
| SA-05 | Implement `MessagingBusBuilder.WithEntityFrameworkSagaRepository<TDbContext, TSaga>()` — wires MassTransit EF Core saga repository; `TDbContext : DbContext` constraint only — no compile-time reference to `SharedKernel.Persistence.*`; XML doc notes consuming service must add saga state entity to DbContext and run EF migrations (P-128) | WO-021 | SharedKernel.Messaging.MassTransit | `●` |
| SA-06 | TestHarness saga test — define minimal two-state saga (Initial → Active → Final) triggered by two events; publish both events in sequence; assert saga instance reaches Final state; saga state is inspectable via harness (P-128) | WO-021 | SharedKernel.Messaging.MassTransit | `●` |
| SA-07 | Verify no `SharedKernel.Persistence.*` ProjectReference added to MassTransit csproj; `dotnet build` clean (P-128) | WO-021 | SharedKernel.Messaging.MassTransit | `●` |

---

## Phase: Batch <!-- phase-key: SK.07.Batch -->

> Batch consumer base for high-throughput multi-message processing. Covers P-129.

| ID | Task | Work Order | Package(s) | State |
| --- | --- | --- | --- | --- |
| B-01 | Implement `BatchOptions` sealed POCO — `SectionName = "SharedKernel:Messaging:Batch"`; `MessageLimit` (int, default 10), `TimeLimit` (TimeSpan, default 1s), `ConcurrencyLimit` (int, default 1); full XML docs (P-129) | WO-021 | SharedKernel.Messaging.Abstractions | `●` |
| B-02 | Implement `BatchConsumerBase<TMessage>` abstract class — implements `IConsumer<Batch<TMessage>>`; `Consume(ConsumeContext<Batch<TMessage>>)` is the sealed entry point; forwards to abstract `ConsumeAsync(IReadOnlyList<TMessage> messages, CancellationToken ct)`; structured log at batch entry with batch size and batch-level CorrelationId; unhandled exceptions from `ConsumeAsync` are logged at Error level then rethrown (P-129) | WO-021 | SharedKernel.Messaging.MassTransit | `●` |
| B-03 | Implement `MessagingBusBuilder.AddBatchConsumer<TConsumer>(Action<BatchOptions>? configure = null)` — registers `TConsumer` with MassTransit batch endpoint configuration; applies `MessageLimit`, `TimeLimit`, `ConcurrencyLimit` from `BatchOptions`; returns `MessagingBusBuilder` for fluent chaining; XML doc note: must use this method, NOT `AddConsumer<T>()`, for batch consumers (P-129) | WO-021 | SharedKernel.Messaging.MassTransit | `●` |
| B-04 | TestHarness batch delivery test — publish 5 messages; configure `MessageLimit = 5`; assert `ConsumeAsync` was called exactly once with a list of 5 messages (not 5 separate single-message invocations) (P-129) | WO-021 | SharedKernel.Messaging.MassTransit | `●` |
| B-05 | TestHarness time-limit test — publish 2 messages with `MessageLimit = 10`, `TimeLimit = 100ms`; assert batch is delivered after `TimeLimit` elapses with exactly 2 messages (partial batch on timeout) (P-129) | WO-021 | SharedKernel.Messaging.MassTransit | `●` |
| B-06 | Exception propagation test — `ConsumeAsync` throws; assert exception is logged and rethrown; assert MassTransit publishes fault for the batch (P-129) | WO-021 | SharedKernel.Messaging.MassTransit | `●` |
| B-07 | Verify `dotnet build` clean; `dotnet test` passes; `BatchOptions` is in Abstractions (zero transport NuGet deps); `BatchConsumerBase` is in MassTransit package (P-129) | WO-021 | SharedKernel.Messaging.MassTransit | `●` |

---

## Phase: Routing <!-- phase-key: SK.07.Routing -->

> Cross-service command routing via ISendEndpointResolver and per-type route overrides. Covers P-131.

| ID | Task | Work Order | Package(s) | State |
| --- | --- | --- | --- | --- |
| RO-01 | Implement `ISendEndpointResolver` interface — single method `Resolve<T>() → string`; full XML docs; zero NuGet dependencies in Abstractions (P-131) | WO-021 | SharedKernel.Messaging.Abstractions | `●` |
| RO-02 | Implement internal `ConventionSendEndpointResolver` sealed class in MassTransit package — uses `MessagingOptions.ServiceName` as prefix and `KebabCaseEndpointNameFormatter.SanitizeName(typeof(T).Name)` as suffix; this is the default behavior from original `MassTransitMessageBus.SendAsync<T>()` made explicit and overrideable (P-131) | WO-021 | SharedKernel.Messaging.MassTransit | `●` |
| RO-03 | Implement `MessagingBusBuilder.WithSendEndpointRoute<T>(string queueName)` — stores per-type queue name override in an internal `Dictionary<Type, string>` on the builder; validates `queueName` is non-null/non-empty at call time; returns `MessagingBusBuilder` for fluent chaining (P-131) | WO-021 | SharedKernel.Messaging.MassTransit | `●` |
| RO-04 | Update `MassTransitMessageBus.SendAsync<T>()` — inject `IReadOnlyDictionary<Type, string>` route map (from builder) and `ConventionSendEndpointResolver` via constructor; check per-type route first; fall back to `ConventionSendEndpointResolver.Resolve<T>()` when no route registered (P-131) | WO-021 | SharedKernel.Messaging.MassTransit | `●` |
| RO-05 | Pass built route dictionary from `MessagingBusBuilder.Build()` to `MassTransitMessageBus` registration — register `IReadOnlyDictionary<Type, string>` as singleton keyed service or register `MassTransitMessageBus` with the dictionary captured via closure (P-131) | WO-021 | SharedKernel.Messaging.MassTransit | `●` |
| RO-06 | Unit test — `WithSendEndpointRoute<ProcessPaymentCommand>("payment-service-process-payment")`; publish via `IMessageBus.SendAsync<ProcessPaymentCommand>()`; assert MassTransit send endpoint URI contains `payment-service-process-payment` (P-131) | WO-021 | SharedKernel.Messaging.MassTransit | `●` |
| RO-07 | Unit test — no route registered for `OrderCreatedCommand`; `SendAsync<OrderCreatedCommand>()`; assert endpoint name matches `ConventionSendEndpointResolver` output (service-name prefix + kebab-case type name) (P-131) | WO-021 | SharedKernel.Messaging.MassTransit | `●` |
| RO-08 | Verify `dotnet build` clean; `dotnet test` passes; `ISendEndpointResolver` in Abstractions has zero transport NuGet references (P-131) | WO-021 | SharedKernel.Messaging.MassTransit | `●` |

---

## Overall Progress

> Counts updated whenever a task state changes.

| Phase Key | Phase | Total | ● Done | ○ Pending | State |
| --- | --- | :---: | :---: | :---: | :---: |
| `SK.07.Design` | Design | 23 | 23 | 0 | `●` |
| `SK.07.Scaffold` | Scaffold | 8 | 8 | 0 | `●` |
| `SK.07.Core` | Core | 24 | 24 | 0 | `●` |
| `SK.07.Tests` | Tests | 17 | 17 | 0 | `●` |
| `SK.07.Docs` | Docs | 14 | 14 | 0 | `●` |
| `SK.07.Published` | Published | 5 | 5 | 0 | `●` |
| `SK.07.Resilience` | Resilience | 9 | 9 | 0 | `●` |
| `SK.07.Scheduling` | Scheduling | 10 | 10 | 0 | `●` |
| `SK.07.Saga` | Saga | 7 | 7 | 0 | `●` |
| `SK.07.Batch` | Batch | 7 | 7 | 0 | `●` |
| `SK.07.Routing` | Routing | 8 | 8 | 0 | `●` |
| `SK.07.OTel` | OTel | — | — | — | `○` |

---

## Pending Phases (WO-021 — Post-Review Gap Fill)

> These phases were identified by deep architectural review on 2026-06-08. They address gaps discovered after the initial 6-phase delivery cycle completed. Root state-map phases P-125 through P-133.

| Root Phase | Phase Key | Capability | Domain | Depends On |
| --- | --- | --- | --- | --- |
| P-125 | `SK.07.Resilience` | Circuit Breaker and Fault Consumer Contracts (Abstractions) | 07.Messaging | None |
| P-126 | `SK.07.Resilience` | Circuit Breaker Builder Method and Fault Consumer Wiring (MassTransit) | 07.Messaging | P-125 |
| P-127 | `SK.07.Scheduling` | IMessageScheduler and Deferred Delivery (Abstractions + MassTransit) | 07.Messaging | P-125 |
| P-128 | `SK.07.Saga` | Saga State Machine Base and Builder Methods | 07.Messaging | P-125 |
| P-129 | `SK.07.Batch` | Batch Consumer Base and AddBatchConsumer Builder Method | 07.Messaging | None |
| P-130 | `SK.07.Core` | Fix Build() Eager ServiceProvider Anti-Pattern | 07.Messaging | None |
| P-131 | `SK.07.Routing` | ISendEndpointResolver and Cross-Service Command Routing | 07.Messaging | P-125 |
| P-132 | — | Messaging OTel Wiring (13.ServiceDefaults) | 13.ServiceDefaults | P-117, P-118 |
| P-133 | — | Extended Messaging Governance Rules MSG0105-MSG0108 (00.Governance) | 00.Governance | P-125, P-126, P-127, P-128 |

---

## Changelog

> One line per session. Format: `[YYYY-MM-DD] {what changed} — {trigger}`.

- [2026-06-05] Sub state-map initialized — phase key registry, 6 phases scaffolded at ○, no tasks yet; package board with 2 packages at not-started
- [2026-06-05] All phase tasks populated — Design phase marked ● (9 tasks complete via CLAUDE.md authoring + root state-map phases P-115–P-121 written); Scaffold 6 tasks, Core 20 tasks, Tests 14 tasks, Docs 8 tasks, Published 5 tasks all at ○ pending; cross-domain phases P-122 (ServiceDefaults), P-123 (Governance), P-124 (Testing) tracked in root state-map.md only — arch-lead session WO-020 continuation
- [2026-06-05] SK.07.Scaffold complete — 4 csproj files created/updated; MassTransit 9.1.2 used (not 8.4.0; EF outbox package is MassTransit.EntityFrameworkCore not MassTransit.EntityFrameworkCoreIntegration; testing package is MassTransit.TestFramework not MassTransit.Testing); all projects build 0 errors 0 warnings; slnx updated with Abstractions.Tests entry — messaging-phase-implementer
- [2026-06-05] SK.07.Scaffold → ● — all 6 tasks complete; promoted to root state-map (state-map-phase)
- [2026-06-05] SK.07.Core → ● — all 20 tasks complete; 0 errors 0 warnings; key impl notes: MassTransit 9.1.2 (not 8.x); PublishContext alias needed to disambiguate from MassTransit.PublishContext; MassTransitEventPublisher uses reflection-cached delegate to satisfy `EventEnvelope<TEvent>` where `TEvent:IDomainEvent` constraint; IClientFactory.CreateRequestClient uses `IServiceProvider.CreateRequestClient<T>()`; ASB host uses ServiceUri sb://namespace for managed identity path; BindConfiguration not available (no ConfigurationExtensions transitive dep) — consumers bind config manually — messaging-phase-implementer
- [2026-06-08] SK.07.Tests → ● — all 14 tasks complete; 27 Abstractions + 43 MassTransit tests pass; key discoveries: `file` modifier breaks MassTransit type matching (use `internal`), KebabCaseEndpointNameFormatter strips `Consumer` suffix in 9.x, SQLite can't run outbox delivery worker (nested tx), outbox rows written during SaveChangesAsync not before, await using required for service providers — (state-map-phase)
- [2026-06-08] SK.07.Docs → ● — all 8 tasks complete; all public types carry full XML doc comments; 0 warnings; CLAUDE.md changelog updated (state-map-phase)
- [2026-06-08] SK.07.Published → ● — all 5 tasks complete; full NuGet metadata on both csproj files; nupkgs/SharedKernel.Messaging.Abstractions.1.0.0.{nupkg,snupkg} and SharedKernel.Messaging.MassTransit.1.0.0.{nupkg,snupkg} verified with embedded XML docs; ConsumerVerifyTests added (6 tests, 33 total in Abstractions.Tests); fixed cref XML doc warnings in PublishContext.cs and ConsumerBase.cs; Microsoft.Extensions.DependencyInjection added to Abstractions.Tests.csproj for DI resolution tests (state-map-phase)
- [2026-06-08] WO-021 deep review — 9 gap phases queued (P-125–P-133): circuit breaker + fault consumer, IMessageScheduler, saga state machine base, batch consumer base, Build() ServiceProvider anti-pattern fix, ISendEndpointResolver cross-service routing, OTel wiring (ServiceDefaults), extended governance rules MSG0105-MSG0108 — arch-lead
- [2026-06-08] WO-021 task planning complete — P-125 through P-131 fully detailed in phase sections: Resilience (R-01–R-09), Scheduling (SC-01–SC-10), Saga (SA-01–SA-07), Batch (B-01–B-07), Routing (RO-01–RO-08); Core anti-pattern fix tasks C-21–C-24; Design tasks D-10–D-23; Scaffold tasks S-07–S-08; Tests tasks T-15–T-17; Docs tasks DO-09–DO-14; Overall Progress table updated (messaging-arch-planner, WO-021)
- [2026-06-08] SK.07.Design → ● — all 23 tasks complete; D-10–D-23 verified against CLAUDE.md; MassTransitMessageScheduler internal design detail (token-to-TokenId mapping, CancelScheduledSend no-op) added to CLAUDE.md; state-map-phase propagated to root (messaging-phase-implementer, WO-021)
- [2026-06-08] S-07 → ● in SK.07.Scaffold — MassTransit.Quartz 9.1.2 added to SharedKernel.Messaging.MassTransit.csproj; S-08 → ● — Abstractions csproj verified zero transport deps; SK.07.Scaffold promoted to ● (state-map-phase)
- [2026-06-08] C-21→C-24 → ● in SK.07.Core — Build() anti-pattern fix: captured configure action used for inline validation; no BuildServiceProvider(); deferred path via ValidateOnStart(); SK.07.Core → ● (state-map-phase)
- [2026-06-08] T-15→T-17 → ● in SK.07.Tests — CircuitBreakerOptions/FaultExceptionInfo stubs added; 48 Abstractions + 47 MassTransit tests pass; SK.07.Tests → ● (state-map-phase)
- [2026-06-08] DO-09→DO-14 → ● in SK.07.Docs — IFaultConsumer, IMessageScheduler, SchedulingOptions, QuartzSchedulerOptions, SchedulingProvider, BatchOptions, ISendEndpointResolver, SagaStateBase, SagaStateMachineBase, BatchConsumerBase stubs created with full XML docs; all new builder methods documented; SK.07.Docs → ● (state-map-phase)
- [2026-06-08] R-01→R-09 → ● in SK.07.Resilience — FaultExceptionInfo, IFaultConsumer, CircuitBreakerOptions (verified stubs); FaultConsumerAdapter implemented; WithCircuitBreaker + AddFaultConsumer wired in Build(); 6 resilience tests pass; SK.07.Resilience → ● (state-map-phase)
- [2026-06-08] SC-01→SC-10 → ● in SK.07.Scheduling — SchedulingProvider/Options/QuartzOptions stubs verified; MassTransitMessageScheduler implemented (ConcurrentDictionary token map, SchedulePublish/CancelScheduledPublish); WithInMemoryScheduler + WithQuartzScheduler wired in Build(); 7 scheduling tests pass; SK.07.Scheduling → ● (state-map-phase)
- [2026-06-09] SA-01→SA-07 → ● in SK.07.Saga — SagaStateBase/SagaStateMachineBase stubs verified; AddSaga/WithEntityFrameworkSagaRepository wired in MessagingBusBuilder; 4 saga tests pass; SK.07.Saga → ● (state-map-phase)
- [2026-06-09] B-01→B-07 → ● in SK.07.Batch — BatchOptions stub verified complete; BatchConsumerBase stub verified complete; AddBatchConsumer wired in MessagingBusBuilder (replaced placeholder with real MassTransit BatchOptions configurator); 4 batch tests pass (MessageLimit delivery, TimeLimit partial batch, exception propagation, fault publication); MassTransit 9.x publishes Fault\<TMessage\> not Fault\<Batch\<TMessage\>\> on batch exception — documented; SK.07.Batch → ● (state-map-phase)
- [2026-06-09] RO-01→RO-08 → ● in SK.07.Routing — ConventionSendEndpointResolver implemented; MassTransitMessageBus.SendAsync updated with routeMap+resolver injection; Build() registers singleton routeMap + scoped resolver; 11 routing tests pass; SK.07.Routing → ● (state-map-phase)
