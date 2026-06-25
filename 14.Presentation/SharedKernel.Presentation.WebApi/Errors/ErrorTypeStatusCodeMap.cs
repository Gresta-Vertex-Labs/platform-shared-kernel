using Microsoft.AspNetCore.Http;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Presentation.WebApi.Errors;

/// <summary>
/// Single source of truth for mapping a <see cref="ErrorType"/> to an HTTP status code.
/// </summary>
/// <remarks>
/// Any inline switch statement duplicating this mapping anywhere else in a consuming service is a
/// platform violation — always call <see cref="Resolve"/> (directly, or indirectly via
/// <see cref="ErrorProblemDetailsExtensions.ToProblemDetails"/>) instead.
/// </remarks>
public static class ErrorTypeStatusCodeMap
{
    /// <summary>
    /// Resolves the HTTP status code that corresponds to the specified <paramref name="type"/>.
    /// </summary>
    /// <param name="type">The error type to resolve.</param>
    /// <returns>
    /// The mapped HTTP status code: <see cref="ErrorType.Validation"/> → 400,
    /// <see cref="ErrorType.Unauthorized"/> → 401, <see cref="ErrorType.NotFound"/> → 404,
    /// <see cref="ErrorType.Conflict"/> → 409, <see cref="ErrorType.BusinessRule"/> → 422,
    /// <see cref="ErrorType.Unexpected"/> → 500. Any <see cref="ErrorType"/> not explicitly mapped
    /// (including <see cref="ErrorType.None"/>) falls back to 500.
    /// </returns>
    public static int Resolve(ErrorType type) => type switch
    {
        ErrorType.Validation => StatusCodes.Status400BadRequest,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.BusinessRule => StatusCodes.Status422UnprocessableEntity,
        ErrorType.Unexpected => StatusCodes.Status500InternalServerError,
        _ => StatusCodes.Status500InternalServerError,
    };
}
