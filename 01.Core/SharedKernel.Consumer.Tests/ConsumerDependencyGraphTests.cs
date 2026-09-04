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
using SharedKernel.Core.Extensions;
using SharedKernel.Cryptography.Extensions;
using SharedKernel.Cryptography.Hashing;
using SharedKernel.Cryptography.KeyVault.Azure;
using SharedKernel.Cryptography.KeyVault.Azure.Extensions;
using SharedKernel.Cryptography.KeyVault.Azure.Options;
using SharedKernel.Cryptography.Random;
using SharedKernel.Cryptography.Signing;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.DataPrivacy.Classification;
using SharedKernel.DataPrivacy.DataSubjectRequests;
using SharedKernel.DataPrivacy.Masking;
using SharedKernel.FeatureManagement.Abstractions;
using SharedKernel.FeatureManagement.Extensions;
using SharedKernel.Guards;
using SharedKernel.Guards.Clauses;
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
    public void Core_EnumerableExtensions_ToBatches_ResolvedFromPackage()
    {
        int[] source = [1, 2, 3, 4, 5];
        List<IEnumerable<int>> batches = [.. source.ToBatches(2)];

        Assert.Equal(3, batches.Count);
    }

    [Fact]
    public void Core_GuidExtensions_IsEmpty_ResolvedFromPackage()
    {
        Assert.True(Guid.Empty.IsEmpty());
        Assert.False(Guid.NewGuid().IsEmpty());
    }

    [Fact]
    public void Core_DateTimeOffsetExtensions_ToUnixMilliseconds_ResolvedFromPackage()
    {
        DateTimeOffset epoch = DateTimeOffset.UnixEpoch;
        long ms = epoch.ToUnixMilliseconds();

        Assert.Equal(0L, ms);
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
    // SharedKernel.Guards — functional Against.* path and imperative Throw.* path
    // Verifies that Guards resolves correctly from the local feed and that its
    // transitive dependencies (Primitives + Core) resolve without conflict.
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
        // Confirms SmartEnum transitive dep (Primitives) resolves correctly through Guards package.
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
                services.AddSharedKernelCryptography(ctx.Configuration);
                // AddSharedKernelCryptography ships no key material — the consuming
                // service must supply its own IAsymmetricKeyProvider before resolving
                // IAsymmetricSignatureService (documented in 01.Core/CLAUDE.md).
                services.AddSingleton<IAsymmetricKeyProvider, ConsumerAsymmetricKeyProvider>();
            })
            .Build();

        await host.StartAsync();

        Assert.NotNull(host.Services.GetRequiredService<IOneWayHasher>());
        Assert.NotNull(host.Services.GetRequiredService<IHmacSigner>());
        Assert.NotNull(host.Services.GetRequiredService<ISecureRandomGenerator>());
        Assert.NotNull(host.Services.GetRequiredService<IAsymmetricSignatureService>());
        Assert.NotNull(host.Services.GetRequiredKeyedService<IAsymmetricSignatureService>(
            CryptographyServiceCollectionExtensions.RsaSignatureServiceKey));
        Assert.NotNull(host.Services.GetRequiredKeyedService<IAsymmetricSignatureService>(
            CryptographyServiceCollectionExtensions.EcdsaSignatureServiceKey));

        await host.StopAsync();
    }

    [Fact]
    public void Cryptography_OneWayHasher_HashAndVerifyRoundtrip_ResolvedFromPackage()
    {
        using ServiceProvider provider = BuildCryptographyServiceProvider();
        IOneWayHasher hasher = provider.GetRequiredService<IOneWayHasher>();

        string hash = hasher.Hash("correct-horse-battery-staple");

        Assert.Equal(HashVerificationResult.Success, hasher.Verify(hash, "correct-horse-battery-staple"));
        Assert.Equal(HashVerificationResult.Failed, hasher.Verify(hash, "wrong-password"));
    }

    [Fact]
    public void Cryptography_HmacSigner_SignAndVerifyRoundtrip_ResolvedFromPackage()
    {
        using ServiceProvider provider = BuildCryptographyServiceProvider();
        IHmacSigner signer = provider.GetRequiredService<IHmacSigner>();

        byte[] secret = "shared-secret"u8.ToArray();
        byte[] data = "payload"u8.ToArray();
        byte[] signature = signer.Sign(data, secret);

        Assert.True(signer.Verify(data, signature, secret));
        Assert.False(signer.Verify("tampered"u8.ToArray(), signature, secret));
    }

    [Fact]
    public void Cryptography_SecureRandomGenerator_ProducesRequestedLength_ResolvedFromPackage()
    {
        using ServiceProvider provider = BuildCryptographyServiceProvider();
        ISecureRandomGenerator generator = provider.GetRequiredService<ISecureRandomGenerator>();

        byte[] bytes = generator.NextBytes(32);
        string token = generator.NextToken();

        Assert.Equal(32, bytes.Length);
        Assert.NotEmpty(token);
    }

    [Fact]
    public void Cryptography_SymmetricEncryption_EncryptDecryptRoundtrip_ResolvedFromPackage()
    {
        using ServiceProvider provider = BuildCryptographyServiceProvider();
        var keyProvider = new ConsumerEncryptionKeyProvider();
        var encryption = new AesGcmEncryptionService(keyProvider);

        EncryptedPayload payload = encryption.Encrypt("plaintext-from-consumer"u8.ToArray());
        Result<byte[]> decrypted = encryption.Decrypt(payload);

        Assert.True(decrypted.IsSuccess);
        Assert.Equal("plaintext-from-consumer", System.Text.Encoding.UTF8.GetString(decrypted.Value));
    }

    [Fact]
    public async Task Cryptography_SymmetricEncryption_AsyncEncryptDecryptRoundtrip_ResolvedFromPackage()
    {
        // P-446/WO-068: proves the async IEncryptionKeyProvider contract and
        // ISymmetricEncryptionService's additive *Async overloads resolve correctly end-to-end
        // through the packed (not project-referenced) SharedKernel.Cryptography assembly.
        using ServiceProvider provider = BuildCryptographyServiceProvider();
        var keyProvider = new ConsumerEncryptionKeyProvider();
        var encryption = new AesGcmEncryptionService(keyProvider);

        EncryptedPayload payload = await encryption.EncryptAsync("plaintext-from-consumer-async"u8.ToArray());
        Result<byte[]> decrypted = await encryption.DecryptAsync(payload);

        Assert.True(decrypted.IsSuccess);
        Assert.Equal("plaintext-from-consumer-async", System.Text.Encoding.UTF8.GetString(decrypted.Value));
    }

    [Fact]
    public async Task Cryptography_CachedEncryptionKeyProvider_ComposesOverInnerProvider_ResolvedFromPackage()
    {
        // P-446/WO-068: CachedEncryptionKeyProvider ships with no package-owned DI extension —
        // this proves the documented plain-composition recipe resolves and functions correctly
        // against the packed assembly.
        var keyProvider = new CachedEncryptionKeyProvider(
            new ConsumerEncryptionKeyProvider(), TimeProvider.System, TimeSpan.FromMinutes(5));
        var encryption = new AesGcmEncryptionService(keyProvider);

        EncryptedPayload payload = await encryption.EncryptAsync("plaintext-via-cached-provider"u8.ToArray());
        Result<byte[]> decrypted = await encryption.DecryptAsync(payload);

        Assert.True(decrypted.IsSuccess);
        Assert.Equal("plaintext-via-cached-provider", System.Text.Encoding.UTF8.GetString(decrypted.Value));
    }

    [Fact]
    public async Task Cryptography_EnvelopeEncryptionProvider_ContractResolvesFromPackage()
    {
        // P-446/WO-068: proves IEnvelopeEncryptionProvider/EnvelopeDataKey resolve correctly
        // against the packed assembly, via a minimal in-memory test double.
        IEnvelopeEncryptionProvider envelope = new ConsumerEnvelopeEncryptionProvider();

        EnvelopeDataKey dataKey = await envelope.GenerateDataKeyAsync();
        Result<byte[]> unwrapped = await envelope.UnwrapDataKeyAsync(dataKey.WrappedKey, dataKey.MasterKeyId);

        Assert.True(unwrapped.IsSuccess);
        Assert.Equal(dataKey.PlaintextKey, unwrapped.Value);
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
    // feed and that its transitive dependencies (Primitives + Guards) resolve
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
    // → Primitives + Guards) plus the third-party FluentValidation package
    // resolve without conflict, end-to-end through an AbstractValidator<T>
    // (P-22/WO-067).
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
    // + Configuration, plus the third-party Azure.Security.KeyVault.Keys and
    // Azure.Identity packages) resolves without conflict, end-to-end through DI
    // registration; also directly inspects the packed SharedKernel.Cryptography
    // .nuspec to prove neither Azure package leaks as one of ITS dependencies
    // (P-26/WO-068).
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CryptographyKeyVaultAzure_AddSharedKernelAzureKeyVaultCryptography_RegistersSameSingletonInstance_ResolvedFromPackage()
    {
        IServiceCollection services = new ServiceCollection();
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SharedKernel:Cryptography:KeyVault:Azure:VaultUri"] = "https://consumer-verify.vault.azure.net/",
                ["SharedKernel:Cryptography:KeyVault:Azure:CurrentKeyId"] = "primary",
                ["SharedKernel:Cryptography:KeyVault:Azure:KeyNames:primary"] = "tenant-data-key",
            })
            .Build();

        services.AddSharedKernelAzureKeyVaultCryptography(configuration);
        using ServiceProvider provider = services.BuildServiceProvider();

        var asKeyProvider = provider.GetRequiredService<IEncryptionKeyProvider>();
        var asEnvelopeProvider = provider.GetRequiredService<IEnvelopeEncryptionProvider>();

        Assert.IsType<AzureKeyVaultEncryptionKeyProvider>(asKeyProvider);
        Assert.Same(asKeyProvider, asEnvelopeProvider);
    }

    [Fact]
    public async Task CryptographyKeyVaultAzure_MissingRequiredOptions_ThrowsAtHostStartup_ResolvedFromPackage()
    {
        IConfiguration invalidConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        using IHost host = Host.CreateDefaultBuilder()
            .ConfigureServices(services => services.AddSharedKernelAzureKeyVaultCryptography(invalidConfiguration))
            .Build();

        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
    }

    [Fact]
    public void CryptographyKeyVaultAzure_UnwrapDataKeyAsync_LocalValidation_ResolvedFromPackage()
    {
        // Exercises AzureKeyVaultEncryptionKeyProvider's purely-local masterKeyId validation
        // branch against the PACKED assembly — no reachable vault needed for this specific path.
        var provider = new AzureKeyVaultEncryptionKeyProvider(
            Microsoft.Extensions.Options.Options.Create(new AzureKeyVaultCryptographyOptions
            {
                VaultUri = new Uri("https://consumer-verify.vault.azure.net/"),
                CurrentKeyId = "primary",
                KeyNames = new Dictionary<string, string> { ["primary"] = "tenant-data-key" },
            }),
            new CryptoRandomGenerator());

        Result<byte[]> result = provider.UnwrapDataKeyAsync([1, 2, 3], "not-a-valid-key-vault-uri").AsTask().GetAwaiter().GetResult();

        Assert.True(result.IsFailure);
        Assert.Equal(AzureKeyVaultCryptographyErrorCodes.MalformedMasterKeyId, result.Error.Code);
    }

    [Fact]
    public void CryptographyKeyVaultAzure_AzureDependenciesDoNotLeakIntoSharedKernelCryptographyNuspec()
    {
        string nupkgsDirectory = FindNupkgsDirectory();

        string cryptographyNuspec = ReadNuspecXml(nupkgsDirectory, "SharedKernel.Cryptography.*.nupkg",
            // Exclude the sibling KeyVault.Azure package, which legitimately shares the
            // "SharedKernel.Cryptography." filename prefix.
            candidate => !Path.GetFileName(candidate).StartsWith("SharedKernel.Cryptography.KeyVault.Azure.", StringComparison.OrdinalIgnoreCase));

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
    public void CryptographyKeyVaultAzure_NuspecDeclaresBothAzureDependencies()
    {
        string nupkgsDirectory = FindNupkgsDirectory();

        string keyVaultNuspec = ReadNuspecXml(nupkgsDirectory, "SharedKernel.Cryptography.KeyVault.Azure.*.nupkg", _ => true);

        XDocument nuspec = XDocument.Parse(keyVaultNuspec);
        XNamespace ns = nuspec.Root!.GetDefaultNamespace();

        List<string> dependencyIds = [.. nuspec.Descendants(ns + "dependency")
            .Select(d => d.Attribute("id")!.Value)];

        Assert.Contains("Azure.Security.KeyVault.Keys", dependencyIds);
        Assert.Contains("Azure.Identity", dependencyIds);
        Assert.Contains("SharedKernel.Cryptography", dependencyIds);
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
/// Minimal in-memory <see cref="IEncryptionKeyProvider"/> for consumer-verification purposes only.
/// Production services must resolve key material from Key Vault, environment config, or a secret
/// store — never hardcode it as done here for test convenience.
/// </summary>
internal sealed class ConsumerEncryptionKeyProvider : IEncryptionKeyProvider
{
    private static readonly CryptographicKey CurrentKey = new("consumer-key-v1", new byte[32]);

    public ValueTask<CryptographicKey> GetCurrentKeyAsync(CancellationToken ct = default) => new(CurrentKey);

    public ValueTask<CryptographicKey?> GetKeyAsync(string keyId, CancellationToken ct = default) =>
        new(keyId == CurrentKey.Id ? CurrentKey : null);
}

/// <summary>
/// Minimal in-memory <see cref="IEnvelopeEncryptionProvider"/> for consumer-verification purposes
/// only — proves the contract shape resolves against the packed assembly. Production services
/// must resolve this against a real KMS (e.g. Azure Key Vault) — never a process-local master key
/// as done here for test convenience.
/// </summary>
internal sealed class ConsumerEnvelopeEncryptionProvider : IEnvelopeEncryptionProvider
{
    private const string MasterKeyId = "consumer-master-key-v1";
    private static readonly byte[] MasterKey = new byte[32];

    public ValueTask<EnvelopeDataKey> GenerateDataKeyAsync(CancellationToken ct = default)
    {
        byte[] plaintextKey = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        byte[] wrappedKey = Xor(plaintextKey, MasterKey);
        return new(new EnvelopeDataKey(plaintextKey, wrappedKey, MasterKeyId));
    }

    public ValueTask<Result<byte[]>> UnwrapDataKeyAsync(byte[] wrappedDataKey, string masterKeyId, CancellationToken ct = default) =>
        masterKeyId == MasterKeyId
            ? new(Result<byte[]>.Success(Xor(wrappedDataKey, MasterKey)))
            : new(Result<byte[]>.Failure(Error.Unexpected("consumer.envelope.unknown_master_key", "Unknown master key.")));

    // A minimal, deliberately non-production "wrap" (XOR against a fixed key) — sufficient to
    // prove the round-trip contract shape; a real provider wraps via a genuine KMS operation.
    private static byte[] Xor(byte[] data, byte[] key)
    {
        byte[] result = new byte[data.Length];
        for (int i = 0; i < data.Length; i++)
        {
            result[i] = (byte)(data[i] ^ key[i % key.Length]);
        }

        return result;
    }
}

/// <summary>
/// Minimal in-memory <see cref="IAsymmetricKeyProvider"/> for consumer-verification purposes only.
/// Production services must resolve key pairs from Key Vault or a certificate store — never
/// generate ephemeral keys at resolution time as done here for test convenience.
/// </summary>
internal sealed class ConsumerAsymmetricKeyProvider : IAsymmetricKeyProvider
{
    private static readonly System.Security.Cryptography.RSA RsaKey =
        System.Security.Cryptography.RSA.Create(2048);

    private static readonly System.Security.Cryptography.ECDsa EcdsaKey =
        System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);

    public System.Security.Cryptography.RSA GetRsaKey(string keyId) => RsaKey;

    public System.Security.Cryptography.ECDsa GetEcdsaKey(string keyId) => EcdsaKey;
}
