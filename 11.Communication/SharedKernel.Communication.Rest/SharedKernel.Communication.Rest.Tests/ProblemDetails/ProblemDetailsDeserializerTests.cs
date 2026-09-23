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
    [InlineData(HttpStatusCode.PreconditionFailed, ErrorType.Conflict)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, ErrorType.Validation)]
    [InlineData(HttpStatusCode.UnsupportedMediaType, ErrorType.Validation)]
    [InlineData(HttpStatusCode.UnprocessableEntity, ErrorType.BusinessRule)]
    [InlineData(HttpStatusCode.PreconditionRequired, ErrorType.Validation)]
    [InlineData(HttpStatusCode.TooManyRequests, ErrorType.Unavailable)]
    [InlineData(HttpStatusCode.InternalServerError, ErrorType.Unexpected)]
    [InlineData(HttpStatusCode.BadGateway, ErrorType.Unexpected)]
    [InlineData(HttpStatusCode.ServiceUnavailable, ErrorType.Unavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout, ErrorType.Timeout)]
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
    // P-562: a downstream outage round-trips as Unavailable / Timeout, not Unexpected
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DeserializeAsync_503ProblemDetails_RoundTripsAsTheSameUnavailableError()
    {
        // Arrange — what the server's HTTP boundary writes for Error.Unavailable("storage.unavailable", …).
        var sent = Error.Unavailable("storage.unavailable", "Store 'invoices' is unavailable; the upload can be retried later.");
        var response = BuildProblemDetailsResponse(
            HttpStatusCode.ServiceUnavailable,
            body: $$"""
                {"type":"https://httpstatuses.io/503","title":"{{sent.Code}}","errorCode":"{{sent.Code}}","detail":"{{sent.Message}}","status":503}
                """);

        // Act
        var received = await ProblemDetailsDeserializer.DeserializeAsync(response, CancellationToken.None);

        // Assert — equal by value: same code, message and type on both sides of the wire.
        received.Should().Be(sent);
        received.Type.Should().Be(ErrorType.Unavailable);
    }

    [Fact]
    public async Task DeserializeAsync_504ProblemDetails_RoundTripsAsTheSameTimeoutError()
    {
        // Arrange — what the server's HTTP boundary writes for Error.Timeout("search.timeout", …).
        var sent = Error.Timeout("search.timeout", "Operation 'search' timed out after 00:00:30.");
        var response = BuildProblemDetailsResponse(
            HttpStatusCode.GatewayTimeout,
            body: $$"""
                {"type":"https://httpstatuses.io/504","title":"{{sent.Code}}","errorCode":"{{sent.Code}}","detail":"{{sent.Message}}","status":504}
                """);

        // Act
        var received = await ProblemDetailsDeserializer.DeserializeAsync(response, CancellationToken.None);

        // Assert
        received.Should().Be(sent);
        received.Type.Should().Be(ErrorType.Timeout);
    }

    [Fact]
    public async Task DeserializeAsync_429ProblemDetails_IsUnavailable_SoTheCallerBacksOffAndRetries()
    {
        // Arrange — a rate-limit rejection: the same call succeeds once the caller backs off.
        var response = BuildProblemDetailsResponse(
            HttpStatusCode.TooManyRequests,
            body: """{"type":"https://httpstatuses.io/429","title":"rate_limit.exceeded","errorCode":"rate_limit.exceeded","detail":"Too many requests.","status":429}""");

        // Act
        var error = await ProblemDetailsDeserializer.DeserializeAsync(response, CancellationToken.None);

        // Assert
        error.Type.Should().Be(ErrorType.Unavailable);
        error.Code.Should().Be("rate_limit.exceeded");
        error.Message.Should().Be("Too many requests.");
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
    // Non-JSON / empty / malformed bodies — never throw, still status-aware: the category comes
    // from the status through HttpStatusErrorTypeMap, the code is http.{status}
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DeserializeAsync_WithNonProblemJsonContentType_ReturnsStatusAwareError()
    {
        // Arrange
        var response = new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("<html>Error</html>", Encoding.UTF8, "text/html")
        };

        // Act
        var error = await ProblemDetailsDeserializer.DeserializeAsync(response, CancellationToken.None);

        // Assert — 500 is a defect the caller cannot act on, so it stays Unexpected.
        error.Type.Should().Be(ErrorType.Unexpected);
        error.Code.Should().Be("http.500");
        error.Message.Should().Be("HTTP 500 Internal Server Error");
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, ErrorType.Unavailable)]
    [InlineData(HttpStatusCode.TooManyRequests, ErrorType.Unavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout, ErrorType.Timeout)]
    public async Task DeserializeAsync_GatewayOutageWithHtmlBody_IsUnavailableOrTimeout_NotUnexpected(
        HttpStatusCode statusCode,
        ErrorType expectedType)
    {
        // Arrange — the common gateway outage: a load balancer or proxy answers for a service that is
        // down, rate limited or slow, with its own HTML page instead of a ProblemDetails body.
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent("<html><body><h1>503 Service Temporarily Unavailable</h1></body></html>", Encoding.UTF8, "text/html"),
        };

        // Act
        var error = await ProblemDetailsDeserializer.DeserializeAsync(response, CancellationToken.None);

        // Assert — the caller can retry it, exactly as it would a ProblemDetails outage.
        error.Type.Should().Be(expectedType);
        error.Code.Should().Be($"http.{(int)statusCode}");
        error.Message.Should().StartWith($"HTTP {(int)statusCode} ");
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, ErrorType.Unavailable)]
    [InlineData(HttpStatusCode.TooManyRequests, ErrorType.Unavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout, ErrorType.Timeout)]
    public async Task DeserializeAsync_GatewayOutageWithEmptyBody_IsUnavailableOrTimeout_NotUnexpected(
        HttpStatusCode statusCode,
        ErrorType expectedType)
    {
        // Arrange
        var response = new HttpResponseMessage(statusCode) { Content = new StringContent(string.Empty) };

        // Act
        var error = await ProblemDetailsDeserializer.DeserializeAsync(response, CancellationToken.None);

        // Assert
        error.Type.Should().Be(expectedType);
        error.Code.Should().Be($"http.{(int)statusCode}");
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, ErrorType.Validation)]
    [InlineData(HttpStatusCode.Unauthorized, ErrorType.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, ErrorType.Forbidden)]
    [InlineData(HttpStatusCode.NotFound, ErrorType.NotFound)]
    [InlineData(HttpStatusCode.Conflict, ErrorType.Conflict)]
    [InlineData(HttpStatusCode.PreconditionFailed, ErrorType.Conflict)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, ErrorType.Validation)]
    [InlineData(HttpStatusCode.UnsupportedMediaType, ErrorType.Validation)]
    [InlineData(HttpStatusCode.UnprocessableEntity, ErrorType.BusinessRule)]
    [InlineData(HttpStatusCode.PreconditionRequired, ErrorType.Validation)]
    [InlineData(HttpStatusCode.TooManyRequests, ErrorType.Unavailable)]
    [InlineData(HttpStatusCode.InternalServerError, ErrorType.Unexpected)]
    [InlineData(HttpStatusCode.BadGateway, ErrorType.Unexpected)]
    [InlineData(HttpStatusCode.ServiceUnavailable, ErrorType.Unavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout, ErrorType.Timeout)]
    [InlineData(HttpStatusCode.MethodNotAllowed, ErrorType.Unexpected)]
    public async Task DeserializeAsync_WithoutABody_TakesTheCategoryOfItsStatus_WithAnHttpStatusCode(
        HttpStatusCode statusCode,
        ErrorType expectedType)
    {
        // Arrange — the same status with and without a body must read as the same category; only the
        // code and the message differ (http.{status} and the status line when there is no body).
        var response = new HttpResponseMessage(statusCode) { Content = new StringContent(string.Empty) };

        // Act
        var error = await ProblemDetailsDeserializer.DeserializeAsync(response, CancellationToken.None);

        // Assert
        error.Type.Should().Be(expectedType);
        error.Type.Should().Be(HttpStatusErrorTypeMap.Resolve((int)statusCode));
        error.Code.Should().Be($"http.{(int)statusCode}");
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
    public async Task DeserializeAsync_WithEmptyBody_DoesNotThrow_AndReturnsStatusAwareError()
    {
        // Arrange
        var response = new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent(string.Empty)
        };

        // Act
        Func<Task<Error>> act = () => ProblemDetailsDeserializer.DeserializeAsync(response, CancellationToken.None);

        // Assert — 502 is outside the map, so it stays Unexpected.
        var error = await act.Should().NotThrowAsync();
        error.Subject.Type.Should().Be(ErrorType.Unexpected);
        error.Subject.Code.Should().Be("http.502");
    }

    [Fact]
    public async Task DeserializeAsync_WithEmptyJsonObjectBody_NoRecognizableMembers_ReturnsStatusAwareErrorOfItsStatus()
    {
        // Arrange — a well-formed JSON body that deserializes cleanly but carries none of the members
        // this deserializer maps from.
        var response = BuildProblemDetailsResponse(
            statusCode: HttpStatusCode.NotFound,
            body: "{}");

        // Act
        var error = await ProblemDetailsDeserializer.DeserializeAsync(response, CancellationToken.None);

        // Assert — treated as no body: the category of 404, the code http.404 (until P-562: Unexpected).
        error.Type.Should().Be(ErrorType.NotFound);
        error.Code.Should().Be("http.404");
    }

    [Fact]
    public async Task DeserializeAsync_OutageWithEmptyJsonObjectBody_IsUnavailable()
    {
        // Arrange — a 503 whose JSON body carries nothing this deserializer maps from.
        var response = BuildProblemDetailsResponse(
            statusCode: HttpStatusCode.ServiceUnavailable,
            body: "{}");

        // Act
        var error = await ProblemDetailsDeserializer.DeserializeAsync(response, CancellationToken.None);

        // Assert
        error.Type.Should().Be(ErrorType.Unavailable);
        error.Code.Should().Be("http.503");
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
