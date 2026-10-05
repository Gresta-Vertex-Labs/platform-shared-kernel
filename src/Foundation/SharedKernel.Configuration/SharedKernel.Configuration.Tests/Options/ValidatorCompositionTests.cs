using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharedKernel.Configuration.Extensions;
using Xunit;

namespace SharedKernel.Configuration.Tests.Options;

/// <summary>
/// Pins the registration semantics of <c>IValidateOptions&lt;TOptions&gt;</c>, which is a
/// COLLECTION service: the options pipeline runs every registered validator for a type, never
/// just the first one.
/// </summary>
/// <remarks>
/// These tests exist because the validator overload originally used
/// <c>TryAddSingleton&lt;IValidateOptions&lt;TOptions&gt;, TValidator&gt;()</c>. That silently
/// registered NOTHING whenever any other validator for the same options type was already
/// present — including the Data Annotations validator added by the sibling overload — so a
/// caller's rules never ran and invalid configuration started the host cleanly. Every test here
/// fails if the registration reverts to <c>TryAddSingleton</c>.
/// </remarks>
public sealed class ValidatorCompositionTests
{
    [Fact]
    public async Task DataAnnotationsRegisteredFirst_CustomValidatorStillRuns()
    {
        // Passes Data Annotations; violates only the cross-property rule.
        IHost host = BuildHost(
            services =>
            {
                IConfigurationSection section = Section(name: "ok", min: 100, max: 1);
                services.AddValidatedOptions<PoolOptions>(section);
                services.AddValidatedOptions<PoolOptions, MinNotAboveMaxValidator>(section);
            });

        OptionsValidationException ex =
            await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.Contains("MinSize must not exceed MaxSize.", ex.Failures);
    }

    [Fact]
    public async Task CustomValidatorRegisteredFirst_DataAnnotationsStillRun()
    {
        // Satisfies the cross-property rule; violates only [Required].
        IHost host = BuildHost(
            services =>
            {
                IConfigurationSection section = Section(name: null, min: 1, max: 9);
                services.AddValidatedOptions<PoolOptions, MinNotAboveMaxValidator>(section);
                services.AddValidatedOptions<PoolOptions>(section);
            });

        OptionsValidationException ex =
            await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.Contains(ex.Failures, f => f.Contains("Name", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TwoDistinctValidators_TheFirstOnesRuleIsEnforced()
    {
        IHost host = BuildHost(
            services =>
            {
                IConfigurationSection section = Section(name: "ok", min: 100, max: 1);
                services.AddValidatedOptions<PoolOptions, MinNotAboveMaxValidator>(section);
                services.AddValidatedOptions<PoolOptions, NameNotReservedValidator>(section);
            });

        OptionsValidationException ex =
            await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.Contains("MinSize must not exceed MaxSize.", ex.Failures);
    }

    [Fact]
    public async Task TwoDistinctValidators_TheSecondOnesRuleIsAlsoEnforced()
    {
        // This is the half a TryAddSingleton registration loses outright.
        IHost host = BuildHost(
            services =>
            {
                IConfigurationSection section = Section(name: "reserved", min: 1, max: 9);
                services.AddValidatedOptions<PoolOptions, MinNotAboveMaxValidator>(section);
                services.AddValidatedOptions<PoolOptions, NameNotReservedValidator>(section);
            });

        OptionsValidationException ex =
            await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.Contains("Name 'reserved' is not allowed.", ex.Failures);
    }

    [Fact]
    public void SameValidatorRegisteredTwice_IsRegisteredOnce()
    {
        // TryAddEnumerable de-duplicates the identical (service, implementation) pair, so
        // idempotency is preserved even though the service type is a collection.
        var services = new ServiceCollection();
        IConfigurationSection section = Section(name: "ok", min: 1, max: 9);

        services.AddValidatedOptions<PoolOptions, MinNotAboveMaxValidator>(section);
        services.AddValidatedOptions<PoolOptions, MinNotAboveMaxValidator>(section);

        int registrations = services.Count(d =>
            d.ServiceType == typeof(IValidateOptions<PoolOptions>)
            && d.ImplementationType == typeof(MinNotAboveMaxValidator));

        Assert.Equal(1, registrations);
    }

    [Fact]
    public async Task ValidateDataAnnotationsFlag_WhenTrue_EnforcesTheAttributesToo()
    {
        IHost host = BuildHost(
            services => services.AddValidatedOptions<PoolOptions, MinNotAboveMaxValidator>(
                Section(name: null, min: 1, max: 9),
                validateDataAnnotations: true));

        OptionsValidationException ex =
            await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.Contains(ex.Failures, f => f.Contains("Name", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ValidateDataAnnotationsFlag_DefaultsToFalse_SoAttributesAreNotEnforced()
    {
        // [Required] Name is missing, but only the custom validator was asked for — and it is
        // satisfied. The host must start: this is what makes the flag's default meaningful.
        IHost host = BuildHost(
            services => services.AddValidatedOptions<PoolOptions, MinNotAboveMaxValidator>(
                Section(name: null, min: 1, max: 9)));

        await host.StartAsync();
        await host.StopAsync();

        // Reaching here at all is the assertion: no OptionsValidationException was raised for the
        // unsatisfied [Required]. Name is null rather than string.Empty because the configuration
        // key is present with a null value, and the binder writes that over the property
        // initializer.
        Assert.Null(host.Services.GetRequiredService<IOptions<PoolOptions>>().Value.Name);
    }

    private static IHost BuildHost(Action<IServiceCollection> configure)
        => Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) => configure(services))
            .Build();

    private static IConfigurationSection Section(string? name, int min, int max)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Pool:Name"] = name,
                ["Pool:MinSize"] = min.ToString(),
                ["Pool:MaxSize"] = max.ToString(),
            })
            .Build()
            .GetSection("Pool");

    public sealed class PoolOptions
    {
        [Required]
        public string Name { get; set; } = string.Empty;

        public int MinSize { get; set; }

        public int MaxSize { get; set; }
    }

    /// <summary>A rule no Data Annotations attribute can express — it spans two properties.</summary>
    public sealed class MinNotAboveMaxValidator : IValidateOptions<PoolOptions>
    {
        public ValidateOptionsResult Validate(string? name, PoolOptions options) =>
            options.MinSize <= options.MaxSize
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail("MinSize must not exceed MaxSize.");
    }

    public sealed class NameNotReservedValidator : IValidateOptions<PoolOptions>
    {
        public ValidateOptionsResult Validate(string? name, PoolOptions options) =>
            options.Name == "reserved"
                ? ValidateOptionsResult.Fail("Name 'reserved' is not allowed.")
                : ValidateOptionsResult.Success;
    }
}
