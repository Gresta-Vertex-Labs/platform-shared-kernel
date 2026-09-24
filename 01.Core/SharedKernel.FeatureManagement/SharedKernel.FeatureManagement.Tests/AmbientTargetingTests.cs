using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
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
    public async Task WithoutAnAccessor_ActivityBaggage_IsNotTheCaller()
    {
        // P-562 X2: a caller sets baggage itself (the W3C baggage header), so a tenant or user taken from it would let
        // an anonymous caller pick another tenant's flags. Beta targets the tenant-acme group and the user alice.
        await using var provider = await FeatureTestHost.StartAsync(FeatureTestHost.Json(BooleanFlagTests.Configuration));

        using var activity = new Activity("request").Start();
        activity.SetBaggage(WellKnownBaggageKeys.TenantId, "tenant-acme");
        activity.SetBaggage("SubjectId", "alice");
        activity.SetBaggage("UserId", "alice");

        Assert.False(await provider.NewScopeClient().IsEnabledAsync(Beta));
    }

    [Fact]
    public async Task WithoutAnAccessor_ActivityBaggage_AllocatesNoVariant()
    {
        // Theme allocates Dark to the tenant-acme group; the default for everyone else is Classic.
        await using var provider = await FeatureTestHost.StartAsync(FeatureTestHost.Json(VariantFlagTests.Configuration));

        using var activity = new Activity("request").Start();
        activity.SetBaggage(WellKnownBaggageKeys.TenantId, "tenant-acme");

        Assert.Equal("classic", await provider.NewScopeClient().GetValueAsync(Theme));
    }

    [Fact]
    public async Task WithoutAnAccessor_NoneIsRegistered()
    {
        await using var provider = await FeatureTestHost.StartAsync(FeatureTestHost.Json(BooleanFlagTests.Configuration));

        using var scope = provider.CreateScope();

        Assert.Null(scope.ServiceProvider.GetService<IFeatureTargetingContextAccessor>());
    }

    [Fact]
    public async Task RegisteredAccessor_IsTheCaller_WhateverTheBaggageSays()
    {
        await using var provider = await FeatureTestHost.StartAsync(
            FeatureTestHost.Json(BooleanFlagTests.Configuration),
            after: s => s.AddScoped<IFeatureTargetingContextAccessor>(_ => new StaticTargetingAccessor(new FeatureTargetingContext("bob", "tenant-other"))));

        using var activity = new Activity("request").Start();
        activity.SetBaggage(WellKnownBaggageKeys.TenantId, "tenant-acme");

        Assert.False(await provider.NewScopeClient().IsEnabledAsync(Beta));
    }

    [Fact]
    public async Task WithoutAnAccessorOrBaggage_TheCallerIsAnonymous()
    {
        await using var provider = await FeatureTestHost.StartAsync(FeatureTestHost.Json(BooleanFlagTests.Configuration));

        Assert.False(await provider.NewScopeClient().IsEnabledAsync(Beta));
    }
}
