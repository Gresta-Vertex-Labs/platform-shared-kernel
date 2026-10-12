using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Communication.Grpc.Tests.Probe;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Communication.Grpc.Tests;

/// <summary>A client that cannot get its access token: the call is never sent and ends in the same <see cref="Error"/> as over REST.</summary>
public sealed class TokenFailureTests
{
    private const string Prefix = "SharedKernel:Communication:Clients:probe:";

    [Fact]
    public async Task A_failing_provider_gives_the_unavailable_error()
    {
        await using var harness = await StartAsync();

        Result<EchoReply> reply = await harness.Client.EchoAsync(new EchoRequest { Text = "x" }).ToResultAsync();

        reply.Error.Should().BeEquivalentTo(new { Type = ErrorType.Unavailable, Code = CommunicationErrorCodes.AccessTokenUnavailable });
    }

    [Fact]
    public async Task The_channel_status_of_a_token_failure_is_pinned()
    {
        await using var harness = await StartAsync();

        Func<Task> call = async () => await harness.Client.EchoAsync(new EchoRequest { Text = "x" });

        RpcException failure = (await call.Should().ThrowAsync<RpcException>()).Which;

        // Grpc.Net.Client reports a handler exception that is not an HttpRequestException as Internal, so the channel's
        // Unavailable-only retry policy does not cover a token failure. The call still ends in the same Error.
        failure.StatusCode.Should().Be(StatusCode.Internal);
        failure.Status.DebugException.Should().BeOfType<AccessTokenUnavailableException>();
        failure.ToError().Code.Should().Be(CommunicationErrorCodes.AccessTokenUnavailable);
    }

    private static Task<GrpcHarness> StartAsync() => GrpcHarness.StartAsync(
        new Dictionary<string, string?> { [Prefix + "Retry:MaxAttempts"] = "3" },
        client => client.UseAccessTokenProvider<FailingProvider>());

    private sealed class FailingProvider : IAccessTokenProvider
    {
        public ValueTask<Result<AccessToken>> GetAccessTokenAsync(AccessTokenContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result<AccessToken>.Failure(Error.Unexpected("identity.down", "down")));
    }
}
