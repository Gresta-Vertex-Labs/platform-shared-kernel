using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Testing.Cryptography;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Cryptography;

/// <summary>
/// Proves the <c>01.Core</c> P-492 synchronous/async capability gate COMPOSITIONALLY (D-240): the
/// REAL <see cref="AesGcmEncryptionService"/> is wired against 16.Testing's own two fake
/// <see cref="IEncryptionKeyProvider"/> flavors — <see cref="FakeEncryptionKeyProvider"/> (marked
/// <see cref="ISynchronousEncryptionKeyProvider"/>) and <see cref="FakeRemoteEncryptionKeyProvider"/>
/// (deliberately unmarked) — rather than reimplementing the gate inside a fake
/// <see cref="ISymmetricEncryptionService"/>. Proven exclusively in
/// <c>SharedKernel.Testing.SelfTests</c> — see <c>16.Testing/state-map.md</c> T-103.
/// </summary>
public sealed class CryptographyKeyProviderCapabilityGateCompositionTests
{
    [Fact]
    public void AesGcmEncryptionService_WiredToFakeEncryptionKeyProvider_EverySyncMemberSucceeds()
    {
        var service = new AesGcmEncryptionService(new FakeEncryptionKeyProvider());
        var aad = "aad"u8.ToArray();

        var payload = service.Encrypt("plaintext"u8.ToArray(), aad);
        var decrypted = service.Decrypt(payload, aad);
        var encodedString = service.EncryptToString("plaintext", aad);
        var decodedString = service.DecryptToString(encodedString, aad);

        Assert.True(decrypted.IsSuccess);
        Assert.Equal("plaintext"u8.ToArray(), decrypted.Value);
        Assert.True(decodedString.IsSuccess);
        Assert.Equal("plaintext", decodedString.Value);
    }

    [Fact]
    public async Task AesGcmEncryptionService_WiredToFakeRemoteEncryptionKeyProvider_EverySyncMemberThrowsNotSupported()
    {
        var service = new AesGcmEncryptionService(new FakeRemoteEncryptionKeyProvider());
        var aad = "aad"u8.ToArray();

        Assert.Throws<NotSupportedException>(() => service.Encrypt("plaintext"u8.ToArray(), aad));
        Assert.Throws<NotSupportedException>(() => service.EncryptToString("plaintext", aad));

        // Produce a payload via the async path so Decrypt/DecryptToString have something to attempt
        // against — the NotSupportedException must fire before any decryption logic runs at all.
        EncryptedPayload payload = await service.EncryptAsync("plaintext"u8.ToArray(), aad);
        string encoded = await service.EncryptToStringAsync("plaintext", aad);

        Assert.Throws<NotSupportedException>(() => service.Decrypt(payload, aad));
        Assert.Throws<NotSupportedException>(() => service.DecryptToString(encoded, aad));
    }

    [Fact]
    public async Task AesGcmEncryptionService_AsyncMembers_AlwaysWork_RegardlessOfProviderFlavor()
    {
        var service = new AesGcmEncryptionService(new FakeRemoteEncryptionKeyProvider());
        var aad = "aad"u8.ToArray();

        EncryptedPayload payload = await service.EncryptAsync("plaintext"u8.ToArray(), aad);
        var result = await service.DecryptAsync(payload, aad);

        Assert.True(result.IsSuccess);
        Assert.Equal("plaintext"u8.ToArray(), result.Value);
    }

    [Fact]
    public void EncryptionKeyProviderCapabilities_IsGenuinelySynchronous_TrueForFakeEncryptionKeyProvider() =>
        Assert.True(EncryptionKeyProviderCapabilities.IsGenuinelySynchronous(new FakeEncryptionKeyProvider()));

    [Fact]
    public void EncryptionKeyProviderCapabilities_IsGenuinelySynchronous_FalseForFakeRemoteEncryptionKeyProvider() =>
        Assert.False(EncryptionKeyProviderCapabilities.IsGenuinelySynchronous(new FakeRemoteEncryptionKeyProvider()));

    [Fact]
    public void CachedEncryptionKeyProvider_WrappingFakeRemoteEncryptionKeyProvider_StillReportsNotGenuinelySynchronous()
    {
        // Proves the recursive .Inner unwrap sees through the decorator, per D-239's third
        // paragraph and EncryptionKeyProviderCapabilities' own documented contract.
        var remote = new FakeRemoteEncryptionKeyProvider();
        var cached = new CachedEncryptionKeyProvider(remote, TimeProvider.System, TimeSpan.FromMinutes(5));

        Assert.False(EncryptionKeyProviderCapabilities.IsGenuinelySynchronous(cached));
    }

    [Fact]
    public async Task CachedEncryptionKeyProvider_WrappingFakeRemoteEncryptionKeyProvider_SyncMembersStillThrow_EvenAfterCacheWarmsFromAHit()
    {
        var remote = new FakeRemoteEncryptionKeyProvider();
        var cached = new CachedEncryptionKeyProvider(remote, TimeProvider.System, TimeSpan.FromMinutes(5));
        var service = new AesGcmEncryptionService(cached);

        // Warm the cache via a real async call first — a cache hit is fast, but capability is a
        // static provider-identity check, never a per-call cache-warmth test.
        await cached.GetCurrentKeyAsync();

        Assert.Throws<NotSupportedException>(() => service.Encrypt("plaintext"u8.ToArray(), []));
    }

    [Fact]
    public void CachedEncryptionKeyProvider_WrappingFakeEncryptionKeyProvider_RecursivelyReportsGenuinelySynchronous()
    {
        var inner = new FakeEncryptionKeyProvider();
        var cached = new CachedEncryptionKeyProvider(inner, TimeProvider.System, TimeSpan.FromMinutes(5));

        Assert.True(EncryptionKeyProviderCapabilities.IsGenuinelySynchronous(cached));
    }
}
