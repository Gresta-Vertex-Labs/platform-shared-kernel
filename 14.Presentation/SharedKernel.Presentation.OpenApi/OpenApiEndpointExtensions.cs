using Asp.Versioning.ApiExplorer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Scalar.AspNetCore;
using SharedKernel.Presentation.OpenApi.Documents;
using SharedKernel.Presentation.OpenApi.Options;
using SharedKernel.Presentation.OpenApi.Routing;
using SharedKernel.Presentation.WebApi;
using SharedKernel.Primitives.Logging;

namespace SharedKernel.Presentation.OpenApi;

/// <summary>Serves the OpenAPI documents and the Scalar API reference of a SharedKernel HTTP API.</summary>
public static partial class OpenApiEndpointExtensions
{
    /// <summary>
    /// Maps one OpenAPI document per API version at <c>/openapi/{documentName}.json</c> (<c>/openapi/v1.json</c>,
    /// <c>/openapi/v2.json</c>, …) and the Scalar API reference at <c>/scalar</c>, listing every version — in the
    /// Development environment only, unless <see cref="SharedKernelOpenApiOptions.ExposeInProduction"/> is set.
    /// </summary>
    /// <param name="endpoints">The application, such as the built <c>WebApplication</c>.</param>
    /// <returns>
    /// One convention builder for the documents and the API reference together, so
    /// <c>app.MapSharedKernelOpenApi().RequirePermission("docs.read")</c> protects both. When nothing is mapped, the
    /// conventions applied to it have no effect.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The versions listed are those of the endpoints mapped by the time the reference is requested, so this can be
    /// called before or after the API's endpoints are mapped. The reference pages are served without a
    /// <c>Content-Security-Policy</c>, which would stop them from running their scripts; the documents keep the
    /// configured one.
    /// </para>
    /// <para>
    /// A browser fetches the documents for the reference itself, so documents protected by a bearer-token requirement
    /// cannot be loaded from the page; protect them with a scheme the browser sends by itself, such as a cookie, or
    /// behind a gateway.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException"><c>AddSharedKernelOpenApi()</c> was not called.</exception>
    /// <exception cref="OptionsValidationException">The settings are invalid.</exception>
    public static IEndpointConventionBuilder MapSharedKernelOpenApi(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var services = endpoints.ServiceProvider;
        if (services.GetService<OpenApiServicesMarker>() is null)
        {
            throw new InvalidOperationException(
                "MapSharedKernelOpenApi() requires the services of AddSharedKernelOpenApi(). Call builder.AddSharedKernelOpenApi() first.");
        }

        var options = services.GetRequiredService<IOptions<SharedKernelOpenApiOptions>>().Value;
        var environment = services.GetRequiredService<IHostEnvironment>();

        if (!environment.IsDevelopment() && !options.ExposeInProduction)
        {
            var logger = services.GetService<ILoggerFactory>()?.CreateLogger(typeof(OpenApiEndpointExtensions)) ?? NullLogger.Instance;
            Log.DocumentsNotMapped(logger, environment.EnvironmentName);

            return new CompositeEndpointConventionBuilder();
        }

        var documents = endpoints.MapOpenApi().WithDocumentPerVersion();
        var reference = endpoints.MapScalarApiReference(ConfigureReference).WithContentSecurityPolicy(null);

        return new CompositeEndpointConventionBuilder(documents, reference);
    }

    /// <summary>
    /// Lists one document per API version; the newest version that is not deprecated opens by default, and a deprecated
    /// one is labelled as such.
    /// </summary>
    private static void ConfigureReference(ScalarOptions reference, HttpContext httpContext)
    {
        var services = httpContext.RequestServices;
        var title = DocumentText.ResolveTitle(
            services.GetRequiredService<IOptions<SharedKernelOpenApiOptions>>().Value,
            services.GetRequiredService<IHostEnvironment>());

        reference.WithTitle(title);

        // Resolved per request, when every endpoint is mapped: the same versions the document endpoint serves.
        var versions = services.GetRequiredService<IApiVersionDescriptionProvider>().ApiVersionDescriptions;
        var preferred = versions.LastOrDefault(version => !version.IsDeprecated) ?? versions.LastOrDefault();

        foreach (var version in versions)
        {
            reference.AddDocument(
                version.GroupName,
                version.IsDeprecated ? $"{version.GroupName} (deprecated)" : version.GroupName,
                isDefault: ReferenceEquals(version, preferred));
        }
    }

    private static partial class Log
    {
        [LoggerMessage(
            EventId = LoggingEventIdRanges.Presentation + 300,
            Level = LogLevel.Information,
            Message = "The OpenAPI documents and API reference are not served in the {EnvironmentName} environment; set ExposeInProduction to serve them.")]
        public static partial void DocumentsNotMapped(ILogger logger, string environmentName);
    }
}
