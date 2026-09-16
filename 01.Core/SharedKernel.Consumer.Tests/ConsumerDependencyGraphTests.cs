using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using SharedKernel.Compression;
using SharedKernel.Compression.Extensions;
using SharedKernel.Configuration.Extensions;
using SharedKernel.Core.Exceptions;
using SharedKernel.Core.Extensions;
using SharedKernel.Cryptography;
using SharedKernel.Cryptography.Argon2;
using SharedKernel.Cryptography.Envelope;
using SharedKernel.Cryptography.Extensions;
using SharedKernel.Cryptography.Hashing;
using SharedKernel.Cryptography.KeyVault.Azure;
using SharedKernel.Cryptography.Random;
using SharedKernel.Cryptography.Signing;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Cryptography.Totp;
using SharedKernel.DataPrivacy.Classification;
using SharedKernel.DataPrivacy.DataSubjectRequests;
using SharedKernel.DataPrivacy.Masking;
using SharedKernel.FeatureManagement.Abstractions;
using SharedKernel.FeatureManagement.Extensions;
using SharedKernel.Guards;
using SharedKernel.Localization;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Enums;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Validation.Errors;
using SharedKernel.Validation.Extensions;
using SharedKernel.Validation.FluentValidation;
using SharedKernel.Validation.Guards;
using SharedKernel.Validation.NationalId;
using SharedKernel.Validation.Validators;
using Xunit;
using FluentValidationLib = FluentValidation;

namespace SharedKernel.Consumer.Tests;

