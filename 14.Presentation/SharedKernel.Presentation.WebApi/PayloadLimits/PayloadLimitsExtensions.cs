using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;

namespace SharedKernel.Presentation.WebApi.PayloadLimits;

/// <summary>
/// DI and pipeline-registration extensions for the platform's request payload-size and
/// JSON-depth denial-of-service protection defaults.
/// </summary>
/// <remarks>
/// Fully opt-in: a host calling neither <see cref="AddSharedKernelPayloadLimits"/> nor
/// <see cref="UseSharedKernelPayloadLimits"/> is byte-identical to today. A JSON-depth violation
/// surfaces via <c>System.Text.Json</c>'s own existing <see cref="System.Text.Json.JsonException"/>
/// → 400 path — this capability only wires the <see cref="PayloadLimitsOptions.MaxJsonDepth"/>
/// value, it does not invent a new depth-violation response shape.
/// </remarks>
public static class PayloadLimitsExtensions
{
    /// <summary>
    /// Registers the platform's <see cref="PayloadLimitsOptions.MaxJsonDepth"/> default against
    /// both the Minimal API and MVC JSON serialization surfaces.
    /// </summary>
    /// <param name="services">The service collection to add registrations to.</param>
    /// <param name="configure">An optional callback to customise <see cref="PayloadLimitsOptions"/>.</param>
    /// <returns>The same <paramref name="services"/> instance, for chaining.</returns>
    /// <remarks>
    /// Wires <see cref="PayloadLimitsOptions.MaxJsonDepth"/> into both
    /// <see cref="Microsoft.AspNetCore.Http.Json.JsonOptions"/> (Minimal API) and
    /// <see cref="Microsoft.AspNetCore.Mvc.JsonOptions"/> (MVC) — mirroring this package's existing
    /// dual Minimal-API/MVC support pattern (<c>ResultHttpExtensions</c>). The MVC registration is a
    /// plain <see cref="Microsoft.Extensions.Options.IOptions{TOptions}"/> configuration callback:
    /// it never throws and never requires MVC services to be registered — it simply has no effect
    /// unless a consuming service also calls <c>AddControllers()</c>/<c>AddMvc()</c>.
    /// </remarks>
    public static IServiceCollection AddSharedKernelPayloadLimits(
        this IServiceCollection services,
        Action<PayloadLimitsOptions>? configure = null)
    {
        var options = new PayloadLimitsOptions();
        configure?.Invoke(options);

        services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(
            jsonOptions => jsonOptions.SerializerOptions.MaxDepth = options.MaxJsonDepth);

        services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(
            mvcJsonOptions => mvcJsonOptions.JsonSerializerOptions.MaxDepth = options.MaxJsonDepth);

        return services;
    }

    /// <summary>
    /// Adds request-scoped maximum-request-body-size enforcement to the request pipeline.
    /// </summary>
    /// <param name="app">The application builder to add the middleware to.</param>
    /// <param name="configure">An optional callback to customise <see cref="PayloadLimitsOptions"/>.</param>
    /// <returns>The same <paramref name="app"/> instance, for chaining.</returns>
    /// <remarks>
    /// Sets <see cref="IHttpMaxRequestBodySizeFeature.MaxRequestBodySize"/> for the current request,
    /// guarded by <see cref="IHttpMaxRequestBodySizeFeature.IsReadOnly"/> — the feature throws
    /// <see cref="InvalidOperationException"/> once body reading has started, or is unavailable when
    /// unsupported by the current server, so this middleware checks first rather than crashing the
    /// pipeline. Two-part registration mirrors
    /// <c>AddSharedKernelCorrelationId</c>/<c>UseSharedKernelCorrelationId</c>.
    /// </remarks>
    public static IApplicationBuilder UseSharedKernelPayloadLimits(
        this IApplicationBuilder app,
        Action<PayloadLimitsOptions>? configure = null)
    {
        var options = new PayloadLimitsOptions();
        configure?.Invoke(options);

        return app.Use(async (context, next) =>
        {
            var maxRequestBodySizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();

            if (maxRequestBodySizeFeature is not null && !maxRequestBodySizeFeature.IsReadOnly)
            {
                maxRequestBodySizeFeature.MaxRequestBodySize = options.MaxRequestBodySizeBytes;
            }

            await next(context);
        });
    }
}
