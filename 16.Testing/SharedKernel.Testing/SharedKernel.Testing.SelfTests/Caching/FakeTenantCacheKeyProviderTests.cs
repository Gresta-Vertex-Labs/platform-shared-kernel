using SharedKernel.Caching.Abstractions;
using SharedKernel.Testing.Caching;
using Xunit;

using SharedKernel.Execution.Tenancy;

namespace SharedKernel.Testing.SelfTests.Caching;

/// <summary>
/// Proves <see cref="FakeTenantCacheKeyProvider"/> produces exactly the keys <see cref="CacheKeyFormat"/>
/// produces, so tests assert against production key shapes.
/// </summary>
public sealed class FakeTenantCacheKeyProviderTests
{
    private static readonly TenantId TenantA = new(Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"));

    [Fact]
    public void DefaultConstructor_UsesTestServiceName()
    {
        var provider = new FakeTenantCacheKeyProvider();

        Assert.Equal("test-svc", provider.ServiceName);
        Assert.Equal(FakeTenantCacheKeyProvider.DefaultServiceName, provider.ServiceName);
    }

    [Fact]
    public void BuildTenantKey_DefaultServiceName_MatchesCacheKeyFormat()
    {
        var provider = new FakeTenantCacheKeyProvider();

        var key = provider.BuildTenantKey(TenantA, "order", "42");

        Assert.Equal($"test-svc:@{TenantA}:order:42", key);
        Assert.Equal(CacheKeyFormat.BuildTenantKey("test-svc", TenantA, "order", "42"), key);
    }

    [Fact]
    public void BuildTenantKey_WithExtraSegments_AppendsThem()
    {
        var provider = new FakeTenantCacheKeyProvider();

        var key = provider.BuildTenantKey(TenantA, "order", "42", "v2", "summary");

        Assert.Equal($"test-svc:@{TenantA}:order:42:v2:summary", key);
    }

    [Fact]
    public void BuildKey_MatchesCacheKeyFormat()
    {
        var provider = new FakeTenantCacheKeyProvider("svc");

        var key = provider.BuildKey("order", "42", "summary");

        Assert.Equal("svc:order:42:summary", key);
        Assert.Equal(CacheKeyFormat.BuildKey("svc", "order", "42", "summary"), key);
    }

    [Fact]
    public void BuildKey_EscapesReservedCharacters()
    {
        var provider = new FakeTenantCacheKeyProvider();

        Assert.Equal("test-svc:order:a%3Ab%40c", provider.BuildKey("order", "a:b@c"));
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("")]
    [InlineData("Upper")]
    [InlineData("has space")]
    [InlineData("-leading-dash")]
    [InlineData("bad:colon")]
    public void Constructor_InvalidServiceName_Throws(string serviceName) =>
        Assert.Throws<ArgumentException>(() => new FakeTenantCacheKeyProvider(serviceName));

    [Fact]
    public void BuildTenantKey_DefaultTenantId_Throws()
    {
        var provider = new FakeTenantCacheKeyProvider();
        Assert.Throws<ArgumentException>(() => provider.BuildTenantKey(default, "order", "42"));
    }
}
