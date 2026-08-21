using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using SharedKernel.Integration.Webhooks.Options;

namespace SharedKernel.Integration.Webhooks.Dispatch;

/// <summary>
/// Default <see cref="IWebhookUrlValidator"/> — resolves the target host via <see cref="Dns"/> and
/// rejects delivery when the resolved <see cref="IPAddress"/> falls in a loopback, link-local,
/// private, or multicast/reserved range, for both IPv4 and IPv6.
/// </summary>
/// <remarks>
/// Fail-closed by default. Checks the DNS-<b>resolved</b> <see cref="IPAddress"/>, never the literal
/// hostname string — a hostname-only check is trivially bypassed by DNS rebinding. The only
/// permitted opt-out is <see cref="WebhookDeliveryOptions.AllowPrivateNetworkTargets"/>, intended
/// for legitimate internal test/staging subscriptions only.
/// </remarks>
public sealed class PrivateNetworkWebhookUrlValidator : IWebhookUrlValidator
{
    private readonly IOptions<WebhookDeliveryOptions> _options;

    /// <summary>Initializes a new instance of <see cref="PrivateNetworkWebhookUrlValidator"/>.</summary>
    public PrivateNetworkWebhookUrlValidator(IOptions<WebhookDeliveryOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <inheritdoc />
    public async Task<bool> ValidateAsync(Uri url, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(url);

        if (_options.Value.AllowPrivateNetworkTargets)
        {
            return true;
        }

        if (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp)
        {
            return false;
        }

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(url.Host, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is SocketException or ArgumentException)
        {
            // An unresolvable host is not a safe delivery target — fail closed.
            return false;
        }

        if (addresses.Length == 0)
        {
            return false;
        }

        return !addresses.Any(IsPrivateOrReservedAddress);
    }

    /// <summary>
    /// Determines whether <paramref name="address"/> falls in a loopback, link-local
    /// (<c>169.254.0.0/16</c>/<c>fe80::/10</c>), private (RFC1918/RFC4193), or multicast/reserved
    /// range.
    /// </summary>
    private static bool IsPrivateOrReservedAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        return address.AddressFamily switch
        {
            AddressFamily.InterNetwork => IsPrivateOrReservedIPv4(address),
            AddressFamily.InterNetworkV6 => IsPrivateOrReservedIPv6(address),
            _ => true, // unknown address family — fail closed.
        };
    }

    private static bool IsPrivateOrReservedIPv4(IPAddress address)
    {
        var bytes = address.GetAddressBytes();

        return bytes[0] switch
        {
            0 => true, // 0.0.0.0/8 — "this network"
            10 => true, // 10.0.0.0/8 — RFC1918 private
            100 when bytes[1] is >= 64 and <= 127 => true, // 100.64.0.0/10 — carrier-grade NAT
            127 => true, // 127.0.0.0/8 — loopback (redundant with IPAddress.IsLoopback, kept explicit)
            169 when bytes[1] == 254 => true, // 169.254.0.0/16 — link-local
            172 when bytes[1] is >= 16 and <= 31 => true, // 172.16.0.0/12 — RFC1918 private
            192 when bytes[1] == 168 => true, // 192.168.0.0/16 — RFC1918 private
            >= 224 => true, // 224.0.0.0/4 multicast + 240.0.0.0/4 reserved
            _ => false,
        };
    }

    private static bool IsPrivateOrReservedIPv6(IPAddress address)
    {
        if (address.IsIPv6LinkLocal || address.IsIPv6Multicast || address.IsIPv6SiteLocal)
        {
            return true;
        }

        // fc00::/7 — unique local addresses (RFC4193), covering fd00::/8 and fc00::/8.
        var firstByte = address.GetAddressBytes()[0];
        return (firstByte & 0xFE) == 0xFC;
    }
}
