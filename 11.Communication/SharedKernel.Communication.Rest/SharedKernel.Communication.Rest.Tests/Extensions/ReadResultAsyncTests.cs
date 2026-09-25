using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using SharedKernel.Communication.Rest.Extensions;
using SharedKernel.Communication.Rest.ProblemDetails;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Communication.Rest.Tests.Extensions;

/// <summary>
/// ReadResultAsync&lt;T&gt; unit tests covering both the JsonTypeInfo&lt;T&gt; (source-generated)
/// and JsonSerializerOptions? (reflection-based) overloads.
/// </summary>
public sealed class ReadResultAsyncTests
{
    // STJ source-generated context for test DTO (JsonTypeInfo overload)
    private static readonly JsonTypeInfo<OrderDto> OrderDtoTypeInfo =
        ReadResultTestJsonContext.Default.OrderDto;

    // -----------------------------------------------------------------------
    // JsonTypeInfo<T> overload (R-15)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ReadResultAsync_TypeInfo_2xxWithValidJson_ReturnsSuccess()
    {
        // Source-generated context uses exact property names (Id, Amount)
        var response = BuildJsonResponse(HttpStatusCode.OK, """{"Id":"order-1","Amount":99.99}""");

        var result = await response.ReadResultAsync(OrderDtoTypeInfo);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value.Id.Should().Be("order-1");
    }