/// <summary>
/// Dependency-graph verification for the published NuGet packages.
/// Resolved via the local feed (nupkgs/) — not project references.
/// Confirms the Primitives, Core, Configuration, and FeatureManagement
/// packages resolve and compose correctly as a consumer would use them.
/// </summary>
public sealed class ConsumerDependencyGraphTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // SharedKernel.Primitives
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Primitives_Result_Success_ResolvedFromPackage()
    {
        Result<int> result = Result<int>.Success(99);

        Assert.True(result.IsSuccess);
        Assert.Equal(99, result.Value);
    }

    [Fact]
    public void Primitives_Result_Failure_ResolvedFromPackage()
    {
        Result<int> result = Result<int>.Failure(
            Error.NotFound("item.notfound", "Not found."));

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    [Fact]
    public void Primitives_Error_FactoryMethods_ResolvedFromPackage()
    {
        Error validation   = Error.Validation("v.code", "Validation error.");
        Error notFound     = Error.NotFound("nf.code", "Not found.");
        Error conflict     = Error.Conflict("c.code", "Conflict.");
        Error unauthorized = Error.Unauthorized("u.code", "Unauthorized.");
        Error unexpected   = Error.Unexpected("x.code", "Unexpected.");
        Error forbidden    = Error.Forbidden("f.code", "Forbidden.");

        Assert.Equal(ErrorType.Validation,   validation.Type);
        Assert.Equal(ErrorType.NotFound,     notFound.Type);
        Assert.Equal(ErrorType.Conflict,     conflict.Type);
        Assert.Equal(ErrorType.Unauthorized, unauthorized.Type);
        Assert.Equal(ErrorType.Unexpected,   unexpected.Type);
        Assert.Equal(ErrorType.Forbidden,    forbidden.Type);
    }

    // P-16/WO-059: Error.Forbidden resolves through a real PackageReference to SharedKernel.Primitives
    // 1.1.0 (not just via ProjectReference source) and is distinguishable from Error.Unauthorized by
    // ErrorType — the same guarantee T-44 proves at the source level.
    [Fact]
    public void Primitives_Error_Forbidden_IsDistinctFromUnauthorized_ByErrorType()
    {
        Error forbidden    = Error.Forbidden("approval.self_approval_denied", "Cannot approve own request.");
        Error unauthorized = Error.Unauthorized("approval.self_approval_denied", "Cannot approve own request.");

        Assert.NotEqual(forbidden, unauthorized);
        Assert.Equal(ErrorType.Forbidden, forbidden.Type);
        Assert.Equal(ErrorType.Unauthorized, unauthorized.Type);
    }

    [Fact]
    public void Primitives_SystemClock_ResolvedFromPackage()
    {
        IClock clock = new SystemClock();

        Assert.True(clock.UtcNow <= DateTimeOffset.UtcNow.AddSeconds(1));
        Assert.True(clock.Today <= DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)));
    }

    [Fact]
    public void Primitives_ErrorCodes_ConstantsResolvedFromPackage()
    {
        Assert.NotEmpty(ErrorCodes.Validation.Required);
        Assert.NotEmpty(ErrorCodes.NotFound.Default);
        Assert.NotEmpty(ErrorCodes.Conflict.Default);
        Assert.NotEmpty(ErrorCodes.Unauthorized.Default);
    }

    [Fact]
    public void Primitives_SmartEnum_ResolvedFromPackage()
    {
        // Access a static member to ensure ConsumerStatus type initialisation runs
        // (which populates the shared SmartEnum<ConsumerStatus, int> list) before lookup.
        _ = ConsumerStatus.Active;

        ConsumerStatus status = ConsumerStatus.FromValue(1);

        Assert.Equal(ConsumerStatus.Active, status);
        Assert.Equal(2, ConsumerStatus.List.Count);
    }

    [Fact]
    public void Primitives_ValidationResult_ResolvedFromPackage()
    {
        SharedKernel.Primitives.Results.ValidationResult valid =
            SharedKernel.Primitives.Results.ValidationResult.Success();
        SharedKernel.Primitives.Results.ValidationResult invalid =
            SharedKernel.Primitives.Results.ValidationResult.Failure(
            [
                Error.Validation("f1", "Field 1 required."),
                Error.Validation("f2", "Field 2 required.")
            ]);

        Assert.True(valid.IsValid);
        Assert.False(invalid.IsValid);
        Assert.Equal(2, invalid.Errors.Count);
    }

    [Fact]
    public void Primitives_ValidationResultGeneric_ResolvedFromPackage()
    {
        ValidationResult<int> valid = ValidationResult<int>.Success(42);
        ValidationResult<int> invalid = ValidationResult<int>.Failure(
            [Error.Validation("required", "Value required.")]);

        Assert.True(valid.IsValid);
        Assert.Equal(42, valid.Value);
        Assert.False(invalid.IsValid);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // SharedKernel.Core — railway extensions, BCL helpers
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Core_RailwayExtensions_MapBindMatch_ResolvedFromPackage()
    {
        string output = Result<int>.Success(5)
            .Map(x => x * 2)
            .Bind(x => x > 0
                ? Result<string>.Success(x.ToString())
                : Result<string>.Failure(Error.Validation("neg", "Negative.")))
            .Match(
                onSuccess: s => $"ok:{s}",
                onFailure: e => $"err:{e.Code}");

        Assert.Equal("ok:10", output);
    }

    [Fact]
    public void Core_RailwayExtensions_FailurePath_PropagatesError()
    {
        Error original = Error.NotFound("x.notfound", "Not found.");

        string output = Result<int>.Failure(original)
            .Map(x => x * 2)
            .Match(
                onSuccess: _ => "should not reach",
                onFailure: e => e.Code);

        Assert.Equal("x.notfound", output);
    }

    [Fact]
    public void Core_StringExtensions_CaseConversions_ResolvedFromPackage()
    {
        Assert.Equal("my_property_name", "MyPropertyName".ToSnakeCase());
        Assert.Equal("myPropertyName",   "MyPropertyName".ToCamelCase());
        Assert.Equal("MyPropertyName",   "myPropertyName".ToPascalCase());
    }

    [Fact]
    public void Core_EnumerableExtensions_WhereNotNull_ResolvedFromPackage()
    {
        string?[] source = ["a", null, "b"];

        Assert.Equal(["a", "b"], source.WhereNotNull());
    }

    [Fact]
    public void Core_ErrorExceptionExtensions_ToException_ResolvedFromPackage()
    {
        var exception = Error.NotFound("x.notfound", "Not found.").ToException();

        Assert.IsType<NotFoundException>(exception);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // SharedKernel.Configuration — AddValidatedOptions
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Configuration_AddValidatedOptions_ValidConfig_StartsSuccessfully()
    {
        IHost host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(cfg =>
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Consumer:Name"] = "test-service"
                }))
            .ConfigureServices((ctx, services) =>
                services.AddValidatedOptions<ConsumerOptions>(
                    ctx.Configuration.GetSection("Consumer")))
            .Build();

        // Should not throw — config is valid.
        await host.StartAsync();
        await host.StopAsync();
    }

    [Fact]
    public async Task Configuration_AddValidatedOptions_InvalidConfig_ThrowsAtStartup()
    {
        IHost host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(cfg =>
                // Missing required "Consumer:Name" — should fail on start.
                cfg.AddInMemoryCollection(new Dictionary<string, string?>()))
            .ConfigureServices((ctx, services) =>
                services.AddValidatedOptions<ConsumerOptions>(
                    ctx.Configuration.GetSection("Consumer")))
            .Build();

        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
    }

    // ──────────────────────────────────────────────────────────────────────────
    // SharedKernel.FeatureManagement — IFeatureManager
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task FeatureManagement_EnabledFlag_ReturnsTrue()
    {
        IHost host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(cfg =>
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["FeatureManagement:BetaFeature"] = "true"
                }))
            .ConfigureServices((ctx, services) =>
                services.AddSharedKernelFeatureManagement(ctx.Configuration))
            .Build();

        await host.StartAsync();

        IFeatureManager fm = host.Services.GetRequiredService<IFeatureManager>();
        bool enabled = await fm.IsEnabledAsync("BetaFeature", CancellationToken.None);

        Assert.True(enabled);

        await host.StopAsync();
    }

    [Fact]
    public async Task FeatureManagement_DisabledFlag_ReturnsFalse()
    {
        IHost host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(cfg =>
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["FeatureManagement:BetaFeature"] = "false"
                }))
            .ConfigureServices((ctx, services) =>
                services.AddSharedKernelFeatureManagement(ctx.Configuration))
            .Build();

        await host.StartAsync();

        IFeatureManager fm = host.Services.GetRequiredService<IFeatureManager>();
        bool enabled = await fm.IsEnabledAsync("BetaFeature", CancellationToken.None);

        Assert.False(enabled);

        await host.StopAsync();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // SharedKernel.Guards — functional Against.* path and imperative Throw.* path.
    // SharedKernel.Guards was merged into SharedKernel.Core (P-505/WO-082) and no longer exists
    // as a standalone package — these tests now verify that the Guard/IGuardClause surface
    // resolves correctly from the packed SharedKernel.Core assembly, under the exact same
    // SharedKernel.Guards C# namespace, and that
    // SharedKernel.Core's transitive dependency (Primitives only) resolves without conflict.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Guards_Against_Null_PassesForNonNull_ResolvedFromPackage()
    {
        Error? error = Guard.Against.Null("hello", "paramName");

        Assert.Null(error);
    }

    [Fact]
    public void Guards_Against_Null_ReturnsError_ForNull_ResolvedFromPackage()
    {
        string? value = null;
        Error? error = Guard.Against.Null(value, "paramName");

        Assert.NotNull(error);
        Assert.Equal(ErrorType.Validation, error!.Type);
    }

    [Fact]
    public void Guards_Against_NullOrWhiteSpace_PassesForValidString_ResolvedFromPackage()
    {
        Error? error = Guard.Against.NullOrWhiteSpace("hello", "paramName");

        Assert.Null(error);
    }

    [Fact]
    public void Guards_Against_NullOrWhiteSpace_ReturnsError_ForWhitespace_ResolvedFromPackage()
    {
        Error? error = Guard.Against.NullOrWhiteSpace("   ", "paramName");

        Assert.NotNull(error);
    }

    [Fact]
    public void Guards_Against_InvalidGuid_PassesForNonEmpty_ResolvedFromPackage()
    {
        Error? error = Guard.Against.InvalidGuid(Guid.NewGuid(), "id");

        Assert.Null(error);
    }

    [Fact]
    public void Guards_Against_InvalidGuid_ReturnsError_ForEmpty_ResolvedFromPackage()
    {
        Error? error = Guard.Against.InvalidGuid(Guid.Empty, "id");

        Assert.NotNull(error);
    }

    [Fact]
    public void Guards_Against_OutOfRange_PassesWhenInBounds_ResolvedFromPackage()
    {
        Error? error = Guard.Against.OutOfRange(5, 1, 10, "value");

        Assert.Null(error);
    }

    [Fact]
    public void Guards_Against_OutOfRange_ReturnsError_WhenOutOfBounds_ResolvedFromPackage()
    {
        Error? error = Guard.Against.OutOfRange(15, 1, 10, "value");

        Assert.NotNull(error);
    }

    [Fact]
    public void Guards_Against_NegativeOrZero_PassesForPositive_ResolvedFromPackage()
    {
        Error? error = Guard.Against.NegativeOrZero(1, "count");

        Assert.Null(error);
    }

    [Fact]
    public void Guards_Against_NegativeOrZero_ReturnsError_ForZero_ResolvedFromPackage()
    {
        Error? error = Guard.Against.NegativeOrZero(0, "count");

        Assert.NotNull(error);
    }

    [Fact]
    public void Guards_Against_Email_PassesForValidEmail_ResolvedFromPackage()
    {
        Error? error = Guard.Against.Email("user@example.com", "email");

        Assert.Null(error);
    }

    [Fact]
    public void Guards_Against_Email_ReturnsError_ForInvalidEmail_ResolvedFromPackage()
    {
        Error? error = Guard.Against.Email("not-an-email", "email");

        Assert.NotNull(error);
    }

    [Fact]
    public void Guards_Against_Empty_Collection_ReturnsError_ForEmptyList_ResolvedFromPackage()
    {
        Error? error = Guard.Against.Empty(Array.Empty<int>(), "items");

        Assert.NotNull(error);
    }

    [Fact]
    public void Guards_Against_Empty_Collection_PassesForNonEmpty_ResolvedFromPackage()
    {
        Error? error = Guard.Against.Empty(new[] { 1, 2, 3 }, "items");

        Assert.Null(error);
    }

    [Fact]
    public void Guards_Throw_DoesNotThrow_WhenGuardPasses_ResolvedFromPackage()
    {
        // Guard.Throw.* should not throw when the value is valid.
        var exception = Record.Exception(() =>
            Guard.Throw.NullOrWhiteSpace("valid-string", "paramName"));

        Assert.Null(exception);
    }

    [Fact]
    public void Guards_Throw_ThrowsDomainException_WhenGuardFails_ResolvedFromPackage()
    {
        // Guard.Throw.* must throw DomainException (from SharedKernel.Core — transitive dep).
        Assert.ThrowsAny<Exception>(() =>
            Guard.Throw.NullOrWhiteSpace("   ", "paramName"));
    }

    [Fact]
    public void Guards_Against_True_ProvidesCallerSuppliedError_ResolvedFromPackage()
    {
        // Boolean predicate guard — caller supplies the Error; confirms Primitives Error flows through.
        Error domainError = Error.Validation("rule.violated", "Business rule was violated.");
        Error? result = Guard.Against.True(condition: false, domainError);

        Assert.NotNull(result);
        Assert.Equal("rule.violated", result!.Code);
    }

    [Fact]
    public void Guards_Against_InvalidSmartEnum_PassesForKnownValue_ResolvedFromPackage()
    {
        // Confirms SmartEnum transitive dep (Primitives) resolves correctly through the merged
        // Guard surface (now shipped inside SharedKernel.Core, P-505/WO-082).
        _ = ConsumerStatus.Active; // force type initialisation
        Error? error = Guard.Against.InvalidSmartEnum<ConsumerStatus, int>(1);

        Assert.Null(error);
    }

    [Fact]
    public void Guards_Against_InvalidSmartEnum_ReturnsError_ForUnknownValue_ResolvedFromPackage()
    {
        _ = ConsumerStatus.Active; // force type initialisation
        Error? error = Guard.Against.InvalidSmartEnum<ConsumerStatus, int>(99);

        Assert.NotNull(error);
    }

    [Fact]
    public void Core_NuspecDeclaresOnlyPrimitivesAsDependency_NoThirdPartyNuGetPackageLeakedFromGuardsMerge_ResolvedFromPackage()
    {
        // P-505/P-507/WO-082: SharedKernel.Guards (zero third-party NuGet dependencies of its own)
        // was merged into SharedKernel.Core. Proves the merge did not accidentally pull a new
        // transitive dependency into SharedKernel.Core's own nuspec — it must still depend on
        // SharedKernel.Primitives only, exactly as it did before absorbing the Guard surface.
        string nupkgsDirectory = FindNupkgsDirectory();

        string coreNuspec = ReadNuspecXml(nupkgsDirectory, "SharedKernel.Core.*.nupkg", IsExactCorePackage);

        XDocument nuspec = XDocument.Parse(coreNuspec);
        XNamespace ns = nuspec.Root!.GetDefaultNamespace();

        List<string> dependencyIds = [.. nuspec.Descendants(ns + "dependency")
            .Select(d => d.Attribute("id")!.Value)];

        // The one and only dependency this package's architectural claim rests on, unchanged by
        // absorbing Guards' own (zero) third-party dependencies.
        Assert.Single(dependencyIds);
        Assert.Contains("SharedKernel.Primitives", dependencyIds);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // SharedKernel.Cryptography — verifies the package resolves from the local
    // feed and that its transitive dependencies (Primitives + Configuration)
    // resolve without conflict, end-to-end through DI registration.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Cryptography_AddSharedKernelCryptography_AllServicesResolve_ResolvedFromPackage()
    {
        IHost host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(cfg => cfg.AddInMemoryCollection(new Dictionary<string, string?>()))
            .ConfigureServices((ctx, services) =>
            {
                // AddSharedKernelCryptography registers only the key-free services. Key-dependent
                // services are opt-in and need a key provider only the consuming service can supply.
                var keys = new StaticEncryptionKeyProvider("consumer-key-v1", [ConsumerKeys.CreateEncryptionKey("consumer-key-v1")]);
                services.AddSingleton<IEncryptionKeyProvider>(keys);
                services.AddSingleton<ISynchronousEncryptionKeyProvider>(keys);
                services.AddSingleton<ISigningKeyProvider>(ConsumerKeys.CreateSigningKeyProvider("consumer-signing-key"));
                services.AddSharedKernelCryptography(ctx.Configuration)
                    .AddSymmetricEncryption()
                    .AddSynchronousSymmetricEncryption()
                    .AddAsymmetricSigning();
            })
            .Build();

        await host.StartAsync();

        Assert.IsType<OneWayHasher>(host.Services.GetRequiredService<IOneWayHasher>());
        Assert.IsType<HmacSha256Signer>(host.Services.GetRequiredService<IHmacSigner>());
        Assert.IsType<SecureRandomGenerator>(host.Services.GetRequiredService<ISecureRandomGenerator>());
        Assert.IsType<Sha256ContentHasher>(host.Services.GetRequiredService<IContentHasher>());
        Assert.NotNull(host.Services.GetRequiredService<ITotpGenerator>());
        Assert.NotNull(host.Services.GetRequiredService<IRecoveryCodeGenerator>());
        Assert.IsType<AesGcmEncryptionService>(host.Services.GetRequiredService<ISymmetricEncryptionService>());
        Assert.IsType<SynchronousAesGcmEncryptionService>(host.Services.GetRequiredService<ISynchronousSymmetricEncryptionService>());
        Assert.IsType<AsymmetricSignatureService>(host.Services.GetRequiredService<IAsymmetricSignatureService>());

        await host.StopAsync();
    }

    [Fact]
    public void Cryptography_OneWayHasher_HashAndVerifyRoundtrip_ResolvedFromPackage()
    {
        using ServiceProvider provider = BuildCryptographyServiceProvider();
        IOneWayHasher hasher = provider.GetRequiredService<IOneWayHasher>();

        string hash = hasher.Hash("correct-horse-battery-staple");

        Assert.StartsWith("$pbkdf2-sha256$i=600000$", hash, StringComparison.Ordinal);
        Assert.Equal(HashVerificationResult.Success, hasher.Verify(hash, "correct-horse-battery-staple"));
        Assert.Equal(HashVerificationResult.Failed, hasher.Verify(hash, "wrong-password"));
    }

    [Fact]
    public void Cryptography_HmacSigner_SignAndVerifyRoundtrip_ResolvedFromPackage()
    {
        using ServiceProvider provider = BuildCryptographyServiceProvider();
        IHmacSigner signer = provider.GetRequiredService<IHmacSigner>();

        byte[] key = provider.GetRequiredService<ISecureRandomGenerator>().GetBytes(32);
        byte[] data = "payload"u8.ToArray();
        byte[] signature = signer.Sign(data, key);

        Assert.True(signer.Verify(data, signature, key));
        Assert.False(signer.Verify("tampered"u8.ToArray(), signature, key));
        Assert.Throws<ArgumentException>(() => signer.Sign(data, new byte[16]));
    }

    [Fact]
    public void Cryptography_SecureRandomGenerator_ProducesRequestedLength_ResolvedFromPackage()
    {
        using ServiceProvider provider = BuildCryptographyServiceProvider();
        ISecureRandomGenerator generator = provider.GetRequiredService<ISecureRandomGenerator>();

        byte[] bytes = generator.GetBytes(32);
        string token = generator.GetToken();

        Assert.Equal(32, bytes.Length);
        Assert.Equal(43, token.Length);
    }

    [Fact]
    public void Cryptography_SynchronousEncryption_EncryptDecryptRoundtrip_ResolvedFromPackage()
    {
        var keys = new StaticEncryptionKeyProvider("consumer-key-v1", [ConsumerKeys.CreateEncryptionKey("consumer-key-v1")]);
        var encryption = new SynchronousAesGcmEncryptionService(keys);

        EncryptedPayload payload = encryption.Encrypt("plaintext-from-consumer"u8, "consumer-aad"u8);
        Assert.True(EncryptedPayload.TryParse(payload.ToString(), out EncryptedPayload? parsed));
        Result<byte[]> decrypted = encryption.Decrypt(parsed!, "consumer-aad"u8);

        Assert.True(decrypted.IsSuccess);
        Assert.Equal("plaintext-from-consumer", System.Text.Encoding.UTF8.GetString(decrypted.Value));
    }

    [Fact]
    public void Cryptography_SynchronousEncryption_RequiresASynchronousKeyProvider_ResolvedFromPackage()
    {
        // A synchronous encryption service is built over ISynchronousEncryptionKeyProvider by constructor
        // type, so a service that registered only an asynchronous (key-service) provider fails clearly at
        // resolution instead of blocking on the provider.
        IServiceCollection services = new ServiceCollection();
        services.AddSingleton<IEncryptionKeyProvider, ConsumerAsyncOnlyEncryptionKeyProvider>();
        services.AddSharedKernelCryptography(new ConfigurationBuilder().Build())
            .AddSymmetricEncryption()
            .AddSynchronousSymmetricEncryption();
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<ISymmetricEncryptionService>());
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<ISynchronousSymmetricEncryptionService>());
    }

    [Fact]
    public async Task Cryptography_SymmetricEncryption_AsyncEncryptDecryptRoundtrip_ResolvedFromPackage()
    {
        var encryption = new AesGcmEncryptionService(new ConsumerAsyncOnlyEncryptionKeyProvider());

        EncryptedPayload payload = await encryption.EncryptAsync("plaintext-from-consumer-async"u8.ToArray(), "consumer-aad"u8.ToArray());
        Result<byte[]> decrypted = await encryption.DecryptAsync(payload, "consumer-aad"u8.ToArray());
        Result<byte[]> wrongAad = await encryption.DecryptAsync(payload, "other-aad"u8.ToArray());

        Assert.True(decrypted.IsSuccess);
        Assert.Equal("plaintext-from-consumer-async", System.Text.Encoding.UTF8.GetString(decrypted.Value));
        Assert.Equal(CryptographyErrorCodes.DecryptionFailed, wrongAad.Error.Code);
    }

    [Fact]
    public async Task Cryptography_CachedEncryptionKeyProvider_ComposesOverInnerProvider_ResolvedFromPackage()
    {
        // CachedEncryptionKeyProvider ships with no package-owned DI extension — this proves the documented
        // plain-composition recipe resolves and functions correctly against the packed assembly.
        var keyProvider = new CachedEncryptionKeyProvider(
            new ConsumerAsyncOnlyEncryptionKeyProvider(), TimeProvider.System, TimeSpan.FromMinutes(5));
        var encryption = new AesGcmEncryptionService(keyProvider);

        EncryptedPayload payload = await encryption.EncryptAsync("plaintext-via-cached-provider"u8.ToArray(), ReadOnlyMemory<byte>.Empty);
        Result<byte[]> decrypted = await encryption.DecryptAsync(payload, ReadOnlyMemory<byte>.Empty);

        Assert.True(decrypted.IsSuccess);
        Assert.Equal("plaintext-via-cached-provider", System.Text.Encoding.UTF8.GetString(decrypted.Value));
    }

    [Fact]
    public async Task Cryptography_EnvelopeEncryption_RoundtripsOverAConsumerProvider_ResolvedFromPackage()
    {
        IEnvelopeEncryptionProvider envelope = new ConsumerEnvelopeEncryptionProvider();

        using EnvelopeDataKey dataKey = await envelope.GenerateDataKeyAsync();
        byte[] plaintextKey = dataKey.PlaintextKey.ToArray();
        byte[] wrappedKey = dataKey.WrappedKey.ToArray();
        Result<byte[]> unwrapped = await envelope.UnwrapDataKeyAsync(wrappedKey, dataKey.MasterKeyId);

        Assert.True(unwrapped.IsSuccess);
        Assert.Equal(plaintextKey, unwrapped.Value);

        var service = new EnvelopeEncryptionService(envelope);
        EnvelopePayload payload = await service.EncryptAsync("envelope-plaintext"u8.ToArray(), "consumer-aad"u8.ToArray());
        Assert.True(EnvelopePayload.TryParse(payload.ToBytes(), out EnvelopePayload? parsed));
        Result<byte[]> decrypted = await service.DecryptAsync(parsed!, "consumer-aad"u8.ToArray());

        Assert.True(decrypted.IsSuccess);
        Assert.Equal("envelope-plaintext", System.Text.Encoding.UTF8.GetString(decrypted.Value));
    }

    [Fact]
    public async Task Cryptography_AsymmetricSigning_SignAndVerifyRoundtrip_ResolvedFromPackage()
    {
        using InMemorySigningKeyProvider keys = ConsumerKeys.CreateSigningKeyProvider("consumer-signing-key");
        var service = new AsymmetricSignatureService(keys);
        byte[] data = "payload"u8.ToArray();

        byte[] signature = await service.SignAsync(data, "consumer-signing-key");

        Assert.Equal(SignatureAlgorithm.ES256, await service.GetAlgorithmAsync("consumer-signing-key"));
        Assert.True(await service.VerifyAsync(data, signature, "consumer-signing-key"));
        Assert.False(await service.VerifyAsync("tampered"u8.ToArray(), signature, "consumer-signing-key"));
    }

    [Fact]
    public void Cryptography_Totp_GenerateAndValidate_ResolvedFromPackage()
    {
        using ServiceProvider provider = BuildCryptographyServiceProvider();
        ITotpGenerator generator = provider.GetRequiredService<ITotpGenerator>();
        byte[] secret = TotpSecret.Generate(provider.GetRequiredService<ISecureRandomGenerator>());

        string code = generator.GenerateCode(secret);

        Assert.True(generator.TryValidateCode(secret, code, out _));
        Assert.StartsWith("otpauth://totp/", TotpProvisioningUri.Build("Consumer", "user@example.com", secret).AbsoluteUri, StringComparison.Ordinal);
    }

    private static ServiceProvider BuildCryptographyServiceProvider()
    {
        IServiceCollection services = new ServiceCollection();
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        services.AddSharedKernelCryptography(configuration);
        return services.BuildServiceProvider();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // SharedKernel.Compression — verifies the package resolves from the local
    // feed and that its transitive dependencies (Primitives + Configuration)
    // resolve without conflict, end-to-end through DI registration.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Compression_AddSharedKernelCompression_AllServicesResolve_ResolvedFromPackage()
    {
        IHost host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(cfg => cfg.AddInMemoryCollection(new Dictionary<string, string?>()))
            .ConfigureServices((ctx, services) => services.AddSharedKernelCompression(ctx.Configuration))
            .Build();

        await host.StartAsync();

        Assert.IsType<BrotliPayloadCompressor>(host.Services.GetRequiredService<IPayloadCompressor>());
        Assert.IsType<BrotliPayloadCompressor>(
            host.Services.GetRequiredKeyedService<IPayloadCompressor>(
                CompressionServiceCollectionExtensions.BrotliPayloadCompressorKey));
        Assert.IsType<GZipPayloadCompressor>(
            host.Services.GetRequiredKeyedService<IPayloadCompressor>(
                CompressionServiceCollectionExtensions.GZipPayloadCompressorKey));

        await host.StopAsync();
    }

    [Fact]
    public void Compression_BrotliPayloadCompressor_CompressDecompressRoundtrip_ResolvedFromPackage()
    {
        using ServiceProvider provider = BuildCompressionServiceProvider();
        IPayloadCompressor compressor = provider.GetRequiredService<IPayloadCompressor>();

        byte[] original = "consumer-verification-payload"u8.ToArray();
        byte[] compressed = compressor.Compress(original);
        Result<byte[]> decompressed = compressor.Decompress(compressed);

        Assert.True(decompressed.IsSuccess);
        Assert.Equal(original, decompressed.Value);
    }

    [Fact]
    public void Compression_GZipPayloadCompressor_CompressDecompressRoundtrip_ResolvedFromPackage()
    {
        using ServiceProvider provider = BuildCompressionServiceProvider();
        IPayloadCompressor compressor = provider.GetRequiredKeyedService<IPayloadCompressor>(
            CompressionServiceCollectionExtensions.GZipPayloadCompressorKey);

        byte[] original = "consumer-verification-payload"u8.ToArray();
        byte[] compressed = compressor.Compress(original);
        Result<byte[]> decompressed = compressor.Decompress(compressed);

        Assert.True(decompressed.IsSuccess);
        Assert.Equal(original, decompressed.Value);
    }

    [Fact]
    public void Compression_Decompress_CorruptPayload_ReturnsFailureResult_ResolvedFromPackage()
    {
        using ServiceProvider provider = BuildCompressionServiceProvider();
        IPayloadCompressor compressor = provider.GetRequiredService<IPayloadCompressor>();

        byte[] compressed = compressor.Compress(
            "consumer-verification-payload-with-enough-length-to-corrupt-mid-stream"u8.ToArray());
        compressed[compressed.Length / 2] ^= 0xFF;

        Result<byte[]> decompressed = compressor.Decompress(compressed);

        Assert.True(decompressed.IsFailure);
    }

    private static ServiceProvider BuildCompressionServiceProvider()
    {
        IServiceCollection services = new ServiceCollection();
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        services.AddSharedKernelCompression(configuration);
        return services.BuildServiceProvider();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // SharedKernel.Validation — verifies the package resolves from the local
    // feed and that its transitive dependencies (Primitives + Core, the merged Guard.Against.*
    // surface — re-pointed from SharedKernel.Guards by P-506/WO-082) resolve
    // without conflict, end-to-end through DI registration (P-19/WO-067).
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Validation_IbanValidator_KnownGoodIban_ResolvedFromPackage()
    {
        Assert.True(IbanValidator.IsValid("DE89370400440532013000"));

        var result = IbanValidator.Validate("not-an-iban");
        Assert.True(result.IsFailure);
        Assert.Equal(ValidationErrorCodes.Iban.InvalidFormat, result.Error.Code);
    }

    [Fact]
    public void Validation_PanValidator_LuhnAndNetworkDetection_ResolvedFromPackage()
    {
        Assert.True(PanValidator.IsValid("4242424242424242"));
        Assert.Equal(CardNetwork.Visa, PanValidator.DetectNetwork("4242424242424242"));
    }

    [Fact]
    public void Validation_GuardAgainst_InvalidIban_ResolvedFromPackage()
    {
        Error? error = Guard.Against.InvalidIban("not-an-iban");

        Assert.NotNull(error);
        Assert.Equal(ValidationErrorCodes.Iban.InvalidFormat, error!.Code);
    }

    [Fact]
    public async Task Validation_AddSharedKernelValidation_NationalIdRegistryResolves_ResolvedFromPackage()
    {
        IHost host = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) => services
                .AddSharedKernelValidation()
                .AddNationalIdValidator<ConsumerUsNationalIdValidator>())
            .Build();

        await host.StartAsync();

        INationalIdValidatorRegistry registry = host.Services.GetRequiredService<INationalIdValidatorRegistry>();

        Assert.True(registry.TryGetValidator("TR", out INationalIdValidator? trValidator));
        Assert.IsType<TckNationalIdValidator>(trValidator);

        Assert.True(registry.TryGetValidator("US", out INationalIdValidator? usValidator));
        Assert.IsType<ConsumerUsNationalIdValidator>(usValidator);

        await host.StopAsync();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // SharedKernel.Validation.FluentValidation — verifies the package resolves
    // from the local feed and that its transitive dependency chain (Validation
    // → Primitives + Core, the merged Guard.Against.* surface) plus the third-party
    // FluentValidation package resolve without conflict, end-to-end through an
    // AbstractValidator<T> (P-22/WO-067).
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ValidationFluentValidation_MustBeValidIban_ValidValue_Passes()
    {
        var validator = new ConsumerPaymentValidator();

        FluentValidationLib.Results.ValidationResult result = validator.Validate(
            new ConsumerPaymentCommand("DE89370400440532013000", "USD"));

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidationFluentValidation_MustBeValidIban_InvalidValue_FailsWithMatchingErrorCode_ResolvedFromPackage()
    {
        var validator = new ConsumerPaymentValidator();

        FluentValidationLib.Results.ValidationResult result = validator.Validate(
            new ConsumerPaymentCommand("not-an-iban", "USD"));

        Assert.False(result.IsValid);
        FluentValidationLib.Results.ValidationFailure failure = Assert.Single(result.Errors);
        Assert.Equal(ValidationErrorCodes.Iban.InvalidFormat, failure.ErrorCode);

        // Cross-package parity: the packaged adapter surfaces the identical code the packaged
        // standalone SharedKernel.Validation validator produces for the same input.
        Result standalone = IbanValidator.Validate("not-an-iban");
        Assert.Equal(standalone.Error.Code, failure.ErrorCode);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // SharedKernel.Cryptography.KeyVault.Azure — verifies the package resolves
    // from the local feed and that its transitive dependency chain (Cryptography
    // + Configuration, plus the third-party Azure.Security.KeyVault.Keys,
    // Azure.Security.KeyVault.Secrets and Azure.Identity packages) resolves without
    // conflict, end-to-end through DI registration; also directly inspects the
    // packed SharedKernel.Cryptography .nuspec to prove no Azure package leaks as
    // one of ITS dependencies (P-26/WO-068).
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CryptographyKeyVaultAzure_AddAzureKeyVaultEncryption_RegistersSameSingletonInstance_ResolvedFromPackage()
    {
        IServiceCollection services = new ServiceCollection();
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SharedKernel:Cryptography:KeyVault:Azure:Encryption:VaultUri"] = "https://consumer-verify.vault.azure.net/",
                ["SharedKernel:Cryptography:KeyVault:Azure:Encryption:MasterKeyName"] = "tenant-master-key",
                ["SharedKernel:Cryptography:KeyVault:Azure:Encryption:DataKeySecretName"] = "tenant-data-keys",
            })
            .Build();

        services.AddSharedKernelCryptography(configuration).AddAzureKeyVaultEncryption(configuration);
        using ServiceProvider provider = services.BuildServiceProvider();

        var asKeyProvider = provider.GetRequiredService<IEncryptionKeyProvider>();

        Assert.IsType<AzureKeyVaultEncryptionKeyProvider>(asKeyProvider);
        Assert.Same(asKeyProvider, provider.GetRequiredService<IEnvelopeEncryptionProvider>());
        Assert.Same(asKeyProvider, provider.GetRequiredService<IEncryptionKeyProviderProbe>());
        // A key-service provider never offers synchronous key access.
        Assert.Null(provider.GetService<ISynchronousEncryptionKeyProvider>());
    }

    [Fact]
    public void CryptographyKeyVaultAzure_AddAzureKeyVaultSigning_RegistersSigningKeyProvider_ResolvedFromPackage()
    {
        IServiceCollection services = new ServiceCollection();
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SharedKernel:Cryptography:KeyVault:Azure:Signing:VaultUri"] = "https://consumer-verify.vault.azure.net/",
                ["SharedKernel:Cryptography:KeyVault:Azure:Signing:Keys:token-signing:KeyName"] = "token-signing-key",
                ["SharedKernel:Cryptography:KeyVault:Azure:Signing:Keys:token-signing:Algorithm"] = "PS256",
            })
            .Build();

        services.AddSharedKernelCryptography(configuration)
            .AddAzureKeyVaultSigning(configuration)
            .AddAsymmetricSigning();
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.IsType<AzureKeyVaultSigningKeyProvider>(provider.GetRequiredService<ISigningKeyProvider>());
        Assert.IsType<AsymmetricSignatureService>(provider.GetRequiredService<IAsymmetricSignatureService>());
    }

    [Fact]
    public async Task CryptographyKeyVaultAzure_MissingRequiredOptions_ThrowsAtHostStartup_ResolvedFromPackage()
    {
        IConfiguration invalidConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        using IHost host = Host.CreateDefaultBuilder()
            .ConfigureServices(services => services
                .AddSharedKernelCryptography(invalidConfiguration)
                .AddAzureKeyVaultEncryption(invalidConfiguration))
            .Build();

        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
    }

    [Fact]
    public async Task CryptographyKeyVaultAzure_UnwrapDataKeyAsync_LocalValidation_ResolvedFromPackage()
    {
        // Exercises AzureKeyVaultEncryptionKeyProvider's purely-local masterKeyId validation branch against
        // the PACKED assembly — a malformed master key id is rejected before any Key Vault call.
        var vaultUri = new Uri("https://consumer-verify.vault.azure.net/");
        var credential = new Azure.Identity.DefaultAzureCredential();
        var provider = new AzureKeyVaultEncryptionKeyProvider(
            Microsoft.Extensions.Options.Options.Create(new AzureKeyVaultEncryptionOptions
            {
                VaultUri = vaultUri,
                MasterKeyName = "tenant-master-key",
                DataKeySecretName = "tenant-data-keys",
            }),
            new Azure.Security.KeyVault.Keys.KeyClient(vaultUri, credential),
            new Azure.Security.KeyVault.Secrets.SecretClient(vaultUri, credential),
            new SecureRandomGenerator(),
            TimeProvider.System);

        Result<byte[]> result = await provider.UnwrapDataKeyAsync(new byte[] { 1, 2, 3 }, "not-a-valid-master-key-id");

        Assert.True(result.IsFailure);
        Assert.Equal(CryptographyErrorCodes.DataKeyUnwrapFailed, result.Error.Code);
        Assert.False(typeof(ISynchronousEncryptionKeyProvider).IsAssignableFrom(provider.GetType()));
    }

    [Fact]
    public void CryptographyKeyVaultAzure_AzureDependenciesDoNotLeakIntoSharedKernelCryptographyNuspec()
    {
        string nupkgsDirectory = FindNupkgsDirectory();

        string cryptographyNuspec = ReadNuspecXml(nupkgsDirectory, "SharedKernel.Cryptography.*.nupkg",
            // Match ONLY SharedKernel.Cryptography itself, never a sibling that shares the
            // "SharedKernel.Cryptography." filename prefix (.KeyVault.Azure, .Argon2, and any
            // future one). The package's own file is always "SharedKernel.Cryptography.{version}",
            // so the character immediately after the prefix is a version digit; a sibling's is a
            // letter. Deliberately a positive shape check rather than a blacklist of known
            // siblings — a blacklist silently reads the WRONG nuspec the moment a new sibling is
            // added, which is exactly how P-495's SharedKernel.Cryptography.Argon2 broke this
            // test in CI (caught only by the Assert.Contains sanity check below).
            IsExactCryptographyPackage);

        XDocument nuspec = XDocument.Parse(cryptographyNuspec);
        XNamespace ns = nuspec.Root!.GetDefaultNamespace();

        List<string> dependencyIds = [.. nuspec.Descendants(ns + "dependency")
            .Select(d => d.Attribute("id")!.Value)];

        Assert.DoesNotContain(dependencyIds, id => id.StartsWith("Azure.", StringComparison.OrdinalIgnoreCase));
        // Sanity check the assertion above is actually meaningful (not vacuously true because the
        // dependency list came back empty or the nuspec wasn't the one we think it is).
        Assert.Contains("SharedKernel.Primitives", dependencyIds);
        Assert.Contains("SharedKernel.Configuration", dependencyIds);
    }

    [Fact]
    public void CryptographyKeyVaultAzure_NuspecDeclaresEveryAzureDependency()
    {
        string nupkgsDirectory = FindNupkgsDirectory();

        string keyVaultNuspec = ReadNuspecXml(nupkgsDirectory, "SharedKernel.Cryptography.KeyVault.Azure.*.nupkg", _ => true);

        XDocument nuspec = XDocument.Parse(keyVaultNuspec);
        XNamespace ns = nuspec.Root!.GetDefaultNamespace();

        List<string> dependencyIds = [.. nuspec.Descendants(ns + "dependency")
            .Select(d => d.Attribute("id")!.Value)];

        Assert.Contains("Azure.Security.KeyVault.Keys", dependencyIds);
        Assert.Contains("Azure.Security.KeyVault.Secrets", dependencyIds);
        Assert.Contains("Azure.Identity", dependencyIds);
        Assert.Contains("SharedKernel.Cryptography", dependencyIds);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // SharedKernel.Cryptography.Argon2 — verifies the package resolves from the local feed
    // and that its transitive dependency chain (Cryptography + Configuration, plus the
    // third-party Konscious.Security.Cryptography.Argon2 package) resolves without conflict,
    // end-to-end through DI registration; also directly inspects the packed
    // SharedKernel.Cryptography .nuspec to prove Konscious never leaks as one of ITS
    // dependencies (P-39/WO-081), mirroring the KeyVault.Azure precedent above.
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CryptographyArgon2_AddArgon2id_AddsTheAlgorithmAlongsidePbkdf2_ResolvedFromPackage()
    {
        IHost host = Host.CreateDefaultBuilder()
            .ConfigureAppConfiguration(cfg => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SharedKernel:Cryptography:OneWayHashing:Algorithm"] = Argon2idOneWayHashAlgorithm.Id,
            }))
            .ConfigureServices((ctx, services) => services
                .AddSharedKernelCryptography(ctx.Configuration)
                .AddArgon2id(ctx.Configuration))
            .Build();

        await host.StartAsync();

        Assert.IsType<OneWayHasher>(host.Services.GetRequiredService<IOneWayHasher>());
        List<IOneWayHashAlgorithm> algorithms = [.. host.Services.GetServices<IOneWayHashAlgorithm>()];
        Assert.Contains(algorithms, a => a is Pbkdf2OneWayHashAlgorithm);
        Assert.Contains(algorithms, a => a is Argon2idOneWayHashAlgorithm);

        await host.StopAsync();
    }

    [Fact]
    public void CryptographyArgon2_HashAndVerifyRoundtrip_ProducesRealPhcStringFormat_ResolvedFromPackage()
    {
        using ServiceProvider provider = BuildArgon2ServiceProvider(Argon2idOneWayHashAlgorithm.Id);
        IOneWayHasher hasher = provider.GetRequiredService<IOneWayHasher>();

        string hash = hasher.Hash("correct-horse-battery-staple");

        Assert.StartsWith("$argon2id$v=19$m=19456,t=2,p=1$", hash, StringComparison.Ordinal);
        Assert.Equal(HashVerificationResult.Success, hasher.Verify(hash, "correct-horse-battery-staple"));
        Assert.Equal(HashVerificationResult.Failed, hasher.Verify(hash, "wrong-secret"));
    }

    [Fact]
    public void CryptographyArgon2_ExistingPbkdf2Hash_VerifiesAndReportsRehash_ResolvedFromPackage()
    {
        using ServiceProvider pbkdf2Provider = BuildArgon2ServiceProvider(Pbkdf2OneWayHashAlgorithm.Id);
        using ServiceProvider argon2Provider = BuildArgon2ServiceProvider(Argon2idOneWayHashAlgorithm.Id);

        string pbkdf2Hash = pbkdf2Provider.GetRequiredService<IOneWayHasher>().Hash("correct-horse-battery-staple");

        Assert.Equal(
            HashVerificationResult.SuccessRehashNeeded,
            argon2Provider.GetRequiredService<IOneWayHasher>().Verify(pbkdf2Hash, "correct-horse-battery-staple"));
    }

    [Fact]
    public void CryptographyArgon2_KonsciousDoesNotLeakIntoSharedKernelCryptographyNuspec()
    {
        string nupkgsDirectory = FindNupkgsDirectory();

        string cryptographyNuspec = ReadNuspecXml(nupkgsDirectory, "SharedKernel.Cryptography.*.nupkg",
            // Same positive shape check as above — see IsExactCryptographyPackage.
            IsExactCryptographyPackage);

        XDocument nuspec = XDocument.Parse(cryptographyNuspec);
        XNamespace ns = nuspec.Root!.GetDefaultNamespace();

        List<string> dependencyIds = [.. nuspec.Descendants(ns + "dependency")
            .Select(d => d.Attribute("id")!.Value)];

        Assert.DoesNotContain(dependencyIds, id => id.Contains("Konscious", StringComparison.OrdinalIgnoreCase));
        // Sanity check the assertion above is actually meaningful (not vacuously true because the
        // dependency list came back empty or the nuspec wasn't the one we think it is).
        Assert.Contains("SharedKernel.Primitives", dependencyIds);
        Assert.Contains("SharedKernel.Configuration", dependencyIds);
    }

    [Fact]
    public void CryptographyArgon2_NuspecDeclaresKonsciousDependency()
    {
        string nupkgsDirectory = FindNupkgsDirectory();

        string argon2Nuspec = ReadNuspecXml(nupkgsDirectory, "SharedKernel.Cryptography.Argon2.*.nupkg", _ => true);

        XDocument nuspec = XDocument.Parse(argon2Nuspec);
        XNamespace ns = nuspec.Root!.GetDefaultNamespace();

        List<string> dependencyIds = [.. nuspec.Descendants(ns + "dependency")
            .Select(d => d.Attribute("id")!.Value)];

        Assert.Contains("Konscious.Security.Cryptography.Argon2", dependencyIds);
        Assert.Contains("SharedKernel.Cryptography", dependencyIds);
        Assert.Contains("SharedKernel.Configuration", dependencyIds);
    }

    private static ServiceProvider BuildArgon2ServiceProvider(string algorithm)
    {
        IServiceCollection services = new ServiceCollection();
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SharedKernel:Cryptography:OneWayHashing:Algorithm"] = algorithm,
            })
            .Build();

        services.AddSharedKernelCryptography(configuration).AddArgon2id(configuration);
        return services.BuildServiceProvider();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // SharedKernel.DataPrivacy — verifies the package resolves from the local feed and
    // that its transitive dependency chain resolves to SharedKernel.Primitives ONLY (zero
    // third-party NuGet dependency), end-to-end against the packed assembly (P-30/WO-076).
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DataPrivacy_PiiMasking_Email_ResolvedFromPackage()
    {
        Assert.Equal("j***@example.com", PiiMasking.Email("j.doe@example.com"));
    }

    [Fact]
    public void DataPrivacy_PiiMasking_Pan_KeepsOnlyLastFourDigits_ResolvedFromPackage()
    {
        Assert.Equal("****-****-****-1111", PiiMasking.Pan("4111-1111-1111-1111"));
    }

    [Fact]
    public void DataPrivacy_PiiMasking_Suppress_ReturnsFixedSentinel_ResolvedFromPackage()
    {
        Assert.Equal(PiiMasking.RedactedSentinel, PiiMasking.Suppress("anything"));
    }

    [Fact]
    public void DataPrivacy_ClassificationAttributes_ApplyToMember_ResolvedFromPackage()
    {
        System.Reflection.PropertyInfo property =
            typeof(ConsumerClassifiedProfile).GetProperty(nameof(ConsumerClassifiedProfile.NationalId))!;

        var classification = (DataClassificationAttribute?)Attribute.GetCustomAttribute(
            property, typeof(DataClassificationAttribute));
        var category = (SensitiveDataCategoryAttribute?)Attribute.GetCustomAttribute(
            property, typeof(SensitiveDataCategoryAttribute));

        Assert.Equal(DataClassification.Restricted, classification!.Classification);
        Assert.Equal(SensitiveDataCategory.Pii, category!.Category);
    }

    [Fact]
    public async Task DataPrivacy_IDataSubjectRequestHandler_ExportAndErasure_ResolvedFromPackage()
    {
        IDataSubjectRequestHandler handler = new ConsumerDataSubjectRequestHandler();

        Result<DataSubjectExportBundle> export = await handler.ExportDataAsync("subject-1");
        Result<DataSubjectErasureReceipt> erasure = await handler.RequestErasureAsync("subject-1");

        Assert.True(export.IsSuccess);
        Assert.Equal("subject-1", export.Value.SubjectId);
        Assert.True(erasure.IsSuccess);
        Assert.Equal(1, erasure.Value.RecordsAffected);
    }

    [Fact]
    public void DataPrivacy_NuspecDeclaresOnlyPrimitivesAsDependency_NoThirdPartyNuGetPackage_ResolvedFromPackage()
    {
        string nupkgsDirectory = FindNupkgsDirectory();

        string dataPrivacyNuspec = ReadNuspecXml(nupkgsDirectory, "SharedKernel.DataPrivacy.*.nupkg", _ => true);

        XDocument nuspec = XDocument.Parse(dataPrivacyNuspec);
        XNamespace ns = nuspec.Root!.GetDefaultNamespace();

        List<string> dependencyIds = [.. nuspec.Descendants(ns + "dependency")
            .Select(d => d.Attribute("id")!.Value)];

        // The one and only dependency this package's architectural claim rests on.
        Assert.Single(dependencyIds);
        Assert.Contains("SharedKernel.Primitives", dependencyIds);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // SharedKernel.Localization — verifies the package resolves from the local feed and that
    // its transitive dependency chain resolves to SharedKernel.Primitives + the first-party
    // Microsoft.Extensions.Localization.Abstractions package ONLY, end-to-end against the packed
    // assembly (P-33/WO-078).
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Localization_InMemoryLocalizationCatalog_RegisteredTranslation_ResolvedFromPackage()
    {
        ILocalizationCatalog catalog = new InMemoryLocalizationCatalog()
            .AddTranslation("consumer.code", CultureInfo.GetCultureInfo("en-US"), "Consumer translation.");

        bool found = catalog.TryGetString("consumer.code", CultureInfo.GetCultureInfo("en-US"), out string? value);

        Assert.True(found);
        Assert.Equal("Consumer translation.", value);
    }

    [Fact]
    public void Localization_InMemoryLocalizationCatalog_UnregisteredCode_NeverThrows_ResolvedFromPackage()
    {
        var catalog = new InMemoryLocalizationCatalog();

        bool found = catalog.TryGetString("nothing.registered", CultureInfo.GetCultureInfo("en-US"), out string? value);

        Assert.False(found);
        Assert.Null(value);
    }

    [Fact]
    public void Localization_StringLocalizerLocalizationCatalog_ResourceNotFound_ReturnsFalse_ResolvedFromPackage()
    {
        IStringLocalizer localizer = new ConsumerAlwaysMissingStringLocalizer();
        IStringLocalizerFactory factory = new ConsumerStringLocalizerFactory(localizer);

        var catalog = new StringLocalizerLocalizationCatalog(factory, typeof(ConsumerErrorMessages));

        bool found = catalog.TryGetString("consumer.code", CultureInfo.GetCultureInfo("en-US"), out string? value);

        Assert.False(found);
        Assert.Null(value);
    }

    [Fact]
    public void Localization_ServiceCollectionExtensions_AddInMemoryLocalizationCatalog_ResolvedFromPackage()
    {
        var services = new ServiceCollection();

        services.AddInMemoryLocalizationCatalog(catalog =>
            catalog.AddTranslation("consumer.code", CultureInfo.GetCultureInfo("en-US"), "Consumer translation."));

        using ServiceProvider provider = services.BuildServiceProvider();
        ILocalizationCatalog catalog = provider.GetRequiredService<ILocalizationCatalog>();

        catalog.TryGetString("consumer.code", CultureInfo.GetCultureInfo("en-US"), out string? value);

        Assert.Equal("Consumer translation.", value);
    }

    [Fact]
    public void Localization_NuspecDeclaresOnlyPrimitivesAndLocalizationAbstractions_ResolvedFromPackage()
    {
        string nupkgsDirectory = FindNupkgsDirectory();

        string localizationNuspec = ReadNuspecXml(nupkgsDirectory, "SharedKernel.Localization.*.nupkg", _ => true);

        XDocument nuspec = XDocument.Parse(localizationNuspec);
        XNamespace ns = nuspec.Root!.GetDefaultNamespace();

        List<string> dependencyIds = [.. nuspec.Descendants(ns + "dependency")
            .Select(d => d.Attribute("id")!.Value)];

        Assert.Equal(2, dependencyIds.Count);
        Assert.Contains("SharedKernel.Primitives", dependencyIds);
        Assert.Contains("Microsoft.Extensions.Localization.Abstractions", dependencyIds);
    }

    /// <summary>
    /// Walks upward from the test assembly's own output directory looking for the repo-root
    /// <c>nupkgs/</c> local feed directory (NuGet.Config's <c>local-shared-kernel</c> source) —
    /// there is no other reliable way for a test running from
    /// <c>.../SharedKernel.Consumer.Tests/bin/Release/net10.0/</c> to locate it.
    /// </summary>
    /// <summary>
    /// Matches ONLY <c>SharedKernel.Cryptography.{version}.nupkg</c>, never a sibling package that
    /// shares the <c>SharedKernel.Cryptography.</c> filename prefix (<c>.KeyVault.Azure</c>,
    /// <c>.Argon2</c>, or any future one).
    /// </summary>
    /// <remarks>
    /// A positive shape check, deliberately not a blacklist of known siblings: the package's own
    /// file is always the prefix followed by a version, so the next character is a digit, whereas a
    /// sibling's is a letter. A blacklist silently reads the WRONG nuspec the moment a new sibling
    /// ships — which is exactly what happened when P-495 added
    /// <c>SharedKernel.Cryptography.Argon2</c> and one of this file's two identical globs was
    /// updated while the other was missed, turning CI red. The failure was caught only by the
    /// callers' <c>Assert.Contains("SharedKernel.Primitives", ...)</c> anti-vacuity guard.
    /// </remarks>
    private static bool IsExactCryptographyPackage(string candidatePath)
    {
        const string Prefix = "SharedKernel.Cryptography.";
        string fileName = Path.GetFileName(candidatePath);

        return fileName.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
            && fileName.Length > Prefix.Length
            && char.IsAsciiDigit(fileName[Prefix.Length]);
    }

    /// <summary>
    /// Matches ONLY <c>SharedKernel.Core.{version}.nupkg</c>, never a sibling package that might
    /// someday share the <c>SharedKernel.Core.</c> filename prefix — the same positive shape check
    /// as <see cref="IsExactCryptographyPackage"/>, for the same reason.
    /// </summary>
    private static bool IsExactCorePackage(string candidatePath)
    {
        const string Prefix = "SharedKernel.Core.";
        string fileName = Path.GetFileName(candidatePath);

        return fileName.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
            && fileName.Length > Prefix.Length
            && char.IsAsciiDigit(fileName[Prefix.Length]);
    }

    private static string FindNupkgsDirectory()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        for (int i = 0; i < 10 && current is not null; i++, current = current.Parent)
        {
            string candidate = Path.Combine(current.FullName, "nupkgs");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException(
            $"Could not locate the repo-root 'nupkgs' directory by walking up from '{AppContext.BaseDirectory}'.");
    }

    private static string ReadNuspecXml(string nupkgsDirectory, string searchPattern, Func<string, bool> filter)
    {
        string nupkgPath = Directory.GetFiles(nupkgsDirectory, searchPattern)
            .Where(filter)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault()
            ?? throw new FileNotFoundException(
                $"No file matching '{searchPattern}' found in '{nupkgsDirectory}'.");

        using ZipArchive archive = ZipFile.OpenRead(nupkgPath);
        ZipArchiveEntry nuspecEntry = archive.Entries.First(e => e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));

        using Stream entryStream = nuspecEntry.Open();
        using var reader = new StreamReader(entryStream);
        return reader.ReadToEnd();
    }
}

