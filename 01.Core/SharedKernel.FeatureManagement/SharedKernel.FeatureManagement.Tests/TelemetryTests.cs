using System.Diagnostics;
using Xunit;

namespace SharedKernel.FeatureManagement.Tests;

/// <summary>The OpenTelemetry feature_flag.evaluation event on the current activity.</summary>
public sealed class TelemetryTests : IDisposable
{
    private const string Configuration = """
        {
          "feature_management": {
            "feature_flags": [
              {
                "id": "Measured",
                "enabled": true,
                "variants": [ { "name": "Big", "configuration_value": "big" } ],
                "allocation": { "default_when_enabled": "Big" },
                "telemetry": { "enabled": true, "metadata": { "version": "7" } }
              },
              { "id": "Quiet", "enabled": true }
            ]
          }
        }
        """;

    private static readonly FeatureFlag<string> Measured = FeatureFlag.String("Measured", "small");
    private static readonly FeatureFlag<bool> Quiet = FeatureFlag.Boolean("Quiet");

    private readonly ActivitySource _source = new("TelemetryTests");
    private readonly ActivityListener _listener = new()
    {
        ShouldListenTo = static s => s.Name == "TelemetryTests",
        Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    };

    public TelemetryTests() => ActivitySource.AddActivityListener(_listener);

    public void Dispose()
    {
        _listener.Dispose();
        _source.Dispose();
    }

    [Fact]
    public async Task ByDefault_OnlyFlagsWithTelemetryEnabled_EmitTheEvent()
    {
        await using var provider = await FeatureTestHost.StartAsync(FeatureTestHost.Json(Configuration));

        using Activity request = _source.StartActivity("request")!;
        await provider.NewScopeClient().GetValueAsync(Measured);
        await provider.NewScopeClient().IsEnabledAsync(Quiet);

        ActivityEvent evaluation = Assert.Single(request.Events);
        var tags = evaluation.Tags.ToDictionary(t => t.Key, t => t.Value);
        Assert.Equal("feature_flag.evaluation", evaluation.Name);
        Assert.Equal("Measured", tags["feature_flag.key"]);
        Assert.Equal("Big", tags["feature_flag.result.variant"]);
        Assert.Equal("big", tags["feature_flag.result.value"]);
        Assert.Equal("static", tags["feature_flag.result.reason"]);
        Assert.Equal("Microsoft.FeatureManagement", tags["feature_flag.provider.name"]);
        Assert.Equal("7", tags["feature_flag.version"]);
    }

    [Fact]
    public async Task AllFlags_EmitsForEveryEvaluation()
    {
        await using var provider = await FeatureTestHost.StartAsync(
            FeatureTestHost.Json(Configuration), o => o.Telemetry = FeatureTelemetryMode.AllFlags);

        using Activity request = _source.StartActivity("request")!;
        await provider.NewScopeClient().GetValueAsync(Measured);
        await provider.NewScopeClient().IsEnabledAsync(Quiet);

        Assert.Equal(2, request.Events.Count());
    }

    [Fact]
    public async Task Off_EmitsNothing()
    {
        await using var provider = await FeatureTestHost.StartAsync(
            FeatureTestHost.Json(Configuration), o => o.Telemetry = FeatureTelemetryMode.Off);

        using Activity request = _source.StartActivity("request")!;
        await provider.NewScopeClient().GetValueAsync(Measured);

        Assert.Empty(request.Events);
    }

    // Microsoft.FeatureManagement's own "FeatureFlag" event records TargetingId (the user id) for flags with
    // telemetry enabled; the package hides telemetry from its evaluator so only the OpenTelemetry event remains.
    [Theory]
    [InlineData(FeatureTelemetryMode.ConfiguredFlags)]
    [InlineData(FeatureTelemetryMode.Off)]
    public async Task MicrosoftFeatureManagementsOwnEvent_IsNeverEmitted(FeatureTelemetryMode mode)
    {
        await using var provider = await FeatureTestHost.StartAsync(FeatureTestHost.Json(Configuration), o => o.Telemetry = mode);

        using Activity request = _source.StartActivity("request")!;
        await provider.NewScopeClient().GetValueAsync(Measured, new FeatureTargetingContext("user-4711").ToEvaluationContext());

        Assert.DoesNotContain(request.Events, e => e.Name == "FeatureFlag");
        Assert.DoesNotContain(request.Events, e => e.Tags.Any(t => t.Key == "TargetingId"));
    }

    // The evaluator gets a copy of each definition without telemetry. A property added by a future
    // Microsoft.FeatureManagement release would be dropped from that copy, so this fails on upgrade.
    [Fact]
    public void FeatureDefinitionCopy_CoversEveryProperty()
    {
        string[] properties = typeof(Microsoft.FeatureManagement.FeatureDefinition).GetProperties().Select(p => p.Name).Order().ToArray();

        Assert.Equal(["Allocation", "EnabledFor", "Name", "RequirementType", "Status", "Telemetry", "Variants"], properties);
    }

    [Fact]
    public async Task TheEvent_NeverCarriesTheTargetingKey()
    {
        await using var provider = await FeatureTestHost.StartAsync(FeatureTestHost.Json(Configuration));

        using Activity request = _source.StartActivity("request")!;
        await provider.NewScopeClient().GetValueAsync(Measured, new FeatureTargetingContext("user-4711", TestTenants.Other).ToEvaluationContext());

        ActivityEvent evaluation = Assert.Single(request.Events);
        Assert.DoesNotContain(evaluation.Tags, t => t.Value is string s && (s.Contains("user-4711", StringComparison.Ordinal) || s.Contains(TestTenants.OtherText, StringComparison.Ordinal)));
    }
}
