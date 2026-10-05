using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

namespace SharedKernel.Communication.Internal;

/// <summary>
/// Adds the client's credential to every request attempt: an API key header or an <c>Authorization</c> token. A request
/// that already carries the header keeps it. A 401 to a token the handler added is answered once with a new token.
/// </summary>
/// <remarks>
/// Runs inside the retry loop, so each attempt carries a token that is valid when it is sent. The options are read per
/// request, so a key or secret rotated in configuration takes effect without a restart.
/// </remarks>
internal sealed class ClientAuthenticationHandler(
    string clientName,
    Func<ClientAuthenticationOptions> options,
    IServiceProvider services) : DelegatingHandler
{
    private readonly ILogger _logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("SharedKernel.Communication.Authentication");

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ClientAuthenticationOptions authentication = options();
        switch (authentication.Mode)
        {
            case ClientAuthenticationMode.None:
                return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

            case ClientAuthenticationMode.ApiKey:
                if (!request.Headers.Contains(authentication.ApiKey.HeaderName))
                {
                    request.Headers.TryAddWithoutValidation(authentication.ApiKey.HeaderName, authentication.ApiKey.Value);
                }

                return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        if (request.Headers.Authorization is not null)
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        Result<AccessToken> token = await GetTokenAsync(authentication, rejectedToken: null, cancellationToken).ConfigureAwait(false);
        if (token.IsFailure)
        {
            CommunicationLog.AccessTokenUnavailable(_logger, clientName, token.Error.Code);
            throw new AccessTokenUnavailableException(clientName, token.Error);
        }

        request.Headers.Authorization = new AuthenticationHeaderValue(token.Value.Scheme, token.Value.Value);
        HttpResponseMessage response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.Unauthorized || !CanResend(request))
        {
            return response;
        }

        Result<AccessToken> fresh = await GetTokenAsync(authentication, token.Value.Value, cancellationToken).ConfigureAwait(false);
        if (fresh.IsFailure || fresh.Value.Value == token.Value.Value)
        {
            return response;
        }

        CommunicationLog.RetryingWithNewAccessToken(_logger, clientName);
        response.Dispose();
        request.Headers.Authorization = new AuthenticationHeaderValue(fresh.Value.Scheme, fresh.Value.Value);
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private ValueTask<Result<AccessToken>> GetTokenAsync(
        ClientAuthenticationOptions authentication,
        string? rejectedToken,
        CancellationToken cancellationToken) =>
        authentication.Mode switch
        {
            ClientAuthenticationMode.ClientCredentials => services
                .GetRequiredService<ClientCredentialsTokenClient>()
                .GetTokenAsync(authentication.ClientCredentials, rejectedToken, cancellationToken),
            ClientAuthenticationMode.AccessTokenProvider => GetFromProviderAsync(rejectedToken is not null, cancellationToken),
            _ => ValueTask.FromResult(Result<AccessToken>.Failure(Error.Unexpected(
                CommunicationErrorCodes.AccessTokenUnavailable, $"Authentication mode '{authentication.Mode}' is not supported."))),
        };

    private async ValueTask<Result<AccessToken>> GetFromProviderAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        IAccessTokenProvider provider = services.GetKeyedService<IAccessTokenProvider>(clientName)
            ?? throw new InvalidOperationException(
                $"Client '{clientName}' authenticates with an access token provider, but none is registered: call UseAccessTokenProvider<T>() on it.");

        Result<AccessToken> result = await provider
            .GetAccessTokenAsync(new AccessTokenContext(clientName, forceRefresh), cancellationToken)
            .ConfigureAwait(false);

        // A provider's own failure keeps its detail, under the code callers branch on.
        return result.IsSuccess
            ? result
            : Result<AccessToken>.Failure(Error.Unavailable(CommunicationErrorCodes.AccessTokenUnavailable, result.Error.Message));
    }

    /// <summary>
    /// Whether the request can go out a second time: no body, or a body that is rebuilt on every send. A streamed body
    /// (gRPC, an upload) has been consumed, so its 401 is returned as it is.
    /// </summary>
    private static bool CanResend(HttpRequestMessage request) =>
        request.Content is null or ByteArrayContent or JsonContent;
}
