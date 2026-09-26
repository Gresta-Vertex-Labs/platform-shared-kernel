using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
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
        // Arrange — the P-562 wire shape: "errorCode" carries Error.Code, "detail" Error.Message, and
        // "title" is the status reason phrase, never the code.
        var response = BuildProblemDetailsResponse(
            statusCode,
            body: PlatformProblemBody(statusCode, errorCode: "some.code", detail: "some message"));

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
                {"type":"https://tools.ietf.org/html/rfc9110#section-15.6.4","title":"Service Unavailable","status":503,"detail":"{{sent.Message}}","errorCode":"{{sent.Code}}"}
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
                {"type":"https://tools.ietf.org/html/rfc9110#section-15.6.5","title":"Gateway Timeout","status":504,"detail":"{{sent.Message}}","errorCode":"{{sent.Code}}"}
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
            body: """{"title":"Too Many Requests","status":429,"detail":"Too many requests.","errorCode":"rate_limit.exceeded"}""");

        // Act
        var error = await ProblemDetailsDeserializer.DeserializeAsync(response, CancellationToken.None);

        // Assert
        error.Type.Should().Be(ErrorType.Unavailable);
        error.Code.Should().Be("rate_limit.exceeded");
        error.Message.Should().Be("Too many requests.");
    }

    // -----------------------------------------------------------------------
    // Code / message resolution: errorCode, else http.{status}; detail, else "HTTP {status} error".
    // Never title (the status reason phrase since P-562, free text from a service outside the
    // platform) and never type (a URI).
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DeserializeAsync_WithErrorCode_UsesErrorCode_NotTheReasonPhraseTitle()
    {
        // Arrange — the real platform body for Error.NotFound("order.not_found", …).
        var response = BuildProblemDetailsResponse(
            statusCode: HttpStatusCode.NotFound,
            body: """
                {"type":"https://tools.ietf.org/html/rfc9110#section-15.5.5","title":"Not Found","status":404,"detail":"Order 42 was not found.","instance":"/orders/42","errorCode":"order.not_found","traceId":"00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01"}
                """);

        // Act
        var error = await ProblemDetailsDeserializer.DeserializeAsync(response, CancellationToken.None);

        // Assert — equal by value to the error the server returned.
        error.Should().Be(Error.NotFound("order.not_found", "Order 42 was not found."));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "Not Found")] // ASP.NET Core's default problem, from a service outside the platform
    [InlineData(HttpStatusCode.InternalServerError, "An error occurred while processing your request.")]
    [InlineData(HttpStatusCode.NotFound, "order.not_found")] // code-shaped, as servers before P-562 wrote it: still not read
    [InlineData(HttpStatusCode.Forbidden, "unauthorized.step_up_required")] // an upstream posing as a platform code
    [InlineData(HttpStatusCode.Conflict, "Conflict\r\nX-Injected: yes")] // free text with a line break, never a log label
    public async Task DeserializeAsync_WithTitleButNoErrorCode_UsesTheStatusCode_NeverTheTitle(
        HttpStatusCode statusCode,
        string title)
    {
        // Arrange
        var response = BuildProblemDetailsResponse(
            statusCode,
            body: $$"""{"title":{{JsonSerializer.Serialize(title)}},"status":{{(int)statusCode}},"detail":"The upstream refused the request."}""");

        // Act
        var error = await ProblemDetailsDeserializer.DeserializeAsync(response, CancellationToken.None);

        // Assert — the category and code of the status; the title is read nowhere, not even as the message.
        error.Code.Should().Be($"http.{(int)statusCode}");
        error.Type.Should().Be(HttpStatusErrorTypeMap.Resolve((int)statusCode));
        error.Message.Should().Be("The upstream refused the request.");
    }

    [Fact]
    public async Task DeserializeAsync_WithOnlyTitleAndType_IsTreatedAsNoBody()
    {
        // Arrange — a problem carrying nothing this client reads: title and type are never read.
        var response = BuildProblemDetailsResponse(
            statusCode: HttpStatusCode.ServiceUnavailable,
            body: """{"type":"https://tools.ietf.org/html/rfc9110#section-15.6.4","title":"Service Unavailable","status":503}""");

        // Act
        var error = await ProblemDetailsDeserializer.DeserializeAsync(response, CancellationToken.None);

        // Assert — the same error an empty 503 gets: a retryable outage coded by its status.
        error.Should().Be(Error.Unavailable("http.503", "HTTP 503 Service Unavailable"));
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
            body: """{"type":"looks.like.a.code","title":"Not Found","detail":"Order not found","status":404}""");

        // Act
        var error = await ProblemDetailsDeserializer.DeserializeAsync(response, CancellationToken.None);

        // Assert
        error.Code.Should().Be("http.404");
        error.Code.Should().NotBe("looks.like.a.code");
    }

    [Fact]
    public async Task DeserializeAsync_WithNoDetail_FallsBackToStatusAwareMessage_NeverTitle()
    {
        // Arrange — title is the reason phrase: it must not become the message either.
        var response = BuildProblemDetailsResponse(
            statusCode: HttpStatusCode.NotFound,
            body: """{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.5","title":"Not Found","status":404,"errorCode":"order.not_found"}""");

        // Act
        var error = await ProblemDetailsDeserializer.DeserializeAsync(response, CancellationToken.None);

        // Assert
        error.Code.Should().Be("order.not_found");
        error.Message.Should().Be("HTTP 404 error");
    }

    // -----------------------------------------------------------------------
    // Field errors: rebuilt into one Validation aggregate (P-544), on a 400 or 422 only (P-562)
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
                  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
                  "title": "Bad Request",
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
                  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
                  "title": "Bad Request",
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
            body: """{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"Bad Request","errorCode":"validation.failed","detail":"Bad request","status":400,"errors":{}}""");

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
            body: """{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.10","title":"Conflict","errorCode":"order.already_shipped","detail":"Order has already shipped","status":409}""");

        // Act
        var error = await ProblemDetailsDeserializer.DeserializeAsync(response, CancellationToken.None);

        // Assert
        error.Type.Should().Be(ErrorType.Conflict);
        error.Code.Should().Be("order.already_shipped");
        error.Message.Should().Be("Order has already shipped");
        error.Details.Should().BeEmpty();
    }

    [Fact]
    public async Task DeserializeAsync_PlatformValidationProblem_RoundTripsAsTheSameAggregate_WithEveryFieldCodeAndPath()
    {
        // Arrange — the error a handler returned, and the body the server's HTTP boundary writes for it (design D1):
        // "errorCode" is the aggregate's code, "detail" its message, and "errors"/"errorCodes" are keyed by field
        // path, or by code for an error that names no field, in the order the keys first appear.
        var sent = Error.Validation(
        [
            WithFieldPath(Error.Validation("validation.required", "Name is required."), "Name"),
            WithFieldPath(Error.Validation("validation.max_length", "Name must not exceed 100 characters."), "Name"),
            WithFieldPath(Error.Validation("validation.iban.invalid_check_digits", "IBAN check digits are not correct."), "Accounts[0].Iban"),
            Error.Validation("order.limit_exceeded", "Order exceeds the limit."),
        ]);
        var response = BuildProblemDetailsResponse(
            statusCode: HttpStatusCode.BadRequest,
            body: """
                {
                  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
                  "title": "Bad Request",
                  "status": 400,
                  "detail": "4 validation errors occurred.",
                  "instance": "/orders",
                  "errorCode": "validation.failed",
                  "errors": {
                    "Name": ["Name is required.", "Name must not exceed 100 characters."],
                    "Accounts[0].Iban": ["IBAN check digits are not correct."],
                    "order.limit_exceeded": ["Order exceeds the limit."]
                  },
                  "errorCodes": {
                    "Name": ["validation.required", "validation.max_length"],
                    "Accounts[0].Iban": ["validation.iban.invalid_check_digits"],
                    "order.limit_exceeded": ["order.limit_exceeded"]
                  },
                  "traceId": "00-0af7651916cd43dd8448eb211c80319c-b7ad6b7169203331-01",
                  "correlationId": "0af7651916cd43dd8448eb211c80319c"
                }
                """);

        // Act
        var received = await ProblemDetailsDeserializer.DeserializeAsync(response, CancellationToken.None);

        // Assert — equal by value: the aggregate's code, message and type, and every detail in order.
        received.Should().Be(sent);
        received.Type.Should().Be(ErrorType.Validation);
        received.Code.Should().Be(ErrorCodes.Validation.Failed);
        received.Details.Select(d => d.Code).Should().Equal(
            "validation.required",
            "validation.max_length",
            "validation.iban.invalid_check_digits",
            "order.limit_exceeded");

        // Field paths sit outside equality, so check them one by one; a code-keyed entry has none.
        received.Details[0].MessageArguments[ErrorArgumentNames.PropertyPath].Should().Be("Name");
        received.Details[1].MessageArguments[ErrorArgumentNames.PropertyPath].Should().Be("Name");
        received.Details[2].MessageArguments[ErrorArgumentNames.PropertyPath].Should().Be("Accounts[0].Iban");
        received.Details[3].MessageArguments.Should().BeEmpty();
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task DeserializeAsync_FieldErrorsOn400Or422_BecomeAValidationAggregate(HttpStatusCode statusCode)
    {
        // Arrange — 400 is the platform's validation status; 422 is the one many other frameworks use.
        var response = BuildProblemDetailsResponse(
            statusCode,
            body: $$"""
                {
                  "title": "{{ReasonPhrase(statusCode)}}",
                  "status": {{(int)statusCode}},
                  "detail": "2 validation errors occurred.",
                  "errorCode": "validation.failed",
                  "errors": { "Name": ["Name is required."], "Email": ["Email is invalid."] },
                  "errorCodes": { "Name": ["validation.required"], "Email": ["validation.invalid_format"] }
                }
                """);

        // Act
        var error = await ProblemDetailsDeserializer.DeserializeAsync(response, CancellationToken.None);

        // Assert — a Validation aggregate on both, so a 422 with field errors is not a BusinessRule refusal.
        error.Type.Should().Be(ErrorType.Validation);
        error.Code.Should().Be(ErrorCodes.Validation.Failed);
        error.Details.Select(d => d.Code).Should().Equal("validation.required", "validation.invalid_format");
        error.Details.Select(d => d.MessageArguments[ErrorArgumentNames.PropertyPath]).Should().Equal("Name", "Email");
        error.Details.Should().OnlyContain(d => d.Type == ErrorType.Validation);
    }

    [Fact]
    public async Task DeserializeAsync_422FromAFrameworkOutsideThePlatform_WithFieldErrors_IsAValidationAggregate()
    {
        // Arrange — the common non-platform shape: 422, no "errorCode", an "errors" map keyed by field, no "errorCodes".
        var response = BuildProblemDetailsResponse(
            statusCode: HttpStatusCode.UnprocessableEntity,
            body: """
                {
                  "type": "https://tools.ietf.org/html/rfc4918#section-11.2",
                  "title": "Unprocessable Entity",
                  "status": 422,
                  "detail": "The request has invalid fields.",
                  "errors": { "email": ["The email field is required."] }
                }
                """);

        // Act
        var error = await ProblemDetailsDeserializer.DeserializeAsync(response, CancellationToken.None);

        // Assert — without "errorCodes" the key stands in for the code, as it always has.
        error.Type.Should().Be(ErrorType.Validation);
        error.Code.Should().Be(ErrorCodes.Validation.Failed);
        error.Details.Should().ContainSingle().Which.Should().Be(Error.Validation("email", "The email field is required."));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, ErrorType.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, ErrorType.Forbidden)]
    [InlineData(HttpStatusCode.NotFound, ErrorType.NotFound)]
    [InlineData(HttpStatusCode.Conflict, ErrorType.Conflict)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, ErrorType.Validation)] // Validation by status, but never an aggregate
    [InlineData(HttpStatusCode.InternalServerError, ErrorType.Unexpected)]
    [InlineData(HttpStatusCode.ServiceUnavailable, ErrorType.Unavailable)]
    public async Task DeserializeAsync_FieldErrorsOnAnyOtherStatus_AreIgnored_AndTheStatusKeepsItsCategory(
        HttpStatusCode statusCode,
        ErrorType expectedType)
    {
        // Arrange — an "errors" map where the platform never writes one: from a service outside the platform, or a
        // compromised one. Read as a validation failure it would hide an authentication failure, a conflict or a
        // retryable outage, and hand the map's messages on to the caller's own clients.
        var response = BuildProblemDetailsResponse(
            statusCode,
            body: $$"""
                {
                  "title": "{{ReasonPhrase(statusCode)}}",
                  "status": {{(int)statusCode}},
                  "detail": "The request failed.",
                  "errorCode": "upstream.failed",
                  "errors": { "Name": ["Name is required."] },
                  "errorCodes": { "Name": ["validation.required"] }
                }
                """);

        // Act
        var error = await ProblemDetailsDeserializer.DeserializeAsync(response, CancellationToken.None);

        // Assert — one error of the status's category, built from errorCode and detail; the maps are ignored.
        error.Type.Should().Be(expectedType);
        error.Code.Should().Be("upstream.failed");
        error.Message.Should().Be("The request failed.");
        error.Details.Should().BeEmpty();
    }

    [Fact]
    public async Task DeserializeAsync_FieldErrorsAloneOnAConflict_AreTreatedAsNoBody()
    {
        // Arrange — nothing but an "errors" map on a 409: once the map is ignored, nothing usable is left.
        var response = BuildProblemDetailsResponse(
            statusCode: HttpStatusCode.Conflict,
            body: """{"status":409,"errors":{"Name":["Name is already taken."]}}""");

        // Act
        var error = await ProblemDetailsDeserializer.DeserializeAsync(response, CancellationToken.None);

        // Assert — the same error an empty 409 gets.
        error.Should().Be(Error.Conflict("http.409", "HTTP 409 Conflict"));
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

    /// <summary>
    /// A problem in the shape <c>14.Presentation</c> writes since P-562 (design D1): <c>title</c> is the status
    /// reason phrase, never the code; <c>errorCode</c> carries <see cref="Error.Code"/> and <c>detail</c>
    /// <see cref="Error.Message"/>. <c>type</c> is left out: the client never reads it.
    /// </summary>
    private static string PlatformProblemBody(HttpStatusCode statusCode, string errorCode, string detail) =>
        $$"""{"title":"{{ReasonPhrase(statusCode)}}","status":{{(int)statusCode}},"detail":"{{detail}}","errorCode":"{{errorCode}}"}""";

    /// <summary>The standard reason phrase of <paramref name="statusCode"/>, such as <c>Not Found</c>.</summary>
    private static string ReasonPhrase(HttpStatusCode statusCode)
    {
        using var response = new HttpResponseMessage(statusCode);
        return response.ReasonPhrase!;
    }

    /// <summary>The error as a validation failure of one field, the way the server's validators build it.</summary>
    private static Error WithFieldPath(Error error, string fieldPath) =>
        error with
        {
            MessageArguments = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [ErrorArgumentNames.PropertyPath] = fieldPath,
            },
        };
}
