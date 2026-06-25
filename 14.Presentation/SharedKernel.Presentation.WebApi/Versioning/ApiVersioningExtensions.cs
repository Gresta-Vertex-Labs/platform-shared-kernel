using Asp.Versioning;
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
    /// <returns>The same <paramref name="services"/> instance, for chaining.</returns>
    /// <remarks>
    /// Must be called before <c>AddSharedKernelOpenApi</c> when both are used — the OpenAPI
    /// extension reads <see cref="Asp.Versioning.ApiExplorer.IApiVersionDescriptionProvider"/> to
    /// discover version groups. <c>AssumeDefaultVersionWhenUnspecified = true</c> is a
    /// non-negotiable platform default — unversioned client requests fall back to
    /// <see cref="SharedKernelApiVersioningDefaults.DefaultApiVersion"/> rather than failing
    /// outright. <c>ReportApiVersions = true</c> adds <c>api-supported-versions</c>/
    /// <c>api-deprecated-versions</c> response headers. <c>GroupNameFormat = "'v'VVV"</c> ensures
    /// ApiExplorer group names match the OpenAPI document grouping consumed by
    /// <c>AddSharedKernelOpenApi</c>.
    /// </remarks>
    public static IServiceCollection AddSharedKernelApiVersioning(this IServiceCollection services)
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

        return services;
    }
}
