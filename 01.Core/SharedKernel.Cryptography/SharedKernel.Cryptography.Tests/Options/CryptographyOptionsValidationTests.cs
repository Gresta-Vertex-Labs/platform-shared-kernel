using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Cryptography.Extensions;
using SharedKernel.Cryptography.Hashing;
using SharedKernel.Cryptography.Options;

namespace SharedKernel.Cryptography.Tests.Options;

public sealed class CryptographyOptionsValidationTests
{
    private const string Section = "SharedKernel:Cryptography:OneWayHashing";
    private const string Pbkdf2Section = "SharedKernel:Cryptography:Pbkdf2";

    [Fact]
    public void SectionNames_AreUnderSharedKernelCryptography()
    {
        Assert.Equal("SharedKernel:Cryptography", CryptographyOptions.SectionName);
        Assert.Equal(Pbkdf2Section, Pbkdf2Options.SectionName);
    }

    [Fact]
    public void Defaults_AreValid()
    {
        using ServiceProvider provider = Build([]);

        OneWayHashingOptions options = provider.GetRequiredService<IOptions<CryptographyOptions>>().Value.OneWayHashing;
        Pbkdf2Options pbkdf2 = provider.GetRequiredService<IOptions<Pbkdf2Options>>().Value;

        Assert.Equal(Pbkdf2OneWayHashAlgorithm.Id, options.Algorithm);
        Assert.Null(options.CurrentPepperId);
        Assert.Empty(options.Peppers);
        Assert.Equal(600_000, pbkdf2.Iterations);
    }

    [Fact]
    public void FullValidConfiguration_BindsAndValidates()
    {
        string p1 = Pepper(32);
        string p2 = Pepper(64);
        using ServiceProvider provider = Build(new()
        {
            [$"{Section}:Algorithm"] = "pbkdf2-sha256",
            [$"{Section}:CurrentPepperId"] = "p2",
            [$"{Section}:Peppers:p1"] = p1,
            [$"{Section}:Peppers:p2"] = p2,
            [$"{Pbkdf2Section}:Iterations"] = "150000",
        });

        OneWayHashingOptions options = provider.GetRequiredService<IOptionsMonitor<CryptographyOptions>>().CurrentValue.OneWayHashing;

        Assert.Equal("p2", options.CurrentPepperId);
        Assert.Equal(p1, options.Peppers["p1"]);
        Assert.Equal(p2, options.Peppers["p2"]);
        Assert.Equal(150_000, provider.GetRequiredService<IOptionsMonitor<Pbkdf2Options>>().CurrentValue.Iterations);
    }

    [Theory]
    [InlineData(Pbkdf2Options.MinimumIterations)]
    [InlineData(Pbkdf2Options.MaximumIterations)]
    public void BoundaryIterations_AreValid(int iterations)
    {
        using ServiceProvider provider = Build(new() { [$"{Pbkdf2Section}:Iterations"] = iterations.ToString(System.Globalization.CultureInfo.InvariantCulture) });

        Assert.Equal(iterations, provider.GetRequiredService<IOptions<Pbkdf2Options>>().Value.Iterations);
    }

    [Theory]
    [InlineData("99999")]
    [InlineData("2000001")]
    [InlineData("0")]
    public void IterationsOutOfRange_FailValidation(string iterations)
    {
        using ServiceProvider provider = Build(new() { [$"{Pbkdf2Section}:Iterations"] = iterations });

        OptionsValidationException exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<Pbkdf2Options>>().Value);

