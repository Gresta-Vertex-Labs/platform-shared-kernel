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

    private const string PreconditionInvalidMessage =
        "The If-Match header must name exactly one entity tag, such as \"42\".";

    private const string PreconditionFailedMessage =
        "The entity tag in If-Match does not match the current version.";

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

        var opaque = GetOpaqueTag(first);
        return opaque.Length == 0 ? null : opaque;
    }

    /// <summary>Returns every entity tag of <c>If-Match</c>, parsed strictly; empty when it is missing or malformed.</summary>
    public static IReadOnlyList<EntityTagHeaderValue> GetIfMatchTags(HttpRequest request) =>
        EntityTagHeaderValue.TryParseStrictList(request.Headers.IfMatch, out var tags) ? [.. tags] : [];

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
    /// Checks the <c>If-Match</c> of an endpoint that requires it (RFC 9110 section 13.1.1) and returns
    /// <see langword="null"/> when it names one strong entity tag that every <see cref="IEntityTagValidator"/> of the
    /// endpoint accepts; otherwise the response to send: 428 when it is missing or <c>*</c>, 400 when it is malformed
    /// or names several tags, 412 when the tag is weak or not a version the endpoint uses.
    /// </summary>
    public static IResult? CheckRequiredIfMatch(HttpContext httpContext, EndpointMetadataCollection metadata)
    {
        var values = httpContext.Request.Headers.IfMatch;
        if (!RequestFacts.HasValue(values))
        {
            return Reject(StatusCodes.Status428PreconditionRequired, PresentationErrorCodes.PreconditionRequired, PreconditionRequiredMessage);
        }

        if (!EntityTagHeaderValue.TryParseStrictList(values, out var tags) || tags.Count != 1)
        {
            return Reject(StatusCodes.Status400BadRequest, PresentationErrorCodes.PreconditionInvalid, PreconditionInvalidMessage);
        }

        var tag = tags[0];
        if (tag.Equals(EntityTagHeaderValue.Any))
        {
            return Reject(StatusCodes.Status428PreconditionRequired, PresentationErrorCodes.PreconditionRequired, PreconditionRequiredMessage);
        }

        // If-Match compares strongly: a weak tag never matches the current version.
        if (tag.IsWeak || metadata.GetOrderedMetadata<IEntityTagValidator>().Any(validator => !validator.IsValid(GetOpaqueTag(tag))))
        {
            return Reject(StatusCodes.Status412PreconditionFailed, PresentationErrorCodes.PreconditionFailed, PreconditionFailedMessage);
        }

        return null;
    }

    /// <summary>Returns the opaque value of <paramref name="tag"/>: its text without quotes and without <c>W/</c>.</summary>
    public static string GetOpaqueTag(EntityTagHeaderValue tag)
    {
        var quoted = tag.Tag;
        return quoted.Length >= 2 ? quoted.Subsegment(1, quoted.Length - 2).ToString() : string.Empty;
    }

    private static PresentationProblemResult Reject(int statusCode, string code, string message) => new(statusCode, code, message);
}
