using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Headers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using SharedKernel.Presentation.WebApi.Http;

namespace SharedKernel.Presentation.WebApi.Concurrency;

/// <summary>
/// Evaluates an inbound <c>If-Match</c> request header against a resource's current
/// <c>ETag</c>, producing a 412 Precondition Failed <see cref="ProblemDetails"/> on mismatch.
/// </summary>
/// <remarks>
/// <para>
/// A 412 response is constructed directly via the shared internal RFC 9457
/// <see cref="ProblemDetailsShaping"/> helper — deliberately NOT routed through
/// <see cref="SharedKernel.Primitives.Errors.Error"/>/<see cref="SharedKernel.Primitives.Errors.ErrorType"/>.
/// 412 is an HTTP-protocol-native conditional-request outcome that never originates as a domain
/// <c>Error</c>/<c>Result&lt;T&gt;</c> failure, unlike the platform's <c>ErrorType</c>-mapped cases.
/// </para>
/// <para>
/// Additive to, never a replacement for,
/// <see cref="SharedKernel.Primitives.Errors.Error.Conflict(string, string)"/>/409 — a service may
/// use either, both, or neither. Zero change to <c>ErrorTypeStatusCodeMap</c>'s existing mapping or
/// to <c>06.Persistence</c>'s own concurrency-conflict behavior.
/// </para>
/// </remarks>
public static class ConditionalRequestExtensions
{
    private const string PreconditionFailedTitle = "Precondition Failed";

    /// <summary>
    /// Evaluates the inbound <c>If-Match</c> header on <paramref name="context"/> against
    /// <paramref name="currentETag"/>.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="currentETag">
    /// The resource's current <c>ETag</c> value, typically produced by
    /// <see cref="RowVersionETag.From"/>.
    /// </param>
    /// <param name="problemDetails">
    /// The 412 <see cref="ProblemDetails"/> to return when this method returns
    /// <see langword="false"/>; otherwise <see langword="null"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when no <c>If-Match</c> header is present (no precondition was
    /// requested) or when at least one supplied entity tag matches <paramref name="currentETag"/>
    /// under RFC 9110 strong comparison (or is <c>*</c>); otherwise <see langword="false"/>.
    /// </returns>
    public static bool TryValidateIfMatch(this HttpContext context, string currentETag, out ProblemDetails? problemDetails)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(currentETag);

        var ifMatch = new RequestHeaders(context.Request.Headers).IfMatch;

        if (ifMatch is null || ifMatch.Count == 0)
        {
            problemDetails = null;
            return true;
        }

        var current = EntityTagHeaderValue.Parse(currentETag);
        var isSatisfied = ifMatch.Any(tag => tag.Equals(EntityTagHeaderValue.Any) || tag.Compare(current, useStrongComparison: true));

        if (isSatisfied)
        {
            problemDetails = null;
            return true;
        }

        problemDetails = ProblemDetailsShaping.Create(
            StatusCodes.Status412PreconditionFailed,
            PreconditionFailedTitle,
            $"The If-Match header did not match the current resource ETag {currentETag}.",
            context);

        return false;
    }
}
