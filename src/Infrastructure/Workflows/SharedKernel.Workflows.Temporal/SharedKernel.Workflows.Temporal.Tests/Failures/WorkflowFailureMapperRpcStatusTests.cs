using FluentAssertions;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using SharedKernel.Primitives.Errors;
using SharedKernel.Workflows.Temporal.Errors;
using SharedKernel.Workflows.Temporal.Failures;
using Temporalio.Api.Common.V1;
using Temporalio.Api.ErrorDetails.V1;
using Temporalio.Exceptions;

namespace SharedKernel.Workflows.Temporal.Tests.Failures;

/// <summary>
/// The <see cref="RpcException"/> arm of <see cref="WorkflowFailureMapper.ToError"/> maps by gRPC status, one test row
/// per status, never wholesale. A deployment or contract fault stays <see cref="ErrorType.Unexpected"/>; no message
/// carries the RPC text or host. Dispatch-side errors never pass through <c>ToFailure</c>, so rule 8's retry rows are
/// not involved here.
/// </summary>
public sealed class WorkflowFailureMapperRpcStatusTests
{
    private const string FabricatedRpcText = "connect failure to temporal.internal.example:7233 for 0f6b1c2e:Order:key-9";

    public static TheoryData<RpcException.StatusCode, string, ErrorType> StatusRows() => new()
    {
        { RpcException.StatusCode.NotFound, "workflow.not_found", ErrorType.NotFound },
        { RpcException.StatusCode.DeadlineExceeded, "workflow.request_timed_out", ErrorType.Timeout },
        { RpcException.StatusCode.Unavailable, "workflow.service_unavailable", ErrorType.Unavailable },
        { RpcException.StatusCode.ResourceExhausted, "workflow.service_unavailable", ErrorType.Unavailable },
        { RpcException.StatusCode.Aborted, "workflow.service_unavailable", ErrorType.Unavailable },
        { RpcException.StatusCode.PermissionDenied, "workflow.rpc_failed", ErrorType.Unexpected },
        { RpcException.StatusCode.Unauthenticated, "workflow.rpc_failed", ErrorType.Unexpected },
        { RpcException.StatusCode.Internal, "workflow.rpc_failed", ErrorType.Unexpected },
        { RpcException.StatusCode.Unknown, "workflow.rpc_failed", ErrorType.Unexpected },
        { RpcException.StatusCode.InvalidArgument, "workflow.rpc_failed", ErrorType.Unexpected },
        { RpcException.StatusCode.FailedPrecondition, "workflow.rpc_failed", ErrorType.Unexpected },
        { RpcException.StatusCode.Unimplemented, "workflow.rpc_failed", ErrorType.Unexpected },
        { RpcException.StatusCode.DataLoss, "workflow.rpc_failed", ErrorType.Unexpected },
        { RpcException.StatusCode.OutOfRange, "workflow.rpc_failed", ErrorType.Unexpected },
        { RpcException.StatusCode.Cancelled, "workflow.rpc_failed", ErrorType.Unexpected },
        { RpcException.StatusCode.AlreadyExists, "workflow.rpc_failed", ErrorType.Unexpected },
    };

    [Theory]
    [MemberData(nameof(StatusRows))]
    public void ToError_RpcStatus_MapsByStatus_WithAFixedMessage(RpcException.StatusCode status, string code, ErrorType type)
    {
        var exception = new RpcException(status, FabricatedRpcText, rawStatus: null);

        Error mapped = WorkflowFailureMapper.ToError(exception);

        mapped.Code.Should().Be(code);
        mapped.Type.Should().Be(type);
        mapped.Message.Should().NotContain("temporal.internal.example").And.NotContain("key-9").And.NotContain("0f6b1c2e");
    }

    [Fact]
    public void ToError_NotFoundWithNamespaceDetail_IsNamespaceNotFound_AndStaysUnexpected()
    {
        var exception = new RpcException(
            RpcException.StatusCode.NotFound,
            FabricatedRpcText,
            NamespaceNotFoundStatus("billing-ns"));

        Error mapped = WorkflowFailureMapper.ToError(exception);

        // A deployment fault must not surface to the end user as a 404.
        mapped.Code.Should().Be("workflow.namespace_not_found");
        mapped.Type.Should().Be(ErrorType.Unexpected);
        mapped.Message.Should().NotContain("temporal.internal.example").And.NotContain("key-9");
    }

    [Fact]
    public void ToError_NotFoundWithoutNamespaceDetail_IsWorkflowNotFound()
    {
        var exception = new RpcException(RpcException.StatusCode.NotFound, FabricatedRpcText, rawStatus: null);

        Error mapped = WorkflowFailureMapper.ToError(exception);

        mapped.Code.Should().Be(WorkflowErrors.NotFound().Code);
        mapped.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public void ToError_NotFoundWithAnotherDetail_IsWorkflowNotFound()
    {
        var status = new GrpcStatus { Code = 5, Message = FabricatedRpcText };
        status.Details.Add(Any.Pack(new WorkflowExecutionAlreadyStartedFailure { RunId = "r" }));
        var exception = new RpcException(RpcException.StatusCode.NotFound, FabricatedRpcText, status.ToByteArray());

        Error mapped = WorkflowFailureMapper.ToError(exception);

        mapped.Code.Should().Be(WorkflowErrors.NotFound().Code);
    }

    [Fact]
    public void ToError_UnmappedException_UsesAFixedMessage_NeverTheExceptionText()
    {
        Error mapped = WorkflowFailureMapper.ToError(new InvalidOperationException(FabricatedRpcText));

        mapped.Code.Should().Be(WorkflowErrorCodes.UnmappedFailure);
        mapped.Type.Should().Be(ErrorType.Unexpected);
        mapped.Message.Should().NotContain("temporal.internal.example").And.NotContain("key-9");
    }

    [Fact]
    public void ToError_AlreadyStarted_CarriesNoWorkflowIdInTheMessage()
    {
        var exception = new WorkflowAlreadyStartedException("dup 0f6b1c2e:Order:key-9", "0f6b1c2e:Order:key-9", "Order", "run");

        Error mapped = WorkflowFailureMapper.ToError(exception);

        mapped.Message.Should().NotContain("key-9").And.NotContain("0f6b1c2e");
        mapped.Type.Should().Be(ErrorType.Conflict);
    }

    private static byte[] NamespaceNotFoundStatus(string missingNamespace)
    {
        var status = new GrpcStatus { Code = 5, Message = FabricatedRpcText };
        status.Details.Add(Any.Pack(new NamespaceNotFoundFailure { Namespace = missingNamespace }));
        return status.ToByteArray();
    }
}
