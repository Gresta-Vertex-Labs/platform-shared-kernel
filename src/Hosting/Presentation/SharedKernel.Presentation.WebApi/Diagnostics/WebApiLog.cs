using Microsoft.Extensions.Logging;

namespace SharedKernel.Presentation.WebApi.Diagnostics;

/// <summary>
/// The log statements of this package. Never logs an idempotency key, a header value, a message or any caller data:
/// an endpoint display name is a route pattern, a status or error code is a code.
/// </summary>
internal static partial class WebApiLog
{
    [LoggerMessage(
        EventId = WebApiLoggingEventIds.ServerError,
        Level = LogLevel.Error,
        Message = "Request to '{EndpointDisplayName}' failed with status {StatusCode} and code '{ErrorCode}'.")]
    public static partial void ServerError(ILogger logger, string endpointDisplayName, int statusCode, string errorCode, Exception exception);

    [LoggerMessage(
        EventId = WebApiLoggingEventIds.IdempotencyKeyRefused,
        Level = LogLevel.Warning,
        Message = "Refused the request to '{EndpointDisplayName}' with '{ErrorCode}' because of its idempotency key.")]
    public static partial void IdempotencyKeyRefused(ILogger logger, string endpointDisplayName, string errorCode);

    [LoggerMessage(
        EventId = WebApiLoggingEventIds.CorsConfigurationInvalid,
        Level = LogLevel.Warning,
        Message = "CORS settings failed startup validation with {FailureCount} unsafe combinations.")]
    public static partial void CorsConfigurationInvalid(ILogger logger, int failureCount);

    [LoggerMessage(
        EventId = WebApiLoggingEventIds.RateLimitRejected,
        Level = LogLevel.Warning,
        Message = "Rate limiting rejected the request to '{EndpointDisplayName}'.")]
    public static partial void RateLimitRejected(ILogger logger, string endpointDisplayName);

    [LoggerMessage(
        EventId = WebApiLoggingEventIds.ClientError,
        Level = LogLevel.Debug,
        Message = "Request to '{EndpointDisplayName}' was rejected with status {StatusCode} and code '{ErrorCode}'.")]
    public static partial void ClientError(ILogger logger, string endpointDisplayName, int statusCode, string errorCode, Exception exception);

    [LoggerMessage(
        EventId = WebApiLoggingEventIds.RequestAborted,
        Level = LogLevel.Debug,
        Message = "The client closed the request to '{EndpointDisplayName}' before it completed.")]
    public static partial void RequestAborted(ILogger logger, string endpointDisplayName, Exception exception);

    [LoggerMessage(
        EventId = WebApiLoggingEventIds.PipelineNotApplied,
        Level = LogLevel.Warning,
        Message = "AddSharedKernelWebApi() was called but UseSharedKernelWebApi() was not.")]
    public static partial void PipelineNotApplied(ILogger logger);

    [LoggerMessage(
        EventId = WebApiLoggingEventIds.ExceptionDetailsOutsideDevelopment,
        Level = LogLevel.Warning,
        Message = "Problems:IncludeExceptionDetails is enabled in the '{EnvironmentName}' environment.")]
    public static partial void ExceptionDetailsOutsideDevelopment(ILogger logger, string environmentName);

    [LoggerMessage(
        EventId = WebApiLoggingEventIds.WebSocketOriginRefused,
        Level = LogLevel.Warning,
        Message = "Refused a WebSocket request to '{EndpointDisplayName}' from an origin the CORS policy does not allow.")]
    public static partial void WebSocketOriginRefused(ILogger logger, string endpointDisplayName);
}
