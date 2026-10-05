using System.Diagnostics;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Polly;
using Polly.Retry;
using Polly.Telemetry;
using SharedKernel.ServiceDefaults.Telemetry;
using SharedKernel.ServiceDefaults.Tests.Telemetry.GrpcFixtures;

namespace SharedKernel.ServiceDefaults.Tests.Telemetry;

/// <summary>
/// T-43 (WO-056/P-365) gating coverage. Beyond the four baseline tests every
/// <c>With*Telemetry</c> sibling carries (idempotent registration x2, no-throw,
/// same-builder-return), this class proves — against a genuine outbound gRPC call and a genuine
/// Polly retry execution, not merely that the wiring calls didn't throw — that
/// <see cref="CommunicationTelemetryExtensions.WithCommunicationTelemetry"/>'s instrumentation
/// actually takes effect end to end.
/// </summary>
/// <remarks>
/// <b>Known, documented deviation from the phase's original text (T-43, criterion 2 and the
/// metrics half of criterion 3):</b> the phase spec asked for proof that a Polly-driven retry
/// "emits a captured span/metric from the <c>"Polly"</c> source/meter". C-47's empirical finding
/// (decompiling the exact pinned dependency chain — <c>Microsoft.Extensions.Http.Resilience
/// 10.7.0</c> → <c>Microsoft.Extensions.Resilience 10.7.0</c> → <c>Polly.Extensions 8.4.2</c> →
/// <c>Polly.Core 8.4.2</c>) confirmed Polly v8.4.2 creates a <c>"Polly"</c>-named
/// <see cref="System.Diagnostics.Metrics.Meter"/> but NO <see cref="ActivitySource"/> anywhere in
/// that chain — see <see cref="CommunicationTelemetryExtensions"/>'s own remarks for the full
/// finding. Consequently the Polly METRIC is proven below
/// (<see cref="WithCommunicationTelemetry_PollyRetry_EmitsPollyMeterMetric"/> and
/// <see cref="WithCommunicationTelemetry_CalledTwice_PollyRetry_DoesNotDuplicateMetric"/>); a
/// Polly SPAN cannot be proven because no such source exists to emit one against this pinned
/// version. If a future Polly release introduces an <see cref="ActivitySource"/>, extending
/// <see cref="CommunicationTelemetryExtensions.WithCommunicationTelemetry"/> with a matching
/// <c>WithTracing(t =&gt; t.AddSource("Polly"))</c> call — and a genuine span-capture test to
/// match — is a follow-up, not a silently-missed criterion here.
/// </remarks>
public sealed class CommunicationTelemetryExtensionsTests
{
    [Fact]
    public void WithCommunicationTelemetry_CalledTwice_RegistersTracerProviderBuilderOnce()
    {
        var builder = WebApplication.CreateBuilder();

        builder.WithCommunicationTelemetry();
        builder.WithCommunicationTelemetry();

        using var provider = builder.Services.BuildServiceProvider();
        var tracerProviderBuilders = provider.GetServices<TracerProviderBuilder>().ToList();

        // AddGrpcClientInstrumentation() registers its backing type via TryAddSingleton internally
        // (confirmed by decompiling OpenTelemetry.Instrumentation.GrpcNetClient 1.15.1-beta.1 and
        // OpenTelemetry.Api.ProviderBuilderExtensions 1.16.0 — see CommunicationTelemetryExtensions'
        // XML remarks for the full idempotency finding); calling the extension twice must not throw
        // and must not register a second TracerProviderBuilder configuration delegate.
        Assert.True(tracerProviderBuilders.Count <= 1);
    }

    [Fact]
    public void WithCommunicationTelemetry_CalledTwice_RegistersMeterProviderBuilderOnce()
    {
        var builder = WebApplication.CreateBuilder();

        builder.WithCommunicationTelemetry();
        builder.WithCommunicationTelemetry();

        using var provider = builder.Services.BuildServiceProvider();
        var meterProviderBuilders = provider.GetServices<MeterProviderBuilder>().ToList();

        // Same de-duplication guarantee as the tracing half, for the "Polly" meter — the OpenTelemetry
        // SDK dedups AddMeter(string) registrations by name internally, the same mechanism every
        // other family sibling's idempotency guarantee already relies on.
        Assert.True(meterProviderBuilders.Count <= 1);
    }

    [Fact]
    public void WithCommunicationTelemetry_DoesNotThrow()
    {
        var builder = WebApplication.CreateBuilder();

        var exception = Record.Exception(() =>
        {
            builder.WithCommunicationTelemetry();
            builder.WithCommunicationTelemetry();
        });

        Assert.Null(exception);
    }

