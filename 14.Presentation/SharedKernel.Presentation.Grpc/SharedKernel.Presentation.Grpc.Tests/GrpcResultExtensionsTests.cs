using FluentAssertions;
using Grpc.Core;
using SharedKernel.Presentation.Grpc.Tests.TestSupport;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using Xunit;

namespace SharedKernel.Presentation.Grpc.Tests;

/// <summary>
/// Design D13: <see cref="GrpcResultExtensions"/> return the value or throw an <see cref="RpcException"/> with the
/// rich status, synchronously and on <see cref="Task{TResult}"/>. Called outside a gRPC call (as here) the status has
/// no domain or correlation id and redacts server errors, as in production; the host tests cover the completed status.
/// </summary>
public sealed class GrpcResultExtensionsTests
{
    private static readonly Error NotFound = Error.NotFound("order.not_found", "Order 42 was not found.");

    [Fact]
    public void ThrowIfFailure_Success_DoesNotThrow()
    {
        var act = () => Result.Success().ThrowIfFailure();

        act.Should().NotThrow();
    }

    [Fact]
    public void ThrowIfFailure_Failure_ThrowsTheRichStatus()
    {
        var act = () => Result.Failure(NotFound).ThrowIfFailure();

        var status = act.Should().Throw<RpcException>().Which.ShouldHaveRichStatus(StatusCode.NotFound);
        status.Message.Should().Be("Order 42 was not found.");
        var errorInfo = status.ErrorInfo();
        errorInfo.Reason.Should().Be("order.not_found");
        errorInfo.Domain.Should().BeEmpty("the domain is added by the interceptor of the call");
    }

    [Fact]
    public async Task ThrownException_IsExactlyAnRpcException_SoExactTypeAssertionsHold()
    {
        // xUnit's Assert.Throws*<T> (and NUnit's Throws.TypeOf<T>) reject subclasses; a service's own unit tests use them.
        var exception = await Assert.ThrowsAsync<RpcException>(() => Task.FromResult(Result.Failure(NotFound)).ThrowIfFailure());
        var fromValue = Assert.Throws<RpcException>(() => Result<int>.Failure(NotFound).GetValueOrThrow());

        exception.GetType().Should().Be<RpcException>();
        fromValue.GetType().Should().Be<RpcException>();
    }

    [Fact]
    public void GetValueOrThrow_Success_ReturnsTheValue()
    {
        Result<int>.Success(42).GetValueOrThrow().Should().Be(42);
    }

    [Fact]
    public void GetValueOrThrow_Failure_ThrowsTheRichStatus()
    {
        var act = () => Result<int>.Failure(Error.Forbidden("order.forbidden", "Not permitted.")).GetValueOrThrow();

        act.Should().Throw<RpcException>().Which.ShouldHaveRichStatus(StatusCode.PermissionDenied)
            .ErrorInfo().Reason.Should().Be("order.forbidden");
    }

    [Fact]
    public void B11_ValidationFailure_CarriesEveryFieldViolation()
    {
        var name = Error.Validation("customer.name_required", "Name is required.") with
        {
            MessageArguments = new Dictionary<string, object?> { [ErrorArgumentNames.PropertyPath] = "customer.name" },
        };
        var lines = Error.Validation("order.lines_empty", "An order needs at least one line.");

        var act = () => Result<string>.Failure(Error.Validation([name, lines])).GetValueOrThrow();

        var status = act.Should().Throw<RpcException>().Which.ShouldHaveRichStatus(StatusCode.InvalidArgument);
        status.ErrorInfo().Reason.Should().Be(ErrorCodes.Validation.Failed);
        status.FieldViolations().Should().Equal(
            ("customer.name", "Name is required.", "customer.name_required"),
            ("order.lines_empty", "An order needs at least one line.", "order.lines_empty"));
    }

    [Theory]
    [InlineData(ErrorType.Unavailable, StatusCode.Unavailable, "The service is temporarily unavailable. Try again later.")]
    [InlineData(ErrorType.Timeout, StatusCode.DeadlineExceeded, "The operation did not complete in time.")]
    [InlineData(ErrorType.Unexpected, StatusCode.Internal, "An unexpected error occurred.")]
    public void ServerError_OutsideACall_IsRedacted(ErrorType type, StatusCode expected, string generic)
    {
        var error = new Error("search.unreachable", "Search cluster http://10.0.0.5:9200 is unreachable.", type);

        var act = () => Result.Failure(error).ThrowIfFailure();

        var status = act.Should().Throw<RpcException>().Which.ShouldHaveRichStatus(expected);
        status.Message.Should().Be(generic);
        status.ErrorInfo().Reason.Should().Be("search.unreachable");
    }

    [Theory]
    [InlineData(ErrorType.Validation, StatusCode.InvalidArgument)]
    [InlineData(ErrorType.Unauthorized, StatusCode.Unauthenticated)]
    [InlineData(ErrorType.Conflict, StatusCode.Aborted)]
    [InlineData(ErrorType.BusinessRule, StatusCode.FailedPrecondition)]
    [InlineData(ErrorType.Unavailable, StatusCode.Unavailable)]
    [InlineData(ErrorType.Timeout, StatusCode.DeadlineExceeded)]
    public void Failure_RoutesThroughGrpcStatusCodeMap(ErrorType type, StatusCode expected)
    {
        var act = () => Result<string>.Failure(new Error("code", "message", type)).GetValueOrThrow();

        act.Should().Throw<RpcException>().Which.StatusCode.Should().Be(expected);
    }

    [Fact]
    public async Task ThrowIfFailure_OnATask_Success_Completes()
    {
        var act = () => Task.FromResult(Result.Success()).ThrowIfFailure();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ThrowIfFailure_OnATask_Failure_ThrowsTheRichStatus()
    {
        var act = () => Task.FromResult(Result.Failure(NotFound)).ThrowIfFailure();

        (await act.Should().ThrowAsync<RpcException>()).Which.ShouldHaveRichStatus(StatusCode.NotFound)
            .ErrorInfo().Reason.Should().Be("order.not_found");
    }

    [Fact]
    public async Task GetValueOrThrow_OnATask_Success_ReturnsTheValue()
    {
        (await Task.FromResult(Result<string>.Success("ok")).GetValueOrThrow()).Should().Be("ok");
    }

    [Fact]
    public async Task GetValueOrThrow_OnATask_Failure_ThrowsTheRichStatus()
    {
        var act = () => Task.FromResult(Result<string>.Failure(NotFound)).GetValueOrThrow();

        (await act.Should().ThrowAsync<RpcException>()).Which.ShouldHaveRichStatus(StatusCode.NotFound);
    }

    [Fact]
    public async Task NullArguments_AreRejected()
    {
        var nullResult = () => ((Result<int>)null!).GetValueOrThrow();
        var nullTask = () => ((Task<Result>)null!).ThrowIfFailure();
        var nullTaskOfT = () => ((Task<Result<int>>)null!).GetValueOrThrow();

        nullResult.Should().Throw<ArgumentNullException>();
        await nullTask.Should().ThrowAsync<ArgumentNullException>();
        await nullTaskOfT.Should().ThrowAsync<ArgumentNullException>();
    }
}
