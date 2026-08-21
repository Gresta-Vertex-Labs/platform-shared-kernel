using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace SharedKernel.Presentation.WebApi.Uploads;

/// <summary>
/// DI registration and Minimal API sugar for <see cref="UploadValidationEndpointFilter"/>.
/// </summary>
public static class UploadValidationEndpointFilterExtensions
{
    /// <summary>
    /// Registers the platform-default <see cref="UploadValidationOptions"/> and
    /// <see cref="UploadValidationEndpointFilter"/> as a singleton.
    /// </summary>
    /// <param name="services">The service collection to add registrations to.</param>
    /// <param name="configure">Configures the platform-default upload validation limits.</param>
    /// <returns>The same <paramref name="services"/> instance, for chaining.</returns>
    /// <remarks>
    /// Mirrors <c>AddSharedKernelCors</c>'s single-call registration shape.
    /// <b>This registration alone does not attach the filter to any endpoint.</b> The consumer must
    /// additionally call <c>.AddEndpointFilter&lt;UploadValidationEndpointFilter&gt;()</c> on
    /// <c>MapControllers()</c> and/or each minimal-API route group — this is the one place this
    /// domain's "convention over configuration" philosophy cannot fully deliver a zero-wiring
    /// default, mirroring the identical caveat on <c>AddSharedKernelAuthorizationFilters</c>/
    /// <c>AddSharedKernelIdempotencyFilters</c>.
    /// </remarks>
    public static IServiceCollection AddSharedKernelUploadValidation(
        this IServiceCollection services,
        Action<UploadValidationOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new UploadValidationOptions();
        configure(options);

        services.AddSingleton(options);
        services.AddSingleton<UploadValidationEndpointFilter>();

        return services;
    }

    /// <summary>
    /// Attaches a <see cref="RequireValidatedUploadAttribute"/> to the endpoint produced by
    /// <paramref name="builder"/>.
    /// </summary>
    /// <param name="builder">The route handler builder to attach metadata to.</param>
    /// <param name="maxSizeBytes">
    /// The maximum accepted upload size, in bytes, for this endpoint, or <see langword="null"/> to
    /// fall back to <see cref="UploadValidationOptions.MaxSizeBytes"/>.
    /// </param>
    /// <param name="allowedContentTypes">
    /// The set of accepted <c>Content-Type</c> values for this endpoint. An empty set falls back to
    /// <see cref="UploadValidationOptions.AllowedContentTypes"/>.
    /// </param>
    /// <returns>The same <paramref name="builder"/>, for chaining.</returns>
    /// <remarks>
    /// Purely metadata attachment — this does not itself perform the check or register
    /// <see cref="UploadValidationEndpointFilter"/> on the pipeline. The consumer must additionally
    /// call <c>.AddEndpointFilter&lt;UploadValidationEndpointFilter&gt;()</c> — see
    /// <see cref="AddSharedKernelUploadValidation"/>.
    /// </remarks>
    public static RouteHandlerBuilder RequireValidatedUpload(
        this RouteHandlerBuilder builder,
        long? maxSizeBytes = null,
        params string[] allowedContentTypes)
        => builder.WithMetadata(new RequireValidatedUploadAttribute(maxSizeBytes, allowedContentTypes));

    /// <summary>
    /// Attaches a <see cref="RequireValidatedUploadAttribute"/> to every endpoint produced by
    /// <paramref name="builder"/>.
    /// </summary>
    /// <param name="builder">The route group builder to attach metadata to.</param>
    /// <param name="maxSizeBytes">
    /// The maximum accepted upload size, in bytes, for this endpoint, or <see langword="null"/> to
    /// fall back to <see cref="UploadValidationOptions.MaxSizeBytes"/>.
    /// </param>
    /// <param name="allowedContentTypes">
    /// The set of accepted <c>Content-Type</c> values for this endpoint. An empty set falls back to
    /// <see cref="UploadValidationOptions.AllowedContentTypes"/>.
    /// </param>
    /// <returns>The same <paramref name="builder"/>, for chaining.</returns>
    public static RouteGroupBuilder RequireValidatedUpload(
        this RouteGroupBuilder builder,
        long? maxSizeBytes = null,
        params string[] allowedContentTypes)
        => builder.WithMetadata(new RequireValidatedUploadAttribute(maxSizeBytes, allowedContentTypes));
}
