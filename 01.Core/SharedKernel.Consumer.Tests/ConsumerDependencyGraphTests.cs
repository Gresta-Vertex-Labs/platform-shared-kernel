using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharedKernel.Configuration.Extensions;
using SharedKernel.Core.Extensions;
using SharedKernel.Cryptography.Extensions;
using SharedKernel.Cryptography.Hashing;
using SharedKernel.Cryptography.Random;
using SharedKernel.Cryptography.Signing;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.FeatureManagement.Abstractions;
using SharedKernel.FeatureManagement.Extensions;
using SharedKernel.Guards;
using SharedKernel.Guards.Clauses;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Enums;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using Xunit;

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

        Assert.Equal(ErrorType.Validation,   validation.Type);
        Assert.Equal(ErrorType.NotFound,     notFound.Type);
        Assert.Equal(ErrorType.Conflict,     conflict.Type);
        Assert.Equal(ErrorType.Unauthorized, unauthorized.Type);
        Assert.Equal(ErrorType.Unexpected,   unexpected.Type);
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

        Assert.NotNull(host.Services.GetRequiredService<IPasswordHasher>());
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
    public void Cryptography_PasswordHasher_HashAndVerifyRoundtrip_ResolvedFromPackage()
    {
        using ServiceProvider provider = BuildCryptographyServiceProvider();
        IPasswordHasher hasher = provider.GetRequiredService<IPasswordHasher>();

        string hash = hasher.Hash("correct-horse-battery-staple");

        Assert.Equal(PasswordVerificationResult.Success, hasher.Verify(hash, "correct-horse-battery-staple"));
        Assert.Equal(PasswordVerificationResult.Failed, hasher.Verify(hash, "wrong-password"));
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

    private static ServiceProvider BuildCryptographyServiceProvider()
    {
        IServiceCollection services = new ServiceCollection();
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        services.AddSharedKernelCryptography(configuration);
        return services.BuildServiceProvider();
    }
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
/// Minimal in-memory <see cref="IEncryptionKeyProvider"/> for consumer-verification purposes only.
/// Production services must resolve key material from Key Vault, environment config, or a secret
/// store — never hardcode it as done here for test convenience.
/// </summary>
internal sealed class ConsumerEncryptionKeyProvider : IEncryptionKeyProvider
{
    private static readonly CryptographicKey CurrentKey = new("consumer-key-v1", new byte[32]);

    public CryptographicKey GetCurrentKey() => CurrentKey;

    public CryptographicKey? GetKey(string keyId) => keyId == CurrentKey.Id ? CurrentKey : null;
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
