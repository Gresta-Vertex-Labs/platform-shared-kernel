using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenFeature;
using OpenFeature.Constant;
using Xunit;

namespace SharedKernel.FeatureManagement.Tests;

/// <summary>A flag keeps one value for the whole scope, even when configuration reloads in between.</summary>
public sealed class ScopeConsistencyTests
{
    private static readonly FeatureFlag<bool> Kill = FeatureFlag.Boolean("KillSwitch");

    private static async Task SwitchOffAsync(IConfigurationRoot configuration)
    {
        configuration["FeatureManagement:KillSwitch"] = "false";
        configuration.Reload();
        await Task.Yield();
    }

    [Fact]
    public async Task WithinOneScope_AFlagDoesNotChange_WhenConfigurationReloads()
    {
        IConfigurationRoot configuration = FeatureTestHost.InMemory(("FeatureManagement:KillSwitch", "true"));
        await using var provider = await FeatureTestHost.StartAsync(configuration);
        IFeatureClient request = provider.NewScopeClient();

        Assert.True(await request.IsEnabledAsync(Kill));
        await SwitchOffAsync(configuration);

        Assert.True(await request.IsEnabledAsync(Kill));
        Assert.False(await provider.NewScopeClient().IsEnabledAsync(Kill));
    }

    [Fact]
    public async Task AReusedResult_ExpiresAfterScopeResultLifetime()
    {
        var clock = new ManualTimeProvider();
        IConfigurationRoot configuration = FeatureTestHost.InMemory(("FeatureManagement:KillSwitch", "true"));
        await using var provider = await FeatureTestHost.StartAsync(
            configuration,
            o => o.ScopeResultLifetime = TimeSpan.FromSeconds(30),
            before: s => s.AddSingleton<TimeProvider>(clock));
        IFeatureClient longRunning = provider.NewScopeClient();

        Assert.True(await longRunning.IsEnabledAsync(Kill));
        await SwitchOffAsync(configuration);
        clock.Advance(TimeSpan.FromSeconds(31));

        Assert.False(await longRunning.IsEnabledAsync(Kill));
    }

    [Fact]
    public async Task WhenTurnedOff_EveryEvaluationSeesTheCurrentValue()
    {
        IConfigurationRoot configuration = FeatureTestHost.InMemory(("FeatureManagement:KillSwitch", "true"));
        await using var provider = await FeatureTestHost.StartAsync(configuration, o => o.EvaluateOncePerScope = false);
        IFeatureClient request = provider.NewScopeClient();

        Assert.True(await request.IsEnabledAsync(Kill));
        await SwitchOffAsync(configuration);

        Assert.False(await request.IsEnabledAsync(Kill));
    }

    [Fact]
    public async Task AFailedEvaluation_IsNotReused()
    {
        IConfigurationRoot configuration = FeatureTestHost.InMemory(("FeatureManagement:Other", "true"));
        await using var provider = await FeatureTestHost.StartAsync(configuration);
        IFeatureClient request = provider.NewScopeClient();

        Assert.Equal(ErrorType.FlagNotFound, (await request.GetDetailsAsync(Kill)).ErrorType);
        configuration["FeatureManagement:KillSwitch"] = "true";
        configuration.Reload();

        Assert.True(await request.IsEnabledAsync(Kill));
    }

    [Fact]
    public async Task DifferentExplicitTargets_InOneScope_AreEvaluatedSeparately()
    {
        await using var provider = await FeatureTestHost.StartAsync(FeatureTestHost.Json(BooleanFlagTests.Configuration));
        IFeatureClient request = provider.NewScopeClient();
        FeatureFlag<bool> beta = FeatureFlag.Boolean("Beta");

        Assert.True(await request.IsEnabledAsync(beta, new FeatureTargetingContext("alice").ToEvaluationContext()));
        Assert.False(await request.IsEnabledAsync(beta, new FeatureTargetingContext("bob").ToEvaluationContext()));
        Assert.True(await request.IsEnabledAsync(beta, new FeatureTargetingContext("alice").ToEvaluationContext()));
    }

    [Fact]
    public async Task ConcurrentEvaluations_InOneScope_AllAgree()
    {
        IConfigurationRoot configuration = FeatureTestHost.InMemory(("FeatureManagement:KillSwitch", "true"));
        await using var provider = await FeatureTestHost.StartAsync(configuration);
        IFeatureClient request = provider.NewScopeClient();

        Task<bool>[] evaluations = Enumerable.Range(0, 50).Select(_ => request.IsEnabledAsync(Kill)).ToArray();
        await SwitchOffAsync(configuration);
        bool[] results = await Task.WhenAll(evaluations);

        Assert.Single(results.Distinct());
        Assert.Equal(results[0], await request.IsEnabledAsync(Kill));
    }
}
