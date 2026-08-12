---
name: feedback-grpc-deadline-test-technique
description: How to write a genuine behavioral test proving Grpc.Net.ClientFactory's per-call deadline is enforced, and how to seed KubernetesServiceEndpointResolver's TTL cache for stale-while-revalidate tests — both discovered during 11.Communication WO-056 T-33/T-35
metadata:
  type: feedback
---

Two reusable test techniques discovered while implementing `11.Communication`'s WO-056 T-33/T-35
(deferred regression tests for P-357/P-359). Both required empirical verification against the real
third-party assembly before writing test code — reuse these rather than re-deriving from scratch.

## 1. gRPC deadline-exceeded behavioral proof

To prove a call issued through a client built via `AddGrpcClient<TClient>` genuinely faults with
`RpcException`/`StatusCode.DeadlineExceeded` once `DeadlineSeconds` elapses (not merely that the
option is documented), you need a real call to actually time out — a DI-resolution smoke test proves
nothing here.

**Recipe:**
1. Register the typed client normally: `services.AddSharedKernelGrpcCommunication().AddGrpcClient<TClient>(address, o => o.DeadlineSeconds = 1)`.
2. Call `services.AddGrpcClient<TClient>()` (the **parameterless** overload) a **second** time and
   chain `.ConfigurePrimaryHttpMessageHandler(() => new NeverRespondingHandler())`. This is additive
   configuration against the SAME named `HttpClient` the first call already registered — not a
   conflicting second registration. `Grpc.Net.ClientFactory`'s `AddGrpcClient<TClient>` keys the
   named client by `typeof(TClient).Name`, so repeated calls accumulate.
3. `NeverRespondingHandler : HttpMessageHandler` just does
   `await Task.Delay(Timeout.Infinite, cancellationToken)` inside `SendAsync` — the deadline timer,
   not a real response, is what completes the call.
4. The typed client's constructor **must accept `CallInvoker`, not `ChannelBase`**.
   `Grpc.Net.ClientFactory`'s `DefaultClientActivator<T>` specifically looks for a
   `CallInvoker`-accepting constructor when activating a typed client through DI — a `ChannelBase`-only
   constructor throws `InvalidOperationException: A suitable constructor... could not be located` at
   resolution time. (A `ChannelBase` constructor is fine for a pure DI-resolution smoke test that never
   issues a call — that's why the pre-existing `FakeGrpcClient` test double in this domain uses it — but
   it breaks the moment you actually try to invoke a method.)
5. Give the client one real unary method backed by `CallInvoker.AsyncUnaryCall(method, host, options, request)`
   using a hand-rolled `Method<string,string>` (mirrors `CorrelationTracingInterceptorTests`'s existing
   `TestMethod` pattern).
6. Assert: `await act.Should().ThrowAsync<RpcException>()` then `.Which.StatusCode.Should().Be(StatusCode.DeadlineExceeded)`.

