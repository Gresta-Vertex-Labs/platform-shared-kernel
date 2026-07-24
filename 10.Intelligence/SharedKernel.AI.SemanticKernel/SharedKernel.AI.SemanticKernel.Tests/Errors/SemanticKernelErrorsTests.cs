using System.ClientModel;
using FluentAssertions;
using SharedKernel.AI.SemanticKernel.Errors;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.AI.SemanticKernel.Tests.Errors;

public sealed class SemanticKernelErrorsTests
{
    private static ClientResultException CreateClientResultException(int status) =>
        new(new FakePipelineResponse(status), innerException: null);

    [Theory]
    [InlineData(401, ErrorType.Unauthorized)]
    [InlineData(403, ErrorType.Unauthorized)]
    [InlineData(404, ErrorType.NotFound)]
    [InlineData(429, ErrorType.Unexpected)]
    [InlineData(500, ErrorType.Unexpected)]
    [InlineData(503, ErrorType.Unexpected)]
    [InlineData(400, ErrorType.Unexpected)]
    public void FromException_MapsClientResultExceptionStatus_ToExpectedErrorType(int status, ErrorType expectedType)
    {
        var exception = CreateClientResultException(status);

        var error = SemanticKernelErrors.FromException(exception, "semantickernel", "TestOp", "gpt-test");

        error.Type.Should().Be(expectedType);
        error.Should().NotBe(Error.None);
    }

    [Fact]
    public void FromException_Status404_MapsToModelNotFound()
    {
        var exception = CreateClientResultException(404);

        var error = SemanticKernelErrors.FromException(exception, "semantickernel", "TestOp", "gpt-test");

        error.Code.Should().Be("intelligence.model_not_found");
    }

    [Fact]
    public void FromException_Status429_MapsToRateLimited()
    {
        var exception = CreateClientResultException(429);

        var error = SemanticKernelErrors.FromException(exception, "semantickernel", "TestOp", "gpt-test");

        error.Code.Should().Be("intelligence.rate_limited");
    }

    [Fact]
    public void FromException_MapsUnknownException_ToCompletionFailed()
    {
        var exception = new InvalidOperationException("boom");

        var error = SemanticKernelErrors.FromException(exception, "semantickernel", "TestOp", "gpt-test");

        error.Type.Should().Be(ErrorType.Unexpected);
        error.Code.Should().Be("intelligence.completion_failed");
    }
}
