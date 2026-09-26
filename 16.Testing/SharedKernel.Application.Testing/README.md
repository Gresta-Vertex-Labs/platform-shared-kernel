# SharedKernel.Application.Testing

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![Tier: Testing](https://img.shields.io/badge/tier-Testing-orange)
![Test projects only](https://img.shields.io/badge/use-test%20projects%20only-orange)
![Test framework: any](https://img.shields.io/badge/test%20framework-any-informational)
![License: MIT](https://img.shields.io/badge/license-MIT-blue)

**Run a command or query through the real SharedKernel application pipeline in a unit test.**
`ApplicationPipelineTestHarness` registers the behaviors you choose — the same `ApplicationBehaviorsBuilder` a
service uses — sends the request through them, and captures the pipeline's activities and metric measurements.

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
| `ApplicationPipelineTestHarness` | `Services` (add handlers and fakes), `AddBehaviors()` (an `ApplicationBehaviorsBuilder`), `WithActivityCapture()`, then `Build()` or `Build<TMarker>()`; send with `SendThroughPipelineAsync<TRequest, TResponse>` or `SendAsync`; read `CapturedActivities` and `CapturedMeasurements` |
| `ApplicationServiceCollectionExtensions.AddFakeApplicationBehaviorServices()` | Registers the fakes the opt-in behaviors need, as singletons: `FakeUnitOfWork` (`IUnitOfWork`, from `SharedKernel.Persistence.Testing`), `FakeRequestContext` (`IRequestContext`, authenticated, from `SharedKernel.Testing`) and `FakeIdempotencyStore` for `IdempotencyPurpose.Request` (from `SharedKernel.Idempotency.Testing`) |

The two build modes run the same behaviors in the same order:

- **`Build()`** — no mediator. Register each handler on `Services` and send with `SendThroughPipelineAsync`, which
  resolves the kernel `RequestPipeline<TRequest, TResponse>` directly.
- **`Build<TMarker>()`** — also registers the MediatR adapter (`AddSharedKernelMediatR`) over `TMarker`'s assembly, so
  `SendAsync` goes through the kernel `ISender` exactly as the service does.

## Example

```csharp
using var harness = new ApplicationPipelineTestHarness();
harness.Services.AddFakeApplicationBehaviorServices();
harness.Services.AddScoped<IRequestHandler<PlaceOrder, Result<OrderId>>, PlaceOrderHandler>();
harness.AddBehaviors()
    .AddDefaultBehaviors()          // tracing, logging, metrics, validation
    .AddAuthorizationBehavior()
    .AddTransactionBehavior()
    .Build();
harness.WithActivityCapture().Build();

var result = await harness.SendThroughPipelineAsync<PlaceOrder, Result<OrderId>>(command);

result.IsSuccess.Should().BeTrue();
harness.CapturedActivities.Should().NotBeEmpty();     // the TracingBehavior span
harness.CapturedMeasurements.Should().NotBeEmpty();   // the MetricsBehavior duration
```

Call `Build()` after every behavior and handler registration. Assert on the fakes by resolving them from the
harness's service provider, or register your own instances on `Services` first.

## Related packages

- References `SharedKernel.Application.Pipeline`, `SharedKernel.Application.Mediator.MediatR`,
  `SharedKernel.Persistence.Testing` and `SharedKernel.Idempotency.Testing`.
- [`SharedKernel.Testing`](../SharedKernel.Testing/README.md) — `TestRequestContext`, `FakeRequestContext`,
  `InMemoryLogger`, `FakeClock`.
- [`SharedKernel.Persistence.Testing`](../SharedKernel.Persistence.Testing/README.md) — `FakeUnitOfWork` with
  `TransientFailures` to prove a handler is re-runnable under `TransactionBehavior`.
