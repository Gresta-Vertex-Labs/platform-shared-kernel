using System.Net;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Presentation.Errors;

/// <summary>
/// Single source of truth for mapping a <see cref="ErrorType"/> to an HTTP status code. Lives in
/// <c>SharedKernel.Presentation.Core</c> (P-570) beside <see cref="GrpcStatusCodeMap"/>, so both boundary
/// packages share it and neither references the other; it uses <see cref="HttpStatusCode"/> rather than
/// ASP.NET Core's <c>StatusCodes</c> so this package needs no ASP.NET Core reference.
/// </summary>
/// <remarks>
/// Any inline switch statement duplicating this mapping anywhere else in a consuming service is a
/// platform violation — always call <see cref="Resolve"/> (directly, or indirectly via
/// <c>ErrorProblemDetailsExtensions.ToProblemDetails</c>) instead.
/// </remarks>
public static class ErrorTypeStatusCodeMap
{
    /// <summary>
    /// Resolves the HTTP status code that corresponds to the specified <paramref name="type"/>.
    /// </summary>
    /// <param name="type">The error type to resolve.</param>
    /// <returns>
    /// The mapped HTTP status code: <see cref="ErrorType.Validation"/> → 400,
    /// <see cref="ErrorType.Unauthorized"/> → 401, <see cref="ErrorType.Forbidden"/> → 403,
    /// <see cref="ErrorType.NotFound"/> → 404, <see cref="ErrorType.Conflict"/> → 409,
    /// <see cref="ErrorType.BusinessRule"/> → 422, <see cref="ErrorType.Unexpected"/> → 500. Any
    /// <see cref="ErrorType"/> not explicitly mapped (including <see cref="ErrorType.None"/>)
    /// falls back to 500.
    /// </returns>
    public static int Resolve(ErrorType type) => type switch
    {
        ErrorType.Validation => (int)HttpStatusCode.BadRequest,
        ErrorType.Unauthorized => (int)HttpStatusCode.Unauthorized,
        ErrorType.Forbidden => (int)HttpStatusCode.Forbidden,
        ErrorType.NotFound => (int)HttpStatusCode.NotFound,
        ErrorType.Conflict => (int)HttpStatusCode.Conflict,
        ErrorType.BusinessRule => (int)HttpStatusCode.UnprocessableEntity,
        ErrorType.Unexpected => (int)HttpStatusCode.InternalServerError,
        _ => (int)HttpStatusCode.InternalServerError,
    };
}
