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
    public async Task WithoutAnAccessor_TheUserComesFromTheOpenRequestContextScope()
    {
        await using var provider = await FeatureTestHost.StartAsync(FeatureTestHost.Json(BooleanFlagTests.Configuration));

        using var caller = RequestContextScope.Begin(new SystemRequestContext([], "alice"));

        Assert.True(await provider.NewScopeClient().IsEnabledAsync(Beta));
    }

    [Fact]
    public async Task WithoutAnAccessor_AnOpenScopeWithoutATenant_IsAnonymous()
    {
        await using var provider = await FeatureTestHost.StartAsync(FeatureTestHost.Json(BooleanFlagTests.Configuration));

        using var activity = new Activity("request").Start();
        activity.SetBaggage(WellKnownBaggageKeys.TenantId, TestTenants.AcmeText);
        using var caller = RequestContextScope.Begin(AnonymousRequestContext.Instance);

        Assert.False(await provider.NewScopeClient().IsEnabledAsync(Beta));
    }

    [Fact]
    public async Task WithoutAnAccessor_ActivityBaggage_IsNotTheCaller()
    {
        // P-562 X2: a caller sets baggage itself (the W3C baggage header), so a tenant or user taken from it would let
        // an anonymous caller pick another tenant's flags. Beta targets the Acme tenant's group and the user alice.
        await using var provider = await FeatureTestHost.StartAsync(FeatureTestHost.Json(BooleanFlagTests.Configuration));

        using var activity = new Activity("request").Start();
        activity.SetBaggage(WellKnownBaggageKeys.TenantId, TestTenants.AcmeText);
        activity.SetBaggage("SubjectId", "alice");
        activity.SetBaggage("UserId", "alice");

        Assert.False(await provider.NewScopeClient().IsEnabledAsync(Beta));
    }

    [Fact]
    public async Task WithoutAnAccessor_ActivityBaggage_AllocatesNoVariant()
    {
        // Theme allocates Dark to the Acme tenant's group; the default for everyone else is Classic.
        await using var provider = await FeatureTestHost.StartAsync(FeatureTestHost.Json(VariantFlagTests.Configuration));

        using var activity = new Activity("request").Start();
        activity.SetBaggage(WellKnownBaggageKeys.TenantId, TestTenants.AcmeText);

        Assert.Equal("classic", await provider.NewScopeClient().GetValueAsync(Theme));
    }

    [Fact]
    public async Task RegisteredAccessor_IsTheCaller_WhateverTheBaggageSays()
    {
        await using var provider = await FeatureTestHost.StartAsync(
            FeatureTestHost.Json(BooleanFlagTests.Configuration),
            after: s => s.AddScoped<IFeatureTargetingContextAccessor>(_ => new StaticTargetingAccessor(new FeatureTargetingContext("bob", TestTenants.Other))));

        using var activity = new Activity("request").Start();
        activity.SetBaggage(WellKnownBaggageKeys.TenantId, TestTenants.AcmeText);

        Assert.False(await provider.NewScopeClient().IsEnabledAsync(Beta));
    }

    [Fact]
    public async Task WithoutAnAccessorOrScope_TheCallerIsAnonymous()
    {
        await using var provider = await FeatureTestHost.StartAsync(FeatureTestHost.Json(BooleanFlagTests.Configuration));

        Assert.False(await provider.NewScopeClient().IsEnabledAsync(Beta));
    }
}
