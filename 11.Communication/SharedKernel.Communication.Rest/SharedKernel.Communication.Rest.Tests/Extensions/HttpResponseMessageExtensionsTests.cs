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
        // Arrange
        var body = """{"type":"validation.required","title":"Validation Failed","detail":"Name is required","status":422}""";
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
}
