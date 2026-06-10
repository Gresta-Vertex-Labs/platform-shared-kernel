---
name: masstransit-header-propagation
description: IMessageHeaderPropagator wiring patterns, ConsumerBase x-sk-* header extraction, PublishContext propagator precedence, and test patterns for SK.07.HeaderPropagation
metadata:
  type: project
---

## IMessageHeaderPropagator wiring (SK.07.HeaderPropagation)

**Registration:** `WithHeaderPropagator<T>()` calls `Services.AddScoped<IMessageHeaderPropagator, T>()` — additive, multiple registrations produce an `IEnumerable<IMessageHeaderPropagator>` in DI order.

**Propagator precedence rule:** Propagators run BEFORE the explicit `Action<PublishContext>` configure callback. When both set the same key, the explicit callback wins (runs last, overwrites).

**MassTransitEventPublisher:** injects `IEnumerable<IMessageHeaderPropagator>` via constructor. DI always provides a non-null enumerable (empty when none registered). Calls `propagator.Propagate(ctx)` on each, then `configure?.Invoke(ctx)`.

**MassTransitMessageBus:** resolves `IEnumerable<IMessageHeaderPropagator>` via `IServiceProvider.GetService<>()` at call time in `BuildContextFromPropagators()`. Returns `null` context when no propagators and no explicit callback (fast path — avoids pipe allocation). Non-null `configure` always produces non-null context (use `!` null-forgiving operator on result).

**ConsumerBase x-sk-* extraction:** `context.Headers.GetAll()` returns `IEnumerable<KeyValuePair<string, object?>>`. Filter keys with `StartsWith("x-sk-", StringComparison.OrdinalIgnoreCase)`. Add to `ILogger.BeginScope` dictionary. Non-x-sk-* headers are NOT added (intentional — avoids leaking transport metadata into logs).

**PublishContext alias in test files:** Test files that reference both `MassTransit.Testing` and `SharedKernel.Messaging.Abstractions.HeaderPropagation` must add:
`using MessagingPublishContext = SharedKernel.Messaging.Abstractions.EventPublisher.PublishContext;`
Same alias as in production code. Without it, CS0104 ambiguous reference compile error.

**Test pattern for propagator header assertions:** Use a static store (`ConcurrentBag<string>` or `Dictionary<string, string>`) populated by a `IConsumer<T>` implementation that reads `context.Headers.GetAll()`. Static stores must be reset before each test (`.Reset()` method). Do NOT use `file` modifier on consumer types.

**Test pattern for log scope assertion (x-sk-*):** Implement `ILogger` directly in the test file. In `BeginScope<TState>`, cast `state` to `IDictionary<string, object?>` and record keys to a static store. Pass this logger to the `ConsumerBase` constructor. Assert keys are/aren't present in the store after `harness.Consumed.Any<T>()` returns true.

**Why:** See [[masstransit-idempotency-filter-wiring]] for related cross-cutting filter patterns. Propagators are the publish-time equivalent of the consume-time idempotency filter.
