using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using SharedKernel.Presentation.WebApi.Errors;

namespace SharedKernel.Presentation.WebApi.Http;

/// <summary>Entity tag handling shared by the conditional-request helpers.</summary>
internal static class EntityTags
{
    /// <summary>The <c>*</c> an <c>If-Match</c> header may carry instead of entity tags.</summary>
    public const string Any = "*";

    private const string PreconditionRequiredMessage =
        "This request requires an If-Match header with the entity tag of the version it changes.";

    /// <summary>Wraps <paramref name="version"/> in double quotes as a strong entity tag, after checking it can be one.</summary>
    public static string ToEntityTag(string version, string parameterName)
    {
        ArgumentException.ThrowIfNullOrEmpty(version, parameterName);

        // RFC 9110 etagc: visible ASCII except the double quote.
        if (version.AsSpan().ContainsAnyExceptInRange('!', '~') || version.Contains('"', StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "A version used as an entity tag must consist of visible ASCII characters other than '\"'.",
                parameterName);
        }

        return $"\"{version}\"";
    }

    /// <summary>Returns the first entity tag of <c>If-Match</c> without quotes or <c>W/</c>, <c>*</c> for any, or <see langword="null"/>.</summary>
    public static string? GetIfMatch(HttpRequest request)
    {
        var tags = request.GetTypedHeaders().IfMatch;
        if (tags.Count == 0)
        {
            return null;
        }

        var first = tags[0];
        if (first.Equals(EntityTagHeaderValue.Any))
        {
            return Any;
        }

        var tag = first.Tag;
        return tag.Length > 2 ? tag.Subsegment(1, tag.Length - 2).ToString() : null;
    }

    /// <summary>
    /// Returns <see langword="true"/> when <c>If-None-Match</c> matches <paramref name="entityTag"/> under the weak
    /// comparison of RFC 9110 section 13.1.2, or is <c>*</c>.
    /// </summary>
    public static bool IfNoneMatchMatches(HttpRequest request, string entityTag)
    {
        var tags = request.GetTypedHeaders().IfNoneMatch;
        if (tags.Count == 0)
        {
            return false;
        }

        var current = new EntityTagHeaderValue(entityTag);
        return tags.Any(tag => tag.Equals(EntityTagHeaderValue.Any) || tag.Compare(current, useStrongComparison: false));
    }

    /// <summary>
    /// Returns <see langword="null"/> when the request has a usable <c>If-Match</c>; otherwise the 428 Precondition
    /// Required response to send.
    /// </summary>
    public static IResult? CheckIfMatchPresent(HttpContext httpContext) =>
        GetIfMatch(httpContext.Request) is null
            ? new PresentationProblemResult(
                StatusCodes.Status428PreconditionRequired,
                PresentationErrorCodes.PreconditionRequired,
                PreconditionRequiredMessage)
            : null;
}
