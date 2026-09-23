using Microsoft.AspNetCore.Http;

namespace SharedKernel.Presentation.WebApi;

/// <summary>Reads the correlation id <c>UseSharedKernelWebApi()</c> resolved for the current request.</summary>
public static class CorrelationIdHttpContextExtensions
{
    private static readonly object ItemsKey = new();

    /// <summary>Returns the correlation id of the request.</summary>
    /// <param name="httpContext">The current request.</param>
    /// <returns>
    /// The validated inbound <c>X-Correlation-Id</c>, or the id assigned when the caller sent none or an invalid one
    /// (the trace id when the request is traced). <see langword="null"/> when correlation ids are disabled or the
    /// request did not pass through <c>UseSharedKernelWebApi()</c>.
    /// </returns>
    /// <remarks>
    /// The same value is in the <c>X-Correlation-Id</c> response header, in the <c>correlationId</c> member of every
    /// error response and in <see cref="System.Diagnostics.Activity"/> baggage, from where logs and outgoing calls
    /// pick it up. In a gRPC service call <c>context.GetHttpContext().GetCorrelationId()</c>.
    /// </remarks>
    public static string? GetCorrelationId(this HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        return httpContext.Items.TryGetValue(ItemsKey, out var value) ? value as string : null;
    }

    internal static void Set(HttpContext httpContext, string correlationId) => httpContext.Items[ItemsKey] = correlationId;
}
