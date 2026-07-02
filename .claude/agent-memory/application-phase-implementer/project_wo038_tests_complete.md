---
name: project-wo038-tests-complete
description: WO-038 Tests phase completion details — key fixes, patterns, and pitfalls for T-17..T-32
metadata:
  type: project
---

SK.05.Tests phase (T-17..T-32) completed 2026-07-02. All 32 test tasks ● (28 + 92 = 120 tests passing).

## Critical implementation fixes discovered during this phase

**`await Task.WhenAll()` unwraps `AggregateException`**
`MediatRDomainEventDispatcher.DispatchParallelAsync` must NOT rely on `await whenAllTask` to propagate the full `AggregateException`. Standard C# always unwraps to the first inner exception. Fix: catch after `await`, then `throw whenAllTask.Exception` explicitly if non-null.

**`LoggerMessage.Define` + NSubstitute `IsEnabled()` = silent logs**
NSubstitute `ILogger<T>` mocks return `false` from `IsEnabled()` by default. `LoggerMessage.Define`-generated delegates check `IsEnabled()` before calling `Log()`. Result: all log assertions fail silently. Fix: use a hand-rolled `RecordingLogger<T>` that always returns `true` from `IsEnabled()`. Pattern established in `StreamLoggingBehaviorTests`.

**Shared static `ApplicationDiagnostics.Meter` cross-test pollution**
`MetricsBehavior` records to a single `Meter` shared across all test runs. `ContainSingle()` assertions fail when other tests run concurrently. Fix: filter `capture.Measurements` by `RequestName` tag before asserting `ContainSingle()`.

**`ChannelFireAndForgetDispatcher` is internal — cannot reference from tests**
`FireAndForgetDispatcherTests` consumer tests must wire `Channel<IFireAndForgetCommand>` + `FireAndForgetBackgroundConsumer` directly, bypassing `ChannelFireAndForgetDispatcher`. Use `ChannelWriter<IFireAndForgetCommand>` directly to enqueue commands for consumer tests.

**`FireAndForgetGuardBehavior` intercepts ALL `ISender.Send()` calls including from the background consumer**
The guard is registered as unconstrained open-generic `IPipelineBehavior<,>`. Consumer tests cannot use `ISender.Send(IFireAndForgetCommand)` — the guard rejects it. Build a separate `BuildConsumerDirectProvider()` that registers the channel + consumer WITHOUT `AddFireAndForgetDispatch()`.

**`CS0104 ValidationException` ambiguity in streaming test files**
Both `SharedKernel.Core.Exceptions.ValidationException` and `FluentValidation.ValidationException` are in scope. Fix: `using ValidationException = SharedKernel.Core.Exceptions.ValidationException;` alias at file top.

**`CS8425 [EnumeratorCancellation]` warnings in async iterator test handlers**
All async iterator handler methods in test files need `[EnumeratorCancellation]` on the `CancellationToken` parameter and `using System.Runtime.CompilerServices;`.

## T-17 deliberate exception

`PipelineTestHarness` is most valuable for tests needing `ActivityListener`/`MeterListener` capture (TracingBehavior, MetricsBehavior). Existing behavior tests (Validation, Logging, Transaction, Authorization, Idempotency, Caching) are clearer with direct `ServiceCollection` setup. T-17 marked complete with per-file exception documented.

**Why:** Forcing all tests through a harness adds accidental complexity where direct setup is simpler and equally valid. The harness is an opt-in pattern, not a mandate.

**How to apply:** New streaming/metrics/tracing tests should use the harness pattern. Behavior-contract tests with simple pass/fail assertions can use direct `ServiceCollection` setup.
