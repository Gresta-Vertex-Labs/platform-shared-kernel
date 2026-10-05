using System.Diagnostics.CodeAnalysis;
using Asp.Versioning;
using Asp.Versioning.OpenApi;
using Asp.Versioning.OpenApi.Transformers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharedKernel.Configuration.Extensions;
using SharedKernel.Presentation.OpenApi.Documents;
using SharedKernel.Presentation.OpenApi.Startup;
using SharedKernel.Presentation.OpenApi.Versioning;

namespace SharedKernel.Presentation.OpenApi;

/// <summary>Registers API versioning and the OpenAPI documents of a SharedKernel HTTP API.</summary>
public static class OpenApiHostBuilderExtensions
{
    private const string BindingReason =
        "Binds SharedKernelOpenApiOptions from configuration by reflection, and the API Explorer that describes versioned endpoints uses MVC.";

    /// <summary>
    /// Registers API versioning and one OpenAPI document per API version, configured from
    /// <c>SharedKernel:Presentation:OpenApi</c> (<see cref="SharedKernelOpenApiOptions"/>) and validated when the host
    /// starts. Serve the documents with <see cref="OpenApiEndpointExtensions.MapSharedKernelOpenApi"/>.
    /// </summary>
    /// <param name="builder">The host builder, such as <c>WebApplication.CreateBuilder(args)</c>.</param>
    /// <param name="configure">Adjusts the settings after they are bound from configuration.</param>
    /// <returns>The same <paramref name="builder"/>.</returns>
    /// <remarks>
    /// <para>Registers:</para>
    /// <list type="bullet">
    ///   <item>API versioning: version 1.0 by default and assumed when a request names none; the version read from the
    ///   URL segment (<c>/v{version:apiVersion}/…</c>) or the <c>X-Api-Version</c> header; the supported and
    ///   deprecated versions reported in <c>api-supported-versions</c> and <c>api-deprecated-versions</c>; then
    ///   <see cref="SharedKernelOpenApiOptions.Versioning"/>, which also declares sunset and deprecation policies.</item>
    ///   <item>The API Explorer, grouping endpoints by version (<c>v1</c>, <c>v2</c>, …) with the version written into
    ///   the documented URLs.</item>
    ///   <item>One OpenAPI 3.1 document per version, titled and described from the settings, in which every operation
    ///   documents the <c>application/problem+json</c> error shape, a protected operation its security requirement
    ///   and 401/403, and a required <c>Idempotency-Key</c> or <c>If-Match</c> header its parameter and 400 (plus
    ///   412/428 for <c>If-Match</c>) — whether a convention, an attribute or an <c>IdempotencyKey</c> or
    ///   <c>IfMatch&lt;TVersion&gt;</c> handler parameter requires it.</item>
    ///   <item>The platform's problem shape for API versioning's own errors, such as an unsupported version.</item>
    ///   <item>A startup warning when the documents are served outside Development and neither an authorization
    ///   convention nor a fallback policy protects them (see <see cref="OpenApiEndpointExtensions.MapSharedKernelOpenApi"/>).</item>
    /// </list>
    /// <para>
    /// Call it with <c>AddSharedKernelWebApi()</c>, whose error responses the documents describe. Safe to call more
    /// than once; each <paramref name="configure"/> is applied.
    /// </para>
    /// </remarks>
    /// <exception cref="OptionsValidationException">At host startup, when the settings are invalid.</exception>
    [RequiresUnreferencedCode(BindingReason)]
    [RequiresDynamicCode(BindingReason)]
    public static IHostApplicationBuilder AddSharedKernelOpenApi(
        this IHostApplicationBuilder builder,
        Action<SharedKernelOpenApiOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var services = builder.Services;

        if (!services.Any(descriptor => descriptor.ServiceType == typeof(OpenApiSetupState)))
        {
            services.AddSingleton<OpenApiSetupState>();
            services.AddValidatedOptions<SharedKernelOpenApiOptions, SharedKernelOpenApiOptionsValidator>(builder.Configuration);
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, OpenApiStartupDiagnostics>());

            // Before AddOpenApi(), which registers its own transformer only when none is registered.
            services.TryAddTransient<XmlCommentsTransformer>(EntryAssemblyXmlComments.CreateTransformer);

            services
                .AddApiVersioning(ApiVersioningDefaults.Configure)
                .AddApiExplorer(ApiVersioningDefaults.ConfigureExplorer)
                .AddOpenApi();

            // Registered after the platform defaults above, so the service's callback can override them.
            services.AddOptions<ApiVersioningOptions>()
                .Configure<IOptions<SharedKernelOpenApiOptions>>((versioning, options) => options.Value.Versioning?.Invoke(versioning));

            services.TryAddEnumerable(ServiceDescriptor.Transient<IConfigureOptions<VersionedOpenApiOptions>, VersionedDocumentSetup>());
            services.TryAddSingleton<EndpointMetadataLookup>();
            services.AddOptions<ProblemDetailsOptions>().PostConfigure(ApiVersioningProblems.Chain);
        }

        if (configure is not null)
        {
            services.Configure(configure);
        }

        return builder;
    }
}
