using FluentAssertions;
using Microsoft.Extensions.Options;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Persistence.EfCore.Encryption;
using SharedKernel.Persistence.EfCore.Options;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;

namespace SharedKernel.Persistence.EfCore.Tests.Encryption;

/// <summary>
/// Unit tests for <see cref="EncryptedValueConverter"/> — verifies AES-256-GCM round-trip,
/// pass-through mode, legacy plaintext handling, and key-not-found exception.
/// The converter delegates crypto to <see cref="ISynchronousSymmetricEncryptionService"/>;
/// tests use the real <see cref="SynchronousAesGcmEncryptionService"/> with <see cref="EncryptionOptionsKeyProvider"/>.
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
    /// Builds a fully-wired <see cref="EncryptedValueConverter"/> using the real delegation chain:
    /// EncryptionOptionsKeyProvider → SynchronousAesGcmEncryptionService → EncryptedValueConverter.
    /// </summary>
    private static readonly byte[] TestAssociatedData = System.Text.Encoding.UTF8.GetBytes("public.test_table.test_column");

    private static EncryptedValueConverter MakeConverter(
        EncryptionOptions options,
        IEncryptionVersionOverride? versionOverride = null,
        string? propertyName = null)
    {
        var monitor = MakeMonitor(options);
        var keyProvider = new EncryptionOptionsKeyProvider(monitor, versionOverride ?? EncryptionVersionOverride.NoOp);
        var encryptionService = new SynchronousAesGcmEncryptionService(keyProvider);
        return new EncryptedValueConverter(monitor, encryptionService, TestAssociatedData, versionOverride, propertyName);
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
        // Stored format: the canonical EncryptedPayload encoding, recording the key id.
        StoredPayload.KeyIdOf(encrypted).Should().Be("v1");
        encrypted.Should().NotContain(plaintext);
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

    [Theory]
    [InlineData("old-unencrypted-data")]
    [InlineData("plaintext")]
    [InlineData("hello world")]
    [InlineData("a")]
    [InlineData("x=y")]
    [InlineData("user@example.com")]
    [InlineData("")]
    public void Decrypt_NotAnEncryptedPayload_FailsClosed_ByDefault(string storedValue)
    {
        // Fail closed: a value planted directly in the database must never be read as if it had been decrypted.
        var converter = MakeConverter(EnabledOptions(), propertyName: "Customer.Email");

        var act = () => converter.ConvertFromProviderExpression.Compile()(storedValue);

        var exception = act.Should().Throw<System.Security.Cryptography.CryptographicException>().Which;
        exception.Message.Should().Contain("Customer.Email", "the message names the property");
        if (storedValue.Length > 3)
        {
            exception.Message.Should().NotContain(storedValue, "the message must never include the stored value");
        }
    }

    [Fact]
    public void Decrypt_TruncatedPayload_FailsClosed_ByDefault()
    {
        var converter = MakeConverter(EnabledOptions(), propertyName: "Customer.Email");
        var toProvider = converter.ConvertToProviderExpression.Compile();
        var fromProvider = converter.ConvertFromProviderExpression.Compile();
        var encrypted = toProvider("a value long enough to truncate");

        // Every truncation — whether it still parses (and fails authentication) or no longer parses — throws.
        foreach (var length in new[] { encrypted.Length - 1, encrypted.Length / 2, 10, 1 })
        {
            var truncated = encrypted[..length];
            var act = () => fromProvider(truncated);
            act.Should().Throw<System.Security.Cryptography.CryptographicException>($"truncated to {length} characters");
        }
    }

    [Theory]
    [InlineData("old-unencrypted-data")]
    [InlineData("hello world")]
    [InlineData("")]
    public void Decrypt_NotAnEncryptedPayload_WithAllowUnencryptedValues_ReturnsAsIs(string legacyValue)
    {
        var options = EnabledOptions();
        options.AllowUnencryptedValues = true;
        var converter = MakeConverter(options);

        converter.ConvertFromProviderExpression.Compile()(legacyValue).Should().Be(legacyValue);
    }

    [Fact]
    public void AllowUnencryptedValues_StillEncryptsWrites_AndStillRejectsTamperedPayloads()
    {
        var options = EnabledOptions();
        options.AllowUnencryptedValues = true;
        var converter = MakeConverter(options);
        var encrypted = converter.ConvertToProviderExpression.Compile()("secret");

        StoredPayload.KeyIdOf(encrypted).Should().Be("v1", "the migration setting never disables encryption of writes");

        var payload = StoredPayload.Parse(encrypted);
        var ciphertext = payload.Ciphertext.ToArray();
        ciphertext[0] ^= 0x01;
        var tampered = new EncryptedPayload(payload.KeyId, payload.Nonce, ciphertext, payload.Tag).ToString();

        var act = () => converter.ConvertFromProviderExpression.Compile()(tampered);
        act.Should().Throw<System.Security.Cryptography.CryptographicException>(
            "a well-formed payload that fails authentication always throws, even during a migration");
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

        // A well-formed payload recording key id "v1" (which is now removed), so the converter reaches
        // Decrypt and maps Error.Code == CryptographyErrorCodes.UnknownKeyId.
        var storedWithV1 = StoredPayload.ForKeyId("v1");

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

        // Tamper: flip one ciphertext bit while keeping the payload well-formed
        var payload = StoredPayload.Parse(encrypted);
        var ciphertext = payload.Ciphertext.ToArray();
        ciphertext[0] ^= 0x01;
        var tampered = new EncryptedPayload(payload.KeyId, payload.Nonce, ciphertext, payload.Tag).ToString();

        // Act
        var act = () => fromProvider(tampered);

        // Assert (T-126) — a tamper/wrong-key decrypt failure (Error.Code != UnknownKeyId) surfaces
        // as the generic CryptographicException, asserted against the concrete type.
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
        StoredPayload.KeyIdOf(encrypted).Should().Be("v1");
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
