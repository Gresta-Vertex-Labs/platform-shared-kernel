using SharedKernel.Primitives.Errors;

namespace SharedKernel.Communication.Rest.ProblemDetails;

/// <summary>
/// Maps an HTTP status code back to the <see cref="ErrorType"/> that produced it.
/// </summary>
/// <remarks>
/// <para>
/// This is the reverse of <c>14.Presentation</c>'s <c>ErrorTypeStatusCodeMap.Resolve</c>
/// (<c>SharedKernel.Presentation.WebApi/Errors/ErrorTypeStatusCodeMap.cs</c>): <see cref="ErrorType.Validation"/>
/// → 400, <see cref="ErrorType.Unauthorized"/> → 401, <see cref="ErrorType.Forbidden"/> → 403,
/// <see cref="ErrorType.NotFound"/> → 404, <see cref="ErrorType.Conflict"/> → 409,
/// <see cref="ErrorType.BusinessRule"/> → 422, <see cref="ErrorType.Unavailable"/> → 503,
/// <see cref="ErrorType.Timeout"/> → 504, everything else → <see cref="ErrorType.Unexpected"/>.
/// </para>
/// <para>
/// It also reads back the statuses an HTTP boundary answers outside that map, so none of them lands
/// in <see cref="ErrorType.Unexpected"/> and reads as a defect: 412 (a failed <c>If-Match</c>) as
/// <see cref="ErrorType.Conflict"/>, the same as a concurrency conflict; 413, 415 and 428 (a body too
/// large, an unsupported media type, a required precondition header missing) as
/// <see cref="ErrorType.Validation"/>, since the caller can fix the request; and 429 (rate limited)
/// as <see cref="ErrorType.Unavailable"/>, since the same call succeeds once the caller backs off.
/// </para>
/// <para>
/// It applies whether or not the response carries a ProblemDetails body: an HTML page or an empty body
/// from a gateway or proxy gets the same <see cref="ErrorType"/> for its status, with the code
/// <c>http.{status}</c> (see <see cref="ProblemDetailsDeserializer"/>).
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
    /// The mapped <see cref="ErrorType"/>: 400, 413, 415 and 428 → <see cref="ErrorType.Validation"/>,
    /// 401 → <see cref="ErrorType.Unauthorized"/>, 403 → <see cref="ErrorType.Forbidden"/>,
    /// 404 → <see cref="ErrorType.NotFound"/>, 409 and 412 → <see cref="ErrorType.Conflict"/>,
    /// 422 → <see cref="ErrorType.BusinessRule"/>, 429 and 503 → <see cref="ErrorType.Unavailable"/>,
    /// 504 → <see cref="ErrorType.Timeout"/>. Any other status code (including every other 4xx/5xx,
    /// such as 500 and 502, and any non-standard code) falls back to <see cref="ErrorType.Unexpected"/>.
    /// </returns>
    internal static ErrorType Resolve(int statusCode) => statusCode switch
    {
        400 => ErrorType.Validation,
        401 => ErrorType.Unauthorized,
        403 => ErrorType.Forbidden,
        404 => ErrorType.NotFound,
        409 => ErrorType.Conflict,
        412 => ErrorType.Conflict,
        413 => ErrorType.Validation,
        415 => ErrorType.Validation,
        422 => ErrorType.BusinessRule,
        428 => ErrorType.Validation,
        429 => ErrorType.Unavailable,
        503 => ErrorType.Unavailable,
        504 => ErrorType.Timeout,
        _ => ErrorType.Unexpected,
    };
}
