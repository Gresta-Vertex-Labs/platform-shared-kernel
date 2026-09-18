using SharedKernel.Primitives.Errors;
using Xunit;

namespace SharedKernel.Validation.Tests;

public sealed class IdentifierTests
{
    [Theory]
    [InlineData("DEUTDEFF", "DEUT", "DE", "FF", null, true)]
    [InlineData("deutdeff500", "DEUT", "DE", "FF", "500", false)]
    [InlineData("TGBATRIS XXX", "TGBA", "TR", "IS", "XXX", true)]
    [InlineData("RBKOXKPR", "RBKO", "XK", "PR", null, true)]
    public void Bic_Valid_ExposesItsParts(string value, string bank, string country, string location, string? branch, bool headOffice)
    {
        Bic bic = Bic.Parse(value, null);

        Assert.Equal(bank, bic.BankCode);
        Assert.Equal(country, bic.CountryCode.Value);
        Assert.Equal(location, bic.LocationCode);
        Assert.Equal(branch, bic.BranchCode);
        Assert.Equal(headOffice, bic.IsHeadOffice);
    }

    [Theory]
    [InlineData("DEUTDEF", ValidationErrorCodes.Bic.InvalidFormat)]
    [InlineData("DEUT1EFF", ValidationErrorCodes.Bic.InvalidFormat)]
    [InlineData("DEUTDEFF50", ValidationErrorCodes.Bic.InvalidFormat)]
    [InlineData("DEUTQQFF", ValidationErrorCodes.Bic.UnknownCountry)]
    public void Bic_Invalid_ReturnsTheSpecificCode(string value, string code)
    {
        Assert.Equal(code, Bic.Create(value).Error.Code);
    }

    [Theory]
    [InlineData(" tr ", "TR")]
    [InlineData("XK", "XK")]
    public void CountryCode_Valid_IsNormalized(string value, string expected)
    {
        Assert.Equal(expected, CountryCode.Parse(value, null).Value);
    }

    [Theory]
    [InlineData("T", ValidationErrorCodes.CountryCode.InvalidFormat)]
    [InlineData("T1", ValidationErrorCodes.CountryCode.InvalidFormat)]
    [InlineData("TUR", ValidationErrorCodes.CountryCode.InvalidFormat)]
    [InlineData("QQ", ValidationErrorCodes.CountryCode.Unknown)]
    public void CountryCode_Invalid_ReturnsTheSpecificCode(string value, string code)
    {
        Assert.Equal(code, CountryCode.Create(value).Error.Code);
    }

    [Theory]
    [InlineData("try", "TRY")]
    [InlineData("XCG", "XCG")]
    [InlineData("ZWG", "ZWG")]
    [InlineData("VED", "VED")]
    public void CurrencyCode_Valid_IsNormalized(string value, string expected)
    {
        Assert.Equal(expected, CurrencyCode.Parse(value, null).Value);
    }

    [Theory]
    [InlineData("TR", ValidationErrorCodes.CurrencyCode.InvalidFormat)]
    [InlineData("T1Y", ValidationErrorCodes.CurrencyCode.InvalidFormat)]
    [InlineData("ANG", ValidationErrorCodes.CurrencyCode.Unknown)]
    [InlineData("ZWL", ValidationErrorCodes.CurrencyCode.Unknown)]
    [InlineData("HRK", ValidationErrorCodes.CurrencyCode.Unknown)]
    [InlineData("XAU", ValidationErrorCodes.CurrencyCode.Unknown)]
    public void CurrencyCode_InvalidOrWithdrawn_ReturnsTheSpecificCode(string value, string code)
    {
        Assert.Equal(code, CurrencyCode.Create(value).Error.Code);
    }

    [Theory]
    [InlineData("+90 (532) 123-45-67", "+905321234567")]
    [InlineData("+1.415.555.2671", "+14155552671")]
    [InlineData("+6831234", "+6831234")]
    [InlineData("+123456789012345", "+123456789012345")]
    public void PhoneNumber_Valid_IsNormalizedToE164(string value, string expected)
    {
        Assert.Equal(expected, PhoneNumber.Parse(value, null).Value);
    }

    [Theory]
    [InlineData("05321234567")]
    [InlineData("+05321234567")]
    [InlineData("+123456")]
    [InlineData("+1234567890123456")]
    [InlineData("+90 532 ABC 45 67")]
    public void PhoneNumber_Invalid_ReturnsInvalidFormat(string value)
    {
        Assert.Equal(ValidationErrorCodes.PhoneNumber.InvalidFormat, PhoneNumber.Create(value).Error.Code);
    }

