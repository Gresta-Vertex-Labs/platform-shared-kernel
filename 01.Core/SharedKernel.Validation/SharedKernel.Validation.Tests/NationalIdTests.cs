using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using Xunit;

namespace SharedKernel.Validation.Tests;

public sealed class NationalIdTests
{
    private static readonly CountryCode Turkiye = CountryCode.Parse("TR", null);
    private static readonly CountryCode Germany = CountryCode.Parse("DE", null);

    [Theory]
    [InlineData("10000000146")]
    [InlineData("12345678950")]
    [InlineData("123 456 789-50")]
    public void Turkish_Valid(string value)
    {
        Result<NationalId> result = NationalId.Create(Turkiye, value);

        Assert.True(result.IsSuccess);
        Assert.Equal("TR", result.Value.Country.Value);
    }

    [Theory]
    [InlineData("1000000014", ValidationErrorCodes.NationalId.InvalidFormat)]
    [InlineData("01000000146", ValidationErrorCodes.NationalId.InvalidFormat)]
    [InlineData("1000000014A", ValidationErrorCodes.NationalId.InvalidFormat)]
    [InlineData("10000000156", ValidationErrorCodes.NationalId.InvalidCheckDigit)]
    [InlineData("10000000147", ValidationErrorCodes.NationalId.InvalidCheckDigit)]
    public void Turkish_Invalid_ReturnsTheSpecificCode(string value, string code)
    {
        Error error = NationalId.Create(Turkiye, value).Error;

        Assert.Equal(code, error.Code);
        Assert.Equal("TR", error.MessageArguments["country"]);
        Assert.DoesNotContain(value, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ToString_IsMasked()
    {
        NationalId id = NationalId.Create(Turkiye, "10000000146").Value;

        Assert.Equal("*******0146", id.ToString());
        Assert.Equal("10000000146", id.Value);
    }

    [Fact]
    public void UnregisteredCountry_ReturnsUnsupportedCountry()
    {
        Error error = NationalId.Create(Germany, "123").Error;

        Assert.Equal(ValidationErrorCodes.NationalId.UnsupportedCountry, error.Code);
        Assert.Equal("DE", error.MessageArguments["country"]);
    }

    [Fact]
    public void Registry_CustomValidatorAddsACountry_AndReplacesABuiltInOne()
    {
        var registry = new NationalIdValidatorRegistry([new AcceptAll(Germany), new AcceptAll(Turkiye)]);

        Assert.True(NationalId.Create(Germany, "anything", registry).IsSuccess);
        Assert.True(NationalId.Create(Turkiye, "not-a-tckn", registry).IsSuccess);
        Assert.Equal(["DE", "TR"], registry.Countries.Order());
        Assert.Equal(["TR"], NationalIdValidatorRegistry.Default.Countries);
    }

    [Fact]
    public void AddSharedKernelValidation_BuildsTheRegistryFromTheContainer()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelValidation();
        services.AddNationalIdValidator<GermanTestValidator>();
        services.AddNationalIdValidator<GermanTestValidator>();
        services.AddSharedKernelValidation();

        using ServiceProvider provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<NationalIdValidatorRegistry>();

        Assert.Single(services, d => d.ServiceType == typeof(NationalIdValidatorRegistry));
        Assert.Single(services, d => d.ServiceType == typeof(INationalIdValidator));
        Assert.True(registry.TryGetValidator(Germany, out _));
        Assert.True(registry.TryGetValidator(Turkiye, out _));
    }

    private sealed class AcceptAll(CountryCode country) : INationalIdValidator
    {
        public CountryCode Country { get; } = country;

        public Result Validate(string number) => Result.Success();
    }

    private sealed class GermanTestValidator : INationalIdValidator
    {
        public CountryCode Country => CountryCode.Parse("DE", null);

        public Result Validate(string number) => Result.Success();
    }
}
