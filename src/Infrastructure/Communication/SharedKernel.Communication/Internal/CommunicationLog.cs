using Microsoft.Extensions.Logging;

namespace SharedKernel.Communication.Internal;

/// <summary>The log events of <c>SharedKernel.Communication</c>: EventIds 11000–11099.</summary>
internal static partial class CommunicationLog
{
    [LoggerMessage(
        EventId = 11000,
        Level = LogLevel.Debug,
        Message = "Acquired an access token from {TokenEndpoint} for client id {ClientId}, valid for {Lifetime}.")]
    public static partial void TokenAcquired(ILogger logger, Uri tokenEndpoint, string clientId, TimeSpan lifetime);

    [LoggerMessage(
        EventId = 11001,
        Level = LogLevel.Warning,
        Message = "The token endpoint {TokenEndpoint} refused client id {ClientId}: HTTP {StatusCode}, OAuth error '{OAuthError}'.")]
    public static partial void TokenRequestRefused(ILogger logger, Uri tokenEndpoint, string clientId, int statusCode, string oAuthError);

    [LoggerMessage(
        EventId = 11002,
        Level = LogLevel.Warning,
        Message = "The token endpoint {TokenEndpoint} could not be reached for client id {ClientId}.")]
    public static partial void TokenEndpointUnreachable(ILogger logger, Uri tokenEndpoint, string clientId, Exception exception);

    [LoggerMessage(
        EventId = 11003,
        Level = LogLevel.Warning,
        Message = "Client {ClientName} has no access token ({ErrorCode}); the request was not sent.")]
    public static partial void AccessTokenUnavailable(ILogger logger, string clientName, string errorCode);

    [LoggerMessage(
        EventId = 11004,
        Level = LogLevel.Debug,
        Message = "Client {ClientName} was answered 401; sending the request once more with a new access token.")]
    public static partial void RetryingWithNewAccessToken(ILogger logger, string clientName);
}
