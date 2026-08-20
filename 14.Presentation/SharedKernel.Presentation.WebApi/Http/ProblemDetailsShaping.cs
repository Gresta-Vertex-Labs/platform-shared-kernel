using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace SharedKernel.Presentation.WebApi.Http;

/// <summary>
/// Shared internal RFC 9457 <see cref="ProblemDetails"/> shaping primitives — the
/// <c>Type</c>-URI/<c>traceId</c>-population convention common to every
/// <see cref="ProblemDetails"/> this domain produces.
/// </summary>
/// <remarks>
/// <para>
/// Extracted from <c>ErrorProblemDetailsExtensions</c>'s original private
/// <c>ProblemTypeBaseUri</c> constant/construction pattern — zero behavioral change to that type's
/// existing public output. Reused by the 412 (<c>ConditionalRequestExtensions</c>) and 429
/// (<c>RateLimitRejectionProblemDetails</c>) helpers, both HTTP-protocol-native outcomes that never
/// originate as a domain <c>Error</c>, so both share this one convention instead of two
/// independently hand-rolled ones.
/// </para>
/// <para>Internal — not part of this package's public surface.</para>
/// </remarks>
internal static class ProblemDetailsShaping
{
    private const string ProblemTypeBaseUri = "https://httpstatuses.io/";

    /// <summary>Builds the RFC 9457 <c>Type</c> URI for the specified HTTP status code.</summary>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <returns>The RFC 9457 status URI (e.g. <c>"https://httpstatuses.io/404"</c>).</returns>
    public static string BuildTypeUri(int statusCode) => $"{ProblemTypeBaseUri}{statusCode}";

    /// <summary>
    /// Resolves the trace identifier for <c>Extensions["traceId"]</c>: the current
    /// <see cref="Activity"/> id when available, otherwise <paramref name="context"/>'s
    /// <see cref="HttpContext.TraceIdentifier"/>.
    /// </summary>
    /// <param name="context">The current <see cref="HttpContext"/>, or <see langword="null"/>.</param>
    /// <returns>The resolved trace identifier, or <see langword="null"/> when neither is available.</returns>
    public static string? ResolveTraceId(HttpContext? context) => Activity.Current?.Id ?? context?.TraceIdentifier;

    /// <summary>
    /// Builds a <see cref="ProblemDetails"/> with <c>Status</c>/<c>Type</c>/
    /// <c>Extensions["traceId"]</c> populated per this domain's shared convention.
    /// </summary>
    /// <param name="statusCode">The HTTP status code.</param>
    /// <param name="title">The <c>Title</c> value.</param>
    /// <param name="detail">The <c>Detail</c> value.</param>
    /// <param name="context">The current <see cref="HttpContext"/>, used to resolve the trace id when available.</param>
    /// <returns>A <see cref="ProblemDetails"/> shaped per RFC 9457.</returns>
    public static ProblemDetails Create(int statusCode, string title, string? detail, HttpContext? context)
    {
        var problemDetails = new ProblemDetails
        {
            Title = title,
            Detail = detail,
            Status = statusCode,
            Type = BuildTypeUri(statusCode),
        };

        problemDetails.Extensions["traceId"] = ResolveTraceId(context);

        return problemDetails;
    }
}
