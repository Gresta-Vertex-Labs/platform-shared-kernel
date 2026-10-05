using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Configuration.Extensions;
using Xunit;

namespace SharedKernel.Configuration.Tests.Options;

/// <summary>
/// Pins the argument guards, the registration idempotency, and the two host- and reload-related
/// behaviours the XML docs describe — so a doc claim cannot drift away from what the code does.
/// </summary>
public sealed partial class ValidatedOptionsContractTests
{
    // ---- Argument guards, on all four overloads ----

    [Fact]
    public void NullServices_Throws()
    {
        IServiceCollection services = null!;

        Assert.Throws<ArgumentNullException>(
            () => services.AddValidatedOptions<PlainOptions>(Section()));
        Assert.Throws<ArgumentNullException>(
            () => services.AddValidatedOptions<PlainOptions, PlainOptionsValidator>(Section()));
    }

    [Fact]
    public void NullSection_Throws()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(
            () => services.AddValidatedOptions<PlainOptions>((IConfigurationSection)null!));
        Assert.Throws<ArgumentNullException>(
            () => services.AddValidatedOptions<PlainOptions, PlainOptionsValidator>(
                (IConfigurationSection)null!));
    }

    // ---- Idempotency: registering twice must not report every failure twice ----

    [Fact]
    public void RegisteredTwiceForTheSameName_RegistersOneDataAnnotationsValidator()
    {
        var services = new ServiceCollection();
        services.AddValidatedOptions<PlainOptions>(Section());
        services.AddValidatedOptions<PlainOptions>(Section());

        int validators = services.Count(d => d.ServiceType == typeof(IValidateOptions<PlainOptions>));

        Assert.Equal(1, validators);
    }

    [Fact]
    public void RegisteredTwiceForTheSameName_ReportsEachFailureOnce()
    {
        // The BCL's ValidateDataAnnotations() uses a plain AddSingleton, which duplicated both
        // validators and produced four failure messages for two broken properties. Measured
        // before the fix: 4. After: 2.
        var services = new ServiceCollection();
        services.AddValidatedOptions<PlainOptions>(InvalidSection());
        services.AddValidatedOptions<PlainOptions>(InvalidSection());

        OptionsValidationException ex = Assert.Throws<OptionsValidationException>(
            () => _ = services.BuildServiceProvider().GetRequiredService<IOptions<PlainOptions>>().Value);

        Assert.Equal(2, ex.Failures.Count());
    }

    [Fact]
    public void RegisteredForTwoDifferentNames_RegistersOneValidatorPerName()
    {
        var services = new ServiceCollection();
        services.AddValidatedOptions<PlainOptions>(Section(), name: "a");
        services.AddValidatedOptions<PlainOptions>(Section(), name: "b");

        int validators = services.Count(d => d.ServiceType == typeof(IValidateOptions<PlainOptions>));

        Assert.Equal(2, validators);
    }

    // ---- Documented behaviour 1: ValidateOnStart needs a real host ----

    [Fact]
    public void WithNoHost_InvalidConfigurationIsNotCaughtAtProviderBuildTime()
    {
        var services = new ServiceCollection();
        services.AddValidatedOptions<PlainOptions>(InvalidSection());

        ServiceProvider provider = services.BuildServiceProvider();

        // Nothing has validated yet — resolving the accessor itself is fine.
        Assert.NotNull(provider.GetService<IOptions<PlainOptions>>());

        // The failure surfaces only on first read of the value.
        Assert.Throws<OptionsValidationException>(
            () => _ = provider.GetRequiredService<IOptions<PlainOptions>>().Value);
    }

    // ---- Documented behaviour 2: reload-time validation, and where it throws ----

    [Fact]
    public void ReloadIntroducingAnInvalidValue_ThrowsFromReload_WhenAMonitorIsResolved()
    {
        IConfigurationRoot config = Reloadable(timeout: 30);
        var services = new ServiceCollection();
        services.AddValidatedOptions<PlainOptions>(config.GetSection("S"));
        ServiceProvider provider = services.BuildServiceProvider();

        // Resolving the monitor is what registers the eager re-validating change callback.
        Assert.Equal(30, provider.GetRequiredService<IOptionsMonitor<PlainOptions>>().CurrentValue.Timeout);

        config["S:Timeout"] = "9999";

        AggregateException ex = Assert.Throws<AggregateException>(() => config.Reload());
        Assert.IsType<OptionsValidationException>(ex.InnerException);
    }

    [Fact]
    public void ReloadIntroducingAnInvalidValue_IsSilent_WhenNoMonitorIsResolved()
    {
        IConfigurationRoot config = Reloadable(timeout: 30);
        var services = new ServiceCollection();
        services.AddValidatedOptions<PlainOptions>(config.GetSection("S"));
        ServiceProvider provider = services.BuildServiceProvider();

        config["S:Timeout"] = "9999";

        Assert.Null(Record.Exception(() => config.Reload()));

        // Deferred to the next read instead.
        Assert.Throws<OptionsValidationException>(
            () => _ = provider.GetRequiredService<IOptions<PlainOptions>>().Value);
    }

    // ---- Registration surface ----

    [Fact]
    public void RegistersAllThreeOptionsAccessors()
    {
        var services = new ServiceCollection();
        services.AddValidatedOptions<PlainOptions>(Section());
        ServiceProvider provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<IOptions<PlainOptions>>());
        Assert.NotNull(provider.GetService<IOptionsSnapshot<PlainOptions>>());
        Assert.NotNull(provider.GetService<IOptionsMonitor<PlainOptions>>());
    }

    [Fact]
    public void ReturnsTheSameCollectionForChaining()
    {
        var services = new ServiceCollection();

        Assert.Same(services, services.AddValidatedOptions<PlainOptions>(Section()));
        Assert.Same(
            services,
            services.AddValidatedOptions<PlainOptions, PlainOptionsValidator>(Section()));
    }

    private static IConfigurationSection Section()
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["S:Name"] = "ok",
                ["S:Timeout"] = "30",
            })
            .Build()
            .GetSection("S");

    /// <summary>Breaks exactly two properties, so a duplicated-message count is unambiguous.</summary>
    private static IConfigurationSection InvalidSection()
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["S:Timeout"] = "0" })
            .Build()
            .GetSection("S");

    private static IConfigurationRoot Reloadable(int timeout)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["S:Name"] = "ok",
                ["S:Timeout"] = timeout.ToString(),
            })
            .Build();

    public sealed class PlainOptions
    {
        [Required]
        public string Name { get; set; } = string.Empty;

        [Range(1, 300)]
        public int Timeout { get; set; }
    }

    [OptionsValidator]
    public sealed partial class PlainOptionsValidator : IValidateOptions<PlainOptions>
    {
    }
}
