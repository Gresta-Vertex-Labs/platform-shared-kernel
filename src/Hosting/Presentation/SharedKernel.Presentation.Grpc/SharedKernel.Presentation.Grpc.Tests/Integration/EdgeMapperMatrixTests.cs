using FluentAssertions;
using FluentAssertions.Execution;
using Grpc.Core;
using SharedKernel.Presentation.EdgeCases;
using SharedKernel.Presentation.Grpc.Tests.Integration.Fixtures;
using SharedKernel.Presentation.Grpc.Tests.TestSupport;
using SharedKernel.Presentation.WebApi;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Propagation;
using Xunit;

namespace SharedKernel.Presentation.Grpc.Tests.Integration;

/// <summary>
/// SK.14.PresentationConformance: the edge-mapper conformance matrix over a real gRPC channel. Every
/// <c>SharedKernelException</c> another domain throws, thrown and returned as a failed result, ends the call with the
/// status its error type defines and the owner's code as the <c>ErrorInfo</c> reason, with the trace and correlation
/// ids; outside Development the message of a server-category case is replaced. The unary shape covers every case, a
/// streaming shape the timeout and unavailable cases.
/// </summary>
public sealed class EdgeMapperMatrixTests
{
    private const string CorrelationId = "flow-matrix-1";

    [Theory]
    [InlineData(GrpcTestHost.Production, Failures.EdgeCase)]
    [InlineData(GrpcTestHost.Production, Failures.EdgeResult)]
    [InlineData(GrpcTestHost.Development, Failures.EdgeCase)]
    [InlineData(GrpcTestHost.Development, Failures.EdgeResult)]
    public async Task EveryCase_EndsTheUnaryCall_WithItsStatusAndCode(string environment, string prefix)
    {
        await using var app = await GrpcTestHost.StartAsync(environment);
        var client = app.CreateClient();

        foreach (var edgeCase in EdgeMapperCatalogue.Cases)
        {
            using var scope = new AssertionScope(edgeCase.Name);
            var headers = new Metadata { { WellKnownHeaders.CorrelationId, CorrelationId } };

            var act = async () => await client.FailAsync(new EchoRequest { Value = prefix + edgeCase.Name }, headers);
            var exception = (await act.Should().ThrowAsync<RpcException>()).Which;

            AssertStatus(exception, edgeCase, environment);
        }
    }

    [Theory]
    [InlineData(GrpcTestHost.Production)]
    [InlineData(GrpcTestHost.Development)]
    public async Task TheTimeoutAndUnavailableCases_EndAServerStreamAndADuplexCall_WithTheSameStatus(string environment)
    {
        await using var app = await GrpcTestHost.StartAsync(environment);
        var client = app.CreateClient();

        foreach (var edgeCase in EdgeMapperCatalogue.Cases.Where(c => c.ExpectedType is ErrorType.Timeout or ErrorType.Unavailable))
        {
            using var scope = new AssertionScope(edgeCase.Name);
            var request = new EchoRequest { Value = Failures.EdgeCase + edgeCase.Name };
            var headers = new Metadata { { WellKnownHeaders.CorrelationId, CorrelationId } };

            var serverStreaming = async () =>
            {
                using var call = client.StreamFail(request, headers);
                while (await call.ResponseStream.MoveNext(CancellationToken.None))
                {
                }
            };
            AssertStatus((await serverStreaming.Should().ThrowAsync<RpcException>()).Which, edgeCase, environment);

            var duplex = async () =>
            {
                using var call = client.DuplexFail(headers);
                await call.RequestStream.WriteAsync(request);
                await call.RequestStream.CompleteAsync();
                while (await call.ResponseStream.MoveNext(CancellationToken.None))
                {
                }
            };
            AssertStatus((await duplex.Should().ThrowAsync<RpcException>()).Which, edgeCase, environment);
        }
    }

    // The forward map and its inverse agree with the case table: the status of every case is the one its type maps to.
    [Fact]
    public void EveryCase_ExpectsTheStatusItsTypeMapsTo()
    {
        foreach (var edgeCase in EdgeMapperCatalogue.Cases)
        {
            SharedKernel.Presentation.Grpc.Errors.GrpcStatusCodeMap.Resolve(edgeCase.ExpectedType).Should().Be(Enum.Parse<StatusCode>(edgeCase.ExpectedGrpcStatus), edgeCase.Name);
        }
    }

    // A plain exception, and one that exposes an Error without deriving SharedKernelException, are defects: both end
    // the call Internal with unexpected.default, and the second one's own code never reaches the client.
    [Theory]
    [InlineData(Failures.DefectPlain)]
    [InlineData(Failures.DefectErrorBearing)]
    public async Task AnExceptionThatIsNotASharedKernelException_IsAnUnexpectedFailure_WhateverItCarries(string failure)
    {
        await using var app = await GrpcTestHost.StartAsync();

        var act = async () => await app.CreateClient().FailAsync(new EchoRequest { Value = failure });
        var exception = (await act.Should().ThrowAsync<RpcException>()).Which;

        var status = exception.ShouldHaveRichStatus(StatusCode.Internal);
        status.ErrorInfo().Reason.Should().Be(ErrorCodes.Unexpected.Default);
        status.ToString().Should().NotContain(ErrorBearingException.Code).And.NotContain("Password=secret");
    }

    private static void AssertStatus(RpcException exception, EdgeMapperCase edgeCase, string environment)
    {
        var status = exception.ShouldHaveRichStatus(Enum.Parse<StatusCode>(edgeCase.ExpectedGrpcStatus));
        var errorInfo = status.ErrorInfo();

        errorInfo.Reason.Should().Be(edgeCase.ExpectedCode);
        errorInfo.Metadata.Should().ContainKey(ProblemDetailsExtensionNames.TraceId).WhoseValue.Should().NotBeNullOrWhiteSpace();
        errorInfo.Metadata.Should().Contain(ProblemDetailsExtensionNames.CorrelationId, CorrelationId);

        if (edgeCase.IsServerCategory && environment == GrpcTestHost.Production)
        {
            status.Message.Should().NotBe(edgeCase.CreateError().Message).And.NotContain("db-7.internal");
        }
        else
        {
            status.Message.Should().Be(edgeCase.CreateError().Message);
        }
    }
}
