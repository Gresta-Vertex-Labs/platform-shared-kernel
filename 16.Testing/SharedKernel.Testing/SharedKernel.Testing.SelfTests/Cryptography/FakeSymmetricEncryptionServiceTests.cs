using SharedKernel.Cryptography;
using SharedKernel.Testing.Cryptography;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Cryptography;

/// <summary>
/// Proves <see cref="FakeSymmetricEncryptionService"/> against <c>ISymmetricEncryptionService</c>'s
/// documented contract. Proven exclusively in <c>SharedKernel.Testing.SelfTests</c> — see
/// <c>16.Testing/state-map.md</c> T-56.
/// </summary>
public sealed class FakeSymmetricEncryptionServiceTests
{
    [Fact]
    public void Encrypt_ThenDecrypt_RoundTripsPlaintext()
    {
        var service = new FakeSymmetricEncryptionService();
        var plaintext = "secret payload"u8.ToArray();

        var payload = service.Encrypt(plaintext);
        var result = service.Decrypt(payload);

        Assert.True(result.IsSuccess);
        Assert.Equal(plaintext, result.Value);
    }

    [Fact]
    public void EncryptToString_ThenDecryptToString_RoundTripsPlaintext()
    {
        var service = new FakeSymmetricEncryptionService();

        var encoded = service.EncryptToString("hello world");
        var result = service.DecryptToString(encoded);

        Assert.True(result.IsSuccess);
        Assert.Equal("hello world", result.Value);
    }

    [Fact]
    public void Decrypt_UnknownKeyId_ReturnsFailure_MatchingCryptographyErrorCodesUnknownKeyId()
    {
        var service = new FakeSymmetricEncryptionService();
        var payload = service.Encrypt("data"u8.ToArray());
        var withUnknownKey = payload with { KeyId = "retired-key" };

        var result = service.Decrypt(withUnknownKey);

        Assert.True(result.IsFailure);
        Assert.Equal(CryptographyErrorCodes.UnknownKeyId, result.Error.Code);
    }

    [Fact]
    public void SimulateDecryptFailure_ForcesDecryptFailure_MatchingCryptographyErrorCodesDecryptionFailed()
    {
        var service = new FakeSymmetricEncryptionService { SimulateDecryptFailure = true };
        var payload = service.Encrypt("data"u8.ToArray());

        var result = service.Decrypt(payload);

        Assert.True(result.IsFailure);
        Assert.Equal(CryptographyErrorCodes.DecryptionFailed, result.Error.Code);
    }

    [Fact]
    public void SimulateDecryptFailure_False_AllowsNormalDecryptToSucceed()
    {
        var service = new FakeSymmetricEncryptionService { SimulateDecryptFailure = false };
        var payload = service.Encrypt("data"u8.ToArray());

        var result = service.Decrypt(payload);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void EncryptedPayloads_RecordsEveryPayload_ProducedByBothEncryptOverloads()
    {
        var service = new FakeSymmetricEncryptionService();

        service.Encrypt("first"u8.ToArray());
        service.EncryptToString("second");

        Assert.Equal(2, service.EncryptedPayloads.Count);
    }

    [Fact]
    public void Constructor_WithNoKeyProviderSupplied_UsesInternalDefaultProvider_EncryptDecryptStillWorks()
    {
        var service = new FakeSymmetricEncryptionService();

        var payload = service.Encrypt("zero config convenience"u8.ToArray());
        var result = service.Decrypt(payload);

        Assert.True(result.IsSuccess);
        Assert.Equal("zero config convenience"u8.ToArray(), result.Value);
    }

    [Fact]
    public void Constructor_WithSuppliedKeyProvider_UsesThatProviderForEncryption()
    {
        var keyProvider = new FakeEncryptionKeyProvider(currentKeyId: "custom-key");
        var service = new FakeSymmetricEncryptionService(keyProvider);

        var payload = service.Encrypt("data"u8.ToArray());

        Assert.Equal("custom-key", payload.KeyId);
    }

    [Fact]
    public void DecryptToString_MalformedInput_ReturnsFailure_MatchingCryptographyErrorCodesMalformedPayload()
    {
        var service = new FakeSymmetricEncryptionService();

        var result = service.DecryptToString("not-a-valid-payload-!!!");

        Assert.True(result.IsFailure);
        Assert.Equal(CryptographyErrorCodes.MalformedPayload, result.Error.Code);
    }

    [Fact]
    public void Encrypt_NullPlaintext_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new FakeSymmetricEncryptionService().Encrypt(null!));

    [Fact]
    public void Decrypt_NullPayload_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new FakeSymmetricEncryptionService().Decrypt(null!));
}
