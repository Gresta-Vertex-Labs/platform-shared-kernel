using System.Reflection;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using Xunit;

namespace SharedKernel.Validation.Tests;

public sealed class IbanTests
{
    // Example IBANs published in the SWIFT IBAN registry and by the national banking associations.
    [Theory]
    [InlineData("DE89370400440532013000")]
    [InlineData("GB82WEST12345698765432")]
    [InlineData("FR1420041010050500013M02606")]
    [InlineData("NL91ABNA0417164300")]
    [InlineData("BE68539007547034")]
    [InlineData("CH9300762011623852957")]
    [InlineData("AT611904300234573201")]
    [InlineData("ES9121000418450200051332")]
    [InlineData("IT60X0542811101000000123456")]
    [InlineData("TR330006100519786457841326")]
    [InlineData("NO9386011117947")]
    [InlineData("SE4550000000058398257466")]
    [InlineData("PL61109010140000071219812874")]
    [InlineData("AL47212110090000000235698741")]
    [InlineData("GR1601101250000000012300695")]
    [InlineData("MT84MALT011000012345MTLCAST001S")]
    [InlineData("SA0380000000608010167519")]
    [InlineData("AE070331234567890123456")]
    [InlineData("KW81CBKU0000000000001234560101")]
    [InlineData("BR1800360305000010009795493C1")]
    [InlineData("IE29AIBK93115212345678")]
    [InlineData("PT50000201231234567890154")]
    [InlineData("LU280019400644750000")]
    [InlineData("FI2112345600000785")]
    [InlineData("DK5000400440116243")]
    [InlineData("HU42117730161111101800000000")]
    [InlineData("CZ6508000000192000145399")]
    [InlineData("SK3112000000198742637541")]
    [InlineData("BG80BNBG96611020345678")]
    [InlineData("HR1210010051863000160")]
    [InlineData("SI56263300012039086")]
    [InlineData("IS140159260076545510730339")]
    [InlineData("CY17002001280000001200527600")]
    [InlineData("EE382200221020145685")]
    [InlineData("LT121000011101001000")]
    [InlineData("LV80BANK0000435195001")]
    [InlineData("IL620108000000099999999")]
    [InlineData("JO94CBJO0010000000000131000302")]
    [InlineData("QA58DOHB00001234567890ABCDEFG")]
    [InlineData("MU17BOMM0101101030300200000MUR")]
    [InlineData("SC18SSCB11010000000000001497USD")]
    [InlineData("UA213223130000026007233566001")]
    public void Create_PublishedExample_IsValid(string value)
    {
        Result<Iban> result = Iban.Create(value);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(value, result.Value.Value);
    }

    [Fact]
    public void Create_EveryRegistryCountry_AcceptsAnIbanBuiltFromItsStructure()
    {
        // Builds one IBAN per country from the registry's BBAN structure (digits as 1, letters as B,
        // mixed as 7) with correct check digits, proving every country's length and structure are
        // consistent and reachable.
        FieldInfo field = typeof(Iban).Assembly.GetType("SharedKernel.Validation.Internal.IbanRegistry")!
            .GetField("Countries")!;
        var countries = (System.Collections.IDictionary)field.GetValue(null)!;
        Assert.Equal(89, countries.Count);

        foreach (System.Collections.DictionaryEntry entry in countries)
        {
            string country = (string)entry.Key;
            string mask = (string)entry.Value!.GetType().GetProperty("BbanMask")!.GetValue(entry.Value)!;
            string bban = new(mask.Select(c => c switch { 'n' => '1', 'a' => 'B', _ => '7' }).ToArray());
            string iban = TestIbans.WithCheckDigits(country, bban);

            Result<Iban> result = Iban.Create(iban);
            Assert.True(result.IsSuccess, $"{country}: {(result.IsFailure ? result.Error.Code : string.Empty)}");
        }
    }

