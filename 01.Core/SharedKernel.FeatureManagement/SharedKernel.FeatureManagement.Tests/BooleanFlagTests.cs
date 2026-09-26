using Microsoft.Extensions.DependencyInjection;
using OpenFeature;
using OpenFeature.Constant;
using OpenFeature.Model;
using Xunit;

namespace SharedKernel.FeatureManagement.Tests;

/// <summary>On/off flags through the real Microsoft.FeatureManagement pipeline, both configuration schemas.</summary>
public sealed class BooleanFlagTests
{
    internal const string Configuration = """
        {
          "feature_management": {
            "feature_flags": [
              { "id": "AlwaysOn", "enabled": true },
              { "id": "Off", "enabled": false },
              {
                "id": "Beta",
                "enabled": true,
                "conditions": {
                  "client_filters": [
                    {
                      "name": "Microsoft.Targeting",
                      "parameters": {
                        "Audience": {
                          "Users": [ "alice" ],
                          "Groups": [
                            { "Name": "beta-testers", "RolloutPercentage": 100 },
                            { "Name": "0f8fad5b-d9cb-469f-a165-70867728950e", "RolloutPercentage": 100 }
                          ],
                          "DefaultRolloutPercentage": 0
                        }
                      }
                    }
                  ]
                }
              },
              {
                "id": "HalfRollout",
                "enabled": true,
                "conditions": {
                  "client_filters": [
                    { "name": "Microsoft.Targeting", "parameters": { "Audience": { "DefaultRolloutPercentage": 50 } } }
                  ]
                }
              }
            ]
          },
          "FeatureManagement": { "LegacyOn": true, "LegacyOff": false }
        }
        """;

    private static readonly FeatureFlag<bool> AlwaysOn = FeatureFlag.Boolean("AlwaysOn");
    private static readonly FeatureFlag<bool> Off = FeatureFlag.Boolean("Off");
    private static readonly FeatureFlag<bool> Beta = FeatureFlag.Boolean("Beta");
    private static readonly FeatureFlag<bool> HalfRollout = FeatureFlag.Boolean("HalfRollout");

    [Fact]
    public async Task EnabledFlag_IsOn_WithStaticReason()
    {
        await using var provider = await FeatureTestHost.StartAsync(FeatureTestHost.Json(Configuration));

        FlagEvaluationDetails<bool> details = await provider.NewScopeClient().GetDetailsAsync(AlwaysOn);

        Assert.True(details.Value);
        Assert.Equal(ErrorType.None, details.ErrorType);
        Assert.Equal(Reason.Static, details.Reason);
    }

    [Fact]
    public async Task DisabledFlag_IsOff_WithDisabledReason()
    {
        await using var provider = await FeatureTestHost.StartAsync(FeatureTestHost.Json(Configuration));

        FlagEvaluationDetails<bool> details = await provider.NewScopeClient().GetDetailsAsync(Off);

        Assert.False(details.Value);
        Assert.Equal(Reason.Disabled, details.Reason);
    }

