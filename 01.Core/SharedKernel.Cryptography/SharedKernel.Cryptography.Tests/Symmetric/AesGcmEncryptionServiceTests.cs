using System.Text;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Primitives.Results;
using Xunit;

namespace SharedKernel.Cryptography.Tests.Symmetric;

public sealed class AesGcmEncryptionServiceTests
{
    [Fact]
    public void Encrypt_ThenDecrypt_RoundTripsPlaintext()
    {
        var keyProvider = new InMemoryEncryptionKeyProvider();
        var service = new AesGcmEncryptionService(keyProvider);
        byte[] plaintext = Encoding.UTF8.GetBytes("secret payload");

        EncryptedPayload payload = service.Encrypt(plaintext);
        Result<byte[]> result = service.Decrypt(payload);

        Assert.True(result.IsSuccess);
        Assert.Equal(plaintext, result.Value);
    }

    [Fact]
    public void Encrypt_GeneratesDifferentNonceEachCall()
    {
        var keyProvider = new InMemoryEncryptionKeyProvider();
        var service = new AesGcmEncryptionService(keyProvider);
        byte[] plaintext = Encoding.UTF8.GetBytes("same plaintext");

        EncryptedPayload first = service.Encrypt(plaintext);
        EncryptedPayload second = service.Encrypt(plaintext);

        Assert.NotEqual(first.Nonce, second.Nonce);
    }

    [Fact]
    public void Decrypt_WithFlippedCiphertextByte_Fails()
    {
        var keyProvider = new InMemoryEncryptionKeyProvider();
        var service = new AesGcmEncryptionService(keyProvider);
        EncryptedPayload payload = service.Encrypt(Encoding.UTF8.GetBytes("secret"));

        byte[] tamperedCiphertext = [.. payload.Ciphertext];
        tamperedCiphertext[0] ^= 0xFF;
        var tampered = new EncryptedPayload(payload.KeyId, payload.Nonce, tamperedCiphertext, payload.Tag);

        Result<byte[]> result = service.Decrypt(tampered);

        Assert.True(result.IsFailure);
        Assert.Equal(CryptographyErrorCodes.DecryptionFailed, result.Error.Code);
    }

    [Fact]
    public void Decrypt_WithFlippedTagByte_Fails()
    {
        var keyProvider = new InMemoryEncryptionKeyProvider();
        var service = new AesGcmEncryptionService(keyProvider);
        EncryptedPayload payload = service.Encrypt(Encoding.UTF8.GetBytes("secret"));

        byte[] tamperedTag = [.. payload.Tag];
        tamperedTag[0] ^= 0xFF;
        var tampered = new EncryptedPayload(payload.KeyId, payload.Nonce, payload.Ciphertext, tamperedTag);

        Result<byte[]> result = service.Decrypt(tampered);

        Assert.True(result.IsFailure);
        Assert.Equal(CryptographyErrorCodes.DecryptionFailed, result.Error.Code);
    }

    [Fact]
    public void Decrypt_WithUnknownKeyId_Fails()
    {
        var keyProvider = new InMemoryEncryptionKeyProvider();
        var service = new AesGcmEncryptionService(keyProvider);
        EncryptedPayload payload = service.Encrypt(Encoding.UTF8.GetBytes("secret"));

        var withUnknownKey = new EncryptedPayload("retired-key", payload.Nonce, payload.Ciphertext, payload.Tag);
        Result<byte[]> result = service.Decrypt(withUnknownKey);

        Assert.True(result.IsFailure);
        Assert.Equal(CryptographyErrorCodes.UnknownKeyId, result.Error.Code);
    }

    [Fact]
    public void Decrypt_AfterKeyRotation_StillDecryptsWithOldKeyVersion()
    {
        var keyProvider = new InMemoryEncryptionKeyProvider("v1");
        var service = new AesGcmEncryptionService(keyProvider);
        byte[] plaintext = Encoding.UTF8.GetBytes("payload encrypted under v1");

        EncryptedPayload payload = service.Encrypt(plaintext);

        // Rotate to a new current key — v1 remains resolvable for old payloads.
        keyProvider.AddKey("v2");
        keyProvider.SetCurrentKey("v2");

        Result<byte[]> result = service.Decrypt(payload);

        Assert.True(result.IsSuccess);
        Assert.Equal(plaintext, result.Value);
        Assert.Equal("v1", payload.KeyId);
    }

    [Fact]
    public void EncryptToString_ThenDecryptToString_RoundTripsPlaintext()
    {
        var keyProvider = new InMemoryEncryptionKeyProvider();
        var service = new AesGcmEncryptionService(keyProvider);

        string encoded = service.EncryptToString("hello world");
        Result<string> result = service.DecryptToString(encoded);

        Assert.True(result.IsSuccess);
        Assert.Equal("hello world", result.Value);
    }

    [Fact]
    public void DecryptToString_WithMalformedInput_Fails()
    {
        var keyProvider = new InMemoryEncryptionKeyProvider();
        var service = new AesGcmEncryptionService(keyProvider);

        Result<string> result = service.DecryptToString("not-valid-base64-payload-!!!");

        Assert.True(result.IsFailure);
        Assert.Equal(CryptographyErrorCodes.MalformedPayload, result.Error.Code);
    }

    [Fact]
    public void Constructor_NullKeyProvider_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new AesGcmEncryptionService(null!));
    }
}