    [Fact]
    public async Task ReadResultAsync_TypeInfo_2xxWithEmptyBody_ReturnsFailure_EmptyBodyCode()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(string.Empty, Encoding.UTF8, "application/json")
        };

        var result = await response.ReadResultAsync(OrderDtoTypeInfo);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("http.empty-body");
    }

    [Fact]
    public async Task ReadResultAsync_TypeInfo_2xxWithWhitespaceBody_ReturnsFailure_EmptyBodyCode()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("   ", Encoding.UTF8, "application/json")
        };

        var result = await response.ReadResultAsync(OrderDtoTypeInfo);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("http.empty-body");
    }

    [Fact]
    public async Task ReadResultAsync_TypeInfo_2xxWithJsonNullBody_ReturnsFailure_EmptyBodyCode()
    {
        var response = BuildJsonResponse(HttpStatusCode.OK, "null");

        var result = await response.ReadResultAsync(OrderDtoTypeInfo);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("http.empty-body");
    }

    [Fact]
    public async Task ReadResultAsync_TypeInfo_Non2xxWithProblemJson_ReturnsFailure_WithProblemCode()
    {
        // "errorCode" carries Error.Code; "title" is the status reason phrase and "type" a URI (P-562).
        var body = """{"type":"https://tools.ietf.org/html/rfc4918#section-11.2","title":"Unprocessable Entity","errorCode":"validation.required","detail":"Id is required","status":422}""";
        var response = BuildProblemDetailsResponse(HttpStatusCode.UnprocessableEntity, body);

        var result = await response.ReadResultAsync(OrderDtoTypeInfo);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("validation.required");
        result.Error.Message.Should().Be("Id is required");
    }

    [Fact]
    public async Task ReadResultAsync_TypeInfo_Non2xxWithTitleButNoErrorCode_FailsWithTheStatusCode()
    {
        // ASP.NET Core's default problem from a service outside the platform: the reason phrase in "title" is
        // never adopted as the caller's error code.
        var body = """{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.5","title":"Not Found","status":404,"detail":"No order 42."}""";
        var response = BuildProblemDetailsResponse(HttpStatusCode.NotFound, body);

        var result = await response.ReadResultAsync(OrderDtoTypeInfo);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(Error.NotFound("http.404", "No order 42."));
    }

    [Fact]
    public async Task ReadResultAsync_TypeInfo_PlatformValidationProblem_FailsWithEveryFieldError()
    {
        // The body a platform service writes for Error.Validation of two field errors.
        var body = """
            {
              "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
              "title": "Bad Request",
              "status": 400,
              "detail": "2 validation errors occurred.",
              "instance": "/orders",
              "errorCode": "validation.failed",
              "errors": { "Id": ["Id is required."], "Amount": ["Amount must be positive."] },
              "errorCodes": { "Id": ["validation.required"], "Amount": ["validation.out_of_range"] }
            }
            """;
        var response = BuildProblemDetailsResponse(HttpStatusCode.BadRequest, body);

        var result = await response.ReadResultAsync(OrderDtoTypeInfo);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
        result.Error.Code.Should().Be(ErrorCodes.Validation.Failed);
        result.Error.Details.Select(d => d.Code).Should().Equal("validation.required", "validation.out_of_range");
        result.Error.Details.Select(d => d.MessageArguments[ErrorArgumentNames.PropertyPath]).Should().Equal("Id", "Amount");
    }

    [Fact]
    public async Task ReadResultAsync_TypeInfo_FieldErrorsOnAServiceUnavailable_StayARetryableOutage()
    {
        // An "errors" map on a 503 must not turn a retryable outage into a validation failure.
        var body = """{"title":"Service Unavailable","status":503,"detail":"Try again later.","errorCode":"storage.unavailable","errors":{"Id":["Id is required."]}}""";
        var response = BuildProblemDetailsResponse(HttpStatusCode.ServiceUnavailable, body);

        var result = await response.ReadResultAsync(OrderDtoTypeInfo);

        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(Error.Unavailable("storage.unavailable", "Try again later."));
        result.Error.Details.Should().BeEmpty();
    }

    [Fact]
    public async Task ReadResultAsync_TypeInfo_Non2xxWithNonProblemBody_ReturnsFailure_WithGenericCode()
    {
        var response = new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("<html>Error</html>", Encoding.UTF8, "text/html")
        };

        var result = await response.ReadResultAsync(OrderDtoTypeInfo);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, SharedKernel.Primitives.Errors.ErrorType.Unavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout, SharedKernel.Primitives.Errors.ErrorType.Timeout)]
    public async Task ReadResultAsync_TypeInfo_GatewayOutageWithHtmlBody_FailsAsARetryableOutage(
        HttpStatusCode statusCode,
        SharedKernel.Primitives.Errors.ErrorType expectedType)
    {
        // A gateway answering for a down or slow service with its own HTML page: the typed client's caller sees
        // an outage it can retry (P-562), not an Unexpected defect.
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent("<html><body>Try again later</body></html>", Encoding.UTF8, "text/html")
        };

        var result = await response.ReadResultAsync(OrderDtoTypeInfo);

        result.IsSuccess.Should().BeFalse();
        result.Error.Type.Should().Be(expectedType);
        result.Error.Code.Should().Be($"http.{(int)statusCode}");
    }

    // -----------------------------------------------------------------------
    // JsonSerializerOptions? overload (R-16)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ReadResultAsync_Options_2xxWithValidJson_ReturnsSuccess()
    {
        var response = BuildJsonResponse(HttpStatusCode.OK, """{"id":"order-2","amount":50.00}""");

        var result = await response.ReadResultAsync<OrderDto>(options: null);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be("order-2");
    }

    [Fact]
    public async Task ReadResultAsync_Options_2xxWithEmptyBody_ReturnsFailure_EmptyBodyCode()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(string.Empty, Encoding.UTF8, "application/json")
        };

        var result = await response.ReadResultAsync<OrderDto>(options: null);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("http.empty-body");
    }

    [Fact]
    public async Task ReadResultAsync_Options_Non2xxWithProblemJson_ReturnsFailure_WithProblemCode()
    {
        // "errorCode" carries Error.Code; "title" is the status reason phrase and "type" a URI (P-562).
        var body = """{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.5","title":"Not Found","errorCode":"not.found","detail":"Order does not exist","status":404}""";
        var response = BuildProblemDetailsResponse(HttpStatusCode.NotFound, body);

        var result = await response.ReadResultAsync<OrderDto>(options: null);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("not.found");
        result.Error.Message.Should().Be("Order does not exist");
    }

    [Fact]
    public async Task ReadResultAsync_Options_UsesReflectionFallbackWhenOptionsNull()
    {
        // Assert that null options resolves to the static readonly field (case-insensitive)
        var response = BuildJsonResponse(HttpStatusCode.OK, """{"ID":"order-3","Amount":25.00}""");
        // Case-insensitive matching — ID (uppercase) should map to Id property
        var caseInsensitiveOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        var result = await response.ReadResultAsync<OrderDto>(options: caseInsensitiveOptions);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be("order-3");
    }

    [Fact]
    public async Task ReadResultAsync_Options_WithExplicitOptions_UsesProvidedOptions()
    {
        // Explicit camelCase policy — "id" maps to "Id" when PropertyNameCaseInsensitive is true
        var camelCaseOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var response = BuildJsonResponse(HttpStatusCode.OK, """{"id":"order-4","amount":10.00}""");

        var result = await response.ReadResultAsync<OrderDto>(options: camelCaseOptions);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be("order-4");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static HttpResponseMessage BuildJsonResponse(HttpStatusCode statusCode, string json) =>
        new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private static HttpResponseMessage BuildProblemDetailsResponse(HttpStatusCode statusCode, string body)
    {
        var content = new StringContent(body, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue(ProblemDetailsDeserializer.ProblemDetailsContentType);
        return new HttpResponseMessage(statusCode) { Content = content };
    }
}

/// <summary>DTO for ReadResultAsync tests.</summary>
public sealed class OrderDto
{
    public string Id { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

[JsonSerializable(typeof(OrderDto))]
internal partial class ReadResultTestJsonContext : JsonSerializerContext;
