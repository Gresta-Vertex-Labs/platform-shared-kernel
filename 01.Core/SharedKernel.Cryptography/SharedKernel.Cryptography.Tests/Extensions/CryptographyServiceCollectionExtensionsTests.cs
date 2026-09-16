using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Cryptography.Envelope;
using SharedKernel.Cryptography.Extensions;
using SharedKernel.Cryptography.Hashing;
using SharedKernel.Cryptography.Options;
using SharedKernel.Cryptography.Random;
using SharedKernel.Cryptography.Signing;
using SharedKernel.Cryptography.Symmetric;
using SharedKernel.Cryptography.Tests.TestDoubles;
using SharedKernel.Cryptography.Totp;
using SharedKernel.Primitives.Clocks;

namespace SharedKernel.Cryptography.Tests.Extensions;

public sealed class CryptographyServiceCollectionExtensionsTests
{
    private static readonly ServiceProviderOptions StrictValidation = new() { ValidateOnBuild = true, ValidateScopes = true };

    private static readonly Type[] KeylessServices =
    [
        typeof(IOneWayHasher),
        typeof(ISecureRandomGenerator),
        typeof(IContentHasher),
        typeof(IHmacSigner),
        typeof(IHotpGenerator),
        typeof(ITotpGenerator),
        typeof(IRecoveryCodeGenerator),
        typeof(IClock),
    ];

    private static readonly Type[] KeyDependentServices =
    [
        typeof(ISymmetricEncryptionService),
        typeof(ISynchronousSymmetricEncryptionService),
        typeof(IEnvelopeEncryptionService),
        typeof(IAsymmetricSignatureService),
        typeof(ITotpVerifier),
    ];

    [Fact]
    public void AddSharedKernelCryptography_WithStrictValidation_ResolvesEveryKeylessService()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelCryptography(EmptyConfiguration());

        using ServiceProvider provider = services.BuildServiceProvider(StrictValidation);

