using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace SharedKernel.ServiceDefaults.Telemetry;

/// <summary>
/// Activates two third-party OpenTelemetry integrations that are already latent inside
/// <c>11.Communication</c>'s dependency graph but never invoked by anything: gRPC client
/// instrumentation (<c>OpenTelemetry.Instrumentation.GrpcNetClient</c>) and Polly v8's own
/// resilience telemetry.
/// </summary>
/// <remarks>
/// <para>
/// This is the eighth sibling in the <c>With*Telemetry</c> family (alongside
/// <see cref="ApplicationTelemetryExtensions.WithApplicationTelemetry"/>,
/// <see cref="CachingTelemetryExtensions.WithCachingTelemetry"/>,
/// <see cref="IntelligenceTelemetryExtensions.WithIntelligenceTelemetry"/>,
/// <see cref="MessagingTelemetryExtensions.WithMessagingTelemetry"/>,
/// <see cref="PersistenceTelemetryExtensions.WithPersistenceTelemetry"/>,
/// <see cref="SearchTelemetryExtensions.WithSearchTelemetry"/>, and
/// <see cref="WorkflowTelemetryExtensions.WithWorkflowTelemetry"/>), but architecturally distinct
/// from all seven: <c>11.Communication</c> owns no <c>"SharedKernel.Communication"</c>
/// <see cref="System.Diagnostics.ActivitySource"/>/<see cref="System.Diagnostics.Metrics.Meter"/>
/// of its own to wire by bare string name. Instead this method activates two independent
/// third-party diagnostics sources:
/// </para>
/// <para>
/// <b>(1) gRPC — tracing only.</b> <c>WithTracing(t =&gt; t.AddGrpcClientInstrumentation())</c>
/// activates <c>OpenTelemetry.Instrumentation.GrpcNetClient</c>, enriching outbound gRPC spans
/// with RPC semantic-convention tags (<c>rpc.system</c>, <c>rpc.service</c>, <c>rpc.method</c>,
/// <c>rpc.grpc.status_code</c>) in place of the under-specified generic-HTTP span that
/// <c>OpenTelemetry.Instrumentation.Http</c> alone produces today (already wired unconditionally
/// inside <see cref="TelemetryExtensions.AddSharedKernelTelemetry"/> — this method is strictly
/// additive to that, never a replacement). This is the family's first member requiring a genuine
/// instrumentation-extension-method call rather than a bare <c>AddSource(string)</c>/
/// <c>AddMeter(string)</c> call, and consequently the first requiring its own new
/// <c>PackageReference</c> to <c>OpenTelemetry.Instrumentation.GrpcNetClient</c> on
/// <c>SharedKernel.ServiceDefaults.csproj</c> itself (version-aligned with
/// <c>11.Communication.Grpc/SharedKernel.Communication.Grpc.csproj</c>'s existing
/// <c>1.15.1-beta.1</c> pin). Deliberately zero <c>ProjectReference</c> to
/// <c>11.Communication.Grpc</c> — the instrumentation package hooks the gRPC client pipeline via
/// <c>DiagnosticSource</c> at runtime, with no compile-time coupling to <c>Grpc.Net.Client</c>
/// types.
/// </para>
/// <para>
/// <b>(2) Polly — metrics only, NOT tracing (a correction to the phase's original design note,
/// made after empirically decompiling the exact pinned dependency chain).</b> The phase design
/// (D-18) assumed Polly v8 emits both a <c>"Polly"</c>-named <see cref="System.Diagnostics.ActivitySource"/>
/// and a <c>"Polly"</c>-named <see cref="System.Diagnostics.Metrics.Meter"/>. Direct decompilation
/// of the exact assemblies transitively pulled in by <c>Microsoft.Extensions.Http.Resilience</c>
/// <c>10.7.0</c> (via <c>Microsoft.Extensions.Resilience 10.7.0</c> → <c>Polly.Extensions
/// 8.4.2</c> → <c>Polly.Core 8.4.2</c>) confirms only half of that assumption:
/// <c>Polly.Extensions.dll</c>'s internal <c>Polly.Telemetry.TelemetryListenerImpl</c> creates
/// exactly one instrument, <c>internal static readonly Meter Meter = new Meter("Polly", "1.0")</c>
/// — the <see cref="System.Diagnostics.Metrics.Meter"/> name/version this method wires is
/// byte-identical to that. A repo-wide grep for the string <c>"ActivitySource"</c> (and, more
/// broadly, <c>"Activity"</c>) across <c>Polly.Core.dll</c>, <c>Polly.Extensions.dll</c>,
/// <c>Polly.RateLimiting.dll</c>, <c>Microsoft.Extensions.Resilience.dll</c>,
/// <c>Microsoft.Extensions.Http.Resilience.dll</c>, and
/// <c>Microsoft.Extensions.Http.Diagnostics.dll</c> — the complete pinned dependency chain —
/// returns zero matches: Polly v8.4.2's own resilience telemetry (retry attempts, circuit-breaker
/// state transitions, timeout events) is reported exclusively through this <see cref="System.Diagnostics.Metrics.Meter"/>
/// (as counters/histograms) and through an <c>ILogger</c> category also named <c>"Polly"</c> — it
/// creates no <see cref="System.Diagnostics.ActivitySource"/> and starts no
/// <see cref="System.Diagnostics.Activity"/> of its own. Retry/circuit-breaker events therefore
/// enrich the pipeline via metrics only; the outbound HTTP/gRPC request span itself continues to
/// come from <c>OpenTelemetry.Instrumentation.Http</c>/<c>.GrpcNetClient</c>. Consequently this
/// method wires only <c>WithMetrics(m =&gt; m.AddMeter(PollyInstrumentationName))</c> for Polly —
/// there is no corresponding <c>WithTracing(t =&gt; t.AddSource(PollyInstrumentationName))</c>
/// call, because no such source is ever emitted by this exact, pinned dependency chain. Pure
/// string-name wiring, zero new package or project reference — mirrors
/// <see cref="MessagingTelemetryExtensions.WithMessagingTelemetry"/>'s pre-existing third-party
/// <c>"MassTransit"</c>-meter-name wiring precedent.
/// </para>
/// <para>
/// <b>Idempotency finding for <c>.AddGrpcClientInstrumentation()</c> — confirmed already
/// dedup-safe, no guard needed.</b> Unlike a bare <c>AddSource(string)</c>/<c>AddMeter(string)</c>
/// call, <c>.AddGrpcClientInstrumentation()</c> is an instrumentation-factory registration, which
/// does not automatically inherit the OpenTelemetry SDK's by-name deduplication. Direct
/// decompilation of the installed <c>OpenTelemetry.Instrumentation.GrpcNetClient 1.15.1-beta.1</c>
/// and <c>OpenTelemetry.Api.ProviderBuilderExtensions 1.16.0</c> assemblies shows repeated calls
/// are nonetheless safe, for three independent, defense-in-depth reasons: (1) the extension method
/// registers its backing type via <c>services.TryAddSingleton&lt;GrpcClientInstrumentation&gt;()</c>
/// — the DI container dedupes to exactly one instance regardless of how many times
/// <c>.AddGrpcClientInstrumentation()</c> is called; (2) the one side-effecting action —
/// subscribing to the <c>"Grpc.Net.Client"</c> <c>DiagnosticListener</c> via
/// <c>DiagnosticSourceSubscriber.Subscribe()</c> — happens exactly once, inside that singleton's
/// constructor, and is itself internally guarded (<c>if (allSourcesSubscription == null)</c>); the
/// per-call span enrichment logic in <c>GrpcClientDiagnosticListener</c> does not start its own
/// <see cref="System.Diagnostics.Activity"/> — it only tags <c>Activity.Current</c>, which
/// <c>Grpc.Net.Client</c>'s own diagnostics start exactly once per call — so with only one
/// subscriber ever attached, no duplicate/double-tagged span can occur regardless of how many
/// times this method is called; (3) even the residual case of the same singleton instance being
/// registered more than once in the <c>TracerProviderBuilder</c>'s internal instrumentation
/// bookkeeping list only affects <c>Dispose()</c> at provider shutdown, and
/// <c>DiagnosticSourceSubscriber.Dispose()</c> is itself idempotent
/// (<c>Interlocked.CompareExchange</c>-guarded). No custom idempotency guard (e.g. a static
/// already-added flag) is required — this method relies entirely on the third-party package's own
/// correctly-idempotent design. Full runtime proof against a live gRPC call (not just this
/// decompiled-source analysis) is the explicit acceptance criterion of the dedicated
/// <c>SK.13.Tests</c> follow-up task, which also proves the same for the Polly meter via a real
/// retry pipeline.
/// </para>
/// <para>
/// REST's baseline HTTP spans remain covered by the pre-existing generic
/// <c>OpenTelemetry.Instrumentation.Http</c> wiring inside
/// <see cref="TelemetryExtensions.AddSharedKernelTelemetry"/> — this method is additive to that,
/// never a replacement.
/// </para>
/// <para>
/// Idempotent: calling this method more than once on the same <c>IHostApplicationBuilder</c>
/// registers no duplicate instrument or duplicate gRPC span enrichment — see the idempotency
/// finding above.
/// </para>
/// </remarks>
public static class CommunicationTelemetryExtensions
{
    /// <summary>
    /// The <see cref="System.Diagnostics.Metrics.Meter"/> name Polly v8's own resilience telemetry
    /// uses (<c>new Meter("Polly", "1.0")</c> in <c>Polly.Telemetry.TelemetryListenerImpl</c>,
    /// confirmed by decompiling the installed <c>Polly.Extensions 8.4.2</c> assembly — the exact
    /// version transitively pinned by <c>Microsoft.Extensions.Http.Resilience 10.7.0</c>). Polly
    /// emits no corresponding <see cref="System.Diagnostics.ActivitySource"/> in this dependency
    /// chain — see this class's <see cref="CommunicationTelemetryExtensions"/> remarks for the full
    /// finding.
    /// </summary>
    private const string PollyInstrumentationName = "Polly";

    /// <summary>
    /// Adds gRPC client OpenTelemetry instrumentation to the host's <c>TracerProvider</c>, and the
    /// <c>"Polly"</c> meter (Polly v8's own resilience telemetry — retry attempts, circuit-breaker
    /// state transitions, timeout events) to the host's <c>MeterProvider</c>.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <returns>The same <paramref name="builder"/> instance, for fluent chaining.</returns>
    /// <remarks>See the type-level remarks for the full design rationale and verification findings.</remarks>
    public static IHostApplicationBuilder WithCommunicationTelemetry(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services
            .AddOpenTelemetry()
            .WithTracing(tracing => tracing.AddGrpcClientInstrumentation())
            .WithMetrics(metrics => metrics.AddMeter(PollyInstrumentationName));

        return builder;
    }
}
