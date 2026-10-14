using FluentAssertions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Workflows.Temporal.Errors;
using SharedKernel.Workflows.Temporal.Failures;
using Temporalio.Exceptions;

namespace SharedKernel.Workflows.Temporal.Tests.Failures;

/// <summary>
/// The mapper reports whether a row matched (so a dispatch site can log an unmapped failure with its exception), reads
/// the stashed <see cref="ErrorType"/> by type instead of by a blanket catch, and classifies the SDK failed-connect
/// exception as a transport fault.
/// </summary>
public sealed class WorkflowFailureMapperConformanceTests
{
    private sealed class UnmappedTemporalException(string message) : TemporalException(message)
    {
    }

    [Fact]
    public void TryToError_ATemporalFailureWithNoRow_ReturnsFalse_AndTheFixedUnmappedError()
    {
        bool mapped = WorkflowFailureMapper.TryToError(new UnmappedTemporalException("secret detail"), out Error error);

        mapped.Should().BeFalse();
        error.Should().Be(WorkflowErrors.UnmappedFailure());
        error.Message.Should().NotContain("secret detail");
    }

    [Fact]
    public void TryToError_AMappedFailure_ReturnsTrue()
    {
        WorkflowFailureMapper.TryToError(new WorkflowAlreadyStartedException("dup", "wf", "Type", "run"), out Error error)
            .Should().BeTrue();

        error.Code.Should().Be(WorkflowErrorCodes.AlreadyStarted);
    }

    [Fact]
    public void ToError_AFailureWhoseFirstDetailIsNotTheStashedInt_FallsBackToTheNonRetryableDerivedType()
    {
        var nonRetryable = new ApplicationFailureException("m", errorType: "custom.code", nonRetryable: true, details: ["not an int"]);
        var retryable = new ApplicationFailureException("m", errorType: "custom.code", nonRetryable: false, details: ["not an int"]);

        WorkflowFailureMapper.ToError(nonRetryable).Type.Should().Be(ErrorType.Validation);
        WorkflowFailureMapper.ToError(retryable).Type.Should().Be(ErrorType.Unexpected);
        WorkflowFailureMapper.ToError(nonRetryable).Code.Should().Be("custom.code");
    }

    [Fact]
    public void ToError_AFailureWithNoDetails_FallsBackToTheNonRetryableDerivedType()
    {
        WorkflowFailureMapper.ToError(new ApplicationFailureException("m", errorType: "c", nonRetryable: true)).Type
            .Should().Be(ErrorType.Validation);
    }

    [Fact]
    public void ToError_AFailureWithNoErrorType_UsesTheApplicationFailureCode()
    {
        WorkflowFailureMapper.ToError(new ApplicationFailureException("m", errorType: null)).Code
            .Should().Be(WorkflowErrorCodes.ApplicationFailure);
    }

    [Fact]
    public void TryToError_TheSdkFailedConnectException_IsServiceUnavailable_WithoutItsText()
    {
        var connect = new InvalidOperationException("Connection failed: Server connection error: tcp connect error 10.0.0.1:7233");

        WorkflowFailureMapper.TryToError(connect, out Error error).Should().BeTrue();

        error.Code.Should().Be(WorkflowErrorCodes.ServiceUnavailable);
        error.Type.Should().Be(ErrorType.Unavailable);
        error.Message.Should().NotContain("10.0.0.1");
    }

    [Fact]
    public void TryToError_AnyOtherInvalidOperationException_IsNotClassified()
    {
        WorkflowFailureMapper.TryToError(new InvalidOperationException("a defect"), out Error error).Should().BeFalse();

        error.Code.Should().Be(WorkflowErrorCodes.UnmappedFailure);
    }
}
