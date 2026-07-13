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
internal sealed partial class KubernetesServiceEndpointResolver(
    ServiceEndpointResolver resolver,
    IOptions<K8sServiceDiscoveryOptions> options,
    ILogger<KubernetesServiceEndpointResolver> logger) : IServiceEndpointResolver
{
    private readonly K8sServiceDiscoveryOptions _options = options.Value;

    /// <summary>Internal cache entry holding a resolved <see cref="Uri"/> and its expiry timestamp.</summary>
    private readonly record struct CachedEntry(Uri Uri, DateTimeOffset ExpiresAt);

    private readonly ConcurrentDictionary<string, CachedEntry> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    [LoggerMessage(
        EventId = 11300,
        Level = LogLevel.Debug,
        Message = "Attempting SRV DNS lookup for service '{ServiceName}'.")]
    private static partial void LogSrvLookupAttempt(ILogger logger, string serviceName);

    [LoggerMessage(
        EventId = 11301,
        Level = LogLevel.Debug,
        Message = "Attempting A-record DNS lookup for service '{ServiceName}'.")]
    private static partial void LogARecordLookupAttempt(ILogger logger, string serviceName);

    [LoggerMessage(
        EventId = 11302,
        Level = LogLevel.Warning,
        Message = "DNS resolution failed for service '{ServiceName}'. Returning K8s convention URI.")]
    private static partial void LogDnsFallback(ILogger logger, string serviceName);

    [LoggerMessage(
        EventId = 11303,
        Level = LogLevel.Debug,
        Message = "Service '{ServiceName}' resolved to '{Uri}'.")]
    private static partial void LogServiceResolved(ILogger logger, string serviceName, Uri uri);

    [LoggerMessage(
        EventId = 11304,
        Level = LogLevel.Warning,
        Message = "DNS resolution failed for service '{ServiceName}'. Using stale cached URI '{Uri}'.")]
    private static partial void LogStaleCacheUsed(ILogger logger, string serviceName, Uri uri);

    [LoggerMessage(
        EventId = 11305,
        Level = LogLevel.Debug,
        Message = "Cache hit for service '{ServiceName}', returning cached URI '{Uri}'.")]
    private static partial void LogCacheHit(ILogger logger, string serviceName, Uri uri);

    [LoggerMessage(
        EventId = 11306,
        Level = LogLevel.Debug,
        Message = "SRV DNS lookup failed for service '{ServiceName}'.")]
    private static partial void LogSrvLookupFailed(ILogger logger, string serviceName, Exception exception);

    [LoggerMessage(
        EventId = 11307,
        Level = LogLevel.Debug,
        Message = "A-record DNS lookup failed for service '{ServiceName}'.")]
    private static partial void LogARecordLookupFailed(ILogger logger, string serviceName, Exception exception);

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
            LogCacheHit(logger, serviceName, cached.Uri);
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

            LogServiceResolved(logger, serviceName, resolved);
            return resolved;
        }

        // DNS failed — try stale cache before falling back to convention URI.
        if (ttl > 0 && _cache.TryGetValue(serviceName, out var stale))
        {
            LogStaleCacheUsed(logger, serviceName, stale.Uri);
            return stale.Uri;
        }

        // Convention URI fallback — never throws.
        var fallback = BuildFallbackUri(serviceName, scheme, ns, domain);
        LogDnsFallback(logger, serviceName);
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
            LogSrvLookupAttempt(logger, serviceName);
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
            LogSrvLookupFailed(logger, serviceName, ex);
        }

        // Step 2 — A-record lookup
        var aRecordName = $"{scheme}://{serviceName}.{ns}.svc.{domain}";
        try
        {
            LogARecordLookupAttempt(logger, serviceName);
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
            LogARecordLookupFailed(logger, serviceName, ex);
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
