using Xunit;

namespace SharedKernel.Caching.Abstractions.Tests;

public sealed class CacheKeyFormatTests
{
    [Theory]
    [InlineData("order-svc")]
    [InlineData("a")]
    [InlineData("svc.v2_x-1")]
    [InlineData("0api")]
    public void IsValidServiceName_ValidName_ReturnsTrue(string name) =>
        Assert.True(CacheKeyFormat.IsValidServiceName(name));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Order")]
    [InlineData("order svc")]
    [InlineData("order:svc")]
    [InlineData("-svc")]
    [InlineData("svc@x")]
    public void IsValidServiceName_InvalidName_ReturnsFalse(string? name) =>
        Assert.False(CacheKeyFormat.IsValidServiceName(name));

    [Fact]
    public void IsValidServiceName_LongerThan64_ReturnsFalse() =>
        Assert.False(CacheKeyFormat.IsValidServiceName(new string('a', 65)));

    [Theory]
    [InlineData("invoice", "invoice")]
    [InlineData("a:b", "a%3Ab")]
    [InlineData("@t", "%40t")]
    [InlineData("100%", "100%25")]
    [InlineData("%3A", "%253A")]
    public void Escape_EncodesReservedCharacters(string part, string expected) =>
        Assert.Equal(expected, CacheKeyFormat.Escape(part));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Escape_NullOrWhitespace_Throws(string? part) =>
        Assert.ThrowsAny<ArgumentException>(() => CacheKeyFormat.Escape(part!));

    [Fact]
    public void BuildKey_FormatsServiceEntityIdAndSegments()
    {
        Assert.Equal("order-svc:invoice:42", CacheKeyFormat.BuildKey("order-svc", "invoice", "42"));
        Assert.Equal("order-svc:invoice:42:en-GB", CacheKeyFormat.BuildKey("order-svc", "invoice", "42", "en-GB"));
    }

    [Fact]
    public void BuildTenantKey_FormatsWithTenantMarker() =>
        Assert.Equal("order-svc:@tenant-a:invoice:42", CacheKeyFormat.BuildTenantKey("order-svc", "tenant-a", "invoice", "42"));

    [Fact]
    public void BuildKey_InvalidServiceName_Throws() =>
        Assert.Throws<ArgumentException>(() => CacheKeyFormat.BuildKey("Order Svc", "invoice", "42"));

    [Fact]
    public void BuildKey_WhitespaceSegment_Throws() =>
        Assert.ThrowsAny<ArgumentException>(() => CacheKeyFormat.BuildKey("svc", "invoice", "42", " "));

    [Fact]
    public void GlobalKey_NeverEqualsTenantKeyWithSameParts()
    {
        string global = CacheKeyFormat.BuildKey("svc", "tenant-a", "invoice", "42");
        string tenant = CacheKeyFormat.BuildTenantKey("svc", "tenant-a", "invoice", "42");

        Assert.NotEqual(global, tenant);
    }

    [Fact]
    public void GlobalKey_CannotForgeTenantMarker()
    {
        string forged = CacheKeyFormat.BuildKey("svc", "@tenant-a", "invoice", "42");
        string tenant = CacheKeyFormat.BuildTenantKey("svc", "tenant-a", "invoice", "42");

        Assert.NotEqual(tenant, forged);
    }

    [Fact]
    public void Keys_SeparatorInsideParts_DoNotCollide()
    {
        Assert.NotEqual(
            CacheKeyFormat.BuildKey("svc", "a:b", "c"),
            CacheKeyFormat.BuildKey("svc", "a", "b:c"));
        Assert.NotEqual(
            CacheKeyFormat.BuildTenantKey("svc", "a:b", "c", "d"),
            CacheKeyFormat.BuildTenantKey("svc", "a", "b:c", "d"));
    }

    [Fact]
    public void TenantTags_SeparatorInsideParts_DoNotCollide() =>
        Assert.NotEqual(
            CacheKeyFormat.BuildTenantTag("a", "b:x"),
            CacheKeyFormat.BuildTenantTag("a:b", "x"));

    [Fact]
    public void TenantTag_FormatsWithMarker()
    {
        Assert.Equal("@tenant-a:orders", CacheKeyFormat.BuildTenantTag("tenant-a", "orders"));
        Assert.Equal("@tenant-a", CacheKeyFormat.BuildTenantWideTag("tenant-a"));
    }

    [Fact]
    public void TenantWideTag_NeverEqualsAnotherTenantsTag() =>
        Assert.NotEqual(
            CacheKeyFormat.BuildTenantWideTag("a:b"),
            CacheKeyFormat.BuildTenantTag("a", "b"));
}
