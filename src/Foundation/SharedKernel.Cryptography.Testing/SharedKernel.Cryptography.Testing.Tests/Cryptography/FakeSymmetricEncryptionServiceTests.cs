using SharedKernel.Cryptography;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Primitives.Errors;
using SharedKernel.Testing.Cryptography;

namespace SharedKernel.Testing.SelfTests.Cryptography;

/// <summary>
/// Proves <see cref="FakeSymmetricEncryptionService"/> against both <c>ISymmetricEncryptionService</c> and
/// <c>ISynchronousSymmetricEncryptionService</c>: real AES-256-GCM with enforced associated data, the production
/// error codes, a record of every encryption, and simulated decryption failure.
/// </summary>
public sealed class FakeSymmetricEncryptionServiceTests
{
    private static readonly byte[] NoAad = [];

    [Fact]
    public void Encrypt_ThenDecrypt_RoundTripsPlaintext()
    {
        var service = new FakeSymmetricEncryptionService();

        EncryptedPayload payload = service.Encrypt("secret payload"u8, NoAad);
        var result = service.Decrypt(payload, NoAad);

        Assert.True(result.IsSuccess);
        Assert.Equal("secret payload"u8.ToArray(), result.Value);
    }

    [Fact]
    public async Task EncryptAsync_ThenDecryptAsync_RoundTripsPlaintext()
    {
        var service = new FakeSymmetricEncryptionService();

        EncryptedPayload payload = await service.EncryptAsync("secret payload"u8.ToArray(), NoAad);
        var result = await service.DecryptAsync(payload, NoAad);

        Assert.True(result.IsSuccess);
        Assert.Equal("secret payload"u8.ToArray(), result.Value);
    }

    [Fact]
    public async Task StringMembers_RoundTrip_AcrossTheSynchronousAndAsynchronousShapes()
    {
        var service = new FakeSymmetricEncryptionService();

        string fromSync = service.EncryptToString("hello", NoAad);
        string fromAsync = await service.EncryptToStringAsync("world", NoAad);

        Assert.Equal("hello", (await service.DecryptToStringAsync(fromSync, NoAad)).Value);
        Assert.Equal("world", service.DecryptToString(fromAsync, NoAad).Value);
    }

    [Fact]
    public void Payload_RoundTripsThroughBothWireFormats()
    {
        var service = new FakeSymmetricEncryptionService();
        EncryptedPayload payload = service.Encrypt("data"u8, NoAad);

        Assert.True(EncryptedPayload.TryParse(payload.ToString(), out EncryptedPayload? fromText));
        Assert.True(EncryptedPayload.TryParse(payload.ToBytes(), out EncryptedPayload? fromBytes));

        Assert.Equal("data"u8.ToArray(), service.Decrypt(fromText!, NoAad).Value);
        Assert.Equal("data"u8.ToArray(), service.Decrypt(fromBytes!, NoAad).Value);
    }

    [Fact]
    public async Task Payloads_ReadableByTheProductionServicesOverTheSameKeys()
    {
        var service = new FakeSymmetricEncryptionService();
        EncryptedPayload payload = service.Encrypt("data"u8, NoAad);

        var bySync = new SynchronousAesGcmEncryptionService(service.KeyProvider).Decrypt(payload, NoAad);
        var byAsync = await new AesGcmEncryptionService(service.KeyProvider).DecryptAsync(payload, NoAad);

        Assert.Equal("data"u8.ToArray(), bySync.Value);
        Assert.Equal("data"u8.ToArray(), byAsync.Value);
    }

    [Fact]
    public void Decrypt_UnknownKeyId_FailsWithUnknownKeyId()
    {
        var service = new FakeSymmetricEncryptionService();
        EncryptedPayload payload = service.Encrypt("data"u8, NoAad);
        var withUnknownKey = new EncryptedPayload("retired-key", payload.Nonce, payload.Ciphertext, payload.Tag);

        var result = service.Decrypt(withUnknownKey, NoAad);

        Assert.True(result.IsFailure);
        Assert.Equal(CryptographyErrorCodes.UnknownKeyId, result.Error.Code);
        Assert.Equal(ErrorType.Unexpected, result.Error.Type);
    }

