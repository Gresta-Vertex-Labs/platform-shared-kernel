using Microsoft.AspNetCore.Http;
using SharedKernel.Execution.Tenancy;
using SharedKernel.MultiTenancy.Resolution;
using SharedKernel.Testing.ServiceDefaults;
using Xunit;

namespace SharedKernel.Testing.SelfTests.ServiceDefaults;

public sealed class FakeTenantResolutionStrategyTests
{
    [Fact]
    public async Task TryResolveAsync_FixedResultConstructor_AlwaysReturnsSameValue()
    {
        var tenantId = new TenantId(Guid.NewGuid());
        var strategy = new FakeTenantResolutionStrategy(tenantId);

        var resolved = await strategy.TryResolveAsync(new DefaultHttpContext(), CancellationToken.None);

        Assert.Equal(tenantId, resolved);
    }

    [Fact]
    public async Task TryResolveAsync_DelegateConstructor_InvokesDelegate()
    {
        var expected = new TenantId(Guid.NewGuid());
        var strategy = new FakeTenantResolutionStrategy((_, _) => Task.FromResult<TenantId?>(expected));

        var resolved = await strategy.TryResolveAsync(new DefaultHttpContext(), CancellationToken.None);

        Assert.Equal(expected, resolved);
    }

    [Fact]
    public async Task TryResolveAsync_DefaultConstructor_ReturnsNull()
    {
        var strategy = new FakeTenantResolutionStrategy();

        var resolved = await strategy.TryResolveAsync(new DefaultHttpContext(), CancellationToken.None);

        Assert.Null(resolved);
    }

    [Fact]
    public async Task ImplementsITenantResolutionStrategy()
    {
        var tenantId = new TenantId(Guid.NewGuid());
        ITenantResolutionStrategy strategy = new FakeTenantResolutionStrategy(tenantId) { StrategyName = "Custom" };

        Assert.Equal("Custom", strategy.StrategyName);
        Assert.Equal(tenantId, await strategy.TryResolveAsync(new DefaultHttpContext(), CancellationToken.None));
    }

    [Fact]
    public void StrategyName_DefaultsTo_Fake()
    {
        var strategy = new FakeTenantResolutionStrategy();
        Assert.Equal("Fake", strategy.StrategyName);
    }

    [Fact]
    public void StrategyName_IsSettable()
    {
        var strategy = new FakeTenantResolutionStrategy { StrategyName = "Custom" };
        Assert.Equal("Custom", strategy.StrategyName);
    }

    [Fact]
    public void Constructor_NullResolverDelegate_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new FakeTenantResolutionStrategy((Func<HttpContext, CancellationToken, Task<TenantId?>>)null!));
}
