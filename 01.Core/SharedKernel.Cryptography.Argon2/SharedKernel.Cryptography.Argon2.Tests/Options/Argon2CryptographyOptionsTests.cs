using System.ComponentModel.DataAnnotations;
using SharedKernel.Cryptography.Argon2.Options;
using Xunit;

namespace SharedKernel.Cryptography.Argon2.Tests.Options;

public sealed class Argon2CryptographyOptionsTests
{
    [Fact]
    public void Defaults_MatchOwaspCurrentRecommendation()
    {
        var options = new Argon2CryptographyOptions();

        Assert.Equal(19_456, options.MemorySizeKb);
        Assert.Equal(2, options.Iterations);
        Assert.Equal(1, options.DegreeOfParallelism);
    }

    [Theory]
    [InlineData(nameof(Argon2CryptographyOptions.MemorySizeKb), Argon2CryptographyOptions.MinMemorySizeKb - 1, false)]
    [InlineData(nameof(Argon2CryptographyOptions.MemorySizeKb), Argon2CryptographyOptions.MinMemorySizeKb, true)]
    [InlineData(nameof(Argon2CryptographyOptions.MemorySizeKb), Argon2CryptographyOptions.MaxMemorySizeKb, true)]
    [InlineData(nameof(Argon2CryptographyOptions.MemorySizeKb), Argon2CryptographyOptions.MaxMemorySizeKb + 1, false)]
    [InlineData(nameof(Argon2CryptographyOptions.Iterations), Argon2CryptographyOptions.MinIterations - 1, false)]
    [InlineData(nameof(Argon2CryptographyOptions.Iterations), Argon2CryptographyOptions.MinIterations, true)]
    [InlineData(nameof(Argon2CryptographyOptions.Iterations), Argon2CryptographyOptions.MaxIterations, true)]
    [InlineData(nameof(Argon2CryptographyOptions.Iterations), Argon2CryptographyOptions.MaxIterations + 1, false)]
    [InlineData(nameof(Argon2CryptographyOptions.DegreeOfParallelism), Argon2CryptographyOptions.MinDegreeOfParallelism - 1, false)]
    [InlineData(nameof(Argon2CryptographyOptions.DegreeOfParallelism), Argon2CryptographyOptions.MinDegreeOfParallelism, true)]
    [InlineData(nameof(Argon2CryptographyOptions.DegreeOfParallelism), Argon2CryptographyOptions.MaxDegreeOfParallelism, true)]
    [InlineData(nameof(Argon2CryptographyOptions.DegreeOfParallelism), Argon2CryptographyOptions.MaxDegreeOfParallelism + 1, false)]
    public void RangeValidation_EnforcesRealFloorAndCeiling_NotNominalOnes(string propertyName, int value, bool expectedValid)
    {
        var options = new Argon2CryptographyOptions();
        typeof(Argon2CryptographyOptions).GetProperty(propertyName)!.SetValue(options, value);

        var results = new List<ValidationResult>();
        bool isValid = Validator.TryValidateObject(
            options, new ValidationContext(options), results, validateAllProperties: true);

        Assert.Equal(expectedValid, isValid);
    }

    [Fact]
    public void MinIterations_IsAtLeastTwo_UnlikePbkdf2IterationsOriginalNominalFloorOfOne()
    {
        // WO-083/P-512 tracks the corresponding fix on CryptographyOptions.Pbkdf2Iterations —
        // this package must never repeat that [Range(1, int.MaxValue)] mistake.
        Assert.True(Argon2CryptographyOptions.MinIterations > 1);
    }
}
