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
    private const string ApiKeySecuritySchemeName = "ApiKey";
    private const string MutualTlsSecuritySchemeName = "MutualTLS";

    /// <summary>
    /// Registers one native OpenAPI document per discovered API version group.
    /// </summary>
    /// <param name="services">The service collection to add registrations to.</param>
    /// <param name="title">The title shown in the generated OpenAPI document(s) and Scalar UI.</param>
    /// <param name="description">An optional description shown alongside <paramref name="title"/>.</param>
    /// <param name="configureSecuritySchemes">
    /// An optional callback to customise which security schemes are registered — see
    /// <see cref="OpenApiSecuritySchemesOptions"/>. Additive parameter: omitting it preserves this
    /// method's original unconditional Bearer-only behavior.
    /// </param>
    /// <returns>The same <paramref name="services"/> instance, for chaining.</returns>
    /// <remarks>
    /// Sources version groups from <see cref="IApiVersionDescriptionProvider"/> when
    /// <c>AddSharedKernelApiVersioning</c> was called first; falls back to a single
    /// <c>"v1"</c> document otherwise. Registers a document transformer setting
    /// <c>Info.Title</c>/<c>Info.Description</c> and adding every active security scheme by name
    /// (scheme metadata only — token validation logic stays in <c>12.Security.Oidc</c>/<c>.ApiKey</c>/
    /// <c>.Mtls</c>, never duplicated here). Each active scheme is registered as its own separate
    /// OpenAPI security requirement object (OR semantics) — see
    /// <see cref="OpenApiSecuritySchemesOptions"/> for details.
    /// </remarks>
    public static IServiceCollection AddSharedKernelOpenApi(
        this IServiceCollection services,
        string title,
        string? description = null,
        Action<OpenApiSecuritySchemesOptions>? configureSecuritySchemes = null)
    {
        var securitySchemesOptions = new OpenApiSecuritySchemesOptions();
        configureSecuritySchemes?.Invoke(securitySchemesOptions);

        var versionProviderDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IApiVersionDescriptionProvider));

        if (versionProviderDescriptor is null)
        {
            services.AddOpenApi(FallbackDocumentName, options => ConfigureDocument(options, title, description, FallbackDocumentName, securitySchemesOptions));
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
            services.AddOpenApi(documentName, options => ConfigureDocument(options, title, description, documentName, securitySchemesOptions));
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

    private static void ConfigureDocument(
        OpenApiOptions options,
        string title,
        string? description,
        string documentName,
        OpenApiSecuritySchemesOptions securitySchemesOptions)
    {
        options.AddDocumentTransformer((document, _, _) =>
        {
            document.Info.Title = title;
            document.Info.Description = description;
            document.Info.Version = documentName;

            var components = document.Components ?? new OpenApiComponents();
            var securitySchemes = components.SecuritySchemes ?? new Dictionary<string, IOpenApiSecurityScheme>();

            if (securitySchemesOptions.Bearer)
            {
                securitySchemes[BearerSecuritySchemeName] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                };
            }

            if (securitySchemesOptions.ApiKey)
            {
                securitySchemes[ApiKeySecuritySchemeName] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.ApiKey,
                    Name = securitySchemesOptions.ApiKeyHeaderName,
                    In = ParameterLocation.Header,
                };
            }

            if (securitySchemesOptions.MutualTls)
            {
                securitySchemes[MutualTlsSecuritySchemeName] = new MutualTlsSecurityScheme();
            }

            components.SecuritySchemes = securitySchemes;
            document.Components = components;

            // Resolves every OpenApiSecuritySchemeReference constructed below against the
            // just-updated Components.SecuritySchemes dictionary — without this, each reference's
            // Target stays unresolved and the corresponding entry silently serializes as an empty
            // object inside document.Security (confirmed via a real serialization round trip
            // against the installed Microsoft.OpenApi 2.0.0 package).
            document.RegisterComponents();

            var securityRequirements = new List<OpenApiSecurityRequirement>();

            if (securitySchemesOptions.Bearer)
            {
                securityRequirements.Add(BuildSecurityRequirement(document, BearerSecuritySchemeName));
            }

            if (securitySchemesOptions.ApiKey)
            {
                securityRequirements.Add(BuildSecurityRequirement(document, ApiKeySecuritySchemeName));
            }

            if (securitySchemesOptions.MutualTls)
            {
                securityRequirements.Add(BuildSecurityRequirement(document, MutualTlsSecuritySchemeName));
            }

            // Each active scheme is its OWN SEPARATE security requirement object — OR semantics,
            // a request satisfies ANY one active mechanism — never a single combined requirement
            // object, which would mean simultaneous/AND-required semantics. The opposite of this
            // package's usual AND-across-attributes composition rule elsewhere
            // ([RequireRole]/[RequirePermission]).
            if (securityRequirements.Count > 0)
            {
                document.Security = securityRequirements;
            }

            return Task.CompletedTask;
        });
    }

    private static OpenApiSecurityRequirement BuildSecurityRequirement(OpenApiDocument document, string schemeName)
        => new() { [new OpenApiSecuritySchemeReference(schemeName, document, null)] = [] };
}
