using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharedKernel.Configuration.Extensions;
using SharedKernel.Presentation.WebApi.Cors;
using SharedKernel.Presentation.WebApi.Errors;
using SharedKernel.Presentation.WebApi.ExceptionHandling;
using SharedKernel.Presentation.WebApi.Options;
using SharedKernel.Presentation.WebApi.RateLimiting;

namespace SharedKernel.Presentation.WebApi;

/// <summary>Registers the HTTP API boundary of a SharedKernel service.</summary>
public static class WebApiHostBuilderExtensions
{
    private const string BindingReason = "Binds WebApiOptions from configuration by reflection.";

    /// <summary>
    /// Registers everything <see cref="WebApiApplicationBuilderExtensions.UseSharedKernelWebApi"/> needs: one error
    /// contract, authorization, CORS, security headers and request limits, configured from
    /// <c>SharedKernel:Presentation:WebApi</c> (<see cref="WebApiOptions"/>) and validated when the host starts.
    /// </summary>
    /// <param name="builder">The host builder, such as <c>WebApplication.CreateBuilder(args)</c>.</param>
    /// <param name="configure">Adjusts the settings after they are bound from configuration.</param>
    /// <returns>The same <paramref name="builder"/>.</returns>
    /// <remarks>
    /// <para>Registers:</para>
    /// <list type="bullet">
    ///   <item>Problem details: every error response is RFC 9457 <c>application/problem+json</c> with <c>errorCode</c>,
    ///   <c>traceId</c> and <c>correlationId</c>, including the framework's own (unmatched route, wrong method).</item>
    ///   <item>An exception handler that turns thrown exceptions into that shape and logs them — 5xx at Error, 4xx at Debug.</item>
    ///   <item><see cref="SharedKernelAuthorizationExtensions.AddSharedKernelAuthorization"/>.</item>
    ///   <item>A CORS policy, only when <c>Cors:AllowedOrigins</c> lists origins.</item>
    ///   <item>A 429 problem body for rate limiting that has no <c>OnRejected</c> of its own.</item>
    ///   <item>Kestrel without the <c>Server</c> header and with <c>Limits:MaxRequestBodySize</c>; <c>Limits:MaxJsonDepth</c>
    ///   for minimal APIs and MVC; HSTS settings.</item>
    /// </list>
    /// <para>Safe to call more than once; each <paramref name="configure"/> is applied.</para>
    /// </remarks>
    /// <exception cref="OptionsValidationException">At host startup, when the settings are invalid.</exception>
    [RequiresUnreferencedCode(BindingReason)]
    [RequiresDynamicCode(BindingReason)]
    public static IHostApplicationBuilder AddSharedKernelWebApi(this IHostApplicationBuilder builder, Action<WebApiOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var services = builder.Services;

        if (!services.Any(descriptor => descriptor.ServiceType == typeof(WebApiServicesMarker)))
        {
            services.AddSingleton<WebApiServicesMarker>();
            services.AddValidatedOptions<WebApiOptions, WebApiOptionsValidator>(builder.Configuration);

            services.AddRouting();
            services.AddProblemDetails();
            services.AddOptions<ProblemDetailsOptions>().PostConfigure(ChainCustomization);
            services.AddExceptionHandler<SharedKernelExceptionHandler>();

            services.AddSharedKernelAuthorization();

            services.AddCors();
            services.AddOptions<CorsOptions>()
                .Configure<IOptions<WebApiOptions>>((cors, options) => CorsPolicyConfiguration.Configure(cors, options.Value.Cors));

            services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<RateLimiterOptions>, RateLimitRejectionPostConfigure>());

            services.AddOptions<KestrelServerOptions>().Configure<IOptions<WebApiOptions>>(ConfigureKestrel);
            services.AddOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>()
                .Configure<IOptions<WebApiOptions>>((json, options) => json.SerializerOptions.MaxDepth = options.Value.Limits.MaxJsonDepth);
            services.AddOptions<Microsoft.AspNetCore.Mvc.JsonOptions>()
                .Configure<IOptions<WebApiOptions>>((json, options) => json.JsonSerializerOptions.MaxDepth = options.Value.Limits.MaxJsonDepth);
            services.AddOptions<HstsOptions>().Configure<IOptions<WebApiOptions>>(ConfigureHsts);
        }

        if (configure is not null)
        {
            services.Configure(configure);
        }

        return builder;
    }

    // Runs after every other configuration of ProblemDetailsOptions, so a service's own CustomizeProblemDetails is
    // kept and runs after the platform's, free to add or override members.
    private static void ChainCustomization(ProblemDetailsOptions options)
    {
        var serviceCustomization = options.CustomizeProblemDetails;

        options.CustomizeProblemDetails = context =>
        {
            ProblemDetailsCustomizer.Customize(context);
            serviceCustomization?.Invoke(context);
        };
    }

    private static void ConfigureKestrel(KestrelServerOptions kestrel, IOptions<WebApiOptions> options)
    {
        var settings = options.Value;

        if (settings.RemoveServerHeader)
        {
            kestrel.AddServerHeader = false;
        }

        if (settings.Limits.MaxRequestBodySize is { } maxRequestBodySize)
        {
            kestrel.Limits.MaxRequestBodySize = maxRequestBodySize;
        }
    }

    private static void ConfigureHsts(HstsOptions hsts, IOptions<WebApiOptions> options)
    {
        var settings = options.Value.SecurityHeaders;

        hsts.MaxAge = settings.HstsMaxAge;
        hsts.IncludeSubDomains = settings.HstsIncludeSubDomains;
        hsts.Preload = settings.HstsPreload;
    }
}

/// <summary>Marks a service collection <c>AddSharedKernelWebApi()</c> has already configured.</summary>
internal sealed class WebApiServicesMarker;
