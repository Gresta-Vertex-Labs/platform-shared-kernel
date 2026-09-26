---
name: masstransit-concurrency-wiring
description: MassTransit 9.1.2 bus-level concurrency-limit API facts (obsolete MaxConcurrentCalls, write-only ConcurrentMessageLimit/PrefetchCount) and the NSubstitute/Castle proxy limitation for internal consumer types, discovered during SK.07.ConsumerConcurrency (P-342/WO-054)
metadata:
  type: project
---

> WO-086 (2026-09): recorded on MassTransit 9.1.2; the platform is pinned to 8.5.x (P-560) — re-verify. The RabbitMQ/Azure Service Bus configuration helpers and `RabbitMqBusOptions`/`AzureServiceBusOptions` moved out of `MessagingBusBuilder` into the satellites `SharedKernel.Messaging.MassTransit.RabbitMq`/`.AzureServiceBus`.

# MassTransit 9.1.2 Concurrency-Limit API Facts (SK.07.ConsumerConcurrency, P-342/WO-054)

## `IServiceBusEndpointConfigurator.MaxConcurrentCalls` is obsolete
Compiles with `CS0618`: "Set ConcurrentMessageLimit instead (which is exactly what setting this
property does)." The correct API is the core `IBusFactoryConfigurator.ConcurrentMessageLimit`
(`int?`) — the SAME property RabbitMQ's bus configurator uses. `IServiceBusBusFactoryConfigurator`
inherits it transitively via `IBusFactoryConfigurator`, so `cfg.ConcurrentMessageLimit = value;`
works directly on the ASB bus configurator, no cast/downcast needed. Both transports now share one
bus-level concurrency-default property in 9.1.2 — this was NOT true in earlier MassTransit versions
(hence the still-shipped, still-typed, but functionally dead `AzureServiceBusOptions.MaxConcurrentCalls`
in this codebase, which P-342 wires to `ConcurrentMessageLimit` under the hood, not to the obsolete
ASB-specific setter).

## `IBusFactoryConfigurator.ConcurrentMessageLimit`/`.PrefetchCount` are WRITE-ONLY
Both compile for assignment (`cfg.ConcurrentMessageLimit = 5;`) but fail with `CS0154` ("lacks the
get accessor") when read back (`cfg.ConcurrentMessageLimit` as an expression). This is specific to
the BUS-LEVEL configurator interface. The per-ENDPOINT `IReceiveEndpointConfigurator.ConcurrentMessageLimit`/
`.PrefetchCount` have BOTH accessors (get works fine there — used by `ConsumerDefinitionBase`'s
existing `PrefetchCount` step and the new `ConcurrentMessageLimit` step (e)).

**Test implication:** unit tests proving bus-level wiring (e.g. `ConfigureAzureServiceBus`/
`ConfigureRabbitMq` helpers in `MessagingBusBuilder`) must substitute the configurator with
NSubstitute and assert via `substitute.Received(1).ConcurrentMessageLimit = expectedValue;` (setter-
call verification) — reading the property back to assert `.Should().Be(...)` is a **compile error**,
not a runtime failure, so this surfaces immediately during `dotnet build`, not during test-run.

## `MassTransit.ConsumerDefinition<TConsumer>` already declares its own `ConcurrentMessageLimit`
Public get, protected set — separate from the platform's `ConsumerDefinitionBase<TConsumer>`. Any
new same-named override property added to `ConsumerDefinitionBase<TConsumer>` MUST use the `new`
keyword to shadow cleanly (`protected virtual new int? ConcurrentMessageLimit => null;`), exactly
like the existing `EndpointName` property already does for the same reason (MassTransit's base
class declares a write-only `EndpointName` too — set-only, no getter). Omitting `new` produces
`CS0108` (hide-inherited-member warning), which fails the build under this repo's
warnings-as-errors policy. **When adding ANY new override property to `ConsumerDefinitionBase<T>`,
always check `MassTransit.ConsumerDefinition<T>`'s own member list first** (via `dotnet build` or
reflection) — this is now the third property name that could collide (`EndpointName`,
`ConcurrentMessageLimit`, and implicitly `PrefetchCount` — though `PrefetchCount` happens not to
collide, confirmed empirically, since MassTransit's base class has no such member).

## NSubstitute/Castle DynamicProxy cannot proxy a strong-named-assembly generic interface closed over an `internal` type argument
`Substitute.For<IConsumerConfigurator<TConsumer>>()` throws `ArgumentException` ("...because
assembly MassTransit.Abstractions is strong-named...") when `TConsumer` is `internal` to the test
assembly. Castle's dynamically generated proxy assembly has no `InternalsVisibleTo` grant for the
internal type, and adding one is impractical (would require hardcoding Castle's dynamic-assembly
public key). **Fix: make the consumer type (and its message type, since it appears in the
consumer's public surface) `public`, not `internal`.** This ONLY matters when a test directly
substitutes a MassTransit generic configurator interface (bypassing `TestHarness`) — e.g. calling
`IConsumerDefinition<TConsumer>.Configure(endpointConfigurator, consumerConfigurator, context)`
directly with `Substitute.For<IReceiveEndpointConfigurator>()`/`Substitute.For<IConsumerConfigurator<TConsumer>>()`.
Tests going through `TestHarness`/`AddMassTransitTestHarness` never hit this — the harness builds
real MassTransit types, not NSubstitute proxies. This is a narrow, documented exception to this
domain's usual "always use `internal` for test consumer/message types" convention (see
[[masstransit-9x-testing-patterns]]).

## Reflection-against-installed-DLLs technique for uncertain MassTransit interface shapes
When XML docs don't state an interface member's exact accessor shape or declaring type in a deep
inheritance chain, `dotnet build` on a throwaway usage is the fastest ground truth — the compiler
error names the exact declaring type and accessor (`CS0154`/`CS0618`/etc.). For questions a compile
error can't answer (does a member exist at all, full inheritance chain across many types), a
throwaway `System.Reflection` console app against the NuGet-cached DLLs
(`~/.nuget/packages/masstransit*/9.1.2/lib/net10.0/*.dll`) works well — register an
`AppDomain.CurrentDomain.AssemblyResolve` handler that searches the whole NuGet cache by simple
assembly name (`Directory.EnumerateFiles(nugetRoot, name + ".dll", SearchOption.AllDirectories)`,
prefer `net10.0`/`net9.0`/`net8.0` paths) to pull in transitive dependencies (e.g.
`Azure.Messaging.ServiceBus`) that plain `Assembly.LoadFrom` won't resolve — otherwise reflecting
over `MethodInfo.ToString()`/parameter types throws `FileNotFoundException` mid-dump.

**Why:** Phase SK.07.ConsumerConcurrency (P-342/WO-054) implemented 2026-08-06; 119/119
`SharedKernel.Messaging.MassTransit.Tests` passing (was 113).
**How to apply:** Consult before touching `AzureServiceBusOptions`/`RabbitMqBusOptions`/
`ConsumerDefinitionBase` concurrency surfaces, or before writing a test that substitutes any
MassTransit bus-level or receive-endpoint configurator interface.
