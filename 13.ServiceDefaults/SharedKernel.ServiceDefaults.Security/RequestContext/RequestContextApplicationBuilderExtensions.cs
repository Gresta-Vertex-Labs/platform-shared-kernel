using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Execution.Context;

namespace SharedKernel.ServiceDefaults.Security;

/// <summary>Adds the HTTP inbound request-context adapter to the request pipeline.</summary>
public static class RequestContextApplicationBuilderExtensions
{
    /// <summary>
    /// Adds the middleware that resolves the request's <c>X-Correlation-Id</c> and runs the rest of the request
    /// inside a <see cref="RequestContextScope"/> carrying the caller and that correlation id.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The same <paramref name="app"/> for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// <see cref="RequestContextServiceCollectionExtensions.AddSharedKernelRequestContext"/> was not called.
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>Middleware order.</b> Call it <b>first</b>, before <c>UseExceptionHandler()</c>, so the correlation id is
    /// in the log context of every later middleware and on every response, error responses included:
    /// </para>
    /// <code>
    /// app.UseSharedKernelRequestContext();          // correlation id + the request's context scope
    /// app.UseSharedKernelSecurityHeaders();
    /// app.UseExceptionHandler();
    /// app.UseAuthentication();
    /// app.UseMiddleware&lt;TenantResolutionMiddleware&gt;(); // optional: refines the tenant in an inner scope
    /// app.UseAuthorization();
    /// </code>
    /// <para>
    /// Placing it before <c>UseAuthentication()</c> is safe: the caller is read from the request's
    /// <c>IUserContext</c> only when something first asks, which is after authentication has run. Nothing between
    /// this middleware and <c>UseAuthentication()</c> should read the caller.
    /// </para>
    /// <para>
    /// It replaces <c>SharedKernel.Presentation.WebApi</c>'s former <c>UseSharedKernelCorrelationId()</c>, which it
    /// absorbs: one middleware now owns both the correlation id and the request's context.
    /// </para>
    /// </remarks>
    public static IApplicationBuilder UseSharedKernelRequestContext(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (app.ApplicationServices.GetService<IServiceProviderIsService>() is { } isService
            && !isService.IsService(typeof(SecurityRequestContext)))
        {
            throw new InvalidOperationException(
                "UseSharedKernelRequestContext() requires services.AddSharedKernelRequestContext() to be called first.");
        }

        return app.UseMiddleware<RequestContextMiddleware>();
    }
}
