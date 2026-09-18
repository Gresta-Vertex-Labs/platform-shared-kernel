using System.Globalization;
using System.Reflection;
using System.Text.Json;
using SharedKernel.Guards;
using SharedKernel.Localization;
using SharedKernel.Primitives.Errors;
using Xunit;

namespace SharedKernel.Validation.Tests;

/// <summary>Behaviour every identifier type shares: JSON, parsing, equality, guards and translations.</summary>
public sealed class ValueBehaviorTests
{
    private sealed record Payment(Iban Iban, CardNumber Card, CurrencyCode Currency, VatNumber? Vat);

    [Fact]
    public void Json_RoundTripsThroughTheValue()
    {
        var payment = new Payment(
            Iban.Parse("DE89370400440532013000", null),
            CardNumber.Parse("4111111111111111", null),
            CurrencyCode.Parse("EUR", null),
            null);

        string json = JsonSerializer.Serialize(payment);
        Payment back = JsonSerializer.Deserialize<Payment>(json)!;

        Assert.Equal("""{"Iban":"DE89370400440532013000","Card":"4111111111111111","Currency":"EUR","Vat":null}""", json);
        Assert.Equal(payment, back);
    }

    [Theory]
    [InlineData("""{"Iban":"DE88370400440532013000","Card":"4111111111111111","Currency":"EUR"}""", "check digits")]
    [InlineData("""{"Iban":123,"Card":"4111111111111111","Currency":"EUR"}""", "Expected a JSON string")]
    public void Json_InvalidValue_ThrowsJsonExceptionWithTheReason(string json, string expected)
    {
        JsonException ex = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<Payment>(json));

        Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Equality_IsByNormalizedValue()
    {
        Assert.Equal(Iban.Parse("de89 3704 0044 0532 0130 00", null), Iban.Parse("DE89370400440532013000", null));
        Assert.NotEqual(Bic.Parse("DEUTDEFF", null), Bic.Parse("DEUTDEFFXXX", null));
    }

    [Fact]
    public void TryParse_ReturnsFalseAndDefault_ForInvalidInput()
    {
        Assert.False(Iban.TryParse("nope", CultureInfo.InvariantCulture, out Iban iban));
        Assert.Equal(default, iban);
        Assert.True(Lei.TryParse("5493001KJTIIGC8Y1R12", null, out Lei lei));
        Assert.Equal("5493001KJTIIGC8Y1R12", lei.Value);
    }

    [Fact]
    public void Guard_Invalid_ReturnsNullForValid_TheErrorForInvalid_AndNamesTheParameterWhenBlank()
    {
        string valid = "DE89370400440532013000";
        string invalid = "DE88370400440532013000";
        string blank = " ";

        Assert.Null(Guard.Against.Invalid<Iban>(valid));
        Assert.Equal(ValidationErrorCodes.Iban.InvalidCheckDigits, Guard.Against.Invalid<Iban>(invalid)!.Code);

        Error missing = Guard.Against.Invalid<Iban>(blank)!;
        Assert.Equal(ErrorCodes.Validation.Required, missing.Code);
        Assert.Contains("blank", missing.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Guard_InvalidNationalId()
    {
        CountryCode turkiye = CountryCode.Parse("TR", null);

        Assert.Null(Guard.Against.InvalidNationalId("10000000146", turkiye));
        Assert.Equal(ValidationErrorCodes.NationalId.InvalidCheckDigit, Guard.Against.InvalidNationalId("10000000147", turkiye)!.Code);
    }

    [Fact]
    public void EveryMessage_HasATurkishTranslation_WithTheSamePlaceholders()
    {
        InMemoryLocalizationCatalog catalog = new LocalizationCatalogBuilder().AddValidationTranslations().Build();
        CultureInfo turkish = CultureInfo.GetCultureInfo("tr-TR");

        FieldInfo[] fields = typeof(ValidationMessages).GetFields(BindingFlags.Public | BindingFlags.Static);
        Assert.NotEmpty(fields);

        foreach (FieldInfo field in fields)
        {
            object message = field.GetValue(null)!;
            string code = (string)message.GetType().GetProperty("Code")!.GetValue(message)!;
            var template = (MessageTemplate)message.GetType().GetProperty("DefaultTemplate")!.GetValue(message)!;

            Assert.True(catalog.TryGetTemplate(code, turkish, out MessageTemplate? translation), $"No Turkish text for {code}.");
            Assert.Equal(template.PlaceholderNames.Order(), translation.PlaceholderNames.Order());
        }

        Assert.Equal(fields.Length, catalog.Count);
    }

    [Fact]
    public void EveryCode_HasExactlyOneMessage()
    {
        string[] codes = typeof(ValidationErrorCodes).GetNestedTypes()
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Select(f => (string)f.GetRawConstantValue()!)
            .Append(ErrorCodes.Validation.Required)
            .Order()
            .ToArray();

        string[] messageCodes = typeof(ValidationMessages).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => f.GetValue(null)!)
            .Select(m => (string)m.GetType().GetProperty("Code")!.GetValue(m)!)
            .Order()
            .ToArray();

        Assert.Equal(codes, messageCodes);
    }

    [Fact]
    public void Localize_TranslatesAnErrorWithItsValues()
    {
        InMemoryLocalizationCatalog catalog = new LocalizationCatalogBuilder().AddValidationTranslations().Build();

        Error error = Iban.Create("DE8937040044053201300").Error;

        Assert.Equal("DE IBAN'ı 22 karakter olmalıdır, girilen 21 karakter.", catalog.Localize(error, CultureInfo.GetCultureInfo("tr-TR")));
        Assert.Equal(error.Message, catalog.Localize(error, CultureInfo.GetCultureInfo("en-US")));
    }
}
