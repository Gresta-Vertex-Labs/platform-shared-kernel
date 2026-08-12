using System.Collections;
using System.Net;
using System.Reflection;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.ServiceDiscovery;
using SharedKernel.Communication.Internal.Options;
using SharedKernel.Communication.Internal.Resolvers;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Testing.Clocks;

namespace SharedKernel.Communication.Internal.Tests;

/// <summary>
/// T-33 (P-357/WO-056): deterministic <see cref="FakeClock"/>-driven tests for
/// <c>KubernetesServiceEndpointResolver</c>'s TTL cache. Every branch (cache-hit, cache-expired,
/// stale-while-revalidate) is driven by advancing <see cref="FakeClock"/> — never a real
/// wall-clock <c>Sleep</c>/<c>Delay</c>.
/// </summary>
public sealed class FakeClockTtlCacheTests
{
    // KubernetesServiceEndpointResolver's TTL cache is a private field/nested type — reflection is
    // the only way to seed a pre-existing (and, for the stale-while-revalidate scenario,
    // deliberately already-expired-relative-to-a-later-Advance) cache entry directly, without first
    // requiring a real successful DNS resolution through Microsoft.Extensions.ServiceDiscovery's own
    // ServiceEndpointResolver — which, once a query string resolves successfully, never re-queries
    // its IServiceEndpointProviderFactory for the same query string again, making a "succeed once,
    // then fail" scenario impossible to drive through the real resolver. This is the same class of
    // test-only reflection already sanctioned elsewhere on the platform (16.Testing's own
    // Domain/SpecificationAssert and Containers/MilvusContainerFixtureTests precedents) — never
    // acceptable in production code.
    private static readonly FieldInfo CacheField = typeof(KubernetesServiceEndpointResolver)
        .GetField("_cache", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static readonly Type CachedEntryType = typeof(KubernetesServiceEndpointResolver)
        .GetNestedType("CachedEntry", BindingFlags.NonPublic)!;

    private static KubernetesServiceEndpointResolver CreateResolver(
        IClock clock,
        IServiceEndpointProviderFactory providerFactory,
        int ttlSeconds = 60,
        TestLogSink? sink = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            if (sink is not null)
            {
                builder.AddProvider(new TestLoggerProvider(sink));
            }
        });
        services.AddServiceDiscoveryCore();
        services.AddSingleton<IServiceEndpointProviderFactory>(providerFactory);

        // Not disposed here: ServiceEndpointResolver only implements IAsyncDisposable, and this
        // synchronous helper is called from every test's Arrange step — the resolved dnsResolver
        // and logger must outlive this method for the full duration of the test's Act/Assert.
        var sp = services.BuildServiceProvider();
        var dnsResolver = sp.GetRequiredService<ServiceEndpointResolver>();
        var logger = sp.GetRequiredService<ILogger<KubernetesServiceEndpointResolver>>();
        // Fully qualified: the "Options" namespace segment in SharedKernel.Communication.Internal.Options
        // shadows the Microsoft.Extensions.Options.Options static class for a bare "Options.Create(...)" call.
        var options = Microsoft.Extensions.Options.Options.Create(
            new K8sServiceDiscoveryOptions { EndpointCacheTtlSeconds = ttlSeconds });