    [Fact]
    public void Create_NormalizesSpacesHyphensAndCase()
    {
        Iban iban = Iban.Parse(" de89 3704-0044 0532 0130 00 ", null);

        Assert.Equal("DE89370400440532013000", iban.Value);
        Assert.Equal("DE", iban.CountryCode.Value);
        Assert.Equal("89", iban.CheckDigits);
        Assert.Equal("370400440532013000", iban.Bban);
        Assert.Equal("DE89 3704 0044 0532 0130 00", iban.ToPrintString());
        Assert.Equal("DE89370400440532013000", iban.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_Blank_ReturnsRequired(string? value)
    {
        Assert.Equal(ErrorCodes.Validation.Required, Iban.Create(value).Error.Code);
    }

    [Theory]
    [InlineData("D", ValidationErrorCodes.Iban.InvalidFormat)]
    [InlineData("1E89370400440532013000", ValidationErrorCodes.Iban.InvalidFormat)]
    [InlineData("DEX9370400440532013000", ValidationErrorCodes.Iban.InvalidFormat)]
    [InlineData("DE89370400440532013_00", ValidationErrorCodes.Iban.InvalidFormat)]
    [InlineData("DE8937040044053201300000000000000000", ValidationErrorCodes.Iban.InvalidFormat)]
    [InlineData("US89370400440532013000", ValidationErrorCodes.Iban.UnsupportedCountry)]
    [InlineData("DE8937040044053201300", ValidationErrorCodes.Iban.InvalidLength)]
    [InlineData("DE8937040044053201300A", ValidationErrorCodes.Iban.InvalidBban)]
    [InlineData("DE88370400440532013000", ValidationErrorCodes.Iban.InvalidCheckDigits)]
    [InlineData("DE89370400440532013001", ValidationErrorCodes.Iban.InvalidCheckDigits)]
    public void Create_Invalid_ReturnsTheSpecificCode(string value, string code)
    {
        Error error = Iban.Create(value).Error;

        Assert.Equal(code, error.Code);
        Assert.Equal(ErrorType.Validation, error.Type);
    }

    [Fact]
    public void Create_InvalidLength_CarriesCountryExpectedAndActual()
    {
        Error error = Iban.Create("DE8937040044053201300").Error;

        Assert.Equal("DE", error.MessageArguments["country"]);
        Assert.Equal(22, error.MessageArguments["expected"]);
        Assert.Equal(21, error.MessageArguments["actual"]);
        Assert.Equal("An IBAN from DE is 22 characters long, not 21.", error.Message);
    }

    [Fact]
    public void Create_TurkishIbanWithNonZeroReservedDigit_FailsTheBbanStructureNotJustTheChecksum()
    {
        // TR: 5 digits bank code, 1 reserved digit, 16 alphanumeric. A letter in the bank code is
        // caught by the structure check, with correct check digits.
        string iban = TestIbans.WithCheckDigits("TR", "0006A00519786457841326");

        Assert.Equal(ValidationErrorCodes.Iban.InvalidBban, Iban.Create(iban).Error.Code);
    }

    [Fact]
    public void Create_AllowUnregisteredCountry_ChecksShapeAndCheckDigitsOnly()
    {
        string unregistered = TestIbans.WithCheckDigits("US", "12345678901234");

        Assert.Equal(ValidationErrorCodes.Iban.UnsupportedCountry, Iban.Create(unregistered).Error.Code);
        Assert.True(Iban.Create(unregistered, allowUnregisteredCountry: true).IsSuccess);
        Assert.Equal(
            ValidationErrorCodes.Iban.UnsupportedCountry,
            Iban.Create(TestIbans.WithCheckDigits("QQ", "12345678"), allowUnregisteredCountry: true).Error.Code);
        Assert.Equal(
            ValidationErrorCodes.Iban.InvalidLength,
            Iban.Create("DE8937040044053201300", allowUnregisteredCountry: true).Error.Code);
    }

    [Fact]
    public void Parse_Invalid_ThrowsFormatExceptionWithTheMessage()
    {
        FormatException ex = Assert.Throws<FormatException>(() => Iban.Parse("DE88370400440532013000", null));

        Assert.Contains("check digits", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Default_HasEmptyValue()
    {
        Assert.Equal(string.Empty, default(Iban).Value);
        Assert.Equal(string.Empty, default(Iban).ToPrintString());
    }
}

internal static class TestIbans
{
    // Independent ISO 13616 check-digit calculation (BigInteger), for building test IBANs.
    public static string WithCheckDigits(string country, string bban)
    {
        string rearranged = bban + country + "00";
        string numeric = string.Concat(rearranged.Select(c => char.IsAsciiDigit(c) ? c.ToString() : (c - 'A' + 10).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        int check = 98 - (int)(System.Numerics.BigInteger.Parse(numeric, System.Globalization.CultureInfo.InvariantCulture) % 97);
        return country + check.ToString("D2", System.Globalization.CultureInfo.InvariantCulture) + bban;
    }
}
