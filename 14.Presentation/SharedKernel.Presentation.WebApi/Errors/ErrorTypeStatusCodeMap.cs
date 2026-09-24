using Microsoft.AspNetCore.Http;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Presentation.WebApi.Errors;

/// <summary>The single source of truth for the HTTP status code of an <see cref="ErrorType"/>.</summary>
/// <remarks>
/// Never duplicate this mapping in a switch of your own. Response writers call
/// <see cref="ErrorPresentation.GetStatusCode"/>, which applies this map plus the one request-dependent rule: a
/// <see cref="ErrorType.Conflict"/> whose code is in <c>Problems:PreconditionFailedErrorCodes</c>, on a request that
/// carries <c>If-Match</c> or <c>If-None-Match</c>, is 412.
/// </remarks>
public static class ErrorTypeStatusCodeMap
{
    /// <summary>Returns the HTTP status code for <paramref name="type"/>.</summary>
    /// <param name="type">The error type.</param>
    /// <returns>
    /// <see cref="ErrorType.Validation"/> 400, <see cref="ErrorType.Unauthorized"/> 401,
    /// <see cref="ErrorType.Forbidden"/> 403, <see cref="ErrorType.NotFound"/> 404, <see cref="ErrorType.Conflict"/> 409,
    /// <see cref="ErrorType.BusinessRule"/> 422, <see cref="ErrorType.Unexpected"/> 500,
    /// <see cref="ErrorType.Unavailable"/> 503 and <see cref="ErrorType.Timeout"/> 504. Anything else, including
    /// <see cref="ErrorType.None"/>, is 500.
    /// </returns>
    public static int Resolve(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.BusinessRule => StatusCodes.Status422UnprocessableEntity,
        ErrorType.Unexpected => StatusCodes.Status500InternalServerError,
        ErrorType.Unavailable => StatusCodes.Status503ServiceUnavailable,
        ErrorType.Timeout => StatusCodes.Status504GatewayTimeout,
        _ => StatusCodes.Status500InternalServerError,
    };
}
