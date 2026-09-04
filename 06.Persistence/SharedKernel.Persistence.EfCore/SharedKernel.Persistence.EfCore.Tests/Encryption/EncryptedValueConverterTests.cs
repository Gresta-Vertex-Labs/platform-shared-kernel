using FluentAssertions;
using Microsoft.Extensions.Options;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Persistence.EfCore.Encryption;
using SharedKernel.Persistence.EfCore.Options;

namespace SharedKernel.Persistence.EfCore.Tests.Encryption;

/// <summary>
/// Unit tests for <see cref="EncryptedValueConverter"/> — verifies AES-256-GCM round-trip,
/// pass-through mode, legacy plaintext handling, and key-not-found exception.
/// After P-227 the converter delegates crypto to <see cref="ISymmetricEncryptionService"/>;
/// tests use the real <see cref="AesGcmEncryptionService"/> with <see cref="EncryptionOptionsKeyProvider"/>.
/// </summary>
public sealed class EncryptedValueConverterTests
{
    private static IOptionsMonitor<EncryptionOptions> MakeMonitor(EncryptionOptions options)
        => new FixedOptionsMonitor(options);

    // Simple non-generic IOptionsMonitor implementation for test isolation.
    private sealed class FixedOptionsMonitor(EncryptionOptions value) : IOptionsMonitor<EncryptionOptions>
    {
        public EncryptionOptions CurrentValue => value;
        public EncryptionOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<EncryptionOptions, string?> listener) => null;
    }

    private static EncryptionOptions EnabledOptions(string version = "v1")
    {
        // 32-byte AES-256 key (all zeros — valid for testing)
        var keyBytes = new byte[32];
        var base64Key = Convert.ToBase64String(keyBytes);
        return new EncryptionOptions
        {
            Enabled = true,
            CurrentVersion = version,
            Keys = { [version] = base64Key }
        };
    }

    /// <summary>
    /// Builds a fully-wired <see cref="EncryptedValueConverter"/> using the real P-227 delegation chain:
    /// EncryptionOptionsKeyProvider → AesGcmEncryptionService → EncryptedValueConverter.
    /// </summary>
    private static EncryptedValueConverter MakeConverter(
        EncryptionOptions options,
        IEncryptionVersionOverride? versionOverride = null)
    {
        var monitor = MakeMonitor(options);
        var keyProvider = new EncryptionOptionsKeyProvider(monitor, versionOverride ?? EncryptionVersionOverride.NoOp, new EncryptionKeyByteCache(monitor));
        var encryptionService = new AesGcmEncryptionService(keyProvider);
        return new EncryptedValueConverter(monitor, encryptionService, versionOverride);
    }

    [Fact]
    public void Encrypt_Then_Decrypt_RoundTrips_Value()
    {
        // Arrange
        var converter = MakeConverter(EnabledOptions());

        const string plaintext = "sensitive-email@example.com";

        // Act — use the converter's ConvertToProvider / ConvertFromProvider expressions
        var encrypted = converter.ConvertToProviderExpression.Compile()(plaintext);
        var decrypted = converter.ConvertFromProviderExpression.Compile()(encrypted);

        // Assert
        // Format: "v{version}:{payload}" — for version "v1" the prefix is "vv1:"
        encrypted.Should().StartWith("vv1:", "ciphertext must have 'v{version}:' prefix");
        decrypted.Should().Be(plaintext);
    }

    [Fact]
    public void Encrypt_ProducesUniqueCiphertexts_ForSamePlaintext()
    {
        // Arrange — random nonce means two encryptions of the same plaintext differ
        var converter = MakeConverter(EnabledOptions());
        var toProvider = converter.ConvertToProviderExpression.Compile();

        // Act
        var enc1 = toProvider("same-value");
        var enc2 = toProvider("same-value");

        // Assert — random nonce means different ciphertext each time
        enc1.Should().NotBe(enc2, "each encryption uses a unique random 12-byte nonce");
    }

    [Fact]
    public void Decrypt_WithDisabledEncryption_ReturnsStoredValueAsIs()
    {
        // Arrange — Enabled == false — pass-through (no crypto calls made)
        var converter = MakeConverter(new EncryptionOptions { Enabled = false });
        var fromProvider = converter.ConvertFromProviderExpression.Compile();

        const string stored = "plaintext-no-version-prefix";

        // Act
        var result = fromProvider(stored);

        // Assert
        result.Should().Be(stored);
    }

    [Fact]
    public void Encrypt_WithDisabledEncryption_ReturnsValueAsIs()
    {
        // Arrange — Enabled == false — pass-through (no crypto calls made)
        var converter = MakeConverter(new EncryptionOptions { Enabled = false });
        var toProvider = converter.ConvertToProviderExpression.Compile();

        const string value = "plaintext";

        // Act
        var result = toProvider(value);

        // Assert — no encryption performed; stored value equals plaintext
        result.Should().Be(value);
    }

    [Fact]
    public void Decrypt_LegacyPlaintext_NoVersionPrefix_ReturnsAsIs()
    {
        // Arrange — stored value has no "v" prefix → legacy plaintext path
        var converter = MakeConverter(EnabledOptions());
        var fromProvider = converter.ConvertFromProviderExpression.Compile();

        const string legacyValue = "old-unencrypted-data";

        // Act
        var result = fromProvider(legacyValue);

        // Assert
        result.Should().Be(legacyValue);
    }

    [Fact]
    public void Decrypt_UnknownKeyVersion_ThrowsEncryptionKeyNotFoundException()
    {
        // Arrange — options only has "v2" key but stored value references "v1"
        var keyBytes = new byte[32];
        var base64Key = Convert.ToBase64String(keyBytes);
        var options = new EncryptionOptions
        {
            Enabled = true,
            CurrentVersion = "v2",
            Keys = { ["v2"] = base64Key }
        };
        var converter = MakeConverter(options);
        var fromProvider = converter.ConvertFromProviderExpression.Compile();

        // A ciphertext that was created with "v1" (which is now removed).
        // Format: "v{version}:{payload}" — version "v1" would be stored as "vv1:...".
        // D-108/P-448: there is no more pre-check short-circuiting before length validation, so the
        // payload must be a well-formed length (>= 12-byte nonce + 16-byte tag = 28 bytes) — the
        // converter must actually reach ISymmetricEncryptionService.Decrypt for this to prove the
        // Error.Code == CryptographyErrorCodes.UnknownKeyId mapping, not merely the length guard.
        var storedWithV1 = "vv1:" + Convert.ToBase64String(new byte[28]);

        // Act
        var act = () => fromProvider(storedWithV1);

        // Assert
        act.Should().Throw<EncryptionKeyNotFoundException>()
            .Which.Version.Should().Be("v1");
    }

    [Fact]
    public void Encrypt_ThenTamper_ThrowsCryptographicException()
    {
        // Arrange — authenticate tag check means tampering the ciphertext causes AES-GCM to fail
        var converter = MakeConverter(EnabledOptions());
        var toProvider = converter.ConvertToProviderExpression.Compile();
        var fromProvider = converter.ConvertFromProviderExpression.Compile();

        var encrypted = toProvider("original-value");

        // Tamper: flip the last character of the base64 payload
        var colonIdx = encrypted.IndexOf(':');
        var base64Part = encrypted[(colonIdx + 1)..];
        var chars = base64Part.ToCharArray();
        chars[^1] = chars[^1] == 'A' ? 'B' : 'A';
        var tampered = encrypted[..(colonIdx + 1)] + new string(chars);

        // Act
        var act = () => fromProvider(tampered);

        // Assert (T-126) — a tamper/wrong-key decrypt failure (Error.Code != UnknownKeyId) still
        // surfaces as the existing generic CryptographicException, unchanged from before D-108 —
        // asserted against the concrete type, not merely `Exception`, so this test actually proves
        // the "still the generic CryptographicException" claim rather than any thrown exception.
        act.Should().Throw<System.Security.Cryptography.CryptographicException>(
            "tampered ciphertext fails AES-GCM authentication tag check and the key id IS known " +
            "(only the auth tag is corrupted), so the converter must map this to the generic " +
            "CryptographicException path, not EncryptionKeyNotFoundException");
    }

    [Fact]
    public void Decrypt_MultipleVersionKeys_UsesCorrectKey()
    {
        // Arrange — two versions exist; encrypt with v1, decrypt with v1 key still present
        var key1 = new byte[32]; key1[0] = 1;
        var key2 = new byte[32]; key2[0] = 2;
        var options = new EncryptionOptions
        {
            Enabled = true,
            CurrentVersion = "v1",
            Keys =
            {
                ["v1"] = Convert.ToBase64String(key1),
                ["v2"] = Convert.ToBase64String(key2)
            }
        };
        var converter = MakeConverter(options);
        var toProvider = converter.ConvertToProviderExpression.Compile();
        var fromProvider = converter.ConvertFromProviderExpression.Compile();

        const string plaintext = "multi-key-test";

        // Act
        var encrypted = toProvider(plaintext);
        var decrypted = fromProvider(encrypted);

        // Assert
        encrypted.Should().StartWith("vv1:", "format is 'v{version}:' so version 'v1' produces 'vv1:'");
        decrypted.Should().Be(plaintext);
    }

    [Fact]
    public void EmptyString_Encrypts_And_Decrypts()
    {
        // Arrange
        var converter = MakeConverter(EnabledOptions());
        var toProvider = converter.ConvertToProviderExpression.Compile();
        var fromProvider = converter.ConvertFromProviderExpression.Compile();

        const string empty = "";

        // Act
        var encrypted = toProvider(empty);
        var decrypted = fromProvider(encrypted);

        // Assert
        decrypted.Should().Be(empty);
    }
}
