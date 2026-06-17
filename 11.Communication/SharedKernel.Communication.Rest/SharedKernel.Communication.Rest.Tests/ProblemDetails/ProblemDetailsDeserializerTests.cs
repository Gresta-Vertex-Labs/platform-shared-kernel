using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SharedKernel.Communication.Rest.ProblemDetails;

namespace SharedKernel.Communication.Rest.Tests.ProblemDetails;

public sealed class ProblemDetailsDeserializerTests
{
    [Fact]
    public async Task DeserializeAsync_WithProblemJsonContentType_MapsTypeToCode()
    {
        // Arrange
        var response = BuildProblemDetailsResponse(
            statusCode: HttpStatusCode.UnprocessableEntity,
            body: """{"type":"validation.required","title":"Validation Failed","detail":"Name is required","status":422}""");

        // Act
        var error = await ProblemDetailsDeserializer.DeserializeAsync(response, CancellationToken.None);

        // Assert
        error.Code.Should().Be("validation.required");
        error.Message.Should().Be("Name is required");
    }

    [Fact]
    public async Task DeserializeAsync_WithProblemJsonAndNoDetail_UsesTitleAsMessage()
    {
        // Arrange
        var response = BuildProblemDetailsResponse(
            statusCode: HttpStatusCode.NotFound,
            body: """{"type":"not.found","title":"Resource Not Found","status":404}""");

        // Act
        var error = await ProblemDetailsDeserializer.DeserializeAsync(response, CancellationToken.None);

        // Assert
        error.Code.Should().Be("not.found");
        error.Message.Should().Be("Resource Not Found");
    }

    [Fact]
    public async Task DeserializeAsync_WithNonProblemJsonContentType_ReturnsGenericError()
    {
        // Arrange
        var response = new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("<html>Error</html>", Encoding.UTF8, "text/html")
        };

        // Act
        var error = await ProblemDetailsDeserializer.DeserializeAsync(response, CancellationToken.None);

        // Assert
        error.Should().NotBeNull();
        error.Code.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task DeserializeAsync_WithProblemJsonAndNoType_UsesStatusCodeAsCode()
    {
        // Arrange
        var response = BuildProblemDetailsResponse(
            statusCode: HttpStatusCode.BadRequest,
            body: """{"title":"Bad Request","detail":"Some error","status":400}""");

        // Act
        var error = await ProblemDetailsDeserializer.DeserializeAsync(response, CancellationToken.None);

        // Assert
        error.Code.Should().Be("http.400");
        error.Message.Should().Be("Some error");
    }

    [Fact]
    public async Task DeserializeAsync_WithMalformedJson_DoesNotThrow()
    {
        // Arrange
        var response = BuildProblemDetailsResponse(
            statusCode: HttpStatusCode.InternalServerError,
            body: "{ not valid json }}}");

        // Act
        Func<Task> act = () => ProblemDetailsDeserializer.DeserializeAsync(response, CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeserializeAsync_NeverThrows_OnAnyInput()
    {
        // Arrange
        var response = new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent(string.Empty)
        };

        // Act
        Func<Task> act = () => ProblemDetailsDeserializer.DeserializeAsync(response, CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }

    private static HttpResponseMessage BuildProblemDetailsResponse(HttpStatusCode statusCode, string body)
    {
        var content = new StringContent(body, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue(ProblemDetailsDeserializer.ProblemDetailsContentType);
        return new HttpResponseMessage(statusCode) { Content = content };
    }
}
