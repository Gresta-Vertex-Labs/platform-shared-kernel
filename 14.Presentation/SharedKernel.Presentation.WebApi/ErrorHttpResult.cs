using Microsoft.AspNetCore.Http;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Presentation.WebApi;

/// <summary>
/// An <see cref="IResult"/> that writes an <see cref="Primitives.Errors.Error"/> as an RFC 9457
/// <c>application/problem+json</c> response — the failure half of every typed result this package returns, for
/// minimal APIs and MVC controllers alike.
/// </summary>
/// <remarks>
/// <para>
/// The response is built when the result executes, with the request at hand, so the message is translated into the
/// request's culture, a server error's message is hidden outside Development, and the correlation and trace ids are
/// included. Created by <c>ToErrorResult()</c> and by the <c>Result</c> mapping methods such as <c>ToOk()</c>.
/// </para>
/// <para>
/// <see cref="StatusCode"/> is the status of the error type alone. The response can differ in one case only the
/// request decides: a version conflict of a conditional request (<c>If-Match</c> or <c>If-None-Match</c>) is written
/// as 412 Precondition Failed (see <see cref="ErrorPresentation.GetStatusCode"/>).
/// </para>
/// </remarks>
public sealed class ErrorHttpResult : IResult, IStatusCodeHttpResult, IContentTypeHttpResult
{
    /// <summary>Initializes a new instance of the <see cref="ErrorHttpResult"/> class.</summary>
    /// <param name="error">The error to write.</param>
    /// <exception cref="ArgumentException"><paramref name="error"/> is <see cref="Error.None"/>, which describes no failure.</exception>
    public ErrorHttpResult(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        if (error.Type == ErrorType.None)
        {
            throw new ArgumentException("Error.None does not describe a failure.", nameof(error));
        }

        Error = error;
    }

    /// <summary>Gets the error this result writes.</summary>
    public Error Error { get; }

    /// <summary>
    /// Gets the status code of the error's type, from <see cref="ErrorTypeStatusCodeMap"/>. A conditional request
    /// failing with a version conflict is answered 412 instead, which only the request can decide.
    /// </summary>
    public int StatusCode => ErrorTypeStatusCodeMap.Resolve(Error.Type);

    /// <inheritdoc />
    int? IStatusCodeHttpResult.StatusCode => StatusCode;

    /// <summary>Gets the media type of the response: <c>application/problem+json</c>.</summary>
    public string ContentType => ProblemResponseWriter.ContentType;

    /// <inheritdoc />
    public Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        return ProblemResponseWriter.WriteAsync(httpContext, ProblemFactory.ForError(Error, httpContext));
    }
}
