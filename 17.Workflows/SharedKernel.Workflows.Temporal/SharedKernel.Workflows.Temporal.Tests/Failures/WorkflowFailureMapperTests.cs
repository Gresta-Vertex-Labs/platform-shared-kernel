using FluentAssertions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Workflows.Temporal.Errors;
using SharedKernel.Workflows.Temporal.Failures;
using Temporalio.Exceptions;

namespace SharedKernel.Workflows.Temporal.Tests.Failures;

/// <summary>
/// T-03 — the <see cref="WorkflowFailureMapper"/> table, asserted exhaustively row by row: all five
/// expected <see cref="ErrorType"/> values map to <c>nonRetryable: true</c> with <c>errorType == Error.Code</c>;
/// <see cref="ErrorType.Unexpected"/> maps to <c>nonRetryable: false</c>. Plus the inverse round trip.
/// </summary>
public sealed class WorkflowFailureMapperTests
{
    public static TheoryData<ErrorType, Error> NonRetryableRows() => new()
    {
        { ErrorType.Validation, Error.Validation("wf.validation", "invalid input") },
        { ErrorType.NotFound, Error.NotFound("wf.not_found", "missing") },
        { ErrorType.Conflict, Error.Conflict("wf.conflict", "already exists") },
        { ErrorType.Unauthorized, Error.Unauthorized("wf.unauthorized", "forbidden") },
        { ErrorType.BusinessRule, Error.BusinessRule("wf.business_rule", "rule violated") },
    };

    [Theory]
    [MemberData(nameof(NonRetryableRows))]
    public void ToFailure_ExpectedErrorTypes_AreNonRetryable_WithErrorTypeEqualToCode(ErrorType errorType, Error error)
    {
        ApplicationFailureException failure = WorkflowFailureMapper.ToFailure(error);

        failure.NonRetryable.Should().BeTrue(because: $"{errorType} is a deterministic failure that will never succeed on retry");
        failure.ErrorType.Should().Be(error.Code);
        failure.Message.Should().Be(error.Message);
    }

    [Fact]
    public void ToFailure_Unexpected_IsRetryable()
    {
        Error error = Error.Unexpected("wf.unexpected", "transient fault");

        ApplicationFailureException failure = WorkflowFailureMapper.ToFailure(error);

        failure.NonRetryable.Should().BeFalse(because: "a transient infrastructure fault is exactly what the retry policy exists for");
        failure.ErrorType.Should().Be(error.Code);
    }

    [Theory]
    [MemberData(nameof(NonRetryableRows))]
    public void ToFailure_ThenToError_RoundTripsSameCodeAndType(ErrorType errorType, Error error)
    {
        ApplicationFailureException failure = WorkflowFailureMapper.ToFailure(error);

        Error roundTripped = WorkflowFailureMapper.ToError(failure);

        roundTripped.Code.Should().Be(error.Code, because: $"a code set for {errorType} must survive the round trip and be branchable at the dispatch site");
        roundTripped.Type.Should().Be(errorType);
    }

    [Fact]
    public void ToFailure_ThenToError_Unexpected_RoundTripsSameCodeAndType()
    {
        Error error = Error.Unexpected("wf.unexpected", "transient fault");

        ApplicationFailureException failure = WorkflowFailureMapper.ToFailure(error);
        Error roundTripped = WorkflowFailureMapper.ToError(failure);

        roundTripped.Code.Should().Be(error.Code);
        roundTripped.Type.Should().Be(ErrorType.Unexpected);
    }

