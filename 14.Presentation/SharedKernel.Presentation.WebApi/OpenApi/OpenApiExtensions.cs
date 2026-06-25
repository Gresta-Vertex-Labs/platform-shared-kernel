using Asp.Versioning.ApiExplorer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

namespace SharedKernel.Presentation.WebApi.OpenApi;

/// <summary>
/// DI and endpoint-mapping extensions for the platform's native OpenAPI + Scalar documentation
/// stack.
/// </summary>
/// <remarks>
/// Swashbuckle and NSwag are deliberately never used —
/// <see cref="Microsoft.AspNetCore.OpenApi"/> (native) + <c>Scalar.AspNetCore</c> is the only
/// sanctioned OpenAPI combination on this platform.
/// </remarks>
public static class OpenApiExtensions
{
    private const string FallbackDocumentName = "v1";
    private const string BearerSecuritySchemeName = "Bearer";

    /// <summary>
    /// Registers one native OpenAPI document per discovered API version group.
    /// </summary>
    /// <param name="services">The service collection to add registrations to.</param>
    /// <param name="title">The title shown in the generated OpenAPI document(s) and Scalar UI.</param>
    /// <param name="description">An optional description shown alongside <paramref name="title"/>.</param>
    /// <returns>The same <paramref name="services"/> instance, for chaining.</returns>
    /// <remarks>
    /// Sources version groups from <see cref="IApiVersionDescriptionProvider"/> when
    /// <c>AddSharedKernelApiVersioning</c> was called first; falls back to a single
    /// <c>"v1"</c> document otherwise. Registers a document transformer setting
    /// <c>Info.Title</c>/<c>Info.Description</c> and adding the Bearer JWT security scheme by name
    /// (scheme metadata only — token validation logic stays in <c>12.Security.Oidc</c>, never
    /// duplicated here).
    /// </remarks>
    public static IServiceCollection AddSharedKernelOpenApi(
        this IServiceCollection services,
        string title,
        string? description = null)
    {
        var versionProviderDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IApiVersionDescriptionProvider));

        if (versionProviderDescriptor is null)
        {
            services.AddOpenApi(FallbackDocumentName, options => ConfigureDocument(options, title, description, FallbackDocumentName));
            return services;
        }

        // The version provider is registered, but its descriptions are only known once the
        // service provider is built (it depends on discovered controllers/endpoints). We
        // register a document per well-known group name pattern by deferring to a temporary
        // provider build — this is the standard pattern recommended for native OpenAPI +
        // Asp.Versioning integration.
        using var serviceProvider = services.BuildServiceProvider();
        var versionDescriptionProvider = serviceProvider.GetRequiredService<IApiVersionDescriptionProvider>();

        foreach (var apiVersionDescription in versionDescriptionProvider.ApiVersionDescriptions)
        {
            var documentName = apiVersionDescription.GroupName;
            services.AddOpenApi(documentName, options => ConfigureDocument(options, title, description, documentName));
        }

        return services;
    }

    /// <summary>
    /// Maps the native OpenAPI document endpoint(s) and the Scalar interactive UI.
    /// </summary>
    /// <param name="app">The web application to map endpoints on.</param>
    /// <returns>The same <paramref name="app"/> instance, for chaining.</returns>
    /// <remarks>
    /// Calls <c>MapOpenApi(string)</c> per registered document, then <c>MapScalarApiReference</c>
    /// configured to list every discovered version. No Swagger UI mapping.
    /// </remarks>
    public static WebApplication MapSharedKernelOpenApi(this WebApplication app)
    {
        var versionDescriptionProvider = app.Services.GetService<IApiVersionDescriptionProvider>();

        var documentNames = versionDescriptionProvider is not null
            ? versionDescriptionProvider.ApiVersionDescriptions.Select(d => d.GroupName).ToArray()
            : [FallbackDocumentName];

        foreach (var documentName in documentNames)
        {
            app.MapOpenApi($"/openapi/{documentName}.json");
        }

        app.MapScalarApiReference(options =>
        {
            foreach (var documentName in documentNames)
            {
                options.AddDocument(documentName, documentName, $"/openapi/{documentName}.json");
            }
        });

        return app;
    }

    private static void ConfigureDocument(OpenApiOptions options, string title, string? description, string documentName)
    {
        options.AddDocumentTransformer((document, _, _) =>
        {
            document.Info.Title = title;
            document.Info.Description = description;
            document.Info.Version = documentName;

            var components = document.Components ?? new OpenApiComponents();
            var securitySchemes = components.SecuritySchemes ?? new Dictionary<string, IOpenApiSecurityScheme>();
            securitySchemes[BearerSecuritySchemeName] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
            };
            components.SecuritySchemes = securitySchemes;
            document.Components = components;

            return Task.CompletedTask;
        });
    }
}
