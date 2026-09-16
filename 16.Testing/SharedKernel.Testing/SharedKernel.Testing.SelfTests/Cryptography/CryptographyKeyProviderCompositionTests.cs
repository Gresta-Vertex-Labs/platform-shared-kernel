using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Cryptography;
using SharedKernel.Cryptography.Extensions;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Primitives.Errors;
using SharedKernel.Testing.Cryptography;

namespace SharedKernel.Testing.SelfTests.Cryptography;

/// <summary>
/// Proves the two fake key provider flavors compose with the REAL <c>SharedKernel.Cryptography</c> services the way
/// production providers do: <see cref="FakeEncryptionKeyProvider"/> is an in-memory provider usable from both the
/// synchronous and the asynchronous service, and <see cref="FakeRemoteEncryptionKeyProvider"/> is a key-service
/// stand-in that only the asynchronous service can use.
/// </summary>
/// <remarks>
/// A synchronous service can only be built over an <see cref="ISynchronousEncryptionKeyProvider"/>: the constructor
/// takes that type, so an asynchronous-only provider is rejected at compile time or, through dependency injection,
/// when the service is resolved — never by blocking on an asynchronous call.
/// </remarks>
public sealed class CryptographyKeyProviderCompositionTests
{
    private static readonly byte[] Aad = "aad"u8.ToArray();

    [Fact]
    public void SynchronousAesGcmEncryptionService_OverFakeEncryptionKeyProvider_EveryMemberRoundTrips()
    {
        var service = new SynchronousAesGcmEncryptionService(new FakeEncryptionKeyProvider());

        EncryptedPayload payload = service.Encrypt("plaintext"u8, Aad);
        var decrypted = service.Decrypt(payload, Aad);
        string encoded = service.EncryptToString("plaintext", Aad);
        var decoded = service.DecryptToString(encoded, Aad);

        Assert.True(decrypted.IsSuccess);
        Assert.Equal("plaintext"u8.ToArray(), decrypted.Value);
        Assert.True(decoded.IsSuccess);
        Assert.Equal("plaintext", decoded.Value);
        Assert.True(service.IsEncryptedWithCurrentKey(payload));
    }

    [Fact]
    public async Task AesGcmEncryptionService_OverFakeRemoteEncryptionKeyProvider_RoundTripsThroughAsyncKeyLookups()
    {
        var provider = new FakeRemoteEncryptionKeyProvider();
        var service = new AesGcmEncryptionService(provider);

        EncryptedPayload payload = await service.EncryptAsync("plaintext"u8.ToArray(), Aad);
        var decrypted = await service.DecryptAsync(payload, Aad);

        Assert.True(decrypted.IsSuccess);
        Assert.Equal("plaintext"u8.ToArray(), decrypted.Value);
        Assert.Equal(1, provider.CurrentKeyCallCount);
        Assert.Equal(1, provider.KeyCallCount);
    }

    [Fact]
    public void FakeRemoteEncryptionKeyProvider_IsNotASynchronousKeyProvider() =>
        Assert.False(typeof(ISynchronousEncryptionKeyProvider).IsAssignableFrom(typeof(FakeRemoteEncryptionKeyProvider)));

    [Fact]
    public void FakeEncryptionKeyProvider_ImplementsBothProviderInterfaces()
    {
        var provider = new FakeEncryptionKeyProvider();

        Assert.IsAssignableFrom<IEncryptionKeyProvider>(provider);
        Assert.IsAssignableFrom<ISynchronousEncryptionKeyProvider>(provider);
    }

