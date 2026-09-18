using System.Diagnostics.Metrics;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.FeatureManagement;
using OpenFeature;
using OpenFeature.Constant;
using OpenFeature.Hooks;
using OpenFeature.Model;
using Xunit;

namespace SharedKernel.FeatureManagement.Tests;

/// <summary>Every code sample and output in README.md, run as written.</summary>
public sealed partial class ReadmeSampleTests
{
    // ---- Quick start, as written in the README ----

    private const string QuickStartConfiguration = """
        {
          "feature_management": {
            "feature_flags": [
              {
                "id": "NewCheckout",
                "enabled": true,
                "conditions": {
                  "client_filters": [
                    {
                      "name": "Microsoft.Targeting",
                      "parameters": {
                        "Audience": {
                          "Users": [ "alice" ],
                          "Groups": [ { "Name": "acme", "RolloutPercentage": 100 } ],
                          "DefaultRolloutPercentage": 0
                        }
                      }
                    }
                  ]
                }
              },
              {
                "id": "CheckoutTheme",
                "enabled": true,
                "variants": [
                  { "name": "Classic", "configuration_value": "classic" },
                  { "name": "Dark", "configuration_value": "dark" }
                ],
                "allocation": {
                  "default_when_enabled": "Classic",
                  "user": [ { "variant": "Dark", "users": [ "alice" ] } ]
                }
              }
            ]
          }
        }
        """;

    public static class Flags
    {
        public static readonly FeatureFlag<bool> NewCheckout =
            FeatureFlag.Boolean("NewCheckout", description: "The redesigned checkout flow.");

        public static readonly FeatureFlag<string> CheckoutTheme =
            FeatureFlag.String("CheckoutTheme", defaultValue: "classic");
    }

    // Stand-in for SharedKernel.Security.Abstractions.IUserContext (01.Core cannot reference 12.Security);
    // the members the README uses have the same names.
    public interface IUserContext
    {
        bool IsAuthenticated { get; }

        string? SubjectId { get; }

        string? TenantId { get; }

        IReadOnlyCollection<string> Roles { get; }
    }

    private sealed record TestUser(bool IsAuthenticated, string? SubjectId, string? TenantId, IReadOnlyCollection<string> Roles) : IUserContext;

    public sealed class UserFeatureTargeting(IUserContext user) : IFeatureTargetingContextAccessor
    {
        public FeatureTargetingContext? GetTargetingContext() =>
            user.IsAuthenticated
                ? new FeatureTargetingContext(user.SubjectId, user.TenantId?.ToString(), user.Roles)
                : null;
    }

    public sealed class CheckoutEndpoint(IFeatureClient flags)
    {
        public async Task<string> GetLayoutAsync(CancellationToken ct) =>
            await flags.IsEnabledAsync(Flags.NewCheckout, ct)
                ? $"new-checkout/{await flags.GetValueAsync(Flags.CheckoutTheme, ct)}"
                : "legacy-checkout";
    }

    private static readonly AsyncLocal<IUserContext?> CurrentUser = new();

    private static async Task<IHost> StartQuickStartHostAsync()
    {
        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddConfiguration(FeatureTestHost.Json(QuickStartConfiguration));
        builder.Services.AddScoped(_ => CurrentUser.Value ?? new TestUser(false, null, null, []));

        builder.Services.AddScoped<IFeatureTargetingContextAccessor, UserFeatureTargeting>();
        builder.Services.AddSharedKernelFeatureManagement(builder.Configuration, flags => flags
            .ValidateOnStart(Flags.NewCheckout, Flags.CheckoutTheme));

        IHost host = builder.Build();
        await host.StartAsync();
        return host;
    }

    private static async Task<T> AsAsync<T>(IHost host, IUserContext user, Func<IServiceProvider, Task<T>> action)
    {
        CurrentUser.Value = user;
        using IServiceScope scope = host.Services.CreateScope();
        return await action(scope.ServiceProvider);
    }

    [Theory]
    [InlineData("alice", null, "new-checkout/dark")]
    [InlineData("bob", "acme", "new-checkout/classic")]
    [InlineData("bob", null, "legacy-checkout")]
    public async Task QuickStart_Output(string user, string? tenant, string expected)
    {
        using IHost host = await StartQuickStartHostAsync();

        string layout = await AsAsync(host, new TestUser(true, user, tenant, []), sp =>
            new CheckoutEndpoint(sp.GetRequiredService<IFeatureClient>()).GetLayoutAsync(CancellationToken.None));

        Assert.Equal(expected, layout);
    }

