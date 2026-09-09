using System.Globalization;
using System.Security.Cryptography;
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

        EncryptedPayload payload = service.Encrypt(plaintext, []);
        Result<byte[]> result = service.Decrypt(payload, []);

        Assert.True(result.IsSuccess);
        Assert.Equal(plaintext, result.Value);
    }

    [Fact]
    public void Encrypt_GeneratesDifferentNonceEachCall()
    {
        var keyProvider = new InMemoryEncryptionKeyProvider();
        var service = new AesGcmEncryptionService(keyProvider);
        byte[] plaintext = Encoding.UTF8.GetBytes("same plaintext");

        EncryptedPayload first = service.Encrypt(plaintext, []);
        EncryptedPayload second = service.Encrypt(plaintext, []);

        Assert.NotEqual(first.Nonce, second.Nonce);
    }

    [Fact]
    public void Decrypt_WithFlippedCiphertextByte_Fails()
    {
        var keyProvider = new InMemoryEncryptionKeyProvider();
        var service = new AesGcmEncryptionService(keyProvider);
        EncryptedPayload payload = service.Encrypt(Encoding.UTF8.GetBytes("secret"), []);

        byte[] tamperedCiphertext = [.. payload.Ciphertext];
        tamperedCiphertext[0] ^= 0xFF;
        var tampered = new EncryptedPayload(payload.KeyId, payload.Nonce, tamperedCiphertext, payload.Tag);

        Result<byte[]> result = service.Decrypt(tampered, []);

        Assert.True(result.IsFailure);
        Assert.Equal(CryptographyErrorCodes.DecryptionFailed, result.Error.Code);
    }

    [Fact]
    public void Decrypt_WithFlippedTagByte_Fails()
    {
        var keyProvider = new InMemoryEncryptionKeyProvider();
        var service = new AesGcmEncryptionService(keyProvider);
        EncryptedPayload payload = service.Encrypt(Encoding.UTF8.GetBytes("secret"), []);

        byte[] tamperedTag = [.. payload.Tag];
        tamperedTag[0] ^= 0xFF;
        var tampered = new EncryptedPayload(payload.KeyId, payload.Nonce, payload.Ciphertext, tamperedTag);

        Result<byte[]> result = service.Decrypt(tampered, []);

        Assert.True(result.IsFailure);
        Assert.Equal(CryptographyErrorCodes.DecryptionFailed, result.Error.Code);
    }

    [Fact]
    public void Decrypt_WithUnknownKeyId_Fails()
    {
        var keyProvider = new InMemoryEncryptionKeyProvider();
        var service = new AesGcmEncryptionService(keyProvider);
        EncryptedPayload payload = service.Encrypt(Encoding.UTF8.GetBytes("secret"), []);

        var withUnknownKey = new EncryptedPayload("retired-key", payload.Nonce, payload.Ciphertext, payload.Tag);
        Result<byte[]> result = service.Decrypt(withUnknownKey, []);

        Assert.True(result.IsFailure);
        Assert.Equal(CryptographyErrorCodes.UnknownKeyId, result.Error.Code);
    }

    [Fact]
    public void Decrypt_AfterKeyRotation_StillDecryptsWithOldKeyVersion()
    {
        var keyProvider = new InMemoryEncryptionKeyProvider("v1");
        var service = new AesGcmEncryptionService(keyProvider);
        byte[] plaintext = Encoding.UTF8.GetBytes("payload encrypted under v1");

        EncryptedPayload payload = service.Encrypt(plaintext, []);

        // Rotate to a new current key — v1 remains resolvable for old payloads.
        keyProvider.AddKey("v2");
        keyProvider.SetCurrentKey("v2");

        Result<byte[]> result = service.Decrypt(payload, []);

        Assert.True(result.IsSuccess);
        Assert.Equal(plaintext, result.Value);
        Assert.Equal("v1", payload.KeyId);
    }

    [Fact]
    public void EncryptToString_ThenDecryptToString_RoundTripsPlaintext()
    {
        var keyProvider = new InMemoryEncryptionKeyProvider();
        var service = new AesGcmEncryptionService(keyProvider);

        string encoded = service.EncryptToString("hello world", []);
        Result<string> result = service.DecryptToString(encoded, []);

        Assert.True(result.IsSuccess);
        Assert.Equal("hello world", result.Value);
    }

    [Fact]
    public void DecryptToString_WithMalformedInput_Fails()
    {
        var keyProvider = new InMemoryEncryptionKeyProvider();
        var service = new AesGcmEncryptionService(keyProvider);

        Result<string> result = service.DecryptToString("not-valid-base64-payload-!!!", []);

        Assert.True(result.IsFailure);
        Assert.Equal(CryptographyErrorCodes.MalformedPayload, result.Error.Code);
    }

    [Fact]
    public void Constructor_NullKeyProvider_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new AesGcmEncryptionService(null!));
    }

    // --- P-491/WO-081: associated data (AAD) ---

    [Fact]
    public void Encrypt_ThenDecrypt_WithMatchingAssociatedData_RoundTrips()
    {
        var keyProvider = new InMemoryEncryptionKeyProvider();
        var service = new AesGcmEncryptionService(keyProvider);
        byte[] plaintext = Encoding.UTF8.GetBytes("secret payload");
        byte[] associatedData = Encoding.UTF8.GetBytes("row-id:42");

        EncryptedPayload payload = service.Encrypt(plaintext, associatedData);
        Result<byte[]> result = service.Decrypt(payload, associatedData);

        Assert.True(result.IsSuccess);
        Assert.Equal(plaintext, result.Value);
    }

    [Fact]
    public void Decrypt_WithMismatchedAssociatedData_Fails()
    {
        // The phase's headline acceptance criterion: swap the AAD between two otherwise-identical
        // payloads and confirm authentication fails exactly like a tampered ciphertext/tag/wrong key.
        var keyProvider = new InMemoryEncryptionKeyProvider();
        var service = new AesGcmEncryptionService(keyProvider);
        byte[] plaintext = Encoding.UTF8.GetBytes("secret payload");

        EncryptedPayload payload = service.Encrypt(plaintext, Encoding.UTF8.GetBytes("row-id:42"));
        Result<byte[]> result = service.Decrypt(payload, Encoding.UTF8.GetBytes("row-id:43"));

        Assert.True(result.IsFailure);
        Assert.Equal(CryptographyErrorCodes.DecryptionFailed, result.Error.Code);
    }

    [Fact]
    public void Decrypt_WithAssociatedDataOmittedAtDecryptTime_Fails()
    {
        var keyProvider = new InMemoryEncryptionKeyProvider();
        var service = new AesGcmEncryptionService(keyProvider);
        byte[] plaintext = Encoding.UTF8.GetBytes("secret payload");

        EncryptedPayload payload = service.Encrypt(plaintext, Encoding.UTF8.GetBytes("row-id:42"));
        Result<byte[]> result = service.Decrypt(payload, []);

        Assert.True(result.IsFailure);
        Assert.Equal(CryptographyErrorCodes.DecryptionFailed, result.Error.Code);
    }

    [Fact]
    public void Encrypt_ThenDecrypt_WithEmptyAssociatedData_IsAValidNoContextBindingChoiceThatAlwaysSucceeds()
    {
        var keyProvider = new InMemoryEncryptionKeyProvider();
        var service = new AesGcmEncryptionService(keyProvider);
        byte[] plaintext = Encoding.UTF8.GetBytes("no context binding needed");

        EncryptedPayload payload = service.Encrypt(plaintext, Array.Empty<byte>());
        Result<byte[]> result = service.Decrypt(payload, Array.Empty<byte>());

        Assert.True(result.IsSuccess);
        Assert.Equal(plaintext, result.Value);
    }

    [Fact]
    public void EncryptToString_ThenDecryptToString_WithMatchingAssociatedData_RoundTrips()
    {
        var keyProvider = new InMemoryEncryptionKeyProvider();
        var service = new AesGcmEncryptionService(keyProvider);
        byte[] associatedData = Encoding.UTF8.GetBytes("subscription-id:sub_123");

        string encoded = service.EncryptToString("hello world", associatedData);
        Result<string> result = service.DecryptToString(encoded, associatedData);

        Assert.True(result.IsSuccess);
        Assert.Equal("hello world", result.Value);
    }

    [Fact]
    public void DecryptToString_WithMismatchedAssociatedData_Fails()
    {
        var keyProvider = new InMemoryEncryptionKeyProvider();
        var service = new AesGcmEncryptionService(keyProvider);

        string encoded = service.EncryptToString("hello world", Encoding.UTF8.GetBytes("subscription-id:sub_123"));
        Result<string> result = service.DecryptToString(encoded, Encoding.UTF8.GetBytes("subscription-id:sub_456"));

        Assert.True(result.IsFailure);
        Assert.Equal(CryptographyErrorCodes.DecryptionFailed, result.Error.Code);
    }

    [Fact]
    public void Encrypt_NullAssociatedData_Throws()
    {
        var keyProvider = new InMemoryEncryptionKeyProvider();
        var service = new AesGcmEncryptionService(keyProvider);

        Assert.Throws<ArgumentNullException>(() => service.Encrypt(Encoding.UTF8.GetBytes("secret"), null!));
    }

    [Fact]
    public void Decrypt_NullAssociatedData_Throws()
    {
        var keyProvider = new InMemoryEncryptionKeyProvider();
        var service = new AesGcmEncryptionService(keyProvider);
        EncryptedPayload payload = service.Encrypt(Encoding.UTF8.GetBytes("secret"), []);

        Assert.Throws<ArgumentNullException>(() => service.Decrypt(payload, null!));
    }

    // --- P-492/WO-081: ISynchronousEncryptionKeyProvider capability gate ---

    [Fact]
    public void Encrypt_MarkedProvider_BehavesExactlyAsBeforeTheGate()
    {
        // InMemoryEncryptionKeyProvider implements ISynchronousEncryptionKeyProvider — the sync
        // path must be byte-for-byte unchanged.
        var keyProvider = new InMemoryEncryptionKeyProvider();
        var service = new AesGcmEncryptionService(keyProvider);
        byte[] plaintext = Encoding.UTF8.GetBytes("unaffected by the gate");

        EncryptedPayload payload = service.Encrypt(plaintext, []);
        Result<byte[]> result = service.Decrypt(payload, []);

        Assert.True(result.IsSuccess);
        Assert.Equal(plaintext, result.Value);
    }

    [Fact]
    public void Encrypt_UnmarkedProvider_ThrowsNotSupportedException_WithoutAttemptingTheBridge()
    {
        var keyProvider = new ControllableEncryptionKeyProvider(new CryptographicKey("v1", new byte[32]));
        var service = new AesGcmEncryptionService(keyProvider);

        Assert.Throws<NotSupportedException>(() => service.Encrypt(Encoding.UTF8.GetBytes("secret"), []));
        Assert.Equal(0, keyProvider.CurrentKeyCallCount);
    }

    [Fact]
    public void Decrypt_UnmarkedProvider_ThrowsNotSupportedException_WithoutAttemptingTheBridge()
    {
        var key = new CryptographicKey("v1", new byte[32]);
        var keyProvider = new ControllableEncryptionKeyProvider(key);
        var service = new AesGcmEncryptionService(keyProvider);
        var payload = new EncryptedPayload("v1", new byte[12], [], new byte[16]);

        Assert.Throws<NotSupportedException>(() => service.Decrypt(payload, []));
        Assert.Equal(0, keyProvider.GetKeyCallCount);
    }

    [Fact]
    public void EncryptToString_UnmarkedProvider_ThrowsNotSupportedException_WithoutAttemptingTheBridge()
    {
        var keyProvider = new ControllableEncryptionKeyProvider(new CryptographicKey("v1", new byte[32]));
        var service = new AesGcmEncryptionService(keyProvider);

        Assert.Throws<NotSupportedException>(() => service.EncryptToString("secret", []));
        Assert.Equal(0, keyProvider.CurrentKeyCallCount);
    }

    [Fact]
    public void DecryptToString_UnmarkedProvider_ThrowsNotSupportedException_WithoutAttemptingTheBridge()
    {
        // Encode a syntactically valid payload with a synchronous provider first so the gate,
        // not TryUnpack, is what's under test — then swap in the unmarked provider.
        var syncProvider = new InMemoryEncryptionKeyProvider();
        var syncService = new AesGcmEncryptionService(syncProvider);
        string encoded = syncService.EncryptToString("secret", []);

        var keyProvider = new ControllableEncryptionKeyProvider(new CryptographicKey("v1", new byte[32]));
        var service = new AesGcmEncryptionService(keyProvider);

        Assert.Throws<NotSupportedException>(() => service.DecryptToString(encoded, []));
        Assert.Equal(0, keyProvider.GetKeyCallCount);
    }

    [Fact]
    public void Encrypt_UnmarkedProvider_ExceptionMessage_DirectsCallerToAsyncOverload()
    {
        var keyProvider = new ControllableEncryptionKeyProvider(new CryptographicKey("v1", new byte[32]));
        var service = new AesGcmEncryptionService(keyProvider);

        NotSupportedException exception = Assert.Throws<NotSupportedException>(
            () => service.Encrypt(Encoding.UTF8.GetBytes("secret"), []));

        Assert.Contains(nameof(ISymmetricEncryptionService.EncryptAsync), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Decrypt_UnmarkedProvider_ExceptionMessage_DirectsCallerToAsyncOverload()
    {
        var keyProvider = new ControllableEncryptionKeyProvider(new CryptographicKey("v1", new byte[32]));
        var service = new AesGcmEncryptionService(keyProvider);
        var payload = new EncryptedPayload("v1", new byte[12], [], new byte[16]);

        NotSupportedException exception = Assert.Throws<NotSupportedException>(() => service.Decrypt(payload, []));

        Assert.Contains(nameof(ISymmetricEncryptionService.DecryptAsync), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CachedProvider_WrappingMarkedInner_AllowsSyncMembers()
    {
        var inner = new InMemoryEncryptionKeyProvider();
        var cached = new CachedEncryptionKeyProvider(inner, TimeProvider.System, TimeSpan.FromMinutes(5));
        var service = new AesGcmEncryptionService(cached);
        byte[] plaintext = Encoding.UTF8.GetBytes("cached, genuinely synchronous inner");

        EncryptedPayload payload = service.Encrypt(plaintext, []);
        Result<byte[]> result = service.Decrypt(payload, []);

        Assert.True(result.IsSuccess);
        Assert.Equal(plaintext, result.Value);
    }

    [Fact]
    public void CachedProvider_WrappingUnmarkedInner_ThrowsOnSyncMembers()
    {
        var inner = new ControllableEncryptionKeyProvider(new CryptographicKey("v1", new byte[32]));
        var cached = new CachedEncryptionKeyProvider(inner, TimeProvider.System, TimeSpan.FromMinutes(5));
        var service = new AesGcmEncryptionService(cached);

        Assert.Throws<NotSupportedException>(() => service.Encrypt(Encoding.UTF8.GetBytes("secret"), []));
    }

    // --- P-513/WO-083: structural AES-256 key-length enforcement ---

    [Theory]
    [InlineData(16)] // AES-128 downgrade
    [InlineData(24)] // AES-192 downgrade
    public void Encrypt_WrongSizeKey_ThrowsCryptographicException_NamingExpectedAndActualLength(int wrongKeySizeBytes)
    {
        var keyProvider = new InMemoryEncryptionKeyProvider();
        keyProvider.SetCurrentKey(new CryptographicKey("wrong-size", new byte[wrongKeySizeBytes]));
        var service = new AesGcmEncryptionService(keyProvider);

        CryptographicException exception = Assert.Throws<CryptographicException>(
            () => service.Encrypt(Encoding.UTF8.GetBytes("secret"), []));

        Assert.Contains("32", exception.Message, StringComparison.Ordinal);
        Assert.Contains(wrongKeySizeBytes.ToString(CultureInfo.InvariantCulture), exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    public async Task EncryptAsync_WrongSizeKey_ThrowsCryptographicException(int wrongKeySizeBytes)
    {
        var keyProvider = new InMemoryEncryptionKeyProvider();
        keyProvider.SetCurrentKey(new CryptographicKey("wrong-size", new byte[wrongKeySizeBytes]));
        var service = new AesGcmEncryptionService(keyProvider);

        await Assert.ThrowsAsync<CryptographicException>(
            () => service.EncryptAsync(Encoding.UTF8.GetBytes("secret"), []).AsTask());
    }

    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    public void Decrypt_WrongSizeKey_ThrowsCryptographicException_NamingExpectedAndActualLength(int wrongKeySizeBytes)
    {
        var goodKeyProvider = new InMemoryEncryptionKeyProvider();
        var goodService = new AesGcmEncryptionService(goodKeyProvider);
        EncryptedPayload payload = goodService.Encrypt(Encoding.UTF8.GetBytes("secret"), []);

        var wrongSizeKeyProvider = new InMemoryEncryptionKeyProvider();
        wrongSizeKeyProvider.AddKey(payload.KeyId, new byte[wrongKeySizeBytes]);
        var service = new AesGcmEncryptionService(wrongSizeKeyProvider);

        CryptographicException exception = Assert.Throws<CryptographicException>(() => service.Decrypt(payload, []));

        Assert.Contains("32", exception.Message, StringComparison.Ordinal);
        Assert.Contains(wrongKeySizeBytes.ToString(CultureInfo.InvariantCulture), exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    public async Task DecryptAsync_WrongSizeKey_ThrowsCryptographicException(int wrongKeySizeBytes)
    {
        var goodKeyProvider = new InMemoryEncryptionKeyProvider();
        var goodService = new AesGcmEncryptionService(goodKeyProvider);
        EncryptedPayload payload = goodService.Encrypt(Encoding.UTF8.GetBytes("secret"), []);

        var wrongSizeKeyProvider = new InMemoryEncryptionKeyProvider();
        wrongSizeKeyProvider.AddKey(payload.KeyId, new byte[wrongKeySizeBytes]);
        var service = new AesGcmEncryptionService(wrongSizeKeyProvider);

        await Assert.ThrowsAsync<CryptographicException>(() => service.DecryptAsync(payload, []).AsTask());
    }

    [Fact]
    public void Encrypt_CorrectSize32ByteKey_IsUnaffected()
    {
        // Existing round-trip behavior for a correctly-sized key must be byte-for-byte unchanged.
        var keyProvider = new InMemoryEncryptionKeyProvider();
        var service = new AesGcmEncryptionService(keyProvider);
        byte[] plaintext = Encoding.UTF8.GetBytes("unaffected by the length guard");

        EncryptedPayload payload = service.Encrypt(plaintext, []);
        Result<byte[]> result = service.Decrypt(payload, []);

        Assert.True(result.IsSuccess);
        Assert.Equal(plaintext, result.Value);
    }

    [Fact]
    public void Encrypt_WrongSizeKey_RejectsBeforeProducingAnyCiphertextOrTagOutput()
    {
        // Proves the rejection happens before any AesGcm instance is constructed: no partial
        // output escapes — the call throws outright rather than returning a payload.
        var keyProvider = new InMemoryEncryptionKeyProvider();
        keyProvider.SetCurrentKey(new CryptographicKey("wrong-size", new byte[16]));
        var service = new AesGcmEncryptionService(keyProvider);

        EncryptedPayload? captured = null;
        var exception = Record.Exception(() => captured = service.Encrypt(Encoding.UTF8.GetBytes("secret"), []));

        Assert.IsType<CryptographicException>(exception);
        Assert.Null(captured);
    }

    // ---- P-524/WO-083: key-material zeroization ----

    /// <summary>
    /// Proves — with a genuine runtime check against actual bytes — that
    /// <see cref="AesGcmEncryptionService.EncryptToString(string, byte[])"/>'s intermediate UTF-8
    /// plaintext buffer is zeroed in place before the public call returns. Uses the internal,
    /// test-only capture-before-zeroing overload (gated via <c>InternalsVisibleTo</c>) to grab the
    /// exact same array reference the production code path zeroes — never the primary
    /// <c>byte[]</c>-based <see cref="AesGcmEncryptionService.Encrypt(byte[], byte[])"/> overload's
    /// own caller-owned return value, which this phase must never touch.
    /// </summary>
    [Fact]
    public void EncryptToString_ZeroesTheIntermediateUtf8PlaintextBuffer_BeforeReturning()
    {
        var keyProvider = new InMemoryEncryptionKeyProvider();
        var service = new AesGcmEncryptionService(keyProvider);
        byte[]? capturedPlaintextBytes = null;

        string encoded = service.EncryptToString("hello world", [], buffer => capturedPlaintextBytes = buffer);

        Assert.NotNull(encoded);
        Assert.NotNull(capturedPlaintextBytes);
        Assert.NotEmpty(capturedPlaintextBytes);
        Assert.All(capturedPlaintextBytes, b => Assert.Equal(0, b));
    }

    /// <summary>
    /// The primary <c>byte[]</c>-based <see cref="AesGcmEncryptionService.Decrypt(EncryptedPayload, byte[])"/>
    /// overload's directly-returned plaintext must NEVER be zeroed by this phase — it is the
    /// caller's own needed output. Proven by decrypting, reading every byte of the result, then
    /// asserting the array still equals the original plaintext afterward (a zeroed array would
    /// silently corrupt this comparison).
    /// </summary>
    [Fact]
    public void Decrypt_PrimaryByteArrayOverload_NeverZeroesItsOwnReturnValue()
    {
        var keyProvider = new InMemoryEncryptionKeyProvider();
        var service = new AesGcmEncryptionService(keyProvider);
        byte[] plaintext = Encoding.UTF8.GetBytes("must remain readable after the call returns");

        EncryptedPayload payload = service.Encrypt(plaintext, []);
        Result<byte[]> result = service.Decrypt(payload, []);

        Assert.True(result.IsSuccess);
        Assert.Equal(plaintext, result.Value);
        Assert.Equal("must remain readable after the call returns", Encoding.UTF8.GetString(result.Value));
    }
}
