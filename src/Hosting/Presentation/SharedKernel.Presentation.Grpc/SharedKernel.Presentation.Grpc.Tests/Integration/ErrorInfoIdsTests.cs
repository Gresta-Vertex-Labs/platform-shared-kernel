using FluentAssertions;
using Google.Rpc;
using Grpc.Core;
using SharedKernel.Presentation.Grpc.Tests.Integration.Fixtures;
using SharedKernel.Presentation.Grpc.Tests.TestSupport;
using SharedKernel.Presentation.WebApi;
using SharedKernel.Primitives.Propagation;
using Xunit;

namespace SharedKernel.Presentation.Grpc.Tests.Integration;

/// <summary>
/// SK.14.EdgeErrorContext T-10: every status this package builds carries the trace id and the correlation id of the
/// call in its <c>ErrorInfo</c> — classified, validation, timeout, unclassified and rebuilt foreign statuses alike — and
/// the correlation id is the one the response header reports. The documented exceptions: a cancelled call (nobody to
/// read it) and an authorization refusal, which the HTTP pipeline answers before the service runs with status and
/// headers only.
/// </summary>
public sealed class ErrorInfoIdsTests
{
    private const string CorrelationId = "flow-grpc-42";

    [Theory]
    [InlineData(Failures.NotFoundException)]
    [InlineData(Failures.UnavailableException)]
    [InlineData(Failures.ValidationException)]
    [InlineData(Failures.SingleValidationException)]
    [InlineData(Failures.ValidationResult)]
    [InlineData(Failures.TimeoutException)]
    [InlineData(Failures.TimeoutResult)]
    [InlineData(Failures.InternalTimeout)]
    [InlineData(Failures.Unknown)]
    [InlineData(Failures.Rpc)]
    [InlineData(Failures.UnavailableRpc)]
    [InlineData(Failures.InternalRpcWithTrailers)]
    public async Task EveryBuiltStatus_CarriesTheTraceId_AndTheCorrelationIdOfTheResponseHeader(string failure)
    {
        await using var app = await GrpcTestHost.StartAsync();
        var headers = new Metadata { { WellKnownHeaders.CorrelationId, CorrelationId } };

        var act = async () => await app.CreateClient().FailAsync(new EchoRequest { Value = failure }, headers);
        var exception = (await act.Should().ThrowAsync<RpcException>()).Which;

        var errorInfo = exception.GetRpcStatus()!.GetDetail<ErrorInfo>();
        errorInfo.Should().NotBeNull();
        errorInfo!.Metadata.Should().ContainKey(ProblemDetailsExtensionNames.TraceId).WhoseValue.Should().NotBeNullOrWhiteSpace();
        errorInfo.Metadata.Should().Contain(ProblemDetailsExtensionNames.CorrelationId, CorrelationId);
        exception.Trailers.GetValue(WellKnownHeaders.CorrelationId.ToLowerInvariant())
            .Should().Be(errorInfo.Metadata[ProblemDetailsExtensionNames.CorrelationId], "the response header and the status carry the same id");
    }

    [Fact]
    public async Task AuthorizationRefusal_CarriesTheCorrelationIdHeader_AndNoErrorInfo()
    {
        await using var app = await GrpcTestHost.StartAsync();
        var headers = TestAuthentication.SignedIn(permissions: "orders.write");
        headers.Add(WellKnownHeaders.CorrelationId, CorrelationId);

        using var call = app.CreateClient().ReadOrdersAsync(new EchoRequest(), headers);
        var act = async () => await call;
        var exception = (await act.Should().ThrowAsync<RpcException>()).Which;

        exception.StatusCode.Should().Be(StatusCode.PermissionDenied);
        exception.GetRpcStatus().Should().BeNull("the HTTP pipeline answers a refusal with status and headers only");
        var responseHeaders = await call.ResponseHeadersAsync;
        responseHeaders.GetValue(WellKnownHeaders.CorrelationId.ToLowerInvariant()).Should().Be(CorrelationId);
    }
}
