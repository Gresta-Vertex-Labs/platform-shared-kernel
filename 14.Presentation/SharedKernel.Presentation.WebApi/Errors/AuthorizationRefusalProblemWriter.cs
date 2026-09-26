using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SharedKernel.Presentation.Authorization;

namespace SharedKernel.Presentation.WebApi;

/// <summary>
/// Writes the RFC 9457 problem body of a request the platform's authorization refused, after
/// <c>SharedKernel.Presentation.Core</c> has set the status and the challenge headers.
/// </summary>
internal sealed class AuthorizationRefusalProblemWriter : IAuthorizationRefusalWriter
{
    /// <inheritdoc />
    public Task WriteAsync(HttpContext context, int statusCode, string code, string message) =>
        ProblemResponseWriter.WriteAsync(context, ProblemFactory.ForPresentation(context, statusCode, code, message));
}

/// <summary>Registers the platform's authorization with this package's problem bodies for refusals.</summary>
internal static class WebApiAuthorizationExtensions
{
    /// <summary>
    /// Registers <c>SharedKernel.Presentation.Core</c>'s authorization — the policies behind
    /// <see cref="RequireEndpointPermissionAttribute"/> and its siblings — and the problem body every refusal gets over
    /// HTTP. Called by <c>AddSharedKernelWebApi()</c> and <c>AddSharedKernelSignalR()</c>; safe to call more than once.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same <paramref name="services"/>.</returns>
    public static IServiceCollection AddSharedKernelWebApiAuthorization(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSharedKernelAuthorization();
        services.TryAddSingleton<IAuthorizationRefusalWriter, AuthorizationRefusalProblemWriter>();

        return services;
    }
}
