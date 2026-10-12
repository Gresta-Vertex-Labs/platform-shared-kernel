# 17.Workflows — Domain Brain

> Durable, crash-proof orchestration on **Temporal**, in one package: dispatch workflows (start, signal, query,
> cancel, terminate, await result, describe) from application code, author workflows and activities on platform
> base types, host a Temporal worker in the generic host, and report readiness as the `workflows` `IReadinessProbe` —
> with tenant-scoped workflow ids, caller propagation into activities, `Result`↔Temporal-failure mapping and payload
> encryption wired in. It does **not** own message-shaped reactions (`07.Messaging` — no sagas there either), recurring
> triggers (`19.Scheduling`; a trigger that starts a workflow composes both) or health endpoints (`13.ServiceDefaults`).
> The governing rule: **workflow code is replay code.**

## Packages

| Package | Tier | Purpose |
| --- | --- | --- |
| `SharedKernel.Workflows.Temporal` | Adapter | The whole domain: dispatch surface, authoring bases, worker hosting, propagation interceptor, failure mapper, payload codec, readiness probe, `TemporalOptions`, `WorkflowErrors`, `WorkflowWellKnown`. References `Primitives`, `Execution`, `Configuration`, `Cryptography` and `SharedKernel.Application` (for `ISender` only); `Temporalio`, `.Extensions.Hosting`, `.Extensions.OpenTelemetry`, `.Extensions.DiagnosticSource` (all 1.17.0) |

`consumer-verify/` composes the package through a real host. There is deliberately **no**
`SharedKernel.Workflows.Abstractions` (see Decisions).

## Public Entry Points

API detail, option keys and defaults: `SharedKernel.Workflows.Temporal/README.md`.

**Registration** — `services.AddSharedKernelTemporalWorkflows(configuration)` → `ITemporalWorkflowsBuilder`:
`.AddWorkflow<TWorkflow>()`, `.AddActivities<TActivities>()`, `.WithWorker(taskQueue, tune?)`, `.AsClientOnly()`,
`.WithPayloadEncryption()`, `.WithOpenTelemetry()`, `.WithMetrics()`, `.AllowRawClientAccess()`, `.Build()`.
Section `Workflows:Temporal` (`TemporalOptions.SectionName`); `TargetHost` and `Namespace` are required.

```csharp
services.AddSharedKernelTemporalWorkflows(configuration)       // worker-hosting service
        .AddWorkflow<OrderFulfilmentWorkflow>()
        .AddActivities<ApproveOrderActivity>()                 // a CommandActivity<ApproveOrderCommand>
        .WithWorker(OrderTaskQueues.Fulfilment, t => t with { MaxConcurrentActivities = 50 })
        .WithPayloadEncryption().WithOpenTelemetry().WithMetrics()
        .Build();

services.AddSharedKernelTemporalWorkflows(configuration)       // dispatch-only service
        .AsClientOnly().WithPayloadEncryption().WithOpenTelemetry()
        .Build();
```

Lifetimes: `IWorkflowDispatcher` scoped; `IWorkflowIdFactory`, the Temporal client, the codec and the probe singletons;
activities scoped (one DI scope per activity task).

**Dispatch** (`SharedKernel.Workflows.Temporal.Dispatch`) — `IWorkflowDispatcher` (`StartAsync` → `Result<IWorkflowHandle>`,
`GetHandle`, `DescribeAsync`), `WorkflowStartOptions`, `IWorkflowHandle` / `IWorkflowHandle<TResult>`,
`IWorkflowIdFactory` (`"{tenant}:{workflowType}:{businessKey}"`).

**Authoring** (`SharedKernel.Workflows.Temporal.Authoring`) — `WorkflowBase` (replay-safe `Logger`, `UtcNow`, `NewId()`,
header-derived `TenantScope`/`CorrelationId`, `ExecuteAsync<TActivity, TArgs, TResult>`), `ActivityBase(ILogger, IClock)`
(`Heartbeat`, `Fail(Error)`, `FailFrom(Result)`), `CommandActivity<TCommand>` / `<TCommand, TResult>` (sends through
`ISender`, maps a failed `Result`), `ActivityDispatchOptions`.

