using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using Xunit;

namespace SharedKernel.Validation.Tests;

/// <summary>
/// Valid numbers are the published examples from python-stdnum's documentation, an independent
/// implementation of each country's rules; each invalid case changes one check digit.
/// </summary>
public sealed class VatNumberTests
{
    [Theory]
    [InlineData("ATU13585627", "AT", "U13585627")]
    [InlineData("BE0403019261", "BE", "0403019261")]
    [InlineData("BE 428759497", "BE", "0428759497")]
    [InlineData("BG175074752", "BG", "175074752")]
    [InlineData("CY-10259033P", "CY", "10259033P")]
    [InlineData("CZ25123891", "CZ", "25123891")]
    [InlineData("CZ7103192745", "CZ", "7103192745")]
    [InlineData("DE 136,695 976", "DE", "136695976")]
    [InlineData("DK 13585628", "DK", "13585628")]
    [InlineData("EE100594102", "EE", "100594102")]
    [InlineData("EE100931558", "EE", "100931558")]
    [InlineData("EL094259216", "EL", "094259216")]
    [InlineData("GR094259216", "EL", "094259216")]
    [InlineData("ESB58378431", "ES", "B58378431")]
    [InlineData("ESB64717838", "ES", "B64717838")]
    [InlineData("ES54362315K", "ES", "54362315K")]
    [InlineData("ESX2482300W", "ES", "X2482300W")]
    [InlineData("ESJ99216582", "ES", "J99216582")]
    [InlineData("FI 20774740", "FI", "20774740")]
    [InlineData("FR 40 303 265 045", "FR", "40303265045")]
    [InlineData("FR23334175221", "FR", "23334175221")]
    [InlineData("FRK7399859412", "FR", "K7399859412")]
    [InlineData("FR83404833048", "FR", "83404833048")]
    [InlineData("HR 33392005961", "HR", "33392005961")]
    [InlineData("HU-12892312", "HU", "12892312")]
    [InlineData("IE 6433435F", "IE", "6433435F")]
    [InlineData("IE 6433435OA", "IE", "6433435OA")]
    [InlineData("IE8D79739I", "IE", "8D79739I")]
    [InlineData("IT 00743110157", "IT", "00743110157")]
    [InlineData("LT119511515", "LT", "119511515")]
    [InlineData("LT 100001919017", "LT", "100001919017")]
    [InlineData("LT100004801610", "LT", "100004801610")]
    [InlineData("LU 150 274 42", "LU", "15027442")]
    [InlineData("LV 4000 3521 600", "LV", "40003521600")]
    [InlineData("MT 1167-9112", "MT", "11679112")]
    [InlineData("NL004495445B01", "NL", "004495445B01")]
    [InlineData("NL4495445B01", "NL", "004495445B01")]
    [InlineData("NL002455799B11", "NL", "002455799B11")]
    [InlineData("PL 8567346215", "PL", "8567346215")]
    [InlineData("PT 501 964 843", "PT", "501964843")]
    [InlineData("RO 185 472 90", "RO", "18547290")]
    [InlineData("SE 123456789701", "SE", "123456789701")]
    [InlineData("SI 5022 3054", "SI", "50223054")]
    [InlineData("SK 202 274 96 19", "SK", "2022749619")]
    [InlineData("GB 980 7806 84", "GB", "980780684")]
    [InlineData("XI980780684", "XI", "980780684")]
    [InlineData("CHE-107.787.577 IVA", "CH", "107787577IVA")]
    [InlineData("NO 995 525 828 MVA", "NO", "995525828MVA")]
    [InlineData("TR4540536920", "TR", "4540536920")]
    public void Create_PublishedValidNumber_IsValid(string value, string prefix, string number)
    {
        Result<VatNumber> result = VatNumber.Create(value);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(prefix, result.Value.Prefix);
        Assert.Equal(number, result.Value.Number);
    }

