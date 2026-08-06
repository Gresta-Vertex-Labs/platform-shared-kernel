---
name: masstransit-header-propagation
description: IMessageHeaderPropagator wiring patterns, ConsumerBase x-sk-* header extraction, PublishContext propagator precedence, and test patterns for SK.07.HeaderPropagation and SK.07.PropagationSymmetry
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

## Propagation symmetry across all dispatch verbs (SK.07.PropagationSymmetry, P-341/WO-054)

**Confirmed defect pattern:** `MassTransitMessageBus.SendAsync<T>()` and `RequestAsync<TRequest,TResponse>()` originally never called the private `BuildContextFromPropagators` helper that both `PublishAsync` overloads already used — a silent asymmetry where registered `IMessageHeaderPropagator`s only ever ran on the fan-out publish path. Fixed by calling `BuildContextFromPropagators(configure: null)` at the top of both methods (after resolving the send endpoint in `SendAsync`, before resolving the request client in `RequestAsync`). Since neither verb has an `Action<PublishContext>` overload, "explicit callback wins" reduces to "none" — propagator output alone determines the resulting `CorrelationId`/headers on these two verbs.

**`ISendEndpoint.Send` pipe overload:** `MassTransit.SendExecuteExtensions.Send<T>(this ISendEndpoint endpoint, T message, Action<SendContext<T>> callback, CancellationToken ct)` — identical shape to `IPublishEndpoint.Publish`'s pipe callback. `SendContext<T>` has a settable `CorrelationId` (`Guid?`) and a `Headers` (`SendHeaders`, has `.Set(key, value)`).

**`IRequestClient<TRequest>.GetResponse` has NO equivalent raw pipe overload.** Its only configuration hook is `GetResponse<TResponse>(TRequest message, RequestPipeConfiguratorCallback<TRequest> callback, CancellationToken ct, RequestTimeout timeout)`, returning `Task<Response<TResponse>>`. The callback receives an `IRequestPipeConfigurator<TRequest>`, which does NOT expose `CorrelationId`/`Headers` directly — it only implements `IPipeConfigurator<SendContext<TRequest>>` (via `AddPipeSpecification`). Reach the underlying `SendContext<TRequest>` (same settable `CorrelationId`/`Headers.Set` shape as `Send`/`Publish`) via `MassTransit.DelegateConfigurationExtensions.UseExecute<TContext>(this IPipeConfigurator<TContext> configurator, Action<TContext> callback)`:
```csharp
await client.GetResponse<TResponse>(request, cfg => cfg.UseExecute(sendContext =>
{
    if (ctx.CorrelationId.HasValue) sendContext.CorrelationId = ctx.CorrelationId.Value;
    foreach (var (k, v) in ctx.Headers) sendContext.Headers.Set(k, v);
}), ct);
```
This signature chain (`IRequestClient<T>` → `RequestPipeConfiguratorCallback<T>` → `IRequestPipeConfigurator<T>` → `IPipeConfigurator<SendContext<T>>` → `UseExecute`) is NOT documented anywhere obvious — confirmed by .NET reflection against the installed `MassTransit.Abstractions` 9.1.2 assembly (`MassTransit.Abstractions.dll`) rather than found in XML docs, which describe the parameter only as `<param name="callback"></param>` with no elaboration. Use a throwaway reflection console app (`Assembly.LoadFrom` + `GetMethods`/`GetInterfaces`) against the NuGet-cached DLL when the next ambiguous MassTransit 9.x API question comes up — faster and more reliable than guessing from XML docs alone.

**Test pattern for `SendAsync` propagator assertions (point-to-point, not fan-out):** `TestHarness`'s `ConfigureEndpoints(ctx)` convention-maps consumer types to queue names via `KebabCaseEndpointNameFormatter`, which is fragile to replicate exactly for a `SendAsync` route-map override. Simpler and deterministic: pick an explicit queue name string, register it in both the route map (`IReadOnlyDictionary<Type,string>` singleton passed to `MassTransitMessageBus`) AND an explicit `busCfg.ReceiveEndpoint(queueName, e => e.ConfigureConsumer<TConsumer>(ctx))` (skip `cfg.AddConsumer<TConsumer>()` + `ConfigureEndpoints` convention entirely). This guarantees the `SendAsync`-resolved queue name and the consumer's actual receive endpoint match without needing to reverse-engineer the kebab-case/Consumer-suffix-stripping convention.

**Test pattern for `RequestAsync` propagator assertions:** a responding `IConsumer<TRequest>` captures `ConsumeContext<TRequest>.CorrelationId` (`Guid?`) into a static store, then calls `await context.RespondAsync(new TResponse(...))` so `RequestAsync` actually completes. Assert the captured value equals the propagator-set `Guid`.
