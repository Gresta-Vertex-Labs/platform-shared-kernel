using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharedKernel.Configuration.Extensions;
using Xunit;

namespace SharedKernel.Configuration.Tests.Options;

/// <summary>
/// Covers named options instances — two differently-configured instances of one options type,
/// each bound from its own section and each validated on its own.
/// </summary>
public sealed class NamedValidatedOptionsTests
{
    private const string Primary = "primary";
    private const string Secondary = "secondary";

    [Fact]
    public void TwoNamedInstances_EachBindsItsOwnSection()
    {
        IConfiguration config = Configuration(primaryTimeout: 10, secondaryTimeout: 20);
        var services = new ServiceCollection();
        services.AddValidatedOptions<ClientOptions>(config.GetSection("Clients:Primary"), Primary);
        services.AddValidatedOptions<ClientOptions>(config.GetSection("Clients:Secondary"), Secondary);

        IOptionsMonitor<ClientOptions> monitor = services
            .BuildServiceProvider()
            .GetRequiredService<IOptionsMonitor<ClientOptions>>();

        Assert.Equal(10, monitor.Get(Primary).Timeout);
        Assert.Equal(20, monitor.Get(Secondary).Timeout);
    }

    [Fact]
    public async Task EveryNamedInstanceIsValidated_NotJustTheFirstOneRegistered()
    {
        // The load-bearing test for name-aware registration. The BCL's
        // DataAnnotationValidateOptions<T> is scoped to ONE name and skips others, so a naive
        // TryAddEnumerable — which de-duplicates on implementation type — would register the
        // validator for "primary" and leave "secondary" completely unvalidated, starting the
        // host cleanly on invalid configuration.
        IHost host = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                IConfiguration config = Configuration(primaryTimeout: 10, secondaryTimeout: 0);
                services.AddValidatedOptions<ClientOptions>(config.GetSection("Clients:Primary"), Primary);
                services.AddValidatedOptions<ClientOptions>(config.GetSection("Clients:Secondary"), Secondary);
            })
            .Build();

        OptionsValidationException ex =
            await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.Equal(Secondary, ex.OptionsName);
    }

    [Fact]
    public async Task AValidNamedInstanceIsNotFailedByAnInvalidSibling()
    {
        IHost host = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                IConfiguration config = Configuration(primaryTimeout: 10, secondaryTimeout: 20);
                services.AddValidatedOptions<ClientOptions>(config.GetSection("Clients:Primary"), Primary);
                services.AddValidatedOptions<ClientOptions>(config.GetSection("Clients:Secondary"), Secondary);
            })
            .Build();

        await host.StartAsync();
        await host.StopAsync();

        IOptionsMonitor<ClientOptions> monitor =
            host.Services.GetRequiredService<IOptionsMonitor<ClientOptions>>();
        Assert.Equal(10, monitor.Get(Primary).Timeout);
        Assert.Equal(20, monitor.Get(Secondary).Timeout);
    }

    [Fact]
    public void PlainIOptions_NeverSeesANamedInstance()
    {
        // Documented trap: IOptions<T> resolves the default (unnamed) instance only, so a
        // consumer of named options must inject IOptionsMonitor<T> or IOptionsSnapshot<T>.
        IConfiguration config = Configuration(primaryTimeout: 10, secondaryTimeout: 20);
        var services = new ServiceCollection();
        services.AddValidatedOptions<ClientOptions>(config.GetSection("Clients:Primary"), Primary);

        ClientOptions unnamed = services
            .BuildServiceProvider()
            .GetRequiredService<IOptions<ClientOptions>>()
            .Value;

        Assert.Equal(0, unnamed.Timeout);
    }

    [Fact]
    public void ACustomValidatorAppliesToEveryName_NotOnlyTheOneItWasRegisteredWith()
    {
        // Documented asymmetry with Data Annotations: TValidator is registered once against
        // IValidateOptions<T>, so it runs for every named instance. A validator that should
        // apply to one name must inspect its own name argument.
        IConfiguration config = Configuration(primaryTimeout: 10, secondaryTimeout: 20);
        var services = new ServiceCollection();
        services.AddValidatedOptions<ClientOptions, RejectEverythingValidator>(
            config.GetSection("Clients:Primary"), name: Primary);
        services.AddValidatedOptions<ClientOptions>(
            config.GetSection("Clients:Secondary"), name: Secondary);

        IOptionsMonitor<ClientOptions> monitor = services
            .BuildServiceProvider()
            .GetRequiredService<IOptionsMonitor<ClientOptions>>();

        OptionsValidationException ex =
            Assert.Throws<OptionsValidationException>(() => monitor.Get(Secondary));
        Assert.Contains("rejected", ex.Failures);
    }

    [Fact]
    public void ANameAwareValidatorCanScopeItselfWithSkip()
    {
        // The prescribed remedy for the asymmetry above.
        IConfiguration config = Configuration(primaryTimeout: 10, secondaryTimeout: 20);
        var services = new ServiceCollection();
        services.AddValidatedOptions<ClientOptions, RejectPrimaryOnlyValidator>(
            config.GetSection("Clients:Primary"), name: Primary);
        services.AddValidatedOptions<ClientOptions>(
            config.GetSection("Clients:Secondary"), name: Secondary);

        IOptionsMonitor<ClientOptions> monitor = services
            .BuildServiceProvider()
            .GetRequiredService<IOptionsMonitor<ClientOptions>>();

        Assert.Equal(20, monitor.Get(Secondary).Timeout);
        Assert.Throws<OptionsValidationException>(() => monitor.Get(Primary));
    }

    [Fact]
    public void PassingNullAsTheName_IsTheDefaultInstance()
    {
        IConfiguration config = Configuration(primaryTimeout: 10, secondaryTimeout: 20);
        var services = new ServiceCollection();
        services.AddValidatedOptions<ClientOptions>(config.GetSection("Clients:Primary"), name: null);

        Assert.Equal(
            10,
            services.BuildServiceProvider().GetRequiredService<IOptions<ClientOptions>>().Value.Timeout);
    }

    private static IConfiguration Configuration(int primaryTimeout, int secondaryTimeout)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Clients:Primary:Timeout"] = primaryTimeout.ToString(),
                ["Clients:Secondary:Timeout"] = secondaryTimeout.ToString(),
            })
            .Build();

    public sealed class ClientOptions
    {
        [Range(1, 300)]
        public int Timeout { get; set; }
    }

    public sealed class RejectEverythingValidator : IValidateOptions<ClientOptions>
    {
        public ValidateOptionsResult Validate(string? name, ClientOptions options) =>
            ValidateOptionsResult.Fail("rejected");
    }

    public sealed class RejectPrimaryOnlyValidator : IValidateOptions<ClientOptions>
    {
        public ValidateOptionsResult Validate(string? name, ClientOptions options) =>
            name == Primary ? ValidateOptionsResult.Fail("rejected") : ValidateOptionsResult.Skip;
    }
}
