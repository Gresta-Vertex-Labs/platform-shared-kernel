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

    /// <summary>
    /// Returns the opaque value of the one strong entity tag <c>If-Match</c> names, <c>*</c> for any, or
    /// <see langword="null"/> when it is missing, malformed, weak or lists several tags.
    /// </summary>
    public static string? GetIfMatch(HttpRequest request)
    {
        if (GetIfMatchTags(request) is not [var tag])
        {
            return null;
        }

        if (tag.Equals(EntityTagHeaderValue.Any))
        {
            return Any;
        }

        // If-Match compares strongly: a weak tag never matches, so it names no version to act on.
        return tag.IsWeak ? null : GetOpaqueTag(tag);
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
    /// Checks the <c>If-Match</c> of an endpoint that requires or accepts it (RFC 9110 section 13.1.1) and returns
    /// <see langword="null"/> when it names one strong entity tag that every <see cref="IEntityTagValidator"/> of the
    /// endpoint accepts, or is missing and not <paramref name="required"/>; otherwise the response to send. Missing or
    /// <c>*</c> is 428 when the header is required; when it is only accepted, <c>*</c> is 400 like a malformed header or
    /// several tags; a weak tag, or one that is not a version the endpoint uses, is 412.
    /// </summary>
    public static IResult? CheckIfMatch(HttpContext httpContext, EndpointMetadataCollection metadata, bool required)
    {
        var values = httpContext.Request.Headers.IfMatch;
        if (!RequestFacts.HasValue(values))
        {
            return required
                ? Reject(StatusCodes.Status428PreconditionRequired, PresentationErrorCodes.PreconditionRequired, PreconditionRequiredMessage)
                : null;
        }

        if (!EntityTagHeaderValue.TryParseStrictList(values, out var tags) || tags.Count != 1)
        {
            return Reject(StatusCodes.Status400BadRequest, PresentationErrorCodes.PreconditionInvalid, PreconditionInvalidMessage);
        }

        var tag = tags[0];
        if (tag.Equals(EntityTagHeaderValue.Any))
        {
            // * asks for any current version. A required If-Match must name one; an accepted one names one or is left
            // out, since the endpoint never checks for "any": read as missing, * would let a replace-only write create.
            return required
                ? Reject(StatusCodes.Status428PreconditionRequired, PresentationErrorCodes.PreconditionRequired, PreconditionRequiredMessage)
                : Reject(StatusCodes.Status400BadRequest, PresentationErrorCodes.PreconditionInvalid, PreconditionInvalidMessage);
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

    /// <summary>
    /// Returns the exception an <see cref="IfMatch{TVersion}"/> parameter throws for an <c>If-Match</c> it cannot bind,
    /// which <c>UseSharedKernelWebApi()</c> refuses before binding: 400, without the header's value.
    /// </summary>
    public static BadHttpRequestException UnusableIfMatch() =>
        new(PreconditionInvalidMessage, StatusCodes.Status400BadRequest);

    private static PresentationProblemResult Reject(int statusCode, string code, string message) => new(statusCode, code, message);
}
