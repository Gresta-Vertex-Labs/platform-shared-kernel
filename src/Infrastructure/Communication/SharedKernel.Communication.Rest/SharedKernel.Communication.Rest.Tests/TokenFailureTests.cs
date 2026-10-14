using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Communication.Internal;
using SharedKernel.Core.Exceptions;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Testing.Communication;

namespace SharedKernel.Communication.Rest.Tests;

/// <summary>A client that cannot get its access token: the call is never sent, and the pipeline treats it as a transient failure.</summary>
public sealed class TokenFailureTests
{
    private const string Prefix = "SharedKernel:Communication:Clients:inventory:";

    [Fact]
    public async Task A_failing_provider_returns_an_unavailable_result()
    {
        using var harness = Harness(out FailingProvider provider, new Dictionary<string, string?> { [Prefix + "Retry:MaxRetryAttempts"] = "0" });

        Result result = await harness.Http.SendResultAsync(new HttpRequestMessage(HttpMethod.Get, "stock"));

        result.Error.Should().BeEquivalentTo(new { Type = ErrorType.Unavailable, Code = CommunicationErrorCodes.AccessTokenUnavailable });
        harness.Stub.Requests.Should().BeEmpty();
        provider.Calls.Should().Be(1);
    }

    [Fact]
    public async Task An_unreachable_token_endpoint_returns_an_unavailable_result()
    {
        var tokenEndpoint = new StubHttpMessageHandler().Throw(HttpMethod.Post, "/token", new HttpRequestException(HttpRequestError.ConnectionError));
        using var harness = new RestHarness(
            ClientCredentialsSettings(),
            services: s => s.UseStubHttpMessageHandler(ClientCredentialsTokenClient.HttpClientName, tokenEndpoint));

        Result result = await harness.Http.SendResultAsync(new HttpRequestMessage(HttpMethod.Get, "stock"));

        result.Error.Should().BeEquivalentTo(new { Type = ErrorType.Unavailable, Code = CommunicationErrorCodes.AccessTokenUnavailable });
        harness.Stub.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task The_raw_client_throws_an_exception_an_inbound_edge_can_map()
    {
        using var harness = Harness(out _, new Dictionary<string, string?> { [Prefix + "Retry:MaxRetryAttempts"] = "0" });

        Func<Task> send = () => harness.Http.GetAsync("stock");

        Exception thrown = (await send.Should().ThrowAsync<Exception>()).Which;
        thrown.Should().BeAssignableTo<SharedKernelException>();
        thrown.Should().BeOfType<AccessTokenUnavailableException>().Which.Error.Type.Should().Be(ErrorType.Unavailable);
        harness.Stub.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task A_GET_is_retried_for_every_attempt()
    {
        using var harness = Harness(out FailingProvider provider);

        Result result = await harness.Http.SendResultAsync(new HttpRequestMessage(HttpMethod.Get, "stock"));

        result.Error.Code.Should().Be(CommunicationErrorCodes.AccessTokenUnavailable);
        provider.Calls.Should().Be(4, "one attempt and three retries");
        harness.Stub.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Without_retries_the_provider_is_asked_once()
    {
        using var harness = Harness(out FailingProvider provider, new Dictionary<string, string?> { [Prefix + "Retry:MaxRetryAttempts"] = "0" });

        await harness.Http.SendResultAsync(new HttpRequestMessage(HttpMethod.Get, "stock"));

        provider.Calls.Should().Be(1);
    }

    [Fact]
    public async Task A_POST_without_an_idempotency_key_is_not_repeated()
    {
        using var harness = Harness(out FailingProvider provider);

        Result result = await harness.Http.SendResultAsync(new HttpRequestMessage(HttpMethod.Post, "reserve"));

        result.Error.Code.Should().Be(CommunicationErrorCodes.AccessTokenUnavailable);
        provider.Calls.Should().Be(1);
        harness.Stub.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task A_POST_with_the_idempotency_key_propagated_is_retried()
    {
        using var harness = Harness(
            out FailingProvider provider,
            new Dictionary<string, string?> { [Prefix + "PropagateIdempotencyKey"] = "true" });

        await harness.Http.SendResultAsync(new HttpRequestMessage(HttpMethod.Post, "reserve"));

        provider.Calls.Should().Be(4);
    }

    [Fact]
    public async Task Consecutive_token_failures_open_the_circuit()
    {
        using var harness = Harness(out FailingProvider provider, new Dictionary<string, string?>
        {
            [Prefix + "Retry:MaxRetryAttempts"] = "0",
            [Prefix + "CircuitBreaker:MinimumThroughput"] = "2",
            [Prefix + "CircuitBreaker:FailureRatio"] = "0.5",
        });

        await harness.Http.SendResultAsync(new HttpRequestMessage(HttpMethod.Get, "stock"));
        await harness.Http.SendResultAsync(new HttpRequestMessage(HttpMethod.Get, "stock"));
        Result third = await harness.Http.SendResultAsync(new HttpRequestMessage(HttpMethod.Get, "stock"));

        third.Error.Code.Should().Be(CommunicationErrorCodes.CircuitOpen);
        provider.Calls.Should().Be(2, "the open circuit refused the third call before it asked for a token");
    }

    [Fact]
    public async Task A_disabled_circuit_never_opens_on_token_failures()
    {
        using var harness = Harness(out FailingProvider provider, new Dictionary<string, string?>
        {
            [Prefix + "Retry:MaxRetryAttempts"] = "0",
            [Prefix + "CircuitBreaker:Enabled"] = "false",
            [Prefix + "CircuitBreaker:MinimumThroughput"] = "2",
        });

        for (var i = 0; i < 5; i++)
        {
            await harness.Http.SendResultAsync(new HttpRequestMessage(HttpMethod.Get, "stock"));
        }

        provider.Calls.Should().Be(5);
    }

    [Fact]
    public async Task Hedging_tries_a_GET_more_than_once_and_a_POST_once()
    {
        using var harness = Harness(
            out FailingProvider provider,
            new Dictionary<string, string?> { [Prefix + "Hedging:Delay"] = "00:00:00.050" },
            c => c.UseHedging());

        await harness.Http.SendResultAsync(new HttpRequestMessage(HttpMethod.Get, "stock"));
        int afterGet = provider.Calls;
        await harness.Http.SendResultAsync(new HttpRequestMessage(HttpMethod.Post, "reserve"));

        afterGet.Should().BeGreaterThan(1);
        provider.Calls.Should().Be(afterGet + 1);
        harness.Stub.Requests.Should().BeEmpty();
    }

    private static Dictionary<string, string?> ClientCredentialsSettings() => new()
    {
        [Prefix + "Retry:MaxRetryAttempts"] = "0",
        [Prefix + "Authentication:Mode"] = "ClientCredentials",
        [Prefix + "Authentication:ClientCredentials:TokenEndpoint"] = "https://idp.example.test/token",
        [Prefix + "Authentication:ClientCredentials:ClientId"] = "inventory-client",
        [Prefix + "Authentication:ClientCredentials:ClientSecret"] = "test-secret",
    };

    private static RestHarness Harness(
        out FailingProvider provider,
        Dictionary<string, string?>? settings = null,
        Action<IRestClientBuilder>? configure = null)
    {
        var failing = new FailingProvider();
        provider = failing;
        return new RestHarness(
            settings,
            builder =>
            {
                builder.UseAccessTokenProvider<FailingProvider>();
                configure?.Invoke(builder);
            },
            services => services.AddKeyedSingleton<IAccessTokenProvider>(RestHarness.ClientName, failing));
    }

    private sealed class FailingProvider : IAccessTokenProvider
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public ValueTask<Result<AccessToken>> GetAccessTokenAsync(AccessTokenContext context, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return ValueTask.FromResult(Result<AccessToken>.Failure(Error.Unexpected("identity.down", "down")));
        }
    }
}
