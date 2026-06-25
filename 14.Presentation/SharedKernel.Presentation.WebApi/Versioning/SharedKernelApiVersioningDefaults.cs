using Asp.Versioning;

namespace SharedKernel.Presentation.WebApi.Versioning;

/// <summary>
/// Platform-default API versioning configuration values shared by
/// <see cref="ApiVersioningExtensions.AddSharedKernelApiVersioning"/>.
/// </summary>
public static class SharedKernelApiVersioningDefaults
{
    /// <summary>
    /// The default <see cref="ApiVersion"/> assumed when a client does not specify a version and
    /// <c>AssumeDefaultVersionWhenUnspecified</c> is enabled.
    /// </summary>
    public static ApiVersion DefaultApiVersion { get; } = new(1, 0);

    /// <summary>
    /// The platform's combined API version reader: <see cref="UrlSegmentApiVersionReader"/>
    /// (primary, <c>"/v{version}/..."</c>) combined with <see cref="HeaderApiVersionReader"/>
    /// reading the <c>"X-Api-Version"</c> header (secondary override).
    /// </summary>
    public static IApiVersionReader ApiVersionReader { get; } = Asp.Versioning.ApiVersionReader.Combine(
        new UrlSegmentApiVersionReader(),
        new HeaderApiVersionReader("X-Api-Version"));
}
