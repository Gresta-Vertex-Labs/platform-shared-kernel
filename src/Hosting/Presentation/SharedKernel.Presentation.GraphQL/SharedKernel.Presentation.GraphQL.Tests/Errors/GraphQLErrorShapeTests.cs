using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using SharedKernel.Presentation.GraphQL.Tests.TestSupport;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Propagation;

namespace SharedKernel.Presentation.GraphQL.Tests.Errors;

/// <summary>
/// SK.14.EdgeErrorContext T-07: over real HTTP (<c>MapGraphQL</c>) every GraphQL error has the members of an HTTP
/// problem, from the same decisions: the status of Core's table, the framework's type and title, the client message,
/// the code, the trace id and the correlation id, field errors keyed like HTTP's, and exception text only in
/// Development.
/// </summary>
public sealed class GraphQLErrorShapeTests
{
    private const string TypePrefix = "https://tools.ietf.org/html/";

    /// <summary>The literal table: the status every <see cref="ErrorType"/> is answered with, over HTTP and GraphQL alike.</summary>
    public static TheoryData<ErrorType, int, bool> Statuses => new()
    {
        { ErrorType.Validation, StatusCodes.Status400BadRequest, false },
        { ErrorType.Unauthorized, StatusCodes.Status401Unauthorized, false },
        { ErrorType.Forbidden, StatusCodes.Status403Forbidden, false },
        { ErrorType.NotFound, StatusCodes.Status404NotFound, false },
        { ErrorType.Conflict, StatusCodes.Status409Conflict, false },
        { ErrorType.BusinessRule, StatusCodes.Status422UnprocessableEntity, false },
        { ErrorType.Unexpected, StatusCodes.Status500InternalServerError, true },
        { ErrorType.Unavailable, StatusCodes.Status503ServiceUnavailable, true },
        { ErrorType.Timeout, StatusCodes.Status504GatewayTimeout, true },
    };

    [Theory]
    [MemberData(nameof(Statuses))]
    public async Task EveryErrorType_ThrownAsASharedKernelException_IsAnsweredWithTheStatusOfCoresTable(
        ErrorType type,
        int status,
        bool serverCategory)
    {
        await using var app = await GraphQLTestHost.StartAsync();

        using var result = await app.PostAsync($"{{ fail(type: \"{type}\") }}");

        var extensions = result.Extensions();
        extensions.GetProperty("status").GetInt32().Should().Be(status);
        extensions.GetProperty("errorCode").GetString().Should().Be($"test.{type.ToString().ToLowerInvariant()}");
        extensions.GetProperty("title").GetString().Should().Be(ExpectedTitle(status), "the title is the framework's for the status, never the code or the message");
        extensions.GetProperty("type").GetString().Should().StartWith(TypePrefix);
        extensions.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
        extensions.GetProperty("correlationId").GetString().Should().Be(GraphQLTestHost.CorrelationId);

        var detail = extensions.GetProperty("detail").GetString();
        result.Error().GetProperty("message").GetString().Should().Be(detail, "the top-level message is the client message too");

        if (serverCategory)
        {
            detail.Should().NotContain("hunter2", "a server error's message describes internals");
            result.Text.Should().NotContain("hunter2").And.NotContain("db.internal");
        }
        else
        {
            detail.Should().Be($"{type} failed at {GraphQLTestHost.Canary}.");
        }
    }

    // The framework's title for the status: the reason phrase, except 500, whose default title is its own sentence.
    private static string ExpectedTitle(int status) =>
        status == StatusCodes.Status500InternalServerError
            ? "An error occurred while processing your request."
            : ReasonPhrases.GetReasonPhrase(status);

    [Fact]
    public void TheLiteralTable_CoversEveryErrorTypeAndIsCoresTable()
    {
        var covered = Statuses.Select(row => (ErrorType)row[0]).ToHashSet();

        Enum.GetValues<ErrorType>().Where(type => type != ErrorType.None).Should().OnlyContain(
            type => covered.Contains(type),
            "a new ErrorType member must get a row, and therefore a status");

        foreach (var row in Statuses)
        {
            ErrorTypeStatusCodeMap.Resolve((ErrorType)row[0]).Should().Be((int)row[1]);
            ErrorPresentation.IsServerError((ErrorType)row[0]).Should().Be((bool)row[2]);
        }
    }

    [Fact]
    public async Task ValidationException_WithOneError_IsThatError()
    {
        await using var app = await GraphQLTestHost.StartAsync();

        using var result = await app.PostAsync("{ validationOne }");

        var extensions = result.Extensions();
        extensions.GetProperty("status").GetInt32().Should().Be(StatusCodes.Status400BadRequest);
        extensions.GetProperty("errorCode").GetString().Should().Be("customer.name_required");
        extensions.GetProperty("detail").GetString().Should().Be("Name is required.");
        extensions.TryGetProperty("errors", out _).Should().BeFalse("a single error with no details has no field errors");
    }

