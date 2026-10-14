using SharedKernel.Primitives.Logging;

namespace SharedKernel.Communication.Internal;

/// <summary>
/// The EventIds of this package: sub-block 0 of the <see cref="LoggingEventIdRanges.Communication"/> block
/// (11000-11099). Sub-block 1 (11100-11199) belongs to <c>SharedKernel.Communication.Grpc</c> and sub-block 2
/// (11200-11299) to <c>SharedKernel.Communication.Rest</c>; sub-block 3 (11300-11399) is retired and must not be reused.
/// </summary>
internal static class CommunicationLoggingEventIds
{
    /// <summary>The first id of this package's sub-block.</summary>
    internal const int Base = LoggingEventIdRanges.Communication + 0 * LoggingEventIdRanges.PackageSubBlockWidth;

    /// <summary>An access token was acquired from the token endpoint.</summary>
    internal const int TokenAcquired = Base + 0;

    /// <summary>The token endpoint refused the client credentials.</summary>
    internal const int TokenRequestRefused = Base + 1;

    /// <summary>The token endpoint could not be reached.</summary>
    internal const int TokenEndpointUnreachable = Base + 2;

    /// <summary>The client has no access token, so the request was not sent.</summary>
    internal const int AccessTokenUnavailable = Base + 3;

    /// <summary>A 401 was answered with a new access token.</summary>
    internal const int RetryingWithNewAccessToken = Base + 4;
}