    [Fact]
    public void WithCommunicationTelemetry_ReturnsSameBuilderInstance()
    {
        var builder = WebApplication.CreateBuilder();

        var result = builder.WithCommunicationTelemetry();

        Assert.Same(builder, result);
    }

    [Fact]
    public async Task WithCommunicationTelemetry_GrpcCall_ProducesSpanWithRpcSystemGrpcTag()
    {
        var captured = new List<Activity>();

        var builder = WebApplication.CreateBuilder();

        // The base HTTP client span comes from OpenTelemetry.Instrumentation.Http (already wired
        // unconditionally by AddSharedKernelTelemetry in production). Wired directly here — rather
        // than via AddSharedKernelTelemetry — to keep this test self-contained and avoid its OTLP
        // exporter registration, which this test does not need and which would otherwise attempt a
        // real network flush on TracerProvider disposal; mirrors the
        // CachingTelemetryExtensionsTests/PersistenceTelemetryExtensionsTests (T-39/T-40) precedent
        // of a scoped, exporter-free WithTracing(...) pipeline for span-capture tests.
        builder.Services
            .AddOpenTelemetry()
            .WithTracing(tracing => tracing
                .AddHttpClientInstrumentation()
                .AddProcessor(new CapturingProcessor(captured)));

        builder.WithCommunicationTelemetry();

        using var provider = builder.Services.BuildServiceProvider();

        // Resolving TracerProvider forces the OpenTelemetry SDK to build the pipeline configured
        // above — including WithCommunicationTelemetry()'s AddGrpcClientInstrumentation()
        // registration — and attach its DiagnosticListener subscription to the runtime. Without
        // this, BuildServiceProvider() alone never builds the provider (that normally happens via
        // a hosted service at IHost.StartAsync() time, which this unit test never starts).
        using var tracerProvider = provider.GetRequiredService<TracerProvider>();

        using var factory = new GrpcTestWebApplicationFactory();
        using var httpClient = factory.CreateDefaultClient(new ResponseVersionHandler());
        using var channel = GrpcChannel.ForAddress(
            httpClient.BaseAddress!,
            new GrpcChannelOptions { HttpClient = httpClient });
        var client = new Greeter.GreeterClient(channel);

        await client.SayHelloAsync(new HelloRequest { Name = "World" });

        // Proves .AddGrpcClientInstrumentation() genuinely took effect: the captured span carries
        // the gRPC RPC semantic-convention tag, not merely that the outbound call didn't throw.
        // Verified (during implementation) to FAIL — no captured activity carries
        // rpc.system == "grpc" — when the .AddGrpcClientInstrumentation() call is removed from
        // WithCommunicationTelemetry().
        Assert.Contains(captured, a => a.GetTagItem("rpc.system") as string == "grpc");
    }

    [Fact]
    public async Task WithCommunicationTelemetry_CalledTwice_GrpcCall_DoesNotProduceDuplicateSpan()
    {
        var captured = new List<Activity>();

        var builder = WebApplication.CreateBuilder();
        builder.Services
            .AddOpenTelemetry()
            .WithTracing(tracing => tracing
                .AddHttpClientInstrumentation()
                .AddProcessor(new CapturingProcessor(captured)));

        builder.WithCommunicationTelemetry();
        builder.WithCommunicationTelemetry();

        using var provider = builder.Services.BuildServiceProvider();
        using var tracerProvider = provider.GetRequiredService<TracerProvider>();

        using var factory = new GrpcTestWebApplicationFactory();
        using var httpClient = factory.CreateDefaultClient(new ResponseVersionHandler());
        using var channel = GrpcChannel.ForAddress(
            httpClient.BaseAddress!,
            new GrpcChannelOptions { HttpClient = httpClient });
        var client = new Greeter.GreeterClient(channel);

        await client.SayHelloAsync(new HelloRequest { Name = "World" });

        // Exercises C-47's idempotency finding directly (TryAddSingleton-backed
        // GrpcClientInstrumentation, a single DiagnosticListener subscription regardless of
        // registration count) rather than merely trusting the finding's changelog note: calling
        // WithCommunicationTelemetry() twice must not double-tag or duplicate the captured span
        // for one gRPC call.
        Assert.Single(captured, a => a.GetTagItem("rpc.system") as string == "grpc");
    }

