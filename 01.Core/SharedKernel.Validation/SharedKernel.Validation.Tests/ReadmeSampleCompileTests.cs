using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.Guards;
using SharedKernel.Localization;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using Xunit;

namespace SharedKernel.Validation.Tests;

/// <summary>
/// Runs the code shown in <c>SharedKernel.Validation/README.md</c>. Recipe 1 (FluentValidation)
/// runs in the FluentValidation package's tests; the minimal-API route in the quick start needs
/// ASP.NET Core, and is covered here by the <see cref="IParsable{TSelf}"/> binding it relies on.
/// </summary>
public sealed partial class ReadmeSampleCompileTests
{
    [Fact]
    public void QuickStart_ParseAtTheEdge()
    {
        Result<Iban> result = Iban.Create("de89 3704 0044 0532 0130 00");
        Assert.True(result.IsSuccess);

        Iban iban = result.Value;
        Assert.Equal("DE89370400440532013000", iban.Value);
        Assert.Equal("DE", iban.CountryCode.Value);
        Assert.Equal("DE89 3704 0044 0532 0130 00", iban.ToPrintString());
    }

    [Fact]
    public void QuickStart_EveryOtherTypeWorksTheSameWay()
    {
        VatNumber vat   = VatNumber.Create(CountryCode.Parse("TR", null), "4540536920").Value;
        CardNumber card = CardNumber.Parse("4111 1111 1111 1111", null);
        NationalId id   = NationalId.Create(CountryCode.Parse("TR", null), "10000000146").Value;
        bool ok         = PhoneNumber.IsValid("+90 (532) 123-45-67");

        Assert.Equal("TR4540536920", vat.Value);
        Assert.Equal(CardNetwork.Visa, card.Network);
        Assert.Equal("411111******1111", card.ToString());
        Assert.Equal("*******0146", id.ToString());
        Assert.True(ok);
        Assert.Equal("+905321234567", PhoneNumber.Parse("+90 (532) 123-45-67", null).Value);
    }

    [Fact]
    public void QuickStart_RouteBindingUsesIParsable()
    {
        Assert.True(Bic.TryParse("DEUTDEFF500", null, out Bic bic));
        Assert.Equal("DEUT in DE", $"{bic.BankCode} in {bic.CountryCode}");

        var payout = System.Text.Json.JsonSerializer.Deserialize<CreatePayout>(
            """{"Iban":"DE89370400440532013000","Currency":"try","Amount":10}""")!;
        Assert.Equal("TRY", payout.Currency.Value);
    }

