using System.Collections.Concurrent;
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
///   <item>Cache hit (not expired) — returns cached <see cref="Uri"/> without DNS I/O.</item>
///   <item>DNS SRV lookup via <see cref="ServiceEndpointResolver"/> (<c>_http._tcp.{service}.{namespace}.svc.{clusterDomain}</c>)</item>
///   <item>A-record lookup via <see cref="ServiceEndpointResolver"/> (<c>{service}.{namespace}.svc.{clusterDomain}</c>)</item>
///   <item>K8s convention URI fallback (<c>{scheme}://{service}.{namespace}.svc.{clusterDomain}</c>) — never throws</item>
/// </list>
/// When <c>EndpointCacheTtlSeconds = 0</c> caching is bypassed entirely.
/// Stale-while-revalidate: on DNS failure with a stale cache entry, logs Warning and returns the stale <see cref="Uri"/>.
/// Registered as singleton via <c>AddK8sServiceDiscovery</c>.
/// </summary>
internal sealed class KubernetesServiceEndpointResolver(
    ServiceEndpointResolver resolver,
    IOptions<K8sServiceDiscoveryOptions> options,
    ILogger<KubernetesServiceEndpointResolver> logger) : IServiceEndpointResolver
{
    private readonly K8sServiceDiscoveryOptions _options = options.Value;

    /// <summary>Internal cache entry holding a resolved <see cref="Uri"/> and its expiry timestamp.</summary>
    private readonly record struct CachedEntry(Uri Uri, DateTimeOffset ExpiresAt);

    private readonly ConcurrentDictionary<string, CachedEntry> _cache =
        new(StringComparer.OrdinalIgnoreCase);

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

    private static readonly Action<ILogger, string, Uri, Exception?> _logStaleCache =
        LoggerMessage.Define<string, Uri>(
            LogLevel.Warning,
            new EventId(5, "StaleCacheUsed"),
            "DNS resolution failed for service '{ServiceName}'. Using stale cached URI '{Uri}'.");

    private static readonly Action<ILogger, string, Uri, Exception?> _logCacheHit =
        LoggerMessage.Define<string, Uri>(
            LogLevel.Debug,
            new EventId(6, "CacheHit"),
            "Cache hit for service '{ServiceName}', returning cached URI '{Uri}'.");

    /// <inheritdoc />
    public async ValueTask<Uri> ResolveAsync(string serviceName, CancellationToken ct)
    {
        var scheme = _options.SchemeOverride ?? "http";
        var ns = _options.Namespace;
        var domain = _options.ClusterDomain;
        var ttl = _options.EndpointCacheTtlSeconds;

        // Fast path — cache hit when TTL > 0 and entry has not expired.
        if (ttl > 0 && _cache.TryGetValue(serviceName, out var cached) && DateTimeOffset.UtcNow < cached.ExpiresAt)
        {
            _logCacheHit(logger, serviceName, cached.Uri, null);
            return cached.Uri;
        }

        // Attempt DNS resolution.
        var resolved = await TryResolveViaDnsAsync(serviceName, scheme, ns, domain, ct).ConfigureAwait(false);

        if (resolved is not null)
        {
            // DNS succeeded — update cache when TTL > 0.
            if (ttl > 0)
            {
                var expiry = DateTimeOffset.UtcNow.AddSeconds(ttl);
                _cache[serviceName] = new CachedEntry(resolved, expiry);
            }

            _logResolved(logger, serviceName, resolved, null);
            return resolved;
        }

        // DNS failed — try stale cache before falling back to convention URI.
        if (ttl > 0 && _cache.TryGetValue(serviceName, out var stale))
        {
            _logStaleCache(logger, serviceName, stale.Uri, null);
            return stale.Uri;
        }

        // Convention URI fallback — never throws.
        var fallback = BuildFallbackUri(serviceName, scheme, ns, domain);
        _logFallback(logger, serviceName, null);
        return fallback;
    }

    /// <summary>
    /// Attempts SRV then A-record lookup. Returns the resolved <see cref="Uri"/> on success,
    /// or <c>null</c> if both lookups fail. Never throws (except <see cref="OperationCanceledException"/>).
    /// </summary>
    private async Task<Uri?> TryResolveViaDnsAsync(
        string serviceName, string scheme, string ns, string domain, CancellationToken ct)
    {
        // Step 1 — SRV lookup
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
                    return uri;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "SRV DNS lookup failed for service '{ServiceName}'.", serviceName);
        }

        // Step 2 — A-record lookup
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
                    return uri;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "A-record DNS lookup failed for service '{ServiceName}'.", serviceName);
        }

        return null;
    }

    /// <summary>Converts a <see cref="EndPoint"/> to a <see cref="Uri"/> using the resolved endpoint data.</summary>
    private static Uri? EndPointToUri(EndPoint endPoint, string scheme) =>
        endPoint switch
        {
            UriEndPoint uriEp => uriEp.Uri,
            DnsEndPoint dnsEp => new Uri($"{scheme}://{dnsEp.Host}:{dnsEp.Port}"),
            IPEndPoint ipEp => new Uri($"{scheme}://{ipEp.Address}:{ipEp.Port}"),
            _ => null
        };

    /// <summary>Builds the K8s DNS-convention URI. Never throws.</summary>
    private static Uri BuildFallbackUri(string serviceName, string scheme, string ns, string domain) =>
        new($"{scheme}://{serviceName}.{ns}.svc.{domain}");
}
