using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using SharedKernel.Primitives.Propagation;
using SharedKernel.ServiceDefaults.Telemetry;

namespace SharedKernel.ServiceDefaults.Tests.Telemetry;

public sealed class BaggageLogRecordProcessorTests
{
    [Fact]
    public void OnEnd_ActivityCurrentHasBaggage_CopiesEntriesToAttributes()
    {
        using var activity = new Activity("test-activity").Start();
        activity.SetBaggage("TenantId", "11111111-1111-1111-1111-111111111111");
        activity.SetBaggage(WellKnownBaggageKeys.CorrelationId, "corr-abc");

        var captured = EmitAndCapture(logger => logger.LogInformation("hello"));

        Assert.Single(captured);
        var attributes = captured[0];
        Assert.NotNull(attributes);
        Assert.Contains(attributes!, kv => kv.Key == "TenantId" && Equals(kv.Value, "11111111-1111-1111-1111-111111111111"));
        Assert.Contains(attributes!, kv => kv.Key == WellKnownBaggageKeys.CorrelationId && Equals(kv.Value, "corr-abc"));
    }

    [Fact]
    public void OnEnd_ActivityCurrentIsNull_IsNoOpAndDoesNotThrow()
    {
        Assert.Null(Activity.Current);

        var exception = Record.Exception(() =>
            EmitAndCapture(logger => logger.LogInformation("no ambient activity")));

        Assert.Null(exception);
    }

    [Fact]
    public void OnEnd_ExplicitAttributeAtSameKeyAsBaggage_IsNeverOverwritten()
    {
        using var activity = new Activity("test-activity").Start();
        activity.SetBaggage("TenantId", "from-baggage");

        var captured = EmitAndCapture(logger => logger.LogInformation("value {TenantId}", "explicit-value"));

        var attributes = captured[0];
        Assert.NotNull(attributes);
        var tenantIdAttributes = attributes!.Where(kv => kv.Key == "TenantId").ToList();

        // The explicit message-template placeholder value must win — the ambient baggage value
        // for the same key must never be appended as a second/overwriting entry.
        Assert.Single(tenantIdAttributes);
        Assert.Equal("explicit-value", tenantIdAttributes[0].Value);
    }

    private static List<IReadOnlyList<KeyValuePair<string, object?>>?> EmitAndCapture(
        Action<ILogger> emit)
    {
        var captured = new List<IReadOnlyList<KeyValuePair<string, object?>>?>();

        using var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddOpenTelemetry(options =>
            {
                options.AddProcessor(new BaggageLogRecordProcessor());
                options.AddProcessor(new CapturingProcessor(captured));
            }));

        var logger = loggerFactory.CreateLogger("BaggageLogRecordProcessorTests");
        emit(logger);

        return captured;
    }

    /// <summary>Copies a <see cref="LogRecord"/>'s attributes at <see cref="OnEnd"/> time, since the
    /// SDK may pool/reset the <see cref="LogRecord"/> instance after the pipeline completes.</summary>
    private sealed class CapturingProcessor(List<IReadOnlyList<KeyValuePair<string, object?>>?> sink)
        : BaseProcessor<LogRecord>
    {
        public override void OnEnd(LogRecord data) =>
            sink.Add(data.Attributes is null ? null : [.. data.Attributes]);
    }
}
