using System.Globalization;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Core.Exceptions;
using SharedKernel.Localization;
using SharedKernel.Presentation.WebApi.Errors;
using SharedKernel.Primitives.Errors;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.Errors;

/// <summary>
/// Tests for the optional <see cref="ILocalizationCatalog"/> localization step on
/// <see cref="ErrorProblemDetailsExtensions.ToProblemDetails(Error, HttpContext?)"/> and
/// <see cref="ValidationProblemDetailsExtensions.ToProblemDetails(ValidationException, HttpContext?)"/>
/// (P-484/WO-078).
/// </summary>
public class LocalizedProblemDetailsTests
{
    private static HttpContext BuildContext(ILocalizationCatalog? catalog)
    {
        var services = new ServiceCollection();
        if (catalog is not null)
        {
            services.AddSingleton(catalog);
        }

        return new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
    }

    [Fact]
    public void ToProblemDetails_NoCatalogRegistered_ProducesByteIdenticalDetailToPreLocalizationBehavior()
    {
        var error = Error.NotFound("order.not_found", "Order could not be found.");
        var context = BuildContext(catalog: null);

        var withEmptyContainer = error.ToProblemDetails(context);
        var withNoContextAtAll = error.ToProblemDetails();

        withEmptyContainer.Detail.Should().Be(error.Message);
        withNoContextAtAll.Detail.Should().Be(error.Message);
        withEmptyContainer.Detail.Should().Be(withNoContextAtAll.Detail);
    }

