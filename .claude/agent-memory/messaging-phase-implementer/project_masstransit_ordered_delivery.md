---
name: project_masstransit_ordered_delivery
description: MassTransit 9.1.2 partition-key ordered-delivery mapping (RabbitMQ routing-key / ASB SessionId) and a license-gate discovery affecting how to empirically probe MassTransit API behavior
type: project
---

Discovered/confirmed while implementing SK.07.OrderedDelivery (P-344/WO-054).

## The correct transport-agnostic APIs for partition-key mapping

- **RabbitMQ**: `MassTransit.RoutingKeyExtensions.TrySetRoutingKey(SendContext, string)` — lives in
  `MassTransit.Abstractions` (core `MassTransit` package, no extra NuGet ref needed). Also has a
  throwing `SetRoutingKey` variant and a `RoutingKey(SendContext)` getter for reading it back in
  tests. Confirmed empirically (via `AddMassTransitTestHarness`) to never throw regardless of
  configured transport.
- **Azure Service Bus**: `MassTransit.ServiceBusSendContextExtensions.SetSessionId(SendContext, string)`
  — lives in `MassTransit.Azure.ServiceBus.Core`. Internally does
  `context.TryGetPayload<ServiceBusSendContext>(out var ctx)` then `ctx.SessionId = value` — a safe,
  silent no-op under any non-ASB transport since that payload type is only attached by the real ASB
  transport (`MassTransit.AzureServiceBusTransport.AzureServiceBusSendContext<T>`) at actual send time.
  `ServiceBusSendContext.SessionId` (the public interface) is **setter-only, no getter** — same for
  `RoutingKeySendContext.RoutingKey`. Test both via NSubstitute `Received(1).Property = value` setter
  assertions (see `[[project_masstransit_concurrency_wiring]]`'s established write-only-property
  pattern from P-342's `ConcurrencyLimitConfigurationTests.cs` — this is now the second confirmed case
  of this pattern in the codebase).

Both are safe to call unconditionally from one shared code path (no transport branching needed) —
this is exactly what `PartitionKeySendContextExtensions.ApplyPartitionKey(this SendContext, string?)`
(`07.Messaging/SharedKernel.Messaging.MassTransit/MessageBus/`) does.

## License-gate discovery: how to empirically probe MassTransit API behavior safely

A plain `dotnet run` console host calling `services.AddMassTransit(cfg => cfg.UsingInMemory(...))`
then `provider.GetRequiredService<IBusControl>().StartAsync()` (or even
`AddMassTransitTestHarness(...)` + `harness.Start()` in a **non-test-runner** process) throws:

```
MassTransit.ConfigurationException: The bus configuration is invalid:
[Failure] License must be specified with SetLicense/SetLicenseLocation or by setting the
MT_LICENSE/MT_LICENSE_PATH environment variables.
```

This happens even for `UsingInMemory` with zero broker involved. **However**, the exact same
`AddMassTransitTestHarness(...)` + `harness.Start()` call succeeds with zero license configuration
when run via `dotnet test` (xUnit/VSTest) — confirmed by running the identical code both ways. The
existing 130+ passing tests in this repo's `SharedKernel.Messaging.MassTransit.Tests` never call
`SetLicense`/set `MT_LICENSE`, so this is not a repo-wide gap — it's specifically that MassTransit
9.x's test-harness bus factory path is exempt where the real bus-factory path is not.

**Practical implication**: to empirically verify uncertain MassTransit API behavior (which extension
method to use, whether something throws), do NOT write a standalone `dotnet run` scratch console app
— it will hit the license gate and give a false "throws" reading. Instead add a throwaway `[Fact]` to
the real `SharedKernel.Messaging.MassTransit.Tests` project (or any xUnit project referencing
MassTransit), run it via `dotnet test --filter`, read the result via a deliberate assertion failure
(`throw new Exception($"captured={value}")`) to print captured values, then delete the throwaway file
before finishing. This is faster and more reliable than reflection-based IL inspection for behavioral
(not just shape) questions, though IL/reflection inspection (see `RabbitMqTransportPropertyNames`
constants, `RabbitMqSendContextExtensions.SetRoutingKey`'s absence, `RoutingKeySendContext`'s
interface-hierarchy discovery) is still the right tool for discovering *which type/method exists* in
the first place — combine both: reflect to find candidates, `dotnet test`-probe to confirm behavior.

## Related

See [[project_masstransit_concurrency_wiring]] for the write-only-property NSubstitute test pattern
this phase reused a second time, and [[project_masstransit_header_propagation]] for the
`SendContext`/`PublishContext<T>` pipe-callback shape this phase's `ApplyPartitionKey` extension
plugs into (same `Action<PublishContext<T>>`/`Action<SendContext<T>>` lambda parameter already used
by `MassTransitMessageBus`/`MassTransitEventPublisher` for CorrelationId/Headers).
