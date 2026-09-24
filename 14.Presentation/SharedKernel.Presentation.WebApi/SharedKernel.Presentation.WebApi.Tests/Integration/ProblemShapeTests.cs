using System.Net.Http.Headers;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using SharedKernel.Core.Exceptions;
using SharedKernel.Presentation.WebApi.Errors;
using SharedKernel.Presentation.WebApi.Tests.TestSupport;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Propagation;
using SharedKernel.Primitives.Results;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.Integration;

/// <summary>
/// Design D1/D16: every error source produces the same <c>application/problem+json</c> shape — a returned
/// <see cref="Error"/>, a thrown exception, the framework's own statuses, authorization, required headers and
/// rate limiting (413 is covered by <see cref="KestrelLimitsTests"/> on a real Kestrel listener).
/// </summary>
public sealed class ProblemShapeTests : IClassFixture<FullStackHost>
{
    private readonly FullStackHost _host;

    public ProblemShapeTests(FullStackHost host)
    {
        _host = host;
    }

    [Theory]
    [InlineData("GET", "/result-failure", 404, "order.not_found")]
    [InlineData("GET", "/throw-known", 404, "order.not_found")]
    [InlineData("GET", "/throw-unknown", 500, ErrorCodes.Unexpected.Default)]
    [InlineData("GET", "/does-not-exist", 404, "http.404")]
    [InlineData("POST", "/only-get", 405, "http.405")]
    [InlineData("GET", "/auth/perm", 401, ErrorCodes.Unauthorized.Default)]
    [InlineData("PUT", "/versioned", 428, PresentationErrorCodes.PreconditionRequired)]
    [InlineData("POST", "/idempotent", 400, PresentationErrorCodes.IdempotencyKeyRequired)]
    [InlineData("GET", "/mvc-api/failure", 404, "order.not_found")]
    [InlineData("DELETE", "/mvc-api/failure", 404, "order.not_found")]
    [InlineData("GET", "/mvc-api/throw", 404, "order.not_found")]
    [InlineData("GET", "/mvc-api/not-found", 404, "http.404")]
    [InlineData("GET", "/mvc-plain/failure", 404, "order.not_found")]
    public async Task EveryErrorSource_WritesTheSameProblemShape(string method, string path, int status, string errorCode)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);

        using var response = await _host.Client.SendAsync(request);

        await response.ShouldBeProblemAsync(status, errorCode);
    }

    [Fact]
    public async Task UnsupportedMediaType_WritesProblem()
    {
        using var content = new StringContent("name", Encoding.UTF8, "text/plain");

        using var response = await _host.Client.PostAsync("/json", content);

        await response.ShouldBeProblemAsync(StatusCodes.Status415UnsupportedMediaType, "http.415");
    }

    [Fact]
    public async Task UnsupportedMediaType_InDevelopment_WhereTheFrameworkThrows_WritesTheSameProblem()
    {
        // Outside Development a minimal API sets 415; in Development it throws BadHttpRequestException instead.
        await using var app = await WebApiTestHost.StartAsync(
            app => app.MapPost("/json", ([Microsoft.AspNetCore.Mvc.FromBody] FullStackHost.Payload payload) => payload.Name),
            environment: WebApiTestHost.Development);
        using var content = new StringContent("name", Encoding.UTF8, "text/plain");

        using var response = await app.GetTestClient().PostAsync("/json", content);

        await response.ShouldBeProblemAsync(StatusCodes.Status415UnsupportedMediaType, "http.415");
    }

    [Fact]
    public async Task Forbidden_WritesProblem()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/auth/perm").SignedIn(permissions: "orders.write");

        using var response = await _host.Client.SendAsync(request);

        await response.ShouldBeProblemAsync(StatusCodes.Status403Forbidden, ErrorCodes.Forbidden.InsufficientPermission);
    }

    [Fact]
    public async Task StepUp_WritesProblem()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/auth/fresh")
            .SignedIn(authTime: FullStackHost.Now.AddHours(-1));

        using var response = await _host.Client.SendAsync(request);

        await response.ShouldBeProblemAsync(StatusCodes.Status401Unauthorized, PresentationErrorCodes.StepUpRequired);
    }

    [Fact]
    public async Task StaleVersion_WritesPreconditionFailedProblem()
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, "/versioned");
        request.Headers.IfMatch.Add(new EntityTagHeaderValue("\"2\""));

        using var response = await _host.Client.SendAsync(request);

        await response.ShouldBeProblemAsync(StatusCodes.Status412PreconditionFailed, TestErrors.ConcurrencyConflictCode);
    }

    [Fact]
    public async Task R9_MvcModelState_IsThePlatformsValidationShape_KeepingAttributeMessages()
    {
        using var response = await _host.Client.PostAsync("/mvc-api/customers", Json("{\"age\":5}"));

        var problem = await response.ShouldBeProblemAsync(StatusCodes.Status400BadRequest, ErrorCodes.Validation.Failed);
        var errors = problem.GetProperty(ProblemDetailsExtensionNames.Errors);
        var codes = problem.GetProperty(ProblemDetailsExtensionNames.ErrorCodes);
        errors.GetProperty("Name")[0].GetString().Should().Be("The Name field is required.");
        codes.GetProperty("Name")[0].GetString().Should().Be(PresentationErrorCodes.InvalidValue);
        errors.GetProperty("Age")[0].GetString().Should().Contain("18");
        codes.GetProperty("Age")[0].GetString().Should().Be(PresentationErrorCodes.InvalidValue);
    }

    [Fact]
    public async Task R9_MvcUnreadableJson_NamesTheField_WithoutDotNetTypeNames()
    {
        using var response = await _host.Client.PostAsync("/mvc-api/customers", Json("{\"name\":\"Ada\",\"age\":\"old\"}"));

        var problem = await response.ShouldBeProblemAsync(StatusCodes.Status400BadRequest, ErrorCodes.Validation.Failed);
        problem.GetProperty(ProblemDetailsExtensionNames.Errors).GetProperty("age")[0].GetString().Should().Be("The value is not valid.");
        problem.GetProperty(ProblemDetailsExtensionNames.ErrorCodes).GetProperty("age")[0].GetString()
            .Should().Be(ErrorCodes.Validation.InvalidFormat);
        problem.GetRawText().Should().NotContain("System.").And.NotContain("Int32").And.NotContain("LineNumber");
    }

    [Theory]
    [InlineData(WebApiTestHost.Production)]
    [InlineData(WebApiTestHost.Development)]
    public async Task R9_MinimalApiUnreadableJson_IsTheSameValidationShape_InEveryEnvironment(string environment)
    {
        await using var app = await StartBindingHostAsync(environment);

        using var response = await app.GetTestClient().PostAsync("/json", Json("{\"name\":5}"));

        var problem = await response.ShouldBeProblemAsync(StatusCodes.Status400BadRequest, ErrorCodes.Validation.Failed);
        problem.GetProperty(ProblemDetailsExtensionNames.Errors).GetProperty("name")[0].GetString().Should().Be("The value is not valid.");
        problem.GetProperty(ProblemDetailsExtensionNames.ErrorCodes).GetProperty("name")[0].GetString()
            .Should().Be(ErrorCodes.Validation.InvalidFormat);
        problem.GetRawText().Should().NotContain("System.").And.NotContain("Payload");
    }

    [Theory]
    [InlineData(WebApiTestHost.Production)]
    [InlineData(WebApiTestHost.Development)]
    public async Task R9_MinimalApiParameterThatCannotBeBound_Is400_WithoutDotNetTypeNames(string environment)
    {
        // Lower-case letters outside hexadecimal: the value cannot occur by chance in the body's random trace and
        // correlation ids (hex digits, or an upper-case request id), so finding it would mean it was echoed.
        const string unbindable = "seventeen";
        await using var app = await StartBindingHostAsync(environment);

        using var response = await app.GetTestClient().GetAsync($"/numbers?id={unbindable}");

        var problem = await response.ShouldBeProblemAsync(StatusCodes.Status400BadRequest, ErrorCodes.Validation.InvalidFormat);
        problem.Detail().Should().Be("The request is not valid.");
        problem.GetRawText().Should().NotContain("Int32").And.NotContain("int id").And.NotContain(unbindable);
    }

    [Theory]
    [InlineData("/throw-validation-single", "/result-validation-single")]
    [InlineData("/throw-validation", "/result-validation-unnamed")]
    public async Task R9_ThrownValidationException_WritesTheBodyOfTheReturnedError(string thrownPath, string returnedPath)
    {
        await using var app = await WebApiTestHost.StartAsync(app =>
        {
            app.MapGet("/throw-validation-single", IResult () => throw new ValidationException(Error.Validation("customer.name_required", "Name is required.")));
            app.MapGet("/result-validation-single", () => Result<string>.Failure(Error.Validation("customer.name_required", "Name is required.")).ToOk());
            app.MapGet("/throw-validation", IResult () => throw new ValidationException(
                [Error.Validation("customer.name_required", "Name is required."), Error.Validation("customer.email_invalid", "Email is invalid.")]));
            app.MapGet("/result-validation-unnamed", () => Result<string>.Failure(Error.Validation(
                [Error.Validation("customer.name_required", "Name is required."), Error.Validation("customer.email_invalid", "Email is invalid.")])).ToOk());
        });

        using var thrown = await app.GetTestClient().GetAsync(thrownPath);
        using var returned = await app.GetTestClient().GetAsync(returnedPath);

        var thrownBody = Comparable(await thrown.ShouldBeProblemAsync(StatusCodes.Status400BadRequest, await CodeOfAsync(returned)));
        var returnedBody = Comparable(await returned.ShouldBeProblemAsync(StatusCodes.Status400BadRequest, await CodeOfAsync(thrown)));
        thrownBody.Should().Be(returnedBody);
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private static async Task<string> CodeOfAsync(HttpResponseMessage response)
    {
        using var document = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty(ProblemDetailsExtensionNames.ErrorCode).GetString()!;
    }

    // The members that differ between any two requests are left out.
    private static string Comparable(System.Text.Json.JsonElement problem)
    {
        var members = problem.EnumerateObject()
            .Where(member => member.Name is not ("instance" or ProblemDetailsExtensionNames.TraceId or ProblemDetailsExtensionNames.CorrelationId))
            .Select(member => $"{member.Name}={member.Value.GetRawText()}");
        return string.Join("|", members);
    }

    private static Task<WebApplication> StartBindingHostAsync(string environment) =>
        WebApiTestHost.StartAsync(
            app =>
            {
                app.MapPost("/json", ([Microsoft.AspNetCore.Mvc.FromBody] FullStackHost.Payload payload) => payload.Name);
                app.MapGet("/numbers", (int id) => id);
            },
            environment: environment);

    [Fact]
    public async Task RateLimitRejection_WritesProblem()
    {
        var partition = Guid.NewGuid().ToString("N");

        using var first = await SendLimitedAsync(partition);
        using var second = await SendLimitedAsync(partition);

        first.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
        await second.ShouldBeProblemAsync(StatusCodes.Status429TooManyRequests, PresentationErrorCodes.RateLimitExceeded);
    }

    [Fact]
    public async Task ValidationFailure_ListsEveryFieldError()
    {
        using var response = await _host.Client.GetAsync("/result-validation");

        var problem = await response.ShouldBeProblemAsync(StatusCodes.Status400BadRequest, ErrorCodes.Validation.Failed);
        var errors = problem.GetProperty(ProblemDetailsExtensionNames.Errors);
        errors.GetProperty("Name")[0].GetString().Should().Be("Name is required.");
        errors.GetProperty("Email")[0].GetString().Should().Be("Email is invalid.");
        var codes = problem.GetProperty(ProblemDetailsExtensionNames.ErrorCodes);
        codes.GetProperty("Name")[0].GetString().Should().Be("customer.name_required");
        codes.GetProperty("Email")[0].GetString().Should().Be("customer.email_invalid");
    }

    [Fact]
    public async Task ThrownValidationException_ListsEveryFieldError_LikeAReturnedOne()
    {
        using var response = await _host.Client.GetAsync("/throw-validation");

        var problem = await response.ShouldBeProblemAsync(StatusCodes.Status400BadRequest, ErrorCodes.Validation.Failed);
        var codes = problem.GetProperty(ProblemDetailsExtensionNames.ErrorCodes);
        codes.GetProperty("customer.name_required")[0].GetString().Should().Be("customer.name_required");
        codes.GetProperty("customer.email_invalid")[0].GetString().Should().Be("customer.email_invalid");
    }

    [Fact]
    public async Task ResultFailure_DetailIsTheErrorMessage_AndInstanceIsThePath()
    {
        using var response = await _host.Client.GetAsync("/result-failure");

        var problem = await response.ShouldBeProblemAsync(StatusCodes.Status404NotFound, "order.not_found");
        problem.Detail().Should().Be("Order 42 was not found.");
        problem.GetProperty("instance").GetString().Should().Be("/result-failure");
        problem.GetProperty("title").GetString().Should().Be("Not Found");
        problem.GetProperty("type").GetString().Should().Be("https://tools.ietf.org/html/rfc9110#section-15.5.5");
    }

    [Fact]
    public async Task AcceptHeaderExcludingJson_StillWritesProblemJson()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/result-failure");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));

        using var response = await _host.Client.SendAsync(request);

        var problem = await response.ShouldBeProblemAsync(StatusCodes.Status404NotFound, "order.not_found");
        problem.GetProperty("title").GetString().Should().Be("Not Found");
    }

    [Fact]
    public async Task GrpcRequest_WithBodilessErrorStatus_IsLeftUntouched()
    {
        using var content = new ByteArrayContent([]);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/grpc");

        using var response = await _host.Client.PostAsync("/does-not-exist", content);

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.NotFound);
        (await response.Content.ReadAsByteArrayAsync()).Should().BeEmpty();
        response.Content.Headers.ContentType.Should().BeNull();
    }

    [Fact]
    public async Task InboundCorrelationId_IsEchoedInTheProblem()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/result-failure");
        request.Headers.Add(WellKnownHeaders.CorrelationId, "order-flow-17");

        using var response = await _host.Client.SendAsync(request);

        var problem = await response.ShouldBeProblemAsync(StatusCodes.Status404NotFound, "order.not_found");
        problem.GetProperty(ProblemDetailsExtensionNames.CorrelationId).GetString().Should().Be("order-flow-17");
    }

    private Task<HttpResponseMessage> SendLimitedAsync(string partition)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/limited");
        request.Headers.Add(FullStackHost.PartitionHeader, partition);
        return _host.Client.SendAsync(request);
    }
}