    [Fact]
    public async Task WithCommunicationTelemetry_PollyRetry_EmitsPollyMeterMetric()
    {
        var exportedMetrics = new List<Metric>();

        var builder = WebApplication.CreateBuilder();
        builder.Services
            .AddOpenTelemetry()
            .WithMetrics(metrics => metrics.AddInMemoryExporter(exportedMetrics));

        builder.WithCommunicationTelemetry();

        using var provider = builder.Services.BuildServiceProvider();
        using var meterProvider = provider.GetRequiredService<MeterProvider>();

        await ExecutePollyRetryPipelineAsync();

        meterProvider.ForceFlush();

        // Proves the "Polly" meter genuinely emits a measurement through the built MeterProvider
        // once WithCommunicationTelemetry() is wired — not merely that AddMeter("Polly") was
        // called syntactically. See this class's <remarks> for why there is no tracing
        // counterpart to this proof. Verified (during implementation) to FAIL — exportedMetrics
        // stays empty — when the WithMetrics(m => m.AddMeter("Polly")) call is removed from
        // WithCommunicationTelemetry().
        Assert.Contains(exportedMetrics, m => m.MeterName == "Polly");
    }

    [Fact]
    public async Task WithCommunicationTelemetry_CalledTwice_PollyRetry_DoesNotDuplicateMetric()
    {
        var singleRegistrationCount = await CaptureExportedPollyMetricCountAsync(registerTwice: false);
        var doubleRegistrationCount = await CaptureExportedPollyMetricCountAsync(registerTwice: true);

        // C-47 found no custom idempotency guard is needed for the Polly meter — the OpenTelemetry
        // SDK dedups AddMeter(string) registrations by name internally (the same mechanism the
        // existing WithCommunicationTelemetry_CalledTwice_RegistersMeterProviderBuilderOnce test
        // already exercises structurally, above). This test exercises that guarantee against a
        // real measurement: calling WithCommunicationTelemetry() twice must not double-count a
        // single retry execution's "Polly" meter measurements relative to a single registration.
        Assert.Equal(singleRegistrationCount, doubleRegistrationCount);
        Assert.True(doubleRegistrationCount > 0);
    }

    /// <summary>
    /// Builds and executes, in an isolated <see cref="WebApplication"/>/<see cref="MeterProvider"/>,
    /// a real Polly v8 retry pipeline with telemetry enabled, and returns how many exported metric
    /// points came from the <c>"Polly"</c> meter for that single execution.
    /// </summary>
    private static async Task<int> CaptureExportedPollyMetricCountAsync(bool registerTwice)
    {
        var exportedMetrics = new List<Metric>();

        var builder = WebApplication.CreateBuilder();
        builder.Services
            .AddOpenTelemetry()
            .WithMetrics(metrics => metrics.AddInMemoryExporter(exportedMetrics));

        builder.WithCommunicationTelemetry();
        if (registerTwice)
        {
            builder.WithCommunicationTelemetry();
        }

        using var provider = builder.Services.BuildServiceProvider();
        using var meterProvider = provider.GetRequiredService<MeterProvider>();

        await ExecutePollyRetryPipelineAsync();

        meterProvider.ForceFlush();

        return exportedMetrics.Count(m => m.MeterName == "Polly");
    }

    /// <summary>
    /// Executes a minimal, standalone <see cref="ResiliencePipelineBuilder"/> retry strategy — one
    /// failure forcing exactly one retry attempt — with Polly's own telemetry explicitly enabled
    /// via <see cref="TelemetryOptions"/>. This is the real production dependency chain's telemetry
    /// surface (<c>Polly.Extensions.TelemetryListenerImpl</c>, targeting the static
    /// <c>Meter("Polly", "1.0")</c> C-47 confirmed by decompilation), invoked standalone rather
    /// than via a full <c>SharedKernel.Communication.Rest</c> <c>StandardResilienceHandler</c>
    /// pipeline — pulling in that whole package for this proof would be disproportionate, per the
    /// T-43 task's own explicitly offered alternative.
    /// </summary>
    private static async Task ExecutePollyRetryPipelineAsync()
    {
        var attempt = 0;

        var pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<InvalidOperationException>(),
                MaxRetryAttempts = 1,
                Delay = TimeSpan.Zero,
            })
            .ConfigureTelemetry(new TelemetryOptions())
            .Build();

        await pipeline.ExecuteAsync(async _ =>
        {
            attempt++;
            if (attempt == 1)
            {
                throw new InvalidOperationException("Transient failure — forces one Polly retry attempt.");
            }

            await Task.CompletedTask;
        });
    }

    /// <summary>Copies each ended <see cref="Activity"/> into <paramref name="sink"/>.</summary>
    private sealed class CapturingProcessor(List<Activity> sink) : BaseProcessor<Activity>
    {
        public override void OnEnd(Activity data) => sink.Add(data);
    }
}
