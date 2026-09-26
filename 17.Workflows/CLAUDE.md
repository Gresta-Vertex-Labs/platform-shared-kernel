# 17.Workflows — Durable Orchestration Brain

## What This Domain Is

The durable, crash-proof orchestration layer. Downstream microservices depend on `SharedKernel.Workflows.Temporal` to **dispatch** durable workflows (start, signal, query, update, cancel, terminate, await result) from ordinary application code, to **author** workflows and activities against platform-shaped base types, to **host** a Temporal worker inside the generic host, and to report worker/service readiness as the `workflows` `IReadinessProbe` — all with tenant, correlation-id and caller propagation (an activity runs inside a `RequestContextScope` carrying the dispatching call's context), `Result<T>`-to-Temporal-failure mapping, and payload encryption already wired.

Philosophy: **Deterministic by construction. Non-determinism lives in activities. Fail non-retryably on expected failures. Never abstract what cannot be abstracted.**

> **Tier:** `SharedKernel.Workflows.Temporal` is Adapter tier. It references Foundation packages (`SharedKernel.Primitives`, `.Execution`, `.Configuration`, `.Cryptography`) and one Abstractions package, `SharedKernel.Application` (the kernel `ISender` for `CommandActivity<>`) — never MediatR, the pipeline, persistence, messaging, caching, security, a host package or ASP.NET Core; the build enforces the tiers (SKTIER001–006). The single governing rule of this domain: **workflow code is replay code.** Every line inside a `[Workflow]` type will be re-executed from history, possibly on a different process, possibly years later, and must produce byte-identical commands each time. Anything that reads a clock, a random source, a network, a database, an environment variable, or ambient DI state belongs in an **activity**, never in a workflow. That single rule is the whole design and it is mechanically checkable in review.

---

## Packages

| Package | Role | References |
| --- | --- | --- |
| `SharedKernel.Workflows.Temporal` | The entire domain in one package: the dispatch surface (`IWorkflowDispatcher`, `IWorkflowHandle`, `IWorkflowHandle<TResult>`), the authoring bases (`WorkflowBase`, `ActivityBase`, `CommandActivity<TCommand>` / `CommandActivity<TCommand, TResult>`), the worker-hosting builder, the propagation interceptors, the `Result<T>`↔failure mapper, the payload codec, `TemporalOptions`, `WorkflowErrors`, `WorkflowWellKnown`, the `workflows` readiness probe (`WorkflowReadiness`), `[LoggerMessage]` partials in **17000–17099**, and `AddSharedKernelTemporalWorkflows()` | `SharedKernel.Primitives` + `SharedKernel.Execution` + `SharedKernel.Configuration` + `SharedKernel.Cryptography` (Foundation), `SharedKernel.Application` (Abstractions — the kernel `ISender` for `CommandActivity<>` only), `Temporalio`, `Temporalio.Extensions.Hosting`, `Temporalio.Extensions.OpenTelemetry`, `Temporalio.Extensions.DiagnosticSource`, `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Options`, `Microsoft.Extensions.Logging.Abstractions` |

All packages target `net10.0`, `ImplicitUsings` enabled, `Nullable` enabled. The test project lives inside the project folder (`17.Workflows/SharedKernel.Workflows.Temporal/SharedKernel.Workflows.Temporal.Tests/`), never in a top-level `tests/`.

> **Why there is NO `SharedKernel.Workflows.Abstractions` — the single most important package decision in this domain, and a deliberate departure from the `.Abstractions` + `.{Provider}` convention that governs `06.Persistence`, `08.Storage`, `09.Search`, and `10.Intelligence`.** The platform splits a capability the moment a **second provider is genuinely plausible and genuinely swappable**. Durable execution fails both halves of that test. First, there is no second provider: Elsa, MassTransit sagas, Dapr Workflow, and Hangfire are not alternative implementations of the same contract — they are different execution models with different durability guarantees, different failure semantics, and different authoring rules. Swapping them is a rewrite, not a registration change. Second, and decisively, **the thing a workflow abstraction would have to abstract is the source code itself.** A `[Workflow]` class *is* the durable state machine; its determinism constraints, its replay semantics, its history-versioning rules (`Workflow.Patched`), and its deterministic API surface (`Workflow.UtcNow`, `Workflow.DelayAsync`, `Workflow.NewGuid`) are not implementation details behind an interface — they are the programming model. A neutral `IWorkflowEngine` would be exactly the "member one adapter must throw, degrade, approximate, or no-op" that `09.Search`'s seam rule forbids, applied to an entire package. The root brain names exactly one package here, and the `.slnx` registers exactly one project; this file ratifies that rather than re-litigating it.
>
> **The one seam that IS extractable, and what would trigger extracting it:** `IWorkflowDispatcher` — the client-side start/signal/query/cancel surface — carries no Temporal type on its signature and is genuinely engine-neutral. It is declared in this package anyway, because a one-interface `.Abstractions` package that every consumer must reference alongside the provider buys nothing today. If a second durable-execution backend is ever adopted **for the dispatch side only** (e.g. a service that dispatches into a workflow hosted by another team's stack), extract `IWorkflowDispatcher`, `IWorkflowHandle`, `WorkflowStartOptions`, `WorkflowErrors`, and `WorkflowWellKnown` into `SharedKernel.Workflows.Abstractions` at that point — and only then. Everything else in this package is Temporal-shaped by nature and must not follow.

---

## Technology Stack

| Concern | Technology |
| --- | --- |
| Durable orchestration engine | **Temporal**, via the official `Temporalio` .NET SDK pinned **`1.17.0`** (latest stable on nuget.org, published 2026-07-13, MIT, `temporalio` org). Ships `netstandard2.0`/`net6.0`+ targets and a per-RID **native Rust core** loaded by P/Invoke — see AOT Compatibility for the publishing consequence. Transitive: `Google.Protobuf` ≥ 3.26.1, `Microsoft.Extensions.Logging.Abstractions` ≥ 2.2.0, `NexusRpc` ≥ 0.3.0 |
| Worker hosting / DI | `Temporalio.Extensions.Hosting` **`1.17.0`** (MIT) — `AddTemporalClient`, `AddHostedTemporalWorker`, `AddWorkflow<T>`, `AddScopedActivities<T>`. Depends on `Microsoft.Extensions.Hosting` ≥ 8.0.1 and `Temporalio` ≥ 1.17.0 |
| Distributed tracing | `Temporalio.Extensions.OpenTelemetry` **`1.17.0`** (MIT) — `TracingInterceptor` serialises the ambient diagnostic activity into Temporal headers so a trace survives the client→workflow→activity hop and crosses SDK languages. Depends on `OpenTelemetry.Api` ≥ 1.15.3, `System.Diagnostics.DiagnosticSource` ≥ 10.0.0. **HARD CONSTRAINT stated by the SDK itself: the `System.Diagnostics.Activity` API must never be used inside workflow code** — activities are not replay-safe, so instrumentation inside a workflow is the interceptor's job and never the workflow author's |
| Metrics | `Temporalio.Extensions.DiagnosticSource` **`1.17.0`** (MIT) — a `CustomMetricMeter` bridging the SDK core's internal metrics onto a `System.Diagnostics.Metrics.Meter`, configured on `TemporalRuntime` and shared by the client and every worker created from it. Depends on `System.Diagnostics.DiagnosticSource` ≥ 7.0.0 |
| Outcome type | `Result<T>` / `Result` / `Error` from `SharedKernel.Primitives` on the **dispatch** surface — expected failures (workflow-not-found, already-started, cancelled, timed-out, query-rejected) are `Error` values, never thrown exceptions. Inside workflow and activity bodies, `Result<T>` is mapped to and from Temporal's own failure model by `WorkflowFailureMapper` — see the `Result<T>` mapping blockquote below |
| Application bridge | The kernel `ISender` (`SharedKernel.Application.Messaging`, no MediatR) resolved **inside activities only**, never inside a workflow. This is the sole reason this package references `SharedKernel.Application`; the host chooses the mediator (e.g. `SharedKernel.Application.Mediator.MediatR`) |
| Payload encryption | `ISymmetricEncryptionService` from `01.Core`'s `SharedKernel.Cryptography` (AES-256-GCM, zero third-party dependency), wrapped as a Temporal `IPayloadCodec`. **Every workflow input, output, signal payload, and activity argument is persisted in the Temporal server's event history in full** — the codec is the only thing standing between a domain payload and permanent plaintext storage on a shared cluster. **SHIPPED (WO-081/P-501, 2026-09-08):** `EncodeAsync`/`DecodeAsync` call only `ISymmetricEncryptionService`'s `*Async` members (`EncryptAsync`/`DecryptAsync`), with `CancellationToken.None` passed explicitly since `IPayloadCodec` supplies no token. AES-GCM associated data is bound to the resolved Temporal `WorkflowId` (never `RunId` — see the `EncryptionPayloadCodec` NOTE blocks below for why), sourced via `IWithSerializationContext<IPayloadCodec>.WithSerializationContext`, which `EncryptionPayloadCodec` now implements |
| Propagation | `SharedKernel.Execution`'s `RequestContextPropagation` — the one mapping REST, gRPC, MassTransit and Temporal share — writes the dispatching caller's correlation id, actor and client as **Temporal headers** (`WellKnownHeaders`, never a retyped literal, `SK0022`); the tenant header comes from the dispatch call's explicit `TenantScope`. Worker side, the activity interceptor reads them into a `PropagatedRequestContext` and opens a `RequestContextScope` for the activity, so `IRequestContext` and every outbound call the activity makes carry the same tenant and correlation id. The correlation id is never `Activity.Current.Id` |
| Configuration | Options-pattern via `SharedKernel.Configuration.AddValidatedOptions<TOptions>(IConfigurationSection)` — the single existing 01.Core overload wiring `Bind` → `ValidateDataAnnotations` → `ValidateOnStart`; misconfiguration fails at startup, never at first workflow start. **VERIFIED at Published phase — this is a two-tier mechanism, not one**: an entirely-**absent** config key (e.g. no `Workflows:Temporal:TargetHost` at all) fails **synchronously at `.Build()`**, before the `IHost` even exists (`TemporalWorkflowsBuilder`'s own eager `ConfigureClient()` check, `InvalidOperationException`); a **present-but-invalid** value (e.g. an empty/whitespace `TargetHost`) instead passes that check and fails at the real `IHost.StartAsync()` via `[Required]` + `ValidateOnStart()` (`OptionsValidationException`, naming the property) — see the `TemporalOptions` NOTE below for the full mechanism |
| DI composition | `AddSharedKernelTemporalWorkflows(configuration)` returning a fluent `ITemporalWorkflowsBuilder` (`.AddWorkflow<T>()`, `.AddActivities<T>()`, `.WithWorker(taskQueue)`, `.AsClientOnly()`, `.WithPayloadEncryption()`, `.WithOpenTelemetry()`, `.AllowRawClientAccess()`, `.Build()`) |
| Logging | `[LoggerMessage]` source-generated pattern with explicit `EventId`s, range **17000–17999** (`LoggingEventIdRanges.Workflows`, from `01.Core` — the registry constant already exists on disk and equals `17000`). Single package, so sub-block **17000–17099**; 17100+ stays unallocated against a future split. **Inside workflow code the `ILogger` instance is `Workflow.Logger`, never an injected `ILogger<T>`** — see the logging blockquote below |
| Diagnostics | An `internal static class WorkflowDiagnostics` holding an `ActivitySource` and `Meter` named from `WorkflowWellKnown.ActivitySourceName` / `WorkflowWellKnown.MeterName` (both `"SharedKernel.Workflows"`). `13.ServiceDefaults` wires them string-name-only via `WithWorkflowTelemetry()` with **no `ProjectReference` to `17.Workflows`**, matching the `WithSearchTelemetry()`/`CachingTelemetryExtensions` precedent |
| Testing | `Temporalio.Testing` (in-box in the `Temporalio` package — **not** a separate NuGet reference): `WorkflowEnvironment.StartTimeSkippingAsync()` for deterministic time-skipped workflow tests, `WorkflowEnvironment.StartLocalAsync()` for a real dev server, `ActivityEnvironment` for isolated activity tests, and `WorkflowReplayer` for history-replay determinism regression tests. Consuming services unit-test the *caller* of the dispatcher with `16.Testing/SharedKernel.Workflows.Testing` (`InMemoryWorkflowDispatcher`, `AddInMemoryWorkflowDispatcher()`). **No Testcontainers fixture** — the test environments download and manage a Temporal dev-server binary themselves, which is why this domain's Tests phase carries no inbound blocker of the kind that stalled `SK.09.Tests`. **CONFIRMED at Scaffold phase (S-09, 2026-07-23) on this machine:** the dev-server binary downloads on first run and is cached at `%LOCALAPPDATA%\Temp\temporal-test-server-sdk-dotnet-{version}.exe` (version-suffixed, so an SDK bump fetches a fresh binary); `env.SupportsTimeSkipping` is `true` and a genuine 30-day `Workflow.DelayAsync`-equivalent completed in ~11ms wall-clock. No network restriction was encountered — the one realistic failure mode this row used to flag as open is resolved, not merely mitigated |
| Test mocking | `NSubstitute` `5.3.0` and `FluentAssertions` `8.4.0`, added directly by the `.Tests` project (never by the production package). Mocking is confined to the dispatch surface, options/DI-shape assertions, and `ISender` inside `CommandActivity<>` tests — workflow behaviour always runs against a real `WorkflowEnvironment` |
| XML doc enforcement / NuGet packaging | `GenerateDocumentationFile` + `TreatWarningsAsErrors` + the `PackageReadmeFile`/README pair; package metadata and the version come from the repo-wide build props and release train |

> **Why Temporal and not MassTransit sagas, Elsa, Dapr Workflow, or Hangfire:** the root brain already names Temporal for `17.Workflows`, and this domain ratifies that rather than re-opening it — but the reasoning must be on record so it is not re-litigated. MassTransit sagas (already available in `07.Messaging`) are **correlation-driven state machines**: excellent for "react to messages, mutate persisted state", and the right tool when the process is genuinely message-shaped. They do not give you a durable *call stack* — you cannot write a sequential, branching, long-sleeping procedure and have it survive a process crash mid-statement. Temporal does exactly that, and the price is the determinism constraint this entire brain is organised around. Hangfire is a background job scheduler with no durable-execution semantics at all. Dapr Workflow is built on the Durable Task Framework and would import a sidecar runtime dependency into every consuming service. Elsa is a designer-first workflow engine whose .NET-code-first story is secondary. **The decision rule for a consuming service: if the process is "when message X arrives, update state and maybe send Y", use `07.Messaging`'s saga support. If it is "do A, then wait up to 30 days for B, then do C or compensate", use `17.Workflows`.** A service that reaches for a Temporal workflow to model a two-step message reaction has paid the determinism tax for nothing.

> **`Result<T>` ↔ Temporal failure mapping — the domain's second-most-important decision, because getting it wrong silently burns a retry budget on errors that will never succeed.** Temporal's default activity `RetryPolicy` retries a failing activity indefinitely with exponential backoff. A `Result.Failure(Error.Validation(...))` returned from an activity is a **deterministic** failure: the same input will fail identically on attempt 2 and attempt 200. Retrying it wastes worker capacity, delays the workflow's own failure handling, and buries the real cause under a retry storm. Therefore `WorkflowFailureMapper` maps by `ErrorType`, not by convention:
>
> - `ErrorType.Validation`, `NotFound`, `Conflict`, `Unauthorized`, `BusinessRule` → `ApplicationFailureException` with **`nonRetryable: true`** and `errorType` set to the `Error.Code`, so a workflow's `catch (ActivityFailureException)` can branch on the code and compensate immediately.
> - `ErrorType.Unexpected` → `ApplicationFailureException` with **`nonRetryable: false`** — a transient infrastructure fault is exactly what the retry policy exists for.
> - An **unmapped** exception escaping an activity body is retryable by default, matching Temporal's own semantics. This package never blanket-catches.
>
> **The inverse direction is the sharper edge:** an activity that swallows a `Result.Failure` and returns normally reports **success** to Temporal, and the workflow proceeds down the happy path with a value that was never produced. Every `ActivityBase`/`CommandActivity<>` member therefore ends in an explicit map — a `Result` that reaches the end of an activity body without being mapped is a bug, and the `.Tests` project asserts it for every shipped base type.

> **Reconciling `[LoggerMessage]` with `Workflow.Logger` — a genuine, non-obvious fit, not a carve-out.** The platform mandates `[LoggerMessage]` source-generated partial methods with explicit `EventId`s and forbids direct `ILogger.LogXxx(...)` extension calls (WO-041). Temporal mandates that logging inside a workflow go through **`Workflow.Logger`**, which is replay-aware — it suppresses duplicate emissions when history is being replayed, so a workflow that has been replayed 400 times does not emit 400 copies of every log line. These do not conflict: a `[LoggerMessage]`-generated method is an **extension method on `ILogger`**, and `Workflow.Logger` **is** an `ILogger`. Workflow code therefore calls `Log.OrderApproved(Workflow.Logger, orderId)` — source-generated, explicitly-numbered, and replay-aware simultaneously. **An injected `ILogger<TWorkflow>` inside a `[Workflow]` type is a hard violation** (constructor injection into a workflow is itself a violation — see Implementation Rules), and so is any direct `Workflow.Logger.LogInformation(...)` extension call, for the ordinary WO-041 reason.

> **`IClock` inside a workflow is a violation — this domain inverts the platform's own `SK0001` rule, and that inversion must be understood before writing a single line.** `SK0001` forbids `DateTimeOffset.UtcNow` platform-wide and mandates `IClock`. Inside a `[Workflow]` type, **both are wrong**: `DateTimeOffset.UtcNow` is non-deterministic, and an injected `IClock` cannot be injected at all (workflows are constructed by the Temporal worker, not by DI) and would be non-deterministic even if it could. The only correct clock inside workflow code is **`Workflow.UtcNow`**, which replays to the recorded value. Likewise `Guid.NewGuid()` → `Workflow.NewGuid()`, `Random` → `Workflow.Random`, `Task.Delay` → `Workflow.DelayAsync`, `Task.Run`/`ConfigureAwait(false)` → prohibited outright (they escape the workflow's deterministic scheduler; the SDK detects this at runtime and throws `InvalidWorkflowOperationException`). `IClock` remains the correct and mandatory clock inside **activities**, which are ordinary DI-resolved code. This carve-out is narrow, load-bearing, and must be encoded in `00.Governance` — see Cross-domain work this design requires.

---

## Interface Contracts

### `SharedKernel.Workflows.Temporal` — public surface

> Every dispatch operation returns `Result` / `Result<T>` — expected failures (not-found, already-started, cancelled, timed-out, query-rejected, deadline-exceeded) are `Error` values, never exceptions. Failures **inside** workflow and activity bodies use Temporal's own exception model, because that is the mechanism Temporal's retry, compensation, and history semantics are built on; `WorkflowFailureMapper` is the single, mandatory bridge between the two worlds.
> `CancellationToken cancellationToken = default` is always the trailing parameter, matching `IFileStorage` (08.Storage) and `ISearchIndex<TDocument>` (09.Search).

#### Dispatch surface (`Dispatch/`)

```text
IWorkflowDispatcher   (scoped — carries the ambient tenant/correlation context of the current request)

    — start —
    .StartAsync<TWorkflow>(WorkflowStartOptions options, TenantScope tenantScope,
                           CancellationToken ct)                        → Task<Result<IWorkflowHandle>>
    .StartAsync<TWorkflow, TArgs>(TArgs args, WorkflowStartOptions options,
                                  TenantScope tenantScope, CancellationToken ct)
                                                                        → Task<Result<IWorkflowHandle>>
    .StartAsync<TWorkflow, TArgs, TResult>(TArgs args, WorkflowStartOptions options,
                                           TenantScope tenantScope, CancellationToken ct)
                                                                        → Task<Result<IWorkflowHandle<TResult>>>

    — attach to an already-running execution —
    .GetHandle(string workflowId, string? runId, TenantScope tenantScope)          → IWorkflowHandle
    .GetHandle<TResult>(string workflowId, string? runId, TenantScope tenantScope) → IWorkflowHandle<TResult>

    — describe —
    .DescribeAsync(string workflowId, TenantScope tenantScope, CancellationToken ct)
                                                                        → Task<Result<WorkflowExecutionDescription>>

    NOTE (TenantScope IS A MANDATORY, NON-NULLABLE, NON-DEFAULTED SEPARATE PARAMETER — carried over
          verbatim from 09.Search/10.Intelligence, and it is MORE load-bearing here, not less): a
          workflow execution is addressed by its WORKFLOW ID, which is a caller-supplied string in a
          FLAT per-namespace keyspace. Without a tenant discriminator baked into the id by a
          mechanism the caller cannot bypass, tenant B signalling tenant A's workflow is one guessed
          string away — and unlike a search query, a signal MUTATES. TenantScope is therefore not a
          filter here; it is an input to IWorkflowIdFactory, which composes the physical workflow id,
          AND a Temporal header value asserted by the worker-side interceptor on every activity and
          signal handler. Both halves are required: the id prevents collision, the header prevents a
          workflow started under one tenant from executing activities under another.
          TenantScope is SharedKernel.Execution.Tenancy.TenantScope (the platform's one tenant scope);
          TenantScope.Global on any dispatch member returns WorkflowErrors.TenantScopeMissing before any
          I/O, and the id factory refuses it.

    NOTE (A WORKFLOW ID IS AN IDEMPOTENCY KEY — this is the platform's DURABLE idempotency primitive,
          and it is stronger than the pipeline's in-process one): Temporal enforces at most one
          RUNNING execution per workflow id per namespace. Starting the same id twice returns
          WorkflowErrors.AlreadyStarted rather than producing a second execution. A consuming service
          composing its workflow id from a stable business key (order id, payment reference) gets
          exactly-once process initiation for free, across process restarts, deploys, and duplicate
          upstream deliveries. SharedKernel.Application's IIdempotentRequest is the in-process
          analogue for a single command; this is the durable analogue for a whole process.
          Reuse behaviour is governed by WorkflowStartOptions.IdReusePolicy and .IdConflictPolicy —
          both are EXPLICIT, non-defaulted-to-"whatever the SDK does" members, because the difference
          between "reject the duplicate" and "terminate the running one and start over" is the
          difference between a safe retry and destroying in-flight work.

    NOTE (StartAsync RETURNS ONCE THE EXECUTION IS DURABLY RECORDED, NOT ONCE IT COMPLETES — the same
          honesty decision as 09.Search's SearchWriteConsistency, arrived at independently): the
          returned IWorkflowHandle is a durable reference; awaiting the RESULT is a separate,
          explicit call on the handle. There is deliberately no StartAndWaitAsync convenience — a
          method that hides an unbounded wait behind a start-shaped name is how a request thread ends
          up blocked for thirty days.

    NOTE (NO IQueryable, NO LIST/SEARCH SURFACE): listing or searching workflow executions goes
          through Temporal's Visibility API, whose availability, indexing latency, and supported
          query syntax depend on the SERVER deployment (Elasticsearch-backed advanced visibility vs.
          standard visibility) — a member that works on one cluster and not another is exactly the
          shape 09.Search's seam rule rejects. Consuming services that need a queryable projection of
          in-flight work maintain it themselves from workflow-emitted events. DescribeAsync (single
          execution, by id) is portable and is the only read offered.

IWorkflowHandle
    .WorkflowId                                                         → string { get; }
    .RunId                                                              → string? { get; }
    .SignalAsync<TSignalArgs>(string signalName, TSignalArgs args, CancellationToken ct)
                                                                        → Task<Result>
    .QueryAsync<TQueryResult>(string queryName, CancellationToken ct)   → Task<Result<TQueryResult>>
    .CancelAsync(CancellationToken ct)                                  → Task<Result>
    .TerminateAsync(string reason, CancellationToken ct)                → Task<Result>

IWorkflowHandle<TResult> : IWorkflowHandle
    .GetResultAsync(CancellationToken ct)                               → Task<Result<TResult>>

    NOTE (CANCEL AND TERMINATE ARE BOTH EXPOSED, AND THE DIFFERENCE IS NOT COSMETIC): CancelAsync
          requests cooperative cancellation — the workflow observes Workflow.CancellationToken, runs
          its compensation path, and completes as cancelled. TerminateAsync kills the execution
          server-side with NO compensation, NO cleanup, and NO chance for the workflow to react.
          Terminate is an operator action of last resort; the XML doc says so in capitals, and
          TerminateAsync requires a non-empty reason string precisely so the history records who
          destroyed the execution and why. A convenience overload defaulting the reason does not
          exist and must not be added.

    NOTE (QueryAsync IS A READ AGAINST LIVE WORKFLOW STATE, AND IT IS NOT FREE): a query is dispatched
          to a worker holding (or replaying) the execution. A query against an execution whose worker
          fleet is down returns WorkflowErrors.QueryFailed rather than hanging; a query handler that
          mutates state is a determinism violation the SDK rejects. Queries are for reading progress,
          never for driving it — that is what signals and updates are for.

    NOTE (UPDATE IS DELIBERATELY ABSENT FROM v1 OF THIS SURFACE): Temporal's workflow UPDATE (a
          synchronous, validated, response-returning signal) is genuinely useful and genuinely more
          complex — it adds a validator phase, a distinct rejection path, and server-version
          requirements that differ across self-hosted clusters. It is deferred to a later phase
          GATED ON verifying the target cluster's supported API version, not guessed at now — the
          same discipline 09.Search applied to GeoRadius. Signals plus queries cover the current
          need; adding update later is purely additive to IWorkflowHandle.

IWorkflowIdFactory   (singleton, zero I/O)
    .Create(string workflowTypeName, string businessKey, TenantScope tenantScope)  → string

    NOTE: the default implementation composes "{tenant}:{workflowType}:{businessKey}" using
          WorkflowWellKnown.IdSeparator, and is REPLACEABLE by the consuming service — but the
          TenantScope segment is non-optional in every implementation, and IWorkflowDispatcher
          routes EVERY start through this factory rather than accepting a raw caller-supplied id.
          Accepting a raw id would make the tenant segment a convention instead of a structure, and
          conventions are what a hurried call site skips.
```

#### Authoring bases (`Authoring/`)

```text
WorkflowBase   (abstract, the base every [Workflow] type in a consuming service extends)
    — no constructor, no injected dependencies, no fields initialised from ambient state —
    .Logger                                                             → ILogger  (⇒ Workflow.Logger)
    .UtcNow                                                             → DateTimeOffset (⇒ Workflow.UtcNow)
    .NewId()                                                            → Guid (⇒ Workflow.NewGuid)
    .TenantScope                                                        → TenantScope { get; }
    .CorrelationId                                                      → string { get; }
    .ExecuteAsync<TActivity, TArgs, TResult>(TArgs args, ActivityDispatchOptions options)
                                                                        → Task<TResult>

    NOTE (WHY A BASE CLASS AT ALL, WHEN TEMPORAL'S OWN MODEL IS ATTRIBUTE-DRIVEN AND BASE-CLASS-FREE):
          three things every platform workflow needs and none of which a consuming service should
          re-derive — the replay-safe logger already bound to Workflow.Logger, the TenantScope and
          CorrelationId lifted out of the Temporal headers the propagation interceptor set (so a
          workflow author never touches Workflow.Info.Headers by hand), and an ExecuteAsync overload
          whose ActivityDispatchOptions carry PLATFORM DEFAULTS for StartToCloseTimeout and
          RetryPolicy. Temporal's raw ActivityOptions has NO default StartToCloseTimeout and will
          reject the call at runtime if neither it nor ScheduleToCloseTimeout is set — a first-run
          papercut every team hits exactly once. The base makes the platform default explicit.

    NOTE (WHAT THE BASE DELIBERATELY DOES NOT DO): it does not wrap [WorkflowRun], does not intercept
          the run method, and does not impose a Result<T> return shape on the workflow itself. A
          workflow's return value is serialised into history and read by callers possibly on another
          SDK; keeping it a plain contract type (04.Contracts DTOs are the recommended shape) keeps
          it interoperable. Result<T> is a C#-side outcome type, not a wire contract.

    NOTE (VERIFIED TESTS-PHASE FINDING, GENUINE GAP FIXED — a compensation activity dispatched from a
          catch block needs its OWN, non-ambient cancellation token): confirmed empirically that both
          `Temporalio.Workflows.ActivityOptions.CancellationToken` and `DelayOptions.CancellationToken`
          default to the AMBIENT `Workflow.CancellationToken` when left unset — including for a call
          issued AFTER that token has already been cancelled. A compensation activity dispatched from
          `catch (CanceledFailureException)` using the plain `ExecuteAsync<TActivity,TArgs,TResult>`
          overload (no override) was therefore itself immediately cancelled before it could run,
          silently defeating `IWorkflowHandle.CancelAsync`'s documented "runs the compensation path"
          guarantee. Fixed additively: `ActivityDispatchOptions.CancellationToken` (nullable) now flows
          into the constructed `ActivityOptions.CancellationToken`; pass
          `new ActivityDispatchOptions { CancellationToken = CancellationToken.None }` for any activity
          call that must survive the workflow's own cancellation.

    NOTE (VERIFIED TESTS-PHASE FINDING — `Workflow.DelayAsync`'s cancellation exception is Temporal's
          own type, not the BCL one): a cancelled `Workflow.CancellationToken` observed by
          `Workflow.DelayAsync` surfaces as `Temporalio.Exceptions.CanceledFailureException` — which
          does NOT derive from `System.OperationCanceledException` (confirmed via the type hierarchy:
          `CanceledFailureException` → `FailureException` → `TemporalException` → `Exception`). A
          workflow author writing `catch (OperationCanceledException)` to run compensation logic on
          cancellation will never enter that catch block; the correct catch clause is
          `catch (Exception ex) when (ex is OperationCanceledException or
          Temporalio.Exceptions.CanceledFailureException)` (the BCL type is included defensively, since
          other cancellable SDK operations may still surface it).

ActivityBase   (abstract, DI-resolved, scoped by default)
    .Logger                                                             → ILogger<T>  (ordinary DI)
    .Clock                                                              → IClock  (ordinary DI — CORRECT here)
    .TenantScope                                                        → TenantScope { get; }
    .Heartbeat(params object?[] details)                                → void
    .Fail(Error error)                                                  → ApplicationFailureException
    .FailFrom(Result result)                                            → ApplicationFailureException

    NOTE (ACTIVITIES ARE ORDINARY CODE AND THAT IS THE ENTIRE POINT): an activity may inject IClock,
          ILogger<T>, ISender, a repository, an HttpClient-backed typed REST client — anything the
          consuming service's DI container holds. Every platform rule that applies to ordinary
          service code applies here unchanged, including SK0001 (IClock, never DateTimeOffset.UtcNow).
          The determinism rules apply to WORKFLOWS ONLY and stop precisely at this boundary.

    NOTE (Heartbeat IS NOT OPTIONAL FOR LONG ACTIVITIES): an activity exceeding its heartbeat timeout
          without heartbeating is presumed dead and retried — while the original is still running,
          producing a duplicate side effect. Any activity expected to run longer than
          WorkflowWellKnown.DefaultHeartbeatTimeout MUST heartbeat, and the XML doc says so with the
          duplicate-side-effect consequence stated, not merely "should heartbeat periodically".

CommandActivity<TCommand>              where TCommand : ICommand
CommandActivity<TCommand, TResult>     where TCommand : ICommand<TResult>
    .ExecuteAsync(TCommand command, CancellationToken ct)               → Task  /  Task<TResult>

    NOTE (THE APPLICATION BRIDGE, AND THE ONLY REASON THIS PACKAGE REFERENCES SharedKernel.Application):
          a sealed, per-command generic base that resolves the kernel ISender, sends the command through
          the consuming service's full application pipeline (every SharedKernel.Application.Pipeline
          stage it registered, behind whichever mediator adapter the host chose), and maps the resulting Result/Result<T> through WorkflowFailureMapper. A consuming service
          writes `sealed class ApproveOrderActivity : CommandActivity<ApproveOrderCommand>` plus one
          explicitly-named `[Activity]` override (see the next NOTE — this is not literally zero
          extra code). This is a CLOSED GENERIC per command — no reflection, no MakeGenericType, no
          polymorphic payload deserialisation, and therefore SK0012-clean and AOT-honest.

    NOTE (VERIFIED CORE-PHASE FINDING — activity name collision, and why CommandActivity<TCommand>'s
          own ExecuteAsync carries NO [Activity] attribute): reflected against the real Temporalio
          1.17.0 assembly via ActivityDefinition.CreateAll — Temporal derives an [Activity]-attributed
          method's registered name from the METHOD'S OWN NAME (stripping an "Async" suffix) when no
          explicit name is supplied, NEVER from the declaring or concrete TYPE name. Two sibling
          classes that both inherit an unchanged, identically-attributed base method register under
          the IDENTICAL Temporal activity name — confirmed empirically (two classes inheriting
          `[Activity] Task<int> ExecuteAsync(int)` both registered as "Execute"). A worker hosting more
          than one CommandActivity<> subtype — the overwhelmingly common case — would collide. The fix:
          CommandActivity<TCommand>.ExecuteAsync is a plain (non-attributed) virtual method; the
          concrete sealed activity supplies its own explicitly-named override:
          `[Activity(nameof(ApproveOrderActivity))] public override Task ExecuteAsync(...) =>
          base.ExecuteAsync(...);` — five lines, matching the "one five-line class per command" cost
          already stated below, now for a concrete, verified reason rather than an estimate.

    NOTE (WHAT WAS REJECTED, AND WHY, SO IT IS NOT PROPOSED AGAIN): a single non-generic
          "DispatchCommandActivity" taking a serialised command envelope and reconstructing the
          concrete type at runtime. It would need polymorphic deserialisation of an arbitrary
          ICommand from a payload PERSISTED IN HISTORY — which is a remote-code-shaped deserialisation
          surface, reflection-dependent, AOT-hostile, and a versioning trap the moment a command's
          shape changes while old histories still replay. The closed-generic-per-command form costs
          one five-line class per command and has none of those properties.

    NOTE (THE COMMAND IS AN OUTERMOST COMMAND): each activity execution runs in its own DI scope, so
          the command CommandActivity<> sends is the outermost command of that scope for the
          pipeline's ICommandScope — TransactionBehavior commits its unit of work once on success, and
          ICommandScope.OnCompleted callbacks run before ExecuteAsync returns (a callback that throws is
          logged by the pipeline and never fails the activity). Validation and authorization failures
          come back as a failed Result (Validation/Unauthorized/Forbidden), so WorkflowFailureMapper
          makes them non-retryable.

    NOTE (THE ACTIVITY'S CALLER IS THE DISPATCHING CALL, AS ATTRIBUTION ONLY): the worker interceptor
          runs every activity inside RequestContextScope.Begin(PropagatedRequestContext) built from the
          Temporal headers — the workflow's tenant, the dispatching caller's actor/client, and its
          correlation id (a new one when the header is missing). IRequestContext therefore answers with
          that tenant (persistence tenant filters and idempotency keys work) and outbound REST/gRPC/bus
          calls forward it. A PropagatedRequestContext grants NO permission (HasPermissionAsync is always
          false): a command guarded by AuthorizationBehavior fails closed unless the activity opens its
          own RequestContextScope with a SystemRequestContext carrying an explicit permission set. A
          retried activity re-sends the command — a command with side effects outside its unit of work
          implements IIdempotentRequest with a key derived from the workflow id.
```

#### Worker hosting (`Hosting/`)

```text
ITemporalWorkflowsBuilder   (returned by AddSharedKernelTemporalWorkflows)
    .AddWorkflow<TWorkflow>()                                           → ITemporalWorkflowsBuilder
    .AddActivities<TActivities>()                                       → ITemporalWorkflowsBuilder
    .WithWorker(string taskQueue, Func<WorkerTuningOptions, WorkerTuningOptions>? tune)
                                                                        → ITemporalWorkflowsBuilder
                    [CORRECTED at Docs phase, DO-06 — the shipped signature takes a transforming
                     Func<WorkerTuningOptions, WorkerTuningOptions>, not an Action<WorkerTuningOptions>,
                     because WorkerTuningOptions is an immutable sealed record: the caller writes
                     `tune => tune with { MaxConcurrentActivities = 50 }`, mutating nothing in place]
    .AsClientOnly()                                                     → ITemporalWorkflowsBuilder
    .WithPayloadEncryption()                                            → ITemporalWorkflowsBuilder
    .WithOpenTelemetry()                                                → ITemporalWorkflowsBuilder
    .WithMetrics()                                                      → ITemporalWorkflowsBuilder
    .AllowRawClientAccess()                                             → ITemporalWorkflowsBuilder
    .Build()                                                            → IServiceCollection

    NOTE (AsClientOnly IS THE MOST COMMON REGISTRATION IN A MICROSERVICE FLEET, NOT AN EDGE CASE): an
          API service that STARTS and SIGNALS workflows but hosts no workflow code needs a client and
          nothing else. Hosting a worker there would make the API pod's rolling deploy a workflow
          outage. AsClientOnly registers IWorkflowDispatcher/IWorkflowIdFactory/the `workflows` readiness
          probe and NO IHostedService; calling .AddWorkflow<T>() or .WithWorker(...) after it is a
          configuration error caught at Build() time, not at first poll.

    NOTE (Build() VALIDATES COMPOSITION EAGERLY): a worker registered with zero workflows AND zero
          activities, a task queue registered twice, .AddWorkflow<T>() where T is not
          [Workflow]-attributed, or .WithPayloadEncryption() without a configured key — each fails at
          Build()/startup with a named error, never as a silently idle worker. A worker that polls an
          empty task queue forever is the single hardest workflow failure to diagnose, because
          EVERYTHING looks healthy: the pod is up, the client connects, the workflow starts, and it
          simply never progresses.

    NOTE (VERIFIED TESTS-PHASE FINDING, GENUINE CORE-PHASE GAP FIXED — the non-[Workflow]-type case was
          NOT actually eager until T-08 caught it): `Temporalio.Extensions.Hosting`'s
          `ITemporalWorkerServiceOptionsBuilder.AddWorkflow(Type)` does NOT validate the `[Workflow]`
          attribute at registration time — confirmed empirically that calling it with a plain,
          unattributed type does not throw, deferring the failure to actual worker startup instead
          (unlike the core SDK's own `WorkflowDefinition.Create(Type)`/`TemporalWorkerOptions.
          AddWorkflow(Type)`, which DO throw `ArgumentException` immediately). `TemporalWorkflowsBuilder.
          BuildWorkerHosting()` now calls `Temporalio.Workflows.WorkflowDefinition.Create(workflowType)`
          itself as an eager pre-check before delegating to the hosting builder's `AddWorkflow`,
          restoring the "Build() validates composition eagerly, never silently" guarantee this domain's
          whole design exists to provide.

WorkerTuningOptions   (sealed record, all members with platform defaults)
    .MaxConcurrentWorkflowTasks / .MaxConcurrentActivities /
    .MaxConcurrentLocalActivities / .MaxCachedWorkflows / .GracefulShutdownTimeout
```

#### Propagation, failure mapping, and codec (`Interception/`, `Failures/`, `Codec/`)

```text
WorkflowPropagationInterceptor   (internal sealed; IClientInterceptor + IWorkerInterceptor)
    Client side  → on every StartWorkflow, Signal, and Query call writes, through
                   RequestContextPropagation.WriteHeaders, the correlation id, actor and client of
                   RequestContextScope.Current (CorrelationIds.Current — never Activity.Current.Id), plus
                   WellKnownHeaders.TenantId from the dispatch call's explicit TenantScope (the ambient
                   context's tenant is deliberately not used).
    Workflow     → merges the workflow's own inbound headers onto every activity and child workflow it
                   schedules; WorkflowBase.TenantScope / .CorrelationId read them from Workflow.Info.Headers.
    Activity     → reads the headers with RequestContextPropagation.ReadHeaders into a
                   PropagatedRequestContext (a missing correlation id becomes a new one), exposes the
                   tenant as ActivityBase.TenantScope, and runs the activity inside
                   RequestContextScope.Begin(caller), so IRequestContext and outbound calls carry it.

    NOTE (HEADER NAMES COME FROM 01.Core, NEVER FROM A LOCAL LITERAL): WellKnownHeaders /
          WellKnownBaggageKeys (WO-042, P-259) is the single source shared with 11.Communication,
          13.ServiceDefaults, and 14.Presentation. A retyped literal here is precisely the defect
          WO-042 existed to eliminate, and SK0022 flags it.

    NOTE (WHY THIS IS AN INTERCEPTOR AND NOT A PARAMETER ON EVERY WORKFLOW'S ARGS TYPE): a
          correlation id threaded through every workflow's argument record would be lost the moment
          one workflow forgot it, and would pollute the wire contract that other SDK languages read.
          Headers travel with the execution automatically, including into child workflows and
          activities, and are invisible to the workflow's own contract.

    NOTE (VERIFIED CORE-PHASE ARCHITECTURE — two distinct AsyncLocal ambient-context bridges, and why
          each is needed): reflected against the real assembly — `Temporalio.Workflows.WorkflowInfo`
          exposes `.Headers` directly (an `IReadOnlyDictionary<string, Payload>`), so `WorkflowBase`
          reads `Workflow.Info.Headers` deterministically with NO ambient state needed. But
          `Temporalio.Activities.ActivityInfo` has NO `Headers` member at all — only the WORKER-SIDE
          interceptor's `ExecuteActivityInput.Headers` carries them — so an internal
          `ActivityPropagationContext` (AsyncLocal-backed) bridges the interceptor's decoded
          TenantScope/CorrelationId into `ActivityBase.TenantScope`. Symmetrically, on the CLIENT side,
          none of `ITemporalClient`'s dispatch methods nor the SDK's `StartWorkflowInput`/
          `SignalWorkflowInput`/`QueryWorkflowInput` carry a TenantScope parameter — so a second,
          distinct `DispatchPropagationContext` (also AsyncLocal-backed) carries the TenantScope for
          the in-flight client call, set immediately before each underlying Temporal call by
          `WorkflowDispatcher`/`WorkflowHandleAdapter` and read by this interceptor's client half when
          writing headers. Both are per-logical-call-context state (isolated per async flow, exactly
          like `IHttpContextAccessor`) — the sanctioned, narrow exception to the "no static mutable
          state" rule, never shared across concurrent calls.

    NOTE (VERIFIED TESTS-PHASE FINDING, GENUINE CORE-PHASE GAP FIXED — headers do NOT auto-propagate
          from a workflow's OWN inbound headers to the activities/child workflows IT schedules):
          confirmed empirically that `ScheduleActivityInput.Headers`/`StartChildWorkflowInput.Headers`
          (both `Temporalio.Worker.Interceptors`) start EMPTY unless something explicitly populates
          them — the SDK does not carry a workflow's own start-time headers forward to its own
          outbound calls by default. `WorkflowInboundInterceptor.Init(WorkflowOutboundInterceptor
          outbound)` is the hook: `WorkflowPropagationInterceptor` now also returns a
          `PropagatingWorkflowOutboundInterceptor` from `Init`, which overrides
          `ScheduleActivityAsync<TResult>`/`StartChildWorkflowAsync<TWorkflow,TResult>` to merge
          `Workflow.Info.Headers` onto each call's own `Headers` dictionary (an explicitly-set header
          on that specific call still wins). Without this, an activity or child workflow invoked from
          inside a workflow silently observed `TenantScope.Global` even though the workflow itself was
          correctly scoped — caught only because T-15's real-`WorkflowEnvironment` propagation test
          asserts the ACTIVITY's own observed tenant scope, not just the workflow's.

WorkflowFailureMapper   (internal static)
    .ToFailure(Error error)                                             → ApplicationFailureException
    .ToFailure(Result result)                                           → ApplicationFailureException
    .ToError(Exception exception)                                       → Error

    NOTE (THE MAPPING TABLE IS THE CONTRACT — see the Result<T> blockquote in Technology Stack):
          Validation/NotFound/Conflict/Unauthorized/BusinessRule ⇒ nonRetryable: true, errorType =
          Error.Code. Unexpected ⇒ nonRetryable: false. The inverse (.ToError) maps
          WorkflowFailedException / ActivityFailureException / RpcException back onto WorkflowErrors
          members, preserving the original errorType string as the Error.Code wherever the failure
          originated from this same mapper — so a code set in an activity survives the round trip and
          is branchable at the dispatch site.

    NOTE (VERIFIED CORE-PHASE ENHANCEMENT — the original ErrorType round-trips too, not only Code):
          ToFailure(Error) stashes `(int)error.Type` as the ApplicationFailureException's first
          structured `details` element (verified ctor shape:
          `(message, errorType = null, nonRetryable = false, details = null, nextRetryDelay = null,
          category = Unspecified)` — every parameter but message is optional). ToError's inverse reads
          it back defensively via `IFailureDetails.ElementAt<int>(0)` inside a narrowly-scoped try/catch
          — this is NOT a "swallowed Result.Failure" violation, since it decodes optional round-trip
          metadata Temporal itself never produces for a failure this mapper did not originate, falling
          back to a NonRetryable-derived ErrorType when absent or undecodable. Both Code AND Type now
          survive the round trip for any failure raised by this mapper — stronger than the original
          Code-only guarantee.

EncryptionPayloadCodec   (internal sealed; Temporalio IPayloadCodec, IWithSerializationContext<IPayloadCodec>)
    .EncodeAsync(IReadOnlyCollection<Payload>)                          → Task<IReadOnlyCollection<Payload>>
    .DecodeAsync(IReadOnlyCollection<Payload>)                          → Task<IReadOnlyCollection<Payload>>
    .WithSerializationContext(ISerializationContext context)            → IPayloadCodec
    internal static .DeriveAssociatedData(string? workflowId)           → byte[]

    NOTE (THIS IS NOT OPTIONAL GARNISH — Temporal persists EVERY input, output, signal payload, and
          activity argument in the server's event history, retained for the namespace's full
          retention period, readable by anyone with namespace access including the Temporal Web UI):
          on a shared or managed cluster that is a permanent plaintext copy of the domain data the
          workflow touched. The codec wraps 01.Core's ISymmetricEncryptionService (AES-256-GCM) and
          is enabled by .WithPayloadEncryption().

    NOTE (THE COST IS STATED, NOT HIDDEN): encrypted payloads are OPAQUE in the Temporal Web UI and
          CLI — operators lose the ability to read workflow inputs while debugging unless they run a
          codec server (out of scope for this package). Key rotation follows the versioned-ciphertext
          discipline 06.Persistence's EncryptedValueConverter already established: the key id
          travels inside every encoded payload, and an old key must remain configured until every history
          encrypted under it has aged past retention — which for workflows can be MONTHS longer than
          for database rows, because a workflow started under key v1 will still replay under key v1
          on its final day.

    NOTE (MECHANISM — rewritten for the P-545 SharedKernel.Cryptography redesign):
          ISymmetricEncryptionService is async-only: EncryptAsync(ReadOnlyMemory<byte>, aad, ct) →
          EncryptedPayload; DecryptAsync(EncryptedPayload, aad, ct) → Result<byte[]> (never throws for a
          bad ciphertext; resolves the key via IEncryptionKeyProvider keyed on EncryptedPayload.KeyId).
          The codec serialises the ENTIRE original Temporal Payload proto (metadata + data) as the
          AES-256-GCM plaintext and writes a NEW Payload whose `data` is 01.Core's canonical
          `EncryptedPayload.ToBytes()` layout ([version][key id length][key id][nonce][tag][ciphertext])
          and whose ONLY metadata entry is `encoding: binary/encrypted-sk-v2`. Decode reads it back with
          `EncryptedPayload.TryParse` and reconstructs the original Payload byte-for-byte via
          `Payload.Parser.ParseFrom`. No hand-rolled payload packing: the earlier unversioned
          `binary/encrypted-sk` marker (key id/nonce/tag as separate `sk-encryption-*` metadata entries)
          was never published and is not read — the marker was renamed so the two layouts never mix.
          A payload not carrying this codec's own marker is passed through unchanged on decode (the
          standard Temporal codec-chain convention letting multiple codecs coexist); a payload that DOES
          carry the marker but is malformed or fails to decrypt throws WorkflowErrors.PayloadCodecFailure
          rather than passing ciphertext through as plaintext. EncodeAsync/DecodeAsync pass
          CancellationToken.None explicitly (IPayloadCodec supplies no token) and preserve the payload
          count/order contract via Task.WhenAll over a materialized array. Registration:
          `AddSharedKernelCryptography(configuration).AddSymmetricEncryption()` plus a consumer-supplied
          IEncryptionKeyProvider; a KMS-backed provider works unchanged.

    NOTE (SHIPPED — WO-081/P-501: associated-data source, VERIFIED AGAINST THE REAL COMPILED Temporalio
          1.17.0 ASSEMBLY, not guessed): IPayloadCodec.EncodeAsync/DecodeAsync themselves carry NO
          execution-context parameter — confirmed by reflecting the compiled assembly, exactly matching
          this file's existing S-05 finding. A SEPARATE, opt-in mechanism exists: a codec may implement
          `Temporalio.Converters.IWithSerializationContext<IPayloadCodec>`
          (`WithSerializationContext(ISerializationContext context) → IPayloadCodec`), which the SDK's
          own XML docs confirm is called before a client dispatch (start/signal/query/
          schedule-create-or-describe/child-or-external-workflow) with an `ISerializationContext.Workflow`
          (Namespace, WorkflowId), and before an activity invocation (from a workflow, on the activity
          worker, or via AsyncActivityHandle) with an `ISerializationContext.Activity` (Namespace,
          ActivityId, WorkflowId, WorkflowType, ActivityType, ActivityTaskQueue, IsLocal). Both context
          types implement a shared `ISerializationContext.IHasWorkflow.WorkflowId` — nullable, "null
          e.g. standalone activities" per the SDK's own doc.

    NOTE (THE PHASE THAT COMMISSIONED THIS DESIGN SAID "workflow id + run id" — RunId IS REJECTED,
          both as an SDK gap AND as an architectural error, so it is not merely deferred): RunId
          appears on NONE of ISerializationContext/.IHasWorkflow/.Activity/.Workflow in the real
          compiled 1.17.0 assembly — there is no mechanism by which this codec can observe a run id at
          all. Independently of that gap, binding AAD to RunId would be actively WRONG even if it were
          available: Temporal's continue-as-new mechanism (and workflow retries) assign a NEW RunId to
          the SAME WorkflowId while carrying payload data forward across that boundary, so a
          RunId-bound AAD would make a continued/retried execution's carried-forward payloads fail to
          decrypt — a correctness bug wearing a security feature's clothes. WorkflowId — this domain's
          own idempotency/addressing unit, see D-06 — is the correct and only binding granularity. The
          corrected design: EncryptionPayloadCodec implements IWithSerializationContext<IPayloadCodec>,
          returns a lightweight immutable clone carrying the resolved WorkflowId (pattern-matching the
          supplied context against ISerializationContext.IHasWorkflow), and derives AAD as the UTF-8
          bytes of that WorkflowId when present, Array.Empty<byte>() when null or when no context was
          ever supplied (the base, unbound codec instance) — an explicit, narrower-binding fallback,
          never a crash. The SDK's own doc warns WithSerializationContext "may be called many times...
          make sure this is very inexpensive to call," so the clone reuses the same singleton
          ISymmetricEncryptionService/ILogger instances and adds nothing but the resolved id string.
          The existing Result<T>↔Temporal-failure mapping for a decrypt failure (WorkflowErrors.
          PayloadCodecFailure, an InvalidOperationException) is UNCHANGED by this migration.

    NOTE (SHIPPED, PROVEN AGAINST REAL CAPTURED TEMPORAL HISTORY, NOT ONLY A SYNTHETIC UNIT-TEST
          PAYLOAD): T-18 proves the cross-WorkflowId rejection at the unit level using the real SDK
          `ISerializationContext.Workflow` type directly. `17.Workflows/consumer-verify`'s Surface 5
          proves the SAME guarantee end-to-end — start a real encrypted workflow against a real
          `WorkflowEnvironment`, fetch the REAL captured `Payload` from history via the public
          `ITemporalClient`/`WorkflowHandle.FetchHistoryAsync()`, then attempt to decode THAT ciphertext
          under a codec bound to a different `WorkflowId` and confirm it throws. This required a second,
          deliberately narrow `InternalsVisibleTo("consumer-verify")` grant on the production `.csproj`
          — documented there as a one-purpose exception (consumer-verify otherwise remains a black-box
          "exactly as a downstream consumer would use it" harness; no black-box `IWorkflowDispatcher`
          -only surface could ever construct this proof, because the SDK always resolves the correct
          `WorkflowId` context for whichever execution it is decoding).
```

#### Probe, options, well-known constants, errors (`Health/`, `Configuration/`, `Constants/`, `Errors/`)

```text
WorkflowReadiness   (public static class — the probe's name and ReadinessReport.Data keys)
    ProbeName = "workflows"
    ReachableKey = "Reachable"   NamespaceAddressableKey = "NamespaceAddressable"
    WorkerPollersActiveKey = "WorkerPollersActive"

WorkflowServiceProbe   (internal sealed : SharedKernel.Primitives.Health.IReadinessProbe, registered by
                        AddSharedKernelTemporalWorkflows on every composition, client-only included)
    Healthy iff the Temporal service answers its health check AND this service's namespace can be
    described AND — on a worker-hosting composition — every hosted worker is still polling
    (always true on a client-only composition).

    NOTE (READINESS IS AN IReadinessProbe, NOT A HEALTH CHECK): 17.Workflows ships NO IHealthCheck and
          never references Microsoft.Extensions.Diagnostics.HealthChecks. The host maps the probe with
          services.AddHealthChecks().AddSharedKernelReadiness() (SharedKernel.ServiceDefaults); code
          that wants the report directly calls GetRequiredReadinessProbe(WorkflowReadiness.ProbeName).
          The client and hosted services are resolved on the first probe, never in the constructor
          (a host constructs every probe to read its name while hosted services are being created).

    NOTE (WorkerPollersActive IS THE CHECK THAT MATTERS, AND IT IS THE ONE A NAIVE PROBE OMITS): a
          worker process whose pollers have died is UP, CONNECTED, and USELESS — it accepts traffic,
          reports healthy, and silently processes nothing. Reachability alone cannot distinguish it
          from a working fleet. A task-queue backlog is never a readiness failure: it means work is
          SLOW, not unavailable.

TemporalOptions   (sealed CLASS — mutable `{ get; set; }` properties, the idiomatic Options-pattern
                    shape, DataAnnotations-validated — CORRECTED at Docs phase (DO-06): the Design-time
                    text above called this a "sealed record"; the shipped type is a plain sealed class,
                    matching every other bindable Options type on the platform)
    public const string SectionName = "Workflows:Temporal"
    .TargetHost (required) / .Namespace (required) / .TaskQueue /
    .Tls / .ApiKey / .IdentityPrefix /
    .DefaultActivityStartToCloseTimeoutSeconds (default 30) / .DefaultWorkflowExecutionTimeoutSeconds /
    .DefaultRetryMaximumAttempts (default 5) / .ValidateNamespaceOnStart (default true)

    NOTE (CORRECTED at Docs phase, DO-06): there is no `.DefaultRetryPolicy` member — the shipped shape
          is `.DefaultRetryMaximumAttempts` (a plain `int`, DataAnnotations-`[Range(1, int.MaxValue)]`),
          which `WorkflowBase.ExecuteAsync` folds into a `Temporalio.Common.RetryPolicy` constructed at
          the call site (`new RetryPolicy { MaximumAttempts = ... }`) only when the caller's
          `ActivityDispatchOptions.RetryPolicy` is null. A full `RetryPolicy` object was never bound
          from configuration — only the one scalar a consuming service is expected to tune by default.

    NOTE: the const is passed to GetSection(...), never a bare literal at the call site (SK0022).

    NOTE (VERIFIED PUBLISHED-PHASE FINDING — config-validation is a TWO-TIER mechanism, confirmed via a
          scratch console repro and then again inside the 17.Workflows/consumer-verify harness against
          real compiled code): an entirely ABSENT config key (e.g. no Workflows:Temporal:TargetHost key
          in configuration at all) is caught by TemporalWorkflowsBuilder.ConfigureClient()'s own eager
          check, which reads `_section[nameof(TemporalOptions.TargetHost)]` directly off the raw
          IConfigurationSection indexer — a missing key returns null, and the code does
          `?? throw new InvalidOperationException(...)`. This throws SYNCHRONOUSLY the instant consumer
          code calls `.Build()` on the fluent builder — during service registration, i.e. BEFORE the
          IHost is ever constructed via HostApplicationBuilder.Build(), let alone before
          IHost.StartAsync(). A config key PRESENT but holding an INVALID value (empty string or
          whitespace-only) is NOT caught by that check — a non-null string satisfies `?? throw` — so
          `.Build()` succeeds, and only then does TemporalOptions.TargetHost's [Required] DataAnnotation
          plus AddValidatedOptions's ValidateOnStart() (registered unconditionally in the constructor,
          independent of which builder methods are later called) throw
          Microsoft.Extensions.Options.OptionsValidationException at the genuine IHost.StartAsync()
          call, naming "TargetHost" in .Failures. Both tiers name the offending property; the
          entirely-absent-key tier is a STRONGER guarantee than "fails at IHost.StartAsync()" implies
          (it fails even before the IHost exists), not a weaker one — but a future session must not
          assume a single uniform exception type/timing for every misconfiguration shape.

WorkflowWellKnown   (static class of const string / static readonly)
    .ActivitySourceName = "SharedKernel.Workflows"   .MeterName = "SharedKernel.Workflows"
    .IdSeparator   .TenantHeaderKey   .CorrelationHeaderKey   (both forwarding to 01.Core's
    WellKnownHeaders — declared here as forwarding constants, never as independent literals)
    .DefaultStartToCloseTimeout   .DefaultHeartbeatTimeout   .DefaultMaximumAttempts

WorkflowErrors   (static factory catalog)
    .NotFound / .AlreadyStarted / .Cancelled / .Terminated / .TimedOut /
    .QueryFailed / .SignalFailed / .ServiceUnavailable / .NamespaceNotFound /
    .TenantScopeMissing / .InvalidWorkflowId / .WorkerNotConfigured / .DeterminismViolation /
    .PayloadCodecFailure / .InvalidWorkflowRegistration

    NOTE (VERIFIED AGAINST THE REAL Error API, AND THIS DIFFERS FROM WHAT A GUESS WOULD PRODUCE):
          SharedKernel.Primitives' Error exposes Unexpected, Validation, NotFound, Conflict,
          Unauthorized, Forbidden and BusinessRule — plus the None sentinel. THERE IS NO
          Error.Failure. Every "the operation failed" case routes through Error.Unexpected.
          Error.None is never returned from any member — it is the "no error" sentinel, never a failure.
          TenantScopeMissing is Unauthorized, not Validation — a missing tenant scope on a
          cross-tenant-addressable keyspace is an authorization failure, and framing it as a 422
          would present a would-be cross-tenant signal as an unprocessable business request.
          Error.BusinessRule is used ZERO times in this domain: nothing in a capability package is a
          domain rule. AlreadyStarted is Conflict; DeterminismViolation is Unexpected.
```

---

## Implementation Rules

### The determinism rule (the whole design in one line)

- **Workflow code is replay code.** Every statement inside a `[Workflow]` type will be re-executed from history on a different process at an arbitrary future time and must issue byte-identical commands each time. Anything that reads a clock, a random source, the network, a database, the filesystem, an environment variable, ambient DI, or a thread pool belongs in an **activity**. Every proposed addition to a workflow body is checked against this rule in review, and the constructor is the surface to guard hardest — a workflow that takes a dependency has already lost, because the dependency is not there on replay.

### Hard violations (never do these)

- **Constructor injection into a `[Workflow]` type**, or any field initialised from DI, static state, configuration, or an environment variable. Workflows are instantiated by the Temporal worker, not by the container. Dependencies reach workflow code through **activities** and nowhere else.
- `DateTimeOffset.UtcNow`, `DateTime.Now`, `Guid.NewGuid()`, `new Random()`, `Environment.*`, `File.*`, `HttpClient`, or any I/O inside workflow code. Use `Workflow.UtcNow`, `Workflow.NewGuid()`, `Workflow.Random`; everything else goes in an activity.
- **`IClock` injected into a `[Workflow]` type.** This is the platform's mandated clock everywhere else (`SK0001`) and is wrong here — see the `IClock` blockquote in Technology Stack. `IClock` remains mandatory inside activities.
- `Task.Run`, `Task.Delay`, `Task.ContinueWith`, `ConfigureAwait(false)`, `Thread.Sleep`, `lock`, `Parallel.*`, or any explicit `TaskScheduler` inside workflow code — all escape the workflow's deterministic scheduler. Use `Workflow.DelayAsync`, `Workflow.WaitConditionAsync`, and the SDK's own task combinators. The SDK detects scheduler escapes at runtime and throws `InvalidWorkflowOperationException`; do not "fix" that by suppressing it.
- Using the `System.Diagnostics.Activity` API (`ActivitySource.StartActivity`, `Activity.Current`, `SetTag`, `SetBaggage`) **inside workflow code** — explicitly unsupported by the SDK, non-deterministic under replay. Workflow tracing is `TracingInterceptor`'s job. (`Activity` inside an *activity* is ordinary code and is fine.)
- An injected `ILogger<TWorkflow>` inside a workflow, or a direct `Workflow.Logger.LogInformation(...)` extension call. Workflow logging is `[LoggerMessage]`-generated methods invoked **on `Workflow.Logger`**.
- **Changing the code path of a workflow that has running executions without `Workflow.Patched`.** Reordering activity calls, inserting a step, changing a timer duration, or renaming an activity in a deployed workflow makes every in-flight execution fail on replay with a non-determinism error. `Workflow.Patched`/`DeprecatePatch` is the only sanctioned change mechanism, and its lifecycle (introduce patch → deploy → wait for old executions to drain → deprecate → remove) is documented in the README, not left to folklore. This is the operational analogue of `07.Messaging`'s `IMessageVersionTranslator` and carries the same "rolling upgrade or outage" stakes.
- **Swallowing a `Result.Failure` inside an activity and returning normally.** Every activity body ends in an explicit `WorkflowFailureMapper` call. A silent success on a failed `Result` sends the workflow down the happy path with a value that was never produced — the single most damaging bug shape this domain can produce, because it is invisible in every dashboard.
- Mapping an expected `Error` (Validation/NotFound/Conflict/Unauthorized/BusinessRule) to a **retryable** failure. It will be retried until the policy exhausts, burning worker capacity on an outcome that cannot change.
- Blanket-catching `Exception` in an activity or workflow and converting it to a success, a `Result.Failure`, or a swallowed no-op. Temporal's retry, timeout, and compensation machinery is driven by exceptions escaping; catching them disables it.
- Making `TenantScope` optional, nullable, defaulted, or a member of `WorkflowStartOptions`. It is a required separate parameter on every dispatch member, feeds `IWorkflowIdFactory`, and is asserted worker-side from the Temporal header.
- Accepting a **raw, caller-supplied workflow id** on any dispatch member. Every start routes through `IWorkflowIdFactory`, so the tenant segment is structural rather than conventional.
- Exposing a `StartAndWaitAsync`-shaped convenience that hides an unbounded wait behind a start-shaped name.
- Adding a workflow **list/search** member backed by Temporal's Visibility API — its availability and query syntax depend on the server deployment, so it works on one cluster and not another.
- Referencing MediatR, `SharedKernel.Application.Pipeline`, persistence, messaging, caching, storage, search, AI, communication, security, presentation, integration, a `SharedKernel.ServiceDefaults*` host package or ASP.NET Core from this package. Foundation packages plus `SharedKernel.Application` only.
- Injecting a raw `ITemporalClient`, `TemporalWorker`, `WorkflowHandle`, or any other `Temporalio.*` type into application code. Application code injects `IWorkflowDispatcher` / `IWorkflowHandle`. The raw-client escape hatch follows the three-gate pattern below.
- Implementing `IHealthCheck`, or referencing `Microsoft.Extensions.Diagnostics.HealthChecks`, anywhere in `17.Workflows`. Readiness is the `workflows` `IReadinessProbe`; the host maps it with `AddSharedKernelReadiness()`.
- Treating a deep task-queue backlog as a readiness failure. A deep backlog means work is **slow**, not **unavailable**. `WorkerPollersActive == false` on a worker-hosting service, by contrast, **is** a readiness failure.
- Constructing an ad-hoc `Error` inline — all errors come from `WorkflowErrors`. Naming a non-existent factory (`Error.Failure`) or returning `Error.None` from any member are both violations. `Error.BusinessRule` is unused in this domain.
- Config section paths as bare literals at a `GetSection` call site — always `TemporalOptions.SectionName` (`SK0022`). Likewise header names: always `01.Core`'s `WellKnownHeaders`, never a retyped literal.
- A non-generic "dispatch any command" activity reconstructing an `ICommand` from a serialised envelope by reflection. Use the closed-generic `CommandActivity<TCommand>` per command.
- Reflection of any kind in this domain's own code — no `Activator.CreateInstance`, no `Assembly.Load`, no `Type.GetMethod`, no `MakeGenericMethod`/`MakeGenericType`, no `dynamic`. Workflow and activity registration is explicit via `.AddWorkflow<T>()`/`.AddActivities<T>()`, never an assembly scan.
- Any static mutable state.
- Adding `<IsAotCompatible>true</IsAotCompatible>` to any `17.Workflows` `.csproj` — per root policy the tag is too coarse-grained, and this package's core dependency is not AOT-clean regardless.

### Raw client accessor

`ITemporalRawClientAccessor` is the genuine last resort (Visibility API queries, schedules, namespace administration, Nexus operations — all real Temporal capabilities this package deliberately does not model). It is gated three ways and none may be relaxed:

1. Registered **only** when the composition root calls `.AllowRawClientAccess()` on the builder — an explicit, greppable, reviewable act.
2. That call logs a startup `Warning` (`17012`), mirroring the `AddStaticServiceDiscovery()` and `AllowInvalidCertificates` dev-only-warning precedents and `09.Search`'s raw-accessor gates.
3. A `00.Governance` architecture test asserts no type inside this repo consumes it.

**THE HATCH BYPASSES TENANT SCOPING AND WORKFLOW-ID COMPOSITION.** `TenantScope` is applied inside `IWorkflowIdFactory` and the propagation interceptor; a raw client call receives neither. A multi-tenant service using the accessor **must** compose the tenant segment and set the tenant header itself. This warning is stated in capitals on the accessor's XML doc.

### Cross-domain touch points

Owned by other domains; change them there, never from here.

- **`13.ServiceDefaults`** — `WithWorkflowTelemetry()` wires the `SharedKernel.Workflows` source and meter by string name (no reference to this package), and `AddSharedKernelReadiness()` maps the `workflows` probe. There is no workflows-specific ServiceDefaults package and no layering grant.
- **`16.Testing`** — `SharedKernel.Workflows.Testing` (`InMemoryWorkflowDispatcher`/`InMemoryWorkflowHandle`, `AddInMemoryWorkflowDispatcher()`) lets a consuming service unit-test *that it dispatched* without a Temporal server. No container fixture: Temporal's `WorkflowEnvironment` covers real-engine tests.
- **`00.Governance`** — the tier check (SKTIER001–006) replaces the old layering rule; `WorkflowTopologyRules` (`NoRawClientAccessorConsumptionInRepo`, `NoHealthChecksDependencyInWorkflows`); analyzers `SK0028` (non-deterministic API inside a `[Workflow]` type, including the `IClock` inversion of `SK0001`) and `SK0029` (raw Temporal client injection).

---

## DI Registration (expected shape)

```csharp
// A worker-hosting service (owns the workflow code):
services
    .AddSharedKernelTemporalWorkflows(configuration)
    .AddWorkflow<OrderFulfilmentWorkflow>()
    .AddActivities<OrderActivities>()          // scoped by default — one DI scope per activity task
    .AddActivities<ApproveOrderActivity>()     // a CommandActivity<ApproveOrderCommand>
    .WithWorker(OrderTaskQueues.Fulfilment, tune => tune with { MaxConcurrentActivities = 50 })
    .WithPayloadEncryption()                   // AES-256-GCM via 01.Core's ISymmetricEncryptionService
    .WithOpenTelemetry()
    .WithMetrics()
    .Build();

// An API service that only DISPATCHES workflows — the most common registration in a fleet:
services
    .AddSharedKernelTemporalWorkflows(configuration)
    .AsClientOnly()
    .WithPayloadEncryption()
    .WithOpenTelemetry()
    .Build();

// In application code, inject the dispatch surface — never a Temporalio type:
//   IWorkflowDispatcher     → start / get-handle / describe
//   IWorkflowHandle<T>      → signal / query / cancel / terminate / await result
//   IWorkflowIdFactory      → compose a tenant-scoped workflow id (rarely called directly)
// Readiness: the "workflows" IReadinessProbe is registered automatically; the host maps it with
//   builder.Services.AddHealthChecks().AddSharedKernelReadiness();
```

`IWorkflowDispatcher` is **scoped** — it captures the ambient correlation/tenant context of the current request. `ITemporalClient`, `IWorkflowIdFactory`, the `workflows` readiness probe, and the payload codec are **singletons**; the Temporal client is thread-safe, pools its own connection, and must never be scoped or transient. Activities registered via `.AddActivities<T>()` are **scoped by default** (one DI scope per activity task, matching `Temporalio.Extensions.Hosting`'s `AddScopedActivities<T>`); a stateless activity class may opt into singleton registration explicitly.

`.AsClientOnly()` registers no `IHostedService`. Calling `.AddWorkflow<T>()`, `.AddActivities<T>()`, or `.WithWorker(...)` alongside it is a composition error caught at `Build()`.

**Expected gotchas, carried forward from sibling domains rather than rediscovered:** this package does **not** self-register `IClock` or `ILogger<T>` — both are the consuming host's responsibility, the uniform platform convention confirmed across `08.Storage`/`09.Search`. Any type in this package whose constructor takes a raw `TemporalOptions` (rather than `IOptions<TemporalOptions>`) must be registered through an explicit factory lambda unwrapping `sp.GetRequiredService<IOptions<TemporalOptions>>().Value` — `AddValidatedOptions` only ever registers the `IOptions<T>` wrapper, and the plain `AddSingleton<TInterface, TImplementation>()` shorthand therefore fails to resolve in **every** consuming service, not just tests. That was a real, shipped `09.Search` defect found at Tests phase; it is written here so this domain does not repeat it. **Verified at Core phase: this package never made the mistake in the first place** — every type here takes `IOptions<TemporalOptions>` (or resolves it lazily) directly, so no factory-lambda workaround was ever needed.

**Verified Core-phase hosting-composition pattern:** `TemporalWorkflowsBuilder` reads `TargetHost`/`Namespace`/`EncryptionKeyName` directly off the raw `IConfigurationSection` (synchronously, no `IServiceProvider` needed) for eager `Build()`-time validation, then wires the real client via `Temporalio.Extensions.Hosting`'s `AddTemporalClient(services, targetHost, ns) → OptionsBuilder<TemporalClientConnectOptions>`, using the BCL's `OptionsBuilder<T>.Configure<TDep>`/`.Configure<TDep1,TDep2>(...)` overloads to inject `ISymmetricEncryptionService`/`ILogger<EncryptionPayloadCodec>` into the payload-codec wiring — the clean, DI-aware way to configure Temporalio options against resolved dependencies. `AddHostedTemporalWorker(services, taskQueue, buildId)` reuses the client registered by the prior `AddTemporalClient` call; **this overload is marked `[Obsolete]` in 1.17.0** in favor of one taking `WorkerDeploymentOptions` (Temporal's worker-versioning/deployment feature — a genuinely separate SDK concept this domain's locked contract does not model) — the obsolete overload is used deliberately, with the warning narrowly suppressed at its one call site via a documented `#pragma warning disable/restore CS0618`, rather than adopting an unplanned new concept. A worker-hosting composition additionally registers a small internal `TemporalWorkflowsCompositionLogger : IHostedService` purely to log the "worker built" summary at `StartAsync` (since `Build()` itself has no `ILogger` available) — **this hosted service is never registered on the `.AsClientOnly()` path**, preserving the hard "`AsClientOnly()` registers no `IHostedService`" rule.

---

## AOT Compatibility

- `IWorkflowDispatcher`, `IWorkflowHandle`, `IWorkflowHandle<TResult>`, `IWorkflowIdFactory`, and `ITemporalWorkflowsBuilder` are interfaces — AOT-safe by definition.
- The models (`WorkflowStartOptions`, `ActivityDispatchOptions`, `WorkerTuningOptions`, `WorkflowExecutionDescription`; `TenantScope` from `SharedKernel.Execution`) are `sealed record` / `readonly record struct` over BCL primitives — AOT-safe.
- `CommandActivity<TCommand>` / `CommandActivity<TCommand, TResult>` are **closed generics instantiated once per command at compile time** — no `MakeGenericType`, no polymorphic payload deserialisation, no reflection. This is the specific reason the reflection-based "dispatch any command" design was rejected.
- `WorkflowErrors` and `WorkflowFailureMapper` are static factories returning `Error` / `ApplicationFailureException` values — AOT-safe.
- `EncryptionPayloadCodec` wraps `01.Core`'s `ISymmetricEncryptionService`, which is pure `System.Security.Cryptography` — in-box on `net10.0`, AOT-safe.
- **`Temporalio` 1.17.0 is a documented non-AOT-safe dependency, and its shape differs from every other third party in this repo.** It carries a **native Rust core** shipped as per-RID native assets and loaded by P/Invoke, and its default `DataConverter` uses **reflection-based `System.Text.Json`** for every workflow argument, activity argument, and return value. Two practical consequences that must be stated and not discovered: a consuming service must publish with an explicit **RID** or the native core is not resolved at runtime; and a trimmed or AOT consumer must supply a source-generated `JsonSerializerContext` through the data converter or payload serialisation fails at runtime, not at build time. This is exactly the "non-AOT-safe third party placed behind an abstraction" case the root brain's AOT guidance sanctions — encapsulating it behind `IWorkflowDispatcher` and the authoring bases limits the blast radius to the registration and worker-hosting path. **RID list CONFIRMED at Scaffold phase (S-07) against the extracted NuGet package's `runtimes/` folder on the implementation machine**: `linux-arm64`, `linux-musl-arm64`, `linux-musl-x64`, `linux-x64`, `osx-arm64`, `osx-x64`, `win-arm64`, `win-x64` — exactly 8 RIDs, no more, no fewer. The `JsonSerializerContext` seam itself was not exercised this session (this package's own code never serialises workflow/activity payloads directly — that is the consuming service's concern when it opts into trimming/AOT) and remains a documented risk for a trimmed consumer, not a gap in this package.
- No `Activator.CreateInstance`, no `Assembly.Load`, no `MakeGenericMethod`/`MakeGenericType`, no `Type.GetProperty`/`GetMethod`, no `dynamic` in this domain's own code. Workflow and activity registration is explicit and generic, never an assembly scan.
- No `<IsAotCompatible>true</IsAotCompatible>` tag on the `17.Workflows` `.csproj`, per root policy.

---

## Test Rules

- Unit tests live in the nested `SharedKernel.Workflows.Temporal.Tests` folder. **Standard test package set:** `xunit` 2.9.3, `xunit.runner.visualstudio` 2.8.2, `Microsoft.NET.Test.Sdk` 17.13.0, `coverlet.collector` 6.0.4, `FluentAssertions` 8.4.0, `NSubstitute` 5.3.0, plus a `ProjectReference` to `16.Testing/SharedKernel.Testing` and a `GlobalUsings.cs` containing `global using Xunit;` — `ImplicitUsings` does not auto-import xUnit attributes.
- **`WorkflowEnvironment` replaces the container fixture — there is no Testcontainers dependency in this domain.** `WorkflowEnvironment.StartTimeSkippingAsync()` is the default for workflow behaviour tests: a 30-day `Workflow.DelayAsync` completes in milliseconds, which makes long-running timeout, retry, and escalation paths genuinely testable rather than aspirationally documented. `WorkflowEnvironment.StartLocalAsync()` is used only where a test needs real server behaviour that time-skipping does not model. **CONFIRMED at Scaffold phase (S-09, 2026-07-23)** on this machine: the dev-server binary downloads on first run, is cached at `%LOCALAPPDATA%\Temp\temporal-test-server-sdk-dotnet-{version}.exe`, and genuinely time-skips (a 30-day delay completed in ~11ms). No network restriction was encountered — this domain's only realistic Tests-phase blocker did not materialize.
- **VERIFIED TESTS-PHASE FINDING — genuine auto-time-skipping requires the exact `ITemporalClient` `WorkflowEnvironment.Client` vends; a separately-connected client to the same server does not participate in it.** Confirmed empirically (a standalone repro and this domain's own test suite): starting/awaiting a workflow through a client built via `Temporalio.Extensions.Hosting.AddTemporalClient(targetHost, ns)` — i.e. exactly what `AddSharedKernelTemporalWorkflows`/`IWorkflowDispatcher` construct, even when pointed at the identical target host and namespace as an active `WorkflowEnvironment` — never auto-skips a real `Workflow.DelayAsync`; the call hangs indefinitely waiting for simulated time that never advances. Time-dependent tests (a natural timer actually elapsing, not a cancel/terminate/signal-driven completion) must start/await through `fixture.Environment.Client` directly; the WORKER processing those executions can still be the one built by the production `AddSharedKernelTemporalWorkflows` composition, since task-queue routing does not care which client started an execution. Tests that only need cancellation/termination/signal-driven completions (no natural timer elapsing) are unaffected and may use the full `IWorkflowDispatcher` pipeline normally.
- **History-replay determinism tests are mandatory, not optional, and are the single highest-value test in this domain.** For every shipped workflow-shaped sample and every base-type behaviour, capture the execution history as JSON and replay it with `WorkflowReplayer`. This is the only mechanism that catches a determinism regression *before* it reaches production, where it manifests as every in-flight execution failing at once. A workflow change that passes its behaviour tests and fails its replay test is a change that would have taken down live executions.
- `ActivityEnvironment` unit-tests activities in isolation — including heartbeating, cancellation, and the `Result`→failure mapping — with no server and no workflow.
- **Fail-loud tests are mandatory.** For every rejection path — `TenantScope.Global` on a dispatch, a workflow id colliding with a running execution, `.AddWorkflow<T>()` on a `.AsClientOnly()` builder, a worker registered with no workflows and no activities, `.WithPayloadEncryption()` with no configured key, a non-`[Workflow]` type passed to `.AddWorkflow<T>()` — assert both that the correct `Error` (or `Build()`-time exception) results **and that no I/O occurred**. A test asserting only the error would pass against an implementation that connects first and validates second.
- **The `Result`→failure mapping table is asserted exhaustively, one test per `ErrorType`.** All five expected `ErrorType` values map to `nonRetryable: true` with `errorType == Error.Code`; `Unexpected` maps to `nonRetryable: false`. Plus the inverse: a failure raised by this mapper in an activity round-trips back to the same `Error.Code` at the dispatch site. This table is the contract; a silent flip of one row is a production retry storm or a production stall.
- **Every shipped activity base is tested for the swallow-a-`Result.Failure` defect specifically.** Construct a `CommandActivity<TCommand>` over an `ISender` substitute returning `Result.Failure(...)` and assert the activity **throws** — a passing test that only checks the happy path would not catch the domain's most damaging bug shape.
- Propagation tests assert the round trip through the real `WorkflowEnvironment`: a correlation id and tenant id set client-side arrive intact in `WorkflowBase.CorrelationId`/`.TenantScope` **and** in `ActivityBase.TenantScope` for an activity invoked by that workflow — including across a child-workflow hop. Assert against `01.Core`'s `WellKnownHeaders` constants, never a retyped literal (the exact defect WO-042 existed to kill).
- Payload-codec tests assert that an encrypted workflow argument is **not** present as plaintext in the captured history payload, and that decode round-trips. Include a key-version test proving a payload encrypted under `v1` still decodes after `v2` is added — the workflow-history retention window makes this a longer-lived requirement than the equivalent `06.Persistence` column-encryption case.
- Options-validation tests: valid config registers without throw; a missing `TargetHost`/`Namespace` fails at startup. Resolving `IOptions<TemporalOptions>.Value` directly is sufficient to trigger `ValidateDataAnnotations()` — no `IHost` needed.
- DI registration tests: `IWorkflowDispatcher` resolves scoped; `ITemporalClient`/`IWorkflowIdFactory` resolve as singletons and exactly one `IReadinessProbe` named `workflows` is registered (assert via `ReferenceEquals` across two resolutions); `ITemporalRawClientAccessor` does **not** resolve unless `.AllowRawClientAccess()` was called; `.AsClientOnly()` registers no `IHostedService`. Use `ServiceCollection` + `BuildServiceProvider()`; register `NullLogger<>` and a clock explicitly, since this package deliberately registers neither.
- **Sanctioned mocking exception:** `ISender` inside `CommandActivity<>` tests, and the dispatch surface when testing a consuming-service-shaped composition. Workflow behaviour, replay, and propagation coverage is never mocked — it runs against a real `WorkflowEnvironment`.
- `InternalsVisibleTo` from `SharedKernel.Workflows.Temporal` to its own `.Tests` project, mirroring the `06.Persistence.EfCore`/`13.ServiceDefaults`/`15.Integration.Webhooks`/`09.Search` precedent, so `WorkflowFailureMapper`, `WorkflowPropagationInterceptor`, and `EncryptionPayloadCodec` stay `internal` while remaining directly unit-testable.

---

## Changelog

History — the Design/Scaffold/Core/Tests/Docs/Published sessions, WO-081 (payload AAD binding) and the WO-086 refactor — is in `state-map.md` ("Domain-Brain Changelog").
