# SharedKernel.Application.Testing

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![Tier: Testing](https://img.shields.io/badge/tier-Testing-orange)
![Test projects only](https://img.shields.io/badge/use-test%20projects%20only-orange)
![Test framework: any](https://img.shields.io/badge/test%20framework-any-informational)
![License: MIT](https://img.shields.io/badge/license-MIT-blue)

**Run a command or query through the real SharedKernel application pipeline in a unit test.**
`ApplicationPipelineTestHarness` registers the application layer with the same `AddSharedKernelApplication` call a
service makes, with the opt-in behaviors you choose, sends the request through them, runs the host's start-time seam
check, and captures the pipeline's activities and metric measurements.

## Install

Reference it from a **test project only**; an architecture test fails any production project that references a
testing package.

```xml
<PackageReference Include="SharedKernel.Application.Testing" />
```

Versions come from the single `SharedKernelVersion`. Namespace: `SharedKernel.Testing.Application`.

## Contents

| Type | What it does |
| --- | --- |
| `ApplicationPipelineTestHarness` | `Services` (add handlers and fakes), `Configure(app => app.WithTransactions()...)` (the opt-ins, as passed to `AddSharedKernelApplication`), `WithActivityCapture()`, then `Build()` or `Build<TMarker>()`; send with `SendAsync` or `SendThroughPipelineAsync<TRequest, TResponse>`; read `CapturedActivities` and `CapturedMeasurements` |
| `ApplicationServiceCollectionExtensions.AddFakeApplicationBehaviorServices()` | Registers the fakes the opt-in behaviors need, as singletons: `FakeUnitOfWork` (`IUnitOfWork`, from `SharedKernel.Persistence.Testing`), `FakeRequestContext` (`IRequestContext`, authenticated, from `SharedKernel.Testing`) and `FakeIdempotencyStore` for `IdempotencyPurpose.Request` (from `SharedKernel.Idempotency.Testing`) |

The two build modes run the same behaviors in the same order, and both run the start-time checks a host runs, so a
seam an opt-in needs and the test did not register fails the build with `OptionsValidationException`:

- **`Build()`** — no mediator and no assembly scan. Register each handler on `Services`; `SendAsync` goes through a
  harness `ISender` that runs the kernel `RequestPipeline<TRequest, TResponse>` directly (as does
  `SendThroughPipelineAsync`).
- **`Build<TMarker>()`** — `AddSharedKernelApplication(typeof(TMarker).Assembly, app => { configure(app);
  app.UseMediatR(); })`: the handlers, validators and domain-event handlers of `TMarker`'s assembly are registered for
  you, and `SendAsync` goes through the kernel `ISender` and the MediatR adapter exactly as the service does. When that
  assembly declares a `[RequirePermission]` request, register an `IRequestContext` first — authorization is always on.

## Example

```csharp
using var harness = new ApplicationPipelineTestHarness();
harness.Services.AddFakeApplicationBehaviorServices();
harness.Services.AddScoped<IRequestHandler<PlaceOrder, Result<OrderId>>, PlaceOrderHandler>();
harness
    .Configure(app => app.WithIdempotency().WithTransactions())  // tracing, logging, metrics, authorization, validation are always on
    .WithActivityCapture()
    .Build();

var result = await harness.SendAsync(command);

result.IsSuccess.Should().BeTrue();
harness.CapturedActivities.Should().NotBeEmpty();     // the TracingBehavior span
harness.CapturedMeasurements.Should().NotBeEmpty();   // the MetricsBehavior duration
```

Call `Build()` after every handler and fake registration. Assert on the fakes by resolving them from the
harness's service provider, or register your own instances on `Services` first.

## Related packages

- References `SharedKernel.Application.Pipeline`, `SharedKernel.Application.Mediator.MediatR`,
  `SharedKernel.Persistence.Testing` and `SharedKernel.Idempotency.Testing`.
- [`SharedKernel.Testing`](../SharedKernel.Testing/README.md) — `TestRequestContext`, `FakeRequestContext`,
  `InMemoryLogger`, `FakeClock`.
- [`SharedKernel.Persistence.Testing`](../SharedKernel.Persistence.Testing/README.md) — `FakeUnitOfWork` with
  `TransientFailures` to prove a handler is re-runnable under `TransactionBehavior`.
