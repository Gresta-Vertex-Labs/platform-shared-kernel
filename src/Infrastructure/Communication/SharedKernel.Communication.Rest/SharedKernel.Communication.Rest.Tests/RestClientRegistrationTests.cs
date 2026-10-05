using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace SharedKernel.Communication.Rest.Tests;

public sealed class RestClientRegistrationTests
{
    [Fact]
    public void The_base_address_comes_from_the_client_section()
    {
        using var harness = new RestHarness();

        harness.Http.BaseAddress.Should().Be(new Uri("http://inventory"));
        harness.Http.Timeout.Should().Be(Timeout.InfiniteTimeSpan, "the resilience pipeline owns every timeout");
    }

    [Fact]
    public void Code_changes_the_bound_options()
    {
        using var harness = new RestHarness(configure: c => c.Configure(o => o.BaseAddress = new Uri("https://inventory.example.com/v2/")));

        harness.Http.BaseAddress.Should().Be(new Uri("https://inventory.example.com/v2/"));
    }

    [Fact]
    public void A_client_without_an_address_fails_at_startup()
    {
        Action start = () => Build(("SharedKernel:Communication:Clients:inventory:AttemptTimeout", "00:00:05"));

        start.Should().Throw<OptionsValidationException>().WithMessage("*BaseAddress is required*");
    }

    [Theory]
    [InlineData("AttemptTimeout", "00:00:20", "*SamplingDuration*twice AttemptTimeout*")]
    [InlineData("TotalTimeout", "00:00:01", "*TotalTimeout must be at least AttemptTimeout*")]
    [InlineData("Retry:MaxRetryAttempts", "11", "*MaxRetryAttempts*")]
    [InlineData("BaseAddress", "ftp://inventory", "*http, https*")]
    public void Invalid_settings_fail_at_startup_with_their_name(string key, string value, string message)
    {
        Action start = () => Build(
            ("SharedKernel:Communication:Clients:inventory:BaseAddress", "http://inventory"),
            ($"SharedKernel:Communication:Clients:inventory:{key}", value));

        start.Should().Throw<OptionsValidationException>().WithMessage(message);
    }

    [Fact]
    public void A_disabled_circuit_breaker_is_not_validated()
    {
        Action start = () => Build(
            ("SharedKernel:Communication:Clients:inventory:BaseAddress", "http://inventory"),
            ("SharedKernel:Communication:Clients:inventory:AttemptTimeout", "00:00:20"),
            ("SharedKernel:Communication:Clients:inventory:CircuitBreaker:Enabled", "false"));

        start.Should().NotThrow();
    }

    [Fact]
    public void An_interface_needs_its_implementation()
    {
        var builder = new ServiceCollection().AddSharedKernelCommunication(new ConfigurationBuilder().Build());

        Action register = () => builder.AddRestClient<IInventoryClient>("inventory");

        register.Should().Throw<ArgumentException>().WithMessage("*AddRestClient<IInventoryClient, TImplementation>*");
    }

    [Fact]
    public void A_class_registers_on_its_own()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCommunication(Configuration(("SharedKernel:Communication:Clients:inventory:BaseAddress", "http://inventory")))
            .AddRestClient<InventoryClient>("inventory");

        services.BuildServiceProvider().GetRequiredService<InventoryClient>().Http.BaseAddress.Should().Be(new Uri("http://inventory"));
    }

    [Fact]
    public void A_name_is_used_once()
    {
        var builder = new ServiceCollection().AddSharedKernelCommunication(new ConfigurationBuilder().Build())
            .AddRestClient<IInventoryClient, InventoryClient>("inventory");

        Action again = () => builder.AddRestClient<InventoryClient>("inventory");

        again.Should().Throw<InvalidOperationException>().WithMessage("*already registered*");
    }

    private static void Build(params (string Key, string? Value)[] settings)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharedKernelCommunication(Configuration(settings)).AddRestClient<IInventoryClient, InventoryClient>("inventory");
        services.BuildServiceProvider().GetRequiredService<IStartupValidator>().Validate();
    }

    // One source per setting, so a later setting overrides an earlier one with the same key.
    private static IConfiguration Configuration(params (string Key, string? Value)[] settings) =>
        settings
            .Aggregate(new ConfigurationBuilder(), (builder, s) => (ConfigurationBuilder)builder.AddInMemoryCollection([new(s.Key, s.Value)]))
            .Build();
}
