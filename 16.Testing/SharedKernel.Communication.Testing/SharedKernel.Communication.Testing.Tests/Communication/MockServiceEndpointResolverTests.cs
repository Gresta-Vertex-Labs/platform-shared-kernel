using SharedKernel.Testing.Communication;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Communication;

public sealed class MockServiceEndpointResolverTests
{
    [Fact]
    public async Task ResolveAsync_ConfiguredService_ReturnsConfiguredUri()
    {
        var resolver = new MockServiceEndpointResolver();
        var uri = new Uri("http://orders-service.internal");
        resolver.Configure("orders", uri);

        var resolved = await resolver.ResolveAsync("orders", CancellationToken.None);

        Assert.Equal(uri, resolved);
    }

    [Fact]
    public async Task ResolveAsync_UnconfiguredService_NeverThrows_ReturnsDeterministicFallback()
    {
        var resolver = new MockServiceEndpointResolver();

        var resolved = await resolver.ResolveAsync("unknown-service", CancellationToken.None);

        Assert.Contains("unknown-service", resolved.Host);
    }

    [Fact]
    public async Task GetResolvedNames_RecordsEveryResolveCall_InOrder()
    {
        var resolver = new MockServiceEndpointResolver();

        await resolver.ResolveAsync("a", CancellationToken.None);
        await resolver.ResolveAsync("b", CancellationToken.None);

        Assert.Equal(["a", "b"], resolver.GetResolvedNames());
    }

    [Fact]
    public void Configure_NullUri_Throws()
    {
        var resolver = new MockServiceEndpointResolver();
        Assert.Throws<ArgumentNullException>(() => resolver.Configure("svc", null!));
    }
}