    [Fact]
    public void ToProblemDetails_CatalogRegisteredButNoTranslation_FallsBackToErrorMessage()
    {
        var error = Error.NotFound("order.not_found", "Order could not be found.");
        var catalog = new LocalizationCatalogBuilder().Build();
        var context = BuildContext(catalog);

        var problemDetails = error.ToProblemDetails(context);

        problemDetails.Detail.Should().Be(error.Message);
        problemDetails.Detail.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ToProblemDetails_CatalogHasTranslation_UsesTranslatedStringAsDetail()
    {
        var error = Error.NotFound("order.not_found", "Order could not be found.");
        var originalCulture = CultureInfo.CurrentUICulture;

        try
        {
            var catalog = new LocalizationCatalogBuilder()
                .Add(error.Code, new CultureInfo("tr-TR"), "Sipariş bulunamadı.")
                .Add(error.Code, new CultureInfo("de-DE"), "Bestellung nicht gefunden.")
                .Build();
            var context = BuildContext(catalog);

            CultureInfo.CurrentUICulture = new CultureInfo("tr-TR");
            var trProblemDetails = error.ToProblemDetails(context);

            CultureInfo.CurrentUICulture = new CultureInfo("de-DE");
            var deProblemDetails = error.ToProblemDetails(context);

            trProblemDetails.Detail.Should().Be("Sipariş bulunamadı.");
            deProblemDetails.Detail.Should().Be("Bestellung nicht gefunden.");
            trProblemDetails.Detail.Should().NotBe(deProblemDetails.Detail);
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }

    [Fact]
    public void ToProblemDetails_Localization_NeverChangesTitleStatusTypeOrExtensions()
    {
        var error = Error.Forbidden("order.forbidden", "Not permitted.");
        var catalog = new LocalizationCatalogBuilder()
            .Add(error.Code, CultureInfo.InvariantCulture, "Translated.")
            .Build();
        var context = BuildContext(catalog);

        var localized = error.ToProblemDetails(context);
        var unlocalized = error.ToProblemDetails();

        localized.Title.Should().Be(unlocalized.Title);
        localized.Status.Should().Be(unlocalized.Status);
        localized.Type.Should().Be(unlocalized.Type);
        localized.Extensions["errorCode"].Should().Be(unlocalized.Extensions["errorCode"]);
    }

    [Fact]
    public void ValidationToProblemDetails_AppliesLocalizationIndependentlyPerField()
    {
        var translatedError = Error.Validation("field.required", "Field is required.");
        var untranslatedError = Error.Validation("field.invalid", "Field is invalid.");
        var exception = new ValidationException([translatedError, untranslatedError]);

        var originalCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("tr-TR");
            var catalog = new LocalizationCatalogBuilder()
                .Add(translatedError.Code, new CultureInfo("tr-TR"), "Alan zorunludur.")
                .Build();
            var context = BuildContext(catalog);

            var problemDetails = exception.ToProblemDetails(context);

            var errors = (Dictionary<string, string[]>)problemDetails.Extensions["errors"]!;

            errors[translatedError.Code].Should().ContainSingle().Which.Should().Be("Alan zorunludur.");
            errors[untranslatedError.Code].Should().ContainSingle().Which.Should().Be(untranslatedError.Message);
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }

    [Fact]
    public void ValidationToProblemDetails_NoCatalogRegistered_FallsBackToRawMessagesForEveryField()
    {
        var errorOne = Error.Validation("field.one", "Field one is invalid.");
        var errorTwo = Error.Validation("field.two", "Field two is invalid.");
        var exception = new ValidationException([errorOne, errorTwo]);
        var context = BuildContext(catalog: null);

        var problemDetails = exception.ToProblemDetails(context);

        var errors = (Dictionary<string, string[]>)problemDetails.Extensions["errors"]!;

        errors[errorOne.Code].Should().Equal(errorOne.Message);
        errors[errorTwo.Code].Should().Equal(errorTwo.Message);
    }

    [Fact]
    public void ToProblemDetails_AggregateValidationError_AppliesLocalizationIndependentlyPerDetail()
    {
        // The Result path (Error.Validation(errors), Details non-empty) must localize each detail
        // independently, exactly like the ValidationException path already does (P-544).
        var translatedError = Error.Validation("field.required", "Field is required.");
        var untranslatedError = Error.Validation("field.invalid", "Field is invalid.");
        var aggregateError = Error.Validation([translatedError, untranslatedError]);

        var originalCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("tr-TR");
            var catalog = new LocalizationCatalogBuilder()
                .Add(translatedError.Code, new CultureInfo("tr-TR"), "Alan zorunludur.")
                .Build();
            var context = BuildContext(catalog);

            var problemDetails = aggregateError.ToProblemDetails(context);

            var errors = (Dictionary<string, string[]>)problemDetails.Extensions["errors"]!;

            errors[translatedError.Code].Should().ContainSingle().Which.Should().Be("Alan zorunludur.");
            errors[untranslatedError.Code].Should().ContainSingle().Which.Should().Be(untranslatedError.Message);
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }
    private static readonly LocalizedMessage<int, decimal> OrderOverLimit = LocalizedMessage.Define<int, decimal>(
        "order.over_limit", "Order {orderNumber} exceeds the limit by {amount:N2}.", "orderNumber", "amount");

    private static readonly LocalizedMessage<string> FieldRequired = LocalizedMessage.Define<string>(
        "field.required", "{field} is required.", "field");

    [Fact]
    public void ToProblemDetails_TranslationWithPlaceholders_IsFilledFromMessageArguments_InTheCallersCulture()
    {
        var error = OrderOverLimit.ToError(ErrorType.BusinessRule, 1234, 1500.5m);
        var catalog = new LocalizationCatalogBuilder()
            .Add(error.Code, new CultureInfo("tr"), "{orderNumber} numaralı sipariş limiti {amount:N2} aşıyor.")
            .Build();
        var context = BuildContext(catalog);
        var originalCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("tr-TR");
            var turkish = error.ToProblemDetails(context);

            CultureInfo.CurrentUICulture = new CultureInfo("fr-FR");
            var untranslated = error.ToProblemDetails(context);

            turkish.Detail.Should().Be("1234 numaralı sipariş limiti 1.500,50 aşıyor.");
            untranslated.Detail.Should().Be("Order 1234 exceeds the limit by 1,500.50.");
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }

    [Fact]
    public void ToProblemDetails_TranslationNeedsAValueTheErrorDoesNotCarry_FallsBackToMessage_NeverShowsRawPlaceholder()
    {
        var error = Error.NotFound("order.not_found", "Order could not be found.");
        var catalog = new LocalizationCatalogBuilder()
            .Add(error.Code, CultureInfo.InvariantCulture, "Order {orderNumber} could not be found.")
            .Build();

        var problemDetails = error.ToProblemDetails(BuildContext(catalog));

        problemDetails.Detail.Should().Be(error.Message);
    }

    [Fact]
    public void ToProblemDetails_AggregateValidationError_FillsEachDetailWithItsOwnArguments()
    {
        var aggregateError = Error.Validation(
        [
            FieldRequired.ToError(ErrorType.Validation, "Name"),
            FieldRequired.ToError(ErrorType.Validation, "Email"),
        ]);
        var catalog = new LocalizationCatalogBuilder()
            .Add(FieldRequired.Code, new CultureInfo("tr"), "{field} alanı zorunludur.")
            .Build();
        var originalCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("tr-TR");

            var problemDetails = aggregateError.ToProblemDetails(BuildContext(catalog));

            var errors = (Dictionary<string, string[]>)problemDetails.Extensions["errors"]!;
            errors[FieldRequired.Code].Should().Equal("Name alanı zorunludur.", "Email alanı zorunludur.");
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }
}
