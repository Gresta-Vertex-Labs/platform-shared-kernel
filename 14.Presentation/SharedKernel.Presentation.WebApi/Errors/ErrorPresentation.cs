using Microsoft.AspNetCore.Http;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Presentation.WebApi;

/// <summary>
/// Decides how an <see cref="Error"/> is shown to an HTTP caller: its status code and the message the client may see.
/// </summary>
/// <remarks>
/// The message and the server-error rule are <c>SharedKernel.Presentation.Core</c>'s, shared with gRPC and SignalR so
/// the three protocols never disagree (P-579); this class adds the HTTP status, which depends on this package's
/// <c>Problems:PreconditionFailedErrorCodes</c>.
/// </remarks>
internal static class ErrorPresentation
{
    /// <summary>Returns the HTTP status code for <paramref name="error"/> in the current request.</summary>
    /// <param name="error">The error.</param>
    /// <param name="httpContext">The current request, or <see langword="null"/> outside a request.</param>
    /// <returns>
    /// The status from <c>ErrorTypeStatusCodeMap.Resolve</c>, except for a version conflict of a conditional
    /// request: an <see cref="ErrorType.Conflict"/> whose code is one of <c>Problems:PreconditionFailedErrorCodes</c>
    /// (by default <c>persistence.concurrency_conflict</c>, <c>storage.precondition_failed</c> and
    /// <c>storage.already_exists</c>), in a request carrying <c>If-Match</c> or <c>If-None-Match</c>, is 412
    /// Precondition Failed (RFC 9110 section 13.1): the version the client named is not the current one. Every other
    /// conflict stays 409.
    /// </returns>
    public static int GetStatusCode(Error error, HttpContext? httpContext)
    {
        ArgumentNullException.ThrowIfNull(error);

        if (error.Type == ErrorType.Conflict
            && httpContext is not null
            && RequestFacts.IsConditionalRequest(httpContext.Request)
            && RequestFacts.GetOptions(httpContext).Problems.PreconditionFailedErrorCodes.Contains(error.Code))
        {
            return StatusCodes.Status412PreconditionFailed;
        }

        return ErrorTypeStatusCodeMap.Resolve(error.Type);
    }

    /// <summary>
    /// Returns the message a client may see for <paramref name="error"/>: localized, and generic for a server error
    /// outside Development.
    /// </summary>
    /// <param name="error">The error.</param>
    /// <param name="httpContext">The current request, or <see langword="null"/> outside a request.</param>
    /// <returns>The client message.</returns>
    public static string GetClientMessage(Error error, HttpContext? httpContext) =>
        Presentation.ErrorPresentation.GetClientMessage(error, httpContext);

    /// <summary>Returns <see langword="true"/> when <paramref name="type"/> describes a failure of the server (a 5xx status).</summary>
    /// <param name="type">The error type.</param>
    /// <returns><see langword="true"/> for a server error.</returns>
    public static bool IsServerError(ErrorType type) => Presentation.ErrorPresentation.IsServerError(type);

    /// <summary>Translates a message this package authored itself, keyed by <paramref name="code"/>.</summary>
    internal static string GetPresentationMessage(HttpContext httpContext, string code, string message) =>
        Presentation.ErrorPresentation.GetPresentationMessage(httpContext, code, message);
}
