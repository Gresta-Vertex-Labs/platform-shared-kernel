using Microsoft.AspNetCore.Http;

namespace SharedKernel.Presentation.WebApi.Errors;

/// <summary>
/// An <see cref="IResult"/> for an outcome this package produces itself, such as a missing required header, with
/// a status of its own (428 has no <see cref="Primitives.Errors.ErrorType"/>).
/// </summary>
internal sealed class PresentationProblemResult : IResult, IStatusCodeHttpResult, IContentTypeHttpResult
{
    private readonly string _code;
    private readonly string _message;

    public PresentationProblemResult(int statusCode, string code, string message)
    {
        StatusCode = statusCode;
        _code = code;
        _message = message;
    }

    /// <inheritdoc />
    public int? StatusCode { get; }

    /// <inheritdoc />
    public string ContentType => ProblemResponseWriter.ContentType;

    /// <inheritdoc />
    public Task ExecuteAsync(HttpContext httpContext) =>
        ProblemResponseWriter.WriteAsync(
            httpContext,
            ProblemFactory.ForPresentation(httpContext, StatusCode ?? StatusCodes.Status400BadRequest, _code, _message));
}