    [Fact]
    public void Decrypt_TamperedCiphertext_FailsWithDecryptionFailed()
    {
        var service = new FakeSymmetricEncryptionService();
        EncryptedPayload payload = service.Encrypt("data"u8, NoAad);
        byte[] ciphertext = payload.Ciphertext.ToArray();
        ciphertext[0] ^= 0x01;
        var tampered = new EncryptedPayload(payload.KeyId, payload.Nonce, ciphertext, payload.Tag);

        var result = service.Decrypt(tampered, NoAad);

        Assert.True(result.IsFailure);
        Assert.Equal(CryptographyErrorCodes.DecryptionFailed, result.Error.Code);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
    }

    [Fact]
    public async Task DecryptToString_MalformedInput_FailsWithMalformedPayload()
    {
        var service = new FakeSymmetricEncryptionService();

        var fromSync = service.DecryptToString("not-a-valid-payload-!!!", NoAad);
        var fromAsync = await service.DecryptToStringAsync("not-a-valid-payload-!!!", NoAad);

        Assert.Equal(CryptographyErrorCodes.MalformedPayload, fromSync.Error.Code);
        Assert.Equal(CryptographyErrorCodes.MalformedPayload, fromAsync.Error.Code);
        Assert.Equal(ErrorType.Validation, fromSync.Error.Type);
    }

    [Fact]
    public void Constructor_WithSuppliedKeyProvider_EncryptsWithItsCurrentKey()
    {
        var keyProvider = new FakeEncryptionKeyProvider(currentKeyId: "custom-key");
        var service = new FakeSymmetricEncryptionService(keyProvider);

        EncryptedPayload payload = service.Encrypt("data"u8, NoAad);

        Assert.Same(keyProvider, service.KeyProvider);
        Assert.Equal("custom-key", payload.KeyId);
    }

    [Fact]
    public async Task Rotation_IsEncryptedWithCurrentKey_AndReEncrypt_FollowTheKeyProvider()
    {
        var service = new FakeSymmetricEncryptionService();
        EncryptedPayload original = service.Encrypt("data"u8, NoAad);

        service.KeyProvider.AddKey("v2");
        service.KeyProvider.SetCurrentKey("v2");

        Assert.False(service.IsEncryptedWithCurrentKey(original));
        Assert.False(await service.IsEncryptedWithCurrentKeyAsync(original));
        var syncReEncrypted = service.ReEncrypt(original, NoAad);
        var asyncReEncrypted = await service.ReEncryptAsync(original, NoAad);
        Assert.Equal("v2", syncReEncrypted.Value.KeyId);
        Assert.Equal("v2", asyncReEncrypted.Value.KeyId);
        Assert.True(service.IsEncryptedWithCurrentKey(syncReEncrypted.Value));
    }

    [Fact]
    public async Task SimulateDecryptFailure_FailsEveryDecryptingMember_EvenWithCorrectAssociatedData()
    {
        var service = new FakeSymmetricEncryptionService();
        byte[] aad = "matching-aad"u8.ToArray();
        EncryptedPayload payload = service.Encrypt("data"u8, aad);
        string encoded = service.EncryptToString("data", aad);
        service.KeyProvider.AddKey("v2");
        service.KeyProvider.SetCurrentKey("v2");

        service.SimulateDecryptFailure = true;

        Assert.Equal(CryptographyErrorCodes.DecryptionFailed, service.Decrypt(payload, aad).Error.Code);
        Assert.Equal(CryptographyErrorCodes.DecryptionFailed, (await service.DecryptAsync(payload, aad)).Error.Code);
        Assert.Equal(CryptographyErrorCodes.DecryptionFailed, service.DecryptToString(encoded, aad).Error.Code);
        Assert.Equal(CryptographyErrorCodes.DecryptionFailed, (await service.DecryptToStringAsync(encoded, aad)).Error.Code);
        Assert.Equal(CryptographyErrorCodes.DecryptionFailed, service.ReEncrypt(payload, aad).Error.Code);
        Assert.Equal(CryptographyErrorCodes.DecryptionFailed, (await service.ReEncryptAsync(payload, aad)).Error.Code);
    }

