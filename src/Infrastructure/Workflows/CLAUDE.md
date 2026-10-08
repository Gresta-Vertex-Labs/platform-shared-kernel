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

**Other** — `WorkflowErrors`, `WorkflowWellKnown` (`ActivitySourceName`/`MeterName` = `"SharedKernel.Workflows"`,
header keys, default timeouts/attempts), `WorkflowReadiness` (`ProbeName = "workflows"`), `ITemporalRawClientAccessor`
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

## Cross-Domain Couplings

- **01.Core** — `Result`/`Error`, `IClock`, `IReadinessProbe`; `TenantScope`, `RequestContextScope`, `RequestContextPropagation`, `WellKnownHeaders`; `AddValidatedOptions`; `ISymmetricEncryptionService` (`Cryptography`).
- **05.Application** — `ISender` for `CommandActivity<>`; the host picks the mediator.
- **13.ServiceDefaults** — `WithWorkflowTelemetry()` subscribes to `"SharedKernel.Workflows"` by name; `AddSharedKernelReadiness()` maps the `workflows` probe.
- **16.Testing** — `SharedKernel.Workflows.Testing` doubles `IWorkflowDispatcher`; a dispatch-surface change needs the matching change there.
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
- Fakes: `SharedKernel.Workflows.Testing` — catalogue in `src/Testing/CLAUDE.md`.

## Known Limitations

- `TemporalOptions.DefaultActivityStartToCloseTimeoutSeconds`, `DefaultWorkflowExecutionTimeoutSeconds`, `DefaultRetryMaximumAttempts` and `ValidateNamespaceOnStart` are bound and validated but read by no code: `WorkflowBase.ExecuteAsync` applies the `WorkflowWellKnown` constants (30 s start-to-close, 30 s heartbeat, 5 attempts). Either wire them or remove them.
- No workflow Update, list/search, schedules or worker deployment versioning; reach them only through the gated raw accessor.
- `Temporalio` is not AOT-safe: a native Rust core per RID (publish with an explicit RID) and reflection-based STJ in the default data converter (a trimmed consumer must supply a source-generated context).
- One worker composition (one task queue) per builder.
- The first test run downloads the Temporal dev-server binary (network access needed once per SDK version).
