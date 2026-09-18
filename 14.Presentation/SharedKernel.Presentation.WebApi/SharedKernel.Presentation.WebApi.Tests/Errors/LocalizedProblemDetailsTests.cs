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

    private static Error FieldError(string code, string message, string path, string displayName)
        => Error.Validation(code, message) with
        {
            MessageArguments = new Dictionary<string, object?>
            {
                [ErrorArgumentNames.PropertyPath] = path,
                [ErrorArgumentNames.PropertyName] = displayName,
            },
        };

    [Fact]
    public void ToProblemDetails_ErrorsWithPropertyPath_AreKeyedByField_WithCodesAlignedIndexByIndex()
    {
        var aggregateError = Error.Validation(
        [
            FieldError("validation.iban.invalid_check_digits", "IBAN check digits are not correct.", "Accounts[0].Iban", "IBAN"),
            FieldError("validation.max_length", "IBAN must not exceed 34 characters.", "Accounts[0].Iban", "IBAN"),
            FieldError("validation.required", "Name is required.", "Name", "Name"),
        ]);

        var problemDetails = aggregateError.ToProblemDetails();

        var errors = (Dictionary<string, string[]>)problemDetails.Extensions["errors"]!;
        var errorCodes = (Dictionary<string, string[]>)problemDetails.Extensions["errorCodes"]!;

        errors.Keys.Should().Equal("Accounts[0].Iban", "Name");
        errorCodes.Keys.Should().Equal(errors.Keys);
        errors["Accounts[0].Iban"].Should().Equal("IBAN check digits are not correct.", "IBAN must not exceed 34 characters.");
        errorCodes["Accounts[0].Iban"].Should().Equal("validation.iban.invalid_check_digits", "validation.max_length");
        errors["Name"].Should().Equal("Name is required.");
        errorCodes["Name"].Should().Equal("validation.required");
    }

    [Fact]
    public void ToProblemDetails_ErrorsWithoutAUsablePropertyPath_AreKeyedByCode()
    {
        var emptyPath = Error.Validation("order.total_invalid", "Order total is invalid.") with
        {
            MessageArguments = new Dictionary<string, object?> { [ErrorArgumentNames.PropertyPath] = "" },
        };
        var nonStringPath = Error.Validation("order.lines_invalid", "Order lines are invalid.") with
        {
            MessageArguments = new Dictionary<string, object?> { [ErrorArgumentNames.PropertyPath] = 42 },
        };
        var exception = new ValidationException(
        [
            Error.Validation("order.limit_exceeded", "Order exceeds the limit."),
            emptyPath,
            nonStringPath,
        ]);

        var problemDetails = exception.ToProblemDetails();

        var errors = (Dictionary<string, string[]>)problemDetails.Extensions["errors"]!;
        var errorCodes = (Dictionary<string, string[]>)problemDetails.Extensions["errorCodes"]!;

        errors.Keys.Should().Equal("order.limit_exceeded", "order.total_invalid", "order.lines_invalid");
        errorCodes["order.limit_exceeded"].Should().Equal("order.limit_exceeded");
        errorCodes["order.total_invalid"].Should().Equal("order.total_invalid");
        errorCodes["order.lines_invalid"].Should().Equal("order.lines_invalid");
    }

    [Fact]
    public void ToProblemDetails_FieldKeyedErrors_AreTranslatedByTheirOwnCodeAndArguments()
    {
        var aggregateError = Error.Validation(
        [
            FieldError("validation.iban.invalid_check_digits", "IBAN check digits are not correct.", "Accounts[0].Iban", "IBAN"),
            FieldError("validation.max_length", "IBAN must not exceed 34 characters.", "Accounts[0].Iban", "IBAN"),
        ]);
        var catalog = new LocalizationCatalogBuilder()
            .Add("validation.iban.invalid_check_digits", new CultureInfo("tr"), "{PropertyName} kontrol basamakları hatalı.")
            .Build();
        var originalCulture = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("tr-TR");

            var problemDetails = aggregateError.ToProblemDetails(BuildContext(catalog));

            var errors = (Dictionary<string, string[]>)problemDetails.Extensions["errors"]!;
            var errorCodes = (Dictionary<string, string[]>)problemDetails.Extensions["errorCodes"]!;

            errors["Accounts[0].Iban"].Should().Equal(
                "IBAN kontrol basamakları hatalı.",
                "IBAN must not exceed 34 characters.");
            errorCodes["Accounts[0].Iban"].Should().Equal("validation.iban.invalid_check_digits", "validation.max_length");
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }

    [Fact]
    public void ToProblemDetails_ResultAndExceptionPaths_ProduceTheSameFieldKeyedMembers()
    {
        IReadOnlyList<Error> fieldErrors =
        [
            FieldError("validation.required", "Name is required.", "Name", "Name"),
            Error.Validation("order.limit_exceeded", "Order exceeds the limit."),
        ];

        var fromResult = Error.Validation(fieldErrors).ToProblemDetails();
        var fromException = new ValidationException(fieldErrors).ToProblemDetails();

        fromException.Extensions["errors"].Should().BeEquivalentTo(fromResult.Extensions["errors"]);
        fromException.Extensions["errorCodes"].Should().BeEquivalentTo(fromResult.Extensions["errorCodes"]);
    }

    [Fact]
    public void ToProblemDetails_SingleErrorWithoutDetails_HasNoErrorCodesMember()
    {
        var problemDetails = Error.NotFound("order.not_found", "Order could not be found.").ToProblemDetails();

        problemDetails.Extensions.Should().NotContainKey("errorCodes");
    }
}
