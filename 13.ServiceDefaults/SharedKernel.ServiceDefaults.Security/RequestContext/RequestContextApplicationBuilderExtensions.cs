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
    /// <b>Middleware order.</b> Call it <b>first</b>, before the exception handler, so inbound baggage is refused
    /// before anything reads it, and the correlation id is in the log context of every later middleware and on every
    /// response, error responses included. With <c>SharedKernel.Presentation.WebApi</c>:
    /// </para>
    /// <code>
    /// app.UseSharedKernelRequestContext();   // inbound baggage refused, correlation id, the request's context scope
    /// app.UseSharedKernelWebApi(pipeline =&gt; pipeline
    ///     .BeforeAuthorization(a =&gt; a.UseMiddleware&lt;TenantResolutionMiddleware&gt;()));   // optional
    /// app.MapEndpoints();
    /// </code>
    /// <para>
    /// Without it: <c>UseSharedKernelRequestContext()</c>, <c>UseExceptionHandler()</c>, <c>UseAuthentication()</c>,
    /// the optional <c>TenantResolutionMiddleware</c> (it refines the tenant in an inner scope),
    /// <c>UseAuthorization()</c>. gRPC and SignalR requests pass through the same pipeline and get the same scope.
    /// </para>
    /// <para>
    /// Placing it before <c>UseAuthentication()</c> is safe: the caller is read from the request's
    /// <c>IUserContext</c> only when something first asks, which is after authentication has run. Nothing between
    /// this middleware and <c>UseAuthentication()</c> should read the caller.
    /// </para>
    /// <para>
    /// It replaces <c>SharedKernel.Presentation.WebApi</c>'s former correlation-id middleware and inbound-baggage step,
    /// which it absorbs: one middleware now owns the correlation id, the refusal of caller baggage
    /// (<see cref="RequestContextOptions.TrustInboundBaggage"/>) and the request's context.
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
