using Microsoft.Extensions.Logging;

namespace SharedKernel.Presentation.Diagnostics;

/// <summary>
/// The log statements of this package. Never logs a principal, a permission or a role name: an endpoint display name
/// is a route pattern, an authentication type and a scheme name are configuration names.
/// </summary>
internal static partial class PresentationCoreLog
{
    [LoggerMessage(
        EventId = PresentationCoreLoggingEventIds.AuthorizationRefused,
        Level = LogLevel.Warning,
        Message = "Authorization refused the request to '{EndpointDisplayName}' with '{ErrorCode}'.")]
    public static partial void AuthorizationRefused(ILogger logger, string endpointDisplayName, string errorCode);

    [LoggerMessage(
        EventId = PresentationCoreLoggingEventIds.PrincipalWithoutMapper,
        Level = LogLevel.Warning,
        Message = "Refused a caller authenticated as '{AuthenticationType}' because no IUserContextMapper handles it.")]
    public static partial void PrincipalWithoutMapper(ILogger logger, string authenticationType);

    [LoggerMessage(
        EventId = PresentationCoreLoggingEventIds.SchemeWithoutMapper,
        Level = LogLevel.Warning,
        Message = "Authentication scheme '{SchemeName}' has no IUserContextMapper.")]
    public static partial void SchemeWithoutMapper(ILogger logger, string schemeName);
}
