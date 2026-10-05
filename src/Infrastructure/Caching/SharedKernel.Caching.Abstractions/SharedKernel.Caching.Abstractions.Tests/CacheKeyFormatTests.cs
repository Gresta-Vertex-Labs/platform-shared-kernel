using SharedKernel.Execution.Tenancy;
using Xunit;

namespace SharedKernel.Caching.Abstractions.Tests;

public sealed class CacheKeyFormatTests
{
    private static readonly TenantId TenantA = new(Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"));
    private static readonly TenantId TenantB = new(Guid.Parse("7c9e6679-7425-40de-944b-e07fc1f90ae7"));

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
        Assert.Equal($"order-svc:@{TenantA}:invoice:42", CacheKeyFormat.BuildTenantKey("order-svc", TenantA, "invoice", "42"));

    [Fact]
    public void BuildTenantKey_DefaultTenant_Throws() =>
        Assert.Throws<ArgumentException>(() => CacheKeyFormat.BuildTenantKey("svc", default, "invoice", "42"));

    [Fact]
    public void BuildKey_InvalidServiceName_Throws() =>
        Assert.Throws<ArgumentException>(() => CacheKeyFormat.BuildKey("Order Svc", "invoice", "42"));

    [Fact]
    public void BuildKey_WhitespaceSegment_Throws() =>
        Assert.ThrowsAny<ArgumentException>(() => CacheKeyFormat.BuildKey("svc", "invoice", "42", " "));

    [Fact]
    public void GlobalKey_NeverEqualsTenantKeyWithSameParts()
    {
        string global = CacheKeyFormat.BuildKey("svc", TenantA.ToString(), "invoice", "42");
        string tenant = CacheKeyFormat.BuildTenantKey("svc", TenantA, "invoice", "42");

        Assert.NotEqual(global, tenant);
    }

    [Fact]
    public void GlobalKey_CannotForgeTenantMarker()
    {
        string forged = CacheKeyFormat.BuildKey("svc", "@" + TenantA, "invoice", "42");
        string tenant = CacheKeyFormat.BuildTenantKey("svc", TenantA, "invoice", "42");

        Assert.NotEqual(tenant, forged);
    }

    [Fact]
    public void Keys_SeparatorInsideParts_DoNotCollide()
    {
        Assert.NotEqual(
            CacheKeyFormat.BuildKey("svc", "a:b", "c"),
            CacheKeyFormat.BuildKey("svc", "a", "b:c"));
        Assert.NotEqual(
            CacheKeyFormat.BuildTenantKey("svc", TenantA, "a:b", "c"),
            CacheKeyFormat.BuildTenantKey("svc", TenantA, "a", "b:c"));
    }

    [Fact]
    public void TenantKeys_DifferentTenants_DoNotCollide() =>
        Assert.NotEqual(
            CacheKeyFormat.BuildTenantKey("svc", TenantA, "invoice", "42"),
            CacheKeyFormat.BuildTenantKey("svc", TenantB, "invoice", "42"));

    [Fact]
    public void TenantTags_SeparatorInsideTag_IsEscaped() =>
        Assert.Equal($"@{TenantA}:b%3Ax", CacheKeyFormat.BuildTenantTag(TenantA, "b:x"));

    [Fact]
    public void TenantTag_FormatsWithMarker()
    {
        Assert.Equal($"@{TenantA}:orders", CacheKeyFormat.BuildTenantTag(TenantA, "orders"));
        Assert.Equal($"@{TenantA}", CacheKeyFormat.BuildTenantWideTag(TenantA));
    }

    [Fact]
    public void TenantTag_DefaultTenant_Throws()
    {
        Assert.Throws<ArgumentException>(() => CacheKeyFormat.BuildTenantTag(default, "orders"));
        Assert.Throws<ArgumentException>(() => CacheKeyFormat.BuildTenantWideTag(default));
    }

    [Fact]
    public void TenantWideTag_NeverEqualsAnotherTenantsTag() =>
        Assert.NotEqual(
            CacheKeyFormat.BuildTenantWideTag(TenantB),
            CacheKeyFormat.BuildTenantTag(TenantA, "b"));
}