    [Fact]
    public async Task ValidationException_WithSeveralErrors_GroupsThemByFieldPath_LikeHttp()
    {
        await using var app = await GraphQLTestHost.StartAsync();

        using var result = await app.PostAsync("{ validationMany }");

        var extensions = result.Extensions();
        extensions.GetProperty("status").GetInt32().Should().Be(StatusCodes.Status400BadRequest);
        extensions.GetProperty("errorCode").GetString().Should().Be(ErrorCodes.Validation.Failed);
        extensions.GetProperty("detail").GetString().Should().Be("3 validation errors occurred.");

        var errors = extensions.GetProperty("errors");
        errors.GetProperty("Name").EnumerateArray().Select(e => e.GetString()).Should().Equal("Name is required.", "Name is too long.");
        errors.GetProperty("Email").EnumerateArray().Select(e => e.GetString()).Should().Equal("Email is invalid.");

        var codes = extensions.GetProperty("errorCodes");
        codes.GetProperty("Name").EnumerateArray().Select(e => e.GetString()).Should().Equal("customer.name_required", "customer.name_too_long");
        codes.GetProperty("Email").EnumerateArray().Select(e => e.GetString()).Should().Equal("customer.email_invalid");
    }

    [Fact]
    public async Task UnclassifiedException_OutsideDevelopment_IsGeneric_AndTheCanaryAppearsNowhere()
    {
        await using var app = await GraphQLTestHost.StartAsync();

        using var result = await app.PostAsync("{ boom }");

        var extensions = result.Extensions();
        extensions.GetProperty("status").GetInt32().Should().Be(StatusCodes.Status500InternalServerError);
        extensions.GetProperty("errorCode").GetString().Should().Be(ErrorCodes.Unexpected.Default);
        extensions.GetProperty("detail").GetString().Should().Be("An unexpected error occurred.");
        extensions.TryGetProperty("exception", out _).Should().BeFalse();
        result.Error().GetProperty("message").GetString().Should().Be("An unexpected error occurred.");
        result.Text.Should().NotContain("hunter2").And.NotContain("db.internal").And.NotContain("InvalidOperationException");
    }

    [Fact]
    public async Task UnclassifiedException_InDevelopment_ShowsTheExceptionMember()
    {
        await using var app = await GraphQLTestHost.StartAsync(GraphQLTestHost.Development);

        using var result = await app.PostAsync("{ boom }");

        var exception = result.Extensions().GetProperty("exception");
        exception.GetProperty("type").GetString().Should().Be(typeof(InvalidOperationException).FullName);
        exception.GetProperty("message").GetString().Should().Be(GraphQLTestHost.Canary);
        exception.GetProperty("stackTrace").GetString().Should().NotBeNullOrWhiteSpace();
        result.Extensions().GetProperty("detail").GetString().Should().Be("An unexpected error occurred.");
    }

    [Theory]
    [InlineData("{ timeout }")]
    [InlineData("{ internalCancellation }")]
    public async Task TimeoutInsideTheService_Is504_TimeoutDefault_WithoutItsMessage(string query)
    {
        await using var app = await GraphQLTestHost.StartAsync();

        using var result = await app.PostAsync(query);

        var extensions = result.Extensions();
        extensions.GetProperty("status").GetInt32().Should().Be(StatusCodes.Status504GatewayTimeout);
        extensions.GetProperty("errorCode").GetString().Should().Be(ErrorCodes.Timeout.Default);
        extensions.GetProperty("detail").GetString().Should().Be("The operation did not complete in time.");
        result.Text.Should().NotContain("hunter2");
    }

    [Theory]
    [InlineData("{ greeting", true)]
    [InlineData("{ nope }", false)]
    [InlineData("query { greeting(unknown: 1) }", false)]
    public async Task HotChocolatesOwnRequestError_Is400_RequestInvalid_WithItsCodeKept(string query, bool hasHotChocolateCode)
    {
        await using var app = await GraphQLTestHost.StartAsync();

        using var result = await app.PostAsync(query);

        var extensions = result.Extensions();
        extensions.GetProperty("status").GetInt32().Should().Be(StatusCodes.Status400BadRequest);
        extensions.GetProperty("errorCode").GetString().Should().Be(GraphQLErrorCodes.RequestInvalid);

        if (hasHotChocolateCode)
        {
            extensions.GetProperty("code").GetString().Should().NotBeNullOrWhiteSpace("HotChocolate's own code is kept as code");
        }

        extensions.GetProperty("title").GetString().Should().Be("Bad Request");
        extensions.GetProperty("type").GetString().Should().StartWith(TypePrefix);
        extensions.GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
        extensions.GetProperty("correlationId").GetString().Should().Be(GraphQLTestHost.CorrelationId);
        extensions.GetProperty("detail").GetString().Should().Be(result.Error().GetProperty("message").GetString());
    }

    [Fact]
    public async Task EveryErrorOfARequest_CarriesTheIds()
    {
        await using var app = await GraphQLTestHost.StartAsync();

        using var result = await app.PostAsync("{ a: boom b: timeout c: unavailable }");

        var errors = result.Root.GetProperty("errors");
        errors.GetArrayLength().Should().Be(3);

        for (var index = 0; index < 3; index++)
        {
            result.Extensions(index).GetProperty("traceId").GetString().Should().NotBeNullOrWhiteSpace();
            result.Extensions(index).GetProperty("correlationId").GetString().Should().Be(GraphQLTestHost.CorrelationId);
        }
    }

    [Fact]
    public async Task TheResponseHeaderAndTheErrorsCorrelationId_AreTheSame()
    {
        await using var app = await GraphQLTestHost.StartAsync();

        using var result = await app.PostAsync("{ boom }");

        result.Response.Headers.GetValues(WellKnownHeaders.CorrelationId).Should().ContainSingle()
            .Which.Should().Be(result.Extensions().GetProperty("correlationId").GetString());
    }
}
