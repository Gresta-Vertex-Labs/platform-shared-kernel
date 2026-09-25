using System.Globalization;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using SharedKernel.Localization;
using SharedKernel.Presentation.WebApi.Tests.TestSupport;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.Integration;

/// <summary>
/// Design D1/D16 and B3: the message of a returned <see cref="Error"/> is translated into the request culture on the
/// primary path (typed results), including the values of its placeholders and each field error on its own.
/// </summary>
public sealed class LocalizationTests
{
    private const string LimitPolicy = "none-left";

    private static readonly CultureInfo Turkish = new("tr-TR");

    private static readonly LocalizedMessage<int> OrderNotFound =
        LocalizedMessage.Define<int>("order.not_found", "Order {orderId} was not found.", "orderId");

    [Fact]
    public async Task B3_ResultFailure_IsTranslatedIntoTheRequestCulture_WithItsArguments()
    {
        await using var app = await StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/order");
        request.Headers.Add(HeaderNames.AcceptLanguage, "tr-TR");

        using var response = await app.GetTestClient().SendAsync(request);

        var problem = await response.ShouldBeProblemAsync(StatusCodes.Status404NotFound, "order.not_found");
        problem.Detail().Should().Be("42 numaralı sipariş bulunamadı.");
    }

    [Fact]
    public async Task ResultFailure_WithoutATranslation_KeepsItsOwnMessage()
    {
        await using var app = await StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/order");
        request.Headers.Add(HeaderNames.AcceptLanguage, "en-US");

        using var response = await app.GetTestClient().SendAsync(request);

        var problem = await response.ShouldBeProblemAsync(StatusCodes.Status404NotFound, "order.not_found");
        problem.Detail().Should().Be("Order 42 was not found.");
    }

    [Fact]
    public async Task FieldErrors_AreTranslatedOneByOne()
    {
        await using var app = await StartAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/validation");
        request.Headers.Add(HeaderNames.AcceptLanguage, "tr-TR");

        using var response = await app.GetTestClient().SendAsync(request);

        var problem = await response.ShouldBeProblemAsync(StatusCodes.Status400BadRequest, ErrorCodes.Validation.Failed);
        var errors = problem.GetProperty(ProblemDetailsExtensionNames.Errors);
        errors.GetProperty("field.required")[0].GetString().Should().Be("Alan zorunludur.");
        errors.GetProperty("field.invalid")[0].GetString().Should().Be("Field is invalid.");
    }

    [Fact]
    public async Task WithoutRequestLocalization_TheCurrentUICultureIsUsed()
    {
        var original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("tr-TR");
            var context = new DefaultHttpContext
            {
                RequestServices = new ServiceCollection().AddSingleton(Catalog()).BuildServiceProvider(),
            };

            ErrorPresentation.GetClientMessage(OrderNotFound.ToError(ErrorType.NotFound, 42), context)
                .Should().Be("42 numaralı sipariş bulunamadı.");
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Theory]
    [InlineData("/protected", false, StatusCodes.Status401Unauthorized, "Kimlik doğrulaması gerekiyor.")]
    [InlineData("/protected", true, StatusCodes.Status403Forbidden, "Bu işlem için yetkiniz yok.")]
    [InlineData("/limited", false, StatusCodes.Status429TooManyRequests, "Çok fazla istek.")]
    public async Task R2_RequestLocalization_BeforeAuthorization_TranslatesRefusals(string path, bool signedIn, int status, string expected)
    {
        await using var app = await WebApiTestHost.StartAsync(
            app =>
            {
                app.MapGet("/protected", () => "ok").RequireEndpointPermission("orders.read");
                app.MapGet("/limited", () => "ok").RequireRateLimiting(LimitPolicy);
            },
            builder =>
            {
                builder.AddTestAuthentication();
                builder.Services.AddSingleton<ILocalizationCatalog>(new LocalizationCatalogBuilder()
                    .Add(ErrorCodes.Unauthorized.Default, Turkish, "Kimlik doğrulaması gerekiyor.")
                    .Add(ErrorCodes.Forbidden.InsufficientPermission, Turkish, "Bu işlem için yetkiniz yok.")
                    .Add(PresentationErrorCodes.RateLimitExceeded, Turkish, "Çok fazla istek.")
                    .Build());
                AddCultures(builder);
                builder.Services.AddRateLimiter(options => options.AddFixedWindowLimiter(LimitPolicy, limiter =>
                {
                    limiter.PermitLimit = 1;
                    limiter.Window = TimeSpan.FromMinutes(10);
                    limiter.QueueLimit = 0;
                }));
            },
            configurePipeline: pipeline => pipeline.BeforeAuthorization(app => app.UseRequestLocalization()));
        using var permitUsed = await app.GetTestClient().GetAsync("/limited");
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add(HeaderNames.AcceptLanguage, "tr-TR");
        if (signedIn)
        {
            request.SignedIn(permissions: "orders.write");
        }

        using var response = await app.GetTestClient().SendAsync(request);

        response.StatusCode.Should().Be((System.Net.HttpStatusCode)status);
        (await response.ShouldBeProblemAsync(status, CodeFor(status))).Detail().Should().Be(expected);
    }

    private static string CodeFor(int status) => status switch
    {
        StatusCodes.Status401Unauthorized => ErrorCodes.Unauthorized.Default,
        StatusCodes.Status403Forbidden => ErrorCodes.Forbidden.InsufficientPermission,
        _ => PresentationErrorCodes.RateLimitExceeded,
    };

    private static void AddCultures(WebApplicationBuilder builder) =>
        builder.Services.AddRequestLocalization(options =>
        {
            CultureInfo[] cultures = [new("en-US"), Turkish];
            options.SupportedCultures = cultures;
            options.SupportedUICultures = cultures;
            options.SetDefaultCulture("en-US");
        });

    private static Task<WebApplication> StartAsync() =>
        WebApiTestHost.StartAsync(
            app =>
            {
                app.UseRequestLocalization();
                app.MapGet("/order", () => Result<string>.Failure(OrderNotFound.ToError(ErrorType.NotFound, 42)).ToOk());
                app.MapGet("/validation", () => Result.Failure(Error.Validation(
                [
                    Error.Validation("field.required", "Field is required."),
                    Error.Validation("field.invalid", "Field is invalid."),
                ])).ToNoContent());
            },
            builder =>
            {
                builder.Services.AddSingleton(Catalog());
                builder.Services.AddRequestLocalization(options =>
                {
                    CultureInfo[] cultures = [new("en-US"), new("tr-TR")];
                    options.SupportedCultures = cultures;
                    options.SupportedUICultures = cultures;
                    options.SetDefaultCulture("en-US");
                });
            });

    private static ILocalizationCatalog Catalog() =>
        new LocalizationCatalogBuilder()
            .Add("order.not_found", new CultureInfo("tr-TR"), "{orderId} numaralı sipariş bulunamadı.")
            .Add("field.required", new CultureInfo("tr-TR"), "Alan zorunludur.")
            .Build();
}