    [Fact]
    public async Task IntroTable_Output()
    {
        using IHost host = await StartQuickStartHostAsync();

        Task<FlagEvaluationDetails<T>> Evaluate<T>(string user, string? tenant, FeatureFlag<T> flag) =>
            AsAsync(host, new TestUser(true, user, tenant, []), sp => sp.GetRequiredService<IFeatureClient>().GetDetailsAsync(flag));

        Assert.True((await Evaluate("alice", null, Flags.NewCheckout)).Value);
        Assert.True((await Evaluate("bob", "acme", Flags.NewCheckout)).Value);
        FlagEvaluationDetails<bool> other = await Evaluate("bob", "other", Flags.NewCheckout);
        Assert.False(other.Value);
        Assert.Equal(Reason.TargetingMatch, other.Reason);

        FlagEvaluationDetails<string> alice = await Evaluate("alice", null, Flags.CheckoutTheme);
        Assert.Equal(("dark", "Dark", Reason.TargetingMatch), (alice.Value, alice.Variant, alice.Reason));
        FlagEvaluationDetails<string> bob = await Evaluate("bob", null, Flags.CheckoutTheme);
        Assert.Equal(("classic", "Classic"), (bob.Value, bob.Variant));

        FlagEvaluationDetails<bool> typo = await Evaluate("alice", null, FeatureFlag.Boolean("NewChekout"));
        Assert.False(typo.Value);
        Assert.Equal(ErrorType.FlagNotFound, typo.ErrorType);
    }

    [Fact]
    public async Task Targeting_EvaluatingForSomeoneElse()
    {
        using IHost host = await StartQuickStartHostAsync();

        bool on = await AsAsync(host, new TestUser(true, "alice", null, []), sp =>
            sp.GetRequiredService<IFeatureClient>().IsEnabledAsync(
                Flags.NewCheckout, new FeatureTargetingContext("bob", "acme").ToEvaluationContext(), CancellationToken.None));

        Assert.True(on);
    }

    // ---- Variants and typed values ----

    public sealed record CheckoutSettings(int Steps, bool ExpressPay, IReadOnlyList<string> Providers);

    [JsonSerializable(typeof(CheckoutSettings))]
    internal sealed partial class AppJsonContext : JsonSerializerContext;

    public static readonly FeatureFlag<CheckoutSettings?> Checkout =
        FeatureFlag.Object<CheckoutSettings?>("Checkout", null, AppJsonContext.Default.CheckoutSettings);

    [Fact]
    public async Task Variants_ObjectSample()
    {
        const string configuration = """
            { "feature_management": { "feature_flags": [
              {
                "id": "Checkout",
                "enabled": true,
                "variants": [
                  { "name": "OneStep", "configuration_value": { "Steps": 1, "ExpressPay": true, "Providers": [ "card", "iban" ] } }
                ],
                "allocation": { "default_when_enabled": "OneStep" }
              }
            ] } }
            """;
        await using var provider = await FeatureTestHost.StartAsync(FeatureTestHost.Json(configuration), o => o.ValidateOnStart(Checkout));

        CheckoutSettings? settings = await provider.NewScopeClient().GetValueAsync(Checkout);

        Assert.NotNull(settings);
        Assert.Equal((1, true), (settings.Steps, settings.ExpressPay));
        Assert.Equal(["card", "iban"], settings.Providers);
    }

    [Fact]
    public async Task Configuration_LegacySchema()
    {
        await using var provider = await FeatureTestHost.StartAsync(FeatureTestHost.Json("""{ "FeatureManagement": { "NewCheckout": true } }"""));

        Assert.True(await provider.NewScopeClient().IsEnabledAsync(Flags.NewCheckout));
    }

    // ---- Recipes ----

    [Fact]
    public async Task Recipe1_KillSwitch()
    {
        FeatureFlag<bool> payments = FeatureFlag.Boolean("Payments", defaultValue: true);

        await using var configured = await FeatureTestHost.StartAsync(FeatureTestHost.Json(
            """{ "feature_management": { "feature_flags": [ { "id": "Payments", "enabled": true } ] } }"""));
        await using var stopped = await FeatureTestHost.StartAsync(FeatureTestHost.Json(
            """{ "feature_management": { "feature_flags": [ { "id": "Payments", "enabled": false } ] } }"""));
        await using var missing = await FeatureTestHost.StartAsync(FeatureTestHost.Json("{}"));

        Assert.True(await configured.NewScopeClient().IsEnabledAsync(payments));
        Assert.False(await stopped.NewScopeClient().IsEnabledAsync(payments));
        Assert.True(await missing.NewScopeClient().IsEnabledAsync(payments));
    }

