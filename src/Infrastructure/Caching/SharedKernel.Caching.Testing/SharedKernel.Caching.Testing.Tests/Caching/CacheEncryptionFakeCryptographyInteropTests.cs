using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.Caching.Abstractions;
using SharedKernel.Caching.FusionCache.Extensions;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Testing.Cryptography;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Caching;

/// <summary>
/// Proves D-211's finding end to end: <c>Cryptography/FakeSymmetricEncryptionService</c>
/// (already shipped, <c>16.Testing</c>) genuinely satisfies <c>AddCacheEncryption()</c>'s
/// (<c>src/Infrastructure/Caching/SharedKernel.Caching.FusionCache</c>, Phase 42/P-433) sole prerequisite — a
/// registered <see cref="ISymmetricEncryptionService"/> — with zero real AES-GCM key material,
/// and that a value round-trips correctly through the resulting encrypted-at-rest cache pipeline.
/// </summary>
/// <remarks>
/// The two fakes compose with no direct coupling between them: <c>Caching/FakeTenantCacheService</c>
/// takes no dependency on <c>Cryptography/</c>, and <c>Cryptography/FakeSymmetricEncryptionService</c>
/// takes no dependency on <c>Caching/</c> — this test, like a consuming service, composes both
/// independently against the real production <c>SharedKernel.Caching.FusionCache</c> package.
/// </remarks>
public sealed class CacheEncryptionFakeCryptographyInteropTests
{
    [Fact]
    public async Task AddCacheEncryption_ComposedWithFakeCryptography_RoundTripsValueThroughEncryptedPipeline()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // Satisfies AddCacheEncryption()'s sole prerequisite — a registered
        // ISymmetricEncryptionService, regardless of which implementation — with zero real
        // AES-GCM key material.
        services.AddFakeCryptography();

        services.AddSharedKernelCaching(o => o.ServiceName = "cache-encryption-interop-test")
            .AddCacheEncryption();

        using var provider = services.BuildServiceProvider();

        // The fake genuinely resolves as the interface AddCacheEncryption() requires.
        var encryptionService = provider.GetRequiredService<ISymmetricEncryptionService>();
        Assert.IsType<FakeSymmetricEncryptionService>(encryptionService);

        var cache = provider.GetRequiredService<ICacheService>();

        await cache.SetAsync("interop-key", "secret-payload", CachePolicy.Default);
        var result = await cache.TryGetAsync<string>("interop-key");

        Assert.True(result.IsHit);
        Assert.Equal("secret-payload", result.Value);
    }

    [Fact]
    public void AddCacheEncryption_WithoutAnySymmetricEncryptionServiceRegistered_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        var builder = services.AddSharedKernelCaching(o => o.ServiceName = "cache-encryption-negative-test");

        Assert.Throws<InvalidOperationException>(() => builder.AddCacheEncryption());
    }
}
