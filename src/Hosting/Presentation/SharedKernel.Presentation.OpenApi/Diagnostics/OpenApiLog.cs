using Microsoft.Extensions.Logging;

namespace SharedKernel.Presentation.OpenApi.Diagnostics;

/// <summary>The log statements of this package. Never logs a document, a route or any caller data.</summary>
internal static partial class OpenApiLog
{
    [LoggerMessage(
        EventId = OpenApiLoggingEventIds.DocumentsNotMapped,
        Level = LogLevel.Information,
        Message = "The OpenAPI documents and API reference are not served in the '{EnvironmentName}' environment.")]
    public static partial void DocumentsNotMapped(ILogger logger, string environmentName);

    [LoggerMessage(
        EventId = OpenApiLoggingEventIds.DocumentsServedWithoutAuthorization,
        Level = LogLevel.Warning,
        Message = "The OpenAPI documents and API reference are served in the '{EnvironmentName}' environment without authorization.")]
    public static partial void DocumentsServedWithoutAuthorization(ILogger logger, string environmentName);
}
