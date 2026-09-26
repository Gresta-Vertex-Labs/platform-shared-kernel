using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharedKernel.Configuration.Extensions;
using SharedKernel.Presentation.Authorization;
using SharedKernel.Presentation.WebApi.Cors;
using SharedKernel.Presentation.WebApi.ExceptionHandling;
using SharedKernel.Presentation.WebApi.RateLimiting;
using SharedKernel.Presentation.WebApi.Startup;

namespace SharedKernel.Presentation.WebApi;

/// <summary>Registers the HTTP API boundary of a SharedKernel service.</summary>
public static class WebApiHostBuilderExtensions
{
    private const string BindingReason = "Binds SharedKernelWebApiOptions from configuration by reflection.";

    /// <summary>
    /// Registers everything <see cref="WebApiApplicationBuilderExtensions.UseSharedKernelWebApi"/> needs: one error
    /// contract, authorization, CORS, security headers and request limits, configured from
    /// <c>SharedKernel:Presentation:WebApi</c> (<see cref="SharedKernelWebApiOptions"/>) and validated.
    /// </summary>
    /// <param name="builder">The host builder, such as <c>WebApplication.CreateBuilder(args)</c>.</param>
    /// <param name="configure">Adjusts the settings after they are bound from configuration.</param>
    /// <returns>The same <paramref name="builder"/>.</returns>
    /// <remarks>
    /// <para>Registers:</para>
    /// <list type="bullet">
    ///   <item>Problem details: every error response is RFC 9457 <c>application/problem+json</c> with <c>errorCode</c>,
    ///   <c>traceId</c> and <c>correlationId</c>, including the framework's own (unmatched route, wrong method, MVC model
    ///   validation, minimal-API binding failures), and is never cached.</item>
    ///   <item>The platform's exception handling as the fallback of <c>UseExceptionHandler()</c>: an
    ///   <see cref="Microsoft.AspNetCore.Diagnostics.IExceptionHandler"/> the service registers runs first, and whatever it
    ///   leaves becomes the problem shape, logged once — 5xx at Error, 4xx at Debug.</item>
    ///   <item>The platform's authorization (<c>SharedKernel.Presentation.Core</c>): the policies behind
    ///   <see cref="RequireEndpointPermissionAttribute"/> and its siblings, with a problem body for every refusal.</item>
    ///   <item>A CORS policy, only when <c>Cors:AllowedOrigins</c> lists origins.</item>
    ///   <item>A 429 problem body for rate limiting that has no <c>OnRejected</c> of its own.</item>
    ///   <item>Kestrel without the <c>Server</c> header and with <c>Limits:MaxRequestBodySize</c>; <c>Limits:MaxJsonDepth</c>
    ///   for minimal APIs and MVC when set; HSTS settings.</item>
    ///   <item>Minimal APIs throw on a binding failure in every environment (as they do in Development), so each one
    ///   gets the platform's validation problem; MVC keeps System.Text.Json's messages, which name .NET types, out of
    ///   model state.</item>
    ///   <item>Startup warnings when <c>UseSharedKernelWebApi()</c> is never called and when exception details are
    ///   enabled outside Development.</item>
    /// </list>
    /// <para>Safe to call more than once; each <paramref name="configure"/> is applied.</para>
    /// </remarks>
    /// <exception cref="OptionsValidationException">
    /// When the settings are invalid: with Kestrel as soon as the host is built (the server reads them), otherwise —
    /// for example with <c>TestServer</c> — when <c>UseSharedKernelWebApi()</c> builds the pipeline, and at the latest
    /// when the host starts.
    /// </exception>
    [RequiresUnreferencedCode(BindingReason)]
    [RequiresDynamicCode(BindingReason)]
    public static IHostApplicationBuilder AddSharedKernelWebApi(this IHostApplicationBuilder builder, Action<SharedKernelWebApiOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var services = builder.Services;

        if (!services.Any(descriptor => descriptor.ServiceType == typeof(WebApiPipelineState)))
        {
            services.AddSingleton<WebApiPipelineState>();
            services.AddValidatedOptions<SharedKernelWebApiOptions, WebApiOptionsValidator>(builder.Configuration);
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, WebApiStartupDiagnostics>());

            services.AddRouting();
            services.AddProblemDetails();
            services.AddOptions<ProblemDetailsOptions>().PostConfigure(ChainCustomization);

            services.TryAddSingleton<SharedKernelExceptionHandler>();
            services.AddOptions<ExceptionHandlerOptions>()
                .PostConfigure<SharedKernelExceptionHandler>((options, handler) => handler.Install(options));

            services.AddSharedKernelWebApiAuthorization();

            services.AddCors();
            services.AddOptions<CorsOptions>()
                .Configure<IOptions<SharedKernelWebApiOptions>>((cors, options) => CorsPolicyConfiguration.Configure(cors, options.Value.Cors));

            services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<RateLimiterOptions>, RateLimitRejectionPostConfigure>());

