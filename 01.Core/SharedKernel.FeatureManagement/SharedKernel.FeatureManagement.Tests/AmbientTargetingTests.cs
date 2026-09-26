using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Execution.Context;
using SharedKernel.Primitives.Propagation;
using Xunit;

namespace SharedKernel.FeatureManagement.Tests;

/// <summary>The caller's user, tenant and groups reach every evaluation without being passed.</summary>
public sealed class AmbientTargetingTests
{
    private static readonly FeatureFlag<bool> Beta = FeatureFlag.Boolean("Beta");
    private static readonly FeatureFlag<string> Theme = FeatureFlag.String("Theme", "fallback");

    [Fact]
    public async Task RegisteredAccessor_TargetsTheCurrentCaller_ForFlagsAndVariants()
    {
        await using var provider = await FeatureTestHost.StartAsync(
            FeatureTestHost.Json(BooleanFlagTests.Configuration),
            after: s => s.AddScoped<IFeatureTargetingContextAccessor>(_ => new StaticTargetingAccessor(new FeatureTargetingContext("alice"))));

        Assert.True(await provider.NewScopeClient().IsEnabledAsync(Beta));
    }

    [Fact]
    public async Task AccessorRegisteredBeforeTheCall_IsKept()
    {
        await using var provider = await FeatureTestHost.StartAsync(
            FeatureTestHost.Json(VariantFlagTests.Configuration),
            before: s => s.AddSingleton<IFeatureTargetingContextAccessor>(new StaticTargetingAccessor(new FeatureTargetingContext("alice"))));

        Assert.Equal("dark", await provider.NewScopeClient().GetValueAsync(Theme));
    }

    [Fact]
    public async Task ExplicitContext_ReplacesTheAmbientCaller()
    {
        await using var provider = await FeatureTestHost.StartAsync(
            FeatureTestHost.Json(BooleanFlagTests.Configuration),
            after: s => s.AddScoped<IFeatureTargetingContextAccessor>(_ => new StaticTargetingAccessor(new FeatureTargetingContext("alice"))));

        Assert.False(await provider.NewScopeClient().IsEnabledAsync(Beta, new FeatureTargetingContext("bob").ToEvaluationContext()));
    }

    [Fact]
    public async Task EachScope_ReadsItsOwnCaller()
    {
        var current = new AsyncLocal<string?>();
        await using var provider = await FeatureTestHost.StartAsync(
            FeatureTestHost.Json(BooleanFlagTests.Configuration),
            after: s => s.AddScoped<IFeatureTargetingContextAccessor>(_ => new StaticTargetingAccessor(new FeatureTargetingContext(current.Value))));

        current.Value = "alice";
        bool forAlice = await provider.NewScopeClient().IsEnabledAsync(Beta);
        current.Value = "bob";
        bool forBob = await provider.NewScopeClient().IsEnabledAsync(Beta);

        Assert.True(forAlice);
        Assert.False(forBob);
    }

    [Fact]
    public async Task WithoutAnAccessor_TheTenantComesFromTheOpenRequestContextScope()
    {
        await using var provider = await FeatureTestHost.StartAsync(FeatureTestHost.Json(BooleanFlagTests.Configuration));

        using var caller = RequestContextScope.Begin(new SystemRequestContext([], "nightly-job", TestTenants.Acme));

        Assert.True(await provider.NewScopeClient().IsEnabledAsync(Beta));
    }

    [Fact]
    public async Task WithoutAnAccessor_AnOpenScopeWithoutATenant_WinsOverBaggage()
    {
        await using var provider = await FeatureTestHost.StartAsync(FeatureTestHost.Json(BooleanFlagTests.Configuration));

        using var activity = new Activity("request").Start();
        activity.SetBaggage(WellKnownBaggageKeys.TenantId, TestTenants.AcmeText);
        using var caller = RequestContextScope.Begin(AnonymousRequestContext.Instance);

        Assert.False(await provider.NewScopeClient().IsEnabledAsync(Beta));
    }

    [Fact]
    public async Task WithoutAnAccessorOrScope_TheTenantComesFromActivityBaggage()
    {
        await using var provider = await FeatureTestHost.StartAsync(FeatureTestHost.Json(BooleanFlagTests.Configuration));

        using var activity = new Activity("request").Start();
        activity.SetBaggage(WellKnownBaggageKeys.TenantId, TestTenants.AcmeText);

        Assert.True(await provider.NewScopeClient().IsEnabledAsync(Beta));
    }

    [Fact]
    public async Task WithoutAnAccessorOrBaggage_TheCallerIsAnonymous()
    {
        await using var provider = await FeatureTestHost.StartAsync(FeatureTestHost.Json(BooleanFlagTests.Configuration));

        Assert.False(await provider.NewScopeClient().IsEnabledAsync(Beta));
    }
}
