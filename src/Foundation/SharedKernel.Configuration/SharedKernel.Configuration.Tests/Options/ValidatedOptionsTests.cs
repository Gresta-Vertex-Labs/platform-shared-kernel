using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharedKernel.Configuration.Extensions;
using Xunit;

namespace SharedKernel.Configuration.Tests.Options;

public sealed class ValidatedOptionsTests
{
    // ---- Valid configuration resolves successfully ----

    [Fact]
    public void AddValidatedOptions_ValidConfig_RegistersIOptions()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["TestService:Name"]    = "MyService",
            ["TestService:Timeout"] = "30",
        });

        var services = new ServiceCollection();
        services.AddValidatedOptions<TestServiceOptions>(config.GetSection("TestService"));

        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<TestServiceOptions>>();
        Assert.Equal("MyService", options.Value.Name);
        Assert.Equal(30, options.Value.Timeout);
    }

    [Fact]
    public void AddValidatedOptions_ValidConfig_RegistersIOptionsMonitor()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["TestService:Name"]    = "Monitor",
            ["TestService:Timeout"] = "60",
        });

        var services = new ServiceCollection();
        services.AddValidatedOptions<TestServiceOptions>(config.GetSection("TestService"));

        var sp = services.BuildServiceProvider();
        var monitor = sp.GetRequiredService<IOptionsMonitor<TestServiceOptions>>();
        Assert.Equal("Monitor", monitor.CurrentValue.Name);
    }

    // ---- Invalid configuration fails at host startup ----

    [Fact]
    public async Task AddValidatedOptions_InvalidConfig_ThrowsAtStartup()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            // Name is required — leave it missing to trigger validation failure
            ["TestService:Timeout"] = "30",
        });

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                services.AddValidatedOptions<TestServiceOptions>(config.GetSection("TestService"));
            })
            .Build();

        await Assert.ThrowsAnyAsync<OptionsValidationException>(
            () => host.StartAsync());
    }

    [Fact]
    public async Task AddValidatedOptions_OutOfRangeValue_ThrowsAtStartup()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["TestService:Name"]    = "MyService",
            ["TestService:Timeout"] = "0", // must be >= 1
        });

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                services.AddValidatedOptions<TestServiceOptions>(config.GetSection("TestService"));
            })
            .Build();

        await Assert.ThrowsAnyAsync<OptionsValidationException>(
            () => host.StartAsync());
    }

    private static IConfiguration BuildConfig(Dictionary<string, string?> data)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(data)
            .Build();

    // ---- Options class used for testing ----

    private sealed class TestServiceOptions
    {
        [Required]
        public string Name { get; set; } = string.Empty;

        [Range(1, 300)]
        public int Timeout { get; set; }
    }
}
