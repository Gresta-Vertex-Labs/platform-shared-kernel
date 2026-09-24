using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Core.Exceptions;
using SharedKernel.Presentation.WebApi.Tests.TestSupport;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.Integration;

/// <summary>
/// Design D1/D16: server-error messages are hidden outside Development and shown in Development (B6), and the
/// <c>exception</c> member appears only in Development or when explicitly enabled (B5), on every path.
/// </summary>
public sealed class RedactionTests
{
    private const string ExceptionMessage = "Connection string Server=db;Password=secret is wrong.";

    [Fact]
    public async Task B6_ServerErrorMessage_OnTheResultPath_IsHiddenOutsideDevelopment()
    {
        await using var app = await StartAsync(WebApiTestHost.Production);

        using var response = await app.GetTestClient().GetAsync("/unreachable");

        var problem = await response.ShouldBeProblemAsync(StatusCodes.Status503ServiceUnavailable, "search.unreachable");
        problem.Detail().Should().Be("The service is temporarily unavailable. Try again later.");
        problem.Detail().Should().NotContain("search.internal");
    }

    [Fact]
    public async Task ServerErrorMessage_OfAThrownException_IsHiddenOutsideDevelopment()
    {
        await using var app = await StartAsync(WebApiTestHost.Production);

        using var response = await app.GetTestClient().GetAsync("/unreachable-thrown");

        var problem = await response.ShouldBeProblemAsync(StatusCodes.Status503ServiceUnavailable, "search.unreachable");
        problem.Detail().Should().NotContain("search.internal");
    }

    [Fact]
    public async Task ServerErrorMessage_IsShownInDevelopment()
    {
        await using var app = await StartAsync(WebApiTestHost.Development);

        using var response = await app.GetTestClient().GetAsync("/unreachable");

        var problem = await response.ShouldBeProblemAsync(StatusCodes.Status503ServiceUnavailable, "search.unreachable");
        problem.Detail().Should().Be(TestErrors.SearchUnreachable.Message);
    }

    [Fact]
    public async Task ClientErrorMessage_IsNeverHidden()
    {
        await using var app = await StartAsync(WebApiTestHost.Production);

        using var response = await app.GetTestClient().GetAsync("/not-found");

        var problem = await response.ShouldBeProblemAsync(StatusCodes.Status404NotFound, "order.not_found");
        problem.Detail().Should().Be(TestErrors.OrderNotFound.Message);
    }

    [Fact]
    public async Task UnknownException_OutsideDevelopment_HasAGenericDetail_AndNoExceptionMember()
    {
        await using var app = await StartAsync(WebApiTestHost.Production);

        using var response = await app.GetTestClient().GetAsync("/boom");

        var problem = await response.ShouldBeProblemAsync(StatusCodes.Status500InternalServerError, ErrorCodes.Unexpected.Default);
        problem.Detail().Should().Be("An unexpected error occurred.");
        problem.TryGetProperty(ProblemDetailsExtensionNames.Exception, out _).Should().BeFalse();
        (await response.Content.ReadAsStringAsync()).Should().NotContain("secret");
    }

    [Fact]
    public async Task B5_UnknownException_InDevelopment_CarriesTheExceptionMember()
    {
        await using var app = await StartAsync(WebApiTestHost.Development);

        using var response = await app.GetTestClient().GetAsync("/boom");

        var problem = await response.ShouldBeProblemAsync(StatusCodes.Status500InternalServerError, ErrorCodes.Unexpected.Default);
        var exception = problem.GetProperty(ProblemDetailsExtensionNames.Exception);
        exception.GetProperty("type").GetString().Should().Be(typeof(InvalidOperationException).FullName);
        exception.GetProperty("message").GetString().Should().Be(ExceptionMessage);
        exception.GetProperty("stackTrace").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task IncludeExceptionDetails_OverridesTheEnvironment()
    {
        await using var shown = await StartAsync(WebApiTestHost.Production, options => options.Problems.IncludeExceptionDetails = true);
        await using var hidden = await StartAsync(WebApiTestHost.Development, options => options.Problems.IncludeExceptionDetails = false);

        using var shownResponse = await shown.GetTestClient().GetAsync("/boom");
        using var hiddenResponse = await hidden.GetTestClient().GetAsync("/boom");

        var shownProblem = await shownResponse.ShouldBeProblemAsync(StatusCodes.Status500InternalServerError, ErrorCodes.Unexpected.Default);
        var hiddenProblem = await hiddenResponse.ShouldBeProblemAsync(StatusCodes.Status500InternalServerError, ErrorCodes.Unexpected.Default);
        shownProblem.TryGetProperty(ProblemDetailsExtensionNames.Exception, out _).Should().BeTrue();
        hiddenProblem.TryGetProperty(ProblemDetailsExtensionNames.Exception, out _).Should().BeFalse();
    }

    [Fact]
    public async Task B4_ExceptionResponse_GoesThroughTheProblemDetailsService()
    {
        // A service's own CustomizeProblemDetails only runs when the response is written by IProblemDetailsService.
        await using var app = await StartAsync(
            WebApiTestHost.Production,
            configureBuilder: builder => builder.Services.AddProblemDetails(options =>
                options.CustomizeProblemDetails = context => context.ProblemDetails.Extensions["service"] = "orders"));

        using var response = await app.GetTestClient().GetAsync("/boom");

        var problem = await response.ShouldBeProblemAsync(StatusCodes.Status500InternalServerError, ErrorCodes.Unexpected.Default);
        problem.GetProperty("service").GetString().Should().Be("orders");
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
    }

    private static Task<WebApplication> StartAsync(
        string environment,
        Action<SharedKernelWebApiOptions>? configureOptions = null,
        Action<WebApplicationBuilder>? configureBuilder = null) =>
        WebApiTestHost.StartAsync(
            app =>
            {
                app.MapGet("/unreachable", () => Result<string>.Failure(TestErrors.SearchUnreachable).ToOk());
                app.MapGet("/unreachable-thrown", IResult () => throw TestErrors.SearchUnreachable.ToException());
                app.MapGet("/not-found", () => Result<string>.Failure(TestErrors.OrderNotFound).ToOk());
                app.MapGet("/boom", IResult () => throw new InvalidOperationException(ExceptionMessage));
            },
            configureBuilder,
            configureOptions,
            environment);
}
