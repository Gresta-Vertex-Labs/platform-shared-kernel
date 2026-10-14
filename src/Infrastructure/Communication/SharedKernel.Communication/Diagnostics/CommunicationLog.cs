using Microsoft.Extensions.Logging;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Communication.Internal;

// Names the client by its kernel client name only: never an endpoint, an OAuth client id, a token or a secret.
internal static partial class CommunicationLog
{
    [LoggerMessage(
        EventId = CommunicationLoggingEventIds.TokenAcquired,
        Level = LogLevel.Debug,
        Message = "Acquired an access token for client '{ClientName}' that is valid for {LifetimeSeconds} seconds.")]
    public static partial void TokenAcquired(ILogger logger, string clientName, int lifetimeSeconds);

    [LoggerMessage(
        EventId = CommunicationLoggingEventIds.TokenRequestRefused,
        Level = LogLevel.Warning,
        Message = "The token endpoint refused client '{ClientName}' with status {StatusCode} and OAuth error '{OAuthError}'.")]
    public static partial void TokenRequestRefused(ILogger logger, string clientName, int statusCode, string oAuthError);

    [LoggerMessage(
        EventId = CommunicationLoggingEventIds.TokenEndpointUnreachable,
        Level = LogLevel.Warning,
        Message = "The token endpoint could not be reached for client '{ClientName}'.")]
    public static partial void TokenEndpointUnreachable(ILogger logger, string clientName, Exception exception);

    [LoggerMessage(
        EventId = CommunicationLoggingEventIds.AccessTokenUnavailable,
        Level = LogLevel.Warning,
        Message = "Client '{ClientName}' has no access token, so the request was not sent and failed as {ErrorType} with code '{ErrorCode}'.")]
    public static partial void AccessTokenUnavailable(ILogger logger, string clientName, ErrorType errorType, string errorCode);

    [LoggerMessage(
        EventId = CommunicationLoggingEventIds.RetryingWithNewAccessToken,
        Level = LogLevel.Debug,
        Message = "Client '{ClientName}' was answered with 401, so the request is sent once more with a new access token.")]
    public static partial void RetryingWithNewAccessToken(ILogger logger, string clientName);
}
