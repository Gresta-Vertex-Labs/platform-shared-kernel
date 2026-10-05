# SharedKernel.Application.Testing

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Tier: Testing](https://img.shields.io/badge/tier-Testing-e36209)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

> **Run a command or query through the real SharedKernel application pipeline in a unit test — the same
> `AddSharedKernelApplication` call, the same behaviors in the same order, the same start-time seam check — and
> assert on the result, the spans and the metrics.**

| You get | So that |
| --- | --- |
| `ApplicationPipelineTestHarness` | A handler is tested through tracing, logging, metrics, authorization and validation exactly as the service runs it |
| `Configure(app => app.WithIdempotency().WithTransactions()…)` | The opt-in behaviors are chosen with the builder the service itself passes to `AddSharedKernelApplication` |
| `Build()` (no mediator) or `Build<TMarker>()` (MediatR over your assembly) | Test one handler in isolation, or the whole Application project as the host wires it |
| The host's start-time check on `Build` | A seam an opt-in needs and the test forgot fails with the same `OptionsValidationException` the host would throw |
| `CapturedActivities` / `CapturedMeasurements` | Spans and duration metrics of the `SharedKernel.Application` source are asserted without an OpenTelemetry exporter |
| `AddFakeApplicationBehaviorServices()` | One call registers the unit of work, caller and idempotency store the opt-ins need |

## Install

```xml
<PackageReference Include="SharedKernel.Application.Testing" />
```

The version comes from your central `SharedKernelVersion` property — every SharedKernel package ships at the same
version. See [Using the packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel#using-the-packages).

Reference it from a **test project only**. `TestingNeverReferencedByProduction` fails any production project that
references a testing package.

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Testing — reference it from your **test projects** only |
| Depends on | `SharedKernel.Application.Pipeline`, `SharedKernel.Application.Mediator.MediatR`, `SharedKernel.Testing`, `SharedKernel.Persistence.Testing`, `SharedKernel.Idempotency.Testing`, `Microsoft.Extensions.DependencyInjection` |
| Namespaces | `SharedKernel.Testing.Application` |

## Quick start

```csharp
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Application.Idempotency;
using SharedKernel.Application.Messaging;
using SharedKernel.Primitives.Results;
using SharedKernel.Testing.Application;
using Xunit;

public sealed record PlaceOrder(string IdempotencyKey) : ICommand, IIdempotentRequest;

public sealed class PlaceOrderHandler : IRequestHandler<PlaceOrder, Result>
{
    public int Calls { get; private set; }

    public Task<Result> Handle(PlaceOrder request, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(Result.Success());
    }
}

public sealed class PlaceOrderPipelineTests
{
    [Fact]
    public async Task A_duplicate_key_replays_the_first_response()
    {
        var handler = new PlaceOrderHandler();
        using var harness = new ApplicationPipelineTestHarness();
        harness.Services.AddFakeApplicationBehaviorServices();          // IUnitOfWork, IRequestContext, IIdempotencyStore
        harness.Services.AddSingleton<IRequestHandler<PlaceOrder, Result>>(handler);
        harness.Configure(app => app.WithIdempotency().WithTransactions()).Build();

        var first = await harness.SendAsync(new PlaceOrder("key-1"));
        var second = await harness.SendAsync(new PlaceOrder("key-1"));

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(1, handler.Calls);
        Assert.Contains(harness.CapturedMeasurements, m => m.InstrumentName == "sharedkernel.application.request.duration");
    }
}
```

## How it works

- **Two build modes, one pipeline.** `Build()` calls `AddSharedKernelApplication` with no mediator and scans no
  assembly of yours: register each handler (and any `IRequestValidator<T>`) on `Services`; `SendAsync` goes through a
  harness `ISender` that runs the kernel `RequestPipeline<TRequest, TResponse>` directly. `Build<TMarker>()` calls
  `AddSharedKernelApplication(typeof(TMarker).Assembly, app => { configure(app); app.UseMediatR(); })`, so the
  handlers, validators and domain-event handlers of that assembly are found for you and `SendAsync` goes through the
  kernel `ISender` over MediatR, as in the service.
- **Always-on behaviors.** Tracing, logging, metrics, authorization and validation run in both modes; `Configure`
  adds only the opt-ins. Logging goes to `NullLogger<T>` (the harness registers it).
- **Start-time check.** Both modes resolve `IStartupValidator` and call `Validate()`: a missing `IIdempotencyStore`,
  `IUnitOfWork`, `IAuditTrailWriter` or `IRequestContext` fails `Build` with `OptionsValidationException` naming it.
  A scanned `[RequirePermission]` request needs an `IRequestContext` even if the test never sends it.
- **Diagnostics.** Measurements of the `SharedKernel.Application` meter are always captured; activities only after
  `WithActivityCapture()`. `Dispose()` removes both listeners and disposes the service provider.
- **Lifetimes.** `AddFakeApplicationBehaviorServices()` registers its three fakes as singletons, so they survive
  every send and can be asserted afterwards.

## Recipes

### 1. Prove authorization fails closed

```csharp
using var harness = new ApplicationPipelineTestHarness();
harness.Services.AddSingleton<IRequestContext>(new FakeRequestContext { IsAuthenticated = false });
harness.Build<PlaceOrderHandler>();                    // scans the Application assembly

var result = await harness.SendAsync(new CancelOrder(orderId));   // declares [RequirePermission("orders:cancel")]

Assert.Equal("unauthorized.default", result.Error.Code);
```

Grant the permission with `new FakeRequestContext { Permissions = ["orders:cancel"] }`; an authenticated caller
without it gets `ErrorType.Forbidden`.

### 2. Assert on a fake you own

The harness exposes `Services`, not its provider. Register the instance first and keep the reference:

```csharp
var store = new FakeIdempotencyStore();                               // SharedKernel.Testing.Idempotency
harness.Services.AddKeyedSingleton<IIdempotencyStore>(IdempotencyPurpose.Request, store);
harness.Services.AddSingleton<IRequestContext>(new FakeRequestContext());
harness.Configure(app => app.WithIdempotency()).Build<PlaceOrderHandler>();

await harness.SendAsync(new PlaceOrder("order-42"));

Assert.Single(store.Calls, c => c.Member == nameof(FakeIdempotencyStore.TryBeginAsync));
```

The store receives the 64-hex key scoped to tenant and caller, never the raw `"order-42"`.

### 3. Audit a command

```csharp
var audit = harness.Services.AddFakeAuditTrailWriter();              // SharedKernel.Persistence.Testing
harness.Services.AddFakeApplicationBehaviorServices();
harness.Configure(app => app.WithTransactions().WithAuditing()).Build<PlaceOrderHandler>();

await harness.SendAsync(new RenameCustomer(customerId, "Ada"));        // implements IAuditableRequest<Result>

audit.ShouldHaveAudited("customer.rename", "Customer", customerId.ToString());
```

### 4. Assert the tracing span

```csharp
using var harness = new ApplicationPipelineTestHarness().WithActivityCapture();
// … register, Build, SendAsync …
Assert.Single(harness.CapturedActivities, a => Equals(a.GetTagItem("request.type"), typeof(PlaceOrder).FullName));
```

## Reference

### `ApplicationPipelineTestHarness` (`IDisposable`)

| Member | Behaviour |
| --- | --- |
| `Services` | The `ServiceCollection` to register handlers, validators and fakes on; call before `Build` |
| `Configure(Action<ApplicationPipelineBuilder>)` | The opt-ins, as passed to `AddSharedKernelApplication`; replaces an earlier `Configure` |
| `WithActivityCapture()` | Records every activity started on the `SharedKernel.Application` source |
| `Build()` | Registers the pipeline with no mediator, builds the provider, runs the start-time check |
| `Build<TMarker>()` | Registers the pipeline over `TMarker`'s assembly with `UseMediatR()`, builds, runs the check |
| `SendAsync<TResponse>(IRequest<TResponse>, CancellationToken)` | Sends through the registered `ISender`; throws `InvalidOperationException` before `Build` |
| `SendThroughPipelineAsync<TRequest, TResponse>(TRequest, CancellationToken)` | Resolves `RequestPipeline<TRequest, TResponse>` and runs it directly, in either mode |
| `CapturedActivities` | `IReadOnlyList<Activity>`; empty unless `WithActivityCapture()` was called |
| `CapturedMeasurements` | `(InstrumentName, Value, Tags)` of every `double` measurement on the `SharedKernel.Application` meter |

### `AddFakeApplicationBehaviorServices(this IServiceCollection)`

| Registers | As | From |
| --- | --- | --- |
| `FakeUnitOfWork` | `IUnitOfWork`, singleton | `SharedKernel.Persistence.Testing` |
| `FakeRequestContext` (authenticated, no permissions) | `IRequestContext`, singleton | `SharedKernel.Testing` |
| `FakeIdempotencyStore` | `IIdempotencyStore` keyed `IdempotencyPurpose.Request`, singleton | `SharedKernel.Idempotency.Testing` |

It satisfies the seam check of `WithTransactions()`, `WithIdempotency()` and `[RequirePermission]` requests, before or
after the registration call. It does not register an `IAuditTrailWriter` (`WithAuditing()`).

## Testing

This package is itself the test harness; its self-tests live in
[`SharedKernel.Application.Testing.Tests`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/tree/main/src/Testing/SharedKernel.Application.Testing/SharedKernel.Application.Testing.Tests)
and prove both build modes against the production pipeline — authorization codes, idempotency replay and conflict,
the start-time check. Pair it with
[`SharedKernel.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Testing/SharedKernel.Testing/README.md)
(`FakeRequestContext`, `TestRequestContext`, `FakeClock`) and
[`SharedKernel.Persistence.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Testing/SharedKernel.Persistence.Testing/README.md)
(`FakeUnitOfWork.TransientFailures` proves a handler re-runnable under `WithTransactions()`).

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Reference it from production code | Reference it from test projects only | `TestingNeverReferencedByProduction` fails the build |
| Register services after `Build()` | Register everything on `Services`, then build | The provider is built once; later registrations are never seen |
| Expect `Build()` to find your handlers | Register them yourself, or use `Build<TMarker>()` | `Build()` scans only the harness's own assembly |
| Look for the harness's service provider | Register your own fake instance and keep the reference | Only `Services` is public |
| Rely on `AddFakeApplicationBehaviorServices()`'s unit of work to roll back fake repositories | Use `AddFakeUnitOfWork()` + `AddFakeRepository<,>()` | Only fakes registered through the `Persistence.Testing` helpers are linked for rollback |
| Assert on `CapturedActivities` without opting in | Call `WithActivityCapture()` before sending | Activities are captured only on request |
| Skip `Dispose()` | `using var harness = …` | The meter and activity listeners are process-wide until disposed |

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) · [16.Testing domain](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Testing/README.md) · [MIT license](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
