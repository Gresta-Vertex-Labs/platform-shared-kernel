using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using SharedKernel.Presentation.WebApi.Http;

namespace SharedKernel.Presentation.WebApi;

/// <summary>
/// Optimistic concurrency over HTTP: send the version as an <c>ETag</c>, require it back in <c>If-Match</c>, and
/// answer a stale version with 412.
/// </summary>
/// <remarks>
/// The version is an opaque token, typically <c>EntityVersion.ToString()</c> from the persistence layer: a read
/// returns it with <c>ToOkWithETag(…)</c>, an update requires it with <c>RequireIfMatch()</c> (or an
/// <see cref="IfMatch{TVersion}"/> parameter) — or accepts it with <c>AcceptIfMatch()</c> (or a nullable
/// <see cref="IfMatch{TVersion}"/> parameter) when a request without it is unconditional — and reads it with
/// <see cref="GetIfMatch"/>. When the update then fails because the version is no longer current
/// (<c>persistence.concurrency_conflict</c>), the response is 412 Precondition Failed.
/// </remarks>
public static class ConditionalRequestExtensions
{
    /// <summary>
    /// Requires an <c>If-Match</c> header naming one strong entity tag on the endpoints of <paramref name="builder"/>,
    /// checked before the handler runs: missing or <c>*</c> is 428, malformed or several tags 400, a weak tag 412.
    /// </summary>
    /// <typeparam name="TBuilder">The endpoint convention builder type.</typeparam>
    /// <param name="builder">An endpoint, group or controller mapping.</param>
    /// <returns>The same <paramref name="builder"/>.</returns>
    /// <remarks>
    /// Adds <see cref="RequireIfMatchAttribute"/> as endpoint metadata, which <c>UseSharedKernelWebApi()</c> enforces
    /// for every kind of endpoint; see that attribute for the rules.
    /// </remarks>
    public static TBuilder RequireIfMatch<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithMetadata(new RequireIfMatchAttribute());
    }

    /// <summary>
    /// Accepts an optional <c>If-Match</c> header on the endpoints of <paramref name="builder"/>: a request without it
    /// goes on, unconditional; one with it must name one strong entity tag, checked before the handler runs —
    /// malformed, several tags or <c>*</c> is 400, a weak tag 412.
    /// </summary>
    /// <typeparam name="TBuilder">The endpoint convention builder type.</typeparam>
    /// <param name="builder">An endpoint, group or controller mapping.</param>
    /// <returns>The same <paramref name="builder"/>.</returns>
    /// <remarks>
    /// Adds <see cref="AcceptIfMatchAttribute"/> as endpoint metadata, which <c>UseSharedKernelWebApi()</c> enforces
    /// for every kind of endpoint; see that attribute for the rules. <see cref="RequireIfMatch{TBuilder}"/> on the same
    /// endpoint wins.
    /// </remarks>
    public static TBuilder AcceptIfMatch<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithMetadata(new AcceptIfMatchAttribute());
    }

    /// <summary>Returns the version the request's <c>If-Match</c> header names.</summary>
    /// <param name="httpContext">The current request.</param>
    /// <returns>
    /// The one strong entity tag the header names, without its quotes — ready for <c>EntityVersion.TryParse</c> —;
    /// <c>*</c> when the header is <c>*</c> (any current version); otherwise <see langword="null"/>: the header is
    /// missing, malformed, weak or lists several tags.
    /// </returns>
    /// <remarks>
    /// On an endpoint that requires or accepts <c>If-Match</c> — <c>RequireIfMatch()</c>, <c>AcceptIfMatch()</c>,
    /// their attributes or an <see cref="IfMatch{TVersion}"/> parameter — the header was validated before the handler
    /// ran, so <see langword="null"/> means the request sent none. Elsewhere <see langword="null"/> also covers a header
    /// that cannot be used, and reading it as "no precondition" would make a conditional request unconditional: declare
    /// the header instead of reading it raw. <see cref="GetIfMatchTags"/> lists every tag and whether it is weak.
    /// </remarks>
    public static string? GetIfMatch(this HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        return EntityTags.GetIfMatch(httpContext.Request);
    }

    /// <summary>Returns every entity tag the request's <c>If-Match</c> header lists.</summary>
    /// <param name="httpContext">The current request.</param>
    /// <returns>
    /// The tags in the order sent, parsed strictly (<see cref="EntityTagHeaderValue.Any"/> for <c>*</c>); empty when the
    /// header is missing or malformed. <see cref="EntityTagHeaderValue.Tag"/> keeps its quotes and
    /// <see cref="EntityTagHeaderValue.IsWeak"/> tells a weak tag, which never matches under <c>If-Match</c>'s strong
    /// comparison (RFC 9110 section 13.1.1).
    /// </returns>
    public static IReadOnlyList<EntityTagHeaderValue> GetIfMatchTags(this HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        return EntityTags.GetIfMatchTags(httpContext.Request);
    }

    /// <summary>Sets the <c>ETag</c> response header to <paramref name="version"/> as a strong entity tag.</summary>
    /// <param name="response">The response.</param>
    /// <param name="version">The version, such as <c>EntityVersion.ToString()</c>; visible ASCII other than <c>"</c>.</param>
    /// <exception cref="ArgumentException"><paramref name="version"/> is empty or cannot be an entity tag.</exception>
    public static void SetETag(this HttpResponse response, string version)
    {
        ArgumentNullException.ThrowIfNull(response);

        response.Headers[HeaderNames.ETag] = EntityTags.ToEntityTag(version, nameof(version));
    }
}
