using Microsoft.AspNetCore.Builder;
using SharedKernel.Presentation.WebApi.SecurityHeaders;

namespace SharedKernel.Presentation.WebApi;

/// <summary>Sets the <c>Content-Security-Policy</c> of individual endpoints.</summary>
public static class ContentSecurityPolicyExtensions
{
    /// <summary>
    /// Replaces the configured <c>Content-Security-Policy</c> (<c>SecurityHeaders:ContentSecurityPolicy</c>) for the
    /// endpoints of <paramref name="builder"/>.
    /// </summary>
    /// <typeparam name="TBuilder">The endpoint convention builder type.</typeparam>
    /// <param name="builder">The endpoint, group or controller mapping.</param>
    /// <param name="policy">
    /// The policy to send, or <see langword="null"/> to send none — for an endpoint that serves a page, such as an
    /// API reference UI, which the strict API default would block from loading its scripts and styles.
    /// </param>
    /// <returns>The same <paramref name="builder"/>.</returns>
    public static TBuilder WithContentSecurityPolicy<TBuilder>(this TBuilder builder, string? policy)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.WithMetadata(new ContentSecurityPolicyMetadata(string.IsNullOrWhiteSpace(policy) ? null : policy));
    }
}
