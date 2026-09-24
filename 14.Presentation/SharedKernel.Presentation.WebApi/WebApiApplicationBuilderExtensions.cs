using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharedKernel.Presentation.WebApi.Correlation;
using SharedKernel.Presentation.WebApi.Cors;
using SharedKernel.Presentation.WebApi.Errors;
using SharedKernel.Presentation.WebApi.Http;
using SharedKernel.Presentation.WebApi.Options;
using SharedKernel.Presentation.WebApi.SecurityHeaders;
using SharedKernel.Presentation.WebApi.Startup;

namespace SharedKernel.Presentation.WebApi;

/// <summary>Adds the HTTP API boundary of a SharedKernel service to the request pipeline.</summary>
public static class WebApiApplicationBuilderExtensions
{
    private const string PipelineAddedKey = "SharedKernel.Presentation.WebApi.PipelineAdded";

    /// <summary>
    /// Adds the HTTP API boundary to the pipeline, in this order:
    /// </summary>
    /// <param name="app">The application, such as the built <c>WebApplication</c>.</param>
    /// <param name="configure">
    /// Adds a service's own middleware at the positions of <see cref="WebApiPipeline"/>: forwarded headers
    /// <see cref="WebApiPipeline.AtStart"/>, certificate forwarding <see cref="WebApiPipeline.BeforeAuthentication"/>,
    /// request localization <see cref="WebApiPipeline.BeforeAuthorization"/>.
    /// </param>
    /// <returns>The same <paramref name="app"/>.</returns>
    /// <remarks>
    /// <list type="number">
    ///   <item>Removal of the caller's W3C baggage (unless <c>TrustInboundBaggage</c>).</item>
    ///   <item>The <see cref="WebApiPipeline.AtStart"/> hooks.</item>
    ///   <item>Correlation ids.</item>
    ///   <item>HSTS (outside Development), then security headers and the default <c>Cache-Control</c> — before the
    ///   exception handler, so error responses carry them too.</item>
    ///   <item>The exception handler, then problem bodies for bodiless error statuses.</item>
    ///   <item>Routing, then CORS and the WebSocket origin check (when origins are configured).</item>
    ///   <item>The <see cref="WebApiPipeline.BeforeAuthentication"/> hooks, then authentication (when an authentication
    ///   scheme is registered).</item>
    ///   <item>The <see cref="WebApiPipeline.BeforeAuthorization"/> hooks, then rate limiting (when configured) — before
    ///   authorization, so refused requests count against the limit too.</item>
    ///   <item>Authorization, then the required <c>Idempotency-Key</c> and <c>If-Match</c> headers of the endpoint.</item>
    /// </list>
    /// <para>
    /// Call it first, then map endpoints; add other middleware after it. Because it calls <c>UseRouting()</c>,
    /// middleware added afterwards sees the selected endpoint, and error responses from anywhere in the pipeline get
    /// the correlation id and the problem shape. Calling it again has no effect, its hooks included. When
    /// <c>AddSharedKernelWebApi()</c> ran but this method never does, the host logs a warning at startup.
    /// </para>
    /// <para>
    /// gRPC calls pass through the same pipeline and get correlation ids, authorization and the required-header
    /// checks, but never a JSON body: gRPC carries errors in its own status.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException"><c>AddSharedKernelWebApi()</c> was not called.</exception>
    /// <exception cref="OptionsValidationException">
    /// The settings are invalid (with Kestrel, <c>builder.Build()</c> has already thrown this).
    /// </exception>
    public static IApplicationBuilder UseSharedKernelWebApi(this IApplicationBuilder app, Action<WebApiPipeline>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (app.Properties.ContainsKey(PipelineAddedKey))
        {
            return app;
        }

        var services = app.ApplicationServices;
        var state = services.GetService<WebApiPipelineState>()
            ?? throw new InvalidOperationException(
                "UseSharedKernelWebApi() requires the services of AddSharedKernelWebApi(). Call builder.AddSharedKernelWebApi() first.");

        var options = services.GetRequiredService<IOptions<SharedKernelWebApiOptions>>().Value;
        var environment = services.GetRequiredService<IHostEnvironment>();

        var pipeline = new WebApiPipeline();
        configure?.Invoke(pipeline);

        if (!options.TrustInboundBaggage)
        {
            app.Use(InboundBaggage.InvokeAsync);
        }

        pipeline.ApplyAtStart(app);

        if (options.CorrelationId.Enabled)
        {
            app.UseMiddleware<CorrelationIdMiddleware>();
        }

        if (options.SecurityHeaders.Enabled)
        {
            // HSTS sets its header directly; the security headers middleware right after it keeps the value on the
            // responses the exception handler writes, which clears every header first.
            if (options.SecurityHeaders.Hsts && !environment.IsDevelopment())
            {
                app.UseHsts();
            }

            app.UseMiddleware<SecurityHeadersMiddleware>();
        }

        app.UseExceptionHandler();
        app.UseStatusCodePages(StatusCodeProblemHandler.HandleAsync);

        app.UseRouting();

        if (CorsPolicyConfiguration.IsEnabled(options.Cors))
        {
            app.UseCors(CorsPolicyConfiguration.PolicyName);
            app.UseMiddleware<WebSocketOriginMiddleware>();
        }

        pipeline.ApplyBeforeAuthentication(app);

        if (services.GetService<IAuthenticationSchemeProvider>() is not null)
        {
            app.UseAuthentication();
        }

        pipeline.ApplyBeforeAuthorization(app);

        // AddRateLimiter registers its configuration as IConfigureOptions<RateLimiterOptions>; without it there is no
        // limiter to apply, and UseRateLimiter would refuse to start. This package only post-configures the options.
        if (services.GetServices<IConfigureOptions<RateLimiterOptions>>().Any())
        {
            app.UseRateLimiter();
        }

        app.UseAuthorization();
        app.UseMiddleware<HeaderRequirementsMiddleware>();

        app.Properties[PipelineAddedKey] = true;
        state.MarkApplied();
        return app;
    }
}
