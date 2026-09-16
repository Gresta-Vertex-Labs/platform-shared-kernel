using SharedKernel.Primitives.Errors;

namespace SharedKernel.Communication.Rest.ProblemDetails;

/// <summary>
/// Maps an HTTP status code back to the <see cref="ErrorType"/> that produced it.
/// </summary>
/// <remarks>
/// <para>
/// This is the exact reverse of <c>14.Presentation</c>'s <c>ErrorTypeStatusCodeMap.Resolve</c>
/// (<c>SharedKernel.Presentation.WebApi/Errors/ErrorTypeStatusCodeMap.cs</c>): <see cref="ErrorType.Validation"/>
/// → 400, <see cref="ErrorType.Unauthorized"/> → 401, <see cref="ErrorType.Forbidden"/> → 403,
/// <see cref="ErrorType.NotFound"/> → 404, <see cref="ErrorType.Conflict"/> → 409,
/// <see cref="ErrorType.BusinessRule"/> → 422, everything else → <see cref="ErrorType.Unexpected"/>.
/// </para>
/// <para>
/// <b>Why this table is duplicated here instead of shared:</b> <c>11.Communication</c> may only
/// reference <c>01.Core</c>, <c>04.Contracts</c>, and <c>12.Security</c> abstractions — it must never
/// reference <c>14.Presentation</c>, which is where the forward mapping actually lives. This is the
/// single named place the reverse table lives in this package; a future change to
/// <c>ErrorTypeStatusCodeMap</c>'s mapping must be mirrored here by hand, since no shared package can
/// legally hold both directions.
/// </para>
/// </remarks>
internal static class HttpStatusErrorTypeMap
{
    /// <summary>
    /// Resolves the <see cref="ErrorType"/> that corresponds to the specified
    /// <paramref name="statusCode"/>.
    /// </summary>
    /// <param name="statusCode">The HTTP status code from the response.</param>
    /// <returns>
    /// The mapped <see cref="ErrorType"/>: 400 → <see cref="ErrorType.Validation"/>,
    /// 401 → <see cref="ErrorType.Unauthorized"/>, 403 → <see cref="ErrorType.Forbidden"/>,
    /// 404 → <see cref="ErrorType.NotFound"/>, 409 → <see cref="ErrorType.Conflict"/>,
    /// 422 → <see cref="ErrorType.BusinessRule"/>. Any other status code (including every other
    /// 4xx/5xx and any non-standard code) falls back to <see cref="ErrorType.Unexpected"/>.
    /// </returns>
    internal static ErrorType Resolve(int statusCode) => statusCode switch
    {
        400 => ErrorType.Validation,
        401 => ErrorType.Unauthorized,
        403 => ErrorType.Forbidden,
        404 => ErrorType.NotFound,
        409 => ErrorType.Conflict,
        422 => ErrorType.BusinessRule,
        _ => ErrorType.Unexpected,
    };
}
