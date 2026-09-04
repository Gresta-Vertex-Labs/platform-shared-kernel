using SharedKernel.Validation.NationalId;
using Xunit;

namespace SharedKernel.Validation.Tests;

public sealed class NationalIdValidatorRegistryTests
{
    [Fact]
    public void TryGetValidator_Tr_ResolvesTckNationalIdValidatorByDefault()
    {
        var registry = new NationalIdValidatorRegistry();

        bool found = registry.TryGetValidator("TR", out INationalIdValidator? validator);

        Assert.True(found);
        Assert.NotNull(validator);
        Assert.IsType<TckNationalIdValidator>(validator);
        Assert.Equal("TR", validator!.CountryCode);
    }

    [Fact]
    public void TryGetValidator_IsCaseInsensitive()
    {
        var registry = new NationalIdValidatorRegistry();

        Assert.True(registry.TryGetValidator("tr", out _));
    }

    [Fact]
    public void TryGetValidator_UnregisteredCountry_ReturnsFalse_NeverThrows()
    {
        var registry = new NationalIdValidatorRegistry();

        bool found = registry.TryGetValidator("XX", out INationalIdValidator? validator);

        Assert.False(found);
        Assert.Null(validator);
    }

    // Registering an additional country beyond the pre-seeded "TR" default is exercised through
    // the sanctioned public path — ValidationServiceCollectionExtensionsTests, via
    // AddSharedKernelValidation().AddNationalIdValidator<TValidator>() — since `Register` itself
    // is internal (consumers are never meant to call it directly).
}
