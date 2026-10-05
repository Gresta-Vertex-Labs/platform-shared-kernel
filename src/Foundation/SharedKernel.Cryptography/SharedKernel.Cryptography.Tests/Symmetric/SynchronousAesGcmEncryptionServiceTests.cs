using System.Security.Cryptography;
using System.Text;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Cryptography.Tests.TestDoubles;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using static SharedKernel.Cryptography.Tests.Symmetric.AesGcmEncryptionServiceTests;

namespace SharedKernel.Cryptography.Tests.Symmetric;

public sealed class SynchronousAesGcmEncryptionServiceTests
{
    private static readonly byte[] AssociatedData = Encoding.UTF8.GetBytes("orders/1234");

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1 << 20)]
    public void Encrypt_Decrypt_RoundTrips(int length)
    {
        var service = new SynchronousAesGcmEncryptionService(TestKeys.SingleKeyProvider());
        byte[] plaintext = RandomNumberGenerator.GetBytes(length);

        EncryptedPayload payload = service.Encrypt(plaintext, AssociatedData);
        Result<byte[]> decrypted = service.Decrypt(payload, AssociatedData);

        Assert.True(decrypted.IsSuccess);
        Assert.Equal(plaintext, decrypted.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("Şifreli değer — 🎉")]
    public void EncryptToString_DecryptToString_RoundTrips(string plaintext)
    {
        var service = new SynchronousAesGcmEncryptionService(TestKeys.SingleKeyProvider());

        Result<string> decrypted = service.DecryptToString(service.EncryptToString(plaintext, AssociatedData), AssociatedData);

        Assert.Equal(plaintext, decrypted.Value);
    }

    [Fact]
    public void Encrypt_SamePlaintextTwice_UsesDifferentNonces()
    {
        var service = new SynchronousAesGcmEncryptionService(TestKeys.SingleKeyProvider());

        EncryptedPayload first = service.Encrypt([1, 2, 3], AssociatedData);
        EncryptedPayload second = service.Encrypt([1, 2, 3], AssociatedData);

        Assert.NotEqual(first.Nonce.ToArray(), second.Nonce.ToArray());
    }

    [Fact]
    public void Decrypt_DifferentAssociatedData_ReturnsDecryptionFailed()
    {
        var service = new SynchronousAesGcmEncryptionService(TestKeys.SingleKeyProvider());
        EncryptedPayload payload = service.Encrypt([1, 2, 3], AssociatedData);

        ResultAssert.Failure(service.Decrypt(payload, "orders/1235"u8), CryptographyErrorCodes.DecryptionFailed, ErrorType.Validation);
    }

    [Theory]
    [InlineData(PayloadPart.Nonce)]
    [InlineData(PayloadPart.Ciphertext)]
    [InlineData(PayloadPart.Tag)]
    public void Decrypt_TamperedPayload_ReturnsDecryptionFailed(PayloadPart part)
    {
        var service = new SynchronousAesGcmEncryptionService(TestKeys.SingleKeyProvider());
        EncryptedPayload payload = service.Encrypt("do not alter"u8, AssociatedData);

        ResultAssert.Failure(service.Decrypt(Tamper(payload, part), AssociatedData), CryptographyErrorCodes.DecryptionFailed, ErrorType.Validation);
    }

    [Fact]
    public void Decrypt_UnknownKeyId_ReturnsUnknownKeyIdWithoutRevealingId()
    {
        const string keyId = "retired-key-7f3a9c";
        var encryptor = new SynchronousAesGcmEncryptionService(TestKeys.SingleKeyProvider(keyId));
        var decryptor = new SynchronousAesGcmEncryptionService(TestKeys.SingleKeyProvider("other"));

        Result<byte[]> decrypted = decryptor.Decrypt(encryptor.Encrypt([1], AssociatedData), AssociatedData);

        ResultAssert.Failure(decrypted, CryptographyErrorCodes.UnknownKeyId, ErrorType.Unexpected);
        Assert.DoesNotContain(keyId, decrypted.Error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("AQ")]
    public void DecryptToString_MalformedInput_ReturnsMalformedPayload(string encoded)
    {
        var service = new SynchronousAesGcmEncryptionService(TestKeys.SingleKeyProvider());

        ResultAssert.Failure(service.DecryptToString(encoded, AssociatedData), CryptographyErrorCodes.MalformedPayload, ErrorType.Validation);
    }

    [Theory]
    [InlineData("not a payload")]
    [InlineData("@@@@")]
    public void DecryptToString_CharactersOutsideBase64UrlAlphabet_ReturnsMalformedPayload(string encoded)
    {
        var service = new SynchronousAesGcmEncryptionService(TestKeys.SingleKeyProvider());

        ResultAssert.Failure(service.DecryptToString(encoded, AssociatedData), CryptographyErrorCodes.MalformedPayload, ErrorType.Validation);
    }

    [Fact]
    public void DecryptToString_PlaintextNotUtf8_ReturnsMalformedPayload()
    {
        var service = new SynchronousAesGcmEncryptionService(TestKeys.SingleKeyProvider());
        EncryptedPayload payload = service.Encrypt([0xFF, 0xFE, 0xC3, 0x28], AssociatedData);

        ResultAssert.Failure(service.DecryptToString(payload.ToString(), AssociatedData), CryptographyErrorCodes.MalformedPayload, ErrorType.Validation);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    public void Encrypt_KeyNot32Bytes_Throws(int keyLength)
    {
        var service = new SynchronousAesGcmEncryptionService(TestKeys.SingleKeyProvider("short", keyLength));

        Assert.Throws<CryptographicException>(() => service.Encrypt([1], AssociatedData));
        Assert.Throws<CryptographicException>(() => service.EncryptToString("x", AssociatedData));
    }

    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    public void Decrypt_KeyNot32Bytes_Throws(int keyLength)
    {
        EncryptedPayload payload = new SynchronousAesGcmEncryptionService(TestKeys.SingleKeyProvider("k")).Encrypt([1], AssociatedData);
        var decryptor = new SynchronousAesGcmEncryptionService(TestKeys.SingleKeyProvider("k", keyLength));

        Assert.Throws<CryptographicException>(() => decryptor.Decrypt(payload, AssociatedData));
    }

    [Fact]
    public void Decrypt_SameKeyIdWithDifferentMaterial_ReturnsDecryptionFailed()
    {
        EncryptedPayload payload = new SynchronousAesGcmEncryptionService(TestKeys.SingleKeyProvider("k")).Encrypt([1], AssociatedData);
        var decryptor = new SynchronousAesGcmEncryptionService(TestKeys.SingleKeyProvider("k"));

        ResultAssert.Failure(decryptor.Decrypt(payload, AssociatedData), CryptographyErrorCodes.DecryptionFailed, ErrorType.Validation);
    }

    [Fact]
    public async Task SynchronousPayload_DecryptsWithAsynchronousService()
    {
        StaticEncryptionKeyProvider provider = TestKeys.SingleKeyProvider();
        var syncService = new SynchronousAesGcmEncryptionService(provider);
        var asyncService = new AesGcmEncryptionService(provider);

        EncryptedPayload payload = syncService.Encrypt([4, 5, 6], AssociatedData);
        string encoded = syncService.EncryptToString("text", AssociatedData);

        Assert.Equal([4, 5, 6], (await asyncService.DecryptAsync(payload, AssociatedData)).Value);
        Assert.Equal("text", (await asyncService.DecryptToStringAsync(encoded, AssociatedData)).Value);
    }

    [Fact]
    public async Task AsynchronousPayload_DecryptsWithSynchronousService()
    {
        StaticEncryptionKeyProvider provider = TestKeys.SingleKeyProvider();
        var syncService = new SynchronousAesGcmEncryptionService(provider);
        var asyncService = new AesGcmEncryptionService(provider);

        EncryptedPayload payload = await asyncService.EncryptAsync(new byte[] { 4, 5, 6 }, AssociatedData);
        string encoded = await asyncService.EncryptToStringAsync("text", AssociatedData);

        Assert.Equal([4, 5, 6], syncService.Decrypt(payload, AssociatedData).Value);
        Assert.Equal("text", syncService.DecryptToString(encoded, AssociatedData).Value);
    }

    [Fact]
    public void Decrypt_AfterRotation_OldPayloadStillDecrypts()
    {
        CryptographicKey oldKey = TestKeys.Create("2026-03");
        var before = new SynchronousAesGcmEncryptionService(TestKeys.Provider("2026-03", oldKey));
        var after = new SynchronousAesGcmEncryptionService(TestKeys.Provider("2026-09", oldKey, TestKeys.Create("2026-09")));

        EncryptedPayload oldPayload = before.Encrypt([7], AssociatedData);

        Assert.Equal([7], after.Decrypt(oldPayload, AssociatedData).Value);
        Assert.True(before.IsEncryptedWithCurrentKey(oldPayload));
        Assert.False(after.IsEncryptedWithCurrentKey(oldPayload));
    }

    [Fact]
    public void ReEncrypt_PayloadUsesCurrentKey_ReturnsSameInstance()
    {
        var service = new SynchronousAesGcmEncryptionService(TestKeys.SingleKeyProvider());
        EncryptedPayload payload = service.Encrypt([1], AssociatedData);

        Assert.Same(payload, service.ReEncrypt(payload, AssociatedData).Value);
    }

    [Fact]
    public void ReEncrypt_PayloadUsesOldKey_ReturnsPayloadUnderCurrentKey()
    {
        CryptographicKey oldKey = TestKeys.Create("2026-03");
        var before = new SynchronousAesGcmEncryptionService(TestKeys.Provider("2026-03", oldKey));
        var after = new SynchronousAesGcmEncryptionService(TestKeys.Provider("2026-09", oldKey, TestKeys.Create("2026-09")));
        EncryptedPayload oldPayload = before.Encrypt([1, 2, 3], AssociatedData);

        Result<EncryptedPayload> result = after.ReEncrypt(oldPayload, AssociatedData);

        Assert.Equal("2026-09", result.Value.KeyId);
        Assert.True(after.IsEncryptedWithCurrentKey(result.Value));
        Assert.Equal([1, 2, 3], after.Decrypt(result.Value, AssociatedData).Value);
    }

    [Fact]
    public void ReEncrypt_AssociatedDataMismatch_ReturnsDecryptionFailed()
    {
        CryptographicKey oldKey = TestKeys.Create("2026-03");
        var before = new SynchronousAesGcmEncryptionService(TestKeys.Provider("2026-03", oldKey));
        var after = new SynchronousAesGcmEncryptionService(TestKeys.Provider("2026-09", oldKey, TestKeys.Create("2026-09")));
        EncryptedPayload oldPayload = before.Encrypt([1], AssociatedData);

        ResultAssert.Failure(after.ReEncrypt(oldPayload, "other"u8), CryptographyErrorCodes.DecryptionFailed, ErrorType.Validation);
    }

    [Fact]
    public void NullArguments_Throw()
    {
        var service = new SynchronousAesGcmEncryptionService(TestKeys.SingleKeyProvider());

        Assert.Throws<ArgumentNullException>(() => new SynchronousAesGcmEncryptionService(null!));
        Assert.Throws<ArgumentNullException>(() => service.Decrypt(null!, AssociatedData));
        Assert.Throws<ArgumentNullException>(() => service.EncryptToString(null!, AssociatedData));
        Assert.Throws<ArgumentNullException>(() => service.DecryptToString(null!, AssociatedData));
        Assert.Throws<ArgumentNullException>(() => service.IsEncryptedWithCurrentKey(null!));
        Assert.Throws<ArgumentNullException>(() => service.ReEncrypt(null!, AssociatedData));
    }
}
