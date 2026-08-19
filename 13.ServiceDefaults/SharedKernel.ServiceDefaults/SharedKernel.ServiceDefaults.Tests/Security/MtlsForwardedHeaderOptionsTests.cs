using System.Net;
using FluentAssertions;
using SharedKernel.ServiceDefaults.Security;

namespace SharedKernel.ServiceDefaults.Tests.Security;

/// <summary>Covers WO-061/P-394's <see cref="MtlsForwardedHeaderOptions.TrustedNetworks"/> allowlist surface.</summary>
public sealed class MtlsForwardedHeaderOptionsTests
{
    [Fact]
    public void TrustedNetworks_DefaultsToEmpty()
    {
        var options = new MtlsForwardedHeaderOptions();

        options.TrustedNetworks.Should().BeEmpty();
    }

    [Fact]
    public void AddTrustedProxy_Ipv4Address_AddsSlash32Network()
    {
        var options = new MtlsForwardedHeaderOptions();
        var address = IPAddress.Parse("10.1.2.3");

        options.AddTrustedProxy(address);

        options.TrustedNetworks.Should().ContainSingle();
        var network = options.TrustedNetworks.Single();
        network.PrefixLength.Should().Be(32);
        network.Contains(address).Should().BeTrue();
        network.Contains(IPAddress.Parse("10.1.2.4")).Should().BeFalse();
    }

    [Fact]
    public void AddTrustedProxy_Ipv6Address_AddsSlash128Network()
    {
        var options = new MtlsForwardedHeaderOptions();
        var address = IPAddress.Parse("::1");

        options.AddTrustedProxy(address);

        options.TrustedNetworks.Single().PrefixLength.Should().Be(128);
    }

    [Fact]
    public void AddTrustedProxy_NullAddress_ThrowsArgumentNullException()
    {
        var options = new MtlsForwardedHeaderOptions();

        var act = () => options.AddTrustedProxy(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddTrustedNetwork_AddsSuppliedNetworkVerbatim()
    {
        var options = new MtlsForwardedHeaderOptions();
        var network = IPNetwork.Parse("10.0.0.0/8");

        options.AddTrustedNetwork(network);

        options.TrustedNetworks.Should().ContainSingle().Which.Should().Be(network);
    }

    [Fact]
    public void AddTrustedNetwork_ReturnsSameInstance_ForFluentChaining()
    {
        var options = new MtlsForwardedHeaderOptions();

        var result = options.AddTrustedNetwork(IPNetwork.Parse("10.0.0.0/8"));

        result.Should().BeSameAs(options);
    }

    [Fact]
    public void AddTrustedProxy_ReturnsSameInstance_ForFluentChaining()
    {
        var options = new MtlsForwardedHeaderOptions();

        var result = options.AddTrustedProxy(IPAddress.Parse("10.1.2.3"));

        result.Should().BeSameAs(options);
    }

    [Fact]
    public void AddTrustedNetwork_MultipleCalls_AccumulatesAllNetworks()
    {
        var options = new MtlsForwardedHeaderOptions();

        options
            .AddTrustedNetwork(IPNetwork.Parse("10.0.0.0/8"))
            .AddTrustedNetwork(IPNetwork.Parse("192.168.0.0/16"));

        options.TrustedNetworks.Should().HaveCount(2);
    }
}
