using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Presentation.WebApi;

/// <summary>Builds the one problem shape every error response of this package has.</summary>
internal static class ProblemFactory
{
    /// <summary>
    /// Builds the problem for <paramref name="error"/>: status from <see cref="ErrorPresentation.GetStatusCode"/>
    /// (unless <paramref name="statusCode"/> is given), <c>detail</c> from
    /// <see cref="ErrorPresentation.GetClientMessage"/>, <c>errorCode</c> from <see cref="Error.Code"/>, and
    /// <c>errors</c>/<c>errorCodes</c> from <paramref name="fieldErrors"/> or, when that is <see langword="null"/>,
    /// from <see cref="Error.Details"/>.
    /// </summary>
    public static ProblemDetails ForError(
        Error error,
        HttpContext httpContext,
        int? statusCode = null,
        IReadOnlyList<Error>? fieldErrors = null)
    {
        var problem = Create(
            httpContext,
            statusCode ?? ErrorPresentation.GetStatusCode(error, httpContext),
            error.Code,
            ErrorPresentation.GetClientMessage(error, httpContext));

        var fields = fieldErrors ?? error.Details;
        if (fields.Count > 0)
        {
            AddFieldErrors(problem, fields, httpContext);
        }

        return problem;
    }

    /// <summary>
    /// Builds the problem for an outcome this package produces itself (a missing header, a rejected caller), with
    /// <paramref name="message"/> translated under <paramref name="code"/> when a catalog has it.
    /// </summary>
    public static ProblemDetails ForPresentation(HttpContext httpContext, int statusCode, string code, string message) =>
        Create(httpContext, statusCode, code, ErrorPresentation.GetPresentationMessage(httpContext, code, message));

    /// <summary>Builds the problem for a response the framework produced without an error, coded by <see cref="CodeForStatus"/>.</summary>
    public static ProblemDetails ForStatus(HttpContext httpContext, int statusCode) =>
        Create(httpContext, statusCode, CodeForStatus(statusCode), detail: null);

    /// <summary>
    /// Returns the code of a status the framework produced without an error: <c>request.too_large</c> for 413 — the
    /// framework answers an oversized body with 413 either by throwing or, for a minimal API outside Development, by
    /// setting the status, and both must read the same — and <c>http.{status}</c> for everything else.
    /// </summary>
    public static string CodeForStatus(int statusCode) =>
        statusCode == StatusCodes.Status413PayloadTooLarge
            ? PresentationErrorCodes.RequestTooLarge
            : PresentationErrorCodes.ForStatus(statusCode);

    /// <summary>Builds a problem with the framework's title and type for <paramref name="statusCode"/>.</summary>
    public static ProblemDetails Create(HttpContext httpContext, int statusCode, string code, string? detail)
    {
        var problem = new ProblemDetails { Status = statusCode, Detail = detail };
        problem.Extensions[ProblemDetailsExtensionNames.ErrorCode] = code;

        // The framework's reason phrase as title and RFC section as type, exactly as every IProblemDetailsService
        // writer would.
        ProblemDefaults.Apply(problem);

        ProblemDetailsCustomizer.Apply(httpContext, problem);
        return problem;
    }

    private static void AddFieldErrors(ProblemDetails problem, IReadOnlyList<Error> errors, HttpContext httpContext)
    {
        var groups = FieldErrorGroups.Create(errors, httpContext);

        problem.Extensions[ProblemDetailsExtensionNames.Errors] = groups.Errors;
        problem.Extensions[ProblemDetailsExtensionNames.ErrorCodes] = groups.ErrorCodes;
    }
}
