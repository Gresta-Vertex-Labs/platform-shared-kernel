using Microsoft.Extensions.Options;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.FusionCache.Extensions;
using SharedKernel.Caching.FusionCache.Implementations;
using Xunit;

namespace SharedKernel.Caching.FusionCache.Tests.Policies;

/// <summary>
/// Tests for <see cref="CachePolicy.KeyVersion"/>, <see cref="CachePolicy.WithVersion"/>,
/// and the versioned <see cref="ICacheKeyProvider.BuildKey(string,string,int,string[])"/> overload.
/// </summary>
public sealed class KeyVersioningTests
{
    // -------------------------------------------------------------------------
    // CachePolicy.KeyVersion — default and property correctness (KV-01)
    // -------------------------------------------------------------------------

    [Fact]
    public void Default_HasZeroKeyVersion()
    {
        Assert.Equal(0, CachePolicy.Default.KeyVersion);
    }

    [Fact]
    public void NeverExpire_HasZeroKeyVersion()
    {
        Assert.Equal(0, CachePolicy.NeverExpire.KeyVersion);
    }

    [Fact]
    public void For_ReturnsPolicy_WithZeroKeyVersion()
    {
        var policy = CachePolicy.For(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5));
        Assert.Equal(0, policy.KeyVersion);
    }

    [Fact]
    public void Sliding_ReturnsPolicy_WithZeroKeyVersion()
    {
        var policy = CachePolicy.Sliding(TimeSpan.FromSeconds(30));
        Assert.Equal(0, policy.KeyVersion);
    }

    // -------------------------------------------------------------------------
    // CachePolicy.WithVersion() fluent method (KV-02)
    // -------------------------------------------------------------------------

    [Fact]
    public void WithVersion_ReturnsNewPolicyWithSpecifiedVersion()
    {
        var policy = CachePolicy.Default.WithVersion(3);

        Assert.Equal(3, policy.KeyVersion);
    }

    [Fact]
    public void WithVersion_Zero_ReturnsZeroVersion()
    {
        var policy = CachePolicy.Default.WithVersion(3).WithVersion(0);

        Assert.Equal(0, policy.KeyVersion);
    }

    [Fact]
    public void WithVersion_DoesNotMutateOriginalPolicy()
    {
        var original = CachePolicy.Default;
        _ = original.WithVersion(5);

        Assert.Equal(0, original.KeyVersion);
    }

    [Fact]
    public void WithVersion_NegativeVersion_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CachePolicy.Default.WithVersion(-1));
    }

    // -------------------------------------------------------------------------
    // Fluent chaining — WithVersion + WithTags (KV-02)
    // -------------------------------------------------------------------------

    [Fact]
    public void WithVersion_WithTagsChaining_PreservesVersionAndTags()
    {
        var policy = CachePolicy.Default.WithVersion(3).WithTags("entity:invoice");

        Assert.Equal(3, policy.KeyVersion);
        Assert.Equal(["entity:invoice"], policy.Tags);
    }

    [Fact]
    public void WithTags_WithVersionChaining_PreservesTagsAndVersion()
    {
        var policy = CachePolicy.Default.WithTags("entity:invoice").WithVersion(3);

        Assert.Equal(3, policy.KeyVersion);
        Assert.Equal(["entity:invoice"], policy.Tags);
    }

    [Fact]
    public void WithVersion_WithEagerRefreshChaining_PreservesVersionAndThreshold()
    {
        var policy = CachePolicy.Default.WithVersion(2).WithEagerRefresh(0.8);

        Assert.Equal(2, policy.KeyVersion);
        Assert.Equal(0.8, policy.EagerRefreshThreshold);
    }

    [Fact]
    public void Sliding_WithVersionChaining_PreservesSlidingWindowAndVersion()
    {
        var window = TimeSpan.FromSeconds(45);
        var policy = CachePolicy.Sliding(window).WithVersion(7);

        Assert.Equal(window, policy.SlidingWindow);
        Assert.Equal(7, policy.KeyVersion);
    }

    // -------------------------------------------------------------------------
    // CacheKeyProvider.BuildKey — version 0 produces no suffix (KV-07)
    // -------------------------------------------------------------------------

    [Fact]
    public void BuildKey_WithVersionOverload_VersionZero_ProducesNoSuffix()
    {
        var provider = CreateProvider("order-svc");

        var keyVersioned = provider.BuildKey("invoice", "42", 0);
        var keyOriginal = provider.BuildKey("invoice", "42");

        Assert.Equal(keyOriginal, keyVersioned);
        Assert.Equal("order-svc:invoice:42", keyVersioned);
    }

    [Fact]
    public void BuildKey_WithVersionOverload_VersionZero_WithExtraSegments_ProducesNoSuffix()
    {
        var provider = CreateProvider("order-svc");

        var keyVersioned = provider.BuildKey("invoice", "42", 0, "en-GB");
        var keyOriginal = provider.BuildKey("invoice", "42", "en-GB");

        Assert.Equal(keyOriginal, keyVersioned);
        Assert.Equal("order-svc:invoice:42:en-GB", keyVersioned);
    }

    // -------------------------------------------------------------------------
    // CacheKeyProvider.BuildKey — version > 0 produces :v{n} suffix (KV-07)
    // -------------------------------------------------------------------------

    [Fact]
    public void BuildKey_WithVersionOverload_VersionThree_ProducesV3Suffix()
    {
        var provider = CreateProvider("order-svc");

        var key = provider.BuildKey("invoice", "42", 3);

        Assert.Equal("order-svc:invoice:42:v3", key);
    }

    [Fact]
    public void BuildKey_WithVersionOverload_VersionOne_ProducesV1Suffix()
    {
        var provider = CreateProvider("svc");

        var key = provider.BuildKey("product", "sku-99", 1);

        Assert.Equal("svc:product:sku-99:v1", key);
    }

    [Fact]
    public void BuildKey_WithVersionOverload_WithExtraSegments_AppendsSuffixAfterExtra()
    {
        var provider = CreateProvider("order-svc");

        var key = provider.BuildKey("invoice", "42", 3, "en-GB", "tenant-99");

        Assert.Equal("order-svc:invoice:42:en-GB:tenant-99:v3", key);
    }

    [Fact]
    public void BuildKey_WithVersionOverload_WithSingleExtraSegment_AppendsSuffixLast()
    {
        var provider = CreateProvider("catalog");

        var key = provider.BuildKey("product", "sku-1", 5, "preview");

        Assert.Equal("catalog:product:sku-1:preview:v5", key);
    }

    [Fact]
    public void BuildKey_WithVersionOverload_ServiceNameIsFirstSegment()
    {
        var provider = CreateProvider("my-service");

        var key = provider.BuildKey("entity", "id-001", 2);

        Assert.StartsWith("my-service:", key, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildKey_WithVersionOverload_DoesNotNormaliseCase()
    {
        var provider = CreateProvider("MyService");

        var key = provider.BuildKey("Entity", "ID-001", 3);

        Assert.Equal("MyService:Entity:ID-001:v3", key);
    }

    // -------------------------------------------------------------------------
    // CacheKeyProvider.BuildKey — argument validation on versioned overload
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildKey_VersionedOverload_NullOrWhitespaceEntity_ThrowsArgumentException(string entity)
    {
        var provider = CreateProvider("svc");

        Assert.Throws<ArgumentException>(() => provider.BuildKey(entity, "123", 1));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildKey_VersionedOverload_NullOrWhitespaceId_ThrowsArgumentException(string id)
    {
        var provider = CreateProvider("svc");

        Assert.Throws<ArgumentException>(() => provider.BuildKey("entity", id, 1));
    }

    [Fact]
    public void BuildKey_VersionedOverload_NegativeVersion_ThrowsArgumentOutOfRangeException()
    {
        var provider = CreateProvider("svc");

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            provider.BuildKey("entity", "id", -1));
    }

    // -------------------------------------------------------------------------
    // Integration: CachePolicy.WithVersion → BuildKey round-trip
    // -------------------------------------------------------------------------

    [Fact]
    public void WithVersion_BuildKey_RoundTrip_ProducesExpectedKey()
    {
        var policy = CachePolicy.Default.WithVersion(3).WithTags("x");
        var provider = CreateProvider("order-svc");

        var key = provider.BuildKey("invoice", "42", policy.KeyVersion);

        Assert.Equal("order-svc:invoice:42:v3", key);
        Assert.Equal(["x"], policy.Tags);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static ICacheKeyProvider CreateProvider(string serviceName)
    {
        var options = Options.Create(new CachingOptions { ServiceName = serviceName });
        return new CacheKeyProvider(options);
    }
}
