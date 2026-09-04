using SharedKernel.Validation.Errors;
using SharedKernel.Validation.Validators;
using Xunit;

namespace SharedKernel.Validation.Tests;

public sealed class E164PhoneValidatorTests
{
    // NANP-reserved fictional numbers (555-0100 through 555-0199) — guaranteed-safe, real
    // reserved test numbers, still genuinely E.164-format-valid.
    [Theory]
    [InlineData("+15555550100")]
    [InlineData("+905551234567")] // Turkey mobile-shaped
    [InlineData("+442071838750")] // UK-shaped
    [InlineData("+861234567890")] // 12 digits after '+'
    public void Validate_WellFormedNumber_Succeeds(string phone)
    {
        Assert.True(E164PhoneValidator.IsValid(phone));
        Assert.True(E164PhoneValidator.Validate(phone).IsSuccess);
    }

    [Theory]
    [InlineData("05555550100")]              // missing leading '+'
    [InlineData("+0123456789")]              // leading zero after '+'
    [InlineData("+1234567890123456")]        // 16 digits after '+' — exceeds the 15-digit maximum
    [InlineData("+1 555 555 0100")]           // spaces are not accepted (format-only, no normalization)
    [InlineData("not-a-phone-number")]
    [InlineData("")]
    [InlineData(null)]
    public void Validate_MalformedNumber_ReturnsInvalidFormat(string? phone)
    {
        var result = E164PhoneValidator.Validate(phone);

        Assert.True(result.IsFailure);
        Assert.Equal(ValidationErrorCodes.Phone.InvalidFormat, result.Error.Code);
    }
}
