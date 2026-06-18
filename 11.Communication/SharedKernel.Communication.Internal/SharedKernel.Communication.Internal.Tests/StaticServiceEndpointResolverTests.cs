using SharedKernel.Communication.Internal.Resolvers;

namespace SharedKernel.Communication.Internal.Tests;

public sealed class StaticServiceEndpointResolverTests
{
    private static StaticServiceEndpointResolver Build(Dictionary<string, Uri> map) =>
        new(map);

    [Fact]
    public async Task ResolveAsync_RegisteredService_ReturnsConfiguredUri()
    {
        var expected = new Uri("http://localhost:5001");
        var resolver = Build(new Dictionary<string, Uri>
        {
            ["order-service"] = expected
        });

        var result = await resolver.ResolveAsync("order-service", CancellationToken.None);

        result.Should().Be(expected);
    }

    [Fact]
    public async Task ResolveAsync_UnregisteredService_ReturnsK8sConventionUri()
    {
        var resolver = Build([]);

        var result = await resolver.ResolveAsync("payment-service", CancellationToken.None);

        result.Should().Be(new Uri("http://payment-service.default.svc.cluster.local"));
    }

    [Fact]
    public async Task ResolveAsync_UnregisteredService_IncludesServiceNameInUri()
    {
        var resolver = Build([]);

        var result = await resolver.ResolveAsync("inventory-svc", CancellationToken.None);

        result.Host.Should().Be("inventory-svc.default.svc.cluster.local");
        result.Scheme.Should().Be("http");
    }

    [Fact]
    public async Task ResolveAsync_NeverThrows_ForUnresolvableService()
    {
        var resolver = Build([]);

        Func<Task> act = () => resolver.ResolveAsync("unknown-service", CancellationToken.None).AsTask();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ResolveAsync_NeverThrows_EvenWithCancelledToken()
    {
        var resolver = Build([]);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // StaticServiceEndpointResolver is synchronous — cancellation doesn't cause a throw
        var result = await resolver.ResolveAsync("some-service", cts.Token);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task ResolveAsync_MultipleRegistered_EachReturnsCorrectUri()
    {
        var map = new Dictionary<string, Uri>
        {
            ["order-service"]   = new Uri("http://localhost:5001"),
            ["payment-service"] = new Uri("http://localhost:5002"),
            ["catalog-service"] = new Uri("https://catalog.internal:443"),
        };
        var resolver = Build(map);

        foreach (var (name, expected) in map)
        {
            var result = await resolver.ResolveAsync(name, CancellationToken.None);
            result.Should().Be(expected, because: $"service '{name}' should resolve correctly");
        }
    }
}
