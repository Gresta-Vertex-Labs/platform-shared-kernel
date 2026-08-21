using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using SharedKernel.Presentation.WebApi.Http;
using SharedKernel.Primitives.Logging;

namespace SharedKernel.Presentation.WebApi.RateLimiting;

/// <summary>
/// Shapes a rate-limiter rejection into an RFC 9457 <see cref="ProblemDetails"/> 429 response —
/// the only sanctioned way to produce that body in a consuming service.
/// </summary>
/// <remarks>
/// <para>
/// Closes the handoff <c>13.ServiceDefaults</c>'s own <c>AddSharedKernelRateLimiting()</c> already
/// defers here without a hard reference: its <c>RateLimiterOptions.OnRejected</c> callback should
/// call <see cref="Create"/> directly rather than hand-rolling a 429 body — mirroring this
/// platform's inline-<see cref="ProblemDetails"/>-construction prohibition applied to every other
/// error shape. Referenced by name only — no <c>ProjectReference</c> either direction between the
/// two domains.
/// </para>
/// <para>
/// Reuses the shared internal RFC 9457 <see cref="ProblemDetailsShaping"/> helper — the same one
/// the 412 <c>If-Match</c> path (<c>ConditionalRequestExtensions</c>) uses — rather than
/// duplicating the <c>Type</c>-URI/<c>traceId</c> construction a second time. Deliberately NOT
/// routed through <see cref="SharedKernel.Primitives.Errors.Error"/>/
/// <see cref="SharedKernel.Primitives.Errors.ErrorType"/>: a rate-limit rejection is an
/// HTTP-protocol-native outcome that never originates as a domain <c>Error</c>. Zero new
/// <c>PackageReference</c> — built entirely on the existing
/// <c>Microsoft.AspNetCore.Http</c>/<c>Microsoft.AspNetCore.Mvc.ProblemDetails</c> surface.
/// </para>
/// </remarks>
public static partial class RateLimitRejectionProblemDetails
{
    private const string TooManyRequestsTitle = "Too Many Requests";
    private const string TooManyRequestsDetail = "Too many requests have been made in a given amount of time. Please retry later.";
    private const string UnknownPolicyName = "(unspecified)";

    /// <summary>
    /// Builds a 429 <see cref="ProblemDetails"/> for a rate-limiter rejection, and — when
    /// <paramref name="retryAfter"/> is supplied — sets the standard <c>Retry-After</c> response
    /// header (not just a body field).
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="retryAfter">
    /// An optional duration after which the caller may retry. When supplied, this value is written
    /// to a real <c>Retry-After</c> HTTP response header (in whole seconds) so proxies/client SDKs
    /// that already understand <c>Retry-After</c> work unmodified.
    /// </param>
    /// <param name="policyName">
    /// An optional rate-limit policy name, included in the security-audit log record — e.g. the name
    /// passed to <c>AddSharedKernelRateLimiting</c>'s named policy (<c>13.ServiceDefaults</c>).
    /// Never included in the response body itself.
    /// </param>
    /// <returns>
    /// A <see cref="ProblemDetails"/> with <c>Status</c> 429, an RFC 9457 <c>Type</c> URI, and
    /// <c>Extensions["traceId"]</c> populated identically to every other error path.
    /// </returns>
    public static ProblemDetails Create(HttpContext context, TimeSpan? retryAfter = null, string? policyName = null)
    {
        ArgumentNullException.ThrowIfNull(context);

        var problemDetails = ProblemDetailsShaping.Create(
            StatusCodes.Status429TooManyRequests,
            TooManyRequestsTitle,
            TooManyRequestsDetail,
            context);

        if (retryAfter is { } delay)
        {
            var seconds = Math.Max(0, (long)Math.Ceiling(delay.TotalSeconds));
            context.Response.Headers[HeaderNames.RetryAfter] = seconds.ToString(CultureInfo.InvariantCulture);
        }

        var logger = context.RequestServices?.GetService<ILoggerFactory>()?.CreateLogger(LoggerCategoryName);
        if (logger is not null)
        {
            Log.RateLimitRejected(logger, context.Request.Path.Value ?? string.Empty, policyName ?? UnknownPolicyName);
        }

        return problemDetails;
    }

    private static readonly string LoggerCategoryName = typeof(RateLimitRejectionProblemDetails).FullName!;

    /// <summary>
    /// Source-generated log messages for <see cref="RateLimitRejectionProblemDetails"/>.
    /// </summary>
    /// <remarks>
    /// This type has no DI-resolved instance (it is a pure static helper), so the logger is
    /// resolved per call from <see cref="HttpContext.RequestServices"/> rather than injected via a
    /// constructor.
    /// </remarks>
    private static partial class Log
    {
        [LoggerMessage(
            EventId = LoggingEventIdRanges.Presentation + 5,
            Level = LogLevel.Warning,
            Message = "Rate limit policy {PolicyName} rejected a request to {RequestPath}.")]
        public static partial void RateLimitRejected(ILogger logger, string requestPath, string policyName);
    }
}
