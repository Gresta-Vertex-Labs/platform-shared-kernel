using SharedKernel.Primitives.Logging;

namespace SharedKernel.Presentation.WebApi.Diagnostics;

/// <summary>
/// The <c>EventId</c>s of this package: sub-block 0 of <c>14.Presentation</c>'s block (14000-14099), built from
/// <see cref="LoggingEventIdRanges"/>. Retired ids (14000, 14002, 14006, 14009, 14010) are never reused.
/// </summary>
internal static class WebApiLoggingEventIds
{
    /// <summary>The first id of this package's sub-block (14000).</summary>
    public const int Base = LoggingEventIdRanges.Presentation + (0 * LoggingEventIdRanges.PackageSubBlockWidth);

    /// <summary>A server-category failure of a request (Error level).</summary>
    public const int ServerError = Base + 1;

    /// <summary>A request refused for its idempotency key (Warning level).</summary>
    public const int IdempotencyKeyRefused = Base + 3;

    /// <summary>CORS settings that failed startup validation (Warning level; the host stops with the validation exception).</summary>
    public const int CorsConfigurationInvalid = Base + 4;

    /// <summary>A request rejected by the rate limiter (Warning level).</summary>
    public const int RateLimitRejected = Base + 5;

    /// <summary>A client-category failure of a request (Debug level).</summary>
    public const int ClientError = Base + 7;

    /// <summary>A request the client closed before it completed (Debug level).</summary>
    public const int RequestAborted = Base + 8;

    /// <summary><c>UseSharedKernelWebApi()</c> was never called (Warning level).</summary>
    public const int PipelineNotApplied = Base + 11;

    /// <summary>Exception details are enabled outside Development (Warning level).</summary>
    public const int ExceptionDetailsOutsideDevelopment = Base + 12;

    /// <summary>A WebSocket request from a disallowed origin (Warning level).</summary>
    public const int WebSocketOriginRefused = Base + 13;
}
