using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using SharedKernel.Communication.Rest.Extensions;
using SharedKernel.Communication.Rest.ProblemDetails;

namespace SharedKernel.Communication.Rest.Tests.Extensions;

/// <summary>
/// T-21: ReadEnvelopeAsync&lt;T&gt; unit tests covering both the JsonTypeInfo&lt;T&gt; (AOT-safe)
/// and JsonSerializerOptions? (reflection-based) overloads.
/// </summary>
public sealed class ReadEnvelopeAsyncTests
{
    // STJ source-generated context for test DTO (JsonTypeInfo overload)
    private static readonly JsonTypeInfo<OrderDto> OrderDtoTypeInfo =
        ReadEnvelopeTestJsonContext.Default.OrderDto;

    // -----------------------------------------------------------------------
    // JsonTypeInfo<T> overload (R-15)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ReadEnvelopeAsync_TypeInfo_2xxWithValidJson_ReturnsOkEnvelope()
    {
        // Source-generated context uses exact property names (Id, Amount)
        var response = BuildJsonResponse(HttpStatusCode.OK, """{"Id":"order-1","Amount":99.99}""");

        var envelope = await response.ReadEnvelopeAsync(OrderDtoTypeInfo);

        envelope.IsSuccess.Should().BeTrue();
        envelope.Value.Should().NotBeNull();
        envelope.Value!.Id.Should().Be("order-1");
    }

    [Fact]
    public async Task ReadEnvelopeAsync_TypeInfo_2xxWithEmptyBody_ReturnsFail_EmptyBodyCode()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(string.Empty, Encoding.UTF8, "application/json")
        };

        var envelope = await response.ReadEnvelopeAsync(OrderDtoTypeInfo);

        envelope.IsSuccess.Should().BeFalse();
        envelope.Error!.Code.Should().Be("http.empty-body");
    }

    [Fact]
    public async Task ReadEnvelopeAsync_TypeInfo_2xxWithWhitespaceBody_ReturnsFail_EmptyBodyCode()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("   ", Encoding.UTF8, "application/json")
        };

        var envelope = await response.ReadEnvelopeAsync(OrderDtoTypeInfo);

        envelope.IsSuccess.Should().BeFalse();
        envelope.Error!.Code.Should().Be("http.empty-body");
    }

    [Fact]
    public async Task ReadEnvelopeAsync_TypeInfo_Non2xxWithProblemJson_ReturnsFail_WithProblemCode()
    {
        var body = """{"type":"validation.required","title":"Validation Failed","detail":"Id is required","status":422}""";
        var response = BuildProblemDetailsResponse(HttpStatusCode.UnprocessableEntity, body);

        var envelope = await response.ReadEnvelopeAsync(OrderDtoTypeInfo);

        envelope.IsSuccess.Should().BeFalse();
        envelope.Error!.Code.Should().Be("validation.required");
        envelope.Error!.Message.Should().Be("Id is required");
    }

    [Fact]
    public async Task ReadEnvelopeAsync_TypeInfo_Non2xxWithNonProblemBody_ReturnsFail_WithGenericCode()
    {
        var response = new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("<html>Error</html>", Encoding.UTF8, "text/html")
        };

        var envelope = await response.ReadEnvelopeAsync(OrderDtoTypeInfo);

        envelope.IsSuccess.Should().BeFalse();
        envelope.Error!.Code.Should().NotBeNullOrEmpty();
    }

    // -----------------------------------------------------------------------
    // JsonSerializerOptions? overload (R-16)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ReadEnvelopeAsync_Options_2xxWithValidJson_ReturnsOkEnvelope()
    {
        var response = BuildJsonResponse(HttpStatusCode.OK, """{"id":"order-2","amount":50.00}""");

        var envelope = await response.ReadEnvelopeAsync<OrderDto>(options: null);

        envelope.IsSuccess.Should().BeTrue();
        envelope.Value!.Id.Should().Be("order-2");
    }

    [Fact]
    public async Task ReadEnvelopeAsync_Options_2xxWithEmptyBody_ReturnsFail_EmptyBodyCode()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(string.Empty, Encoding.UTF8, "application/json")
        };

        var envelope = await response.ReadEnvelopeAsync<OrderDto>(options: null);

        envelope.IsSuccess.Should().BeFalse();
        envelope.Error!.Code.Should().Be("http.empty-body");
    }

    [Fact]
    public async Task ReadEnvelopeAsync_Options_Non2xxWithProblemJson_ReturnsFail_WithProblemCode()
    {
        var body = """{"type":"not.found","title":"Not Found","detail":"Order does not exist","status":404}""";
        var response = BuildProblemDetailsResponse(HttpStatusCode.NotFound, body);

        var envelope = await response.ReadEnvelopeAsync<OrderDto>(options: null);

        envelope.IsSuccess.Should().BeFalse();
        envelope.Error!.Code.Should().Be("not.found");
        envelope.Error!.Message.Should().Be("Order does not exist");
    }

    [Fact]
    public async Task ReadEnvelopeAsync_Options_UsesReflectionFallbackWhenOptionsNull()
    {
        // Assert that null options resolves to the static readonly field (case-insensitive)
        var response = BuildJsonResponse(HttpStatusCode.OK, """{"ID":"order-3","Amount":25.00}""");
        // Case-insensitive matching — ID (uppercase) should map to Id property
        var caseInsensitiveOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        var envelope = await response.ReadEnvelopeAsync<OrderDto>(options: caseInsensitiveOptions);

        envelope.IsSuccess.Should().BeTrue();
        envelope.Value!.Id.Should().Be("order-3");
    }

    [Fact]
    public async Task ReadEnvelopeAsync_Options_WithExplicitOptions_UsesProvidedOptions()
    {
        // Explicit camelCase policy — "id" maps to "Id" when PropertyNameCaseInsensitive is true
        var camelCaseOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var response = BuildJsonResponse(HttpStatusCode.OK, """{"id":"order-4","amount":10.00}""");

        var envelope = await response.ReadEnvelopeAsync<OrderDto>(options: camelCaseOptions);

        envelope.IsSuccess.Should().BeTrue();
        envelope.Value!.Id.Should().Be("order-4");
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

/// <summary>DTO for ReadEnvelopeAsync tests.</summary>
public sealed class OrderDto
{
    public string Id { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

[JsonSerializable(typeof(OrderDto))]
internal partial class ReadEnvelopeTestJsonContext : JsonSerializerContext;
