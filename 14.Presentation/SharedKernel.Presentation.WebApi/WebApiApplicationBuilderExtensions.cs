using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharedKernel.Presentation.WebApi.Correlation;
using SharedKernel.Presentation.WebApi.Cors;
using SharedKernel.Presentation.WebApi.Errors;
using SharedKernel.Presentation.WebApi.Options;
using SharedKernel.Presentation.WebApi.SecurityHeaders;

namespace SharedKernel.Presentation.WebApi;

/// <summary>Adds the HTTP API boundary of a SharedKernel service to the request pipeline.</summary>
public static class WebApiApplicationBuilderExtensions
{
    private const string PipelineAddedKey = "SharedKernel.Presentation.WebApi.PipelineAdded";

    /// <summary>
    /// Adds, in this order: correlation ids, security headers, the exception handler, problem bodies for bodiless
    /// error statuses, HSTS (outside Development), routing, CORS (when origins are configured), authentication (when an
    /// authentication scheme is registered), authorization and rate limiting (when configured).
    /// </summary>
    /// <param name="app">The application, such as the built <c>WebApplication</c>.</param>
    /// <returns>The same <paramref name="app"/>.</returns>
    /// <remarks>
    /// <para>
    /// Call it first, then map endpoints; add custom middleware after it. Because it calls <c>UseRouting()</c>,
    /// middleware added afterwards sees the selected endpoint, and error responses from anywhere in the pipeline get
    /// the correlation id and the problem shape. Calling it again has no effect.
    /// </para>
    /// <para>
    /// gRPC calls pass through the same pipeline and get correlation ids and authorization, but never a JSON body:
    /// gRPC carries errors in its own status.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException"><c>AddSharedKernelWebApi()</c> was not called.</exception>
    /// <exception cref="OptionsValidationException">The settings are invalid.</exception>
    public static IApplicationBuilder UseSharedKernelWebApi(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (app.Properties.ContainsKey(PipelineAddedKey))
        {
            return app;
        }

        var services = app.ApplicationServices;
        if (services.GetService<WebApiServicesMarker>() is null)
        {
            throw new InvalidOperationException(
                "UseSharedKernelWebApi() requires the services of AddSharedKernelWebApi(). Call builder.AddSharedKernelWebApi() first.");
        }

        var options = services.GetRequiredService<IOptions<WebApiOptions>>().Value;
        var environment = services.GetRequiredService<IHostEnvironment>();

        if (options.CorrelationId.Enabled)
        {
            app.UseMiddleware<CorrelationIdMiddleware>();
        }

        if (options.SecurityHeaders.Enabled)
        {
            app.UseMiddleware<SecurityHeadersMiddleware>();
        }

        app.UseExceptionHandler();
        app.UseStatusCodePages(StatusCodeProblemHandler.HandleAsync);

        if (options.SecurityHeaders.Enabled && options.SecurityHeaders.Hsts && !environment.IsDevelopment())
        {
            app.UseHsts();
        }

        app.UseRouting();

        if (CorsPolicyConfiguration.IsEnabled(options.Cors))
        {
            app.UseCors(CorsPolicyConfiguration.PolicyName);
        }

        if (services.GetService<IAuthenticationSchemeProvider>() is not null)
        {
            app.UseAuthentication();
        }

        app.UseAuthorization();

        // AddRateLimiter registers its configuration as IConfigureOptions<RateLimiterOptions>; without it there is no
        // limiter to apply, and UseRateLimiter would refuse to start. This package only post-configures the options.
        if (services.GetServices<IConfigureOptions<RateLimiterOptions>>().Any())
        {
            app.UseRateLimiter();
        }

        app.Properties[PipelineAddedKey] = true;
        return app;
    }
}
