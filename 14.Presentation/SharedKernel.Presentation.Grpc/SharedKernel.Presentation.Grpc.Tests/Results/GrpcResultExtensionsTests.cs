using FluentAssertions;
using Grpc.Core;
using SharedKernel.Presentation.Grpc.Errors;
using SharedKernel.Presentation.Grpc.Results;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using Xunit;

namespace SharedKernel.Presentation.Grpc.Tests.Results;

public class GrpcResultExtensionsTests
{
    [Fact]
    public void ToGrpcResult_NonGeneric_Success_DoesNotThrow()
    {
        var result = Result.Success();

        var act = () => result.ToGrpcResult();

        act.Should().NotThrow();
    }

    [Fact]
    public void ToGrpcResult_NonGeneric_Failure_ThrowsRpcExceptionWithMappedStatusCode()
    {
        var error = Error.NotFound("order.not_found", "Order could not be found.");
        var result = Result.Failure(error);

        var act = () => result.ToGrpcResult();

        var exception = act.Should().Throw<RpcException>().Which;
        exception.StatusCode.Should().Be(GrpcStatusCodeMap.Resolve(error.Type));
        exception.Status.Detail.Should().Be(error.Message);
    }

    [Fact]
    public void ToGrpcResult_Generic_Success_ReturnsUnwrappedValue()
    {
        var result = Result<int>.Success(42);

        var value = result.ToGrpcResult();

        value.Should().Be(42);
    }

    [Fact]
    public void ToGrpcResult_Generic_Failure_ThrowsRpcExceptionWithMappedStatusCode()
    {
        var error = Error.Forbidden("order.forbidden", "Not permitted.");
        var result = Result<int>.Failure(error);

        var act = () => result.ToGrpcResult();

        var exception = act.Should().Throw<RpcException>().Which;
        exception.StatusCode.Should().Be(StatusCode.PermissionDenied);
        exception.Status.Detail.Should().Be(error.Message);
    }

    [Theory]
    [InlineData(ErrorType.Validation, StatusCode.InvalidArgument)]
    [InlineData(ErrorType.Unauthorized, StatusCode.Unauthenticated)]
    [InlineData(ErrorType.Conflict, StatusCode.Aborted)]
    [InlineData(ErrorType.BusinessRule, StatusCode.FailedPrecondition)]
    [InlineData(ErrorType.Unexpected, StatusCode.Internal)]
    public void ToGrpcResult_Failure_AlwaysRoutesThroughGrpcStatusCodeMap(ErrorType errorType, StatusCode expected)
    {
        var error = errorType switch
        {
            ErrorType.Validation => Error.Validation("code", "message"),
            ErrorType.Unauthorized => Error.Unauthorized("code", "message"),
            ErrorType.Conflict => Error.Conflict("code", "message"),
            ErrorType.BusinessRule => Error.BusinessRule("code", "message"),
            ErrorType.Unexpected => Error.Unexpected("code", "message"),
            _ => throw new ArgumentOutOfRangeException(nameof(errorType)),
        };

        var act = () => Result<string>.Failure(error).ToGrpcResult();

        act.Should().Throw<RpcException>().Which.StatusCode.Should().Be(expected);
    }
}
