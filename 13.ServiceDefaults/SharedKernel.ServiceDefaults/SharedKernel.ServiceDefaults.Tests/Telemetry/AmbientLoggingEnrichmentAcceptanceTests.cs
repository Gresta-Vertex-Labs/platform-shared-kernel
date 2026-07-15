using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using SharedKernel.MultiTenancy.Middleware;
using SharedKernel.MultiTenancy.Resolution;
using SharedKernel.Primitives.Propagation;
using SharedKernel.ServiceDefaults.Telemetry;

namespace SharedKernel.ServiceDefaults.Tests.Telemetry;

/// <summary>
/// End-to-end acceptance tests proving <see cref="BaggageLogRecordProcessor"/> correctly surfaces
/// ambient enrichment from other domains' <see cref="Activity"/> baggage — with zero
/// <c>ProjectReference</c> from <c>SharedKernel.ServiceDefaults</c> to <c>14.Presentation</c> (whose
/// correlation-id middleware is simulated here via the same raw BCL
/// <see cref="Activity.SetBaggage"/> call it uses in production, per WO-031, against the same
/// <see cref="WellKnownBaggageKeys.CorrelationId"/> shared constant it consumes, per WO-042/P-261) —
/// and that it composes without collision with <c>SharedKernel.MultiTenancy</c>'s real
/// <see cref="TenantResolutionMiddleware"/> baggage enrichment.
/// </summary>
public sealed class AmbientLoggingEnrichmentAcceptanceTests
{
    [Fact]
    public async Task CorrelationIdBaggage_SetDirectlyViaBcl_SurfacesOnLogRecordAttributes()
    {
        using var activity = new Activity("http-request").Start();

        // Simulates 14.Presentation's correlation-id middleware, which owns its own Activity
        // baggage key directly against the BCL (WO-031) — no reference to that package needed.
        activity.SetBaggage(WellKnownBaggageKeys.CorrelationId, "corr-e2e-001");

        var captured = await EmitAndCaptureAsync(logger => logger.LogInformation("request handled"));

        Assert.Contains(captured, kv => kv.Key == WellKnownBaggageKeys.CorrelationId && Equals(kv.Value, "corr-e2e-001"));
    }

    [Fact]
    public async Task TenantIdAndCorrelationIdBaggage_ComposeOnSameLogRecord_WithoutCollision()
    {
        using var activity = new Activity("http-request").Start();

        var expectedTenantId = Guid.NewGuid();
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Tenant-Id"] = expectedTenantId.ToString();

        var options = Microsoft.Extensions.Options.Options.Create(
            new TenantResolutionOptions { StrategyOrder = [TenantResolutionStrategyNames.Header] });

        var middleware = new TenantResolutionMiddleware(
            _ => Task.CompletedTask,
            [new HeaderTenantResolutionStrategy()],
            options);
        var tenantProvider = new AmbientTenantProvider();

        // Runs the real TenantResolutionMiddleware, which sets TenantBaggageKeys.TenantId on
        // Activity.Current as a side effect of resolving the tenant (WO-041/P-251, C-33).
        await middleware.InvokeAsync(context, tenantProvider);

        // Simulates 14.Presentation's independent correlation-id enrichment on the same Activity.
        activity.SetBaggage(WellKnownBaggageKeys.CorrelationId, "corr-e2e-002");

        var captured = await EmitAndCaptureAsync(logger => logger.LogInformation("request handled"));

        Assert.Contains(
            captured,
            kv => kv.Key == TenantBaggageKeys.TenantId && Equals(kv.Value, expectedTenantId.ToString()));
        Assert.Contains(captured, kv => kv.Key == WellKnownBaggageKeys.CorrelationId && Equals(kv.Value, "corr-e2e-002"));
    }

    private static async Task<IReadOnlyList<KeyValuePair<string, object?>>> EmitAndCaptureAsync(
        Action<ILogger> emit)
    {
        var captured = new List<IReadOnlyList<KeyValuePair<string, object?>>?>();

        using var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddOpenTelemetry(otelOptions =>
            {
                otelOptions.AddProcessor(new BaggageLogRecordProcessor());
                otelOptions.AddProcessor(new CapturingProcessor(captured));
            }));

        var logger = loggerFactory.CreateLogger("AmbientLoggingEnrichmentAcceptanceTests");
        emit(logger);
        await Task.Yield();

        return captured[0] ?? [];
    }

    private sealed class CapturingProcessor(List<IReadOnlyList<KeyValuePair<string, object?>>?> sink)
        : BaseProcessor<LogRecord>
    {
        public override void OnEnd(LogRecord data) =>
            sink.Add(data.Attributes is null ? null : [.. data.Attributes]);
    }
}
