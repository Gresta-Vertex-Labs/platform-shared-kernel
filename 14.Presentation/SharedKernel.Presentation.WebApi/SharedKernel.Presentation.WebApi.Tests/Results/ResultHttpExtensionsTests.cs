using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using SharedKernel.Presentation.WebApi.Results;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.Results;

public class ResultHttpExtensionsTests
{
    [Fact]
    public void ToProblemDetailsResult_NonGeneric_Success_ReturnsNoContent()
    {
        var result = Result.Success();

        var httpResult = result.ToProblemDetailsResult();

        httpResult.Should().BeOfType<NoContent>();
    }

    [Fact]
    public void ToProblemDetailsResult_NonGeneric_Failure_ReturnsProblem()
    {
        var result = Result.Failure(Error.NotFound("x.not_found", "Not found."));

        var httpResult = result.ToProblemDetailsResult();

        httpResult.Should().BeAssignableTo<IStatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public void ToProblemDetailsResult_Generic_Success_DefaultsToOk()
    {
        var result = Result<int>.Success(42);

        var httpResult = result.ToProblemDetailsResult();

        httpResult.Should().BeOfType<Ok<int>>()
            .Which.Value.Should().Be(42);
    }

    [Fact]
    public void ToProblemDetailsResult_Generic_Success_WithOnSuccessProjection_UsesProjection()
    {
        var result = Result<int>.Success(42);

        var httpResult = result.ToProblemDetailsResult(value => Microsoft.AspNetCore.Http.Results.Created($"/items/{value}", value));

        httpResult.Should().BeOfType<Created<int>>();
    }

    [Fact]
    public void ToProblemDetailsResult_Generic_Failure_ReturnsProblem()
    {
        var result = Result<int>.Failure(Error.Validation("x.invalid", "Invalid."));

        var httpResult = result.ToProblemDetailsResult();

        httpResult.Should().BeAssignableTo<IStatusCodeHttpResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public void ToActionResult_NonGeneric_Success_ReturnsNoContentResult()
    {
        var result = Result.Success();

        var actionResult = result.ToActionResult();

        actionResult.Should().BeOfType<NoContentResult>();
    }

    [Fact]
    public void ToActionResult_NonGeneric_Failure_ReturnsObjectResultWithMappedStatus()
    {
        var result = Result.Failure(Error.Conflict("x.conflict", "Conflict."));

        var actionResult = result.ToActionResult();

        var objectResult = actionResult.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status409Conflict);
        objectResult.Value.Should().BeOfType<ProblemDetails>();
    }

    [Fact]
    public void ToActionResult_Generic_Success_ReturnsValueInActionResult()
    {
        var result = Result<string>.Success("hello");

        var actionResult = result.ToActionResult();

        actionResult.Value.Should().Be("hello");
    }

    [Fact]
    public void ToActionResult_Generic_Failure_ReturnsObjectResultWithMappedStatus()
    {
        var result = Result<string>.Failure(Error.Unauthorized("x.unauthorized", "Not allowed."));

        var actionResult = result.ToActionResult();

        var objectResult = actionResult.Result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status401Unauthorized);
    }

    private static Error BuildAggregateValidationError() => Error.Validation(
    [
        Error.Validation("name.required", "Name is required."),
        Error.Validation("email.invalid", "Email is invalid."),
    ]);

    [Fact]
    public void ToProblemDetailsResult_Generic_AggregateValidationFailure_Returns400WithErrorsExtension()
    {
        var result = Result<string>.Failure(BuildAggregateValidationError());

        var httpResult = result.ToProblemDetailsResult();

        var problemResult = httpResult.Should().BeOfType<ProblemHttpResult>().Subject;
        problemResult.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        var grouped = problemResult.ProblemDetails.Extensions["errors"]
            .Should().BeOfType<Dictionary<string, string[]>>().Subject;
        grouped.Should().HaveCount(2);
        grouped["name.required"].Should().BeEquivalentTo("Name is required.");
        grouped["email.invalid"].Should().BeEquivalentTo("Email is invalid.");
    }

    [Fact]
    public void ToActionResult_Generic_AggregateValidationFailure_ObjectResultCarriesErrorsExtension()
    {
        var result = Result<string>.Failure(BuildAggregateValidationError());

        var actionResult = result.ToActionResult();

        var objectResult = actionResult.Result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        var problemDetails = objectResult.Value.Should().BeOfType<ProblemDetails>().Subject;
        var grouped = problemDetails.Extensions["errors"].Should().BeOfType<Dictionary<string, string[]>>().Subject;
        grouped.Should().HaveCount(2);
    }

    [Fact]
    public void ToProblemDetailsResult_NonGeneric_AggregateValidationFailure_Returns400WithErrorsExtension()
    {
        var result = Result.Failure(BuildAggregateValidationError());

        var httpResult = result.ToProblemDetailsResult();

        var problemResult = httpResult.Should().BeOfType<ProblemHttpResult>().Subject;
        problemResult.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
        problemResult.ProblemDetails.Extensions.Should().ContainKey("errors");
    }
}
