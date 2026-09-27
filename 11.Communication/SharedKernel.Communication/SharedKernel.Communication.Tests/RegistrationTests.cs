using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.ServiceDiscovery;
using SharedKernel.Communication.Internal;
using SharedKernel.Execution.Context;

namespace SharedKernel.Communication.Tests;

public sealed class RegistrationTests
{
    [Fact]
    public void A_second_call_returns_a_builder_over_the_same_registrations()
    {
        var services = TestHost.Services();
        var configuration = TestHost.Configuration();

        services.AddSharedKernelCommunication(configuration);
        int count = services.Count;
        ICommunicationBuilder second = services.AddSharedKernelCommunication(configuration);

        services.Count.Should().Be(count);
        second.Services.Should().BeSameAs(services);
        second.Configuration.Should().BeSameAs(configuration);
    }

    [Fact]
    public void The_request_context_accessor_is_registered_unless_the_host_has_one()
    {
        var services = TestHost.Services();
        var mine = new RequestContextAccessor();
        services.AddSingleton<IRequestContextAccessor>(mine);

        services.AddSharedKernelCommunication(TestHost.Configuration());

        services.BuildServiceProvider().GetRequiredService<IRequestContextAccessor>().Should().BeSameAs(mine);
    }

    [Fact]
    public async Task Endpoints_in_the_Services_section_win_over_the_host_as_written()
    {
        var services = TestHost.Services();
        services.AddSharedKernelCommunication(TestHost.Configuration(
            ("Services:inventory:http:0", "http://localhost:5080"),
            ("Services:inventory:grpc:0", "http://localhost:5081")));
        await using var provider = services.BuildServiceProvider();
        var resolver = provider.GetRequiredService<ServiceEndpointResolver>();

        ServiceEndpointSource http = await resolver.GetEndpointsAsync("http://inventory", CancellationToken.None);
        ServiceEndpointSource grpc = await resolver.GetEndpointsAsync("http://_grpc.inventory", CancellationToken.None);

        http.Endpoints.Should().ContainSingle().Which.EndPoint.ToString().Should().Contain("5080");
        grpc.Endpoints.Should().ContainSingle().Which.EndPoint.ToString().Should().Contain("5081");
    }

    [Fact]
    public async Task A_host_nothing_knows_is_called_as_written()
    {
        var services = TestHost.Services();
        services.AddSharedKernelCommunication(TestHost.Configuration());
        await using var provider = services.BuildServiceProvider();

        ServiceEndpointSource source = await provider.GetRequiredService<ServiceEndpointResolver>()
            .GetEndpointsAsync("http://inventory.shop.svc.cluster.local", CancellationToken.None);

        source.Endpoints.Should().ContainSingle()
            .Which.EndPoint.Should().BeOfType<DnsEndPoint>()
            .Which.Host.Should().Be("inventory.shop.svc.cluster.local");
    }

    [Theory]
    [InlineData("Dns", "DnsServiceEndpointProviderFactory")]
    [InlineData("DnsSrv", "DnsSrvServiceEndpointProviderFactory")]
    public void The_discovery_mode_adds_its_DNS_provider_between_configuration_and_pass_through(string mode, string provider)
    {
        var services = TestHost.Services();
        services.AddSharedKernelCommunication(TestHost.Configuration(("SharedKernel:Communication:ServiceDiscovery:Mode", mode)));

        var factories = services
            .Where(d => d.ServiceType == typeof(IServiceEndpointProviderFactory))
            .Select(d => d.ImplementationType?.Name)
            .ToList();

        factories.Should().ContainInOrder("ConfigurationServiceEndpointProviderFactory", provider, "PassThroughServiceEndpointProviderFactory");
    }

    [Fact]
    public void Configuration_mode_adds_no_DNS_provider()
    {
        var services = TestHost.Services();
        services.AddSharedKernelCommunication(TestHost.Configuration());

        services.Where(d => d.ServiceType == typeof(IServiceEndpointProviderFactory))
            .Select(d => d.ImplementationType?.Name)
            .Should().NotContain(name => name!.StartsWith("Dns", StringComparison.Ordinal));
    }

    [Fact]
    public void An_invalid_refresh_period_fails_at_startup()
    {
        var services = TestHost.Services();
        services.AddSharedKernelCommunication(TestHost.Configuration(("SharedKernel:Communication:ServiceDiscovery:RefreshPeriod", "00:00:00")));

        Action start = () => services.BuildServiceProvider().ValidateOnStart();

        start.Should().Throw<OptionsValidationException>().WithMessage("*RefreshPeriod*");
    }

    [Fact]
    public void A_client_name_can_be_taken_once_across_protocols()
    {
        var services = TestHost.Services();
        services.AddSharedKernelCommunication(TestHost.Configuration());
        CommunicationClientRegistry.Reserve(services, "inventory", "REST");

        Action again = () => CommunicationClientRegistry.Reserve(services, "Inventory", "gRPC");

        again.Should().Throw<InvalidOperationException>().WithMessage("*REST client named 'Inventory'*");
    }

    [Fact]
    public void A_client_needs_AddSharedKernelCommunication_first()
    {
        Action reserve = () => CommunicationClientRegistry.Reserve(TestHost.Services(), "inventory", "REST");

        reserve.Should().Throw<InvalidOperationException>().WithMessage("*AddSharedKernelCommunication*");
    }

    [Fact]
    public void A_client_name_cannot_contain_a_section_separator()
    {
        var services = TestHost.Services();
        services.AddSharedKernelCommunication(TestHost.Configuration());

        Action reserve = () => CommunicationClientRegistry.Reserve(services, "a:b", "REST");

        reserve.Should().Throw<ArgumentException>();
    }
}
