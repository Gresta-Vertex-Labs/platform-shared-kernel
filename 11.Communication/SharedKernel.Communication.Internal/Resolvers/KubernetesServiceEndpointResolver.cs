using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.ServiceDiscovery;
using SharedKernel.Communication.Internal.Options;

namespace SharedKernel.Communication.Internal.Resolvers;

/// <summary>
/// Production <see cref="IServiceEndpointResolver"/> that performs K8s DNS-based resolution.
/// Resolution order:
/// <list type="number">
///   <item>DNS SRV lookup via <see cref="ServiceEndpointResolver"/> (<c>_http._tcp.{service}.{namespace}.svc.{clusterDomain}</c>)</item>
///   <item>A-record lookup via <see cref="ServiceEndpointResolver"/> (<c>{service}.{namespace}.svc.{clusterDomain}</c>)</item>
///   <item>K8s convention URI fallback (<c>{scheme}://{service}.{namespace}.svc.{clusterDomain}</c>) — never throws</item>
/// </list>
/// Registered as singleton via <c>AddK8sServiceDiscovery</c>.
/// </summary>
internal sealed class KubernetesServiceEndpointResolver(
    ServiceEndpointResolver resolver,
    IOptions<K8sServiceDiscoveryOptions> options,
    ILogger<KubernetesServiceEndpointResolver> logger) : IServiceEndpointResolver
{
    private readonly K8sServiceDiscoveryOptions _options = options.Value;

    private static readonly Action<ILogger, string, Exception?> _logSrvAttempt =
        LoggerMessage.Define<string>(
            LogLevel.Debug,
            new EventId(1, "SrvLookupAttempt"),
            "Attempting SRV DNS lookup for service '{ServiceName}'.");

    private static readonly Action<ILogger, string, Exception?> _logARecordAttempt =
        LoggerMessage.Define<string>(
            LogLevel.Debug,
            new EventId(2, "ARecordLookupAttempt"),
            "Attempting A-record DNS lookup for service '{ServiceName}'.");

    private static readonly Action<ILogger, string, Exception?> _logFallback =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(3, "DnsFallback"),
            "DNS resolution failed for service '{ServiceName}'. Returning K8s convention URI.");

    private static readonly Action<ILogger, string, Uri, Exception?> _logResolved =
        LoggerMessage.Define<string, Uri>(
            LogLevel.Debug,
            new EventId(4, "ServiceResolved"),
            "Service '{ServiceName}' resolved to '{Uri}'.");

    /// <inheritdoc />
    public async ValueTask<Uri> ResolveAsync(string serviceName, CancellationToken ct)
    {
        var scheme = _options.SchemeOverride ?? "http";
        var ns = _options.Namespace;
        var domain = _options.ClusterDomain;

        // Step 1 — SRV lookup via ServiceEndpointResolver
        // SRV name: _http._tcp.<service>.<namespace>.svc.<clusterDomain>
        var srvName = $"{scheme}://_http._tcp.{serviceName}.{ns}.svc.{domain}";
        try
        {
            _logSrvAttempt(logger, serviceName, null);
            var srvSource = await resolver.GetEndpointsAsync(srvName, ct).ConfigureAwait(false);
            var srvEndpoint = srvSource.Endpoints.FirstOrDefault();
            if (srvEndpoint is not null)
            {
                var uri = EndPointToUri(srvEndpoint.EndPoint, scheme);
                if (uri is not null)
                {
                    _logResolved(logger, serviceName, uri, null);
                    return uri;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // SRV lookup failed — fall through to A-record
            logger.LogDebug(ex, "SRV DNS lookup failed for service '{ServiceName}'.", serviceName);
        }

        // Step 2 — A-record lookup via ServiceEndpointResolver
        // A-record name: <service>.<namespace>.svc.<clusterDomain>
        var aRecordName = $"{scheme}://{serviceName}.{ns}.svc.{domain}";
        try
        {
            _logARecordAttempt(logger, serviceName, null);
            var aSource = await resolver.GetEndpointsAsync(aRecordName, ct).ConfigureAwait(false);
            var aEndpoint = aSource.Endpoints.FirstOrDefault();
            if (aEndpoint is not null)
            {
                var uri = EndPointToUri(aEndpoint.EndPoint, scheme);
                if (uri is not null)
                {
                    _logResolved(logger, serviceName, uri, null);
                    return uri;
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A-record lookup failed — fall through to convention URI
            logger.LogDebug(ex, "A-record DNS lookup failed for service '{ServiceName}'.", serviceName);
        }

        // Step 3 — K8s convention URI fallback (never throws)
        var fallback = BuildFallbackUri(serviceName, scheme, ns, domain);
        _logFallback(logger, serviceName, null);
        return fallback;
    }

    /// <summary>Converts a <see cref="EndPoint"/> to a <see cref="Uri"/> using the resolved endpoint data.</summary>
    private static Uri? EndPointToUri(EndPoint endPoint, string scheme)
    {
        return endPoint switch
        {
            UriEndPoint uriEp => uriEp.Uri,
            DnsEndPoint dnsEp => new Uri($"{scheme}://{dnsEp.Host}:{dnsEp.Port}"),
            IPEndPoint ipEp => new Uri($"{scheme}://{ipEp.Address}:{ipEp.Port}"),
            _ => null
        };
    }

    /// <summary>Builds the K8s DNS-convention URI. Never throws.</summary>
    private static Uri BuildFallbackUri(string serviceName, string scheme, string ns, string domain) =>
        new($"{scheme}://{serviceName}.{ns}.svc.{domain}");
}
