using Microsoft.AspNetCore.Http;

namespace SharedKernel.Presentation.Authorization;

/// <summary>
/// Writes the body of a refused HTTP request, after <see cref="SharedKernelAuthorizationResultHandler"/> has set the
/// status and the challenge headers.
/// </summary>
/// <remarks>
/// The seam between the shared authorization and the HTTP error contract: <c>SharedKernel.Presentation.WebApi</c>
/// registers the implementation that writes its RFC 9457 problem response (through <c>AddSharedKernelWebApi()</c>, and
/// <c>AddSharedKernelSignalR()</c> for negotiate requests). Without one — a gRPC-only host — a refusal is its status and
/// headers alone. Never called for a gRPC call, which carries no body.
/// </remarks>
internal interface IAuthorizationRefusalWriter
{
    /// <summary>Writes the body of a refusal.</summary>
    /// <param name="context">The refused request; its status is already <paramref name="statusCode"/>.</param>
    /// <param name="statusCode">401 or 403.</param>
    /// <param name="code">The error code, such as <c>forbidden.insufficient_permission</c>.</param>
    /// <param name="message">The untranslated client message.</param>
    /// <returns>A task that completes when the body is written.</returns>
    Task WriteAsync(HttpContext context, int statusCode, string code, string message);
}