    private static readonly FeatureFlag<bool> NewSearch = FeatureFlag.Boolean("NewSearch");

    private static string RolloutConfiguration(int percentage) => $$"""
        {
          "feature_management": { "feature_flags": [
            {
              "id": "NewSearch",
              "enabled": true,
              "conditions": {
                "client_filters": [ { "name": "Microsoft.Targeting", "parameters": { "Audience": { "DefaultRolloutPercentage": {{percentage}} } } } ]
              }
            }
          ] }
        }
        """;

    [Fact]
    public async Task Recipe2_RolloutTo10PercentOfUsers_IsStickyAndOnlyGrows()
    {
        await using var at10 = await FeatureTestHost.StartAsync(FeatureTestHost.Json(RolloutConfiguration(10)));
        await using var at30 = await FeatureTestHost.StartAsync(FeatureTestHost.Json(RolloutConfiguration(30)));

        int on = 0;
        for (int i = 0; i < 1000; i++)
        {
            EvaluationContext user = new FeatureTargetingContext($"user-{i}").ToEvaluationContext();
            bool in10 = await at10.NewScopeClient().IsEnabledAsync(NewSearch, user);
            Assert.Equal(in10, await at10.NewScopeClient().IsEnabledAsync(NewSearch, user));
            if (in10)
            {
                on++;
                Assert.True(await at30.NewScopeClient().IsEnabledAsync(NewSearch, user));
            }
        }

        Assert.InRange(on, 60, 140);
    }

    [Fact]
    public async Task Recipe3_RolloutToAPercentageOfTenants()
    {
        await using var provider = await FeatureTestHost.StartAsync(FeatureTestHost.Json(RolloutConfiguration(10)));

        int on = 0;
        for (int i = 0; i < 1000; i++)
        {
            string tenantId = $"tenant-{i}";
            bool first = await provider.NewScopeClient().IsEnabledAsync(NewSearch, FeatureTargetingContext.ForTenant(tenantId).ToEvaluationContext(), CancellationToken.None);
            Assert.Equal(first, await provider.NewScopeClient().IsEnabledAsync(NewSearch, FeatureTargetingContext.ForTenant(tenantId).ToEvaluationContext(), CancellationToken.None));
            on += first ? 1 : 0;
        }

        Assert.InRange(on, 60, 140);
    }

    [Fact]
    public async Task Recipe3_NamedTenantsAsGroups()
    {
        const string configuration = """
            { "feature_management": { "feature_flags": [
              { "id": "NewSearch", "enabled": true, "conditions": { "client_filters": [
                { "name": "Microsoft.Targeting", "parameters": { "Audience": { "Groups": [ { "Name": "acme", "RolloutPercentage": 100 } ] } } } ] } }
            ] } }
            """;
        await using var provider = await FeatureTestHost.StartAsync(FeatureTestHost.Json(configuration));

        Assert.True(await provider.NewScopeClient().IsEnabledAsync(NewSearch, new FeatureTargetingContext("anyone", "acme").ToEvaluationContext()));
        Assert.False(await provider.NewScopeClient().IsEnabledAsync(NewSearch, new FeatureTargetingContext("anyone", "globex").ToEvaluationContext()));
    }

    [Theory]
    [InlineData("2026-11-27T00:00:00Z", "2026-11-30T23:59:59Z")]
    public async Task Recipe4_TimeWindow_ParsesTheReadmeFormat(string start, string end)
    {
        static string Window(string s, string e) => $$"""
            { "feature_management": { "feature_flags": [
              { "id": "BlackFriday", "enabled": true, "conditions": { "client_filters": [
                { "name": "Microsoft.TimeWindow", "parameters": { "Start": "{{s}}", "End": "{{e}}" } } ] } }
            ] } }
            """;
        FeatureFlag<bool> blackFriday = FeatureFlag.Boolean("BlackFriday");

        await using var readme = await FeatureTestHost.StartAsync(FeatureTestHost.Json(Window(start, end)));
        await using var open = await FeatureTestHost.StartAsync(FeatureTestHost.Json(Window("2020-01-01T00:00:00Z", "2099-01-01T00:00:00Z")));

        Assert.Equal(DateTimeOffset.UtcNow is var now && now >= DateTimeOffset.Parse(start) && now <= DateTimeOffset.Parse(end),
            await readme.NewScopeClient().IsEnabledAsync(blackFriday));
        Assert.True(await open.NewScopeClient().IsEnabledAsync(blackFriday));
    }

