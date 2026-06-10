---
name: project_routingslip_testharness_patterns
description: MassTransit Courier RoutingSlip TestHarness test patterns (RS-08/RS-09) — execution order, compensation, locale-safe decimals
metadata:
  type: project
---

SK.07.RoutingSlip (WO-022, P-139) is complete (10/10, ●). Test file:
`07.Messaging/SharedKernel.Messaging.MassTransit/SharedKernel.Messaging.MassTransit.Tests/HarnessTests/RoutingSlipTests.cs`

Patterns for future Courier/RoutingSlip TestHarness tests:

- Resolve activity addresses via `harness.GetExecuteActivityAddress<TActivity, TArguments>()` — do not hardcode queue URIs.
- Build the slip with `ISkRoutingSlipBuilder` (alias `SharedKernel.Messaging.Abstractions.RoutingSlips.IRoutingSlipBuilder` to avoid CS0104 collision with `MassTransit.IRoutingSlipBuilder`), constructed directly as `new MassTransitRoutingSlipBuilder()`.
- Construct `MassTransitMessageBus` directly in tests with 5 args: `(harness.Bus, harness.Bus, provider, new Dictionary<Type,string>(), new ConventionSendEndpointResolver(Options.Create(new MessagingOptions { ServiceName = "test-service" })))`. `harness.Bus` serves as both `IPublishEndpoint` and `ISendEndpointProvider`.
- Dispatch via `await bus.ExecuteRoutingSlipAsync(slip, ct)` then `await harness.InactivityTask`.
- Assert lifecycle via `harness.Published.Any<MassTransit.Courier.Contracts.RoutingSlipCompleted>()` / `RoutingSlipFaulted>()`.
- **Locale bug**: never compare against a literal decimal string like `"49.99"` — `decimal.ToString()` is locale-dependent (e.g. Turkish locale produces `"49,99"`). Always use `.ToString(CultureInfo.InvariantCulture)` in both the activity's recorder call and the test's expected-value string.
- Test instrumentation types must be `internal sealed` (not `file`) — `file`-scoped types break MassTransit's type registration/matching in `AddMassTransitTestHarness`.
- A shared `ExecutionRecorder` singleton (thread-safe via `Lock`) registered with `.AddSingleton(recorder)` is the simplest way to assert cross-activity execution/compensation order.
- Compensation test (RS-09): make the second activity always throw in `ExecuteAsync` (e.g. `FailingChargeCardActivity`); assert the first activity's `CompensateAsync` recorded the matching log values — double check the recorded compensation string matches the actual `Quantity`/argument values used when building the slip (easy off-by-one to introduce in test data).

See also [[project_masstransit_testing_patterns]] for general TestHarness conventions (await using ServiceProvider, internal sealed types, etc.).
