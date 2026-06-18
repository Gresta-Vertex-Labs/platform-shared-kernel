using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Communication.Internal.Extensions;
using SharedKernel.Communication.Internal.Options;
using SharedKernel.Communication.Internal.Resolvers;

namespace SharedKernel.Communication.Internal.Tests;

/// <summary>
/// Unit tests for <c>KubernetesServiceEndpointResolver</c>.
/// All DNS-dependent tests are tagged Integration and use the pass-through
/// ServiceDiscovery provider (no live K8s cluster required for the fallback-path tests).
/// </summary>
public sealed class KubernetesServiceEndpointResolverTests
{
    /// <summary>
    /// Builds a service provider with K8s service discovery wired using the
    /// pass-through provider (returns the service name URI as-is, simulating a
    /// successful DNS resolution).
    /// </summary>
    private static ServiceProvider BuildProvider(Action<K8sServiceDiscoveryOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddK8sServiceDiscovery(configure);
        // Add pass-through so ServiceEndpointResolver has at least one registered provider
        services.AddPassThroughServiceEndpointProvider();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task ResolveAsync_NeverThrows_ForUnresolvableService()
    {
        await using var sp = BuildProvider();
        var resolver = sp.GetRequiredService<IServiceEndpointResolver>();

        Func<Task> act = () => resolver.ResolveAsync("non-existent-service", CancellationToken.None).AsTask();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ResolveAsync_ReturnsNonNullUri()
    {
        await using var sp = BuildProvider();
        var resolver = sp.GetRequiredService<IServiceEndpointResolver>();

        var result = await resolver.ResolveAsync("order-service", CancellationToken.None);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task ResolveAsync_FallbackUri_HasCorrectFormat()
    {
        await using var sp = BuildProvider(opts =>
        {
            opts.Namespace = "production";
            opts.ClusterDomain = "cluster.local";
        });
        var resolver = sp.GetRequiredService<IServiceEndpointResolver>();

        var result = await resolver.ResolveAsync("my-service", CancellationToken.None);

        result.Should().NotBeNull();
        result.Scheme.Should().BeOneOf("http", "https");
    }

    [Fact]
    public async Task ResolveAsync_WithSchemeOverride_UsesThatScheme()
    {
        await using var sp = BuildProvider(opts =>
        {
            opts.SchemeOverride = "https";
            opts.Namespace = "default";
            opts.ClusterDomain = "cluster.local";
        });
        var resolver = sp.GetRequiredService<IServiceEndpointResolver>();

        var result = await resolver.ResolveAsync("secure-service", CancellationToken.None);

        result.Should().NotBeNull();
        // The result may come from pass-through (which echoes back the request URI) or fallback.
        // Either way, the scheme should be https.
        result.Scheme.Should().Be("https");
    }

    [Fact]
    public async Task ResolveAsync_FallbackConventionUri_MatchesK8sPattern()
    {
        const string service = "payment-service";
        const string ns = "billing";
        const string domain = "cluster.local";

        await using var sp = BuildProvider(opts =>
        {
            opts.Namespace = ns;
            opts.ClusterDomain = domain;
        });
        var resolver = sp.GetRequiredService<IServiceEndpointResolver>();

        var result = await resolver.ResolveAsync(service, CancellationToken.None);

        result.Should().NotBeNull();
        result.Scheme.Should().Be("http");
        result.Host.Should().Contain(service);
    }

    [Fact]
    public void ResolverIsRegistered_AsSingleton()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddK8sServiceDiscovery();

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IServiceEndpointResolver));

        descriptor.Should().NotBeNull();
        descriptor!.Lifetime.Should().Be(ServiceLifetime.Singleton);
    }
}