    [Fact]
    public async Task Recipe5_AbExperiment_SplitsUsersStablyByTheSeed()
    {
        const string configuration = """
            { "feature_management": { "feature_flags": [
              {
                "id": "CheckoutButton",
                "enabled": true,
                "variants": [
                  { "name": "Control", "configuration_value": "Buy now" },
                  { "name": "Test", "configuration_value": "Complete purchase" }
                ],
                "allocation": {
                  "default_when_enabled": "Control",
                  "percentile": [ { "variant": "Control", "from": 0, "to": 50 }, { "variant": "Test", "from": 50, "to": 100 } ],
                  "seed": "checkout-button-2026-09"
                }
              }
            ] } }
            """;
        FeatureFlag<string> checkoutButton = FeatureFlag.String("CheckoutButton", "Buy now");
        await using var provider = await FeatureTestHost.StartAsync(FeatureTestHost.Json(configuration));

        var counts = new Dictionary<string, int>();
        for (int i = 0; i < 1000; i++)
        {
            EvaluationContext user = new FeatureTargetingContext($"user-{i}").ToEvaluationContext();
            FlagEvaluationDetails<string> button = await provider.NewScopeClient().GetDetailsAsync(checkoutButton, user);
            Assert.Equal(button.Variant, (await provider.NewScopeClient().GetDetailsAsync(checkoutButton, user)).Variant);
            Assert.Equal(button.Variant == "Test" ? "Complete purchase" : "Buy now", button.Value);
            counts[button.Variant!] = counts.GetValueOrDefault(button.Variant!) + 1;
        }

        Assert.InRange(counts["Control"], 400, 600);
        Assert.InRange(counts["Test"], 400, 600);
    }

    public interface IPlanLookup
    {
        Task<string> CurrentPlanAsync();
    }

    private sealed class EnterprisePlan : IPlanLookup
    {
        public Task<string> CurrentPlanAsync() => Task.FromResult("enterprise");
    }

    [FilterAlias("Plan")]
    public sealed class PlanFilter(IPlanLookup plans) : IFeatureFilter
    {
        public async Task<bool> EvaluateAsync(FeatureFilterEvaluationContext context) =>
            await plans.CurrentPlanAsync() == context.Parameters["Plan"];
    }

    [Theory]
    [InlineData("enterprise", true)]
    [InlineData("starter", false)]
    public async Task Recipe6_OwnCondition(string requiredPlan, bool expected)
    {
        string configuration = $$"""
            { "feature_management": { "feature_flags": [
              { "id": "Exports", "enabled": true, "conditions": { "client_filters": [ { "name": "Plan", "parameters": { "Plan": "{{requiredPlan}}" } } ] } }
            ] } }
            """;
        await using var provider = await FeatureTestHost.StartAsync(
            FeatureTestHost.Json(configuration),
            flags => flags.AddFeatureFilter<PlanFilter>(),
            before: s => s.AddSingleton<IPlanLookup, EnterprisePlan>());

        Assert.Equal(expected, await provider.NewScopeClient().IsEnabledAsync(FeatureFlag.Boolean("Exports")));
    }

    [Fact]
    public async Task Recipe8_MetricsHook_RecordsOnTheOpenFeatureMeter()
    {
        var meters = new HashSet<string>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            lock (meters)
            {
                meters.Add(instrument.Meter.Name);
            }

            l.EnableMeasurementEvents(instrument);
        };
        listener.Start();

        await using var provider = await FeatureTestHost.StartAsync(
            FeatureTestHost.Json(QuickStartConfiguration),
            flags => flags.ConfigureOpenFeature(openFeature => openFeature.AddHook(new MetricsHook(MetricsHookOptions.Default))));

        Assert.True(await provider.NewScopeClient().IsEnabledAsync(Flags.NewCheckout, new FeatureTargetingContext("alice").ToEvaluationContext()));
        lock (meters)
        {
            Assert.Contains("OpenFeature", meters);
        }
    }
}
