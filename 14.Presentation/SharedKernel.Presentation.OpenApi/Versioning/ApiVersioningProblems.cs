using System.Collections.Frozen;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using AspVersioningProblems = Asp.Versioning.ProblemDetailsDefaults;

namespace SharedKernel.Presentation.OpenApi.Versioning;

/// <summary>
/// Gives the problems API versioning writes — an unsupported, invalid, unspecified or ambiguous API version — the
/// platform's one error shape.
/// </summary>
/// <remarks>
/// <para>
/// Asp.Versioning writes those problems through <see cref="IProblemDetailsService"/>, so the WebApi core already adds
/// <c>errorCode</c> (<c>http.{status}</c>, as for every problem the framework produces), <c>traceId</c>,
/// <c>correlationId</c> and <c>instance</c>. What remains is theirs: a <c>type</c> on a third-party site, a title that
/// is not the status reason phrase, and a <c>code</c> member no other error has. This replaces the first two with the
/// framework defaults for the status and removes the third.
/// </para>
/// <para>
/// A problem is recognized by that <c>code</c>, not by its <c>type</c>, so the result is the same whether this runs
/// before or after the core's customization, which rewrites <c>type</c> when <c>Problems:TypeBaseUri</c> is set.
/// </para>
/// </remarks>
internal static class ApiVersioningProblems
{
    /// <summary>The extension member Asp.Versioning writes its own code into.</summary>
    internal const string CodeMember = "code";

    private static readonly FrozenSet<string> Codes = new[]
    {
        AspVersioningProblems.Unsupported.Code,
        AspVersioningProblems.Unspecified.Code,
        AspVersioningProblems.Invalid.Code,
        AspVersioningProblems.Ambiguous.Code,
    }.OfType<string>().ToFrozenSet(StringComparer.Ordinal);

    private static readonly FrozenSet<string> Types = new[]
    {
        AspVersioningProblems.Unsupported.Type,
        AspVersioningProblems.Unspecified.Type,
        AspVersioningProblems.Invalid.Type,
        AspVersioningProblems.Ambiguous.Type,
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// Installs <see cref="Normalize"/> ahead of the <see cref="ProblemDetailsOptions.CustomizeProblemDetails"/>
    /// configured so far.
    /// </summary>
    public static void Chain(ProblemDetailsOptions options)
    {
        var next = options.CustomizeProblemDetails;

        options.CustomizeProblemDetails = context =>
        {
            Normalize(context.ProblemDetails);
            next?.Invoke(context);
        };
    }

    /// <summary>Rewrites <paramref name="problem"/> when API versioning wrote it; leaves every other problem alone.</summary>
    public static void Normalize(ProblemDetails problem)
    {
        if (!problem.Extensions.TryGetValue(CodeMember, out var value) || value is not string code || !Codes.Contains(code))
        {
            return;
        }

        problem.Extensions.Remove(CodeMember);
        problem.Title = null;

        if (problem.Type is null || Types.Contains(problem.Type))
        {
            problem.Type = null;
        }

        // Constructing the framework's problem result fills a missing title and type with its defaults for the status
        // on this same instance: the reason phrase and the RFC 9110 section.
        _ = TypedResults.Problem(problem);
    }
}