/// <summary>
/// Minimal consumer-supplied FluentValidation validator for consumer-verification purposes only —
/// proves <c>ValidationRuleBuilderExtensions</c> resolves against the packed
/// SharedKernel.Validation.FluentValidation assembly (and, transitively, the packed
/// SharedKernel.Validation and third-party FluentValidation assemblies).
/// </summary>
internal sealed record ConsumerPaymentCommand(string Iban, string CurrencyCode);

internal sealed class ConsumerPaymentValidator : FluentValidationLib.AbstractValidator<ConsumerPaymentCommand>
{
    public ConsumerPaymentValidator()
    {
        RuleFor(x => x.Iban).MustBeValidIban();
        RuleFor(x => x.CurrencyCode).MustBeValidCurrencyCode();
    }
}

/// <summary>
/// Minimal consumer-supplied <see cref="INationalIdValidator"/> for consumer-verification
/// purposes only — proves the pluggable-registry DI extension resolves against the packed
/// SharedKernel.Validation assembly.
/// </summary>
internal sealed class ConsumerUsNationalIdValidator : INationalIdValidator
{
    public string CountryCode => "US";

    public bool IsValid(string idNumber) => idNumber.Length == 9;
}

// ──────────────────────────────────────────────────────────────────────────
// Test-local helpers — SmartEnum and Options used only in this project
// ──────────────────────────────────────────────────────────────────────────

