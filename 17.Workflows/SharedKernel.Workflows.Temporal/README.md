# SharedKernel.Workflows.Temporal

Durable, crash-proof workflow orchestration for Platform.SharedKernel microservices, over the official
[`Temporalio`](https://github.com/temporalio/sdk-dotnet) .NET SDK. Downstream services depend on this
package to **dispatch** durable workflows (start, signal, query, cancel, terminate, await result) from
ordinary application code, to **author** workflows and activities against platform-shaped base types,
to **host** a Temporal worker inside the generic host, and to **probe** worker/service readiness — all
with correlation-id/tenant-id propagation, `Result<T>`-to-Temporal-failure mapping, and payload
encryption already wired.

A deliberate single package: there is no `SharedKernel.Workflows.Abstractions` split, because durable
execution's determinism/replay/versioning programming model **is** the abstraction — see
[17.Workflows/CLAUDE.md](../CLAUDE.md) for the full reasoning.

---

## Read this first: the determinism rule

**Workflow code is replay code.** Every statement inside a `[Workflow]`-attributed type is re-executed
from history — possibly on a different process, possibly years later — and must issue byte-identical
commands every time it runs. Anything that reads a clock, a random source, the network, a database, an
environment variable, or ambient DI belongs in an **activity**, never in a workflow. The Temporal SDK
detects some violations at runtime (scheduler escapes throw `InvalidWorkflowOperationException`); many
others compile cleanly and only fail on replay, in production, weeks after being deployed. There is no
compiler backstop for most of this list — review is the backstop.

### `WorkflowBase` (deterministic-only) vs. `ActivityBase` (ordinary DI-resolved code)

| Inside a `[Workflow]` type (extends `WorkflowBase`) | Inside an activity (extends `ActivityBase`) |
| --- | --- |
| `Workflow.UtcNow` — **never** `DateTimeOffset.UtcNow` | `IClock.UtcNow` — **never** `DateTimeOffset.UtcNow` (`SK0001`, as everywhere else on the platform) |
| `Workflow.NewGuid()` — never `Guid.NewGuid()` | `Guid.NewGuid()` is fine — ordinary code |
| `Workflow.Random` — never `new Random()` | `new Random()` is fine — ordinary code |
| `Workflow.DelayAsync(...)` — never `Task.Delay`/`Thread.Sleep` | `Task.Delay` is fine — ordinary code |
| No `Task.Run`, `ContinueWith`, `ConfigureAwait(false)`, `lock`, `Parallel.*` | All of these are fine — ordinary code |
| No constructor parameters, no injected dependencies, no fields from ambient state | Ordinary constructor DI: `ILogger<T>`, `IClock`, `ISender`, a repository, a typed HTTP client — anything the container holds |
| `Workflow.Logger` (replay-aware), never an injected `ILogger<TWorkflow>` | `ILogger<T>` via constructor injection |
| No `System.Diagnostics.Activity` API (`ActivitySource.StartActivity`, `Activity.Current`) | `Activity` is ordinary code and fine — workflow tracing is Temporal's `TracingInterceptor`'s job |

**This is a deliberate inversion of the platform's own `SK0001` rule, and it is the single most
important thing to internalize before writing a line of workflow code.** `SK0001` mandates `IClock`
and forbids `DateTimeOffset.UtcNow` everywhere else on this platform. Inside a `[Workflow]` type,
**both are wrong**: `DateTimeOffset.UtcNow` is non-deterministic under replay, and an injected `IClock`
cannot even be injected in the first place — workflows are instantiated by the Temporal worker, not by
the DI container, so a workflow with a constructor dependency has already lost, because the dependency
is not there on replay. The only correct clock inside workflow code is `Workflow.UtcNow`. `IClock`
remains the correct and mandatory clock inside **activities**, which are ordinary DI-resolved code —
every platform rule that applies to ordinary service code applies there unchanged. The determinism
rules apply to workflows only and stop precisely at the activity boundary.

Constructing a `Result.Failure` inside an activity and letting it reach the end of the method body
**without** mapping it through `WorkflowFailureMapper` (via `ActivityBase.Fail(Error)`/`FailFrom(Result)`)
is the single most damaging bug shape this domain can produce: the activity reports **success** to
Temporal, and the workflow proceeds down the happy path with a value that was never produced. It is
invisible in every dashboard. Every activity body must end in an explicit map.

---

## Install

```xml
<ProjectReference Include="..\SharedKernel.Workflows.Temporal\SharedKernel.Workflows.Temporal.csproj" />
```

Or, once published, the NuGet package `SharedKernel.Workflows.Temporal`.

---

## Setup

### The common case: dispatch-only (`.AsClientOnly()`)

The overwhelmingly common registration in a microservice fleet. An API service that starts and signals
workflows but hosts no workflow code needs a Temporal client and nothing else — hosting a worker there
would turn the API pod's rolling deploy into a workflow outage.

```csharp
builder.Services
    .AddSharedKernelTemporalWorkflows(builder.Configuration)
    .AsClientOnly()
    .WithPayloadEncryption()   // optional — AES-256-GCM via 01.Core's ISymmetricEncryptionService
    .WithOpenTelemetry()       // optional — Temporal's TracingInterceptor
    .Build();
```

This registers `IWorkflowDispatcher` (scoped), `IWorkflowIdFactory`/`IWorkflowServiceProbe` (singleton),
and the underlying `ITemporalClient` (singleton) — and registers **no** `IHostedService`. Calling
`.AddWorkflow<T>()`, `.AddActivities<T>()`, or `.WithWorker(...)` after `.AsClientOnly()` is a
configuration error caught at `.Build()`, not discovered at first poll.

### Worker-hosting (owns the workflow/activity code)

```csharp
builder.Services
    .AddSharedKernelTemporalWorkflows(builder.Configuration)
    .AddWorkflow<OrderFulfilmentWorkflow>()
    .AddActivities<OrderActivities>()          // scoped by default — one DI scope per activity task
    .AddActivities<ApproveOrderActivity>()     // a CommandActivity<ApproveOrderCommand>
    .WithWorker("orders-fulfilment", tune => tune with { MaxConcurrentActivities = 50 })
    .WithPayloadEncryption()
    .WithOpenTelemetry()
    .WithMetrics()
    .Build();
```

`.Build()` validates the composition **eagerly** — a worker registered with zero workflows *and* zero
activities, a task queue registered twice, a non-`[Workflow]` type passed to `.AddWorkflow<T>()`, or
`.WithPayloadEncryption()` with no configured encryption key each fail at `.Build()`/startup with a
named error. A worker silently polling an empty task queue forever is the single hardest workflow
failure to diagnose — the pod is up, the client connects, the workflow starts, and it simply never
progresses — so this package refuses to produce that failure mode silently.

### In application code

Inject the dispatch surface — **never** a raw `Temporalio.*` type:

```csharp
public sealed class OrderService(IWorkflowDispatcher dispatcher)
{
    public async Task<Result<string>> StartFulfilmentAsync(string orderId, string tenantId, CancellationToken ct)
    {
        var options = new WorkflowStartOptions
        {
            TaskQueue = "orders-fulfilment",
            BusinessKey = orderId,
            IdReusePolicy = WorkflowIdReusePolicy.RejectDuplicate,
            IdConflictPolicy = WorkflowIdConflictPolicy.Fail,
        };

        Result<IWorkflowHandle> result = await dispatcher.StartAsync<OrderFulfilmentWorkflow, string>(
            orderId, options, TenantScope.Of(tenantId), ct);

        return result.IsSuccess
            ? Result<string>.Success(result.Value.WorkflowId)
            : Result<string>.Failure(result.Error);
    }
}
```

`TenantScope` is a **mandatory, non-nullable, non-defaulted separate parameter** on every dispatch
member — a workflow execution is addressed by a caller-supplied workflow id in a flat per-namespace
keyspace, so without a structural tenant discriminator, tenant B signalling tenant A's workflow is one
guessed string away. A dispatch call made with `TenantScope.None` returns
`WorkflowErrors.TenantScopeMissing` with **no I/O performed**. No dispatch member ever accepts a raw,
caller-supplied workflow id — every start routes through `IWorkflowIdFactory`, so the tenant segment is
structural rather than conventional.

---

## `TemporalOptions` configuration (`Workflows:Temporal`)

Bound via `services.AddValidatedOptions<TemporalOptions>(configuration.GetSection(TemporalOptions.SectionName))`
— misconfiguration (a missing `TargetHost`/`Namespace`) fails at `IHost.StartAsync()`, never at first
workflow dispatch.

| Property | Required | Default | Purpose |
| --- | :---: | --- | --- |
| `TargetHost` | Yes | — | The Temporal server target host, e.g. `"localhost:7233"`. |
| `Namespace` | Yes | — | The Temporal namespace. |
| `TaskQueue` | No | `null` | A default task queue, if the composition does not specify one explicitly via `.WithWorker(...)`. |
| `Tls` | No | `false` | Whether the client connection uses TLS. |
| `ApiKey` | No | `null` | The API key used for Temporal Cloud authentication, if applicable. |
| `IdentityPrefix` | No | `null` | A prefix prepended to the client's reported worker identity (`{prefix}-{MachineName}`). |
| `DefaultActivityStartToCloseTimeoutSeconds` | No | `30` | The platform default `StartToCloseTimeout` applied by `WorkflowBase.ExecuteAsync` when the caller does not override it. Temporal's raw activity options have **no** default and reject the call at runtime without one. |
| `DefaultWorkflowExecutionTimeoutSeconds` | No | `null` | The platform default workflow execution timeout. `null` means no execution-level timeout by default. |
| `DefaultRetryMaximumAttempts` | No | `5` | The platform default maximum retry attempts for an activity. |
| `EncryptionKeyName` | No | `null` | The key name resolved through `IEncryptionKeyProvider`, used by `EncryptionPayloadCodec` when `.WithPayloadEncryption()` is enabled. **Required** if `.WithPayloadEncryption()` is called — `.Build()` fails otherwise. |
| `ValidateNamespaceOnStart` | No | `true` | Whether the configured namespace is validated against the live Temporal service at startup. |

---

## Worked example — workflow, activity, and `CommandActivity<TCommand>`

```csharp
// A plain activity — ordinary DI-resolved code. IClock/ILogger<T> here are correct and mandatory.
public sealed class ChargeCardActivity : ActivityBase
{
    private readonly IPaymentGateway _gateway;

    public ChargeCardActivity(IPaymentGateway gateway, ILogger<ChargeCardActivity> logger, IClock clock)
        : base(logger, clock)
    {
        _gateway = gateway;
    }

    // Every [Activity]-attributed method is explicitly named — Temporal derives an unnamed method's
    // registered name from the METHOD's own name, never the declaring type, so an unnamed shared base
    // method would collide the moment two sibling activity classes both use it.
    [Activity(nameof(ChargeCardActivity))]
    public async Task<string> ChargeAsync(ChargeRequest request, CancellationToken cancellationToken)
    {
        // Long-running activities MUST heartbeat, or they are presumed dead and retried WHILE the
        // original is still running — producing a duplicate side effect (a second card charge).
        Heartbeat("charging");

        var result = await _gateway.ChargeAsync(request.CardToken, request.Amount, cancellationToken);
        if (result.IsFailure)
        {
            // A Result must never reach the end of an activity body unmapped — this is the single
            // most damaging bug shape the domain can produce (a silent success on a failed Result).
            throw FailFrom(result);
        }

        return result.Value.ReceiptId;
    }
}

// The sole 05.Application (MediatR) bridge — a closed generic per command, no reflection.
public sealed class ApproveOrderActivity : CommandActivity<ApproveOrderCommand>
{
    public ApproveOrderActivity(ISender sender, ILogger<ApproveOrderActivity> logger, IClock clock)
        : base(sender, logger, clock)
    {
    }

    [Activity(nameof(ApproveOrderActivity))]
    public override Task ExecuteAsync(ApproveOrderCommand command, CancellationToken cancellationToken = default)
        => base.ExecuteAsync(command, cancellationToken);
}

// The workflow — deterministic-only. No constructor, no injected dependencies, no ambient state.
[Workflow]
public sealed class OrderFulfilmentWorkflow : WorkflowBase
{
    private bool _cancelledByCustomer;

    [WorkflowRun]
    public async Task<string> RunAsync(string orderId)
    {
        await ExecuteAsync<ApproveOrderActivity, ApproveOrderCommand, object?>(new ApproveOrderCommand(orderId));

        string receiptId = await ExecuteAsync<ChargeCardActivity, ChargeRequest, string>(
            new ChargeRequest(orderId, Amount: 4999),
            new ActivityDispatchOptions { StartToCloseTimeout = TimeSpan.FromSeconds(15) });

        // Workflow.UtcNow, never DateTimeOffset.UtcNow — this line is replay code.
        Logger.LogInformation("Order {OrderId} fulfilled at {Timestamp}", orderId, UtcNow);
        return receiptId;
    }

    [WorkflowSignal]
    public Task CancelByCustomer()
    {
        _cancelledByCustomer = true;
        return Task.CompletedTask;
    }

    [WorkflowQuery]
    public bool WasCancelledByCustomer() => _cancelledByCustomer;
}
```

---

## `[LoggerMessage]` EventId table (17000–17012)

All production logging in this package uses the `[LoggerMessage]` source-generated pattern with
explicit `EventId`s inside the reserved sub-block **17000–17099**
(`LoggingEventIdRanges.Workflows`, from `01.Core`; 17100+ remains unallocated against a future split).
Inside workflow code, the logger instance is always `Workflow.Logger` (replay-aware — it suppresses
duplicate emissions during replay); inside activities, an ordinary injected `ILogger<T>`. Both satisfy
the same `ILogger`-extension-method shape, so the identical generated method is callable from either.

| EventId | Level | Method | Fired when |
| --- | --- | --- | --- |
| 17000 | Information | `WorkflowStarted` | A workflow execution was successfully started. |
| 17001 | Warning | `TenantScopeMissingOnDispatch` | A dispatch call was rejected for `TenantScope.None`. |
| 17002 | Debug | `SignalDispatched` | A signal was successfully delivered. |
| 17003 | Debug | `QueryDispatched` | A query was successfully dispatched. |
| 17004 | Warning | `WorkflowTerminated` | A workflow execution was terminated (no compensation runs). |
| 17005 | Warning | `TenantHeaderMissingOnWorkflow` | A tenant-scoped workflow observed no tenant header; `TenantScope.None` was surfaced. |
| 17006 | Error | `PayloadCodecFailed` | The payload codec failed to encode/decode a payload. |
| 17007 | Information | `WorkerBuilt` | A worker-hosting composition finished building. |
| 17008 | Information | `ClientOnlyBuilt` | A client-only composition finished building. |
| 17009 | Warning | `ProbeDegraded` | `IWorkflowServiceProbe.ProbeAsync` reported a degraded axis. |
| 17010 | Information | `PayloadEncryptionConfigured` | `.WithPayloadEncryption()` was applied to the client composition. |
| 17011 | Debug | `ActivityHeartbeatRecorded` | `ActivityBase.Heartbeat(...)` was called. |
| 17012 | Warning | `RawClientAccessEnabled` | `.AllowRawClientAccess()` was called — tenant scoping and workflow-id composition are bypassed for any code consuming the resulting accessor. |

---

## `Workflow.Patched` — the versioning lifecycle as an operational procedure

**This is the domain's highest-stakes operational rule.** `Workflow.Patched`/`Workflow.DeprecatePatch`
is the **only** sanctioned mechanism for changing the code path of a workflow that has running
executions. Reordering activity calls, inserting a step, changing a timer duration, or renaming an
activity in a deployed workflow — without going through this lifecycle — makes **every in-flight
execution** fail on replay with a non-determinism error, the moment its history is next replayed. This
is not a rare edge case: it happens the next time that worker restarts, deploys, or evicts the workflow
from its sticky cache. There is no partial failure mode here — it is every in-flight execution, all at
once, the instant the new code is running.

The lifecycle, followed in order, across separate deploys:

1. **Introduce the patch.** Wrap the changed code path in `Workflow.Patched("my-change-id")`:

   ```csharp
   if (Workflow.Patched("charge-before-approve"))
   {
       await ExecuteAsync<ChargeCardActivity, ChargeRequest, string>(request);
       await ExecuteAsync<ApproveOrderActivity, ApproveOrderCommand, object?>(command);
   }
   else
   {
       // The original order, preserved exactly, for any execution whose history predates the patch.
       await ExecuteAsync<ApproveOrderActivity, ApproveOrderCommand, object?>(command);
       await ExecuteAsync<ChargeCardActivity, ChargeRequest, string>(request);
   }
   ```

   A new execution takes the patched branch and records a marker in its history. An execution already
   in flight, replaying history recorded **before** the patch existed, takes the `else` branch — its
   history has no patch marker, so `Workflow.Patched` deterministically returns `false` for it on
   replay. Both old and new executions replay correctly, side by side, on the same deployed code.

2. **Deploy.** Roll the patched code out. Old in-flight executions keep completing on the `else`
   branch; new executions take the patched branch.

3. **Wait for old executions to drain.** Do not proceed until every execution started before the patch
   has completed — the workflow execution timeout (or a deliberate audit of running executions via
   `DescribeAsync`/an external tracking mechanism) is the practical bound here. **A patch condition
   must never be removed while an execution old enough to need the `else` branch might still be
   in flight.**

4. **Deprecate the patch.** Once no execution older than the patch can still be running, replace
   `Workflow.Patched("my-change-id")` with `Workflow.DeprecatePatch("my-change-id")` and delete the
   `else` branch. `DeprecatePatch` still records a compatible marker for any execution whose history
   was recorded between steps 1 and 4, but assumes the patched behavior otherwise.

5. **Remove.** Once even the deprecated-patch generation of executions has drained, delete the
   `Workflow.DeprecatePatch(...)` call entirely — the workflow code is now simply the new behavior,
   with no versioning scaffolding left behind.

Skipping straight from step 1 to step 5 — or skipping the patch mechanism altogether and just changing
the code — is what turns an ordinary deploy into an incident affecting every in-flight execution
simultaneously. `WorkflowReplayer`-based history-replay determinism tests are the only mechanism that
catches a missed patch **before** it reaches production; treat them as mandatory, not optional, exactly
as `17.Workflows`'s own test suite does.

---

## Payload encryption — the trade-offs, stated plainly

`.WithPayloadEncryption()` wraps `01.Core`'s `ISymmetricEncryptionService` (AES-256-GCM) as a Temporal
`IPayloadCodec`. It exists because Temporal persists **every** workflow input, output, signal payload,
and activity argument in the server's event history in full, for the namespace's entire retention
period, readable by anyone with namespace access — including the Temporal Web UI. On a shared or
managed cluster, that is a permanent plaintext copy of every domain payload a workflow ever touched,
absent this codec.

Two costs that come with it, stated rather than hidden:

- **Web UI / CLI opacity.** Once encryption is enabled, workflow inputs, outputs, and signal payloads
  are opaque ciphertext in the Temporal Web UI and CLI. Operators lose the ability to read workflow
  arguments while debugging directly through those tools unless a codec server is additionally deployed
  (out of scope for this package).
- **A key-retention window measured in months, not days.** The encryption key version travels in the
  encoded payload's own metadata, so key rotation follows the same versioned-ciphertext discipline
  `06.Persistence`'s `EncryptedValueConverter` uses for database columns — but the retention requirement
  is materially longer here. A workflow started under key `v1` will still replay under key `v1` on its
  **final** day, which for a long-running workflow (weeks or months after start) can be far longer than
  any comparable database-row retention window. An old key must remain configured and resolvable for as
  long as any history encrypted under it might still need to replay — removing a key too early turns a
  routine key rotation into replay failures for every execution still carrying that key's ciphertext.

A payload not carrying this codec's own encoding marker passes through unchanged on decode (the
standard Temporal codec-chain convention, so multiple codecs can coexist). A payload that **does**
carry the marker but fails to decrypt (tamper, wrong key, unknown key id) throws rather than silently
passing the ciphertext through as plaintext, or silently returning ciphertext as if it had been
decoded — `WorkflowErrors.PayloadCodecFailure` surfaces in either failure direction.

---

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) — see
[17.Workflows/CLAUDE.md](../CLAUDE.md) for the full interface contracts, the complete determinism
prohibition list, the `Result<T>`↔Temporal-failure mapping table, and verified `Temporalio` SDK shapes.