        Assert.Contains(exception.Failures, f => f.Contains("Iterations", StringComparison.Ordinal));
        Assert.NotNull(provider.GetRequiredService<IOptions<CryptographyOptions>>().Value);
    }

    [Fact]
    public void UnregisteredAlgorithm_FailsValidation()
    {
        OptionsValidationException exception = AssertInvalid(new() { [$"{Section}:Algorithm"] = "argon2id" });

        Assert.Contains(exception.Failures, f => f.Contains("argon2id", StringComparison.Ordinal));
    }

    [Fact]
    public void EmptyAlgorithm_FailsValidation()
    {
        AssertInvalid(new() { [$"{Section}:Algorithm"] = string.Empty });
    }

    [Theory]
    [InlineData("!!!not-base64!!!")]
    [InlineData("   ")]
    public void PepperNotBase64_FailsValidation(string pepper)
    {
        OptionsValidationException exception = AssertInvalid(new() { [$"{Section}:Peppers:p1"] = pepper });

        Assert.Contains(exception.Failures, f => f.Contains("p1", StringComparison.Ordinal));
    }

    [Fact]
    public void PepperShorterThan32Bytes_FailsValidation()
    {
        AssertInvalid(new() { [$"{Section}:Peppers:p1"] = Pepper(31) });
    }

    [Theory]
    [InlineData("bad id")]
    [InlineData("bad_id")]
    [InlineData("pepper.1")]
    [InlineData("abcdefghijklmnopqrstuvwxyz0123456")]
    public void InvalidPepperId_FailsValidation(string pepperId)
    {
        AssertInvalid(new() { [$"{Section}:Peppers:{pepperId}"] = Pepper(32) });
    }

    [Fact]
    public void PepperIdOf32LettersDigitsAndHyphens_IsValid()
    {
        const string id = "Pepper-2026-abcdefghijklmnopqrst";
        Assert.Equal(32, id.Length);

        using ServiceProvider provider = Build(new() { [$"{Section}:Peppers:{id}"] = Pepper(32), [$"{Section}:CurrentPepperId"] = id });

        Assert.Equal(id, provider.GetRequiredService<IOptions<CryptographyOptions>>().Value.OneWayHashing.CurrentPepperId);
    }

    [Fact]
    public void CurrentPepperIdNotInPeppers_FailsValidation()
    {
        OptionsValidationException exception = AssertInvalid(new()
        {
            [$"{Section}:Peppers:p1"] = Pepper(32),
            [$"{Section}:CurrentPepperId"] = "p2",
        });

        Assert.Contains(exception.Failures, f => f.Contains("p2", StringComparison.Ordinal));
    }

    [Fact]
    public void NullPeppers_WithoutCurrentPepperId_IsValid()
    {
        using ServiceProvider provider = Build([], options => options.OneWayHashing.Peppers = null!);

        Assert.Null(provider.GetRequiredService<IOptions<CryptographyOptions>>().Value.OneWayHashing.Peppers);
    }

    [Fact]
    public void NullPeppers_WithCurrentPepperId_FailsValidation()
    {
        using ServiceProvider provider = Build(
            new() { [$"{Section}:CurrentPepperId"] = "p1" },
            options => options.OneWayHashing.Peppers = null!);

        OptionsValidationException exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<CryptographyOptions>>().Value);

        Assert.Contains(exception.Failures, f => f.Contains("p1", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidPepperConfiguration_HasherFromContainerUsesPepper()
    {
        using ServiceProvider provider = Build(new()
        {
            [$"{Section}:Peppers:p1"] = Pepper(32),
            [$"{Section}:CurrentPepperId"] = "p1",
            [$"{Pbkdf2Section}:Iterations"] = "100000",
        });
        IOneWayHasher hasher = provider.GetRequiredService<IOneWayHasher>();

        string hash = hasher.Hash("secret");

        Assert.StartsWith("$pbkdf2-sha256$i=100000,k=p1$", hash, StringComparison.Ordinal);
        Assert.Equal(HashVerificationResult.Success, hasher.Verify(hash, "secret"));
    }

    [Fact]
    public void InvalidIterations_HashingThrows()
    {
        using ServiceProvider provider = Build(new() { [$"{Pbkdf2Section}:Iterations"] = "10" });
        IOneWayHasher hasher = provider.GetRequiredService<IOneWayHasher>();

        Assert.Throws<OptionsValidationException>(() => hasher.Hash("secret"));
    }

    private static OptionsValidationException AssertInvalid(Dictionary<string, string?> values)
    {
        using ServiceProvider provider = Build(values);
        return Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<CryptographyOptions>>().Value);
    }

    private static ServiceProvider Build(Dictionary<string, string?> values, Action<CryptographyOptions>? postConfigure = null)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddSharedKernelCryptography(configuration);
        if (postConfigure is not null)
        {
            services.PostConfigure(postConfigure);
        }

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    private static string Pepper(int bytes) => Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes));
}
