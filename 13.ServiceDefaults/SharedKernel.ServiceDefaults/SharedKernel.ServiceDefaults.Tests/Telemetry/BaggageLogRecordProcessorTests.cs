using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;
using SharedKernel.MultiTenancy.Resolution;
using SharedKernel.Primitives.Propagation;
using SharedKernel.ServiceDefaults.Telemetry;

namespace SharedKernel.ServiceDefaults.Tests.Telemetry;

public sealed class BaggageLogRecordProcessorTests
{
    private const string TenantValue = "11111111-1111-1111-1111-111111111111";

    // The message template, which Microsoft.Extensions.Logging adds to every structured log state.
    private const string OriginalFormat = "{OriginalFormat}";

    [Fact]
    public void OnEnd_PlatformBaggage_IsCopiedUnderItsOwnKey()
    {
        using var activity = new Activity("test-activity").Start();
        activity.SetBaggage(WellKnownBaggageKeys.TenantId, TenantValue);
        activity.SetBaggage(WellKnownBaggageKeys.CorrelationId, "corr-abc");

        var captured = EmitAndCapture(logger => logger.LogInformation("hello"));

        Assert.Single(captured);
        var attributes = captured[0];
        Assert.NotNull(attributes);
        Assert.Contains(attributes!, kv => kv.Key == WellKnownBaggageKeys.TenantId && Equals(kv.Value, TenantValue));
        Assert.Contains(attributes!, kv => kv.Key == WellKnownBaggageKeys.CorrelationId && Equals(kv.Value, "corr-abc"));
    }

    [Fact]
    public void OnEnd_BaggageOutsideThePlatformKeys_IsNeverCopied()
    {
        // P-562 X2: what a caller's baggage header or a message header can carry. Only the two platform keys are
        // copied; near-misses in spelling and case are other keys.
        string[] callerKeys =
        [
            "SubjectId",
            "user.id",
            "tenant.id",
            "tenantid",
            "CorrelationId",
            "messaging.masstransit.correlation_id",
            "messaging.message.conversation_id",
        ];

        using var activity = new Activity("test-activity").Start();
        foreach (var key in callerKeys)
        {
            activity.AddBaggage(key, "forged");
        }

        activity.SetBaggage(WellKnownBaggageKeys.CorrelationId, "corr-abc");

        var attributes = EmitAndCapture(logger => logger.LogInformation("hello")).Single();

        Assert.NotNull(attributes);
        Assert.DoesNotContain(attributes!, kv => callerKeys.Contains(kv.Key));
        Assert.Contains(attributes!, kv => kv.Key == WellKnownBaggageKeys.CorrelationId);
    }

    [Fact]
    public void OnEnd_CopiesExactlyTheWellKnownBaggageKeys()
    {
        // Pins the processor's list to 01.Core's registry, which it cannot reference (WO-084): a key renamed or added
        // there without the same change here fails this test instead of silently leaving log records.
        using var activity = new Activity("test-activity").Start();
        activity.SetBaggage(WellKnownBaggageKeys.CorrelationId, "corr-abc");
        activity.SetBaggage(WellKnownBaggageKeys.TenantId, TenantValue);
        activity.SetBaggage("order.id", "o-1");

        var attributes = EmitAndCapture(logger => logger.LogInformation("hello")).Single();

        Assert.NotNull(attributes);
        Assert.Equal(
            new[] { WellKnownBaggageKeys.CorrelationId, WellKnownBaggageKeys.TenantId },
            attributes!.Select(kv => kv.Key).Where(key => key != OriginalFormat).ToArray());
        Assert.Equal([WellKnownBaggageKeys.CorrelationId, WellKnownBaggageKeys.TenantId], PlatformBaggageKeys.All);
        Assert.Equal(WellKnownBaggageKeys.CorrelationId, PlatformBaggageKeys.CorrelationId);
        Assert.Equal(WellKnownBaggageKeys.TenantId, PlatformBaggageKeys.TenantId);
    }

