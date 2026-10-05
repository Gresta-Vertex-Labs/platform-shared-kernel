using Microsoft.AspNetCore.Diagnostics;

namespace SharedKernel.Presentation.WebApi;

/// <summary>
/// The status-code-pages handler: gives a bodiless 4xx or 5xx response the framework produced — an unmatched
/// route (404), a wrong method (405), an unsupported media type (415) — the platform's problem body.
/// </summary>
internal static class StatusCodeProblemHandler
{
    /// <summary>Writes the problem for the response status, leaving gRPC calls untouched.</summary>
    public static Task HandleAsync(StatusCodeContext context)
    {
        var httpContext = context.HttpContext;

        // A gRPC client reads the HTTP status itself and cannot use a JSON body.
        if (RequestFacts.IsGrpcRequest(httpContext))
        {
            return Task.CompletedTask;
        }

        return ProblemResponseWriter.WriteAsync(
            httpContext,
            ProblemFactory.ForStatus(httpContext, httpContext.Response.StatusCode));
    }
}
