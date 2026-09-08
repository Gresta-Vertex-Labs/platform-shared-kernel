using SharedKernel.Cryptography;
using SharedKernel.Testing.Cryptography;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Cryptography;

/// <summary>
/// Proves <see cref="FakeSymmetricEncryptionService"/> against <c>ISymmetricEncryptionService</c>'s
/// documented contract, including its required-AAD shape (P-502/WO-081). Proven exclusively in
/// <c>SharedKernel.Testing.SelfTests</c> — see <c>16.Testing/state-map.md</c> T-56/T-102.
/// </summary>
public sealed class FakeSymmetricEncryptionServiceTests
{
    private static readonly byte[] NoAad = [];

    [Fact]
    public void Encrypt_ThenDecrypt_RoundTripsPlaintext()
    {
        var service = new FakeSymmetricEncryptionService();
        var plaintext = "secret payload"u8.ToArray();

        var payload = service.Encrypt(plaintext, NoAad);
        var result = service.Decrypt(payload, NoAad);

        Assert.True(result.IsSuccess);
        Assert.Equal(plaintext, result.Value);
    }

    [Fact]
    public void EncryptToString_ThenDecryptToString_RoundTripsPlaintext()
    {
        var service = new FakeSymmetricEncryptionService();

        var encoded = service.EncryptToString("hello world", NoAad);
        var result = service.DecryptToString(encoded, NoAad);

        Assert.True(result.IsSuccess);
        Assert.Equal("hello world", result.Value);
    }

    [Fact]
    public void Decrypt_UnknownKeyId_ReturnsFailure_MatchingCryptographyErrorCodesUnknownKeyId()
    {
        var service = new FakeSymmetricEncryptionService();
        var payload = service.Encrypt("data"u8.ToArray(), NoAad);
        var withUnknownKey = payload with { KeyId = "retired-key" };

        var result = service.Decrypt(withUnknownKey, NoAad);

        Assert.True(result.IsFailure);
        Assert.Equal(CryptographyErrorCodes.UnknownKeyId, result.Error.Code);
    }

    [Fact]
    public void SimulateDecryptFailure_ForcesDecryptFailure_MatchingCryptographyErrorCodesDecryptionFailed()
    {
        var service = new FakeSymmetricEncryptionService { SimulateDecryptFailure = true };
        var payload = service.Encrypt("data"u8.ToArray(), NoAad);

        var result = service.Decrypt(payload, NoAad);

        Assert.True(result.IsFailure);
        Assert.Equal(CryptographyErrorCodes.DecryptionFailed, result.Error.Code);
    }

    [Fact]
    public void SimulateDecryptFailure_False_AllowsNormalDecryptToSucceed()
    {
        var service = new FakeSymmetricEncryptionService { SimulateDecryptFailure = false };
        var payload = service.Encrypt("data"u8.ToArray(), NoAad);

        var result = service.Decrypt(payload, NoAad);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void EncryptedPayloads_RecordsEveryPayload_ProducedByBothEncryptOverloads()
    {
        var service = new FakeSymmetricEncryptionService();

        service.Encrypt("first"u8.ToArray(), NoAad);
        service.EncryptToString("second", NoAad);

        Assert.Equal(2, service.EncryptedPayloads.Count);
    }

    [Fact]
    public void Constructor_WithNoKeyProviderSupplied_UsesInternalDefaultProvider_EncryptDecryptStillWorks()
    {
        var service = new FakeSymmetricEncryptionService();

        var payload = service.Encrypt("zero config convenience"u8.ToArray(), NoAad);
        var result = service.Decrypt(payload, NoAad);

        Assert.True(result.IsSuccess);
        Assert.Equal("zero config convenience"u8.ToArray(), result.Value);
    }

    [Fact]
    public void Constructor_WithSuppliedKeyProvider_UsesThatProviderForEncryption()
    {
        var keyProvider = new FakeEncryptionKeyProvider(currentKeyId: "custom-key");
        var service = new FakeSymmetricEncryptionService(keyProvider);

        var payload = service.Encrypt("data"u8.ToArray(), NoAad);

        Assert.Equal("custom-key", payload.KeyId);
    }

    [Fact]
    public void DecryptToString_MalformedInput_ReturnsFailure_MatchingCryptographyErrorCodesMalformedPayload()
    {
        var service = new FakeSymmetricEncryptionService();

        var result = service.DecryptToString("not-a-valid-payload-!!!", NoAad);

        Assert.True(result.IsFailure);
        Assert.Equal(CryptographyErrorCodes.MalformedPayload, result.Error.Code);
    }

    [Fact]
    public void Encrypt_NullPlaintext_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new FakeSymmetricEncryptionService().Encrypt(null!, NoAad));