    [Fact]
    public void SimulateDecryptFailure_False_AllowsDecryptToSucceed()
    {
        var service = new FakeSymmetricEncryptionService { SimulateDecryptFailure = false };
        EncryptedPayload payload = service.Encrypt("data"u8, NoAad);

        Assert.True(service.Decrypt(payload, NoAad).IsSuccess);
    }

    [Fact]
    public async Task EncryptedPayloads_RecordsEveryEncryptingMember_WithItsAssociatedData()
    {
        var service = new FakeSymmetricEncryptionService();

        EncryptedPayload first = service.Encrypt("first"u8, "aad-1"u8);
        service.EncryptToString("second", "aad-2"u8);
        EncryptedPayload third = await service.EncryptAsync("third"u8.ToArray(), "aad-3"u8.ToArray());
        await service.EncryptToStringAsync("fourth", "aad-4"u8.ToArray());

        Assert.Equal(4, service.EncryptedPayloads.Count);
        Assert.Same(first, service.EncryptedPayloads[0].Payload);
        Assert.Same(third, service.EncryptedPayloads[2].Payload);
        Assert.Equal("aad-1"u8.ToArray(), service.EncryptedPayloads[0].AssociatedData);
        Assert.Equal("aad-2"u8.ToArray(), service.EncryptedPayloads[1].AssociatedData);
        Assert.Equal("aad-3"u8.ToArray(), service.EncryptedPayloads[2].AssociatedData);
        Assert.Equal("aad-4"u8.ToArray(), service.EncryptedPayloads[3].AssociatedData);
    }

    [Fact]
    public void Decrypt_NullPayload_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new FakeSymmetricEncryptionService().Decrypt(null!, NoAad));

    [Fact]
    public async Task NullStringArguments_Throw()
    {
        var service = new FakeSymmetricEncryptionService();

        Assert.Throws<ArgumentNullException>(() => service.EncryptToString(null!, NoAad));
        Assert.Throws<ArgumentNullException>(() => service.DecryptToString(null!, NoAad));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await service.EncryptToStringAsync(null!, NoAad));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await service.DecryptAsync(null!, NoAad));
    }

    // --- Associated data enforcement ---

    [Fact]
    public void Decrypt_WithMismatchedAssociatedData_FailsWithDecryptionFailed()
    {
        var service = new FakeSymmetricEncryptionService();
        EncryptedPayload payload = service.Encrypt("secret payload"u8, "row-pk-42"u8);

        var result = service.Decrypt(payload, "row-pk-99"u8);

        Assert.True(result.IsFailure);
        Assert.Equal(CryptographyErrorCodes.DecryptionFailed, result.Error.Code);
    }

    [Fact]
    public void Decrypt_SwappingAssociatedDataBetweenTwoPayloads_BothFail()
    {
        var service = new FakeSymmetricEncryptionService();
        byte[] aadA = "context-a"u8.ToArray();
        byte[] aadB = "context-b"u8.ToArray();

        EncryptedPayload payloadA = service.Encrypt("payload"u8, aadA);
        EncryptedPayload payloadB = service.Encrypt("payload"u8, aadB);

        Assert.True(service.Decrypt(payloadA, aadB).IsFailure);
        Assert.True(service.Decrypt(payloadB, aadA).IsFailure);
        Assert.True(service.Decrypt(payloadA, aadA).IsSuccess);
        Assert.True(service.Decrypt(payloadB, aadB).IsSuccess);
    }

    [Fact]
    public async Task AsyncMembers_WithMismatchedAssociatedData_FailWithDecryptionFailed()
    {
        var service = new FakeSymmetricEncryptionService();
        EncryptedPayload payload = await service.EncryptAsync("secret"u8.ToArray(), "context-a"u8.ToArray());
        string encoded = await service.EncryptToStringAsync("secret", "context-a"u8.ToArray());

        var bytes = await service.DecryptAsync(payload, "context-b"u8.ToArray());
        var text = await service.DecryptToStringAsync(encoded, "context-b"u8.ToArray());

        Assert.Equal(CryptographyErrorCodes.DecryptionFailed, bytes.Error.Code);
        Assert.Equal(CryptographyErrorCodes.DecryptionFailed, text.Error.Code);
    }
}
