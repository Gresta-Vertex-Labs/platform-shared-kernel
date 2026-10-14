using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace SharedKernel.Presentation.WebApi;

/// <summary>
/// Completes every problem written in a request — this package's own and the framework's (an unmatched route, a
/// rejected media type, an MVC validation failure) — so all of them carry the same members.
/// </summary>
/// <remarks>
/// Installed as <see cref="ProblemDetailsOptions.CustomizeProblemDetails"/>, so it runs inside every
/// <see cref="IProblemDetailsService"/> writer, and applied directly on the rare path where no writer accepts the
/// request. Idempotent: members that are already set are kept, except that with <c>Problems:TypeBaseUri</c>
/// configured a <c>type</c> outside that base URI becomes the address of the code.
/// </remarks>
internal static class ProblemDetailsCustomizer
{
    /// <summary>
    /// The <see cref="ProblemDetailsOptions.CustomizeProblemDetails"/> entry point: completes the problem and, since it
    /// is about to be written, marks the response <c>Cache-Control: no-store</c>.
    /// </summary>
    public static void Customize(ProblemDetailsContext context)
    {
        Apply(context.HttpContext, context.ProblemDetails);

        if (!context.HttpContext.Response.HasStarted)
        {
            context.HttpContext.Response.Headers.CacheControl = ProblemResponseWriter.NoStore;
        }
    }

    /// <summary>
    /// Adds <c>instance</c>, <c>errorCode</c> (<c>http.{status}</c> when the problem has none), <c>correlationId</c>,
    /// <c>traceId</c> and <c>type</c> to <paramref name="problem"/>.
    /// </summary>
    public static void Apply(HttpContext httpContext, ProblemDetails problem)
    {
        var status = problem.Status ?? httpContext.Response.StatusCode;
        var extensions = problem.Extensions;

        problem.Instance ??= RequestFacts.GetInstance(httpContext);

        if (!extensions.TryGetValue(ProblemDetailsExtensionNames.ErrorCode, out var existing)
            || existing is not string { Length: > 0 } code)
        {
            code = ProblemFactory.CodeForStatus(status);
            extensions[ProblemDetailsExtensionNames.ErrorCode] = code;
        }

        if (httpContext.GetCorrelationId() is { } correlationId)
        {
            extensions.TryAdd(ProblemDetailsExtensionNames.CorrelationId, correlationId);
        }

        extensions.TryAdd(ProblemDetailsExtensionNames.TraceId, RequestFacts.GetTraceId(httpContext));

        ProblemDefaults.FillMissingType(problem, status);

        // A type already under the base URI was set from the real code; keep it. MVC's writer rebuilds a problem from
        // its title, type and detail before copying the extension members back, so this runs once without the code.
        if (RequestFacts.GetOptions(httpContext).Problems.TypeBaseUri is { } typeBaseUri
            && problem.Type?.StartsWith(typeBaseUri.AbsoluteUri, StringComparison.Ordinal) != true)
        {
            problem.Type = typeBaseUri.AbsoluteUri + code;
        }
    }
}
