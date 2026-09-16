using FluentAssertions;
using Microsoft.AspNetCore.Http;
using SharedKernel.Core.Exceptions;
using SharedKernel.Presentation.WebApi.Errors;
using SharedKernel.Primitives.Errors;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.Errors;

public class ValidationProblemDetailsExtensionsTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ToProblemDetails_NFieldErrorsWithDistinctCodes_ExtensionsErrorsCountMatchesErrorsCountExactly(int errorCount)
    {
        var errors = Enumerable.Range(0, errorCount)
            .Select(i => Error.Validation($"field{i}.invalid", $"Field {i} is invalid."))
            .ToList();
        var exception = new ValidationException(errors);

        var problemDetails = exception.ToProblemDetails();

        var grouped = problemDetails.Extensions["errors"].Should().BeOfType<Dictionary<string, string[]>>().Subject;
        grouped.Should().HaveCount(exception.Errors.Count);
    }

    [Fact]
    public void ToProblemDetails_MultipleErrorsSharingOneFieldCode_GroupsMessagesUnderThatCode()
    {
        var exception = new ValidationException(
        [
            Error.Validation("name.required", "Name is required."),
            Error.Validation("name.required", "Name must not exceed 50 characters."),
            Error.Validation("email.invalid", "Email is invalid."),
        ]);

        var problemDetails = exception.ToProblemDetails();

        var grouped = problemDetails.Extensions["errors"].Should().BeOfType<Dictionary<string, string[]>>().Subject;
        grouped.Should().HaveCount(2);
        grouped["name.required"].Should().BeEquivalentTo(
            "Name is required.",
            "Name must not exceed 50 characters.");
        grouped["email.invalid"].Should().BeEquivalentTo("Email is invalid.");
    }

    [Fact]
    public void ToProblemDetails_StatusAndTypeResolveViaUnchangedValidationMapping()
    {
        var exception = new ValidationException([Error.Validation("name.required", "Name is required.")]);

        var problemDetails = exception.ToProblemDetails();

        problemDetails.Status.Should().Be(ErrorTypeStatusCodeMap.Resolve(ErrorType.Validation));
        problemDetails.Status.Should().Be(StatusCodes.Status400BadRequest);
        problemDetails.Type.Should().Be("https://httpstatuses.io/400");
    }

    [Fact]
    public void ToProblemDetails_TraceIdPopulatesIdenticallyToSingleErrorPath()
    {
        var httpContext = new DefaultHttpContext { TraceIdentifier = "trace-abc" };
        var exception = new ValidationException([Error.Validation("name.required", "Name is required.")]);
        var singleErrorEquivalent = exception.Error.ToProblemDetails(httpContext);

        var problemDetails = exception.ToProblemDetails(httpContext);

        problemDetails.Extensions["traceId"].Should().Be(singleErrorEquivalent.Extensions["traceId"]);
        problemDetails.Extensions["traceId"].Should().Be("trace-abc");
    }

    [Fact]
    public void ToProblemDetails_BaseShape_MatchesSingleErrorPathForThePrimaryError()
    {
        // ValidationException's inherited Error property is Errors[0] — the multi-error extension
        // reuses ErrorProblemDetailsExtensions for Title/Detail/Status/Type/errorCode, so those
        // fields must be identical to calling the single-Error path directly on that same Error.
        var primary = Error.Validation("name.required", "Name is required.");
        var secondary = Error.Validation("email.invalid", "Email is invalid.");
        var exception = new ValidationException([primary, secondary]);
        var expected = primary.ToProblemDetails();

        var actual = exception.ToProblemDetails();

        actual.Title.Should().Be(expected.Title);
        actual.Detail.Should().Be(expected.Detail);
        actual.Status.Should().Be(expected.Status);
        actual.Type.Should().Be(expected.Type);
        actual.Extensions["errorCode"].Should().Be(expected.Extensions["errorCode"]);
    }

    [Fact]
    public void ToProblemDetails_ErrorsExtension_IsByteIdenticalToTheResultAggregateValidationPath()
    {
        // Same field errors, two independent entry points: a thrown ValidationException (the
        // exception-boundary path) and Result.Failure(Error.Validation(errors)) (the in-process
        // Result path, 05.Application's ValidationBehavior). Both must route through
        // LocalizedDetailResolver.BuildErrorsExtension, so Extensions["errors"] is identical
        // regardless of which path produced the ProblemDetails (P-544).
        Error[] fieldErrors =
        [
            Error.Validation("name.required", "Name is required."),
            Error.Validation("name.required", "Name must not exceed 50 characters."),
            Error.Validation("email.invalid", "Email is invalid."),
        ];
        var exception = new ValidationException(fieldErrors);
        var aggregateError = Error.Validation(fieldErrors);

        var fromException = exception.ToProblemDetails();
        var fromResult = aggregateError.ToProblemDetails();

        var exceptionErrors = fromException.Extensions["errors"].Should().BeOfType<Dictionary<string, string[]>>().Subject;
        var resultErrors = fromResult.Extensions["errors"].Should().BeOfType<Dictionary<string, string[]>>().Subject;
        resultErrors.Should().BeEquivalentTo(exceptionErrors);
    }
}
