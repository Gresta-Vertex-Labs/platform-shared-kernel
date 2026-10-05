using System.Security.Cryptography;
using System.Text;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Cryptography.Tests.TestDoubles;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Cryptography.Tests.Symmetric;

public sealed class AesGcmEncryptionServiceTests
{
    private static readonly byte[] AssociatedData = Encoding.UTF8.GetBytes("tenant-42/row-7");

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1024)]
    [InlineData(1 << 20)]
    public async Task EncryptAsync_DecryptAsync_RoundTrips(int length)
    {
        var service = new AesGcmEncryptionService(TestKeys.SingleKeyProvider());
        byte[] plaintext = RandomNumberGenerator.GetBytes(length);

        EncryptedPayload payload = await service.EncryptAsync(plaintext, AssociatedData);
        Result<byte[]> decrypted = await service.DecryptAsync(payload, AssociatedData);

        Assert.True(decrypted.IsSuccess);
        Assert.Equal(plaintext, decrypted.Value);
        Assert.Equal("key-1", payload.KeyId);
        Assert.Equal(length, payload.Ciphertext.Length);
    }

    [Fact]
    public async Task EncryptAsync_EmptyAssociatedData_RoundTrips()
    {
        var service = new AesGcmEncryptionService(TestKeys.SingleKeyProvider());

        EncryptedPayload payload = await service.EncryptAsync(new byte[] { 1, 2, 3 }, ReadOnlyMemory<byte>.Empty);
        Result<byte[]> decrypted = await service.DecryptAsync(payload, ReadOnlyMemory<byte>.Empty);

        Assert.Equal([1, 2, 3], decrypted.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("Şifreli değer — 🎉")]
    public async Task EncryptToStringAsync_DecryptToStringAsync_RoundTrips(string plaintext)
    {
        var service = new AesGcmEncryptionService(TestKeys.SingleKeyProvider());

        string encoded = await service.EncryptToStringAsync(plaintext, AssociatedData);
        Result<string> decrypted = await service.DecryptToStringAsync(encoded, AssociatedData);

        Assert.True(decrypted.IsSuccess);
        Assert.Equal(plaintext, decrypted.Value);
        Assert.True(EncryptedPayload.TryParse(encoded, out _));
    }

    [Fact]
    public async Task EncryptAsync_SamePlaintextTwice_UsesDifferentNonces()
    {
        var service = new AesGcmEncryptionService(TestKeys.SingleKeyProvider());
        byte[] plaintext = Encoding.UTF8.GetBytes("same");

        EncryptedPayload first = await service.EncryptAsync(plaintext, AssociatedData);
        EncryptedPayload second = await service.EncryptAsync(plaintext, AssociatedData);

        Assert.NotEqual(first.Nonce.ToArray(), second.Nonce.ToArray());
        Assert.NotEqual(first.Ciphertext.ToArray(), second.Ciphertext.ToArray());
    }

    [Fact]
    public async Task EncryptAsync_ProducesStandardAesGcmCiphertext()
    {
        CryptographicKey key = TestKeys.Create("key-1");
        var service = new AesGcmEncryptionService(new StaticEncryptionKeyProvider("key-1", [key]));
        byte[] plaintext = Encoding.UTF8.GetBytes("interop");

        EncryptedPayload payload = await service.EncryptAsync(plaintext, AssociatedData);

        byte[] decrypted = new byte[payload.Ciphertext.Length];
        using var aes = new AesGcm(key.Material, 16);
        aes.Decrypt(payload.Nonce, payload.Ciphertext, payload.Tag, decrypted, AssociatedData);
        Assert.Equal(plaintext, decrypted);
    }

    [Theory]
    [InlineData("tenant-42/row-8")]
    [InlineData("")]
    [InlineData("tenant-42/row-7 ")]
    public async Task DecryptAsync_DifferentAssociatedData_ReturnsDecryptionFailed(string otherAssociatedData)
    {
        var service = new AesGcmEncryptionService(TestKeys.SingleKeyProvider());
        EncryptedPayload payload = await service.EncryptAsync(new byte[] { 1, 2, 3 }, AssociatedData);

        Result<byte[]> decrypted = await service.DecryptAsync(payload, Encoding.UTF8.GetBytes(otherAssociatedData));

        ResultAssert.Failure(decrypted, CryptographyErrorCodes.DecryptionFailed, ErrorType.Validation);
    }

    [Theory]
    [InlineData(PayloadPart.Nonce)]
    [InlineData(PayloadPart.Ciphertext)]
    [InlineData(PayloadPart.Tag)]
    public async Task DecryptAsync_TamperedPayload_ReturnsDecryptionFailed(PayloadPart part)
    {
        var service = new AesGcmEncryptionService(TestKeys.SingleKeyProvider());
        EncryptedPayload payload = await service.EncryptAsync(Encoding.UTF8.GetBytes("do not alter"), AssociatedData);

        Result<byte[]> decrypted = await service.DecryptAsync(Tamper(payload, part), AssociatedData);

        ResultAssert.Failure(decrypted, CryptographyErrorCodes.DecryptionFailed, ErrorType.Validation);
    }

    [Fact]
    public async Task DecryptToStringAsync_TamperedEncodedPayload_ReturnsDecryptionFailed()
    {
        var service = new AesGcmEncryptionService(TestKeys.SingleKeyProvider());
        string encoded = await service.EncryptToStringAsync("do not alter", AssociatedData);
        EncryptedPayload.TryParse(encoded, out EncryptedPayload? payload);

        Result<string> decrypted = await service.DecryptToStringAsync(Tamper(payload!, PayloadPart.Ciphertext).ToString(), AssociatedData);

        ResultAssert.Failure(decrypted, CryptographyErrorCodes.DecryptionFailed, ErrorType.Validation);
    }

    [Fact]
    public async Task DecryptAsync_UnknownKeyId_ReturnsUnknownKeyIdWithoutRevealingId()
    {
        const string keyId = "retired-key-7f3a9c";
        var encryptor = new AesGcmEncryptionService(TestKeys.SingleKeyProvider(keyId));
        var decryptor = new AesGcmEncryptionService(TestKeys.SingleKeyProvider("other-key"));
        EncryptedPayload payload = await encryptor.EncryptAsync(new byte[] { 1 }, AssociatedData);

        Result<byte[]> decrypted = await decryptor.DecryptAsync(payload, AssociatedData);

        ResultAssert.Failure(decrypted, CryptographyErrorCodes.UnknownKeyId, ErrorType.Unexpected);
        Assert.DoesNotContain(keyId, decrypted.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DecryptToStringAsync_UnknownKeyId_ReturnsUnknownKeyId()
    {
        var encryptor = new AesGcmEncryptionService(TestKeys.SingleKeyProvider("a"));
        var decryptor = new AesGcmEncryptionService(TestKeys.SingleKeyProvider("b"));
        string encoded = await encryptor.EncryptToStringAsync("text", AssociatedData);

        Result<string> decrypted = await decryptor.DecryptToStringAsync(encoded, AssociatedData);

        ResultAssert.Failure(decrypted, CryptographyErrorCodes.UnknownKeyId, ErrorType.Unexpected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("AQ")]
    [InlineData("AgVrZXktMQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task DecryptToStringAsync_MalformedInput_ReturnsMalformedPayload(string encoded)
    {
        var service = new AesGcmEncryptionService(TestKeys.SingleKeyProvider());

        Result<string> decrypted = await service.DecryptToStringAsync(encoded, AssociatedData);

        ResultAssert.Failure(decrypted, CryptographyErrorCodes.MalformedPayload, ErrorType.Validation);
    }

    [Theory]
    [InlineData("not a payload")]
    [InlineData("@@@@")]
    public async Task DecryptToStringAsync_CharactersOutsideBase64UrlAlphabet_ReturnsMalformedPayload(string encoded)
    {
        var service = new AesGcmEncryptionService(TestKeys.SingleKeyProvider());

        Result<string> decrypted = await service.DecryptToStringAsync(encoded, AssociatedData);

        ResultAssert.Failure(decrypted, CryptographyErrorCodes.MalformedPayload, ErrorType.Validation);
    }

    [Fact]
    public async Task DecryptToStringAsync_PlaintextNotUtf8_ReturnsMalformedPayload()
    {
        var service = new AesGcmEncryptionService(TestKeys.SingleKeyProvider());
        EncryptedPayload payload = await service.EncryptAsync(new byte[] { 0xFF, 0xFE, 0xC3, 0x28 }, AssociatedData);

        Result<string> decrypted = await service.DecryptToStringAsync(payload.ToString(), AssociatedData);

        ResultAssert.Failure(decrypted, CryptographyErrorCodes.MalformedPayload, ErrorType.Validation);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(31)]
    [InlineData(64)]
    public async Task EncryptAsync_KeyNot32Bytes_Throws(int keyLength)
    {
        var service = new AesGcmEncryptionService(TestKeys.SingleKeyProvider("short", keyLength));

        await Assert.ThrowsAsync<CryptographicException>(async () => await service.EncryptAsync(new byte[] { 1 }, AssociatedData));
        await Assert.ThrowsAsync<CryptographicException>(async () => await service.EncryptToStringAsync("x", AssociatedData));
    }

    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    public async Task DecryptAsync_KeyNot32Bytes_Throws(int keyLength)
    {
        var encryptor = new AesGcmEncryptionService(TestKeys.SingleKeyProvider("k"));
        var decryptor = new AesGcmEncryptionService(TestKeys.SingleKeyProvider("k", keyLength));
        EncryptedPayload payload = await encryptor.EncryptAsync(new byte[] { 1 }, AssociatedData);

        await Assert.ThrowsAsync<CryptographicException>(async () => await decryptor.DecryptAsync(payload, AssociatedData));
        await Assert.ThrowsAsync<CryptographicException>(async () => await decryptor.DecryptToStringAsync(payload.ToString(), AssociatedData));
    }

    [Fact]
    public async Task DecryptAsync_SameKeyIdWithDifferentMaterial_ReturnsDecryptionFailed()
    {
        var encryptor = new AesGcmEncryptionService(TestKeys.SingleKeyProvider("k"));
        var decryptor = new AesGcmEncryptionService(TestKeys.SingleKeyProvider("k"));
        EncryptedPayload payload = await encryptor.EncryptAsync(new byte[] { 1, 2 }, AssociatedData);

        Result<byte[]> decrypted = await decryptor.DecryptAsync(payload, AssociatedData);

        ResultAssert.Failure(decrypted, CryptographyErrorCodes.DecryptionFailed, ErrorType.Validation);
    }

    [Fact]
    public async Task DecryptAsync_AfterRotation_OldPayloadStillDecrypts()
    {
        CryptographicKey oldKey = TestKeys.Create("2026-03");
        CryptographicKey newKey = TestKeys.Create("2026-09");
        var before = new AesGcmEncryptionService(TestKeys.Provider("2026-03", oldKey));
        var after = new AesGcmEncryptionService(TestKeys.Provider("2026-09", oldKey, newKey));
        EncryptedPayload oldPayload = await before.EncryptAsync(new byte[] { 9, 8, 7 }, AssociatedData);

        Result<byte[]> decrypted = await after.DecryptAsync(oldPayload, AssociatedData);
        EncryptedPayload newPayload = await after.EncryptAsync(new byte[] { 9, 8, 7 }, AssociatedData);

        Assert.Equal([9, 8, 7], decrypted.Value);
        Assert.Equal("2026-09", newPayload.KeyId);
    }

    [Fact]
    public async Task IsEncryptedWithCurrentKeyAsync_ReflectsCurrentKey()
    {
        CryptographicKey oldKey = TestKeys.Create("2026-03");
        CryptographicKey newKey = TestKeys.Create("2026-09");
        var before = new AesGcmEncryptionService(TestKeys.Provider("2026-03", oldKey));
        var after = new AesGcmEncryptionService(TestKeys.Provider("2026-09", oldKey, newKey));
        EncryptedPayload oldPayload = await before.EncryptAsync(new byte[] { 1 }, AssociatedData);
        EncryptedPayload newPayload = await after.EncryptAsync(new byte[] { 1 }, AssociatedData);

        Assert.True(await before.IsEncryptedWithCurrentKeyAsync(oldPayload));
        Assert.False(await after.IsEncryptedWithCurrentKeyAsync(oldPayload));
        Assert.True(await after.IsEncryptedWithCurrentKeyAsync(newPayload));
    }

    [Fact]
    public async Task ReEncryptAsync_PayloadUsesCurrentKey_ReturnsSameInstance()
    {
        var service = new AesGcmEncryptionService(TestKeys.SingleKeyProvider());
        EncryptedPayload payload = await service.EncryptAsync(new byte[] { 1 }, AssociatedData);

        Result<EncryptedPayload> result = await service.ReEncryptAsync(payload, AssociatedData);

        Assert.True(result.IsSuccess);
        Assert.Same(payload, result.Value);
    }

    [Fact]
    public async Task ReEncryptAsync_PayloadUsesOldKey_ReturnsPayloadUnderCurrentKey()
    {
        CryptographicKey oldKey = TestKeys.Create("2026-03");
        CryptographicKey newKey = TestKeys.Create("2026-09");
        var before = new AesGcmEncryptionService(TestKeys.Provider("2026-03", oldKey));
        var after = new AesGcmEncryptionService(TestKeys.Provider("2026-09", oldKey, newKey));
        byte[] plaintext = RandomNumberGenerator.GetBytes(100);
        EncryptedPayload oldPayload = await before.EncryptAsync(plaintext, AssociatedData);

        Result<EncryptedPayload> result = await after.ReEncryptAsync(oldPayload, AssociatedData);

        Assert.True(result.IsSuccess);
        Assert.Equal("2026-09", result.Value.KeyId);
        Assert.True(await after.IsEncryptedWithCurrentKeyAsync(result.Value));
        Assert.Equal(plaintext, (await after.DecryptAsync(result.Value, AssociatedData)).Value);
        Assert.True((await after.DecryptAsync(result.Value, ReadOnlyMemory<byte>.Empty)).IsFailure);
    }

    [Fact]
    public async Task ReEncryptAsync_AssociatedDataMismatch_ReturnsDecryptionFailed()
    {
        CryptographicKey oldKey = TestKeys.Create("2026-03");
        var before = new AesGcmEncryptionService(TestKeys.Provider("2026-03", oldKey));
        var after = new AesGcmEncryptionService(TestKeys.Provider("2026-09", oldKey, TestKeys.Create("2026-09")));
        EncryptedPayload oldPayload = await before.EncryptAsync(new byte[] { 1 }, AssociatedData);

        Result<EncryptedPayload> result = await after.ReEncryptAsync(oldPayload, Encoding.UTF8.GetBytes("other"));

        ResultAssert.Failure(result, CryptographyErrorCodes.DecryptionFailed, ErrorType.Validation);
    }

    [Fact]
    public async Task ReEncryptAsync_UnknownKey_ReturnsUnknownKeyId()
    {
        var encryptor = new AesGcmEncryptionService(TestKeys.SingleKeyProvider("gone"));
        var service = new AesGcmEncryptionService(TestKeys.SingleKeyProvider("current"));
        EncryptedPayload payload = await encryptor.EncryptAsync(new byte[] { 1 }, AssociatedData);

        Result<EncryptedPayload> result = await service.ReEncryptAsync(payload, AssociatedData);

        ResultAssert.Failure(result, CryptographyErrorCodes.UnknownKeyId, ErrorType.Unexpected);
    }

    [Fact]
    public async Task EveryMember_PassesCancellationTokenToKeyProvider()
    {
        CryptographicKey oldKey = TestKeys.Create("old");
        var encryptor = new AesGcmEncryptionService(TestKeys.Provider("old", oldKey));
        EncryptedPayload oldPayload = await encryptor.EncryptAsync(new byte[] { 1 }, AssociatedData);
        var recorder = new TokenRecordingKeyProvider(TestKeys.Provider("new", oldKey, TestKeys.Create("new")));
        var service = new AesGcmEncryptionService(recorder);
        using var cts = new CancellationTokenSource();
        CancellationToken token = cts.Token;

        EncryptedPayload payload = await service.EncryptAsync(new byte[] { 1 }, AssociatedData, token);
        await service.DecryptAsync(payload, AssociatedData, token);
        string encoded = await service.EncryptToStringAsync("x", AssociatedData, token);
        await service.DecryptToStringAsync(encoded, AssociatedData, token);
        await service.IsEncryptedWithCurrentKeyAsync(payload, token);
        await service.ReEncryptAsync(oldPayload, AssociatedData, token);

        Assert.Equal(7, recorder.Tokens.Count);
        Assert.All(recorder.Tokens, t => Assert.Equal(token, t));
    }

    [Fact]
    public async Task EncryptAsync_ProviderThrows_PropagatesException()
    {
        var provider = new ScriptedKeyProvider
        {
            OnGetCurrentKey = _ => throw new TimeoutException("key service unreachable"),
        };
        var service = new AesGcmEncryptionService(provider);

        await Assert.ThrowsAsync<TimeoutException>(async () => await service.EncryptAsync(new byte[] { 1 }, AssociatedData));
    }

    [Fact]
    public async Task NullArguments_Throw()
    {
        var service = new AesGcmEncryptionService(TestKeys.SingleKeyProvider());

        Assert.Throws<ArgumentNullException>(() => new AesGcmEncryptionService(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await service.DecryptAsync(null!, AssociatedData));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await service.EncryptToStringAsync(null!, AssociatedData));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await service.DecryptToStringAsync(null!, AssociatedData));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await service.IsEncryptedWithCurrentKeyAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(async () => await service.ReEncryptAsync(null!, AssociatedData));
    }

    public enum PayloadPart
    {
        Nonce,
        Ciphertext,
        Tag,
    }

    internal static EncryptedPayload Tamper(EncryptedPayload payload, PayloadPart part)
    {
        byte[] nonce = payload.Nonce.ToArray();
        byte[] ciphertext = payload.Ciphertext.ToArray();
        byte[] tag = payload.Tag.ToArray();

        byte[] target = part switch
        {
            PayloadPart.Nonce => nonce,
            PayloadPart.Ciphertext => ciphertext,
            _ => tag,
        };
        target[0] ^= 0x01;

        return new EncryptedPayload(payload.KeyId, nonce, ciphertext, tag);
    }
}