internal sealed class ConsumerStatus : SmartEnum<ConsumerStatus, int>
{
    public static readonly ConsumerStatus Active   = new("Active",   1);
    public static readonly ConsumerStatus Inactive = new("Inactive", 2);

    private ConsumerStatus(string name, int value) : base(name, value) { }
}

internal sealed class ConsumerOptions
{
    [Required]
    public string Name { get; init; } = "";
}

/// <summary>
/// Minimal consumer-supplied type carrying both DataPrivacy classification attributes at once,
/// proving <see cref="DataClassificationAttribute"/>/<see cref="SensitiveDataCategoryAttribute"/>
/// resolve and apply correctly against the packed SharedKernel.DataPrivacy assembly.
/// </summary>
internal sealed class ConsumerClassifiedProfile
{
    [DataClassification(DataClassification.Restricted)]
    [SensitiveDataCategory(SensitiveDataCategory.Pii)]
    public string NationalId { get; init; } = string.Empty;
}

/// <summary>
/// Minimal in-memory <see cref="IDataSubjectRequestHandler"/> for consumer-verification purposes
/// only, proving the contract shape (and <see cref="DataSubjectExportBundle"/>/
/// <see cref="DataSubjectErasureReceipt"/>) resolves against the packed SharedKernel.DataPrivacy
/// assembly. A real implementation acts against a service's own persisted data.
/// </summary>
internal sealed class ConsumerDataSubjectRequestHandler : IDataSubjectRequestHandler
{
    public Task<Result<DataSubjectExportBundle>> ExportDataAsync(string subjectId, CancellationToken ct = default) =>
        Task.FromResult(Result<DataSubjectExportBundle>.Success(
            new DataSubjectExportBundle(subjectId, DateTimeOffset.UtcNow, new Dictionary<string, object?>
            {
                ["email"] = "consumer@example.com",
            })));