    [Fact]
    public void Translations_AddValidationTranslationsThenYourOwn()
    {
        string directory = Path.Combine(Path.GetTempPath(), "sk-validation-readme-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "tr.json"), """{ "validation.iban.invalid_check_digits": "Kontrol basamakları hatalı." }""");

        try
        {
            var services = new ServiceCollection();
            services.AddLocalizationCatalog(catalog => catalog
                .AddValidationTranslations()
                .AddJsonDirectory(directory));

            ILocalizationCatalog catalog = services.BuildServiceProvider().GetRequiredService<ILocalizationCatalog>();
            var turkish = System.Globalization.CultureInfo.GetCultureInfo("tr-TR");

            Assert.Equal("Kontrol basamakları hatalı.", catalog.Localize(Iban.Create("DE88370400440532013000").Error, turkish));
            Assert.Equal("Bir değer girilmelidir.", catalog.Localize(Iban.Create(" ").Error, turkish));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Recipe2_GuardAConstructorArgument()
    {
        Assert.True(Payout.Create("DE89370400440532013000", 10m).IsSuccess);
        Assert.Equal(ValidationErrorCodes.Iban.InvalidCheckDigits, Payout.Create("DE88370400440532013000", 10m).Error.Code);
    }

    [Fact]
    public void Recipe3_AddACountrysNationalIdCheck()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelValidation().AddNationalIdValidator<DutchBsnValidator>();
        var registry = services.BuildServiceProvider().GetRequiredService<NationalIdValidatorRegistry>();
        CountryCode netherlands = CountryCode.Parse("NL", null);

        Assert.True(NationalId.Create(netherlands, "111222333", registry).IsSuccess);
        Assert.Equal(ValidationErrorCodes.NationalId.InvalidCheckDigit, NationalId.Create(netherlands, "111252333", registry).Error.Code);
    }

    [Fact]
    public void Recipe4_GenericCodeOverAnyIdentifier()
    {
        Assert.Equal("5493001KJTIIGC8Y1R12", Describe<Lei>("5493001kjtiigc8y1r12"));
        Assert.Equal("invalid", Describe<Iban>("nope"));
    }

    [Fact]
    public async Task Recipe5_LogsShowTheMaskedNumber_ThePaymentCallGetsTheFullOne()
    {
        var sink = new CapturingLoggerProvider();
        using ILoggerFactory factory = LoggerFactory.Create(b => b.AddProvider(sink));
        ILogger logger = factory.CreateLogger("payments");
        var provider = new FakePaymentProvider();
        string input = "4111 1111 1111 1111";

        CardNumber card = CardNumber.Parse(input, null);
        LogCardAccepted(logger, card);
        await provider.ChargeAsync(card.Value);

        Assert.Equal("Card 411111******1111 accepted.", Assert.Single(sink.Messages));
        Assert.Equal("4111111111111111", provider.Charged);
    }

    private static string Describe<T>(string input) where T : struct, IValidatedValue<T> =>
        T.Create(input) is { IsSuccess: true } ok ? ok.Value.Value : "invalid";

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Card {Card} accepted.")]
    private static partial void LogCardAccepted(ILogger logger, CardNumber card);

    private sealed record CreatePayout(Iban Iban, CurrencyCode Currency, decimal Amount);

    private sealed record Payout(Iban Iban, decimal Amount)
    {
        public static Result<Payout> Create(string iban, decimal amount)
        {
            if (Guard.Against.Invalid<Iban>(iban) is { } error)
            {
                return error;
            }

            return new Payout(Iban.Parse(iban, null), amount);
        }
    }

    private sealed class DutchBsnValidator : INationalIdValidator
    {
        public CountryCode Country => CountryCode.Parse("NL", null);

        public Result Validate(string number) =>
            number.Length == 9 && number.All(char.IsAsciiDigit) && ElevenTest(number)
                ? Result.Success()
                : ValidationMessages.NationalIdInvalidCheckDigit.ToError(ErrorType.Validation, "NL");

        private static bool ElevenTest(string n) =>
            (Enumerable.Range(0, 8).Sum(i => (9 - i) * (n[i] - '0')) - (n[8] - '0')) % 11 == 0;
    }

    private sealed class FakePaymentProvider
    {
        public string? Charged { get; private set; }

        public Task ChargeAsync(string cardNumber)
        {
            Charged = cardNumber;
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<string> Messages { get; } = [];

        public ILogger CreateLogger(string categoryName) => new Capturing(Messages);

        public void Dispose()
        {
        }

        private sealed class Capturing(List<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                messages.Add(formatter(state, exception));
        }
    }
}

/// <summary>Keeps the README's coverage tables identical to what the code accepts.</summary>
public sealed class ReadmeCoverageTests
{
    private static string Readme()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "README.md");
            if (File.Exists(candidate) && File.Exists(Path.Combine(dir.FullName, "SharedKernel.Validation.csproj")))
            {
                return File.ReadAllText(candidate);
            }
        }

        throw new InvalidOperationException("README.md not found above the test output directory.");
    }

    private static string[] CodesInSection(string readme, string heading, string nextHeading)
    {
        int start = readme.IndexOf(heading, StringComparison.Ordinal);
        int end = readme.IndexOf(nextHeading, start, StringComparison.Ordinal);
        return System.Text.RegularExpressions.Regex.Matches(readme[start..end], @"^\| `([A-Z]{2})` \|", System.Text.RegularExpressions.RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .Order()
            .ToArray();
    }

    [Fact]
    public void IbanTable_ListsExactlyTheRegistryCountries()
    {
        string[] listed = CodesInSection(Readme(), "### IBAN countries", "### VAT and tax numbers");

        Assert.Equal(89, listed.Length);
        var registry = (System.Collections.IDictionary)typeof(Iban).Assembly
            .GetType("SharedKernel.Validation.Internal.IbanRegistry")!.GetField("Countries")!.GetValue(null)!;
        Assert.Equal(registry.Keys.Cast<string>().Order(), listed);
    }

    [Fact]
    public void VatTable_ListsExactlyTheSupportedPrefixes()
    {
        string[] listed = CodesInSection(Readme(), "### VAT and tax numbers", "### Card networks");

        Assert.Equal(VatNumber.SupportedPrefixes.Order(), listed);
    }
}