    [Fact]
    public void Encrypt_NullAssociatedData_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new FakeSymmetricEncryptionService().Encrypt("data"u8.ToArray(), null!));

    [Fact]
    public void Decrypt_NullPayload_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new FakeSymmetricEncryptionService().Decrypt(null!, NoAad));

    [Fact]
    public void Decrypt_NullAssociatedData_Throws()
    {
        var service = new FakeSymmetricEncryptionService();
        var payload = service.Encrypt("data"u8.ToArray(), NoAad);

        Assert.Throws<ArgumentNullException>(() => service.Decrypt(payload, null!));
    }

    // --- AAD enforcement (T-102, the headline acceptance criterion of P-502/WO-081) ---

    [Fact]
    public void Encrypt_ThenDecrypt_WithMatchingAssociatedData_Succeeds()
    {
        var service = new FakeSymmetricEncryptionService();
        var plaintext = "secret payload"u8.ToArray();
        var aad = "row-pk-42"u8.ToArray();

        var payload = service.Encrypt(plaintext, aad);
        var result = service.Decrypt(payload, aad);

        Assert.True(result.IsSuccess);
        Assert.Equal(plaintext, result.Value);
    }

    [Fact]
    public void Decrypt_WithMismatchedAssociatedData_Fails_MatchingCryptographyErrorCodesDecryptionFailed()
    {
        var service = new FakeSymmetricEncryptionService();
        var payload = service.Encrypt("secret payload"u8.ToArray(), "row-pk-42"u8.ToArray());

        var result = service.Decrypt(payload, "row-pk-99"u8.ToArray());

        Assert.True(result.IsFailure);
        Assert.Equal(CryptographyErrorCodes.DecryptionFailed, result.Error.Code);
    }

    [Fact]
    public void Decrypt_SwappingAssociatedDataBetweenTwoOtherwiseIdenticalPayloads_BothFail()
    {
        var service = new FakeSymmetricEncryptionService();
        var aadA = "context-a"u8.ToArray();
        var aadB = "context-b"u8.ToArray();

        var payloadA = service.Encrypt("payload"u8.ToArray(), aadA);
        var payloadB = service.Encrypt("payload"u8.ToArray(), aadB);

        Assert.True(service.Decrypt(payloadA, aadB).IsFailure);
        Assert.True(service.Decrypt(payloadB, aadA).IsFailure);
        Assert.True(service.Decrypt(payloadA, aadA).IsSuccess);
        Assert.True(service.Decrypt(payloadB, aadB).IsSuccess);
    }

    [Fact]
    public void Encrypt_ThenDecrypt_WithEmptyAssociatedData_IsAValidAlwaysSucceedingNoContextBindingChoice()
    {
        var service = new FakeSymmetricEncryptionService();
        var plaintext = "no context binding needed"u8.ToArray();

        var payload = service.Encrypt(plaintext, []);
        var result = service.Decrypt(payload, []);

        Assert.True(result.IsSuccess);
        Assert.Equal(plaintext, result.Value);
    }

    [Fact]
    public void EncryptedPayloads_PairsEachRecordedPayload_WithTheExactAssociatedDataUsedToProduceIt()
    {
        var service = new FakeSymmetricEncryptionService();
        var aadOne = "aad-one"u8.ToArray();
        var aadTwo = "aad-two"u8.ToArray();

        service.Encrypt("first"u8.ToArray(), aadOne);
        service.Encrypt("second"u8.ToArray(), aadTwo);

        Assert.Equal(2, service.EncryptedPayloads.Count);
        Assert.Equal(aadOne, service.EncryptedPayloads[0].AssociatedData);
        Assert.Equal(aadTwo, service.EncryptedPayloads[1].AssociatedData);
    }

    [Fact]
    public void SimulateDecryptFailure_StillFailsUnconditionally_RegardlessOfAssociatedDataCorrectness()
    {
        var service = new FakeSymmetricEncryptionService { SimulateDecryptFailure = true };
        var aad = "matching-aad"u8.ToArray();
        var payload = service.Encrypt("data"u8.ToArray(), aad);

        // Even with the CORRECT AAD supplied, SimulateDecryptFailure still forces a failure.
        var result = service.Decrypt(payload, aad);

        Assert.True(result.IsFailure);
        Assert.Equal(CryptographyErrorCodes.DecryptionFailed, result.Error.Code);
    }

    [Fact]
    public async Task EncryptAsync_ThenDecryptAsync_WithMismatchedAssociatedData_Fails()
    {
        var service = new FakeSymmetricEncryptionService();
        var payload = await service.EncryptAsync("secret"u8.ToArray(), "context-a"u8.ToArray());

        var result = await service.DecryptAsync(payload, "context-b"u8.ToArray());

        Assert.True(result.IsFailure);
        Assert.Equal(CryptographyErrorCodes.DecryptionFailed, result.Error.Code);
    }

    [Fact]
    public async Task EncryptToStringAsync_ThenDecryptToStringAsync_WithMismatchedAssociatedData_Fails()
    {
        var service = new FakeSymmetricEncryptionService();
        var encoded = await service.EncryptToStringAsync("secret", "context-a"u8.ToArray());

        var result = await service.DecryptToStringAsync(encoded, "context-b"u8.ToArray());

        Assert.True(result.IsFailure);
        Assert.Equal(CryptographyErrorCodes.DecryptionFailed, result.Error.Code);
    }
}