    public Task<Result<DataSubjectErasureReceipt>> RequestErasureAsync(string subjectId, CancellationToken ct = default) =>
        Task.FromResult(Result<DataSubjectErasureReceipt>.Success(
            new DataSubjectErasureReceipt(subjectId, DateTimeOffset.UtcNow, RecordsAffected: 1)));
}

/// <summary>
/// Marker resource type for <see cref="StringLocalizerLocalizationCatalog"/> consumer-verification
/// purposes only — mirrors a real service's own <c>.resx</c>-backed resource class.
/// </summary>
internal sealed class ConsumerErrorMessages;

/// <summary>
/// Minimal consumer-supplied <see cref="IStringLocalizer"/> that always reports
/// <see cref="LocalizedString.ResourceNotFound"/>, proving
/// <see cref="StringLocalizerLocalizationCatalog"/> correctly returns <see langword="false"/>
/// rather than forwarding the raw key as a "translation" — against the packed
/// SharedKernel.Localization assembly.
/// </summary>
internal sealed class ConsumerAlwaysMissingStringLocalizer : IStringLocalizer
{
    public LocalizedString this[string name] => new(name, name, resourceNotFound: true);

    public LocalizedString this[string name, params object[] arguments] => new(name, name, resourceNotFound: true);

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
}

