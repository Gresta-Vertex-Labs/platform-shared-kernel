using System.Globalization;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using SharedKernel.Localization;
using SharedKernel.Presentation.WebApi.Tests.TestSupport;
using SharedKernel.Primitives.Errors;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.Errors;

/// <summary>Design D1: the one place status and client text are decided, shared by HTTP, SignalR and gRPC.</summary>
public sealed class ErrorPresentationTests
{
    [Theory]
    [InlineData(ErrorType.Unexpected, true)]
    [InlineData(ErrorType.Unavailable, true)]
    [InlineData(ErrorType.Timeout, true)]
    [InlineData(ErrorType.None, true)]
    [InlineData(ErrorType.Validation, false)]
    [InlineData(ErrorType.Unauthorized, false)]
    [InlineData(ErrorType.Forbidden, false)]
    [InlineData(ErrorType.NotFound, false)]
    [InlineData(ErrorType.Conflict, false)]
    [InlineData(ErrorType.BusinessRule, false)]
    public void IsServerError_IsTrueForServerCategories(ErrorType type, bool expected)
    {
        ErrorPresentation.IsServerError(type).Should().Be(expected);
    }

    [Fact]
    public void GetStatusCode_UsesTheMap_WithoutARequest()
    {
        ErrorPresentation.GetStatusCode(TestErrors.VersionConflict, httpContext: null).Should().Be(StatusCodes.Status409Conflict);
    }

    [Theory]
    [InlineData("persistence.concurrency_conflict", "If-Match", StatusCodes.Status412PreconditionFailed)]
    [InlineData("storage.precondition_failed", "If-Match", StatusCodes.Status412PreconditionFailed)]
    [InlineData("storage.already_exists", "If-None-Match", StatusCodes.Status412PreconditionFailed)]
    [InlineData("persistence.concurrency_conflict", null, StatusCodes.Status409Conflict)]
    [InlineData("order.duplicate_number", "If-Match", StatusCodes.Status409Conflict)]
    public void R7_GetStatusCode_Is412_ForAVersionConflictOfAConditionalRequest_Only(string code, string? header, int expected)
    {
        var context = new DefaultHttpContext();
        if (header is not null)
        {
            context.Request.Headers[header] = header == "If-None-Match" ? "*" : "\"1\"";
        }

        ErrorPresentation.GetStatusCode(Error.Conflict(code, "Conflict."), context).Should().Be(expected);
    }

    [Fact]
    public void R7_GetStatusCode_OnlyTurnsConflictsInto412()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.IfMatch = "\"1\"";

        ErrorPresentation.GetStatusCode(Error.NotFound("persistence.concurrency_conflict", "Gone."), context)
            .Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public void R7_GetStatusCode_ReadsTheConfiguredCodes()
    {
        var options = new SharedKernelWebApiOptions();
        options.Problems.PreconditionFailedErrorCodes.Clear();
        options.Problems.PreconditionFailedErrorCodes.Add("orders.stale");
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddSingleton(Microsoft.Extensions.Options.Options.Create(options)).BuildServiceProvider(),
        };
        context.Request.Headers.IfMatch = "\"1\"";

        ErrorPresentation.GetStatusCode(Error.Conflict("orders.stale", "Stale."), context).Should().Be(StatusCodes.Status412PreconditionFailed);
        ErrorPresentation.GetStatusCode(TestErrors.StaleVersion, context).Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public void GetStatusCode_WithoutARequest_NeverAnswers412()
    {
        ErrorPresentation.GetStatusCode(TestErrors.StaleVersion, httpContext: null).Should().Be(StatusCodes.Status409Conflict);
    }

    [Theory]
    [InlineData(ErrorType.Unexpected, "An unexpected error occurred.")]
    [InlineData(ErrorType.Unavailable, "The service is temporarily unavailable. Try again later.")]
    [InlineData(ErrorType.Timeout, "The operation did not complete in time.")]
    public void GetClientMessage_HidesServerErrors_OutsideDevelopment(ErrorType type, string expected)
    {
        var error = new Error("search.unreachable", "Engine at http://search.internal:7700 did not answer.", type);

        ErrorPresentation.GetClientMessage(error, Context(Environments.Production)).Should().Be(expected);
        ErrorPresentation.GetClientMessage(error, httpContext: null).Should().Be(expected);
    }

    [Fact]
    public void GetClientMessage_ShowsServerErrors_InDevelopment()
    {
        ErrorPresentation.GetClientMessage(TestErrors.SearchUnreachable, Context(Environments.Development))
            .Should().Be(TestErrors.SearchUnreachable.Message);
    }

    [Fact]
    public void GetClientMessage_ShowsClientErrors_Everywhere()
    {
        ErrorPresentation.GetClientMessage(TestErrors.OrderNotFound, Context(Environments.Production))
            .Should().Be(TestErrors.OrderNotFound.Message);
        ErrorPresentation.GetClientMessage(TestErrors.OrderNotFound, httpContext: null)
            .Should().Be(TestErrors.OrderNotFound.Message);
    }

    [Fact]
    public void GetClientMessage_PrefersTheRequestCulture_OverTheCurrentUICulture()
    {
        var catalog = new LocalizationCatalogBuilder()
            .Add("order.not_found", new CultureInfo("tr-TR"), "Sipariş bulunamadı.")
            .Add("order.not_found", new CultureInfo("de-DE"), "Bestellung nicht gefunden.")
            .Build();
        var context = Context(Environments.Production, catalog);
        context.Features.Set<IRequestCultureFeature>(
            new RequestCultureFeature(new RequestCulture(new CultureInfo("de-DE")), provider: null));
        var original = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("tr-TR");

            ErrorPresentation.GetClientMessage(TestErrors.OrderNotFound, context).Should().Be("Bestellung nicht gefunden.");
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Fact]
    public void GetClientMessage_TranslatesTheGenericServerSentence()
    {
        var catalog = new LocalizationCatalogBuilder()
            .Add(ErrorCodes.Unavailable.Default, new CultureInfo("tr-TR"), "Hizmet geçici olarak kullanılamıyor.")
            .Build();
        var context = Context(Environments.Production, catalog);
        context.Features.Set<IRequestCultureFeature>(
            new RequestCultureFeature(new RequestCulture(new CultureInfo("tr-TR")), provider: null));

        ErrorPresentation.GetClientMessage(TestErrors.SearchUnreachable, context).Should().Be("Hizmet geçici olarak kullanılamıyor.");
    }

    private static DefaultHttpContext Context(string environment, ILocalizationCatalog? catalog = null)
    {
        var services = new ServiceCollection().AddSingleton<IHostEnvironment>(new TestEnvironment(environment));
        if (catalog is not null)
        {
            services.AddSingleton(catalog);
        }

        return new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
    }

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
