using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Presentation.WebApi.Errors;

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

        // Constructing the framework's problem result applies its defaults to this instance: the reason phrase as
        // title and the RFC section as type, exactly as every IProblemDetailsService writer would.
        _ = TypedResults.Problem(problem);

        ProblemDetailsCustomizer.Apply(httpContext, problem);
        return problem;
    }

    /// <summary>
    /// Adds <c>errors</c> and <c>errorCodes</c>: both keyed by each error's field path (its
    /// <see cref="ErrorArgumentNames.PropertyPath"/> argument) or, when it names no field, by its code, in the order
    /// the keys first appear. <c>errorCodes[key][i]</c> is the code of the error whose message is
    /// <c>errors[key][i]</c>; each message is shown and translated on its own.
    /// </summary>
    private static void AddFieldErrors(ProblemDetails problem, IReadOnlyList<Error> errors, HttpContext httpContext)
    {
        var groups = errors.GroupBy(KeyOf, StringComparer.Ordinal).ToArray();

        problem.Extensions[ProblemDetailsExtensionNames.Errors] = groups.ToDictionary(
            group => group.Key,
            group => group.Select(error => ErrorPresentation.GetClientMessage(error, httpContext)).ToArray(),
            StringComparer.Ordinal);

        problem.Extensions[ProblemDetailsExtensionNames.ErrorCodes] = groups.ToDictionary(
            group => group.Key,
            group => group.Select(error => error.Code).ToArray(),
            StringComparer.Ordinal);
    }

    private static string KeyOf(Error error) =>
        error.MessageArguments.TryGetValue(ErrorArgumentNames.PropertyPath, out var path) && path is string { Length: > 0 } field
            ? field
            : error.Code;
}
