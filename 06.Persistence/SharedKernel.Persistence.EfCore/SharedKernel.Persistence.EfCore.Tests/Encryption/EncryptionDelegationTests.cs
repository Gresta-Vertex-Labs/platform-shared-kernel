using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.Options;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Persistence.EfCore.Encryption;
using SharedKernel.Persistence.EfCore.Options;
using SharedKernel.Persistence.EfCore.Tests.TestFixtures;
using SharedKernel.Testing.Cryptography;

namespace SharedKernel.Persistence.EfCore.Tests.Encryption;

/// <summary>
/// T-55: stored-format tests.
/// T-56: EncryptionOptionsKeyProvider unit tests.
/// T-57: decryption failure mapping (EncryptionKeyNotFoundException / CryptographicException).
/// T-58: IEncryptionVersionOverride rotation-scoped seam continuity through key resolution.
/// </summary>
public sealed class EncryptionDelegationTests
{
    // ---------------------------------------------------------------------------
    // Shared helpers
    // ---------------------------------------------------------------------------

    private sealed class FixedOptionsMonitor(EncryptionOptions value) : IOptionsMonitor<EncryptionOptions>
    {
        public EncryptionOptions CurrentValue => value;
        public EncryptionOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<EncryptionOptions, string?> listener) => null;
    }

    private static IOptionsMonitor<EncryptionOptions> MakeMonitor(EncryptionOptions options)
        => new FixedOptionsMonitor(options);

    private static EncryptionOptions EnabledOptions(
        string currentVersion = "v1",
        byte firstByte = 0x01,
        IEnumerable<(string, byte)>? additionalVersions = null)
    {
        var keyBytes = new byte[32];
        Array.Fill(keyBytes, firstByte);
        var options = new EncryptionOptions
        {
            Enabled = true,
            CurrentVersion = currentVersion,
            Keys = { [currentVersion] = Convert.ToBase64String(keyBytes) }
        };
        foreach (var (version, fill) in additionalVersions ?? [])
        {
            var extra = new byte[32];
            Array.Fill(extra, fill);
            options.Keys[version] = Convert.ToBase64String(extra);
        }
        return options;
    }

    private static readonly byte[] TestAssociatedData = System.Text.Encoding.UTF8.GetBytes("public.test_table.test_column");

    /// <summary>
    /// Builds a real converter: EncryptionOptionsKeyProvider → SynchronousAesGcmEncryptionService → EncryptedValueConverter.
    /// </summary>
    private static EncryptedValueConverter MakeConverter(
        EncryptionOptions opts,
        IEncryptionVersionOverride? versionOverride = null)
    {
        var monitor = MakeMonitor(opts);
        var keyProvider = new EncryptionOptionsKeyProvider(monitor, versionOverride ?? EncryptionVersionOverride.NoOp);
        var encService = new SynchronousAesGcmEncryptionService(keyProvider);
        return new EncryptedValueConverter(monitor, encService, TestAssociatedData, versionOverride);
    }

    // ===========================================================================
    // T-55: Stored-format tests
    // ===========================================================================

    [Fact]
    public void T55_Converter_RoundTrip_WithDelegation()
    {
        var converter = MakeConverter(EnabledOptions("v1"));
        const string plaintext = "hello-world-encryption";

        var encrypted = converter.ConvertToProviderExpression.Compile()(plaintext);
        var decrypted = converter.ConvertFromProviderExpression.Compile()(encrypted);

        StoredPayload.KeyIdOf(encrypted).Should().Be("v1");
        decrypted.Should().Be(plaintext);
    }

    [Fact]
    public void T55_UnencryptedStoredValue_FailsClosed_UnlessAllowUnencryptedValues()
    {
        // Fail closed by default; the temporary migration setting returns legacy plaintext unchanged.
        const string legacyValue = "unencrypted-legacy-data";

        var strict = MakeConverter(EnabledOptions("v1"));
        var act = () => strict.ConvertFromProviderExpression.Compile()(legacyValue);
        act.Should().Throw<CryptographicException>();

        var migrating = EnabledOptions("v1");
        migrating.AllowUnencryptedValues = true;
        MakeConverter(migrating).ConvertFromProviderExpression.Compile()(legacyValue)
            .Should().Be(legacyValue, "legacy plaintext passes through only while AllowUnencryptedValues is set");
    }

    [Fact]
    public void T55_EnabledFalse_PassThrough_NoCryptoServiceCalls()
    {
        // NullSymmetricEncryptionService throws on every member, so a successful pass-through proves the
        // converter never called the service in either direction.
        var monitor = MakeMonitor(new EncryptionOptions { Enabled = false });
        var converter = new EncryptedValueConverter(monitor, NullSymmetricEncryptionService.Instance, TestAssociatedData);

        const string val = "plaintext";
        var encrypted = converter.ConvertToProviderExpression.Compile()(val);
        var decrypted = converter.ConvertFromProviderExpression.Compile()(StoredPayload.ForKeyId("v1"));

        encrypted.Should().Be(val, "pass-through: encrypt must return input unchanged when Enabled == false");
        decrypted.Should().Be(StoredPayload.ForKeyId("v1"), "pass-through: decrypt must return input unchanged when Enabled == false");
    }

    [Fact]
    public void T55_StoredFormat_IsTheCanonicalEncryptedPayloadEncoding()
    {
        var converter = MakeConverter(EnabledOptions("v1"));
        const string plaintext = "test-wire-format";

        var encrypted = converter.ConvertToProviderExpression.Compile()(plaintext);

        var payload = StoredPayload.Parse(encrypted);
        payload.KeyId.Should().Be("v1");
        payload.Nonce.Length.Should().Be(EncryptedPayload.NonceSize);
        payload.Tag.Length.Should().Be(EncryptedPayload.TagSize);
        payload.Ciphertext.Length.Should().Be(System.Text.Encoding.UTF8.GetByteCount(plaintext));
        payload.ToString().Should().Be(encrypted, "the column value is exactly EncryptedPayload.ToString()");
    }

    // ===========================================================================
    // T-56: EncryptionOptionsKeyProvider unit tests
    // ===========================================================================

    [Fact]
    public void T56_Implements_OnlyTheSynchronousProviderContract()
    {
        var monitor = MakeMonitor(EnabledOptions("v1"));
        var keyProvider = new EncryptionOptionsKeyProvider(monitor, EncryptionVersionOverride.NoOp);

        keyProvider.Should().BeAssignableTo<ISynchronousEncryptionKeyProvider>();
    }

    [Fact]
    public void T56_GetCurrentKey_Returns_CurrentVersion_Key_When_NoOverride()
    {
        var v1Bytes = new byte[32]; Array.Fill(v1Bytes, (byte)0x11);
        var monitor = MakeMonitor(EnabledOptions("v1", 0x11));
        var keyProvider = new EncryptionOptionsKeyProvider(monitor, new EncryptionVersionOverride());

        var key = keyProvider.GetCurrentKey();

        key.Id.Should().Be("v1");
        key.Material.ToArray().Should().Equal(v1Bytes);
    }

    [Fact]
    public void T56_GetCurrentKey_Returns_OverrideVersion_Key_When_OverrideSet()
    {
        var v2Bytes = new byte[32]; Array.Fill(v2Bytes, (byte)0x22);
        var monitor = MakeMonitor(EnabledOptions("v1", 0x11, [("v2", 0x22)]));
        var versionOverride = new EncryptionVersionOverride { OverrideVersion = "v2" };
        var keyProvider = new EncryptionOptionsKeyProvider(monitor, versionOverride);

        var key = keyProvider.GetCurrentKey();

        key.Id.Should().Be("v2");
        key.Material.ToArray().Should().Equal(v2Bytes);
    }

    [Fact]
    public void T56_GetCurrentKey_Precedence_Does_Not_Mutate_CurrentVersion()
    {
        var monitor = MakeMonitor(EnabledOptions("v1", 0x11, [("v2", 0x22)]));
        var versionOverride = new EncryptionVersionOverride { OverrideVersion = "v2" };
        var keyProvider = new EncryptionOptionsKeyProvider(monitor, versionOverride);

        _ = keyProvider.GetCurrentKey();

        monitor.CurrentValue.CurrentVersion.Should().Be("v1",
            "EncryptionOptions.CurrentVersion must never be mutated by override resolution");
    }

    [Fact]
    public void T56_GetCurrentKey_VersionNotConfigured_Throws_InvalidOperationException()
    {
        var monitor = MakeMonitor(EnabledOptions("v1"));
        var versionOverride = new EncryptionVersionOverride { OverrideVersion = "v9" };
        var keyProvider = new EncryptionOptionsKeyProvider(monitor, versionOverride);

        var act = () => keyProvider.GetCurrentKey();

        act.Should().Throw<InvalidOperationException>().WithMessage("*'v9'*");
    }

    [Fact]
    public void T56_GetKey_Returns_Correct_Key_For_Known_Version()
    {
        var v2Bytes = new byte[32]; Array.Fill(v2Bytes, (byte)0x22);
        var monitor = MakeMonitor(EnabledOptions("v1", 0x11, [("v2", 0x22)]));
        var keyProvider = new EncryptionOptionsKeyProvider(monitor, EncryptionVersionOverride.NoOp);

        var key = keyProvider.GetKey("v2");

        key.Should().NotBeNull();
        key!.Id.Should().Be("v2");
        key.Material.ToArray().Should().Equal(v2Bytes);
    }

    [Fact]
    public void T56_GetKey_Returns_Null_Not_Throw_For_Unknown_Version()
    {
        var monitor = MakeMonitor(EnabledOptions("v1"));
        var keyProvider = new EncryptionOptionsKeyProvider(monitor, EncryptionVersionOverride.NoOp);

        keyProvider.GetKey("v99-not-registered").Should().BeNull(
            "GetKey must return null, not throw, for unknown versions");
    }

    [Fact]
    public void T56_GetKey_Ignores_VersionOverride_Always_Uses_Requested_KeyId()
    {
        var monitor = MakeMonitor(EnabledOptions("v1", 0x11, [("v2", 0x22)]));
        var versionOverride = new EncryptionVersionOverride { OverrideVersion = "v2" };
        var keyProvider = new EncryptionOptionsKeyProvider(monitor, versionOverride);

        var key = keyProvider.GetKey("v1");

        key.Should().NotBeNull("GetKey always ignores OverrideVersion — uses the supplied keyId directly");
        key!.Id.Should().Be("v1");
    }

    [Fact]
    public void T56_HotReload_GetCurrentKey_Reflects_Updated_CurrentVersion()
    {
        var opts = new EncryptionOptions { Enabled = true, CurrentVersion = "v1" };
        var v1 = new byte[32]; Array.Fill(v1, (byte)0x11);
        var v2 = new byte[32]; Array.Fill(v2, (byte)0x22);
        opts.Keys["v1"] = Convert.ToBase64String(v1);
        opts.Keys["v2"] = Convert.ToBase64String(v2);

        var mutableMonitor = new MutableOptionsMonitor(opts);
        var keyProvider = new EncryptionOptionsKeyProvider(mutableMonitor, EncryptionVersionOverride.NoOp);

        keyProvider.GetCurrentKey().Id.Should().Be("v1");

        // Simulate hot-reload: update CurrentVersion in options
        mutableMonitor.CurrentValue.CurrentVersion = "v2";
        keyProvider.GetCurrentKey().Id.Should().Be("v2", "hot-reload: GetCurrentKey reads CurrentValue fresh every call");
    }

    private sealed class MutableOptionsMonitor(EncryptionOptions value) : IOptionsMonitor<EncryptionOptions>
    {
        public EncryptionOptions CurrentValue { get; } = value;
        public EncryptionOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<EncryptionOptions, string?> listener) => null;
    }

    // ===========================================================================
    // T-57: Decryption failure mapping
    // ===========================================================================

    [Fact]
    public void T57_DecryptWithRetiredKeyId_ThrowsEncryptionKeyNotFoundException()
    {
        // The service reports CryptographyErrorCodes.UnknownKeyId; the converter maps it.
        var keys = new FakeEncryptionKeyProvider("v1");
        var service = new FakeSymmetricEncryptionService(keys);
        var converter = new EncryptedValueConverter(MakeMonitor(EnabledOptions("v1")), service, TestAssociatedData);

        var stored = converter.ConvertToProviderExpression.Compile()("retired-key-data");
        keys.AddKey("v2");
        keys.SetCurrentKey("v2");
        keys.RemoveKey("v1");

        var act = () => converter.ConvertFromProviderExpression.Compile()(stored);

        act.Should().Throw<EncryptionKeyNotFoundException>()
            .Which.Version.Should().Be("v1");
    }

    [Fact]
    public void T57_DecryptionFailure_ThrowsCryptographicException()
    {
        // Any failure other than an unknown key id (tamper, wrong key, wrong AAD) maps to CryptographicException.
        var service = new FakeSymmetricEncryptionService();
        var converter = new EncryptedValueConverter(MakeMonitor(EnabledOptions("v1")), service, TestAssociatedData);
        var stored = converter.ConvertToProviderExpression.Compile()("data");
        service.SimulateDecryptFailure = true;

        var act = () => converter.ConvertFromProviderExpression.Compile()(stored);

        act.Should().Throw<CryptographicException>().WithMessage("*'v1'*");
    }

    [Fact]
    public void T57_Converter_BindsItsAssociatedData_OnEveryEncryption()
    {
        var service = new FakeSymmetricEncryptionService();
        var converter = new EncryptedValueConverter(MakeMonitor(EnabledOptions("v1")), service, TestAssociatedData);

        var stored = converter.ConvertToProviderExpression.Compile()("aad-bound");

        service.EncryptedPayloads.Should().ContainSingle();
        service.EncryptedPayloads[0].AssociatedData.Should().Equal(TestAssociatedData);
        converter.ConvertFromProviderExpression.Compile()(stored).Should().Be("aad-bound");
    }

    // ===========================================================================
    // T-58: IEncryptionVersionOverride rotation seam continuity
    // ===========================================================================

    [Fact]
    public void T58_RotateAsync_Override_EncryptsWith_ToVersion_Not_CurrentVersion()
    {
        var opts = EnabledOptions("v1", 0x11, [("v2", 0x22)]);
        var override_ = new EncryptionVersionOverride { OverrideVersion = "v2" };
        var converter = MakeConverter(opts, override_);

        var encrypted = converter.ConvertToProviderExpression.Compile()("test-data");

        StoredPayload.KeyIdOf(encrypted).Should().Be("v2", "when OverrideVersion = 'v2', encrypt must use v2's key");
    }

    [Fact]
    public void T58_CurrentVersion_Unchanged_After_Override_Usage()
    {
        var opts = EnabledOptions("v1", 0x11, [("v2", 0x22)]);
        var monitor = MakeMonitor(opts);
        var override_ = new EncryptionVersionOverride { OverrideVersion = "v2" };
        var keyProvider = new EncryptionOptionsKeyProvider(monitor, override_);
        var encService = new SynchronousAesGcmEncryptionService(keyProvider);
        var converter = new EncryptedValueConverter(monitor, encService, TestAssociatedData, override_);

        _ = converter.ConvertToProviderExpression.Compile()("some-value");

        monitor.CurrentValue.CurrentVersion.Should().Be("v1",
            "EncryptionOptions.CurrentVersion must never be mutated by override-based encryption");
    }

    [Fact]
    public void T58_ConcurrentScope_Without_Override_Uses_CurrentVersion()
    {
        var opts = EnabledOptions("v1", 0x11, [("v2", 0x22)]);

        var overrideV2 = new EncryptionVersionOverride { OverrideVersion = "v2" };
        var converterWithOverride = MakeConverter(opts, overrideV2);
        var converterNoOverride = MakeConverter(opts, null);

        var encOverride = converterWithOverride.ConvertToProviderExpression.Compile()("data-a");
        var encNoOverride = converterNoOverride.ConvertToProviderExpression.Compile()("data-b");

        StoredPayload.KeyIdOf(encOverride).Should().Be("v2", "override scope encrypts with v2");
        StoredPayload.KeyIdOf(encNoOverride).Should().Be("v1", "no-override scope encrypts with CurrentVersion (v1)");
    }

    [Fact]
    public void T58_AfterOverride_Reset_FreshConverter_Uses_CurrentVersion()
    {
        var opts = EnabledOptions("v1", 0x11, [("v2", 0x22)]);
        var override_ = new EncryptionVersionOverride { OverrideVersion = "v2" };
        var converterDuringRotation = MakeConverter(opts, override_);
        _ = converterDuringRotation.ConvertToProviderExpression.Compile()("rotated");

        override_.OverrideVersion = null;

        var converterAfterReset = MakeConverter(opts, override_);
        var encrypted = converterAfterReset.ConvertToProviderExpression.Compile()("fresh");

        StoredPayload.KeyIdOf(encrypted).Should().Be("v1", "after override reset, fresh converter uses CurrentVersion (v1)");
    }
}
