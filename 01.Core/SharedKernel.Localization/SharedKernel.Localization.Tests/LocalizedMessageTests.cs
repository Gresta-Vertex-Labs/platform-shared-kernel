using System.Globalization;
using SharedKernel.Primitives.Errors;
using Xunit;

namespace SharedKernel.Localization.Tests;

public sealed class LocalizedMessageTests
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    private static readonly LocalizedMessage CannotCancel =
        LocalizedMessage.Define("order.cannot_cancel", "A shipped order cannot be cancelled.");

    private static readonly LocalizedMessage<Guid> NotFound =
        LocalizedMessage.Define<Guid>("order.not_found", "Order {orderId} was not found.", "orderId");

    private static readonly LocalizedMessage<decimal, string> OverLimit = LocalizedMessage.Define<decimal, string>(
        "payment.over_limit", "{amount:N2} exceeds the limit of account {account}.", "amount", "account");

    private static readonly LocalizedMessage<int, int, int> Range = LocalizedMessage.Define<int, int, int>(
        "value.out_of_range", "{value} is outside {min}-{max}.", "value", "min", "max");

    private static readonly LocalizedMessage<string, int, int, string> Four = LocalizedMessage.Define<string, int, int, string>(
        "four", "{a} {b} {c} {d}", "a", "b", "c", "d");

    private static readonly Guid OrderId = Guid.Parse("3f2a0000-0000-0000-0000-000000000001");

    [Fact]
    public void ToError_SetsCodeTypeAndInvariantDefaultMessage_AndCarriesArguments()
    {
        Error error = OverLimit.ToError(ErrorType.BusinessRule, 1500.5m, "TR-01");

        Assert.Equal("payment.over_limit", error.Code);
        Assert.Equal(ErrorType.BusinessRule, error.Type);
        Assert.Equal("1,500.50 exceeds the limit of account TR-01.", error.Message);
        Assert.Equal(1500.5m, error.MessageArguments["amount"]);
        Assert.Equal("TR-01", error.MessageArguments["account"]);
    }

    [Fact]
    public void ToError_WithoutArguments_HasEmptyMessageArguments()
    {
        Error error = CannotCancel.ToError(ErrorType.BusinessRule);

        Assert.Equal("A shipped order cannot be cancelled.", error.Message);
        Assert.Empty(error.MessageArguments);
    }

    [Fact]
    public void ToError_EveryArity_FillsItsDefaultText()
    {
        Assert.Equal($"Order {OrderId} was not found.", NotFound.ToError(ErrorType.NotFound, OrderId).Message);
        Assert.Equal("12 is outside 1-10.", Range.ToError(ErrorType.Validation, 12, 1, 10).Message);
        Assert.Equal("x 1 2 y", Four.ToError(ErrorType.Validation, "x", 1, 2, "y").Message);
    }

    [Theory]
    [InlineData(ErrorType.None)]
    [InlineData((ErrorType)999)]
    public void ToError_NoneOrUndefinedType_Throws(ErrorType type)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NotFound.ToError(type, OrderId));
    }

    [Fact]
    public void Format_UsesTranslationInTheGivenCulture_OtherwiseTheDefaultText()
    {
        InMemoryLocalizationCatalog catalog = new LocalizationCatalogBuilder()
            .Add(OverLimit.Code, CultureInfo.GetCultureInfo("tr"), "{account} hesabının limiti {amount:N2} ile aşıldı.")
            .Build();

        Assert.Equal("TR-01 hesabının limiti 1.500,50 ile aşıldı.", OverLimit.Format(catalog, Turkish, 1500.5m, "TR-01"));
        Assert.Equal("1.500,50 exceeds the limit of account TR-01.", OverLimit.Format(catalog, German, 1500.5m, "TR-01"));
        Assert.Equal("1.500,50 exceeds the limit of account TR-01.", OverLimit.Format(null, Turkish, 1500.5m, "TR-01"));
    }

    [Fact]
    public void Format_TranslationWithUnknownPlaceholder_FallsBackToDefaultText()
    {
        InMemoryLocalizationCatalog catalog = new LocalizationCatalogBuilder()
            .Add(NotFound.Code, Turkish, "{siparisNo} bulunamadı.")
            .Build();

        Assert.Equal($"Order {OrderId} was not found.", NotFound.Format(catalog, Turkish, OrderId));
    }

    [Fact]
    public void CodeAndDefaultTemplate_AreExposed()
    {
        Assert.Equal("order.not_found", NotFound.Code);
        Assert.Equal(["orderId"], NotFound.DefaultTemplate.PlaceholderNames);
        Assert.Equal("order.not_found", NotFound.ToString());
    }

    [Fact]
    public void Define_ArgumentNameNotInText_Throws()
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(
            () => LocalizedMessage.Define<Guid>("order.not_found", "Order {orderId} was not found.", "id"));

        Assert.Contains("[id]", ex.Message, StringComparison.Ordinal);
        Assert.Contains("[orderId]", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Define_TextWithAPlaceholderNotDeclared_Throws()
    {
        Assert.Throws<ArgumentException>(
            () => LocalizedMessage.Define<int>("x", "{a} and {b}", "a"));
        Assert.Throws<ArgumentException>(
            () => LocalizedMessage.Define("x", "Hello {name}."));
    }

    [Fact]
    public void Define_DuplicateOrBlankArgumentNames_Throw()
    {
        Assert.Throws<ArgumentException>(() => LocalizedMessage.Define<int, int>("x", "{a}", "a", "a"));
        Assert.Throws<ArgumentException>(() => LocalizedMessage.Define<int>("x", "{a}", " "));
    }

    [Fact]
    public void Define_FormatThatDoesNotSuitTheArgumentType_ThrowsWhenDefined()
    {
        ArgumentException ex = Assert.Throws<ArgumentException>(
            () => LocalizedMessage.Define<decimal>("x", "Total {amount:Q9}", "amount"));

        Assert.IsType<FormatException>(ex.InnerException);
    }

    [Fact]
    public void Define_InvalidTemplateOrBlankCode_Throws()
    {
        Assert.Throws<ArgumentException>(() => LocalizedMessage.Define<int>("x", "{0}", "a"));
        Assert.Throws<ArgumentException>(() => LocalizedMessage.Define(" ", "Text."));
        Assert.Throws<ArgumentException>(() => LocalizedMessage.Define("x", " "));
    }

    [Fact]
    public void Define_ArgumentNamesAreBoundByName_NotByPosition()
    {
        // The text names 'account' first, the call passes amount first: the explicit names keep them apart.
        LocalizedMessage<decimal, string> message = LocalizedMessage.Define<decimal, string>(
            "x", "Account {account}: {amount:N0}", "amount", "account");

        Assert.Equal("Account TR-01: 5", message.ToError(ErrorType.Validation, 5m, "TR-01").Message);
    }
}
