using SharedKernel.Validation.Errors;
using SharedKernel.Validation.Validators;
using Xunit;

namespace SharedKernel.Validation.Tests;

public sealed class IsoCountryValidatorTests
{
    [Theory]
    [InlineData("US")]
    [InlineData("GB")]
    [InlineData("DE")]
    [InlineData("FR")]
    [InlineData("TR")]
    [InlineData("us")] // case-insensitive
    public void Validate_KnownGoodCode_Succeeds(string code)
    {
        Assert.True(IsoCountryValidator.IsValid(code));
        Assert.True(IsoCountryValidator.Validate(code).IsSuccess);
    }

    [Theory]
    [InlineData("ZZ")] // well-formed, not an assigned ISO 3166-1 code
    [InlineData("QQ")]
    [InlineData("USA")] // alpha-3, not alpha-2
    [InlineData("U")]
    [InlineData("")]
    [InlineData(null)]
    public void Validate_UnknownOrMalformedCode_ReturnsUnknownCode(string? code)
    {
        var result = IsoCountryValidator.Validate(code);

        Assert.True(result.IsFailure);
        Assert.Equal(ValidationErrorCodes.Country.UnknownCode, result.Error.Code);
    }
}
