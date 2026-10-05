using System.Net;
using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Communication.Internal;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Testing.Communication;

namespace SharedKernel.Communication.Tests;

public sealed class ClientAuthenticationHandlerTests
{
    private readonly StubHttpMessageHandler _service = new();
    private readonly StubHttpMessageHandler _tokenEndpoint = new();

    [Fact]
    public async Task Without_a_mode_the_request_goes_as_it_is()
    {
        _service.RespondStatus(HttpMethod.Get, "/x", HttpStatusCode.OK);

        await Send(new ClientAuthenticationOptions(), HttpMethod.Get);

        _service.Requests.Single().Header("Authorization").Should().BeNull();
    }

    [Fact]
    public async Task An_API_key_is_added_unless_the_request_has_one()
    {
        _service.RespondStatus(HttpMethod.Get, "/x", HttpStatusCode.OK);
        var options = new ClientAuthenticationOptions
        {
            Mode = ClientAuthenticationMode.ApiKey,
            ApiKey = { HeaderName = "X-Api-Key", Value = "k1" },
        };

        await Send(options, HttpMethod.Get);
        await Send(options, HttpMethod.Get, request => request.Headers.Add("X-Api-Key", "mine"));

        _service.Requests.Select(r => r.Header("X-Api-Key")).Should().Equal("k1", "mine");
    }

    [Fact]
    public async Task A_client_credentials_token_is_sent_as_bearer()
    {
        _service.RespondStatus(HttpMethod.Get, "/x", HttpStatusCode.OK);
        _tokenEndpoint.RespondJson(HttpMethod.Post, "/token", new { access_token = "t1", token_type = "Bearer", expires_in = 3600 });

        await Send(ClientCredentials(), HttpMethod.Get);

        _service.Requests.Single().Header("Authorization").Should().Be("Bearer t1");
    }

    [Fact]
    public async Task A_401_is_answered_once_with_a_new_token()
    {
        _service.Respond(HttpMethod.Get, "/x", (request, _) =>
            new HttpResponseMessage(request.Headers.Authorization?.Parameter == "t2" ? HttpStatusCode.OK : HttpStatusCode.Unauthorized));
        _tokenEndpoint.Respond(HttpMethod.Post, "/token", (_, n) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($$"""{"access_token":"t{{n + 1}}","expires_in":3600}""", System.Text.Encoding.UTF8, "application/json"),
        });

        HttpResponseMessage response = await Send(ClientCredentials(), HttpMethod.Get);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _service.Requests.Select(r => r.Header("Authorization")).Should().Equal("Bearer t1", "Bearer t2");
    }

    [Fact]
    public async Task A_second_401_is_returned_as_it_is()
    {
        _service.RespondStatus(HttpMethod.Get, "/x", HttpStatusCode.Unauthorized);
        _tokenEndpoint.Respond(HttpMethod.Post, "/token", (_, n) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($$"""{"access_token":"t{{n + 1}}","expires_in":3600}""", System.Text.Encoding.UTF8, "application/json"),
        });

        HttpResponseMessage response = await Send(ClientCredentials(), HttpMethod.Get);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _service.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task A_request_with_its_own_authorization_keeps_it()
    {
        _service.RespondStatus(HttpMethod.Get, "/x", HttpStatusCode.OK);

        await Send(ClientCredentials(), HttpMethod.Get, r => r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "user-token"));

        _service.Requests.Single().Header("Authorization").Should().Be("Bearer user-token");
        _tokenEndpoint.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task No_token_means_no_request()
    {
        _tokenEndpoint.RespondJson(HttpMethod.Post, "/token", new { error = "invalid_client" }, HttpStatusCode.BadRequest);

        Func<Task> send = () => Send(ClientCredentials(), HttpMethod.Get);

        (await send.Should().ThrowAsync<AccessTokenUnavailableException>())
            .Which.Error.Code.Should().Be(CommunicationErrorCodes.AccessTokenUnavailable);
        _service.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task A_registered_provider_supplies_the_token_and_is_told_when_it_was_refused()
    {
        var provider = new CountingProvider();
        _service.Respond(HttpMethod.Get, "/x", (request, _) =>
            new HttpResponseMessage(request.Headers.Authorization?.Parameter == "p2" ? HttpStatusCode.OK : HttpStatusCode.Unauthorized));

        await Send(new ClientAuthenticationOptions { Mode = ClientAuthenticationMode.AccessTokenProvider }, HttpMethod.Get, provider: provider);

        provider.Contexts.Should().Equal(new AccessTokenContext("orders", false), new AccessTokenContext("orders", true));
    }

    [Fact]
    public async Task A_provider_failure_keeps_its_message_under_the_platform_code()
    {
        var provider = new FailingProvider();

        Func<Task> send = () => Send(new ClientAuthenticationOptions { Mode = ClientAuthenticationMode.AccessTokenProvider }, HttpMethod.Get, provider: provider);

        AccessTokenUnavailableException failure = (await send.Should().ThrowAsync<AccessTokenUnavailableException>()).Which;
        failure.Error.Code.Should().Be(CommunicationErrorCodes.AccessTokenUnavailable);
        failure.Error.Message.Should().Be("managed identity endpoint down");
        failure.Should().BeAssignableTo<HttpRequestException>();
    }

    private ClientAuthenticationOptions ClientCredentials() => new()
    {
        Mode = ClientAuthenticationMode.ClientCredentials,
        ClientCredentials = { TokenEndpoint = new Uri("https://login.example.com/token"), ClientId = "checkout", ClientSecret = "secret" },
    };

    private async Task<HttpResponseMessage> Send(
        ClientAuthenticationOptions options,
        HttpMethod method,
        Action<HttpRequestMessage>? prepare = null,
        IAccessTokenProvider? provider = null)
    {
        var services = TestHost.Services();
        services.AddSharedKernelCommunication(TestHost.Configuration());
        services.UseStubHttpMessageHandler(ClientCredentialsTokenClient.HttpClientName, _tokenEndpoint);
        if (provider is not null)
        {
            services.AddKeyedSingleton("orders", provider);
        }

        IServiceProvider serviceProvider = services.BuildServiceProvider();
        using var invoker = new HttpMessageInvoker(new ClientAuthenticationHandler("orders", () => options, serviceProvider) { InnerHandler = _service });
        using var request = new HttpRequestMessage(method, "http://orders/x");
        prepare?.Invoke(request);
        return await invoker.SendAsync(request, CancellationToken.None);
    }

    private sealed class CountingProvider : IAccessTokenProvider
    {
        public List<AccessTokenContext> Contexts { get; } = [];

        public ValueTask<Result<AccessToken>> GetAccessTokenAsync(AccessTokenContext context, CancellationToken cancellationToken)
        {
            Contexts.Add(context);
            return ValueTask.FromResult(Result<AccessToken>.Success(new AccessToken($"p{Contexts.Count}")));
        }
    }

    private sealed class FailingProvider : IAccessTokenProvider
    {
        public ValueTask<Result<AccessToken>> GetAccessTokenAsync(AccessTokenContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result<AccessToken>.Failure(Error.Unexpected("identity.down", "managed identity endpoint down")));
    }
}
