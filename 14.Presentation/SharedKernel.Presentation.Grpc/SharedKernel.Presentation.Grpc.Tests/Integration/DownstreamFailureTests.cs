using FluentAssertions;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Presentation.Grpc.Errors;
using SharedKernel.Presentation.Grpc.Tests.Integration.Fixtures;
using SharedKernel.Presentation.Grpc.Tests.TestSupport;
using SharedKernel.Presentation.WebApi.Errors;
using SharedKernel.Primitives.Propagation;
using Xunit;
using RpcStatus = Google.Rpc.Status;

namespace SharedKernel.Presentation.Grpc.Tests.Integration;

/// <summary>
/// P-562 R30 (security finding S4): an edge service calls an internal one through <c>Grpc.Net.Client</c> and lets the
/// <see cref="RpcException"/> it receives propagate. Its caller gets the status code, and the detail where it may be
/// shown — but none of the internal service's status: not its <c>ErrorInfo</c> (reason, domain, trace and correlation
/// ids), not its field violations, not its detail of a server error. Both services run in process; the internal one
/// in Development, so everything it knows is in its status, the worst case.
/// </summary>
public sealed class DownstreamFailureTests
{
    private const string EdgeDomain = "edge.example.com";

    private const string InternalDomain = "internal.example.com";

    private const string EdgeCorrelationId = "edge-flow-1";

    [Theory]
    [InlineData(Failures.Unknown, StatusCode.Internal, "An unexpected error occurred.")]
    [InlineData(Failures.UnknownWithStackText, StatusCode.Internal, "An unexpected error occurred.")]
    [InlineData(Failures.UnavailableException, StatusCode.Unavailable, "The service is temporarily unavailable. Try again later.")]
    [InlineData(Failures.TimeoutResult, StatusCode.DeadlineExceeded, "The operation did not complete in time.")]
    public async Task ServerCategory_InProduction_ReachesTheCaller_Redacted_AsTheEdgesOwnStatus(string failure, StatusCode expected, string generic)
    {
        await using var services = await TwoServices.StartAsync(GrpcTestHost.Production);

        var (external, downstream) = await services.RelayAsync(failure);

        downstream.Status.Detail.Should().NotBe(generic, "the internal service, in Development, sent its own detail");
        var status = external.ShouldHaveRichStatus(expected);
        status.Message.Should().Be(generic);
        ShouldBeTheEdgesOwnStatus(external, status, expected, downstream);
        external.EverythingTheClientSees().Should().NotContain(downstream.Status.Detail)
            .And.NotContain("Password")
            .And.NotContain("OrderRepository");
    }

    [Theory]
    [InlineData(Failures.ValidationException, StatusCode.InvalidArgument)]
    [InlineData(Failures.ValidationResult, StatusCode.InvalidArgument)]
    [InlineData(Failures.NotFoundException, StatusCode.NotFound)]
    public async Task ClientCategory_ReachesTheCaller_WithItsDetail_ButNothingElseOfTheInternalStatus(string failure, StatusCode expected)
    {
        await using var services = await TwoServices.StartAsync(GrpcTestHost.Production);

        var (external, downstream) = await services.RelayAsync(failure);

        if (expected == StatusCode.InvalidArgument)
        {
            downstream.GetRpcStatus()!.FieldViolations().Should().HaveCount(3, "the internal service sent its field paths");
        }

        var status = external.ShouldHaveRichStatus(expected);
        status.Message.Should().Be(downstream.Status.Detail, "a client category's detail is the answer, and is kept");
        ShouldBeTheEdgesOwnStatus(external, status, expected, downstream);
    }

    [Fact]
    public async Task ServerCategory_InDevelopment_ShowsTheDetail_ButStillNothingElseOfTheInternalStatus()
    {
        await using var services = await TwoServices.StartAsync(GrpcTestHost.Development);

        var (external, downstream) = await services.RelayAsync(Failures.Unknown);

        var status = external.ShouldHaveRichStatus(StatusCode.Internal);
        status.Message.Should().Be(Failures.UnknownDetail, "Development shows the detail, as for this service's own errors");
        ShouldBeTheEdgesOwnStatus(external, status, StatusCode.Internal, downstream);
    }

    private static void ShouldBeTheEdgesOwnStatus(RpcException external, RpcStatus status, StatusCode expected, RpcException downstream)
    {
        // The internal service did send a rich status of its own — what must not get through.
        var downstreamStatus = downstream.GetRpcStatus();
        downstreamStatus.Should().NotBeNull("the internal service sent a rich status");
        var downstreamInfo = downstreamStatus!.ErrorInfo();
        downstreamInfo.Domain.Should().Be(InternalDomain);

        // Only the edge's own ErrorInfo: its domain, the gRPC code as reason, its call's trace and correlation ids.
        status.Details.Should().ContainSingle("no BadRequest or any other detail of the internal service is kept");
        var errorInfo = status.ErrorInfo();
        errorInfo.Domain.Should().Be(EdgeDomain);
        errorInfo.Reason.Should().Be(GrpcErrorCodes.ForStatus(expected));
        errorInfo.Metadata.Should().Contain(ProblemDetailsExtensionNames.CorrelationId, EdgeCorrelationId);
        errorInfo.Metadata[ProblemDetailsExtensionNames.TraceId]
            .Should().NotBe(downstreamInfo.Metadata[ProblemDetailsExtensionNames.TraceId]);

        var seen = external.EverythingTheClientSees();
        seen.Should().NotContain(InternalDomain).And.NotContain(downstreamInfo.Reason);
        foreach (var value in downstreamInfo.Metadata.Values)
        {
            seen.Should().NotContain(value, "no trace or correlation id of the internal service reaches the caller");
        }

        IEnumerable<string> fieldPaths = downstreamStatus.GetDetail<Google.Rpc.BadRequest>()?.FieldViolations.Select(violation => violation.Field) ?? [];
        foreach (var fieldPath in fieldPaths)
        {
            seen.Should().NotContain(fieldPath, "no field path of the internal service reaches the caller");
        }
    }

    /// <summary>An internal service (Development, its own domain) and an edge service relaying to it.</summary>
    private sealed class TwoServices(WebApplication internalService, WebApplication edge) : IAsyncDisposable
    {
        public static async Task<TwoServices> StartAsync(string edgeEnvironment)
        {
            var internalService = await GrpcTestHost.StartAsync(
                GrpcTestHost.Development,
                configureGrpc: options => options.ErrorDomain = InternalDomain);

            var edge = await GrpcTestHost.StartAsync(
                edgeEnvironment,
                configureGrpc: options => options.ErrorDomain = EdgeDomain,
                configureBuilder: builder => builder.Services.AddSingleton(new Downstream(internalService.CreateClient())));

            return new TwoServices(internalService, edge);
        }

        /// <summary>Calls the edge's <c>Relay</c>; returns what its caller got and what it got from the internal service.</summary>
        public async Task<(RpcException External, RpcException Downstream)> RelayAsync(string failure)
        {
            var headers = new Metadata { { WellKnownHeaders.CorrelationId, EdgeCorrelationId } };
            var act = async () => await edge.CreateClient().RelayAsync(new EchoRequest { Value = failure }, headers);

            var external = (await act.Should().ThrowAsync<RpcException>()).Which;
            var downstream = edge.Services.GetRequiredService<Downstream>().Received.Should().ContainSingle().Subject;
            return (external, downstream);
        }

        public async ValueTask DisposeAsync()
        {
            await edge.DisposeAsync();
            await internalService.DisposeAsync();
        }
    }
}
