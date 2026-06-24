using SharedKernel.Testing.Caching;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Caching;

/// <summary>
/// Proves <see cref="FakeTenantCacheKeyProvider"/> against <c>ITenantCacheKeyProvider</c>'s
/// documented key-format contract — no existing `02.Caching` consuming-domain test exercises this
/// fake directly, so coverage is provided here per the documented SelfTests fallback.
/// </summary>
public sealed class FakeTenantCacheKeyProviderTests
{
    [Fact]
    public void BuildTenantKey_DefaultServiceName_ProducesExpectedFormat()
    {
        var provider = new FakeTenantCacheKeyProvider();

        var key = provider.BuildTenantKey("tenant-1", "order", "42");

        Assert.Equal("test-svc:tenant-1:order:42", key);
    }

    [Fact]
    public void BuildTenantKey_WithExtraSegments_AppendsThem()
    {
        var provider = new FakeTenantCacheKeyProvider();

        var key = provider.BuildTenantKey("tenant-1", "order", "42", "v2", "summary");

        Assert.Equal("test-svc:tenant-1:order:42:v2:summary", key);
    }

    [Fact]
    public void BuildKey_StandardFormat_NoVersionSegment()
    {
        var provider = new FakeTenantCacheKeyProvider("svc");

        var key = provider.BuildKey("order", "42");

        Assert.Equal("svc:order:42", key);
    }

    [Fact]
    public void BuildKey_WithVersionGreaterThanZero_AppendsVersionSegment()
    {
        var provider = new FakeTenantCacheKeyProvider("svc");

        var key = provider.BuildKey("order", "42", version: 3);

        Assert.Equal("svc:order:42:v3", key);
    }

    [Fact]
    public void BuildKey_WithVersionZero_NoSuffixChange()
    {
        var provider = new FakeTenantCacheKeyProvider("svc");

        var key = provider.BuildKey("order", "42", version: 0);

        Assert.Equal("svc:order:42", key);
    }

    [Fact]
    public void Constructor_NullOrWhitespaceServiceName_Throws() =>
        Assert.Throws<ArgumentException>(() => new FakeTenantCacheKeyProvider(" "));

    [Fact]
    public void BuildTenantKey_NullOrWhitespaceTenantId_Throws()
    {
        var provider = new FakeTenantCacheKeyProvider();
        Assert.Throws<ArgumentException>(() => provider.BuildTenantKey(" ", "order", "42"));
    }
}
