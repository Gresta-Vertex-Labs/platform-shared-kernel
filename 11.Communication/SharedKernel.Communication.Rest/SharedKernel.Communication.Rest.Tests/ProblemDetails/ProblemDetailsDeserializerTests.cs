using System.Net;
using System.Net.Http.Headers;
using System.Text;
using SharedKernel.Communication.Rest.ProblemDetails;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Communication.Rest.Tests.ProblemDetails;

public sealed class ProblemDetailsDeserializerTests
{
    // -----------------------------------------------------------------------
    // Status -> ErrorType reverse mapping (mirrors 14.Presentation's ErrorTypeStatusCodeMap)
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, ErrorType.Validation)]
    [InlineData(HttpStatusCode.Unauthorized, ErrorType.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, ErrorType.Forbidden)]
    [InlineData(HttpStatusCode.NotFound, ErrorType.NotFound)]
    [InlineData(HttpStatusCode.Conflict, ErrorType.Conflict)]
    [InlineData(HttpStatusCode.UnprocessableEntity, ErrorType.BusinessRule)]
    [InlineData(HttpStatusCode.InternalServerError, ErrorType.Unexpected)]
    [InlineData(HttpStatusCode.BadGateway, ErrorType.Unexpected)]
    public async Task DeserializeAsync_EachMappedStatusCode_ProducesExpectedErrorType(
        HttpStatusCode statusCode,
        ErrorType expectedType)
    {
        // Arrange — the real 14.Presentation wire shape: "title"/"errorCode" carry Error.Code,
        // "type" is an RFC 9457 status URI (never the code), "detail" carries Error.Message.
        var response = BuildProblemDetailsResponse(
            statusCode,
            body: $$"""
                {"type":"https://httpstatuses.io/{{(int)statusCode}}","title":"some.code","errorCode":"some.code","detail":"some message","status":{{(int)statusCode}}}
                """);

        // Act
        var error = await ProblemDetailsDeserializer.DeserializeAsync(response, CancellationToken.None);

        // Assert
        error.Type.Should().Be(expectedType);
        error.Code.Should().Be("some.code");
        error.Message.Should().Be("some message");
    }

    // -----------------------------------------------------------------------
    // Code / message field resolution (errorCode / title / detail, never type)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DeserializeAsync_WithErrorCodeExtension_PrefersErrorCodeOverTitle()
    {
        // Arrange — title and errorCode deliberately differ so precedence is unambiguous.
        var response = BuildProblemDetailsResponse(
            statusCode: HttpStatusCode.NotFound,
            body: """{"type":"https://httpstatuses.io/404","title":"title.value","errorCode":"errorCode.value","detail":"Resource not found","status":404}""");

        // Act
        var error = await ProblemDetailsDeserializer.DeserializeAsync(response, CancellationToken.None);

        // Assert
        error.Code.Should().Be("errorCode.value");
        error.Message.Should().Be("Resource not found");
    }

    [Fact]
    public async Task DeserializeAsync_WithNoErrorCodeExtension_UsesTitleAsCode()
    {
        // Arrange — this is the real shape: 14.Presentation always sets Title = Error.Code.
        var response = BuildProblemDetailsResponse(
            statusCode: HttpStatusCode.NotFound,
            body: """{"type":"https://httpstatuses.io/404","title":"order.not_found","detail":"Order 123 was not found","status":404}""");

        // Act
        var error = await ProblemDetailsDeserializer.DeserializeAsync(response, CancellationToken.None);

        // Assert
        error.Code.Should().Be("order.not_found");
        error.Message.Should().Be("Order 123 was not found");
    }

    [Fact]
    public async Task DeserializeAsync_WithNeitherErrorCodeNorTitle_UsesStatusCodeAsCode()
    {
        // Arrange
        var response = BuildProblemDetailsResponse(
            statusCode: HttpStatusCode.BadRequest,
            body: """{"detail":"Some error","status":400}""");

        // Act
        var error = await ProblemDetailsDeserializer.DeserializeAsync(response, CancellationToken.None);

        // Assert
        error.Code.Should().Be("http.400");
        error.Message.Should().Be("Some error");
    }

    [Fact]
    public async Task DeserializeAsync_WithTypeCarryingAStatusUri_NeverUsesTypeAsCode()
    {
        // Arrange — "type" must never be read as the code, even when it looks code-shaped.
        var response = BuildProblemDetailsResponse(
            statusCode: HttpStatusCode.NotFound,
            body: """{"type":"looks.like.a.code","title":"order.not_found","detail":"Order not found","status":404}""");

        // Act
        var error = await ProblemDetailsDeserializer.DeserializeAsync(response, CancellationToken.None);

        // Assert
        error.Code.Should().Be("order.not_found");
        error.Code.Should().NotBe("looks.like.a.code");
    }

    [Fact]
    public async Task DeserializeAsync_WithNoDetail_FallsBackToStatusAwareMessage_NeverTitle()
    {
        // Arrange — Title carries the machine code, so it must never leak into Message as a fallback.
        var response = BuildProblemDetailsResponse(
            statusCode: HttpStatusCode.NotFound,
            body: """{"type":"https://httpstatuses.io/404","title":"order.not_found","status":404}""");

        // Act
        var error = await ProblemDetailsDeserializer.DeserializeAsync(response, CancellationToken.None);

        // Assert
        error.Code.Should().Be("order.not_found");
        error.Message.Should().Be("HTTP 404 error");
    }

    // -----------------------------------------------------------------------
    // Multi-field validation aggregate rebuild (the P-544 fidelity fix)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DeserializeAsync_WithErrorsExtension_RebuildsEveryFieldIntoDetails()
    {
        // Arrange — the legacy shape, with no "errorCodes" member: each "errors" key is taken as the
        // code, each value an array of messages.
        var response = BuildProblemDetailsResponse(
            statusCode: HttpStatusCode.BadRequest,
            body: """
                {
                  "type": "https://httpstatuses.io/400",
                  "title": "validation.failed",
                  "errorCode": "validation.failed",
                  "detail": "2 validation errors occurred.",
                  "status": 400,
                  "errors": {
                    "name.required": ["Name is required."],
                    "email.invalid_format": ["Email is not a valid address.", "Email exceeds the maximum length."]
                  }
                }
                """);

        // Act
        var error = await ProblemDetailsDeserializer.DeserializeAsync(response, CancellationToken.None);

        // Assert — one aggregate Validation error carrying every field failure, never the fallen-back
        // single-error shape the pre-fix deserializer produced.
        error.Type.Should().Be(ErrorType.Validation);
        error.Code.Should().Be(ErrorCodes.Validation.Failed);
        error.Details.Should().HaveCount(3);
        error.Details.Should().ContainSingle(d => d.Code == "name.required" && d.Message == "Name is required.");
        error.Details.Should().ContainSingle(d => d.Code == "email.invalid_format" && d.Message == "Email is not a valid address.");
        error.Details.Should().ContainSingle(d => d.Code == "email.invalid_format" && d.Message == "Email exceeds the maximum length.");
        error.Details.Should().OnlyContain(d => d.Type == ErrorType.Validation);
    }

    [Fact]
    public async Task DeserializeAsync_WithFieldKeyedErrorsAndErrorCodes_RestoresRealCodesAndFieldPaths()
    {
        // Arrange — the current 14.Presentation shape: "errors" keyed by field path (or by code for
        // an error that names no field) and "errorCodes" aligned with it index by index.
        var response = BuildProblemDetailsResponse(
            statusCode: HttpStatusCode.BadRequest,
            body: """
                {
                  "type": "https://httpstatuses.io/400",
                  "title": "validation.failed",
                  "errorCode": "validation.failed",
                  "detail": "3 validation errors occurred.",
                  "status": 400,
                  "errors": {
                    "Accounts[0].Iban": ["IBAN check digits are not correct.", "IBAN must not exceed 34 characters."],
                    "order.limit_exceeded": ["Order exceeds the limit."]
                  },
                  "errorCodes": {
                    "Accounts[0].Iban": ["validation.iban.invalid_check_digits", "validation.max_length"],
                    "order.limit_exceeded": ["order.limit_exceeded"]
                  }
                }
                """);

        // Act
        var error = await ProblemDetailsDeserializer.DeserializeAsync(response, CancellationToken.None);

        // Assert
        error.Code.Should().Be(ErrorCodes.Validation.Failed);
        error.Details.Should().HaveCount(3);

        error.Details[0].Code.Should().Be("validation.iban.invalid_check_digits");
        error.Details[0].Message.Should().Be("IBAN check digits are not correct.");
        error.Details[0].MessageArguments[ErrorArgumentNames.PropertyPath].Should().Be("Accounts[0].Iban");

        error.Details[1].Code.Should().Be("validation.max_length");
        error.Details[1].Message.Should().Be("IBAN must not exceed 34 characters.");
        error.Details[1].MessageArguments[ErrorArgumentNames.PropertyPath].Should().Be("Accounts[0].Iban");

        // A code-keyed entry names no field, so it gets no field path.
        error.Details[2].Code.Should().Be("order.limit_exceeded");
        error.Details[2].MessageArguments.Should().BeEmpty();
    }

    [Fact]
    public async Task DeserializeAsync_WithErrorCodesShorterThanMessages_FallsBackToTheKeyForUnmatchedMessages()
    {
        // Arrange — a malformed or partial "errorCodes" entry must never drop a message.
        var response = BuildProblemDetailsResponse(
            statusCode: HttpStatusCode.BadRequest,
            body: """
                {
                  "status": 400,
                  "errors": { "Name": ["Name is required.", "Name is too long."], "Email": ["Email is invalid."] },
                  "errorCodes": { "Name": ["validation.required"] }
                }
                """);

        // Act
        var error = await ProblemDetailsDeserializer.DeserializeAsync(response, CancellationToken.None);

        // Assert
        error.Details.Select(d => d.Code).Should().Equal("validation.required", "Name", "Email");
        error.Details[0].MessageArguments[ErrorArgumentNames.PropertyPath].Should().Be("Name");
        error.Details[1].MessageArguments.Should().BeEmpty();
        error.Details[2].MessageArguments.Should().BeEmpty();
    }

    [Fact]
    public async Task DeserializeAsync_PlainJsonContentType_AlsoReadsErrorCodes()
    {
        // Arrange — the reflection fallback path (not application/problem+json) binds the same members.
        var content = new StringContent(
            """{"status":400,"errors":{"Name":["Name is required."]},"errorCodes":{"Name":["validation.required"]}}""",
            Encoding.UTF8,
            "application/json");
        var response = new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = content };

        // Act
        var error = await ProblemDetailsDeserializer.DeserializeAsync(response, CancellationToken.None);

        // Assert
        var detail = error.Details.Should().ContainSingle().Subject;
        detail.Code.Should().Be("validation.required");
        detail.MessageArguments[ErrorArgumentNames.PropertyPath].Should().Be("Name");
    }

    [Fact]
    public async Task DeserializeAsync_WithEmptyErrorsExtension_FallsBackToSingleError()
    {
        // Arrange — an "errors" object present but empty must not short-circuit into a zero-detail
        // aggregate (Error.Validation(IReadOnlyList<Error>) rejects an empty list).
        var response = BuildProblemDetailsResponse(
            statusCode: HttpStatusCode.BadRequest,
            body: """{"type":"https://httpstatuses.io/400","title":"validation.failed","errorCode":"validation.failed","detail":"Bad request","status":400,"errors":{}}""");

        // Act
        var error = await ProblemDetailsDeserializer.DeserializeAsync(response, CancellationToken.None);

        // Assert
        error.Type.Should().Be(ErrorType.Validation);
        error.Code.Should().Be("validation.failed");
        error.Message.Should().Be("Bad request");
        error.Details.Should().BeEmpty();
    }

    [Fact]
    public async Task DeserializeAsync_WithNoErrorsExtension_YieldsSingleError()
    {
        // Arrange — the ordinary, non-aggregate case: no "errors" member at all.
        var response = BuildProblemDetailsResponse(
            statusCode: HttpStatusCode.Conflict,
            body: """{"type":"https://httpstatuses.io/409","title":"order.already_shipped","errorCode":"order.already_shipped","detail":"Order has already shipped","status":409}""");

        // Act
        var error = await ProblemDetailsDeserializer.DeserializeAsync(response, CancellationToken.None);

        // Assert
        error.Type.Should().Be(ErrorType.Conflict);
        error.Code.Should().Be("order.already_shipped");
        error.Message.Should().Be("Order has already shipped");
        error.Details.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Non-JSON / empty / malformed bodies — never throw, still status-aware
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DeserializeAsync_WithNonProblemJsonContentType_ReturnsStatusAwareUnexpectedError()
    {
        // Arrange
        var response = new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("<html>Error</html>", Encoding.UTF8, "text/html")
        };

        // Act
        var error = await ProblemDetailsDeserializer.DeserializeAsync(response, CancellationToken.None);

        // Assert
        error.Type.Should().Be(ErrorType.Unexpected);
        error.Code.Should().Be("http.500");
    }

    [Fact]
    public async Task DeserializeAsync_WithMalformedJson_DoesNotThrow_AndReturnsStatusAwareUnexpectedError()
    {
        // Arrange
        var response = BuildProblemDetailsResponse(
            statusCode: HttpStatusCode.InternalServerError,
            body: "{ not valid json }}}");

        // Act
        Func<Task<Error>> act = () => ProblemDetailsDeserializer.DeserializeAsync(response, CancellationToken.None);

        // Assert
        var error = await act.Should().NotThrowAsync();
        error.Subject.Type.Should().Be(ErrorType.Unexpected);
        error.Subject.Code.Should().Be("http.500");
    }

    [Fact]
    public async Task DeserializeAsync_WithEmptyBody_DoesNotThrow_AndReturnsStatusAwareUnexpectedError()
    {
        // Arrange
        var response = new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent(string.Empty)
        };

        // Act
        Func<Task<Error>> act = () => ProblemDetailsDeserializer.DeserializeAsync(response, CancellationToken.None);

        // Assert
        var error = await act.Should().NotThrowAsync();
        error.Subject.Type.Should().Be(ErrorType.Unexpected);
        error.Subject.Code.Should().Be("http.502");
    }

    [Fact]
    public async Task DeserializeAsync_WithEmptyJsonObjectBody_NoRecognizableMembers_ReturnsStatusAwareUnexpectedError()
    {
        // Arrange — a well-formed JSON body that deserializes cleanly but carries none of the members
        // this deserializer maps from.
        var response = BuildProblemDetailsResponse(
            statusCode: HttpStatusCode.NotFound,
            body: "{}");

        // Act
        var error = await ProblemDetailsDeserializer.DeserializeAsync(response, CancellationToken.None);

        // Assert
        error.Type.Should().Be(ErrorType.Unexpected);
        error.Code.Should().Be("http.404");
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