**Other** — `WorkflowErrors`, `WorkflowWellKnown` (`MeterName` = `"SharedKernel.Workflows"`, header keys, default
timeouts/attempts; there is no `ActivitySourceName`), `WorkflowReadiness` (`ProbeName = "workflows"`), `ITemporalRawClientAccessor`
(gated escape hatch).

## Rules & Invariants

### Determinism (workflow code)

1. Nothing inside a `[Workflow]` type may read a clock, random source, network, database, filesystem, environment variable, ambient DI or thread pool. Those belong in activities.
2. **No constructor injection** into a workflow and no field initialised from DI, statics or configuration — the worker, not the container, constructs workflows.
3. Use `Workflow.UtcNow`, `Workflow.NewGuid()`, `Workflow.Random`, `Workflow.DelayAsync`, `Workflow.WaitConditionAsync`. `IClock` is **wrong inside a workflow** (the SK0001 inversion) and mandatory inside activities. Enforced by SK0028.
4. No `Task.Run`, `Task.Delay`, `ContinueWith`, `ConfigureAwait(false)`, `Thread.Sleep`, `lock`, `Parallel.*` or custom schedulers in workflow code; never suppress `InvalidWorkflowOperationException`.
5. No `System.Diagnostics.Activity` API inside workflow code — tracing is the `TracingInterceptor`'s job.
6. Workflow `[LoggerMessage]` methods are called **on `Workflow.Logger`** — never an injected `ILogger<T>`.
7. Changing the code path of a workflow with running executions requires `Workflow.Patched`/`DeprecatePatch` (introduce → deploy → drain → deprecate → remove).

### Failures

