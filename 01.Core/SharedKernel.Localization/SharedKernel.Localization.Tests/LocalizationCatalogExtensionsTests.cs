using System.Globalization;
using SharedKernel.Primitives.Errors;
using Xunit;

namespace SharedKernel.Localization.Tests;

public sealed class LocalizationCatalogExtensionsTests
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    private static readonly LocalizedMessage<int> NotFound =
        LocalizedMessage.Define<int>("order.not_found", "Order {orderNumber} was not found.", "orderNumber");

    private static readonly InMemoryLocalizationCatalog Catalog = new LocalizationCatalogBuilder()
        .Add("order.not_found", CultureInfo.GetCultureInfo("tr"), "{orderNumber} numaralı sipariş bulunamadı.")
        .Add("order.cannot_cancel", CultureInfo.GetCultureInfo("tr"), "Kargolanan sipariş iptal edilemez.")
        .Build();

    [Fact]
    public void Localize_ErrorFromDefinition_FillsTranslationWithItsArguments()
    {
        Error error = NotFound.ToError(ErrorType.NotFound, 1234);

        Assert.Equal("1234 numaralı sipariş bulunamadı.", Catalog.Localize(error, Turkish));
    }

    [Fact]
    public void Localize_NoTranslationForCulture_ReturnsErrorMessage()
    {
        Error error = NotFound.ToError(ErrorType.NotFound, 1234);

        Assert.Equal("Order 1234 was not found.", Catalog.Localize(error, CultureInfo.GetCultureInfo("de-DE")));
    }

    [Fact]
    public void Localize_TranslationNeedsArgumentsTheErrorLacks_ReturnsErrorMessage_NotARawPlaceholder()
    {
        Error error = Error.NotFound("order.not_found", "Order not found.");

        Assert.Equal("Order not found.", Catalog.Localize(error, Turkish));
    }

    [Fact]
    public void Localize_PlainErrorWithParameterlessTranslation_IsTranslated()
    {
        Error error = Error.BusinessRule("order.cannot_cancel", "A shipped order cannot be cancelled.");

        Assert.Equal("Kargolanan sipariş iptal edilemez.", Catalog.Localize(error, Turkish));
    }

    [Fact]
    public void Localize_ErrorNone_ReturnsItsEmptyMessage_WithoutThrowing()
    {
        Assert.Equal(string.Empty, Catalog.Localize(Error.None, Turkish));
    }

    [Fact]
    public void TryGetString_ParameterlessTranslation_Found_TranslationWithPlaceholders_NotFound()
    {
        Assert.True(Catalog.TryGetString("order.cannot_cancel", Turkish, out string? plain));
        Assert.Equal("Kargolanan sipariş iptal edilemez.", plain);

        Assert.False(Catalog.TryGetString("order.not_found", Turkish, out string? needsValues));
        Assert.Null(needsValues);
    }

    [Fact]
    public void TryFormat_FillsTranslation_OrReturnsFalse()
    {
        var args = new Dictionary<string, object?> { ["orderNumber"] = 7 };

        Assert.True(Catalog.TryFormat("order.not_found", Turkish, args, out string? message));
        Assert.Equal("7 numaralı sipariş bulunamadı.", message);
        Assert.False(Catalog.TryFormat("unknown.code", Turkish, args, out _));
    }

    [Fact]
    public void Localize_NullArguments_Throw()
    {
        Error error = Error.NotFound("x", "y");

        Assert.Throws<ArgumentNullException>(() => ((ILocalizationCatalog)null!).Localize(error, Turkish));
        Assert.Throws<ArgumentNullException>(() => Catalog.Localize(null!, Turkish));
        Assert.Throws<ArgumentNullException>(() => Catalog.Localize(error, null!));
    }
}
