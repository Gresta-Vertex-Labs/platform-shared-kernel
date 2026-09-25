using Microsoft.AspNetCore.Builder;

namespace SharedKernel.Presentation.WebApi.Middleware;

/// <summary>
/// Pipeline-registration extension for <see cref="SecurityHeadersMiddleware"/>.
/// </summary>
public static class SecurityHeadersExtensions
{
    /// <summary>
    /// Adds <see cref="SecurityHeadersMiddleware"/> to the request pipeline.
    /// </summary>
    /// <param name="app">The application builder to add the middleware to.</param>
    /// <param name="configure">
    /// An optional callback to customise <see cref="SecurityHeadersOptions"/>. When omitted, every
    /// header ships with its documented default value.
    /// </param>
    /// <returns>The same <paramref name="app"/> instance, for chaining.</returns>
    /// <remarks>
    /// Must be registered immediately after <c>UseSharedKernelRequestContext()</c> (<c>SharedKernel.ServiceDefaults.Security</c>) and before
    /// <c>UseExceptionHandler()</c>/error-handling middleware.
    /// <para>
    /// <b>HSTS IS ENABLED BY DEFAULT.</b> IT MUST BE DISABLED OR GIVEN A SHORT
    /// <see cref="HstsHeaderOptions.MaxAge"/> FOR LOCAL HTTP-ONLY DEVELOPMENT — SEE
    /// <see cref="SecurityHeadersOptions"/> FOR DETAILS.
    /// </para>
    /// </remarks>
    public static IApplicationBuilder UseSharedKernelSecurityHeaders(
        this IApplicationBuilder app,
        Action<SecurityHeadersOptions>? configure = null)
    {
        var options = new SecurityHeadersOptions();
        configure?.Invoke(options);

        return app.UseMiddleware<SecurityHeadersMiddleware>(options);
    }
}