8. `WorkflowFailureMapper` maps by `ErrorType`: Validation, NotFound, Conflict, Unauthorized, Forbidden, BusinessRule → `ApplicationFailureException` **non-retryable** with `errorType = Error.Code`; Unexpected, Unavailable, Timeout → retryable. Flipping a row is a retry storm or a stall.
9. An activity must never swallow a `Result.Failure` and return normally — every activity path ends in an explicit map (`Fail`/`FailFrom`, or `CommandActivity<>`'s built-in map).
10. Never blanket-catch exceptions in a workflow or activity; an unmapped exception escaping an activity is retryable, as in Temporal.
11. Dispatch-surface failures are `Result` values from `WorkflowErrors` — never inline `Error`s, never `Error.None`.

### Tenancy and ids

12. `TenantScope` is a required separate parameter on every dispatch member — never optional, nullable, defaulted, or a `WorkflowStartOptions` member. `TenantScope.Global` is refused (`WorkflowErrors.TenantScopeMissing()`, log 17001) before any I/O.
13. Every start routes through `IWorkflowIdFactory`; no dispatch member accepts a raw caller-supplied workflow id.
14. Propagation uses `RequestContextPropagation` and `WellKnownHeaders`. The worker-side interceptor rebuilds a `PropagatedRequestContext` and opens a `RequestContextScope` for each activity, so `IRequestContext` and outbound calls carry the dispatcher's tenant and correlation id. Correlation is never `Activity.Current.Id`.

### Composition

15. `.AsClientOnly()` registers no `IHostedService`; combining it with `AddWorkflow`/`AddActivities`/`WithWorker` throws. `Build()` also throws for no task queue and no client-only, zero workflows and activities on a worker, a second `WithWorker`, a second `Build()`, or a missing `TargetHost`/`Namespace` key.
16. Registration is explicit (`AddWorkflow<T>`/`AddActivities<T>`) — no assembly scanning, no reflection (`Activator`, `MakeGenericType`, `dynamic`) in this package. One closed `CommandActivity<TCommand>` per command; never a reflective "dispatch any command" activity.
17. This package does not register `IClock` or `ILogger<T>`; the host does. Types take `IOptions<TemporalOptions>`, never raw `TemporalOptions`.
18. The obsolete `AddHostedTemporalWorker(taskQueue, buildId)` overload is used deliberately with a scoped `#pragma warning disable CS0618`; worker deployment versioning is not modelled.
19. Allowed references: Foundation packages and `SharedKernel.Application` only — no MediatR, pipeline, persistence, messaging, caching, security or host packages.
20. No static mutable state.

### Readiness, encryption, raw access

21. Readiness is the `workflows` `IReadinessProbe` only — no `IHealthCheck` (`WorkflowTopologyRules.NoHealthChecksDependencyInWorkflows`). The probe resolves the client lazily. A deep task-queue backlog is not a readiness failure; `WorkerPollersActive == false` on a worker is.
22. `WithPayloadEncryption()` wraps `ISymmetricEncryptionService` (async members only) as an `IPayloadCodec`; AES-GCM associated data is the UTF-8 **WorkflowId** (never RunId, which changes across continue-as-new/retries). Every payload lives in server history in full — encrypt sensitive workflows.
23. `ITemporalRawClientAccessor` exists only after `.AllowRawClientAccess()` (Warning 17012) and is never consumed in this repo (`WorkflowTopologyRules.NoRawClientAccessorConsumptionInRepo`). It **bypasses tenant scoping and id composition**. Application code never injects a `Temporalio.*` type (SK0029).
24. Do not add a `StartAndWaitAsync`-style member (an unbounded wait behind a start-shaped name) or a Visibility-API list/search member (depends on the server deployment).

### Observability

25. The composed workflow id is `{tenant}:{type}:{businessKey}`, so no log, span tag or `Error.Message` of this package carries a workflow id, a hash of one, a tenant id, a business key, a terminate reason or exception text. Logs carry `WorkflowType`, `TaskQueue`, signal/query names, counts and `ErrorType`/`ErrorCode`; the exception goes only in the `Exception` parameter.
26. The package owns no `ActivitySource`: client, workflow and activity spans are the Temporal `TracingInterceptor`'s (rule 5), configured with `TagNameWorkflowId = null` so no span carries the workflow id. It owns meter `SharedKernel.Workflows` (versioned through `TelemetryVersion.Of`), the sink `.WithMetrics()` gives the SDK; its instruments are named by the SDK and are exempt from the `sharedkernel.{capability}.*` naming convention.
27. `WorkflowFailureMapper.ToError` maps an `RpcException` by status, never wholesale: `NotFound` → `workflow.not_found` (`NotFound`), or `workflow.namespace_not_found` (`Unexpected`) when the status carries the `NamespaceNotFoundFailure` detail; `DeadlineExceeded` → `workflow.request_timed_out` (`Timeout`); `Unavailable`/`ResourceExhausted`/`Aborted` → `workflow.service_unavailable` (`Unavailable`); every other status → `workflow.rpc_failed` (`Unexpected`). A deployment defect (missing namespace, credential or contract fault) is never a caller-facing `NotFound`/`Unauthorized`/`Forbidden`. `workflow.timed_out` is `Timeout`. Every dispatch `Error.Message` is a fixed sentence: no id, host or RPC/exception text. The worker registers no interceptor of its own: the SDK chains the client interceptors that implement `IWorkerInterceptor` into it.
28. *(planned, SK.17.WorkflowConformance)* Dispatch members share one failure style: every member, `GetHandle` included, returns a `Result` through one guard (`Global` → `TenantScopeMissing`, blank id → `InvalidWorkflowId`, an id whose tenant segment is not the caller's → `NotFound`). Only `TemporalException` is turned into a `Result`; anything else propagates. Codes are public `WorkflowErrorCodes`; `WorkflowErrors` is internal. A codec failure is the classified `WorkflowPayloadCodecException`; builder composition errors are `InvalidOperationException` with sentences that name the remedy and never wrap an `Error.Message`.

## Decisions

| Decision | Why |
| --- | --- |
| Temporal, not MassTransit sagas / Elsa / Dapr Workflow / Hangfire | A durable call stack ("do A, wait 30 days for B, then C or compensate"); message reactions belong in `07.Messaging` |
| One package, no `.Abstractions` split | The programming model (determinism, replay, patching) *is* the abstraction; a neutral engine interface would force no-op members |
| Only `IWorkflowDispatcher` (+ handle, start options, errors, well-known) would be extracted, and only if a second backend is adopted for dispatch | It carries no Temporal type; nothing else is engine-neutral |
| Expected errors are non-retryable failures carrying `Error.Code` | Temporal retries forever by default; a deterministic failure must fail fast so the workflow can compensate |
| Global tenant scope refused on dispatch | Workflow ids and history are tenant-owned; a structural tenant segment prevents cross-tenant id collisions |
| AAD bound to WorkflowId | Stable for the life of the execution and reproducible on decode |
| `Workflow.Logger` + `[LoggerMessage]` | Replay-aware logging that still follows the platform logging rule |
| Telemetry wired by name in `13.ServiceDefaults` (`WithWorkflowTelemetry()`) | No project reference from ServiceDefaults to this package |
| No kernel `ActivitySource`; `WorkflowWellKnown.ActivitySourceName` is removed | It never started a span; workflow code may not touch `Activity` (rule 5) and the Temporal `TracingInterceptor` already traces client, workflow and activity. The meter stays because `.WithMetrics()` feeds it to the SDK |
| The `TracingInterceptor` is built with `TagNameWorkflowId = null` | The SDK default tags every span with the workflow id, i.e. `{tenant}:{type}:{businessKey}` (proved by a test against the SDK default); the option's documented null disables the tag and the span sources are unchanged |
| A namespace-not-found RPC failure stays `Unexpected` (`workflow.namespace_not_found`), unlike the audit's "missing → NotFound" | It is a deployment fault; a 404 to the end user would hide it. Other RPC statuses outside not-found, deadline and unavailability map to `Unexpected` `workflow.rpc_failed` for the same reason |
| No hash or thumbprint of the workflow id in logs | Business keys are low-entropy, so a hash is brute-forceable; correlate through the ambient trace and correlation id |
| *(planned, SK.17.WorkflowConformance)* Builder composition errors stay `InvalidOperationException`, only the payload codec failure is a classified exception | Composition errors fail at `Build()` and never reach an edge (E2); the codec failure surfaces at runtime and carries `workflow.payload_codec_failure` (E1) |
| *(planned, SK.17.WorkflowConformance)* `ActivityBase`/`CommandActivity` keep the consumer's `ILogger` category | Activity logs belong to the consumer's own type; the kernel's heartbeat event is the only kernel message on it |

## Logging

`LoggingEventIdRanges.Workflows` = **17000–17999**; single package, sub-block **17000–17099** (`Logging/WorkflowLog.cs`);
17100+ unallocated.

| EventId | Event |
| --- | --- |
| 17000 | Workflow started |
| 17001 | Tenant scope missing on dispatch |
| 17002 / 17003 | Signal / query dispatched |
| 17004 | Workflow terminated |
| 17005 | Tenant header missing on a workflow |
| 17006 | Payload codec failed |
| 17007 / 17008 | Worker built / client-only composition built |
| 17009 | Readiness probe degraded |
| 17010 | Payload encryption configured |
| 17011 | Activity heartbeat recorded |
| 17012 | Raw client access enabled (Warning) |
| 17013 | Readiness probe check faulted (Warning, `{Check}` = `reachability`/`namespace`, with the exception) |

No template carries a workflow id (rule 25): 17000 and 17005 log the workflow type, 17002/17003 the signal/query name,
17004 nothing. `WorkflowLoggingEventIds` (internal) seeds `Base` and 17013 as const arithmetic over
`LoggingEventIdRanges.Workflows`.

Planned: 17014 dispatch failed (Warning, `ErrorType`/`ErrorCode`) and 17015 dispatch faulted (Error, with the exception)
in `SK.17.WorkflowConformance`, which also moves the remaining ids into `WorkflowLoggingEventIds` (values unchanged),
wires 17002, 17003, 17004 and 17006 (declared but never called today) and rewrites the templates to the root
convention.

## Cross-Domain Couplings

- **01.Core** — `Result`/`Error`, `IClock`, `IReadinessProbe`; `TenantScope`, `RequestContextScope`, `RequestContextPropagation`, `WellKnownHeaders`; `AddValidatedOptions`; `ISymmetricEncryptionService` (`Cryptography`).
- **05.Application** — `ISender` for `CommandActivity<>`; the host picks the mediator.
- **13.ServiceDefaults** — `AddSharedKernelReadiness()` maps the `workflows` probe; the `SharedKernel.*` wildcard covers the meter and `WithWorkflowTelemetry()` subscribes the Temporal `TracingInterceptor` sources (`Temporalio.Extensions.OpenTelemetry.*`). Its XML doc still cites the removed `WorkflowWellKnown.ActivitySourceName`; that doc fix is 13's.
- **16.Testing** — `SharedKernel.Workflows.Testing` doubles `IWorkflowDispatcher`; a dispatch-surface change needs the matching change there. The telemetry capture helpers (`ActivityRecorder`, `MetricRecorder`, `ShouldNotLeak`) are the only way this domain's tests assert spans, metrics and log hygiene; the SDK-named instruments on meter `SharedKernel.Workflows` need an allow-list entry in the metric convention assertion.
- **19.Scheduling** — a scheduled trigger may start a workflow through `IWorkflowDispatcher`; the two compose, neither references the other.
- **00.Governance** — `WorkflowTopologyRules`, SK0028, SK0029.

## Testing

- `SharedKernel.Workflows.Temporal.Tests`, Unit lane. No Testcontainers: `Temporalio.Testing`'s `WorkflowEnvironment` downloads and runs a dev server itself (`StartTimeSkippingAsync()` by default, `StartLocalAsync()` when real server behaviour is needed); `ActivityEnvironment` for isolated activities. Real-engine tests live under `RealEnvironment/`.
- **Time-skipping only works through `WorkflowEnvironment.Client`**: a separately connected client (what `AddSharedKernelTemporalWorkflows` builds) never auto-skips, so timer-driven tests start/await via `fixture.Environment.Client`; the worker may still be the production composition.
- **Replay determinism tests are mandatory** (`ReplayDeterminismTests`, `WorkflowReplayer`) for every sample workflow and base-type behaviour.
- The failure-mapping table is asserted one test per `ErrorType`, plus the round trip back to the same `Error.Code`.
- Every activity base is tested for the swallowed-`Result.Failure` defect (a failing `ISender` must make the activity throw).
- Fail-loud tests assert both the error and that **no I/O happened** (Global tenant scope, `AsClientOnly` + `AddWorkflow`, empty worker, bad registration).
- Propagation tests assert tenant and correlation reach `WorkflowBase` and `ActivityBase`, including across a child-workflow hop, against `WellKnownHeaders` constants.
- Codec tests assert no plaintext in captured history and decode after key rotation.
- DI tests register `NullLogger<>` and a clock explicitly. Mocking (NSubstitute) is limited to `ISender` and the dispatch surface; workflow behaviour always runs against a real `WorkflowEnvironment`.
- Observability tests (`RealEnvironment/ObservabilityTests`) compose `.WithOpenTelemetry().WithMetrics()` on its own task queue and assert through `ActivityRecorder`/`MetricRecorder`/`InMemoryLoggerFactory`; span source names come from the SDK's `TracingInterceptor.ClientSource`/`WorkflowsSource`/`ActivitiesSource`, so an SDK rename fails the test. A control test proves the SDK default tags the workflow id. Parent linking across the three spans needs the OpenTelemetry SDK's propagator, which the test installs (`Sdk.SetDefaultTextMapPropagator`).
- Namespace semantics need a real server: the time-skipping test server accepts any namespace (describe succeeds), so namespace-not-found and the probe's namespace check run against `LocalDevServerFixture` (`StartLocalAsync`). `RpcException` is constructible in a unit test with a serialized `Temporalio.Api.Common.V1.GrpcStatus` (`NamespaceNotFoundFailure` detail) as `rawStatus`.
- Fakes: `SharedKernel.Workflows.Testing` — catalogue in `src/Testing/CLAUDE.md`.

### Verified SDK shapes (`Temporalio` 1.17.0)

- `RpcException.StatusCode` members: `OK, Cancelled, Unknown, InvalidArgument, DeadlineExceeded, NotFound, AlreadyExists, PermissionDenied, ResourceExhausted, FailedPrecondition, Aborted, OutOfRange, Unimplemented, Internal, Unavailable, DataLoss, Unauthenticated`. `RpcException : TemporalException`, public ctor `(StatusCode, string message, byte[]? rawStatus)`, `GrpcStatus` is `Lazy<Temporalio.Api.Common.V1.GrpcStatus?>` (read `.Value`; `Details` are protobuf `Any`). A missing namespace and a missing workflow are both `NotFound`; only the `NamespaceNotFoundFailure` detail tells them apart.
- `TracingInterceptor(TracingInterceptorOptions)`; `TracingInterceptorOptions.TagNameWorkflowId`/`TagNameRunId`/`TagNameActivityId`/`TagNameUpdateId` are settable and "if null, no tag is created". `TracingInterceptor.ClientSource`/`WorkflowsSource`/`ActivitiesSource`/`NexusSource` are public static sources, all prefixed `Temporalio.Extensions.OpenTelemetry.`. `TemporalWorkerOptions.Interceptors` documents that the worker automatically includes the client interceptors that implement `IWorkerInterceptor`.
- The default OpenTelemetry API propagator injects nothing when the OpenTelemetry SDK is not initialised in the process, so the SDK spans are then unparented; a host with `AddServiceDefaults()` has the SDK.
- A failed lazy connect throws `InvalidOperationException` (message carries the host), not `RpcException`.
- `TerminatedFailureException`, `TimeoutFailureException` and `CanceledFailureException` have no public constructors, so a unit test cannot build them; the real-environment tests produce them.

## Known Limitations

- `TemporalOptions.DefaultActivityStartToCloseTimeoutSeconds`, `DefaultWorkflowExecutionTimeoutSeconds`, `DefaultRetryMaximumAttempts` and `ValidateNamespaceOnStart` are bound and validated but read by no code: `WorkflowBase.ExecuteAsync` applies the `WorkflowWellKnown` constants (30 s start-to-close, 30 s heartbeat, 5 attempts). Either wire them or remove them.
- No workflow Update, list/search, schedules or worker deployment versioning; reach them only through the gated raw accessor.
- `Temporalio` is not AOT-safe: a native Rust core per RID (publish with an explicit RID) and reflection-based STJ in the default data converter (a trimmed consumer must supply a source-generated context).
- One worker composition (one task queue) per builder.
- Today `GetHandle` and `DescribeAsync` accept a composed workflow id without checking that its tenant segment is the caller's, so a tenant can attach to another tenant's id (rule 13 governs starts only). Closed by `SK.17.WorkflowConformance` (rule 28). Also open until then: the dispatcher catches every exception into a `Result`, `GetHandle` throws where the other members return `Result`, and the codec throws `InvalidOperationException(Error.Message)`.
- A connect failure (unreachable host) reaches the dispatcher as an `InvalidOperationException`, which the mapper turns into `workflow.unmapped_failure` (`Unexpected`, fixed message); only a connected client answering `UNAVAILABLE` yields `workflow.service_unavailable`. Classifying the connect failure is open for `SK.17.WorkflowConformance`.
- `ITemporalWorkflowsBuilder.WithWorker`'s tuning is applied with an unnamed `Configure<TemporalWorkerServiceOptions>`; whether the hosted worker's named options pick it up is unverified (an unnamed `IOptionsMonitor` read shows the tuning, the task-queue-named reads in the 1.17.0 hosting package could not be matched). Verify before relying on `WorkerTuningOptions`.
- The first test run downloads the Temporal dev-server binary (network access needed once per SDK version).
