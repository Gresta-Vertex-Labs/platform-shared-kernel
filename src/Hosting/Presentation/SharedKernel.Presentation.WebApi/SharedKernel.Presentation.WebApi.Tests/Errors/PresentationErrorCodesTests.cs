using FluentAssertions;
using Xunit;

namespace SharedKernel.Presentation.WebApi.Tests.Errors;

/// <summary>The codes this package produces are a contract clients branch on; pin them.</summary>
public sealed class PresentationErrorCodesTests
{
    [Fact]
    public void Codes_HaveTheirContractValues()
    {
        PresentationErrorCodes.RequestTooLarge.Should().Be("request.too_large");
        PresentationErrorCodes.IdempotencyKeyRequired.Should().Be("idempotency.key_required");
        PresentationErrorCodes.IdempotencyKeyInvalid.Should().Be("idempotency.key_invalid");
        PresentationErrorCodes.PreconditionRequired.Should().Be("precondition.required");
        PresentationErrorCodes.PreconditionInvalid.Should().Be("precondition.invalid");
        PresentationErrorCodes.PreconditionFailed.Should().Be("precondition.failed");
        PresentationErrorCodes.InvalidValue.Should().Be("validation.invalid_value");
        PresentationErrorCodes.OriginNotAllowed.Should().Be("forbidden.origin_not_allowed");
        PresentationErrorCodes.RateLimitExceeded.Should().Be("rate_limit.exceeded");
        PresentationErrorCodes.StepUpRequired.Should().Be("unauthorized.step_up_required");
    }

    [Fact]
    public void ForStatus_IsTheCodeTheRestClientReadsBack()
    {
        PresentationErrorCodes.ForStatus(404).Should().Be("http.404");
        PresentationErrorCodes.ForStatus(405).Should().Be("http.405");
    }

    [Fact]
    public void ExtensionNames_HaveTheirContractValues()
    {
        ProblemDetailsExtensionNames.ErrorCode.Should().Be("errorCode");
        ProblemDetailsExtensionNames.Errors.Should().Be("errors");
        ProblemDetailsExtensionNames.ErrorCodes.Should().Be("errorCodes");
        ProblemDetailsExtensionNames.TraceId.Should().Be("traceId");
        ProblemDetailsExtensionNames.CorrelationId.Should().Be("correlationId");
        ProblemDetailsExtensionNames.Exception.Should().Be("exception");
    }
}
