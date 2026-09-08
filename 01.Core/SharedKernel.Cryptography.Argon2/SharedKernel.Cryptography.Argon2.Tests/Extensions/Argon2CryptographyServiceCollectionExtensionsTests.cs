using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharedKernel.Cryptography.Argon2.Extensions;
using SharedKernel.Cryptography.Extensions;
using SharedKernel.Cryptography.Hashing;
using SharedKernel.Cryptography.Random;
using Xunit;

namespace SharedKernel.Cryptography.Argon2.Tests.Extensions;

public sealed class Argon2CryptographyServiceCollectionExtensionsTests
{
    [Fact]
    public void NullServices_Throws()
    {
        IServiceCollection? services = null;
        IConfiguration configuration = new ConfigurationBuilder().Build();

        Assert.Throws<ArgumentNullException>(
            () => services!.AddSharedKernelArgon2Cryptography(configuration));
    }

    [Fact]
    public void NullConfiguration_Throws()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(
            () => services.AddSharedKernelArgon2Cryptography(null!));
    }

    [Fact]
    public void ValidConfiguration_RegistersArgon2idOneWayHasher_UnderKeyedLookupOnly()
    {
        IConfiguration configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();

        services.AddSharedKernelArgon2Cryptography(configuration);
        using ServiceProvider provider = services.BuildServiceProvider();

        var keyed = provider.GetRequiredKeyedService<IOneWayHasher>(
            Argon2CryptographyServiceCollectionExtensions.Argon2idOneWayHasherKey);
        Assert.IsType<Argon2idOneWayHasher>(keyed);
    }

    [Fact]
    public void ValidConfiguration_DoesNotRegisterUnkeyedIOneWayHasher()
    {
        // This method registers Argon2idOneWayHasher only under the "Argon2id" keyed lookup —
        // it must never become resolvable as the unkeyed IOneWayHasher default on its own.
        IConfiguration configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();

        services.AddSharedKernelArgon2Cryptography(configuration);
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Null(provider.GetService<IOneWayHasher>());
    }

    [Fact]
    public void CombinedWithAddSharedKernelCryptography_UnkeyedResolvesToPbkdf2_KeyedResolvesToArgon2id()
    {
        // T-72: proves the two registration methods compose without interference — the unkeyed
        // IOneWayHasher default stays Pbkdf2OneWayHasher regardless of Argon2's registration.
        IConfiguration configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();

        services.AddSharedKernelCryptography(configuration);
        services.AddSharedKernelArgon2Cryptography(configuration);
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.IsType<Pbkdf2OneWayHasher>(provider.GetRequiredService<IOneWayHasher>());
        Assert.IsType<Argon2idOneWayHasher>(provider.GetRequiredKeyedService<IOneWayHasher>(
            Argon2CryptographyServiceCollectionExtensions.Argon2idOneWayHasherKey));
    }

    [Fact]
    public void ValidConfiguration_SelfRegistersSecureRandomGenerator_WhenNotAlreadyRegistered()
    {
        IConfiguration configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();

        services.AddSharedKernelArgon2Cryptography(configuration);
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.IsType<CryptoRandomGenerator>(provider.GetRequiredService<ISecureRandomGenerator>());
    }

    [Fact]
    public void ExistingSecureRandomGeneratorRegistration_IsNeverOverridden()
    {
        IConfiguration configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        var sentinel = new CryptoRandomGenerator();
        services.AddSingleton<ISecureRandomGenerator>(sentinel);

        services.AddSharedKernelArgon2Cryptography(configuration);
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Same(sentinel, provider.GetRequiredService<ISecureRandomGenerator>());
    }

    [Fact]
    public async Task NoConfiguration_StartsSuccessfully_UsingDefaults()
    {
        using IHost host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
                services.AddSharedKernelArgon2Cryptography(new ConfigurationBuilder().Build()))
            .Build();

        await host.StartAsync();
        await host.StopAsync();
    }

    [Fact]
    public async Task IterationsBelowFloor_ThrowsAtHostStartup()
    {
        // Argon2CryptographyOptions.MinIterations is a real, OWASP-cited floor — unlike
        // CryptographyOptions.Pbkdf2Iterations's original [Range(1, int.MaxValue)], a value below
        // it must never pass startup validation.
        IConfiguration invalidConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SharedKernel:Cryptography:Argon2:Iterations"] = "1",
            })
            .Build();

        using IHost host = Host.CreateDefaultBuilder()
            .ConfigureServices(services => services.AddSharedKernelArgon2Cryptography(invalidConfiguration))
            .Build();

        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
    }

    [Fact]
    public async Task MemorySizeKbBelowFloor_ThrowsAtHostStartup()
    {
        IConfiguration invalidConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SharedKernel:Cryptography:Argon2:MemorySizeKb"] = "64",
            })
            .Build();

        using IHost host = Host.CreateDefaultBuilder()
            .ConfigureServices(services => services.AddSharedKernelArgon2Cryptography(invalidConfiguration))
            .Build();

        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
    }

    [Fact]
    public async Task DegreeOfParallelismBelowFloor_ThrowsAtHostStartup()
    {
        IConfiguration invalidConfiguration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SharedKernel:Cryptography:Argon2:DegreeOfParallelism"] = "0",
            })
            .Build();

        using IHost host = Host.CreateDefaultBuilder()
            .ConfigureServices(services => services.AddSharedKernelArgon2Cryptography(invalidConfiguration))
            .Build();

        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
    }
}