            services.AddOptions<KestrelServerOptions>().Configure<IOptions<SharedKernelWebApiOptions>>(ConfigureKestrel);
            services.AddOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>()
                .Configure<IOptions<SharedKernelWebApiOptions>>(ConfigureMinimalApiJson);
            services.AddOptions<Microsoft.AspNetCore.Mvc.JsonOptions>()
                .Configure<IOptions<SharedKernelWebApiOptions>>(ConfigureMvcJson);
            services.AddOptions<HstsOptions>().Configure<IOptions<SharedKernelWebApiOptions>>(ConfigureHsts);

            // A binding failure outside Development would otherwise be a bodiless 400; thrown, it reaches the platform's
            // exception handling and becomes the same validation problem in every environment.
            services.AddOptions<RouteHandlerOptions>().Configure(static options => options.ThrowOnBadRequest = true);
            services.AddOptions<ApiBehaviorOptions>().PostConfigure(UsePlatformModelStateResponse);
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

    // MVC's own factory (set by AddControllers, in MVC's assembly) is replaced; a factory the service set itself is kept.
    private static void UsePlatformModelStateResponse(ApiBehaviorOptions options)
    {
        if (options.InvalidModelStateResponseFactory is null
            || options.InvalidModelStateResponseFactory.Method.Module.Assembly == typeof(ApiBehaviorOptions).Assembly)
        {
            options.InvalidModelStateResponseFactory = RequestValidationErrors.CreateModelStateResponse;
        }
    }

    private static void ConfigureKestrel(KestrelServerOptions kestrel, IOptions<SharedKernelWebApiOptions> options)
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

    private static void ConfigureMinimalApiJson(Microsoft.AspNetCore.Http.Json.JsonOptions json, IOptions<SharedKernelWebApiOptions> options)
    {
        if (options.Value.Limits.MaxJsonDepth is { } maxDepth)
        {
            json.SerializerOptions.MaxDepth = maxDepth;
        }
    }

    private static void ConfigureMvcJson(Microsoft.AspNetCore.Mvc.JsonOptions json, IOptions<SharedKernelWebApiOptions> options)
    {
        // Model state keeps the JsonException instead of its message ("could not be converted to System.Int32"), and
        // the platform's model-state response writes a generic message for it.
        json.AllowInputFormatterExceptionMessages = false;

        if (options.Value.Limits.MaxJsonDepth is { } maxDepth)
        {
            json.JsonSerializerOptions.MaxDepth = maxDepth;
        }
    }

    private static void ConfigureHsts(HstsOptions hsts, IOptions<SharedKernelWebApiOptions> options)
    {
        var settings = options.Value.SecurityHeaders;

        hsts.MaxAge = settings.HstsMaxAge;
        hsts.IncludeSubDomains = settings.HstsIncludeSubDomains;
        hsts.Preload = settings.HstsPreload;
    }
}