    [Fact]
    public void PlatformTenantKey_IsTheOneTenantResolutionWrites()
    {
        // Writer and reader live in packages that do not reference each other.
        Assert.Equal(TenantBaggageKeys.TenantId, PlatformBaggageKeys.TenantId);
    }

    [Theory]
    [InlineData("line\r\nforged: entry")]
    [InlineData("line\nforged")]
    [InlineData("carriage\rreturn")]
    [InlineData("nul\0byte")]
    [InlineData("tab\tseparated")]
    [InlineData("delete\u007f")]
    [InlineData("next-line\u0085")]
    public void OnEnd_ValueWithAControlCharacter_IsNotCopied(string forged)
    {
        using var activity = new Activity("test-activity").Start();
        activity.SetBaggage(WellKnownBaggageKeys.TenantId, forged);
        activity.SetBaggage(WellKnownBaggageKeys.CorrelationId, "corr-abc");

        var attributes = EmitAndCapture(logger => logger.LogInformation("hello")).Single();

        Assert.NotNull(attributes);
        Assert.DoesNotContain(attributes!, kv => kv.Key == WellKnownBaggageKeys.TenantId);
        Assert.Contains(attributes!, kv => kv.Key == WellKnownBaggageKeys.CorrelationId && Equals(kv.Value, "corr-abc"));
    }

    [Theory]
    [InlineData(0x2028)]
    [InlineData(0x2029)]
    public void OnEnd_ValueWithAUnicodeLineOrParagraphSeparator_IsNotCopied(int separator)
    {
        using var activity = new Activity("test-activity").Start();
        activity.SetBaggage(WellKnownBaggageKeys.CorrelationId, "corr" + (char)separator + "forged");

        var captured = EmitAndCapture(logger => logger.LogInformation("hello")).Single();

        Assert.True(captured is null || captured.All(kv => kv.Key != WellKnownBaggageKeys.CorrelationId));
    }

    [Theory]
    [InlineData(TenantValue)]
    [InlineData("4bf92f3577b34da6a3ce929d0e0e4736")]
    [InlineData("flow_42-A.b:c")]
    [InlineData("")]
    public void IsLoggable_PlatformShapedValues_AreAccepted(string value)
    {
        Assert.True(BaggageLogRecordProcessor.IsLoggable(value));
    }

    [Fact]
    public void OnEnd_BaggageOnAParentActivity_IsCopied()
    {
        // The request activity carries the baggage; log statements usually run under a child span.
        using var request = new Activity("request").Start();
        request.SetBaggage(WellKnownBaggageKeys.TenantId, TenantValue);
        using var child = new Activity("child").Start();

        var attributes = EmitAndCapture(logger => logger.LogInformation("hello")).Single();

        Assert.NotNull(attributes);
        Assert.Contains(attributes!, kv => kv.Key == WellKnownBaggageKeys.TenantId && Equals(kv.Value, TenantValue));
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
    public void OnEnd_ActivityWithoutPlatformBaggage_LeavesTheAttributesAsTheyWere()
    {
        using var activity = new Activity("test-activity").Start();
        activity.SetBaggage("SubjectId", "forged");

        var attributes = EmitAndCapture(logger => logger.LogInformation("value {Amount}", 42)).Single();

        Assert.NotNull(attributes);
        Assert.Equal(["Amount", OriginalFormat], attributes!.Select(kv => kv.Key).ToArray());
    }

    [Fact]
    public void OnEnd_ExplicitAttributeAtSameKeyAsBaggage_IsNeverOverwritten()
    {
        using var activity = new Activity("test-activity").Start();
        activity.SetBaggage(WellKnownBaggageKeys.TenantId, "from-baggage");

        var captured = EmitAndCapture(logger => logger.LogInformation("value {TenantId}", "explicit-value"));

        var attributes = captured[0];
        Assert.NotNull(attributes);
        var tenantIdAttributes = attributes!.Where(kv => kv.Key == WellKnownBaggageKeys.TenantId).ToList();

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
