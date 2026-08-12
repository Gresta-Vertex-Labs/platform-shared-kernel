---
name: grpc_polly_test_capture_pattern
description: Reusable recipe for a real in-process gRPC call + a real Polly retry pipeline, captured via OTel, to prove instrumentation genuinely fired — used for T-43 (WO-056/P-365)
metadata:
  type: project
---

T-43 (WO-056/P-365, 2026-08-12) needed genuine proof — not registration-count-only — that
`WithCommunicationTelemetry()`'s gRPC and Polly wiring actually fire. No `.proto` file existed
anywhere in the repo before this session (checked `11.Communication.Grpc.Tests`, which only uses
`Grpc.Core.Testing`'s `TestServerCallContext` for interceptor unit tests — no real server/client).
Built the following pattern from scratch; reuse it verbatim for any future genuine-gRPC-call proof
needed elsewhere in the platform.

**gRPC half — real in-process call via `WebApplicationFactory` + `Grpc.Net.Client`:**
1. Add a minimal `.proto` (`Telemetry/GrpcFixtures/greeter.proto` in this session) + `Grpc.AspNetCore`
   (server + `Grpc.Tools` codegen + `Google.Protobuf`, all transitively bundled) + `Grpc.Net.Client`
   (client) as **test-project-only** `PackageReference`s, `<Protobuf Include="..." GrpcServices="Both" />`
   to generate both server base class and client stub in the same assembly.
2. `WebApplicationFactory<TMarker>` needs a `TMarker` type — no real `Program`/`Startup` exists for a
   throwaway test host, so the marker class IS its own `WebApplicationFactory<T>` subclass
   (`GrpcTestWebApplicationFactory : WebApplicationFactory<GrpcTestWebApplicationFactory>`), overriding
   `CreateHostBuilder()` to wire `AddGrpc()` + `MapGrpcService<T>()`.
3. **Gotcha (silent `DirectoryNotFoundException`):** `WebApplicationFactory`'s default content-root
   resolution walks up from the entry-point assembly's *name* looking for a matching project folder —
   fails immediately for a marker type with no real project folder of that name (this domain nests test
   projects under `13.ServiceDefaults/SharedKernel.ServiceDefaults/...Tests/`, not a top-level folder
   matching the assembly name). Fix: override
   `protected override IHost CreateHost(IHostBuilder builder) { builder.UseContentRoot(AppContext.BaseDirectory); return base.CreateHost(builder); }`
   — pins to the test binary's own output directory, which always exists; the actual value is
   irrelevant since this host serves no static content.
4. **Gotcha (`Grpc.Net.Client` response-version validation):** `TestServer`'s in-memory
   `HttpMessageHandler` doesn't report an HTTP/2 response version by default, and `Grpc.Net.Client`
   rejects a response whose reported version isn't ≥ 2.0. Fix (Microsoft's own documented workaround
   for "Test gRPC services in ASP.NET Core"): a `DelegatingHandler` (`ResponseVersionHandler`) that sets
   `response.Version = request.Version` before returning, passed to
   `factory.CreateDefaultClient(new ResponseVersionHandler())`, then
   `GrpcChannel.ForAddress(client.BaseAddress!, new GrpcChannelOptions { HttpClient = client })`.
5. The captured span comes through this same test project's established
   `BaseProcessor<Activity>` + `provider.GetRequiredService<TracerProvider>()` idiom
   ([[otel_wiring_pattern]]) — wire `.AddHttpClientInstrumentation()` (the base span source) alongside
   `WithCommunicationTelemetry()` (which adds `.AddGrpcClientInstrumentation()`, enriching that same
   span with `rpc.system`/etc. tags rather than creating a separate one). **Empirically confirmed:**
   removing `.AddGrpcClientInstrumentation()` from the production method makes the captured-activity
   list come back completely EMPTY for the gRPC call — not merely missing the `rpc.system` tag — meaning
   the grpc instrumentation package is the one actually creating the Activity for this call shape, not
   merely tagging an HTTP-instrumentation-created one. Don't be surprised by an empty collection in this
   specific negative-control check; it's still the correct failure signature.

**Polly half — real retry pipeline, metrics only (no span exists — see
[[decompile_verification_technique]]):**
1. `Polly.Core`/`Polly.Extensions` as test-project-only `PackageReference`s. **Version conflict:** the
   production code pins `8.4.2` (verified via decompilation, see [[decompile_verification_technique]]),
   but this test project transitively pulls `SharedKernel.Testing -> SharedKernel.Application.Behaviors`,
   which floors `Polly.Core` at `>= 8.7.0` — NU1605 (an error under this solution's
   `TreatWarningsAsErrors`) forbids pinning below a floor already set elsewhere in the graph. Just bump
   the test project's pin to `8.7.0` (both `Polly.Core` and `Polly.Extensions`, matched) — the `"Polly"`
   Meter name/version this proof targets is a stable, documented public surface, not a
   version-pinned implementation detail, so the mismatch with production's `8.4.2` doesn't weaken the
   proof.
2. Build a minimal standalone pipeline: `new ResiliencePipelineBuilder().AddRetry(new RetryStrategyOptions
   { ShouldHandle = new PredicateBuilder().Handle<InvalidOperationException>(), MaxRetryAttempts = 1,
   Delay = TimeSpan.Zero }).ConfigureTelemetry(new TelemetryOptions()).Build()`, then execute a delegate
   that throws once (forcing exactly one retry) and succeeds on the second attempt.
   **`ConfigureTelemetry(...)` is mandatory** — without it, Polly's `TelemetryListenerImpl` never
   activates and no measurement is recorded regardless of whether an OTel `MeterListener` is subscribed.
3. Capture via `OpenTelemetry.Exporter.InMemory`'s `MeterProviderBuilder.AddInMemoryExporter(List<Metric>)`
   — the first metrics-capture-via-InMemoryExporter precedent in this test suite (every prior
   `With*Telemetry` sibling only asserted `MeterProviderBuilder` DI-registration counts, never a genuine
   metric). Must call `meterProvider.ForceFlush()` after the pipeline execution to force the reader to
   collect before asserting on the exported list (the default periodic reader interval is otherwise
   60s). Assert `exportedMetrics.Any(m => m.MeterName == "Polly")`.
4. For the idempotency-of-metric proof: run the identical retry execution twice, once against a
   single-`WithCommunicationTelemetry()`-registration provider and once against a
   double-registration provider (each in its own isolated `WebApplication`/`MeterProvider`, disposed
   between runs since the underlying `Meter("Polly","1.0")` is a process-wide static instance — only an
   *actively subscribed* `MeterListener` receives measurements, so sequential dispose-then-rebuild
   correctly isolates each run), then assert the two exported counts are equal.

**Both halves' negative-control discipline:** temporarily comment out the specific wiring line inside
the production `WithCommunicationTelemetry()` method (replace `tracing.AddGrpcClientInstrumentation()`
with `tracing => { }` or `metrics.AddMeter(PollyInstrumentationName)` with `metrics => { }`), re-run just
the affected tests to confirm they genuinely fail, then restore the original line — mirrors the
established T-39/T-40 verification discipline. Confirmed both halves fail correctly and independently
(gRPC removal doesn't break the Polly tests and vice versa).

Result: `SharedKernel.ServiceDefaults.Tests` went from 96/96 to 100/100 (+4 new gating tests), 0
regressions.
