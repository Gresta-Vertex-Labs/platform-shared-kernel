using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Communication.Internal;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Results;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Communication;

namespace SharedKernel.Communication.Tests;

public sealed class ClientCredentialsTokenClientTests
{
    private static readonly Uri TokenEndpoint = new("https://login.example.com/oauth2/token");

    private readonly FakeClock _clock = new(new DateTimeOffset(2026, 9, 27, 10, 0, 0, TimeSpan.Zero));
    private readonly StubHttpMessageHandler _endpoint = new();

    [Fact]
    public async Task The_secret_goes_as_basic_authentication_and_the_grant_as_a_form()
    {
        TokenResponds("t1", expiresIn: 3600);

        Result<AccessToken> token = await Client().GetTokenAsync(Options(scope: "inventory.read"), null, CancellationToken.None);

        token.Value.Should().Be(new AccessToken("t1", _clock.UtcNow.AddHours(1)));
        RecordedHttpRequest request = _endpoint.Requests.Should().ContainSingle().Subject;
        request.Header("Authorization").Should().Be("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("checkout:s%3Acret")));
        request.Body.Should().Be("grant_type=client_credentials&scope=inventory.read");
    }

    [Fact]
    public async Task The_secret_goes_in_the_body_when_configured()
    {
        TokenResponds("t1", expiresIn: 3600);
        ClientCredentialsOptions options = Options();
        options.SecretTransport = ClientSecretTransport.RequestBody;
        options.Audience = "inventory";

        await Client().GetTokenAsync(options, null, CancellationToken.None);

        RecordedHttpRequest request = _endpoint.Requests.Single();
        request.Header("Authorization").Should().BeNull();
        request.Body.Should().Be("grant_type=client_credentials&audience=inventory&client_id=checkout&client_secret=s%3Acret");
    }

    [Fact]
    public async Task A_token_is_reused_until_shortly_before_it_expires()
    {
        TokenResponds("t1", expiresIn: 600);
        ClientCredentialsTokenClient client = Client();

        await client.GetTokenAsync(Options(), null, CancellationToken.None);
        _clock.Advance(TimeSpan.FromSeconds(539));
        Result<AccessToken> reused = await client.GetTokenAsync(Options(), null, CancellationToken.None);
        _clock.Advance(TimeSpan.FromSeconds(1));
        await client.GetTokenAsync(Options(), null, CancellationToken.None);

        reused.Value.Value.Should().Be("t1");
        _endpoint.Requests.Should().HaveCount(2, "the refresh margin is 60 s of the 600 s lifetime");
    }

    [Fact]
    public async Task A_short_lived_token_is_refreshed_half_way_through()
    {
        TokenResponds("t1", expiresIn: 30);
        ClientCredentialsTokenClient client = Client();

        await client.GetTokenAsync(Options(), null, CancellationToken.None);
        _clock.Advance(TimeSpan.FromSeconds(14));
        await client.GetTokenAsync(Options(), null, CancellationToken.None);
        _clock.Advance(TimeSpan.FromSeconds(1));
        await client.GetTokenAsync(Options(), null, CancellationToken.None);

        _endpoint.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task A_rejected_token_is_never_returned_again()
    {
        _endpoint.Respond(HttpMethod.Post, TokenEndpoint.AbsolutePath, (_, n) => Token($"t{n + 1}", 3600));
        ClientCredentialsTokenClient client = Client();

        await client.GetTokenAsync(Options(), null, CancellationToken.None);
        Result<AccessToken> fresh = await client.GetTokenAsync(Options(), "t1", CancellationToken.None);

        fresh.Value.Value.Should().Be("t2");
    }

    [Fact]
    public async Task Concurrent_callers_share_one_token_request()
    {
        var release = new TaskCompletionSource();
        _endpoint.Respond(HttpMethod.Post, TokenEndpoint.AbsolutePath, (_, _) =>
        {
            release.Task.Wait(TimeSpan.FromSeconds(5));
            return Token("t1", 3600);
        });
        ClientCredentialsTokenClient client = Client();

        Task<Result<AccessToken>>[] calls = [.. Enumerable.Range(0, 8).Select(_ => client.GetTokenAsync(Options(), null, CancellationToken.None).AsTask())];
        release.SetResult();
        Result<AccessToken>[] tokens = await Task.WhenAll(calls);

        tokens.Should().OnlyContain(t => t.IsSuccess && t.Value.Value == "t1");
        _endpoint.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task A_refused_request_is_an_unavailable_error_and_is_not_cached()
    {
        _endpoint.RespondJson(HttpMethod.Post, TokenEndpoint.AbsolutePath, new { error = "invalid_client" }, HttpStatusCode.Unauthorized);
        ClientCredentialsTokenClient client = Client();

        Result<AccessToken> first = await client.GetTokenAsync(Options(), null, CancellationToken.None);
        await client.GetTokenAsync(Options(), null, CancellationToken.None);

        first.Error.Code.Should().Be(CommunicationErrorCodes.AccessTokenUnavailable);
        first.Error.Message.Should().Contain("invalid_client").And.NotContain("s:cret");
        _endpoint.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task An_unreachable_endpoint_is_an_unavailable_error()
    {
        _endpoint.Throw(HttpMethod.Post, TokenEndpoint.AbsolutePath, new HttpRequestException("refused"));

        Result<AccessToken> token = await Client().GetTokenAsync(Options(), null, CancellationToken.None);

        token.Error.Code.Should().Be(CommunicationErrorCodes.AccessTokenUnavailable);
    }

    [Fact]
    public async Task A_rotated_secret_gets_a_new_token()
    {
        _endpoint.Respond(HttpMethod.Post, TokenEndpoint.AbsolutePath, (_, n) => Token($"t{n + 1}", 3600));
        ClientCredentialsTokenClient client = Client();
        ClientCredentialsOptions rotated = Options();
        rotated.ClientSecret = "new-secret";

        await client.GetTokenAsync(Options(), null, CancellationToken.None);
        Result<AccessToken> token = await client.GetTokenAsync(rotated, null, CancellationToken.None);

        token.Value.Value.Should().Be("t2");
    }

    [Fact]
    public void A_token_never_prints_its_value()
    {
        new AccessToken("secret-token").ToString().Should().Be("Bearer [redacted]");
    }

    private ClientCredentialsTokenClient Client()
    {
        var services = TestHost.Services();
        services.AddSingleton<IClock>(_clock);
        services.AddSharedKernelCommunication(TestHost.Configuration());
        services.UseStubHttpMessageHandler(ClientCredentialsTokenClient.HttpClientName, _endpoint);
        return services.BuildServiceProvider().GetRequiredService<ClientCredentialsTokenClient>();
    }

    private static ClientCredentialsOptions Options(string? scope = null) => new()
    {
        TokenEndpoint = TokenEndpoint,
        ClientId = "checkout",
        ClientSecret = "s:cret",
        Scope = scope,
    };

    private void TokenResponds(string token, int expiresIn) =>
        _endpoint.Respond(HttpMethod.Post, TokenEndpoint.AbsolutePath, (_, _) => Token(token, expiresIn));

    private static HttpResponseMessage Token(string token, int expiresIn) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(
            $$"""{"access_token":"{{token}}","token_type":"bearer","expires_in":{{expiresIn}}}""",
            Encoding.UTF8,
            "application/json"),
    };
}