        Assert.All(KeylessServices, type => Assert.NotNull(provider.GetRequiredService(type)));
        Assert.IsType<OneWayHasher>(provider.GetRequiredService<IOneWayHasher>());
    }

    [Fact]
    public void AddSharedKernelCryptography_WithStrictValidation_ResolvesHasherAndBothOptions()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelCryptography(EmptyConfiguration());

        using ServiceProvider provider = services.BuildServiceProvider(StrictValidation);

        Assert.IsType<OneWayHasher>(provider.GetRequiredService<IOneWayHasher>());
        Assert.Equal(Pbkdf2OneWayHashAlgorithm.Id, provider.GetRequiredService<IOptions<CryptographyOptions>>().Value.OneWayHashing.Algorithm);
        Assert.Equal(600_000, provider.GetRequiredService<IOptions<Pbkdf2Options>>().Value.Iterations);
        Assert.StartsWith("$pbkdf2-sha256$i=600000$", provider.GetRequiredService<IOneWayHasher>().Hash("secret"), StringComparison.Ordinal);
    }

    [Fact]
    public void AddSharedKernelCryptography_RegistersDefaultImplementations()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelCryptography(EmptyConfiguration());

        using ServiceProvider provider = services.BuildServiceProvider(StrictValidation);

        Assert.IsType<SecureRandomGenerator>(provider.GetRequiredService<ISecureRandomGenerator>());
        Assert.IsType<Sha256ContentHasher>(provider.GetRequiredService<IContentHasher>());
        Assert.IsType<HmacSha256Signer>(provider.GetRequiredService<IHmacSigner>());
        Assert.IsType<HotpGenerator>(provider.GetRequiredService<IHotpGenerator>());
        Assert.IsType<TotpGenerator>(provider.GetRequiredService<ITotpGenerator>());
        Assert.IsType<RecoveryCodeGenerator>(provider.GetRequiredService<IRecoveryCodeGenerator>());
        Assert.IsType<SystemClock>(provider.GetRequiredService<IClock>());
    }

    [Fact]
    public void AddSharedKernelCryptography_RegistersKeylessServicesAsSingletons()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelCryptography(EmptyConfiguration());

        Assert.All(KeylessServices, type =>
            Assert.Equal(ServiceLifetime.Singleton, Assert.Single(services, d => d.ServiceType == type).Lifetime));
        ServiceDescriptor algorithm = Assert.Single(services, d => d.ServiceType == typeof(IOneWayHashAlgorithm));
        Assert.Equal(typeof(Pbkdf2OneWayHashAlgorithm), algorithm.ImplementationType);
    }

    [Fact]
    public void AddSharedKernelCryptography_DoesNotRegisterKeyDependentServices()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelCryptography(EmptyConfiguration());

        Assert.All(KeyDependentServices, type => Assert.DoesNotContain(services, d => d.ServiceType == type));
    }

    [Fact]
    public void AddSharedKernelCryptography_RegistersOptionsValidation()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelCryptography(EmptyConfiguration());

        Assert.Equal(2, services.Count(d => d.ServiceType == typeof(IValidateOptions<CryptographyOptions>)));
        Assert.Contains(services, d => d.ServiceType == typeof(IConfigureOptions<CryptographyOptions>));
        Assert.Single(services, d => d.ServiceType == typeof(IValidateOptions<Pbkdf2Options>));
        Assert.Contains(services, d => d.ServiceType == typeof(IConfigureOptions<Pbkdf2Options>));
    }

    [Fact]
    public void AddSymmetricEncryption_RegistersServiceResolvableWithProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IEncryptionKeyProvider>(TestKeys.SingleKeyProvider());

        ICryptographyBuilder builder = services.AddSharedKernelCryptography(EmptyConfiguration()).AddSymmetricEncryption();

        Assert.Same(services, builder.Services);
        using ServiceProvider provider = services.BuildServiceProvider(StrictValidation);
        Assert.IsType<AesGcmEncryptionService>(provider.GetRequiredService<ISymmetricEncryptionService>());
    }

    [Fact]
    public void AddSynchronousSymmetricEncryption_RegistersServiceResolvableWithProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISynchronousEncryptionKeyProvider>(TestKeys.SingleKeyProvider());

        services.AddSharedKernelCryptography(EmptyConfiguration()).AddSynchronousSymmetricEncryption();

        using ServiceProvider provider = services.BuildServiceProvider(StrictValidation);
        Assert.IsType<SynchronousAesGcmEncryptionService>(provider.GetRequiredService<ISynchronousSymmetricEncryptionService>());
    }

    [Fact]
    public void AddEnvelopeEncryption_RegistersServiceResolvableWithProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IEnvelopeEncryptionProvider>(new FakeEnvelopeEncryptionProvider());

        services.AddSharedKernelCryptography(EmptyConfiguration()).AddEnvelopeEncryption();

        using ServiceProvider provider = services.BuildServiceProvider(StrictValidation);
        Assert.IsType<EnvelopeEncryptionService>(provider.GetRequiredService<IEnvelopeEncryptionService>());
    }

    [Fact]
    public void AddAsymmetricSigning_RegistersServiceResolvableWithProvider()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISigningKeyProvider>(new InMemorySigningKeyProvider([]));

        services.AddSharedKernelCryptography(EmptyConfiguration()).AddAsymmetricSigning();

        using ServiceProvider provider = services.BuildServiceProvider(StrictValidation);
        Assert.IsType<AsymmetricSignatureService>(provider.GetRequiredService<IAsymmetricSignatureService>());
    }

    [Fact]
    public void AddTotpVerification_RegistersServiceResolvableWithReplayGuard()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITotpReplayGuard>(new InMemoryTotpReplayGuard());

        services.AddSharedKernelCryptography(EmptyConfiguration()).AddTotpVerification();

        using ServiceProvider provider = services.BuildServiceProvider(StrictValidation);
        Assert.IsType<TotpVerifier>(provider.GetRequiredService<ITotpVerifier>());
    }

    [Fact]
    public void AllBuilderMethods_WithProvidersRegistered_PassStrictValidation()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IEncryptionKeyProvider>(TestKeys.SingleKeyProvider());
        services.AddSingleton<ISynchronousEncryptionKeyProvider>(TestKeys.SingleKeyProvider());
        services.AddSingleton<IEnvelopeEncryptionProvider>(new FakeEnvelopeEncryptionProvider());
        services.AddSingleton<ISigningKeyProvider>(new InMemorySigningKeyProvider([]));
        services.AddSingleton<ITotpReplayGuard>(new InMemoryTotpReplayGuard());

        AddEverything(services);

        using ServiceProvider provider = services.BuildServiceProvider(StrictValidation);
        Assert.All(KeyDependentServices, type => Assert.NotNull(provider.GetRequiredService(type)));
    }

    [Fact]
    public void BuilderMethod_WithoutItsProvider_FailsStrictValidation()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelCryptography(EmptyConfiguration()).AddSymmetricEncryption();

        AggregateException exception = Assert.Throws<AggregateException>(() => services.BuildServiceProvider(StrictValidation));
        Assert.Contains(exception.InnerExceptions, e => e.Message.Contains(nameof(IEncryptionKeyProvider), StringComparison.Ordinal));
    }

    [Fact]
    public void CallingEverythingTwice_DoesNotDuplicateRegistrations()
    {
        var services = new ServiceCollection();

        AddEverything(services);
        Dictionary<Type, int> afterFirst = services.GroupBy(d => d.ServiceType).ToDictionary(g => g.Key, g => g.Count());
        AddEverything(services);

        Assert.All([.. KeylessServices, .. KeyDependentServices], type => Assert.Single(services, d => d.ServiceType == type));
        Assert.Single(services, d => d.ServiceType == typeof(IOneWayHashAlgorithm) && d.ImplementationType == typeof(Pbkdf2OneWayHashAlgorithm));
        Assert.Single(services, d => d.ServiceType == typeof(IOneWayHashAlgorithm) && d.ImplementationType == typeof(Sha256TestAlgorithm));
        Assert.Equal(2, services.Count(d => d.ServiceType == typeof(IValidateOptions<CryptographyOptions>)));

        // Re-binding configuration adds another binder per call; that comes from the options infrastructure, not from this package.
        IEnumerable<Type> grown = services.GroupBy(d => d.ServiceType)
            .Where(g => g.Count() != afterFirst.GetValueOrDefault(g.Key))
            .Select(g => g.Key);
        Assert.All(grown, type => Assert.Equal("Microsoft.Extensions.Options", type.Namespace));
    }

    [Fact]
    public void PreRegisteredOneWayHasher_Wins()
    {
        var services = new ServiceCollection();
        var custom = new CustomOneWayHasher();
        services.AddSingleton<IOneWayHasher>(custom);

        services.AddSharedKernelCryptography(EmptyConfiguration());

        using ServiceProvider provider = services.BuildServiceProvider();
        Assert.Same(custom, provider.GetRequiredService<IOneWayHasher>());
        Assert.Single(services, d => d.ServiceType == typeof(IOneWayHasher));
    }

    [Fact]
    public void PreRegisteredClock_Wins()
    {
        var services = new ServiceCollection();
        var clock = new FakeClock(DateTimeOffset.UnixEpoch);
        services.AddSingleton<IClock>(clock);

        services.AddSharedKernelCryptography(EmptyConfiguration());

        using ServiceProvider provider = services.BuildServiceProvider();
        Assert.Same(clock, provider.GetRequiredService<IClock>());
    }

    [Fact]
    public void AddOneWayHashAlgorithm_RegistersAdditionalAlgorithm()
    {
        var services = new ServiceCollection();

        services.AddSharedKernelCryptography(EmptyConfiguration()).AddOneWayHashAlgorithm<Sha256TestAlgorithm>();

        Assert.Equal(
            [typeof(Pbkdf2OneWayHashAlgorithm), typeof(Sha256TestAlgorithm)],
            services.Where(d => d.ServiceType == typeof(IOneWayHashAlgorithm)).Select(d => d.ImplementationType));
    }

    [Fact]
    public void AddOneWayHashAlgorithm_ConfiguredAsAlgorithm_IsUsedByHasher()
    {
        var services = new ServiceCollection();
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SharedKernel:Cryptography:OneWayHashing:Algorithm"] = Sha256TestAlgorithm.Id,
            })
            .Build();

        services.AddSharedKernelCryptography(configuration).AddOneWayHashAlgorithm<Sha256TestAlgorithm>();

        using ServiceProvider provider = services.BuildServiceProvider(StrictValidation);
        IOneWayHasher hasher = provider.GetRequiredService<IOneWayHasher>();
        string hash = hasher.Hash("secret");

        Assert.StartsWith($"${Sha256TestAlgorithm.Id}$", hash, StringComparison.Ordinal);
        Assert.Equal(HashVerificationResult.Success, hasher.Verify(hash, "secret"));
    }

    [Fact]
    public void NullArguments_Throw()
    {
        ICryptographyBuilder? noBuilder = null;

        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddSharedKernelCryptography(EmptyConfiguration()));
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddSharedKernelCryptography(null!));
        Assert.Throws<ArgumentNullException>(() => noBuilder!.AddSymmetricEncryption());
        Assert.Throws<ArgumentNullException>(() => noBuilder!.AddSynchronousSymmetricEncryption());
        Assert.Throws<ArgumentNullException>(() => noBuilder!.AddEnvelopeEncryption());
        Assert.Throws<ArgumentNullException>(() => noBuilder!.AddAsymmetricSigning());
        Assert.Throws<ArgumentNullException>(() => noBuilder!.AddTotpVerification());
        Assert.Throws<ArgumentNullException>(() => noBuilder!.AddOneWayHashAlgorithm<Sha256TestAlgorithm>());
    }

    private static void AddEverything(IServiceCollection services) =>
        services.AddSharedKernelCryptography(EmptyConfiguration())
            .AddSymmetricEncryption()
            .AddSynchronousSymmetricEncryption()
            .AddEnvelopeEncryption()
            .AddAsymmetricSigning()
            .AddTotpVerification()
            .AddOneWayHashAlgorithm<Sha256TestAlgorithm>();

    private static IConfiguration EmptyConfiguration() => new ConfigurationBuilder().Build();

    private sealed class CustomOneWayHasher : IOneWayHasher
    {
        public string Hash(string secret) => "custom";

        public HashVerificationResult Verify(string hash, string secret) => HashVerificationResult.Failed;
    }

    private sealed class Sha256TestAlgorithm : IOneWayHashAlgorithm
    {
        public const string Id = "test-sha256";

        public string AlgorithmId => Id;

        public PhcHashString Hash(ReadOnlySpan<byte> secret)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(16);
            return new PhcHashString(Id, null, [], salt, SHA256.HashData([.. salt, .. secret]));
        }

        public bool Verify(PhcHashString hash, ReadOnlySpan<byte> secret) =>
            CryptographicOperations.FixedTimeEquals(SHA256.HashData([.. hash.Salt, .. secret]), hash.Hash);

        public bool RequiresRehash(PhcHashString hash) => false;
    }
}