    [Theory]
    [InlineData("LegacyOn", true)]
    [InlineData("LegacyOff", false)]
    public async Task LegacyFeatureManagementSection_StillWorks(string key, bool expected)
    {
        await using var provider = await FeatureTestHost.StartAsync(FeatureTestHost.Json(Configuration));

        Assert.Equal(expected, await provider.NewScopeClient().IsEnabledAsync(FeatureFlag.Boolean(key)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingFlag_ReturnsTheDeclaredDefault_WithFlagNotFound(bool defaultValue)
    {
        await using var provider = await FeatureTestHost.StartAsync(FeatureTestHost.Json(Configuration));

        FlagEvaluationDetails<bool> details = await provider.NewScopeClient()
            .GetDetailsAsync(FeatureFlag.Boolean("NoSuchFlag", defaultValue));

        Assert.Equal(defaultValue, details.Value);
        Assert.Equal(ErrorType.FlagNotFound, details.ErrorType);
        Assert.Equal(Reason.Error, details.Reason);
    }

    [Fact]
    public async Task TargetedFlag_WithNoCaller_IsOff_WithTargetingMatchReason()
    {
        await using var provider = await FeatureTestHost.StartAsync(FeatureTestHost.Json(Configuration));

        FlagEvaluationDetails<bool> details = await provider.NewScopeClient().GetDetailsAsync(Beta);

        Assert.False(details.Value);
        Assert.Equal(Reason.TargetingMatch, details.Reason);
    }

    // The defect this pass fixed: a context passed to the old IsEnabledAsync<TContext> never reached the
    // Microsoft.Targeting filter, so user, group and percentage targeting were always off.
    [Fact]
    public async Task TargetedFlag_IsOn_ForAListedUser()
    {
        await using var provider = await FeatureTestHost.StartAsync(FeatureTestHost.Json(Configuration));
        IFeatureClient client = provider.NewScopeClient();

        Assert.True(await client.IsEnabledAsync(Beta, new FeatureTargetingContext("alice").ToEvaluationContext()));
        Assert.False(await client.IsEnabledAsync(Beta, new FeatureTargetingContext("bob").ToEvaluationContext()));
    }

    [Fact]
    public async Task TargetedFlag_IsOn_ForAListedGroup()
    {
        await using var provider = await FeatureTestHost.StartAsync(FeatureTestHost.Json(Configuration));

        var betaTester = new FeatureTargetingContext("carol", groups: ["beta-testers"]);

        Assert.True(await provider.NewScopeClient().IsEnabledAsync(Beta, betaTester.ToEvaluationContext()));
    }

    [Fact]
    public async Task TargetedFlag_IsOn_ForAListedTenant()
    {
        await using var provider = await FeatureTestHost.StartAsync(FeatureTestHost.Json(Configuration));
        IFeatureClient client = provider.NewScopeClient();

        Assert.True(await client.IsEnabledAsync(Beta, FeatureTargetingContext.ForTenant(TestTenants.Acme).ToEvaluationContext()));
        Assert.True(await client.IsEnabledAsync(Beta, new FeatureTargetingContext("dave", TestTenants.Acme).ToEvaluationContext()));
        Assert.False(await client.IsEnabledAsync(Beta, FeatureTargetingContext.ForTenant(TestTenants.Other).ToEvaluationContext()));
    }

    [Fact]
    public async Task PercentageRollout_SplitsUsers_AndEachUserAlwaysGetsTheSameAnswer()
    {
        await using var provider = await FeatureTestHost.StartAsync(FeatureTestHost.Json(Configuration));

        int on = 0;
        for (int i = 0; i < 400; i++)
        {
            EvaluationContext user = new FeatureTargetingContext($"user-{i}").ToEvaluationContext();
            bool first = await provider.NewScopeClient().IsEnabledAsync(HalfRollout, user);
            bool second = await provider.NewScopeClient().IsEnabledAsync(HalfRollout, user);

            Assert.Equal(first, second);
            on += first ? 1 : 0;
        }

        Assert.InRange(on, 140, 260);
    }

    [Fact]
    public async Task CancellationToken_ReachesTheEvaluation()
    {
        await using var provider = await FeatureTestHost.StartAsync(FeatureTestHost.Json(Configuration));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        FlagEvaluationDetails<bool> details = await provider.NewScopeClient().GetDetailsAsync(AlwaysOn, cancelled.Token);

        // OpenFeature turns the cancellation into the caller's default instead of throwing.
        Assert.False(details.Value);
        Assert.NotEqual(ErrorType.None, details.ErrorType);
    }

    [Fact]
    public async Task IFeatureClient_IsScoped_AndCannotBeResolvedFromTheRoot()
    {
        await using var provider = await FeatureTestHost.StartAsync(FeatureTestHost.Json(Configuration));

        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IFeatureClient>());
    }
}
