using FluentAssertions;
using Google.Rpc;
using Grpc.Core;
using RpcStatus = Google.Rpc.Status;

namespace SharedKernel.Presentation.Grpc.Tests.TestSupport;

/// <summary>Reads the <c>google.rpc.Status</c> of an <see cref="RpcException"/> the way a client does.</summary>
internal static class RichStatus
{
    public static RpcStatus ShouldHaveRichStatus(this RpcException exception, StatusCode expectedCode)
    {
        exception.StatusCode.Should().Be(expectedCode);

        var status = exception.GetRpcStatus();
        status.Should().NotBeNull("every error of this package carries a google.rpc.Status");
        status!.Code.Should().Be((int)expectedCode);
        status.Message.Should().Be(exception.Status.Detail);
        return status;
    }

    public static ErrorInfo ErrorInfo(this RpcStatus status)
    {
        var errorInfo = status.GetDetail<ErrorInfo>();
        errorInfo.Should().NotBeNull("every error status carries an ErrorInfo detail");
        return errorInfo!;
    }

    public static IReadOnlyList<(string Field, string Description, string Reason)> FieldViolations(this RpcStatus status)
    {
        var badRequest = status.GetDetail<BadRequest>();
        badRequest.Should().NotBeNull("an error with field errors carries a BadRequest detail");
        return [.. badRequest!.FieldViolations.Select(violation => (violation.Field, violation.Description, violation.Reason))];
    }
}
