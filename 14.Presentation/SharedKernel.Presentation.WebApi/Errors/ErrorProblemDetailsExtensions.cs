using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Presentation.WebApi.Errors;

/// <summary>Converts an <see cref="Error"/> into the RFC 9457 <see cref="ProblemDetails"/> body of this platform.</summary>
/// <remarks>
/// Most code never calls this: return a typed result (<c>ToOk()</c>, <c>ToErrorResult()</c>) or throw, and the
/// response is written for you. Use it when you write a response yourself, and write it through
/// <see cref="IProblemDetailsService"/> so the platform's members are completed.
/// </remarks>
public static class ErrorProblemDetailsExtensions
{
    /// <summary>Builds the problem body for <paramref name="error"/> in the current request.</summary>
    /// <param name="error">The error.</param>
    /// <param name="httpContext">The current request, which decides the language, redaction and ids.</param>
    /// <returns>
    /// A problem with <c>status</c> from <see cref="ErrorPresentation.GetStatusCode"/>, the framework's
    /// <c>title</c> and <c>type</c> for that status (or <c>Problems:TypeBaseUri</c> followed by the code),
    /// <c>detail</c> from <see cref="ErrorPresentation.GetClientMessage"/>, <c>instance</c> set to the request path,
    /// and the extension members <c>errorCode</c>, <c>traceId</c>, <c>correlationId</c> (when the request has one)
    /// and, when <see cref="Error.Details"/> is not empty, <c>errors</c> and <c>errorCodes</c> keyed by field path.
    /// </returns>
    public static ProblemDetails ToProblemDetails(this Error error, HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(httpContext);

        return ProblemFactory.ForError(error, httpContext);
    }
}