    [Fact]
    public void ToFailure_Result_Success_Throws()
    {
        Result success = Result.Success();

        Action act = () => WorkflowFailureMapper.ToFailure(success);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ToFailure_Result_Failure_MapsSameAsError()
    {
        Error error = Error.Conflict("wf.conflict", "already exists");
        Result failure = Result.Failure(error);

        ApplicationFailureException mapped = WorkflowFailureMapper.ToFailure(failure);

        mapped.NonRetryable.Should().BeTrue();
        mapped.ErrorType.Should().Be(error.Code);
    }

    [Fact]
    public void ToError_ApplicationFailureException_WithNoErrorType_FallsBackToApplicationFailureCode()
    {
        var failure = new ApplicationFailureException("plain message");

        Error mapped = WorkflowFailureMapper.ToError(failure);

        mapped.Code.Should().Be("workflow.application_failure");
        mapped.Message.Should().Be("plain message");
    }

    [Fact]
    public void ToError_ApplicationFailureException_NonRetryableTrue_NoDetails_FallsBackToValidation()
    {
        var failure = new ApplicationFailureException("msg", errorType: "some.code", nonRetryable: true);

        Error mapped = WorkflowFailureMapper.ToError(failure);

        mapped.Type.Should().Be(ErrorType.Validation, because: "no round-trip detail exists, so the NonRetryable-derived default applies");
        mapped.Code.Should().Be("some.code");
    }

    [Fact]
    public void ToError_ApplicationFailureException_NonRetryableFalse_NoDetails_FallsBackToUnexpected()
    {
        var failure = new ApplicationFailureException("msg", errorType: "some.code", nonRetryable: false);

        Error mapped = WorkflowFailureMapper.ToError(failure);

        mapped.Type.Should().Be(ErrorType.Unexpected);
        mapped.Code.Should().Be("some.code");
    }

    [Fact]
    public void ToError_ApplicationFailureException_UndecodableDetail_FallsBackDefensively_NeverThrows()
    {
        var failure = new ApplicationFailureException(
            "msg",
            errorType: "some.code",
            nonRetryable: true,
            details: new object?[] { "not-an-int" });

        Error mapped = WorkflowFailureMapper.ToError(failure);

        mapped.Type.Should().Be(ErrorType.Validation, because: "an undecodable detail is not this mapper's own round-trip metadata and falls back to the NonRetryable-derived default");
        mapped.Code.Should().Be("some.code");
    }

    [Fact]
    public void ToError_WorkflowFailedException_UnwrapsInnerApplicationFailure()
    {
        var inner = new ApplicationFailureException("inner failed", errorType: "wf.inner", nonRetryable: true, details: [(int)ErrorType.Conflict]);
        var wrapped = new WorkflowFailedException(inner);

        Error mapped = WorkflowFailureMapper.ToError(wrapped);

        mapped.Code.Should().Be("wf.inner");
        mapped.Type.Should().Be(ErrorType.Conflict);
    }

    [Fact]
    public void ToError_WorkflowAlreadyStartedException_MapsToAlreadyStarted()
    {
        var exception = new WorkflowAlreadyStartedException("already started", "wf-id-1", "SomeWorkflowType", "run-id-1");

        Error mapped = WorkflowFailureMapper.ToError(exception);

        mapped.Code.Should().Be(WorkflowErrors.AlreadyStarted("wf-id-1").Code);
        mapped.Type.Should().Be(ErrorType.Conflict);
    }

    [Fact]
    public void ToError_RpcException_MapsToServiceUnavailable()
    {
        var exception = new RpcException(RpcException.StatusCode.Unavailable, "service down", rawStatus: null);

        Error mapped = WorkflowFailureMapper.ToError(exception);

        mapped.Code.Should().Be(WorkflowErrors.ServiceUnavailable("x").Code);
        mapped.Type.Should().Be(ErrorType.Unexpected);
    }

    [Fact]
    public void ToError_WorkflowQueryRejectedException_MapsToQueryFailed()
    {
        var exception = new WorkflowQueryRejectedException(Temporalio.Api.Enums.V1.WorkflowExecutionStatus.Terminated);

        Error mapped = WorkflowFailureMapper.ToError(exception);

        mapped.Code.Should().Be(WorkflowErrors.QueryFailed("x").Code);
        mapped.Type.Should().Be(ErrorType.Unexpected);
    }

    [Fact]
    public void ToError_UnmappedException_FallsBackToUnexpected_NeverBlanketCatchesIntoSuccess()
    {
        var exception = new InvalidOperationException("something unrelated broke");

        Error mapped = WorkflowFailureMapper.ToError(exception);

        mapped.Type.Should().Be(ErrorType.Unexpected);
        mapped.Code.Should().Be("workflow.unmapped_failure");
        mapped.Message.Should().Be(exception.Message);
    }
}
