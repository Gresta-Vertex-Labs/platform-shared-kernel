using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharedKernel.Cryptography;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Persistence.EfCore.Encryption;
using SharedKernel.Persistence.EfCore.Options;

namespace SharedKernel.Persistence.EfCore.Tests.Encryption;

/// <summary>
/// T-55: Wire-format regression tests (P-227).
/// T-56: EncryptionOptionsKeyProvider unit tests (P-227).
/// T-57: EncryptionKeyNotFoundException trigger-path regression test (P-227).
/// T-58: IEncryptionVersionOverride rotation-scoped seam continuity through the new key-resolution path (P-227).
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

    /// <summary>
    /// Builds a real converter using the full P-227 delegation chain.
    /// EncryptionOptionsKeyProvider → AesGcmEncryptionService → EncryptedValueConverter.
    /// </summary>
    private static readonly byte[] TestAssociatedData = System.Text.Encoding.UTF8.GetBytes("public.test_table.test_column");

    private static EncryptedValueConverter MakeConverter(
        EncryptionOptions opts,
        IEncryptionVersionOverride? versionOverride = null)
    {
        var monitor = MakeMonitor(opts);
        var keyProvider = new EncryptionOptionsKeyProvider(monitor, versionOverride ?? EncryptionVersionOverride.NoOp, new EncryptionKeyByteCache(monitor));
        var encService = new AesGcmEncryptionService(keyProvider);
        return new EncryptedValueConverter(monitor, encService, TestAssociatedData, versionOverride);
    }

    // ===========================================================================
    // T-55: Wire-format regression tests (P-227)
    // ===========================================================================

    [Fact]
    public void T55_NewConverter_RoundTrip_WithDelegation()
    {
        // Verify encrypt → decrypt round-trips correctly using the new ISymmetricEncryptionService path.
        var converter = MakeConverter(EnabledOptions("v1"));
        const string plaintext = "hello-world-encryption";

        var encrypted = converter.ConvertToProviderExpression.Compile()(plaintext);
        var decrypted = converter.ConvertFromProviderExpression.Compile()(encrypted);

        encrypted.Should().StartWith("vv1:", "wire format must be 'v{version}:{base64}'");
        decrypted.Should().Be(plaintext);
    }

    [Fact]
    public void T55_LegacyPlaintext_NoVersionPrefix_PassesThroughUnchanged()
    {
        // Stored values without a 'v' prefix are returned unchanged — safe migration path.
        var converter = MakeConverter(EnabledOptions("v1"));
        const string legacyValue = "unencrypted-legacy-data";

        var result = converter.ConvertFromProviderExpression.Compile()(legacyValue);

        result.Should().Be(legacyValue, "legacy plaintext (no version prefix) must pass through unchanged");
    }

    [Fact]
    public void T55_EnabledFalse_PassThrough_NoCryptoServiceCalls()
    {
        // When Enabled == false, neither Encrypt nor Decrypt calls ISymmetricEncryptionService.
        var disabledOpts = new EncryptionOptions { Enabled = false };
        var monitor = MakeMonitor(disabledOpts);
        var cryptoSvc = Substitute.For<ISymmetricEncryptionService>();
        var converter = new EncryptedValueConverter(monitor, cryptoSvc, TestAssociatedData);

        const string val = "plaintext";
        var encrypted = converter.ConvertToProviderExpression.Compile()(val);
        var decrypted = converter.ConvertFromProviderExpression.Compile()(val);

        encrypted.Should().Be(val, "pass-through: encrypt must return input unchanged when Enabled == false");
        decrypted.Should().Be(val, "pass-through: decrypt must return input unchanged when Enabled == false");
        cryptoSvc.DidNotReceiveWithAnyArgs().Encrypt(default!, default!);
        cryptoSvc.DidNotReceiveWithAnyArgs().Decrypt(default!, default!);
    }

    [Fact]
    public void T55_WireFormat_IsCorrect_NonceAndCiphertextAndTagPacked()
    {
        // The wire format "v{KeyId}:{Base64(nonce||ciphertext||tag)}" must be preserved.
        // Verify that the packed Base64 payload when decoded produces enough bytes
        // (12-byte nonce + ciphertext + 16-byte tag).
        var converter = MakeConverter(EnabledOptions("v1"));
        const string plaintext = "test-wire-format";

        var encrypted = converter.ConvertToProviderExpression.Compile()(plaintext);

        var colonIdx = encrypted.IndexOf(':');
        colonIdx.Should().BeGreaterThan(0, "colon must separate version from payload");

        var version = encrypted[1..colonIdx]; // strip leading 'v'
        version.Should().Be("v1");

        var base64Payload = encrypted[(colonIdx + 1)..];
        var combined = Convert.FromBase64String(base64Payload);

        // nonce (12) + ciphertext (≥ 0) + tag (16) — minimum length is 12 + 0 + 16 = 28
        combined.Length.Should().BeGreaterThanOrEqualTo(28,
            "packed payload must have at least 12-byte nonce + 16-byte tag");
    }

    // ===========================================================================
    // T-56: EncryptionOptionsKeyProvider unit tests (P-227)
    // ===========================================================================

    [Fact]
    public async Task T56_GetCurrentKeyAsync_Returns_CurrentVersion_Key_When_NoOverride()
    {
        // GetCurrentKeyAsync() should return key for CurrentVersion when OverrideVersion is null.
        var v1Bytes = new byte[32]; Array.Fill(v1Bytes, (byte)0x11);
        var opts = EnabledOptions("v1", 0x11);
        var monitor = MakeMonitor(opts);
        var versionOverride = new EncryptionVersionOverride(); // OverrideVersion = null
        var keyProvider = new EncryptionOptionsKeyProvider(monitor, versionOverride, new EncryptionKeyByteCache(monitor));

        var key = await keyProvider.GetCurrentKeyAsync();

        key.Id.Should().Be("v1");
        key.Material.Should().Equal(v1Bytes);
    }

    [Fact]
    public async Task T56_GetCurrentKeyAsync_Returns_OverrideVersion_Key_When_OverrideSet()
    {
        // GetCurrentKeyAsync() respects OverrideVersion ?? CurrentVersion precedence.
        var v2Bytes = new byte[32]; Array.Fill(v2Bytes, (byte)0x22);
        var opts = EnabledOptions("v1", 0x11, [("v2", 0x22)]);
        var monitor = MakeMonitor(opts);
        var versionOverride = new EncryptionVersionOverride { OverrideVersion = "v2" };
        var keyProvider = new EncryptionOptionsKeyProvider(monitor, versionOverride, new EncryptionKeyByteCache(monitor));

        var key = await keyProvider.GetCurrentKeyAsync();

        key.Id.Should().Be("v2");
        key.Material.Should().Equal(v2Bytes);
    }

    [Fact]
    public async Task T56_GetCurrentKeyAsync_Precedence_Does_Not_Mutate_CurrentVersion()
    {
        // Setting OverrideVersion changes what GetCurrentKeyAsync returns,
        // but does NOT mutate EncryptionOptions.CurrentVersion.
        var opts = EnabledOptions("v1", 0x11, [("v2", 0x22)]);
        var monitor = MakeMonitor(opts);
        var versionOverride = new EncryptionVersionOverride { OverrideVersion = "v2" };
        var keyProvider = new EncryptionOptionsKeyProvider(monitor, versionOverride, new EncryptionKeyByteCache(monitor));

        _ = await keyProvider.GetCurrentKeyAsync(); // resolve override

        monitor.CurrentValue.CurrentVersion.Should().Be("v1",
            "EncryptionOptions.CurrentVersion must never be mutated by override resolution");
    }

    [Fact]
    public async Task T56_GetKeyAsync_Returns_Correct_Key_For_Known_Version()
    {
        var v2Bytes = new byte[32]; Array.Fill(v2Bytes, (byte)0x22);
        var opts = EnabledOptions("v1", 0x11, [("v2", 0x22)]);
        var monitor = MakeMonitor(opts);
        var keyProvider = new EncryptionOptionsKeyProvider(monitor, EncryptionVersionOverride.NoOp, new EncryptionKeyByteCache(monitor));

        var key = await keyProvider.GetKeyAsync("v2");

        key.Should().NotBeNull();
        key!.Id.Should().Be("v2");
        key.Material.Should().Equal(v2Bytes);
    }

    [Fact]
    public async Task T56_GetKeyAsync_Returns_Null_Not_Throw_For_Unknown_Version()
    {
        var opts = EnabledOptions("v1");
        var monitor = MakeMonitor(opts);
        var keyProvider = new EncryptionOptionsKeyProvider(monitor, EncryptionVersionOverride.NoOp, new EncryptionKeyByteCache(monitor));

        var key = await keyProvider.GetKeyAsync("v99-not-registered");

        key.Should().BeNull("IEncryptionKeyProvider.GetKeyAsync must return null, not throw, for unknown versions");
    }

    [Fact]
    public async Task T56_GetKeyAsync_Ignores_VersionOverride_Always_Uses_Requested_KeyId()
    {
        // Decryption always targets the stored KeyId — override has no effect on GetKeyAsync.
        var opts = EnabledOptions("v1", 0x11, [("v2", 0x22)]);
        var monitor = MakeMonitor(opts);
        var versionOverride = new EncryptionVersionOverride { OverrideVersion = "v2" };
        var keyProvider = new EncryptionOptionsKeyProvider(monitor, versionOverride, new EncryptionKeyByteCache(monitor));

        var key = await keyProvider.GetKeyAsync("v1"); // explicit lookup, ignores override

        key.Should().NotBeNull("GetKeyAsync always ignores OverrideVersion — uses the supplied keyId directly");
        key!.Id.Should().Be("v1");
    }

    [Fact]
    public async Task T56_HotReload_GetCurrentKeyAsync_Reflects_Updated_CurrentVersion()
    {
        // Hot-reload: changing CurrentValue between calls is reflected immediately.
        var opts = new EncryptionOptions { Enabled = true, CurrentVersion = "v1" };
        var v1 = new byte[32]; Array.Fill(v1, (byte)0x11);
        var v2 = new byte[32]; Array.Fill(v2, (byte)0x22);
        opts.Keys["v1"] = Convert.ToBase64String(v1);
        opts.Keys["v2"] = Convert.ToBase64String(v2);

        var mutableMonitor = new MutableOptionsMonitor(opts);
        var keyProvider = new EncryptionOptionsKeyProvider(mutableMonitor, EncryptionVersionOverride.NoOp, new EncryptionKeyByteCache(mutableMonitor));

        var key1 = await keyProvider.GetCurrentKeyAsync();
        key1.Id.Should().Be("v1");

        // Simulate hot-reload: update CurrentVersion in options
        mutableMonitor.CurrentValue.CurrentVersion = "v2";
        var key2 = await keyProvider.GetCurrentKeyAsync();
        key2.Id.Should().Be("v2", "hot-reload: GetCurrentKeyAsync reads CurrentValue fresh every call");
    }

    [Fact]
    public void T56_GetCurrentKeyAsync_And_GetKeyAsync_Complete_Synchronously()
    {
        // D-110/P-448: the config-based provider performs no genuine I/O — both members must return
        // an already-completed ValueTask, proving AesGcmEncryptionService's internal
        // .GetAwaiter().GetResult() bridge never blocks a thread on real I/O for this provider.
        var opts = EnabledOptions("v1", 0x11);
        var monitor = MakeMonitor(opts);
        var keyProvider = new EncryptionOptionsKeyProvider(monitor, EncryptionVersionOverride.NoOp, new EncryptionKeyByteCache(monitor));

        var currentKeyTask = keyProvider.GetCurrentKeyAsync();
        var keyTask = keyProvider.GetKeyAsync("v1");

        currentKeyTask.IsCompletedSuccessfully.Should().BeTrue(
            "the config-based provider resolves synchronously, in-memory");
        keyTask.IsCompletedSuccessfully.Should().BeTrue(
            "the config-based provider resolves synchronously, in-memory");
    }

    private sealed class MutableOptionsMonitor(EncryptionOptions value) : IOptionsMonitor<EncryptionOptions>
    {
        public EncryptionOptions CurrentValue { get; } = value;
        public EncryptionOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<EncryptionOptions, string?> listener) => null;
    }

    // ===========================================================================
    // T-57: EncryptionKeyNotFoundException trigger-path regression test (P-227)
    // ===========================================================================

    [Fact]
    public void T57_DecryptWithUnknownVersion_ThrowsEncryptionKeyNotFoundException_ViaDecryptResultCode()
    {
        // D-108/P-448: there is no more direct pre-check against IEncryptionKeyProvider — the
        // converter calls ISymmetricEncryptionService.Decrypt directly, and maps a failure whose
        // Error.Code == CryptographyErrorCodes.UnknownKeyId to EncryptionKeyNotFoundException.
        var opts = new EncryptionOptions
        {
            Enabled = true,
            CurrentVersion = "v2",
            Keys = { ["v2"] = Convert.ToBase64String(new byte[32]) }
        };
        var monitor = MakeMonitor(opts);
        var cryptoSvc = Substitute.For<ISymmetricEncryptionService>();
        cryptoSvc.Decrypt(Arg.Any<EncryptedPayload>(), Arg.Any<byte[]>()).Returns(
            SharedKernel.Primitives.Errors.Error.Unexpected(
                CryptographyErrorCodes.UnknownKeyId,
                "No encryption key registered for key id 'v1'."));

        var converter = new EncryptedValueConverter(monitor, cryptoSvc, TestAssociatedData);
        var fromProvider = converter.ConvertFromProviderExpression.Compile();

        // Version "v1" is not in Keys. Payload must be well-formed length (>= 28 bytes) so the
        // converter's own length guard doesn't short-circuit before ever calling Decrypt.
        var stored = "vv1:" + Convert.ToBase64String(new byte[28]);
        var act = () => fromProvider(stored);

        act.Should().Throw<EncryptionKeyNotFoundException>()
            .Which.Version.Should().Be("v1");

        // The converter DOES delegate to Decrypt now — the distinction comes from the result code.
        cryptoSvc.Received(1).Decrypt(Arg.Any<EncryptedPayload>(), Arg.Any<byte[]>());
    }

    [Fact]
    public void T57_DecryptWithKnownVersion_Delegates_To_ISymmetricEncryptionService()
    {
        // When the key is found, the converter MUST delegate to ISymmetricEncryptionService.Decrypt.
        var plaintext = "original-data";
        var converter = MakeConverter(EnabledOptions("v1"));
        var toProvider = converter.ConvertToProviderExpression.Compile();
        var fromProvider = converter.ConvertFromProviderExpression.Compile();

        var encrypted = toProvider(plaintext);
        var decrypted = fromProvider(encrypted);

        decrypted.Should().Be(plaintext, "when the key is known, decryption must succeed via ISymmetricEncryptionService");
    }

    // ===========================================================================
    // T-58: IEncryptionVersionOverride rotation seam continuity through new path (P-227)
    // ===========================================================================

    [Fact]
    public void T58_RotateAsync_Override_EncryptsWith_ToVersion_Not_CurrentVersion()
    {
        // When OverrideVersion = "v2" is set on the converter, encryptions use v2's key,
        // even though CurrentVersion remains "v1".
        var opts = EnabledOptions("v1", 0x11, [("v2", 0x22)]);
        var override_ = new EncryptionVersionOverride { OverrideVersion = "v2" };
        var converter = MakeConverter(opts, override_);
        var toProvider = converter.ConvertToProviderExpression.Compile();

        var encrypted = toProvider("test-data");

        // The ciphertext version prefix must be "v2" (wire format: "vv2:")
        encrypted.Should().StartWith("vv2:", "when OverrideVersion = 'v2', encrypt must use v2's key");
    }

    [Fact]
    public void T58_CurrentVersion_Unchanged_After_Override_Usage()
    {
        // Using OverrideVersion must never mutate EncryptionOptions.CurrentVersion.
        var opts = EnabledOptions("v1", 0x11, [("v2", 0x22)]);
        var monitor = MakeMonitor(opts);
        var override_ = new EncryptionVersionOverride { OverrideVersion = "v2" };
        var keyProvider = new EncryptionOptionsKeyProvider(monitor, override_, new EncryptionKeyByteCache(monitor));
        var encService = new AesGcmEncryptionService(keyProvider);
        var converter = new EncryptedValueConverter(monitor, encService, TestAssociatedData, override_);
        var toProvider = converter.ConvertToProviderExpression.Compile();

        _ = toProvider("some-value"); // force encryption with override

        monitor.CurrentValue.CurrentVersion.Should().Be("v1",
            "EncryptionOptions.CurrentVersion must never be mutated by override-based encryption");
    }

    [Fact]
    public void T58_ConcurrentScope_Without_Override_Uses_CurrentVersion()
    {
        // A converter with no override (null OverrideVersion) must use CurrentVersion.
        var opts = EnabledOptions("v1", 0x11, [("v2", 0x22)]);

        // Converter 1: override = v2 (rotation batch)
        var overrideV2 = new EncryptionVersionOverride { OverrideVersion = "v2" };
        var converterWithOverride = MakeConverter(opts, overrideV2);

        // Converter 2: no override (unrelated concurrent scope)
        var converterNoOverride = MakeConverter(opts, null);

        var toOverride = converterWithOverride.ConvertToProviderExpression.Compile();
        var toNoOverride = converterNoOverride.ConvertToProviderExpression.Compile();

        var encOverride = toOverride("data-a");
        var encNoOverride = toNoOverride("data-b");

        encOverride.Should().StartWith("vv2:", "override scope encrypts with v2");
        encNoOverride.Should().StartWith("vv1:", "no-override scope encrypts with CurrentVersion (v1)");
    }

    [Fact]
    public void T58_AfterOverride_Reset_FreshConverter_Uses_CurrentVersion()
    {
        // A fresh converter (after override is reset to null) must use CurrentVersion.
        var opts = EnabledOptions("v1", 0x11, [("v2", 0x22)]);
        var override_ = new EncryptionVersionOverride { OverrideVersion = "v2" };
        var converterDuringRotation = MakeConverter(opts, override_);
        _ = converterDuringRotation.ConvertToProviderExpression.Compile()("rotated");

        // Reset override (simulates end of rotation batch)
        override_.OverrideVersion = null;

        // Fresh converter using the same (now-reset) override
        var converterAfterReset = MakeConverter(opts, override_);
        var encrypted = converterAfterReset.ConvertToProviderExpression.Compile()("fresh");

        encrypted.Should().StartWith("vv1:", "after override reset, fresh converter uses CurrentVersion (v1)");
    }
}
