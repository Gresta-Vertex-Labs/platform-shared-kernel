using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.SignalR.Client;
using SharedKernel.Presentation.EdgeCases;
using SharedKernel.Presentation.SignalR.Tests.TestSupport;
using SharedKernel.Primitives.Propagation;
using Xunit;

namespace SharedKernel.Presentation.SignalR.Tests.Errors;

/// <summary>
/// SK.14.PresentationConformance: the edge-mapper conformance matrix over a live <see cref="HubConnection"/>. Every
/// <c>SharedKernelException</c> another domain throws, thrown from a hub method and returned as a failed
/// <c>Result</c>, reaches the client as <c>{code}: {message}</c> with the owner's code and the trace and correlation
/// ids as the trailer; outside Development the message of a server-category case is replaced.
/// </summary>
public sealed class EdgeMapperMatrixTests
{
    private const string CorrelationId = "flow-hub-matrix-1";

    private static readonly IReadOnlyDictionary<string, string> Headers =
        new Dictionary<string, string> { [WellKnownHeaders.CorrelationId] = CorrelationId };

    [Theory]
    [InlineData(SignalRTestHost.Production, HubPaths.Errors, "Edge")]
    [InlineData(SignalRTestHost.Production, HubPaths.Results, "EdgeResult")]
    [InlineData(SignalRTestHost.Development, HubPaths.Errors, "Edge")]
    [InlineData(SignalRTestHost.Development, HubPaths.Results, "EdgeResult")]
    public async Task EveryCase_ReachesTheClientWithItsCodeAndIds(string environment, string path, string method)
    {
        await using var app = await SignalRTestHost.StartAsync(
            endpoints =>
            {
                endpoints.MapHub<ErrorsHub>(HubPaths.Errors);
                endpoints.MapHub<ResultsHub>(HubPaths.Results);
            },
            environment: environment);
        await using var connection = await app.ConnectAsync(path, Headers);

        foreach (var edgeCase in EdgeMapperCatalogue.Cases)
        {
            using var scope = new AssertionScope(edgeCase.Name);
            var failure = await connection.InvokeExpectingFailureAsync(method, edgeCase.Name);

            var error = SignalRTestHost.ReadCodedErrorWithIds(failure, method);

            error.Code.Should().Be(edgeCase.ExpectedCode);
            error.TraceId.Should().NotBeNullOrWhiteSpace();
            error.CorrelationId.Should().Be(CorrelationId);

            var original = edgeCase.CreateError().Message;

            if (edgeCase.IsServerCategory && environment == SignalRTestHost.Production)
            {
                error.Message.Should().NotBe(original).And.NotContain("db-7.internal");
            }
            else
            {
                error.Message.Should().Be(original);
            }
        }
    }

    // A plain exception, and one that exposes an Error without deriving SharedKernelException, are defects: both reach
    // the client as unexpected.default, and the second one's own code never does.
    [Theory]
    [InlineData("plain")]
    [InlineData("error-bearing")]
    public async Task AnExceptionThatIsNotASharedKernelException_IsAnUnexpectedFailure_WhateverItCarries(string kind)
    {
        await using var app = await SignalRTestHost.StartAsync(
            endpoints => endpoints.MapHub<ErrorsHub>(HubPaths.Errors),
            environment: SignalRTestHost.Production);
        await using var connection = await app.ConnectAsync(HubPaths.Errors, Headers);

        var failure = await connection.InvokeExpectingFailureAsync("Defect", kind);

        var error = SignalRTestHost.ReadCodedErrorWithIds(failure, "Defect");
        error.Code.Should().Be(SharedKernel.Primitives.Errors.ErrorCodes.Unexpected.Default);
        failure.Message.Should().NotContain("defect.error_bearing");
    }
}
