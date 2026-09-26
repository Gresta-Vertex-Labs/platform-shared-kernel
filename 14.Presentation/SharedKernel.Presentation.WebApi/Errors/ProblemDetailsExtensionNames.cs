namespace SharedKernel.Presentation.WebApi;

/// <summary>
/// The names of the extension members every <c>application/problem+json</c> response of this platform may carry,
/// beyond the RFC 9457 <c>type</c>, <c>title</c>, <c>status</c>, <c>detail</c> and <c>instance</c>.
/// </summary>
/// <remarks>
/// These names are a wire contract read by clients (<c>SharedKernel.Communication.Rest</c> rebuilds an
/// <see cref="SharedKernel.Primitives.Errors.Error"/> from them), so reference the constants rather than retyping
/// the strings, and never rename one.
/// </remarks>
public static class ProblemDetailsExtensionNames
{
    /// <summary>
    /// <c>errorCode</c>: the stable code of the error, such as <c>not_found.default</c>; <c>http.{status}</c> for a
    /// response the framework produced without an error, such as an unmatched route (<c>http.404</c>), except that an
    /// oversized body is always <c>request.too_large</c>.
    /// </summary>
    public const string ErrorCode = "errorCode";

    /// <summary><c>errors</c>: field path (or error code, when an error names no field) to the messages for it.</summary>
    public const string Errors = "errors";

    /// <summary><c>errorCodes</c>: the same keys as <see cref="Errors"/> to the codes of the same errors, in the same order.</summary>
    public const string ErrorCodes = "errorCodes";

    /// <summary><c>traceId</c>: the W3C trace context of the request, the id support staff search for.</summary>
    public const string TraceId = ErrorMemberNames.TraceId;

    /// <summary><c>correlationId</c>: the correlation id of the request, also sent as the <c>X-Correlation-Id</c> header.</summary>
    public const string CorrelationId = ErrorMemberNames.CorrelationId;

    /// <summary>
    /// <c>exception</c>: the <c>type</c>, <c>message</c> and <c>stackTrace</c> of an unhandled exception. Present only in the
    /// Development environment, or when <c>Problems:IncludeExceptionDetails</c> is <see langword="true"/>.
    /// </summary>
    public const string Exception = "exception";
}
