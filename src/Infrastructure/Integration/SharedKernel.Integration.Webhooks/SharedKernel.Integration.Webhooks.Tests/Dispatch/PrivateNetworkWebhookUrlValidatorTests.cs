using FluentAssertions;
using Microsoft.Extensions.Options;
using SharedKernel.Integration.Webhooks.Dispatch;
using SharedKernel.Integration.Webhooks.Options;

namespace SharedKernel.Integration.Webhooks.Tests.Dispatch;

/// <summary>
/// Unit coverage for <see cref="PrivateNetworkWebhookUrlValidator"/>. Every case below uses an
/// IP-address literal as the URL host — <see cref="System.Net.Dns.GetHostAddressesAsync(string, System.Threading.CancellationToken)"/>
/// resolves an IP literal without any real DNS lookup or network call.
/// </summary>
public sealed class PrivateNetworkWebhookUrlValidatorTests
{
    private static PrivateNetworkWebhookUrlValidator CreateValidator(bool allowPrivateNetworkTargets = false) =>
        new(Microsoft.Extensions.Options.Options.Create(new WebhookDeliveryOptions { AllowPrivateNetworkTargets = allowPrivateNetworkTargets }));

    [Theory]
    [InlineData("https://127.0.0.1/hook")] // IPv4 loopback
    [InlineData("https://10.0.0.5/hook")] // RFC1918 private
    [InlineData("https://172.16.5.5/hook")] // RFC1918 private
    [InlineData("https://172.31.255.254/hook")] // RFC1918 private, upper bound of 172.16.0.0/12
    [InlineData("https://192.168.1.1/hook")] // RFC1918 private
    [InlineData("https://169.254.1.1/hook")] // IPv4 link-local
    [InlineData("https://100.64.0.1/hook")] // carrier-grade NAT (RFC6598)
    [InlineData("https://224.0.0.1/hook")] // multicast
    [InlineData("https://[::1]/hook")] // IPv6 loopback
    [InlineData("https://[fe80::1]/hook")] // IPv6 link-local
    [InlineData("https://[fd12:3456:789a::1]/hook")] // IPv6 unique local (RFC4193)
    [InlineData("https://[fc00::1]/hook")] // IPv6 unique local (RFC4193)
    [InlineData("https://[ff02::1]/hook")] // IPv6 multicast
    public async Task ValidateAsync_PrivateOrReservedTarget_ReturnsFalse(string url)
    {
        var validator = CreateValidator();

        var isAllowed = await validator.ValidateAsync(new Uri(url), CancellationToken.None);

        isAllowed.Should().BeFalse();
    }

    [Theory]
    [InlineData("https://8.8.8.8/hook")] // public IPv4
    [InlineData("https://1.1.1.1/hook")] // public IPv4
    [InlineData("https://[2001:4860:4860::8888]/hook")] // public IPv6
    public async Task ValidateAsync_PublicTarget_ReturnsTrue(string url)
    {
        var validator = CreateValidator();

        var isAllowed = await validator.ValidateAsync(new Uri(url), CancellationToken.None);

        isAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_AllowPrivateNetworkTargetsEnabled_PermitsOtherwiseRejectedTarget()
    {
        var validator = CreateValidator(allowPrivateNetworkTargets: true);

        var isAllowed = await validator.ValidateAsync(new Uri("https://127.0.0.1/hook"), CancellationToken.None);

        isAllowed.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_NonHttpScheme_ReturnsFalse()
    {
        var validator = CreateValidator();

        var isAllowed = await validator.ValidateAsync(new Uri("ftp://8.8.8.8/hook"), CancellationToken.None);

        isAllowed.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_NullUrl_Throws()
    {
        var validator = CreateValidator();

        var act = async () => await validator.ValidateAsync(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
