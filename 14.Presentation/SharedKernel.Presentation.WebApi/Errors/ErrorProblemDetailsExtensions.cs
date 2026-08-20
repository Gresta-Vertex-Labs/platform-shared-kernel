using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SharedKernel.Presentation.WebApi.Http;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Presentation.WebApi.Errors;

/// <summary>
/// Converts an <see cref="Error"/> into an RFC 9457 <see cref="ProblemDetails"/> response body.
/// </summary>
/// <remarks>
/// This is the only permitted way to convert an <see cref="Error"/> into an HTTP error body.
/// Hand-rolled <see cref="ProblemDetails"/> construction inline in endpoint/controller code is a
/// platform violation.
/// </remarks>
public static class ErrorProblemDetailsExtensions
{
    /// <summary>
    /// Maps the specified <paramref name="error"/> to a <see cref="ProblemDetails"/> instance.
    /// </summary>
    /// <param name="error">The error to convert.</param>
    /// <param name="context">
    /// The current <see cref="HttpContext"/>, used to populate <c>Extensions["traceId"]</c> when
    /// <see cref="System.Diagnostics.Activity.Current"/> is unavailable. May be <see langword="null"/>.
    /// </param>
    /// <returns>
    /// A <see cref="ProblemDetails"/> with <c>Title</c> set to <see cref="Error.Code"/>,
    /// <c>Detail</c> set to <see cref="Error.Message"/>, <c>Status</c> resolved via
    /// <see cref="ErrorTypeStatusCodeMap.Resolve"/>, <c>Type</c> as an RFC 9457 status URI, and
    /// <c>Extensions["errorCode"]</c>/<c>Extensions["traceId"]</c> populated. Pure mapping — no
    /// logging, no I/O.
    /// </returns>
    public static ProblemDetails ToProblemDetails(this Error error, HttpContext? context = null)
    {
        var status = ErrorTypeStatusCodeMap.Resolve(error.Type);
        var problemDetails = ProblemDetailsShaping.Create(status, error.Code, error.Message, context);

        problemDetails.Extensions["errorCode"] = error.Code;

        return problemDetails;
    }
}
