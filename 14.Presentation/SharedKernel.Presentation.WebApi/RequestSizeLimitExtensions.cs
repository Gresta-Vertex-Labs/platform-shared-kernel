using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;

namespace SharedKernel.Presentation.WebApi;

/// <summary>Raises or removes the request body size limit of individual endpoints.</summary>
/// <remarks>
/// The service-wide limit is <c>Limits:MaxRequestBodySize</c> (4 MiB by default). These conventions attach the
/// framework's own <see cref="RequestSizeLimitAttribute"/> and <see cref="DisableRequestSizeLimitAttribute"/>
/// metadata, which routing applies before the endpoint reads the body; MVC actions use the attributes directly.
/// A body over the limit is answered 413 with the code <c>request.too_large</c>.
/// </remarks>
public static class RequestSizeLimitExtensions
{
    /// <summary>Sets the largest request body the endpoints of <paramref name="builder"/> accept.</summary>
    /// <typeparam name="TBuilder">The endpoint convention builder type.</typeparam>
    /// <param name="builder">The endpoint, group or controller mapping.</param>
    /// <param name="bytes">The limit in bytes; must be greater than zero.</param>
    /// <returns>The same <paramref name="builder"/>.</returns>
    public static TBuilder WithRequestSizeLimit<TBuilder>(this TBuilder builder, long bytes)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytes);

        return builder.WithMetadata(new RequestSizeLimitAttribute(bytes));
    }

    /// <summary>
    /// Removes the request body size limit of the endpoints of <paramref name="builder"/>. Only for endpoints that
    /// stream a body of known, trusted origin; prefer a presigned upload straight to storage.
    /// </summary>
    /// <typeparam name="TBuilder">The endpoint convention builder type.</typeparam>
    /// <param name="builder">The endpoint, group or controller mapping.</param>
    /// <returns>The same <paramref name="builder"/>.</returns>
    public static TBuilder DisableRequestSizeLimit<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithMetadata(new DisableRequestSizeLimitAttribute());
    }
}
