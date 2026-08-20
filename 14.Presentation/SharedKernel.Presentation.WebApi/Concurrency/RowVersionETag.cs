namespace SharedKernel.Presentation.WebApi.Concurrency;

/// <summary>
/// Produces a well-formed, correctly-quoted <c>ETag</c> header value from a
/// <c>06.Persistence</c> <c>IHasConcurrency.RowVersion</c>-shaped optimistic-concurrency token.
/// </summary>
/// <remarks>
/// Additive helper surface — adoption is optional per consuming service. Bridges
/// <c>06.Persistence</c>'s row-version optimistic concurrency (mapped to
/// <see cref="SharedKernel.Primitives.Errors.Error.Conflict"/>/409) to HTTP's own standard
/// conditional-request mechanism (<c>ETag</c>/<c>If-Match</c>, RFC 9110 §13). Never a replacement
/// for <see cref="SharedKernel.Primitives.Errors.Error.Conflict"/> — a service may use either,
/// both, or neither.
/// </remarks>
public static class RowVersionETag
{
    /// <summary>
    /// Converts the specified <paramref name="rowVersion"/> token into a strong, quoted
    /// <c>ETag</c> header value.
    /// </summary>
    /// <param name="rowVersion">The row-version/concurrency-token bytes.</param>
    /// <returns>
    /// A base64-encoded, double-quoted strong <c>ETag</c> value (e.g. <c>"AAAAAAAAB9E="</c>) safe
    /// to assign directly to <see cref="Microsoft.AspNetCore.Http.HttpResponse.Headers"/>'s
    /// <c>ETag</c> entry.
    /// </returns>
    public static string From(byte[] rowVersion)
    {
        ArgumentNullException.ThrowIfNull(rowVersion);
        return $"\"{Convert.ToBase64String(rowVersion)}\"";
    }
}
