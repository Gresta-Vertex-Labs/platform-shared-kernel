using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharedKernel.Security.Abstractions.Abstractions;
using SharedKernel.Security.ApiKey.Options;
using SharedKernel.Security.ApiKey.Validation;

namespace SharedKernel.Security.ApiKey.Extensions;

/// <summary>
/// <see cref="IServiceCollection"/> extension methods for registering API-key authentication.
/// </summary>
public static class ApiKeyServiceCollectionExtensions
{
    /// <summary>
    /// Registers the API-key authentication scheme, composing it alongside an already-registered JWT
    /// Bearer scheme (e.g. from <c>AddSharedKernelSecurity</c>/<c>AddAzureB2CAuthentication</c>) via a
    /// policy/forwarding scheme, so a host can accept either credential type on the same set of endpoints
    /// without one scheme silently shadowing the other.
    /// </summary>
    /// <typeparam name="TValidator">
    /// The consumer-supplied <see cref="IApiKeyValidator"/> implementation. This package never dictates
    /// a key storage mechanism.
    /// </typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Optional configuration for <see cref="ApiKeyAuthenticationOptions"/>.</param>
    /// <param name="fallbackAuthenticationScheme">
    /// The authentication scheme to forward to when no API-key credential is present on the request.
    /// Defaults to <c>"Bearer"</c> — the scheme name <c>AddSharedKernelSecurity</c>/
    /// <c>AddAzureB2CAuthentication</c> register via
    /// <c>Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme</c>.
    /// <c>SharedKernel.Security.ApiKey</c> deliberately never references the
    /// <c>Microsoft.AspNetCore.Authentication.JwtBearer</c> NuGet package (see this domain's package
    /// reference rules), so that constant cannot be referenced directly here — override this parameter
    /// if the JWT Bearer scheme was registered under a non-default name.
    /// </param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <remarks>
    /// <para>
    /// Call this method after <c>AddSharedKernelSecurity</c>/<c>AddAzureB2CAuthentication</c> so the
    /// scheme-aware <see cref="IUserContext"/> factory registered here can correctly delegate to the
    /// already-registered OIDC-backed factory for non-API-key-authenticated requests, without this
    /// package ever referencing <c>SharedKernel.Security.Oidc</c> — sibling provider packages never
    /// reference each other.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddApiKeyAuthentication<TValidator>(
        this IServiceCollection services,
        Action<ApiKeyAuthenticationOptions>? configureOptions = null,
        string fallbackAuthenticationScheme = "Bearer")
        where TValidator : class, IApiKeyValidator
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(fallbackAuthenticationScheme);

        services.AddHttpContextAccessor();
        services.AddScoped<IApiKeyValidator, TValidator>();

        RegisterSchemeAwareUserContext(services);

        var builder = services.AddAuthentication(ApiKeyAuthenticationOptions.CompositeSchemeName);

        builder.AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
            ApiKeyAuthenticationOptions.DefaultScheme,
            configureOptions ?? (_ => { }));

        builder.AddPolicyScheme(
            ApiKeyAuthenticationOptions.CompositeSchemeName,
            "SharedKernel composite (JWT Bearer or API key)",
            policyOptions =>
            {
                policyOptions.ForwardDefaultSelector = context =>
                {
                    var apiKeyOptions = context.RequestServices
                        .GetRequiredService<IOptionsMonitor<ApiKeyAuthenticationOptions>>()
                        .Get(ApiKeyAuthenticationOptions.DefaultScheme);

                    var hasHeader = context.Request.Headers.ContainsKey(apiKeyOptions.HeaderName);
                    var hasQuery = !string.IsNullOrEmpty(apiKeyOptions.QueryParameterName)
                        && context.Request.Query.ContainsKey(apiKeyOptions.QueryParameterName);

                    return hasHeader || hasQuery
                        ? ApiKeyAuthenticationOptions.DefaultScheme
                        : fallbackAuthenticationScheme;
                };
            });

        return services;
    }

    private static void RegisterSchemeAwareUserContext(IServiceCollection services)
    {
        // Capture whatever IUserContext factory is already registered (typically OidcUserContext-backed,
        // via AddSharedKernelSecurity/AddAzureB2CAuthentication) so the scheme-aware factory below can
        // delegate to it for non-API-key-authenticated requests without ever referencing
        // SharedKernel.Security.Oidc — sibling provider packages never reference each other.
        var previousUserContext = services.LastOrDefault(d => d.ServiceType == typeof(IUserContext));

        services.AddScoped<IUserContext>(sp =>
        {
            var accessor = sp.GetRequiredService<IHttpContextAccessor>();
            var user = accessor.HttpContext?.User;

            if (user is not null
                && string.Equals(user.Identity?.AuthenticationType, ApiKeyAuthenticationOptions.DefaultScheme, StringComparison.Ordinal))
            {
                return new ApiKeyUserContext(user);
            }

            if (previousUserContext?.ImplementationFactory is { } previousFactory)
            {
                return (IUserContext)previousFactory(sp);
            }

            return AnonymousUserContext.Instance;
        });
    }
}