    [Fact]
    public void AddSynchronousSymmetricEncryption_WithOnlyAnAsynchronousKeyProvider_FailsWhenTheServiceIsResolved()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IEncryptionKeyProvider>(new FakeRemoteEncryptionKeyProvider());
        services.AddSharedKernelCryptography(new ConfigurationBuilder().Build())
            .AddSymmetricEncryption()
            .AddSynchronousSymmetricEncryption();

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<ISymmetricEncryptionService>());
        var exception = Assert.Throws<InvalidOperationException>(
            () => provider.GetRequiredService<ISynchronousSymmetricEncryptionService>());
        Assert.Contains(nameof(ISynchronousEncryptionKeyProvider), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SynchronousAndAsynchronousServices_OverTheSameFakeEncryptionKeyProvider_ReadEachOthersPayloads()
    {
        var keys = new FakeEncryptionKeyProvider();
        var syncService = new SynchronousAesGcmEncryptionService(keys);
        var asyncService = new AesGcmEncryptionService(keys);

        EncryptedPayload fromSync = syncService.Encrypt("from-sync"u8, Aad);
        EncryptedPayload fromAsync = await asyncService.EncryptAsync("from-async"u8.ToArray(), Aad);

        var readByAsync = await asyncService.DecryptAsync(fromSync, Aad);
        var readBySync = syncService.Decrypt(fromAsync, Aad);

        Assert.Equal("from-sync"u8.ToArray(), readByAsync.Value);
        Assert.Equal("from-async"u8.ToArray(), readBySync.Value);
    }

    [Fact]
    public async Task CachedEncryptionKeyProvider_OverFakeRemoteEncryptionKeyProvider_ServesRepeatLookupsFromCache()
    {
        var remote = new FakeRemoteEncryptionKeyProvider();
        var cached = new CachedEncryptionKeyProvider(remote, TimeProvider.System, TimeSpan.FromMinutes(5));
        var service = new AesGcmEncryptionService(cached);

        EncryptedPayload first = await service.EncryptAsync("one"u8.ToArray(), Aad);
        EncryptedPayload second = await service.EncryptAsync("two"u8.ToArray(), Aad);
        Assert.True((await service.DecryptAsync(first, Aad)).IsSuccess);
        Assert.True((await service.DecryptAsync(second, Aad)).IsSuccess);

        Assert.Equal(1, remote.CurrentKeyCallCount);
        Assert.Equal(1, remote.KeyCallCount);
        Assert.False(typeof(ISynchronousEncryptionKeyProvider).IsAssignableFrom(typeof(CachedEncryptionKeyProvider)));
    }

    [Fact]
    public async Task KeyRotation_OnFakeEncryptionKeyProvider_ReEncryptsOldPayloadsUnderTheNewCurrentKey()
    {
        var keys = new FakeEncryptionKeyProvider();
        var service = new AesGcmEncryptionService(keys);
        EncryptedPayload original = await service.EncryptAsync("secret"u8.ToArray(), Aad);

        keys.AddKey("v2");
        keys.SetCurrentKey("v2");

        Assert.False(await service.IsEncryptedWithCurrentKeyAsync(original));
        var reEncrypted = await service.ReEncryptAsync(original, Aad);
        Assert.True(reEncrypted.IsSuccess);
        Assert.Equal("v2", reEncrypted.Value.KeyId);
        Assert.Equal("secret"u8.ToArray(), (await service.DecryptAsync(reEncrypted.Value, Aad)).Value);
    }

    [Fact]
    public async Task RemovedKey_OnFakeEncryptionKeyProvider_FailsDecryptionWithUnknownKeyId()
    {
        var keys = new FakeEncryptionKeyProvider();
        var service = new AesGcmEncryptionService(keys);
        EncryptedPayload payload = await service.EncryptAsync("secret"u8.ToArray(), Aad);

        keys.AddKey("v2");
        keys.SetCurrentKey("v2");
        keys.RemoveKey("v1");
        var result = await service.DecryptAsync(payload, Aad);

        Assert.True(result.IsFailure);
        Assert.Equal(CryptographyErrorCodes.UnknownKeyId, result.Error.Code);
        Assert.Equal(ErrorType.Unexpected, result.Error.Type);
    }
}
