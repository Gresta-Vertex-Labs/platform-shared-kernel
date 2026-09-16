using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Cryptography.Extensions;
using SharedKernel.Cryptography.Hashing;
using SharedKernel.Cryptography.Options;
using Xunit;

namespace SharedKernel.Cryptography.Argon2.Tests;

public sealed class Argon2CryptographyBuilderExtensionsTests
{
    private const string Secret = "correct horse battery staple";

    [Fact]
    public void AddArgon2id_NullArguments_Throws()
    {
        IConfiguration configuration = BuildConfiguration([]);
        ICryptographyBuilder builder = new ServiceCollection().AddSharedKernelCryptography(configuration);

        Assert.Throws<ArgumentNullException>(() => Argon2CryptographyBuilderExtensions.AddArgon2id(null!, configuration));
        Assert.Throws<ArgumentNullException>(() => builder.AddArgon2id(null!));
    }

    [Fact]
    public void AddArgon2id_Always_ReturnsSameBuilder()
    {
        IConfiguration configuration = BuildConfiguration([]);
        ICryptographyBuilder builder = new ServiceCollection().AddSharedKernelCryptography(configuration);

        Assert.Same(builder, builder.AddArgon2id(configuration));
    }

    [Fact]
    public void AddArgon2id_Always_RegistersAlgorithmAlongsidePbkdf2()
    {
        var services = new ServiceCollection();
        IConfiguration configuration = BuildConfiguration(Argon2Settings("argon2id"));

        services.AddSharedKernelCryptography(configuration).AddArgon2id(configuration);

        Assert.Single(services, d => d.ServiceType == typeof(IOneWayHashAlgorithm) && d.ImplementationType == typeof(Argon2idOneWayHashAlgorithm));
        Assert.Single(services, d => d.ServiceType == typeof(IOneWayHashAlgorithm) && d.ImplementationType == typeof(Pbkdf2OneWayHashAlgorithm));
        Assert.All(
            services.Where(d => d.ServiceType == typeof(IOneWayHashAlgorithm)),
            d => Assert.Equal(ServiceLifetime.Singleton, d.Lifetime));
    }

    [Fact]
    public void AddArgon2id_CalledTwice_RegistersAlgorithmOnce()
    {
        IConfiguration configuration = BuildConfiguration(Argon2Settings("argon2id"));
        var services = new ServiceCollection();

        services.AddSharedKernelCryptography(configuration).AddArgon2id(configuration).AddArgon2id(configuration);

        Assert.Single(services, d => d.ServiceType == typeof(IOneWayHashAlgorithm) && d.ImplementationType == typeof(Argon2idOneWayHashAlgorithm));
        Assert.Equal(2, services.Count(d => d.ServiceType == typeof(IOneWayHashAlgorithm)));
    }

    [Fact]
    public void AddArgon2id_CalledTwice_ResolvesEachAlgorithmOnce()
    {
        IConfiguration configuration = BuildConfiguration(Argon2Settings("argon2id"));
        var services = new ServiceCollection();
        services.AddSharedKernelCryptography(configuration).AddArgon2id(configuration).AddArgon2id(configuration);
        using ServiceProvider provider = services.BuildServiceProvider();

        IOneWayHashAlgorithm[] algorithms = [.. provider.GetServices<IOneWayHashAlgorithm>()];

        Assert.Equal(2, algorithms.Length);
        Assert.Single(algorithms, a => a is Argon2idOneWayHashAlgorithm);
        Assert.Single(algorithms, a => a is Pbkdf2OneWayHashAlgorithm);
        IOneWayHasher hasher = provider.GetRequiredService<IOneWayHasher>();
        Assert.Equal(HashVerificationResult.Success, hasher.Verify(hasher.Hash(Secret), Secret));
    }

    [Fact]
    public void Hash_Argon2idConfigured_WritesArgon2idHash()
    {
        using ServiceProvider provider = BuildProvider(Argon2Settings("argon2id"));
        IOneWayHasher hasher = provider.GetRequiredService<IOneWayHasher>();

        string hash = hasher.Hash(Secret);

        Assert.StartsWith("$argon2id$v=19$m=7168,t=2,p=1$", hash, StringComparison.Ordinal);
        Assert.Equal(HashVerificationResult.Success, hasher.Verify(hash, Secret));
        Assert.Equal(HashVerificationResult.Failed, hasher.Verify(hash, Secret + "!"));
    }

    [Fact]
    public void Verify_Pbkdf2HashAfterSwitchingToArgon2id_ReturnsSuccessRehashNeeded()
    {
        string pbkdf2Hash;
        using (ServiceProvider pbkdf2Provider = BuildProvider(Argon2Settings("pbkdf2-sha256")))
        {
            pbkdf2Hash = pbkdf2Provider.GetRequiredService<IOneWayHasher>().Hash(Secret);
        }

        using ServiceProvider argon2Provider = BuildProvider(Argon2Settings("argon2id"));
        IOneWayHasher hasher = argon2Provider.GetRequiredService<IOneWayHasher>();

        Assert.StartsWith("$pbkdf2-sha256$i=100000$", pbkdf2Hash, StringComparison.Ordinal);
        Assert.Equal(HashVerificationResult.SuccessRehashNeeded, hasher.Verify(pbkdf2Hash, Secret));
        Assert.Equal(HashVerificationResult.Failed, hasher.Verify(pbkdf2Hash, Secret + "!"));
    }

