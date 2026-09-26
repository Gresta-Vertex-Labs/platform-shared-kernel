using Microsoft.AspNetCore.Http;

namespace SharedKernel.Presentation.WebApi;

/// <summary>Reads the correlation id of the current request.</summary>
public static class CorrelationIdHttpContextExtensions
{
    /// <summary>Returns the correlation id of the request.</summary>
    /// <param name="httpContext">The current request.</param>
    /// <returns>
    /// The correlation id <c>SharedKernel.ServiceDefaults.Security</c>'s <c>UseSharedKernelRequestContext()</c>
    /// resolved for the request — the validated inbound <c>X-Correlation-Id</c>, or a new one when the caller sent none
    /// or an invalid one — read from the request's <c>RequestContextScope</c>. <see langword="null"/> when the request
    /// did not pass through that middleware.
    /// </returns>
    /// <remarks>
    /// The same value is in the <c>X-Correlation-Id</c> response header, in the <c>correlationId</c> member of every
    /// error response and in <see cref="System.Diagnostics.Activity"/> baggage, from where logs and outgoing calls
    /// pick it up. Where there is no <see cref="HttpContext"/>, read <c>IRequestContext.CorrelationId</c> or
    /// <c>IRequestContextAccessor</c>: this method reads the same scope. In a gRPC service call
    /// <c>context.GetHttpContext().GetCorrelationId()</c>.
    /// </remarks>
    public static string? GetCorrelationId(this HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        return RequestFacts.GetCorrelationId(httpContext);
    }
}
