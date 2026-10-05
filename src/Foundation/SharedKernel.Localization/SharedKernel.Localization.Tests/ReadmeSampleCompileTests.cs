using System.Globalization;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using Xunit;

namespace SharedKernel.Localization.Tests;

/// <summary>
/// Runs the code shown in <c>SharedKernel.Localization/README.md</c>, so a sample that stops
/// compiling or stops doing what the README says fails here instead of in a reader's project.
/// Recipe 1 needs ASP.NET Core and is verified outside this project.
/// </summary>
public sealed class ReadmeSampleCompileTests : IDisposable
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    private const string TurkishJson = """
        {
          "order": {
            "not_found": "{orderId} numaralı sipariş bulunamadı.",
            "over_limit": "Sipariş tutarı {total:N2}, {limit:N2} olan limitinizi aşıyor.",
            "cannot_cancel": "Kargolanan sipariş iptal edilemez."
          }
        }
        """;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sk-readme-" + Guid.NewGuid().ToString("N"));

    public ReadmeSampleCompileTests()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "tr.json"), TurkishJson, Encoding.UTF8);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private ILocalizationCatalog RegisterAsInQuickStart()
    {
        var services = new ServiceCollection();
        services.AddLocalizationCatalog(catalog => catalog.AddJsonDirectory(_directory));
        return services.BuildServiceProvider().GetRequiredService<ILocalizationCatalog>();
    }

    [Fact]
    public void QuickStart_ErrorFromDefinition_HasReadableMessage_AndTranslates()
    {
        Guid id = Guid.Parse("3f2a0000-0000-0000-0000-000000000001");
        Result<Order> result = new OrderService().Find(id, new Dictionary<Guid, Order>());
        ILocalizationCatalog catalog = RegisterAsInQuickStart();

        Assert.True(result.IsFailure);
        Assert.Equal($"Order {id} was not found.", result.Error.Message);
        Assert.Equal(id, result.Error.MessageArguments["orderId"]);
        Assert.Equal($"{id} numaralı sipariş bulunamadı.", catalog.Localize(result.Error, Turkish));
        Assert.Equal($"Order {id} was not found.", catalog.Localize(result.Error, CultureInfo.GetCultureInfo("fr-FR")));
    }

    [Fact]
    public void QuickStart_ParameterlessMessage_Translates()
    {
        ILocalizationCatalog catalog = RegisterAsInQuickStart();

        Error error = OrderMessages.CannotCancel.ToError(ErrorType.BusinessRule);

        Assert.Equal("Kargolanan sipariş iptal edilemez.", catalog.Localize(error, Turkish));
    }

    [Fact]
    public void Recipe2_FormatOutsideHttp()
    {
        var emails = new OrderEmails(RegisterAsInQuickStart());

        Assert.Equal(
            "Sipariş tutarı 1.500,50, 1.000,00 olan limitinizi aşıyor.",
            emails.OverLimitLine(Turkish, 1500.5m, 1000m));
        Assert.Equal(
            "The order total 1,500.50 exceeds your limit of 1,000.00.",
            emails.OverLimitLine(CultureInfo.GetCultureInfo("en-US"), 1500.5m, 1000m));
    }

    [Fact]
    public void TranslationFiles_FlatAndNestedFormsAreEquivalent()
    {
        ILocalizationCatalog flat = new LocalizationCatalogBuilder()
            .AddJson(new MemoryStream(Encoding.UTF8.GetBytes("""{ "order.not_found": "{orderId} numaralı sipariş bulunamadı." }""")), Turkish)
            .Build();
        ILocalizationCatalog nested = new LocalizationCatalogBuilder()
            .AddJson(new MemoryStream(Encoding.UTF8.GetBytes("""{ "order": { "not_found": "{orderId} numaralı sipariş bulunamadı." } }""")), Turkish)
            .Build();

        Assert.True(flat.TryGetTemplate("order.not_found", Turkish, out MessageTemplate? a));
        Assert.True(nested.TryGetTemplate("order.not_found", Turkish, out MessageTemplate? b));
        Assert.Equal(a.Text, b.Text);
    }

    [Fact]
    public void Recipe3_LibraryTranslationsFirst_ApplicationOverridesAfter()
    {
        File.WriteAllText(Path.Combine(_directory, "tr.json"), """{ "greeting": "Selam {name}." }""", Encoding.UTF8);
        var services = new ServiceCollection();

        services.AddLocalizationCatalog(catalog => catalog
            .AddEmbeddedJson(typeof(ReadmeSampleCompileTests).Assembly, "SharedKernel.Localization.Tests.TestData.Embedded.")
            .AddJsonDirectory(_directory));

        ILocalizationCatalog resolved = services.BuildServiceProvider().GetRequiredService<ILocalizationCatalog>();
        var args = new Dictionary<string, object?> { ["name"] = "Ada" };
        Assert.True(resolved.TryFormat("greeting", Turkish, args, out string? turkish));
        Assert.True(resolved.TryFormat("greeting", CultureInfo.GetCultureInfo("en-GB"), args, out string? english));
        Assert.Equal("Selam Ada.", turkish);
        Assert.Equal("Hello Ada.", english);
    }

    [Fact]
    public void Recipe4_ResxComposition_ResolvesAndFallsBack()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddLocalization(options => options.ResourcesPath = "Resources");
        services.AddStringLocalizerCatalog<ErrorMessages>();

        ILocalizationCatalog catalog = services.BuildServiceProvider().GetRequiredService<ILocalizationCatalog>();
        Error error = OrderMessages.CannotCancel.ToError(ErrorType.BusinessRule);

        // No .resx file exists in this project, so the original message is the proof of wiring.
        Assert.IsType<StringLocalizerLocalizationCatalog>(catalog);
        Assert.Equal("A shipped order cannot be cancelled.", catalog.Localize(error, Turkish));
    }

    [Fact]
    public void Recipe5_EachFieldTranslatedWithItsOwnArguments()
    {
        ILocalizationCatalog catalog = new LocalizationCatalogBuilder()
            .Add("field.required", CultureInfo.GetCultureInfo("tr"), "{field} alanı zorunludur.")
            .Build();

        Error error = Error.Validation(
        [
            FieldMessages.Required.ToError(ErrorType.Validation, "Name"),
            FieldMessages.Required.ToError(ErrorType.Validation, "Email"),
        ]);

        Assert.Equal(
            ["Name alanı zorunludur.", "Email alanı zorunludur."],
            error.Details.Select(detail => catalog.Localize(detail, Turkish)));
    }

    // ---- Types exactly as the README declares them ----

    public static class OrderMessages
    {
        public static readonly LocalizedMessage<Guid> NotFound = LocalizedMessage.Define<Guid>(
            "order.not_found", "Order {orderId} was not found.", "orderId");

        public static readonly LocalizedMessage<decimal, decimal> OverLimit = LocalizedMessage.Define<decimal, decimal>(
            "order.over_limit", "The order total {total:N2} exceeds your limit of {limit:N2}.", "total", "limit");

        public static readonly LocalizedMessage CannotCancel = LocalizedMessage.Define(
            "order.cannot_cancel", "A shipped order cannot be cancelled.");
    }

    public static class FieldMessages
    {
        public static readonly LocalizedMessage<string> Required = LocalizedMessage.Define<string>(
            "field.required", "{field} is required.", "field");
    }

    public sealed class OrderService
    {
        public Result<Order> Find(Guid orderId, IReadOnlyDictionary<Guid, Order> orders) =>
            orders.TryGetValue(orderId, out Order? order)
                ? order
                : OrderMessages.NotFound.ToError(ErrorType.NotFound, orderId);
    }

    public sealed class OrderEmails(ILocalizationCatalog catalog)
    {
        public string OverLimitLine(CultureInfo customerCulture, decimal total, decimal limit) =>
            OrderMessages.OverLimit.Format(catalog, customerCulture, total, limit);
    }

    public sealed record Order(Guid Id);

    public sealed class ErrorMessages;
}