        return new KubernetesServiceEndpointResolver(dnsResolver, options, logger, clock);
    }

    private static void SeedCache(
        KubernetesServiceEndpointResolver resolver,
        string serviceName,
        Uri uri,
        DateTimeOffset expiresAt)
    {
        var cache = (IDictionary)CacheField.GetValue(resolver)!;
        var entry = Activator.CreateInstance(CachedEntryType, uri, expiresAt)!;
        cache[serviceName] = entry;
    }

    [Fact]
    public async Task ResolveAsync_CacheHitBeforeExpiry_ReturnsCachedUriWithoutFreshDnsLookup()
    {
        // Arrange — the underlying DNS provider always fails, proving a cache hit never reaches it.
        var clock = new FakeClock();
        var factory = new AlwaysFailingProviderFactory();
        var resolver = CreateResolver(clock, factory);
        var cachedUri = new Uri("http://cached-endpoint:1234");
        SeedCache(resolver, "svc-cache-hit", cachedUri, clock.UtcNow.AddSeconds(60));

        // Act — advance the clock, but stay inside the TTL window.
        clock.Advance(TimeSpan.FromSeconds(30));
        var result = await resolver.ResolveAsync("svc-cache-hit", CancellationToken.None);

        // Assert
        result.Should().Be(cachedUri);
        factory.CreateCount.Should().Be(0,
            "a cache hit must short-circuit before any DNS lookup is attempted");
    }

    [Fact]
    public async Task ResolveAsync_CacheExpired_TriggersFreshDnsLookup()
    {
        // Arrange
        var clock = new FakeClock();
        var freshEndPoint = new DnsEndPoint("fresh-endpoint", 80);
        var factory = new SucceedingProviderFactory(freshEndPoint);
        var resolver = CreateResolver(clock, factory);
        SeedCache(resolver, "svc-cache-expired", new Uri("http://stale-endpoint:9999"), clock.UtcNow.AddSeconds(10));

        // Act — advance the clock past the seeded entry's expiry.
        clock.Advance(TimeSpan.FromSeconds(11));
        var result = await resolver.ResolveAsync("svc-cache-expired", CancellationToken.None);

        // Assert — the fresh DNS answer is returned, not the stale cached value.
        result.Should().Be(new Uri("http://fresh-endpoint:80"));
        factory.CreateCount.Should().BeGreaterThan(0,
            "an expired cache entry must trigger a fresh DNS lookup rather than returning the stale value");
    }

    [Fact]
    public async Task ResolveAsync_CacheExpiredAndDnsFails_ReturnsStaleUriAndLogsWarning()
    {
        // Arrange
        var clock = new FakeClock();
        var sink = new TestLogSink();
        var factory = new AlwaysFailingProviderFactory();
        var resolver = CreateResolver(clock, factory, sink: sink);
        var staleUri = new Uri("http://stale-endpoint:9999");
        SeedCache(resolver, "svc-stale-revalidate", staleUri, clock.UtcNow.AddSeconds(10));

        // Act — advance the clock past expiry; the underlying DNS provider always fails.
        clock.Advance(TimeSpan.FromSeconds(11));
        var result = await resolver.ResolveAsync("svc-stale-revalidate", CancellationToken.None);

        // Assert — stale-while-revalidate: return the stale Uri, never throw, log Warning (11304).
        result.Should().Be(staleUri,
            "a DNS failure with an existing stale entry must fall back to the stale Uri, never throw");
        sink.Entries.Should().Contain(
            e => e.LogLevel == LogLevel.Warning && e.EventId.Id == 11304,
            "the stale-while-revalidate path must log LogStaleCacheUsed (EventId 11304) at Warning");
    }
}

/// <summary>Test double whose <see cref="IServiceEndpointProvider"/> always fails DNS resolution.</summary>
internal sealed class AlwaysFailingProviderFactory : IServiceEndpointProviderFactory
{
    private int _createCount;

    public int CreateCount => _createCount;

    public bool TryCreateProvider(ServiceEndpointQuery query, out IServiceEndpointProvider provider)
    {
        Interlocked.Increment(ref _createCount);
        provider = new AlwaysFailingProvider();
        return true;
    }
}

internal sealed class AlwaysFailingProvider : IServiceEndpointProvider
{
    public ValueTask PopulateAsync(IServiceEndpointBuilder endpoints, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Simulated DNS failure (test double).");

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>Test double whose <see cref="IServiceEndpointProvider"/> always resolves to a fixed endpoint.</summary>
internal sealed class SucceedingProviderFactory(EndPoint endPoint) : IServiceEndpointProviderFactory
{
    private int _createCount;

    public int CreateCount => _createCount;

    public bool TryCreateProvider(ServiceEndpointQuery query, out IServiceEndpointProvider provider)
    {
        Interlocked.Increment(ref _createCount);
        provider = new SucceedingProvider(endPoint);
        return true;
    }
}

internal sealed class SucceedingProvider(EndPoint endPoint) : IServiceEndpointProvider
{
    public ValueTask PopulateAsync(IServiceEndpointBuilder endpoints, CancellationToken cancellationToken)
    {
        endpoints.Endpoints.Add(ServiceEndpoint.Create(endPoint));
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
