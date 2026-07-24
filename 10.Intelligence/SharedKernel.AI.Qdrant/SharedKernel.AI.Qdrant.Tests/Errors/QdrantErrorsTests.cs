using FluentAssertions;
using Grpc.Core;
using Qdrant.Client;
using SharedKernel.AI.Qdrant.Errors;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.AI.Qdrant.Tests.Errors;

public sealed class QdrantErrorsTests
{
    [Theory]
    [InlineData(StatusCode.NotFound, ErrorType.NotFound)]
    [InlineData(StatusCode.AlreadyExists, ErrorType.Conflict)]
    [InlineData(StatusCode.InvalidArgument, ErrorType.Validation)]
    [InlineData(StatusCode.FailedPrecondition, ErrorType.Validation)]
    [InlineData(StatusCode.PermissionDenied, ErrorType.Unauthorized)]
    [InlineData(StatusCode.Unauthenticated, ErrorType.Unauthorized)]
    [InlineData(StatusCode.ResourceExhausted, ErrorType.Unexpected)]
    [InlineData(StatusCode.Unavailable, ErrorType.Unexpected)]
    [InlineData(StatusCode.DeadlineExceeded, ErrorType.Unexpected)]
    [InlineData(StatusCode.Internal, ErrorType.Unexpected)]
    public void FromException_MapsRpcStatusCode_ToExpectedErrorType(StatusCode statusCode, ErrorType expectedType)
    {
        var exception = new RpcException(new Status(statusCode, "boom"));

        var error = QdrantErrors.FromException(exception, "qdrant", "TestOp", "test-collection");

        error.Type.Should().Be(expectedType);
        error.Should().NotBe(Error.None);
    }

    [Fact]
    public void FromException_MapsQdrantException_ToUnexpected()
    {
        var exception = new QdrantException("something went wrong");

        var error = QdrantErrors.FromException(exception, "qdrant", "TestOp", "test-collection");

        error.Type.Should().Be(ErrorType.Unexpected);
    }

    [Fact]
    public void FromException_MapsUnknownException_ToUnexpected()
    {
        var exception = new InvalidOperationException("unexpected failure");

        var error = QdrantErrors.FromException(exception, "qdrant", "TestOp", "test-collection");

        error.Type.Should().Be(ErrorType.Unexpected);
    }
}
