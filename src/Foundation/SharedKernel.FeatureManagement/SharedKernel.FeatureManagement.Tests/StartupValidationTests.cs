using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenFeature;
using Xunit;

namespace SharedKernel.FeatureManagement.Tests;

/// <summary>Declared flags are checked when a real host starts.</summary>
public sealed class StartupValidationTests
{
    private static IHost BuildHost(Action<FeatureFlagOptions> configure)
    {
        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddConfiguration(FeatureTestHost.Json(VariantFlagTests.Configuration));
        builder.Services.AddSharedKernelFeatureManagement(builder.Configuration, configure);
        return builder.Build();
    }

    [Fact]
    public async Task ValidFlags_StartTheHost_AndEvaluate()
    {
        FeatureFlag<string> theme = FeatureFlag.String("Theme", "fallback");
        FeatureFlag<int> pageSize = FeatureFlag.Integer("PageSize", 10);
        FeatureFlag<CheckoutSettings?> checkout = FeatureFlag.Object<CheckoutSettings?>("Checkout", null, TestJsonContext.Default.CheckoutSettings);

        using IHost host = BuildHost(o => o.ValidateOnStart(theme, pageSize, checkout));
        await host.StartAsync();

        using IServiceScope scope = host.Services.CreateScope();
        Assert.Equal("classic", await scope.ServiceProvider.GetRequiredService<IFeatureClient>().GetValueAsync(theme));
        await host.StopAsync();
    }

    [Fact]
    public async Task MissingAndMistypedFlags_StopStartup_ListingEveryProblem()
    {
        using IHost host = BuildHost(o => o.ValidateOnStart(
            FeatureFlag.Boolean("Chekout"),                              // misspelled
            FeatureFlag.Integer("BrokenNumber", 0),                      // "lots" is not a number
            FeatureFlag.Integer("Theme", 0),                             // text variants
            FeatureFlag.Object<CheckoutSettings?>("Discount", null, TestJsonContext.Default.CheckoutSettings),
            FeatureFlag.String("PageSize", "x")));                       // valid: numbers are text too

        var error = await Assert.ThrowsAsync<FeatureFlagValidationException>(() => host.StartAsync());

        Assert.Equal(
            [
                "'Chekout' is not configured.",
                "'BrokenNumber' variant 'Bad': its configuration_value is not a whole number.",
                "'Theme' variant 'Classic': its configuration_value is not a whole number.",
                "'Theme' variant 'Dark': its configuration_value is not a whole number.",
            ],
            error.Failures.Take(4));
        Assert.StartsWith("'Discount' variant 'Standard': ", error.Failures[4]);
        Assert.Equal(5, error.Failures.Count);
        Assert.Contains("- 'Chekout' is not configured.", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AVariantFlagWithoutVariants_FailsValidation()
    {
        const string configuration = """{ "feature_management": { "feature_flags": [ { "id": "Plain", "enabled": true } ] } }""";
        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddConfiguration(FeatureTestHost.Json(configuration));
        builder.Services.AddSharedKernelFeatureManagement(builder.Configuration, o => o.ValidateOnStart(FeatureFlag.String("Plain", "x")));
        using IHost host = builder.Build();

        var error = await Assert.ThrowsAsync<FeatureFlagValidationException>(() => host.StartAsync());

        Assert.Equal(["'Plain' has no variants; a FeatureFlag.String flag reads its value from one."], error.Failures);
    }

    [Fact]
    public void WithoutDeclaredFlags_NoValidatorIsRegistered()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelFeatureManagement(FeatureTestHost.Json("{}"));

        Assert.DoesNotContain(services, d => d.ImplementationType?.Name == "FeatureFlagValidationService");
    }
}