    [Fact]
    public void Verify_Pbkdf2HashFromAlgorithmInstance_ReturnsSuccessRehashNeeded()
    {
        var pbkdf2 = new Pbkdf2OneWayHashAlgorithm(
            new TestOptionsMonitor<Pbkdf2Options>(new Pbkdf2Options { Iterations = 100_000 }));
        string pbkdf2Hash = pbkdf2.Hash(System.Text.Encoding.UTF8.GetBytes(Secret)).ToString();
        using ServiceProvider provider = BuildProvider(Argon2Settings("argon2id"));

        HashVerificationResult result = provider.GetRequiredService<IOneWayHasher>().Verify(pbkdf2Hash, Secret);

        Assert.Equal(HashVerificationResult.SuccessRehashNeeded, result);
    }

    [Fact]
    public void Hash_Argon2idWithPepper_StoresPepperIdAndVerifies()
    {
        Dictionary<string, string?> settings = Argon2Settings("argon2id");
        settings["SharedKernel:Cryptography:OneWayHashing:CurrentPepperId"] = "p1";
        settings["SharedKernel:Cryptography:OneWayHashing:Peppers:p1"] = Convert.ToBase64String(Argon2TestData.Salt(32));
        using ServiceProvider provider = BuildProvider(settings);
        IOneWayHasher hasher = provider.GetRequiredService<IOneWayHasher>();

        string hash = hasher.Hash(Secret);

        Assert.StartsWith("$argon2id$v=19$m=7168,t=2,p=1,k=p1$", hash, StringComparison.Ordinal);
        Assert.Equal(HashVerificationResult.Success, hasher.Verify(hash, Secret));
    }

    [Fact]
    public void GetOptions_Argon2idSelectedWithoutRegistration_ThrowsOptionsValidationException()
    {
        IConfiguration configuration = BuildConfiguration(Argon2Settings("argon2id"));
        using ServiceProvider provider = new ServiceCollection()
            .AddSharedKernelCryptography(configuration)
            .Services
            .BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOneWayHasher>().Hash(Secret));
    }

    [Fact]
    public void GetOptions_NoArgon2Section_UsesValidDefaults()
    {
        using ServiceProvider provider = BuildProvider([]);

        Argon2Options options = provider.GetRequiredService<IOptions<Argon2Options>>().Value;

        Assert.Equal(19_456, options.MemorySizeKb);
        Assert.Equal(2, options.Iterations);
        Assert.Equal(1, options.DegreeOfParallelism);
        Assert.Equal("SharedKernel:Cryptography:Argon2", Argon2Options.SectionName);
    }

    [Fact]
    public void GetOptions_LowCostSection_BindsValues()
    {
        using ServiceProvider provider = BuildProvider(Argon2Settings("argon2id"));

        Argon2Options options = provider.GetRequiredService<IOptionsMonitor<Argon2Options>>().CurrentValue;

        Assert.Equal(7_168, options.MemorySizeKb);
        Assert.Equal(2, options.Iterations);
        Assert.Equal(1, options.DegreeOfParallelism);
    }

    [Theory]
    [InlineData("MemorySizeKb", "7167")]
    [InlineData("MemorySizeKb", "1048577")]
    [InlineData("Iterations", "1")]
    [InlineData("Iterations", "11")]
    [InlineData("DegreeOfParallelism", "0")]
    [InlineData("DegreeOfParallelism", "17")]
    public void GetOptions_ValueOutOfRange_ThrowsOptionsValidationException(string key, string value)
    {
        using ServiceProvider provider = BuildProvider(new Dictionary<string, string?>
        {
            [$"SharedKernel:Cryptography:Argon2:{key}"] = value,
        });

        OptionsValidationException exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<Argon2Options>>().Value);
        Assert.Contains(key, string.Join(" ", exception.Failures), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("MemorySizeKb", "7168")]
    [InlineData("MemorySizeKb", "1048576")]
    [InlineData("Iterations", "2")]
    [InlineData("Iterations", "10")]
    [InlineData("DegreeOfParallelism", "1")]
    [InlineData("DegreeOfParallelism", "16")]
    public void GetOptions_ValueAtBound_IsValid(string key, string value)
    {
        using ServiceProvider provider = BuildProvider(new Dictionary<string, string?>
        {
            [$"SharedKernel:Cryptography:Argon2:{key}"] = value,
        });

        Argon2Options options = provider.GetRequiredService<IOptions<Argon2Options>>().Value;

        Assert.NotNull(options);
    }

    private static Dictionary<string, string?> Argon2Settings(string algorithm) => new()
    {
        ["SharedKernel:Cryptography:OneWayHashing:Algorithm"] = algorithm,
        ["SharedKernel:Cryptography:Pbkdf2:Iterations"] = "100000",
        ["SharedKernel:Cryptography:Argon2:MemorySizeKb"] = "7168",
        ["SharedKernel:Cryptography:Argon2:Iterations"] = "2",
        ["SharedKernel:Cryptography:Argon2:DegreeOfParallelism"] = "1",
    };

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

    private static ServiceProvider BuildProvider(Dictionary<string, string?> settings)
    {
        IConfiguration configuration = BuildConfiguration(settings);
        var services = new ServiceCollection();
        services.AddSharedKernelCryptography(configuration).AddArgon2id(configuration);
        return services.BuildServiceProvider();
    }
}