**Why:** confirms `GrpcClientFactoryOptions.CallOptionsActions`-based deadline wiring (see
`[[project_communication]]`'s "Grpc.Net.ClientFactory real deadline mechanism" entry) is a REAL,
enforced deadline, not merely a documented-but-unconsumed option — the exact class of defect P-359
fixed and 07.Messaging's `MaxConcurrentCalls` (WO-054/P-342) was the platform precedent for.

## 2. `KubernetesServiceEndpointResolver` TTL-cache seeding via reflection

**The trap:** `Microsoft.Extensions.ServiceDiscovery`'s real `ServiceEndpointResolver.GetEndpointsAsync`
does NOT re-invoke its `IServiceEndpointProviderFactory` for a query string that has already resolved
successfully within that resolver instance — confirmed empirically via a throwaway console harness
(register a factory, call `GetEndpointsAsync` twice with the identical query string, flip a
`ShouldFail` flag on the factory between calls — the second call still returns the FIRST successful
result, the factory's `TryCreateProvider`/`PopulateAsync` are never invoked again). This means you
CANNOT drive a "cache populated successfully, then DNS starts failing" scenario through the real
`ServiceEndpointResolver` machinery — there is no way to make a previously-successful query fail later
within one resolver instance.

**The fix:** don't try to make DNS succeed-then-fail. Instead:
1. Directly instantiate `KubernetesServiceEndpointResolver` (bypass `AddK8sServiceDiscovery` — its
   constructor and private `_cache` field are reachable from the test project since
   `InternalsVisibleTo` covers `internal` members, and reflection covers the rest).
2. Seed `_cache` (a `private readonly ConcurrentDictionary<string, CachedEntry>`, where `CachedEntry`
   is a `private` nested `readonly record struct(Uri Uri, DateTimeOffset ExpiresAt)`) directly via
   reflection:
   ```csharp
   var cacheField = typeof(KubernetesServiceEndpointResolver)
       .GetField("_cache", BindingFlags.NonPublic | BindingFlags.Instance)!;
   var cachedEntryType = typeof(KubernetesServiceEndpointResolver)
       .GetNestedType("CachedEntry", BindingFlags.NonPublic)!;
   var cache = (IDictionary)cacheField.GetValue(resolverInstance)!;
   cache[serviceName] = Activator.CreateInstance(cachedEntryType, uri, expiresAt)!;
   ```
3. Use an underlying DNS provider that ALWAYS fails from the first call (never relies on a prior real
   success) — a minimal `IServiceEndpointProviderFactory`/`IServiceEndpointProvider` pair whose
   `PopulateAsync` throws unconditionally is sufficient; confirmed the thrown exception propagates
   synchronously out of `GetEndpointsAsync` (not swallowed/converted internally), so production's
   `catch (Exception ex) when (ex is not OperationCanceledException)` catches it exactly as designed.
4. Drive all three TTL branches (cache-hit / cache-expired-fresh-lookup-succeeds /
   cache-expired-DNS-fails-returns-stale) via `FakeClock.Advance(...)` against the seeded `ExpiresAt`.

This is the same class of test-only reflection already sanctioned elsewhere on the platform
(`16.Testing`'s `Domain/SpecificationAssert`, `Containers/MilvusContainerFixtureTests`'s private
`_container` field access) — never acceptable in production code, always justified when the real
library's own caching semantics make the behavior otherwise undrivable.

**To get `ILogger<KubernetesServiceEndpointResolver>` for the stale-cache-logs-Warning assertion:**
build a small `ServiceCollection` with `services.AddLogging(b => b.AddProvider(new TestLoggerProvider(sink)))`
(the `TestLogSink`/`TestLoggerProvider`/`TestLogger` trio already exists `internal` in
`ServiceCollectionExtensionsTests.cs`, same assembly/namespace — reuse directly, don't redefine), then
`sp.GetRequiredService<ILogger<KubernetesServiceEndpointResolver>>()` — resolves via the BCL's real
`Logger<T>` open-generic adapter, registered automatically by `AddLogging()`.

## Related gotchas hit while writing these tests

- `entry!.Value` where `entry` is `Metadata.Entry?` (a `Grpc.Core.Metadata.Entry?`) — the null-forgiving
  `!` operator on a `Nullable<T>` value-type expression actually unwraps it to `T`, not merely
  suppresses a nullable-reference warning. So `entry!.Value` gives you `Metadata.Entry.Value` (the
  string) directly — do NOT write `entry!.Value.Value`, that's a compile error (`string` has no
  `.Value`). This is genuinely different from `!` on reference types.
- `Options.Create(...)` inside a file that also has `using SharedKernel.Communication.Internal.Options;`
  (or any `...Options` sub-namespace) is ambiguous — the namespace segment shadows the
  `Microsoft.Extensions.Options.Options` static class for a bare `Options.Create(...)` call. Fully
  qualify as `Microsoft.Extensions.Options.Options.Create(...)`.
- A `ServiceProvider` holding a `ServiceEndpointResolver` (only `IAsyncDisposable`) must not be wrapped
  in a synchronous `using var sp = ...` inside a helper method whose caller needs `sp`-derived objects
  to outlive the method — `Dispose()` throws `InvalidOperationException` telling you to use
  `DisposeAsync`. Simplest fix in a test helper: just don't dispose it (acceptable resource-leak
  tradeoff in a short-lived unit test).
