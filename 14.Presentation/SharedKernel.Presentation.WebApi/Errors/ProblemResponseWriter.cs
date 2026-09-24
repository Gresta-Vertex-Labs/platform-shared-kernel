using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using SharedKernel.Presentation.WebApi.Http;

namespace SharedKernel.Presentation.WebApi.Errors;

/// <summary>The one way this package writes an error response.</summary>
internal static class ProblemResponseWriter
{
    /// <summary>The RFC 9457 media type of every error response.</summary>
    public const string ContentType = "application/problem+json";

    /// <summary>The <c>Cache-Control</c> of every error response: an error must never be served from a cache.</summary>
    public const string NoStore = "no-store";

    /// <summary>
    /// Sets the status and <c>Cache-Control: no-store</c>, adds <c>Retry-After</c> to a 503 when configured, and
    /// writes <paramref name="problem"/> through <see cref="IProblemDetailsService"/>; when no writer accepts the
    /// request (its <c>Accept</c> header excludes JSON) or a writer accepts it but writes nothing (MVC's writer does
    /// that for a controller without <c>[ApiController]</c>), the problem is written as
    /// <c>application/problem+json</c> directly.
    /// </summary>
    public static async Task WriteAsync(HttpContext httpContext, ProblemDetails problem)
    {
        var response = httpContext.Response;
        var statusCode = problem.Status ?? StatusCodes.Status500InternalServerError;

        response.StatusCode = statusCode;

        // The body is the problem, whatever the endpoint meant to send; clearing the type also tells a writer that
        // wrote something (it sets the type) apart from one that only claimed the problem.
        response.ContentType = null;
        response.Headers.CacheControl = NoStore;
        AddRetryAfter(httpContext, statusCode);

        var service = httpContext.RequestServices.GetService<IProblemDetailsService>();
        if (service is not null
            && await service.TryWriteAsync(new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = problem }).ConfigureAwait(false)
            && (response.HasStarted || !string.IsNullOrEmpty(response.ContentType)))
        {
            return;
        }

        ProblemDetailsCustomizer.Apply(httpContext, problem);
        await response
            .WriteAsJsonAsync(problem, (JsonSerializerOptions?)null, ContentType, httpContext.RequestAborted)
            .ConfigureAwait(false);
    }

    private static void AddRetryAfter(HttpContext httpContext, int statusCode)
    {
        if (statusCode != StatusCodes.Status503ServiceUnavailable
            || httpContext.Response.Headers.ContainsKey(HeaderNames.RetryAfter)
            || RequestFacts.GetOptions(httpContext).Problems.UnavailableRetryAfter is not { } retryAfter)
        {
            return;
        }

        httpContext.Response.Headers[HeaderNames.RetryAfter] = ToSeconds(retryAfter);
    }

    /// <summary>Formats a delay as whole seconds, rounded up, for <c>Retry-After</c>.</summary>
    internal static string ToSeconds(TimeSpan delay) =>
        Math.Max(0L, (long)Math.Ceiling(delay.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
}
