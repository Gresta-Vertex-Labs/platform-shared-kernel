using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharedKernel.Configuration.Extensions;
using Xunit;

namespace SharedKernel.Configuration.Tests.Options;

/// <summary>
/// Proves the additive <c>AddValidatedOptions&lt;TOptions, TValidator&gt;</c> overload against a
/// REAL, compiled <see cref="OptionsValidatorAttribute"/>-annotated partial class — the in-box
/// BCL source generator (part of the base <c>Microsoft.Extensions.Options</c> package) generates
/// this class's <see cref="IValidateOptions{TOptions}.Validate"/> method body at compile time from
/// the <see cref="System.ComponentModel.DataAnnotations"/> attributes on
/// <see cref="SourceGeneratedTestServiceOptions"/> — never reflection at validation time.
/// </summary>
public sealed partial class SourceGeneratedOptionsValidationTests
{
    // ---- Valid configuration passes at host start ----

    [Fact]
    public async Task AddValidatedOptions_WithGeneratedValidator_ValidConfig_StartsHostSuccessfully()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["SourceGeneratedTestService:Name"]    = "MyService",
            ["SourceGeneratedTestService:Timeout"] = "30",
        });

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                services.AddValidatedOptions<SourceGeneratedTestServiceOptions, SourceGeneratedTestServiceOptionsValidator>(
                    config.GetSection("SourceGeneratedTestService"));
            })
            .Build();

        // Must not throw — valid configuration.
        await host.StartAsync();
        await host.StopAsync();

        var options = host.Services.GetRequiredService<IOptions<SourceGeneratedTestServiceOptions>>();
        Assert.Equal("MyService", options.Value.Name);
        Assert.Equal(30, options.Value.Timeout);
    }

    // ---- Invalid configuration fails at IHost.StartAsync() — identical .ValidateOnStart() semantics ----

    [Fact]
    public async Task AddValidatedOptions_WithGeneratedValidator_MissingRequiredField_ThrowsAtStartup()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            // Name is [Required] — leave it missing to trigger the generated validator's failure.
            ["SourceGeneratedTestService:Timeout"] = "30",
        });

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                services.AddValidatedOptions<SourceGeneratedTestServiceOptions, SourceGeneratedTestServiceOptionsValidator>(
                    config.GetSection("SourceGeneratedTestService"));
            })
            .Build();

        await Assert.ThrowsAnyAsync<OptionsValidationException>(() => host.StartAsync());
    }

    [Fact]
    public async Task AddValidatedOptions_WithGeneratedValidator_OutOfRangeValue_ThrowsAtStartup()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["SourceGeneratedTestService:Name"]    = "MyService",
            ["SourceGeneratedTestService:Timeout"] = "0", // [Range(1, 300)] violation
        });

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                services.AddValidatedOptions<SourceGeneratedTestServiceOptions, SourceGeneratedTestServiceOptionsValidator>(
                    config.GetSection("SourceGeneratedTestService"));
            })
            .Build();

        await Assert.ThrowsAnyAsync<OptionsValidationException>(() => host.StartAsync());
    }

    // ---- The existing DataAnnotations overload's behavior is unchanged (regression coverage) ----

    [Fact]
    public async Task AddValidatedOptions_DataAnnotationsOverload_StillWorksUnchanged_ValidConfig()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["SourceGeneratedTestService:Name"]    = "StillWorks",
            ["SourceGeneratedTestService:Timeout"] = "42",
        });

        var services = new ServiceCollection();
        services.AddValidatedOptions<SourceGeneratedTestServiceOptions>(
            config.GetSection("SourceGeneratedTestService"));

        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<SourceGeneratedTestServiceOptions>>();
        Assert.Equal("StillWorks", options.Value.Name);
        Assert.Equal(42, options.Value.Timeout);
    }

    [Fact]
    public async Task AddValidatedOptions_DataAnnotationsOverload_StillWorksUnchanged_InvalidConfigThrowsAtStartup()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            // Name missing — same [Required] violation, but via the DataAnnotations overload.
            ["SourceGeneratedTestService:Timeout"] = "42",
        });

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                services.AddValidatedOptions<SourceGeneratedTestServiceOptions>(
                    config.GetSection("SourceGeneratedTestService"));
            })
            .Build();

        await Assert.ThrowsAnyAsync<OptionsValidationException>(() => host.StartAsync());
    }

    // ---- TryAddEnumerable semantics: calling the generated-validator overload twice never double-registers ----

    [Fact]
    public void AddValidatedOptions_WithGeneratedValidator_CalledTwice_RegistersValidatorOnlyOnce()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["SourceGeneratedTestService:Name"]    = "MyService",
            ["SourceGeneratedTestService:Timeout"] = "30",
        });

        var services = new ServiceCollection();
        services.AddValidatedOptions<SourceGeneratedTestServiceOptions, SourceGeneratedTestServiceOptionsValidator>(
            config.GetSection("SourceGeneratedTestService"));
        services.AddValidatedOptions<SourceGeneratedTestServiceOptions, SourceGeneratedTestServiceOptionsValidator>(
            config.GetSection("SourceGeneratedTestService"));

        int validatorRegistrationCount = services
            .Count(d => d.ServiceType == typeof(IValidateOptions<SourceGeneratedTestServiceOptions>));

        Assert.Equal(1, validatorRegistrationCount);
    }

    private static IConfiguration BuildConfig(Dictionary<string, string?> data)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(data)
            .Build();

    // ---- Options class + generated validator used for testing ----

    public sealed class SourceGeneratedTestServiceOptions
    {
        [Required]
        public string Name { get; set; } = string.Empty;

        [Range(1, 300)]
        public int Timeout { get; set; }
    }

    [OptionsValidator]
    public sealed partial class SourceGeneratedTestServiceOptionsValidator : IValidateOptions<SourceGeneratedTestServiceOptions>
    {
    }
}