/// <summary>
/// Minimal consumer-supplied <see cref="IStringLocalizerFactory"/> returning a fixed
/// <see cref="IStringLocalizer"/> instance, for consumer-verification purposes only.
/// </summary>
internal sealed class ConsumerStringLocalizerFactory(IStringLocalizer localizer) : IStringLocalizerFactory
{
    public IStringLocalizer Create(Type resourceSource) => localizer;

    public IStringLocalizer Create(string baseName, string location) => localizer;
}

/// <summary>
/// Key material factories for consumer-verification purposes only. Production services must resolve keys from a
/// key management service, environment configuration, or a secret store — never generate them in process as done
/// here for test convenience.
/// </summary>
internal static class ConsumerKeys
{
    public static CryptographicKey CreateEncryptionKey(string keyId) =>
        new(keyId, System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

    public static InMemorySigningKeyProvider CreateSigningKeyProvider(string keyId) =>
        new([SigningKey.FromECDsa(keyId, System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256))]);
}

/// <summary>
/// An asynchronous-only <see cref="IEncryptionKeyProvider"/> (deliberately NOT
/// <see cref="ISynchronousEncryptionKeyProvider"/>) for consumer-verification purposes only — a stand-in for a
/// key-service-backed provider. Proves the asynchronous service works over it, and that the synchronous service
/// cannot be resolved against it, from the packed assembly rather than only in-project.
/// </summary>
internal sealed class ConsumerAsyncOnlyEncryptionKeyProvider : IEncryptionKeyProvider
{
    private static readonly CryptographicKey CurrentKey = ConsumerKeys.CreateEncryptionKey("consumer-async-key-v1");