    [Theory]
    [InlineData("ATU13585626")]
    [InlineData("BE0403019262")]
    [InlineData("BG175074751")]
    [InlineData("CY10259033Z")]
    [InlineData("CZ25123890")]
    [InlineData("DE136695978")]
    [InlineData("DK13585627")]
    [InlineData("EE100594103")]
    [InlineData("EL123456781")]
    [InlineData("ESB64717839")]
    [InlineData("ES54362315Z")]
    [InlineData("ESX2482300A")]
    [InlineData("ESJ99216583")]
    [InlineData("FI20774741")]
    [InlineData("FR84323140391")]
    [InlineData("HR33392005962")]
    [InlineData("HU12892313")]
    [InlineData("IE6433435E")]
    [InlineData("IT00743110158")]
    [InlineData("LT100001919018")]
    [InlineData("LU15027443")]
    [InlineData("LV40003521601")]
    [InlineData("MT11679113")]
    [InlineData("NL004495446B01")]
    [InlineData("PL8567346216")]
    [InlineData("PT501964842")]
    [InlineData("RO18547291")]
    [InlineData("SE123456789101")]
    [InlineData("SI50223055")]
    [InlineData("SK2022749618")]
    [InlineData("GB802311781")]
    [InlineData("CHE107787578IVA")]
    [InlineData("NO995525829MVA")]
    [InlineData("TR4540536921")]
    public void Create_WrongCheckDigit_ReturnsInvalidCheckDigit(string value)
    {
        Error error = VatNumber.Create(value).Error;

        Assert.Equal(ValidationErrorCodes.VatNumber.InvalidCheckDigit, error.Code);
        Assert.Equal(value[..2] == "CH" ? "CH" : value[..2], error.MessageArguments["country"]);
    }

    [Theory]
    [InlineData("DE12345678")]
    [InlineData("DE01234567")]
    [InlineData("ATX13585627")]
    [InlineData("BE2403019261")]
    [InlineData("NL004495445C01")]
    [InlineData("IT00743119157")]
    [InlineData("TR454053692")]
    [InlineData("CHE107787577XYZ")]
    [InlineData("SE123456789702")]
    public void Create_WrongFormat_ReturnsInvalidFormat(string value)
    {
        Assert.Equal(ValidationErrorCodes.VatNumber.InvalidFormat, VatNumber.Create(value).Error.Code);
    }

    [Theory]
    [InlineData("US123456789", "US")]
    [InlineData("123456789", "")]
    [InlineData("D", "")]
    public void Create_NoOrUnsupportedPrefix_ReturnsUnsupportedCountry(string value, string country)
    {
        Error error = VatNumber.Create(value).Error;

        Assert.Equal(ValidationErrorCodes.VatNumber.UnsupportedCountry, error.Code);
        Assert.Equal(country, error.MessageArguments["country"]);
    }

    [Fact]
    public void Create_WithCountry_AcceptsTheNumberWithOrWithoutItsPrefix()
    {
        CountryCode turkiye = CountryCode.Parse("TR", null);

        Assert.Equal("TR4540536920", VatNumber.Create(turkiye, "4540536920").Value.Value);
        Assert.Equal("TR4540536920", VatNumber.Create(turkiye, "TR 4540536920").Value.Value);
        Assert.Equal("EL094259216", VatNumber.Create(CountryCode.Parse("GR", null), "094259216").Value.Value);
        Assert.Equal("CHE107787577IVA", VatNumber.Create(CountryCode.Parse("CH", null), "107.787.577 IVA").Value.Value);
        Assert.Equal(ErrorCodes.Validation.Required, VatNumber.Create(turkiye, " ").Error.Code);
    }

    [Fact]
    public void Value_IncludesThePrefix_AndSupportedPrefixesListsEveryCountry()
    {
        Assert.Equal("CHE107787577IVA", VatNumber.Parse("CHE-107.787.577 IVA", null).Value);
        Assert.Equal(32, VatNumber.SupportedPrefixes.Count);
        Assert.Contains("EL", VatNumber.SupportedPrefixes);
        Assert.DoesNotContain("GR", VatNumber.SupportedPrefixes);
    }
}
