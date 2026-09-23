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
/// returns it with <c>ToOkWithETag(…)</c>, an update requires it with <c>RequireIfMatch()</c> and reads it with
/// <see cref="GetIfMatch"/>. When the update then fails with a <see cref="Primitives.Errors.ErrorType.Conflict"/>
/// because the version is no longer current, the response is 412 Precondition Failed.
/// </remarks>
public static class ConditionalRequestExtensions
{
    /// <summary>
    /// Requires an <c>If-Match</c> header on the endpoints of <paramref name="builder"/>: a request without one is
    /// answered 428 Precondition Required before the handler runs, and a
    /// <see cref="Primitives.Errors.ErrorType.Conflict"/> failure of the endpoint is answered 412 Precondition Failed.
    /// </summary>
    /// <typeparam name="TBuilder">The endpoint convention builder type.</typeparam>
    /// <param name="builder">A minimal-API endpoint or group. For MVC use <see cref="RequireIfMatchAttribute"/>.</param>
    /// <returns>The same <paramref name="builder"/>.</returns>
    public static TBuilder RequireIfMatch<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder
            .WithMetadata(new RequireIfMatchAttribute())
            .AddEndpointFilter(IfMatchEndpointFilter.Instance);
    }

    /// <summary>Returns the version the request's <c>If-Match</c> header names.</summary>
    /// <param name="httpContext">The current request.</param>
    /// <returns>
    /// The first entity tag without its quotes and without <c>W/</c> — ready for <c>EntityVersion.TryParse</c> —;
    /// <c>*</c> when the header is <c>*</c> (any current version); <see langword="null"/> when the header is missing
    /// or malformed.
    /// </returns>
    public static string? GetIfMatch(this HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        return EntityTags.GetIfMatch(httpContext.Request);
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