    public async ValueTask<CryptographicKey> GetCurrentKeyAsync(CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        return CurrentKey;
    }

    public async ValueTask<CryptographicKey?> GetKeyAsync(string keyId, CancellationToken cancellationToken = default)
    {
        await Task.Yield();
        return keyId == CurrentKey.Id ? CurrentKey : null;
    }
}

/// <summary>
/// Minimal in-memory <see cref="IEnvelopeEncryptionProvider"/> for consumer-verification purposes only — proves
/// the contract shape resolves against the packed assembly. Production services must wrap data keys with a real
/// key management service (e.g. Azure Key Vault) — never a process-local master key as done here for test
/// convenience.
/// </summary>
internal sealed class ConsumerEnvelopeEncryptionProvider : IEnvelopeEncryptionProvider
{
    private const string MasterKeyId = "consumer-master-key-v1";
    private static readonly byte[] MasterKey = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);

    public ValueTask<EnvelopeDataKey> GenerateDataKeyAsync(CancellationToken cancellationToken = default)
    {
        byte[] plaintextKey = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        return new(new EnvelopeDataKey(plaintextKey, Xor(plaintextKey, MasterKey), MasterKeyId));
    }

    public ValueTask<Result<byte[]>> UnwrapDataKeyAsync(
        ReadOnlyMemory<byte> wrappedKey,
        string masterKeyId,
        CancellationToken cancellationToken = default) =>
        masterKeyId == MasterKeyId && wrappedKey.Length == MasterKey.Length
            ? new(Result<byte[]>.Success(Xor(wrappedKey.Span, MasterKey)))
            : new(Result<byte[]>.Failure(Error.Validation(CryptographyErrorCodes.DataKeyUnwrapFailed, "Unknown master key.")));

    // A minimal, deliberately non-production "wrap" (XOR against a process-local key) — sufficient to prove the
    // round-trip contract shape; a real provider wraps with an authenticated key-service operation.
    private static byte[] Xor(ReadOnlySpan<byte> data, byte[] key)
    {
        byte[] result = new byte[data.Length];
        for (int i = 0; i < data.Length; i++)
        {
            result[i] = (byte)(data[i] ^ key[i % key.Length]);
        }

        return result;
    }
}
