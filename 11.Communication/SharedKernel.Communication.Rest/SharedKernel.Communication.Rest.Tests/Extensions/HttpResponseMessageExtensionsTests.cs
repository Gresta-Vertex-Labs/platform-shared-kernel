using System.IO;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using SharedKernel.Communication.Rest.Extensions;
using SharedKernel.Communication.Rest.ProblemDetails;

namespace SharedKernel.Communication.Rest.Tests.Extensions;

public sealed class HttpResponseMessageExtensionsTests
{
    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Created)]
    [InlineData(HttpStatusCode.NoContent)]
    [InlineData(HttpStatusCode.Accepted)]
    public async Task EnsureSuccessOrErrorAsync_On2xxResponse_ReturnsSuccess(HttpStatusCode statusCode)
    {
        // Arrange
        var response = new HttpResponseMessage(statusCode);

        // Act
        var result = await response.EnsureSuccessOrErrorAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task EnsureSuccessOrErrorAsync_OnNon2xxResponse_ReturnsFailure(HttpStatusCode statusCode)
    {
        // Arrange
        var body = $@"{{""type"":""http.{(int)statusCode}"",""title"":""Error"",""status"":{(int)statusCode}}}";
        var content = new StringContent(body, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue(ProblemDetailsDeserializer.ProblemDetailsContentType);
        var response = new HttpResponseMessage(statusCode) { Content = content };

        // Act
        var result = await response.EnsureSuccessOrErrorAsync();

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task EnsureSuccessOrErrorAsync_OnProblemJsonResponse_MapsErrorFields()
    {
        // Arrange — the real 14.Presentation shape: "type" is an RFC 9457 status URI, "title" and
        // "errorCode" both carry Error.Code, "detail" carries Error.Message.
        var body = """{"type":"https://httpstatuses.io/422","title":"validation.required","errorCode":"validation.required","detail":"Name is required","status":422}""";
        var content = new StringContent(body, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue(ProblemDetailsDeserializer.ProblemDetailsContentType);
        var response = new HttpResponseMessage(HttpStatusCode.UnprocessableEntity) { Content = content };

        // Act
        var result = await response.EnsureSuccessOrErrorAsync();

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("validation.required");
        result.Error.Message.Should().Be("Name is required");
    }

    [Fact]
    public async Task EnsureSuccessOrErrorAsync_OnBodilessGatewayTimeout_FailsAsATimeout()
    {
        // Arrange — a proxy that gave up waiting: 504 with no body at all.
        var response = new HttpResponseMessage(HttpStatusCode.GatewayTimeout) { Content = new StringContent(string.Empty) };

        // Act
        var result = await response.EnsureSuccessOrErrorAsync();

        // Assert — a retryable timeout (P-562), not an Unexpected defect.
        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(SharedKernel.Primitives.Errors.ErrorType.Timeout);
        result.Error.Code.Should().Be("http.504");
    }

    /// <summary>
    /// T-37 (P-361/WO-056): the non-generic <c>EnsureSuccessOrErrorAsync</c> retired the misleading
    /// generic <c>EnsureSuccessOrErrorAsync&lt;T&gt;</c>, which returned <c>Result&lt;T&gt;.Success(default!)</c>
    /// on 2xx without ever reading the body — its signature made a payload promise it never honored.
    /// The non-generic replacement's signature (<c>Task&lt;Result&gt;</c>, not <c>Task&lt;Result&lt;T&gt;&gt;</c>)
    /// makes no payload promise at all, and this test proves it genuinely never reads the response
    /// body on the success path — the content stream is left completely untouched.
    /// </summary>
    [Fact]
    public async Task EnsureSuccessOrErrorAsync_On2xxResponseWithBody_NeverReadsTheBody()
    {
        // Arrange — a content whose stream throws if ever read, proving the body is never touched.
        var content = new ThrowingContent();
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = content };

        // Act
        var result = await response.EnsureSuccessOrErrorAsync();

        // Assert
        result.IsSuccess.Should().BeTrue();
    }
}

/// <summary>Test double whose content stream throws if ever read from.</summary>
internal sealed class ThrowingContent : HttpContent
{
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        throw new InvalidOperationException(
            "EnsureSuccessOrErrorAsync must never read the response body on the success path.");

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }
}
