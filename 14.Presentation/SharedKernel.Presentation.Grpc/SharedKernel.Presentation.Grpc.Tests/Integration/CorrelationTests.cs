using System.Diagnostics;
using FluentAssertions;
using Grpc.Core;
using SharedKernel.Presentation.Grpc.Tests.Integration.Fixtures;
using SharedKernel.Presentation.Grpc.Tests.TestSupport;
using SharedKernel.Presentation.WebApi.Errors;
using SharedKernel.Primitives.Propagation;
using Xunit;

namespace SharedKernel.Presentation.Grpc.Tests.Integration;

/// <summary>
/// Design D5/D13 and B11: gRPC calls get their correlation id from the one HTTP pipeline middleware. The value a
/// service, the baggage and the error status see is the validated one — never the raw header, which the deleted gRPC
/// correlation interceptor used to re-read and write over the middleware's baggage value.
/// </summary>
public sealed class CorrelationTests
{
    [Fact]
    public async Task ValidInboundId_ReachesTheServiceAndTheBaggage()
    {
        using var listener = ListenToAspNetCore();
        await using var app = await GrpcTestHost.StartAsync();

        var reply = await app.CreateClient().GetContextAsync(new EchoRequest(), CorrelationHeader("flow-7"));

        reply.CorrelationId.Should().Be("flow-7");
        reply.BaggageCorrelationId.Should().Be("flow-7");
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("<script>")]
    public async Task B11_InvalidInboundId_IsReplaced_AndTheRawValueReachesNeitherTheServiceNorTheBaggage(string inbound)
    {
        using var listener = ListenToAspNetCore();
        await using var app = await GrpcTestHost.StartAsync();

        var reply = await app.CreateClient().GetContextAsync(new EchoRequest(), CorrelationHeader(inbound));

        reply.CorrelationId.Should().NotBe(inbound).And.MatchRegex("^[0-9a-f]{32}$");
        reply.BaggageCorrelationId.Should().Be(reply.CorrelationId);
    }

    [Fact]
    public async Task B11_ErrorStatus_CarriesTheValidatedId_NeverTheRawOne()
    {
        await using var app = await GrpcTestHost.StartAsync();
        var inbound = new string('a', 129);

        var act = async () => await app.CreateClient().FailAsync(
            new EchoRequest { Value = Failures.NotFoundException },
            CorrelationHeader(inbound));

        var exception = (await act.Should().ThrowAsync<RpcException>()).Which;
        exception.ShouldHaveRichStatus(StatusCode.NotFound).ErrorInfo().Metadata[ProblemDetailsExtensionNames.CorrelationId]
            .Should().NotBe(inbound).And.MatchRegex("^[0-9a-f]{32}$");
    }

    private static Metadata CorrelationHeader(string value) => new() { { WellKnownHeaders.CorrelationId, value } };

    private static ActivityListener ListenToAspNetCore()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Microsoft.AspNetCore",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }
}
