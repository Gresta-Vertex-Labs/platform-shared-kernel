using Asp.Versioning;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace SharedKernel.Presentation.WebApi.Versioning;

/// <summary>
/// DI extension for wiring the platform's API versioning conventions.
/// </summary>
public static class ApiVersioningExtensions
{
    /// <summary>
    /// Registers <c>Asp.Versioning</c> with the platform defaults from
    /// <see cref="SharedKernelApiVersioningDefaults"/>.
    /// </summary>
    /// <param name="services">The service collection to add registrations to.</param>
    /// <param name="configureLifecycle">
    /// An optional callback declaring, per <see cref="ApiVersion"/>, an RFC 8594 sunset date and/or
    /// successor-version link — see <see cref="ApiVersionLifecycleOptions"/>. Additive parameter:
    /// omitting it means no <c>Sunset</c>/<c>Deprecation</c>/<c>Link</c> headers are ever written,
    /// byte-identical to this method's prior behavior.
    /// </param>
    /// <returns>The same <paramref name="services"/> instance, for chaining.</returns>
    /// <remarks>
    /// <para>
    /// Must be called before <c>AddSharedKernelOpenApi</c> when both are used — the OpenAPI
    /// extension reads <see cref="Asp.Versioning.ApiExplorer.IApiVersionDescriptionProvider"/> to
    /// discover version groups. <c>AssumeDefaultVersionWhenUnspecified = true</c> is a
    /// non-negotiable platform default — unversioned client requests fall back to
    /// <see cref="SharedKernelApiVersioningDefaults.DefaultApiVersion"/> rather than failing
    /// outright. <c>ReportApiVersions = true</c> adds <c>api-supported-versions</c>/
    /// <c>api-deprecated-versions</c> response headers. <c>GroupNameFormat = "'v'VVV"</c> ensures
    /// ApiExplorer group names match the OpenAPI document grouping consumed by
    /// <c>AddSharedKernelOpenApi</c>.
    /// </para>
    /// <para>
    /// When <paramref name="configureLifecycle"/> declares at least one sunset date/successor,
    /// <see cref="ApiVersionLifecycleMiddleware"/> self-inserts into the pipeline via
    /// <see cref="ApiVersionLifecycleStartupFilter"/> (an <see cref="IStartupFilter"/>) — no
    /// companion <c>Use...</c> call is required or exists. The middleware is always registered but
    /// is a genuine no-op (writes nothing) for any API version with no declared lifecycle entry, so
    /// registering it unconditionally carries zero observable behavior change for a service that
    /// never calls <paramref name="configureLifecycle"/>.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddSharedKernelApiVersioning(
        this IServiceCollection services,
        Action<ApiVersionLifecycleOptions>? configureLifecycle = null)
    {
        services
            .AddApiVersioning(options =>
            {
                options.DefaultApiVersion = SharedKernelApiVersioningDefaults.DefaultApiVersion;
                options.ApiVersionReader = SharedKernelApiVersioningDefaults.ApiVersionReader;
                options.AssumeDefaultVersionWhenUnspecified = true;
                options.ReportApiVersions = true;
            })
            .AddApiExplorer(options =>
            {
                options.GroupNameFormat = "'v'VVV";
                options.SubstituteApiVersionInUrl = true;
            });

        var lifecycleOptions = new ApiVersionLifecycleOptions();
        configureLifecycle?.Invoke(lifecycleOptions);

        services.AddSingleton(lifecycleOptions);
        services.AddSingleton<IStartupFilter, ApiVersionLifecycleStartupFilter>();

        return services;
    }
}
