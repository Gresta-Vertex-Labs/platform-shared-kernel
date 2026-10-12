using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace SharedKernel.Presentation.WebApi;

/// <summary>
/// The <c>type</c> and <c>title</c> a problem of a given status carries: the framework's defaults, the same every
/// <see cref="IProblemDetailsService"/> writer applies, so a problem built here, a framework problem and a GraphQL
/// error read alike.
/// </summary>
/// <remarks>
/// The one place a <see cref="ProblemDetails"/> is built only to read its defaults. GraphQL takes them from here
/// instead of constructing a problem of its own.
/// </remarks>
internal static class ProblemDefaults
{
    // The framework's defaults give these statuses a title but no type; RFC 6585 defines both.
    private const string PreconditionRequiredType = "https://tools.ietf.org/html/rfc6585#section-3";

    private const string TooManyRequestsType = "https://tools.ietf.org/html/rfc6585#section-4";

    /// <summary>Applies the framework's <c>type</c> and <c>title</c> for the problem's status to <paramref name="problem"/>.</summary>
    /// <param name="problem">A problem with its <see cref="ProblemDetails.Status"/> set.</param>
    public static void Apply(ProblemDetails problem)
    {
        // Constructing the framework's problem result applies its defaults to this instance: the reason phrase as
        // title and the RFC section as type, exactly as every IProblemDetailsService writer would.
        _ = TypedResults.Problem(problem);

        FillMissingType(problem, problem.Status ?? 0);
    }

    /// <summary>
    /// Gives a problem of status 428 or 429, for which the framework has a title but no type, the RFC 6585 type it
    /// lacks. Any other problem is untouched.
    /// </summary>
    /// <param name="problem">The problem.</param>
    /// <param name="status">The status the problem is answered with.</param>
    public static void FillMissingType(ProblemDetails problem, int status)
    {
        if (string.IsNullOrEmpty(problem.Type))
        {
            problem.Type = status switch
            {
                StatusCodes.Status428PreconditionRequired => PreconditionRequiredType,
                StatusCodes.Status429TooManyRequests => TooManyRequestsType,
                _ => problem.Type,
            };
        }
    }

    /// <summary>Returns the framework's <c>type</c> and <c>title</c> for <paramref name="statusCode"/>.</summary>
    /// <param name="statusCode">The HTTP status.</param>
    /// <returns>The type URI and the reason phrase; either is <see langword="null"/> when the framework has none.</returns>
    public static (string? Type, string? Title) For(int statusCode)
    {
        var problem = new ProblemDetails { Status = statusCode };
        Apply(problem);
        return (problem.Type, problem.Title);
    }
}
