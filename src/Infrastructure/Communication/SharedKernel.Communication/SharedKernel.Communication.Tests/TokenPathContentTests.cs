using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.Communication.Internal;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Testing.Communication;
using SharedKernel.Testing.Logging;

namespace SharedKernel.Communication.Tests;

/// <summary>Nothing from the remote side or the configuration reaches a log record or an <see cref="Error"/> on the token path.</summary>
public sealed class TokenPathContentTests
{
    private const string Host = "idp.example.test";
    private const string ClientId = "canary-client-id";
    private const string Secret = "canary-secret";
    private const string Hostile = "<script>alert(1)</script>";

    private static readonly string[] Forbidden = [Host, ClientId, Secret, "<script>"];

    private readonly StubHttpMessageHandler _service = new();
    private readonly StubHttpMessageHandler _tokenEndpoint = new();

    [Fact]
    public async Task A_successful_fetch_logs_the_client_name_only()
    {
        _service.RespondStatus(HttpMethod.Get, "/x", HttpStatusCode.OK);
        _tokenEndpoint.RespondJson(HttpMethod.Post, "/token", new { access_token = "t1", token_type = "Bearer", expires_in = 3600 });

        (Exception? failure, IReadOnlyList<LogRecord> records) = await SendAsync(ClientCredentials());

        failure.Should().BeNull();
        records.ShouldHaveLogged(new EventId(11000), LogLevel.Debug).Message.Should().Contain("'orders'");
        records.ShouldNotLeak([.. Forbidden, "t1"]);
    }

    [Fact]
    public async Task A_refusal_keeps_a_registered_oauth_error_and_logs_the_client_name()
    {
        _tokenEndpoint.RespondJson(HttpMethod.Post, "/token", new { error = "invalid_client", error_description = $"{Host} said no" }, HttpStatusCode.Unauthorized);

        (Exception? failure, IReadOnlyList<LogRecord> records) = await SendAsync(ClientCredentials());

        AccessTokenUnavailableException token = failure.Should().BeOfType<AccessTokenUnavailableException>().Subject;
        token.Error.Message.Should().Contain("invalid_client").And.Contain("401");
        records.ShouldHaveLogged(new EventId(11001), LogLevel.Warning).Message.Should().Contain("'orders'").And.Contain("invalid_client");
        records.ShouldNotLeak(Forbidden);
        token.Error.Message.Should().NotContainAny(Forbidden);
        token.Message.Should().NotContainAny(Forbidden);
    }

    [Fact]
    public async Task A_hostile_oauth_error_becomes_unrecognized_in_the_log_and_the_error()
    {
        _tokenEndpoint.RespondJson(HttpMethod.Post, "/token", new { error = Hostile }, HttpStatusCode.BadRequest);

        (Exception? failure, IReadOnlyList<LogRecord> records) = await SendAsync(ClientCredentials());

        AccessTokenUnavailableException token = failure.Should().BeOfType<AccessTokenUnavailableException>().Subject;
        token.Error.Message.Should().Contain("unrecognized");
        records.ShouldHaveLogged(new EventId(11001)).Message.Should().Contain("unrecognized");
        records.ShouldNotLeak(Forbidden);
        token.Error.Message.Should().NotContainAny(Forbidden);
    }

    [Fact]
    public async Task An_unreachable_token_endpoint_logs_the_client_name_and_the_exception()
    {
        _tokenEndpoint.Throw(HttpMethod.Post, "/token", new HttpRequestException(HttpRequestError.ConnectionError));

        (Exception? failure, IReadOnlyList<LogRecord> records) = await SendAsync(ClientCredentials());

        AccessTokenUnavailableException token = failure.Should().BeOfType<AccessTokenUnavailableException>().Subject;
        token.Error.Type.Should().Be(ErrorType.Unavailable);
        records.ShouldHaveLoggedWithException(new EventId(11002), typeof(HttpRequestException)).Message.Should().Contain("'orders'");
        records.ShouldNotLeak(Forbidden);
        token.Error.Message.Should().NotContainAny(Forbidden);
    }

    [Fact]
    public async Task A_provider_failure_never_forwards_the_providers_message()
    {
        var options = new ClientAuthenticationOptions { Mode = ClientAuthenticationMode.AccessTokenProvider };

        (Exception? failure, IReadOnlyList<LogRecord> records) = await SendAsync(options, new HostNamingProvider());

        AccessTokenUnavailableException token = failure.Should().BeOfType<AccessTokenUnavailableException>().Subject;
        token.Error.Message.Should().Be("The access token provider could not supply a token.");
        token.Message.Should().NotContainAny(Forbidden);
        records.ShouldNotLeak(Forbidden);
    }

    private static ClientAuthenticationOptions ClientCredentials() => new()
    {
        Mode = ClientAuthenticationMode.ClientCredentials,
        ClientCredentials = { TokenEndpoint = new Uri($"https://{Host}/token"), ClientId = ClientId, ClientSecret = Secret },
    };

    private async Task<(Exception? Failure, IReadOnlyList<LogRecord> Records)> SendAsync(
        ClientAuthenticationOptions options,
        IAccessTokenProvider? provider = null)
    {
        var services = new ServiceCollection();
        services.AddInMemoryLoggerFactory();
        services.AddSharedKernelCommunication(TestHost.Configuration());
        services.UseStubHttpMessageHandler(ClientCredentialsTokenClient.HttpClientName, _tokenEndpoint);
        services.AddInMemoryLoggerFactory();
        if (provider is not null)
        {
            services.AddKeyedSingleton("orders", provider);
        }

        await using ServiceProvider serviceProvider = services.BuildServiceProvider();
        using var invoker = new HttpMessageInvoker(new ClientAuthenticationHandler("orders", () => options, serviceProvider, serviceProvider.GetRequiredService<ILogger<ClientAuthenticationHandler>>()) { InnerHandler = _service });
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://orders/x");

        Exception? failure = null;
        try
        {
            using HttpResponseMessage response = await invoker.SendAsync(request, CancellationToken.None);
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        var factory = (InMemoryLoggerFactory)serviceProvider.GetRequiredService<ILoggerFactory>();
        return (failure, [.. factory.Loggers.Values.SelectMany(l => l.Records)]);
    }

    private sealed class HostNamingProvider : IAccessTokenProvider
    {
        public ValueTask<Result<AccessToken>> GetAccessTokenAsync(AccessTokenContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult(Result<AccessToken>.Failure(Error.Unexpected("identity.down", $"https://{Host}/token returned 500 for {ClientId}")));
    }
}