    [Theory]
    [InlineData("5493001KJTIIGC8Y1R12")]
    [InlineData("hwupkr0mpou8fgxbt394")]
    public void Lei_Valid_IsNormalized(string value)
    {
        Assert.Equal(value.ToUpperInvariant(), Lei.Parse(value, null).Value);
    }

    [Theory]
    [InlineData("5493001KJTIIGC8Y1R1", ValidationErrorCodes.Lei.InvalidFormat)]
    [InlineData("5493001KJTIIGC8Y1RAB", ValidationErrorCodes.Lei.InvalidFormat)]
    [InlineData("5493001KJTIIGC8Y1R13", ValidationErrorCodes.Lei.InvalidCheckDigits)]
    public void Lei_Invalid_ReturnsTheSpecificCode(string value, string code)
    {
        Assert.Equal(code, Lei.Create(value).Error.Code);
    }

    [Theory]
    [InlineData("011000015")]
    [InlineData("021000021")]
    [InlineData("322271627")]
    [InlineData("0110-0001-5")]
    public void AbaRoutingNumber_Valid(string value)
    {
        Assert.True(AbaRoutingNumber.IsValid(value));
    }

    [Theory]
    [InlineData("01100001", ValidationErrorCodes.AbaRoutingNumber.InvalidFormat)]
    [InlineData("01100001X", ValidationErrorCodes.AbaRoutingNumber.InvalidFormat)]
    [InlineData("131000015", ValidationErrorCodes.AbaRoutingNumber.InvalidPrefix)]
    [InlineData("500000005", ValidationErrorCodes.AbaRoutingNumber.InvalidPrefix)]
    [InlineData("011000016", ValidationErrorCodes.AbaRoutingNumber.InvalidCheckDigit)]
    [InlineData("000000000", ValidationErrorCodes.AbaRoutingNumber.InvalidCheckDigit)]
    public void AbaRoutingNumber_Invalid_ReturnsTheSpecificCode(string value, string code)
    {
        Assert.Equal(code, AbaRoutingNumber.Create(value).Error.Code);
    }

    [Fact]
    public void SepaCreditorId_Valid_ExposesItsParts()
    {
        SepaCreditorId id = SepaCreditorId.Parse("de98 zzz0 9999 9999 99", null);

        Assert.Equal("DE98ZZZ09999999999", id.Value);
        Assert.Equal("DE", id.CountryCode.Value);
        Assert.Equal("ZZZ", id.BusinessCode);
        Assert.Equal("09999999999", id.NationalIdentifier);
    }

    [Theory]
    [InlineData("DE98ZZZ", ValidationErrorCodes.SepaCreditorId.InvalidFormat)]
    [InlineData("D198ZZZ09999999999", ValidationErrorCodes.SepaCreditorId.InvalidFormat)]
    [InlineData("QQ98ZZZ09999999999", ValidationErrorCodes.SepaCreditorId.UnknownCountry)]
    [InlineData("DE97ZZZ09999999999", ValidationErrorCodes.SepaCreditorId.InvalidCheckDigits)]
    public void SepaCreditorId_Invalid_ReturnsTheSpecificCode(string value, string code)
    {
        Assert.Equal(code, SepaCreditorId.Create(value).Error.Code);
    }

    [Fact]
    public void SepaCreditorId_BusinessCodeIsNotPartOfTheCheck()
    {
        Assert.True(SepaCreditorId.IsValid("DE98ABC09999999999"));
    }

    [Fact]
    public void EveryType_ReturnsRequired_ForBlankInput()
    {
        string[] codes =
        [
            Iban.Create(" ").Error.Code, Bic.Create(" ").Error.Code, CardNumber.Create(" ").Error.Code,
            CountryCode.Create(" ").Error.Code, CurrencyCode.Create(" ").Error.Code, PhoneNumber.Create(" ").Error.Code,
            Lei.Create(" ").Error.Code, AbaRoutingNumber.Create(" ").Error.Code, SepaCreditorId.Create(" ").Error.Code,
            VatNumber.Create(" ").Error.Code, NationalId.Create(CountryCode.Parse("TR", null), " ").Error.Code,
        ];

        Assert.All(codes, code => Assert.Equal(ErrorCodes.Validation.Required, code));
    }
}
