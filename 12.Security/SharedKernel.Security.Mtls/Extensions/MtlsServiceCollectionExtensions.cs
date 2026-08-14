using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Security.Abstractions.Abstractions;
using SharedKernel.Security.Mtls.Options;
using SharedKernel.Security.Mtls.Validation;

namespace SharedKernel.Security.Mtls.Extensions;

/// <summary>
/// <see cref="IServiceCollection"/> extension methods for registering mutual-TLS client-certificate
/// authentication.
/// </summary>
public static class MtlsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the mutual-TLS client-certificate authentication scheme, composing it alongside an
    /// already-registered JWT Bearer/API-key scheme via the same <see cref="ServiceDescriptor"/>-capture
    /// decorator mechanism <c>AddApiKeyAuthentication</c> already established.
    /// </summary>
    /// <typeparam name="TValidator">
    /// The consumer-supplied <see cref="IMtlsCertificateValidator"/> implementation. This package never
    /// dictates a CA trust store, revocation-check mechanism, or certificate storage.
    /// </typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Optional configuration for <see cref="MtlsAuthenticationOptions"/>.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <remarks>
    /// <para>
    /// This scheme is for mutual-TLS client-certificate scenarios only. It does not perform certificate
    /// issuance, CA management, or revocation checking (CRL/OCSP) — those remain the consuming service's
    /// own concern, identical in spirit to <c>.ApiKey</c>'s key-issuance/rotation/storage disclaimer
    /// (WO-058, P-377).
    /// </para>
    /// <para>
    /// This package cannot reference <c>SharedKernel.Security.Oidc</c> or <c>SharedKernel.Security.ApiKey</c>
    /// (sibling providers never reference each other), so it captures whatever <see cref="IUserContext"/>
    /// factory is already registered and layers its own certificate-aware resolution on top.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddMtlsAuthentication<TValidator>(
        this IServiceCollection services,
        Action<MtlsAuthenticationOptions>? configureOptions = null)
        where TValidator : class, IMtlsCertificateValidator
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpContextAccessor();
        services.AddScoped<IMtlsCertificateValidator, TValidator>();

        var options = new MtlsAuthenticationOptions();
        configureOptions?.Invoke(options);

        RegisterSchemeAwareUserContext(services);

        services
            .AddAuthentication()
            .AddCertificate(MtlsAuthenticationOptions.DefaultScheme, certificateOptions =>
            {
                certificateOptions.AllowedCertificateTypes = options.AllowedCertificateTypes;
                certificateOptions.RevocationMode = options.RevocationMode;
                certificateOptions.Events = new CertificateAuthenticationEvents
                {
                    OnCertificateValidated = MtlsAuthenticationHandler.HandleCertificateValidatedAsync,
                };
            });

        return services;
    }

    private static void RegisterSchemeAwareUserContext(IServiceCollection services)
    {
        // Capture whatever IUserContext factory is already registered (typically OidcUserContext- or
        // ApiKeyUserContext-backed) so the scheme-aware factory below can delegate to it for
        // non-certificate-authenticated requests without ever referencing SharedKernel.Security.Oidc
        // or SharedKernel.Security.ApiKey — sibling provider packages never reference each other.
        var previousUserContext = services.LastOrDefault(d => d.ServiceType == typeof(IUserContext));

        services.AddScoped<IUserContext>(sp =>
        {
            var accessor = sp.GetRequiredService<IHttpContextAccessor>();
            var user = accessor.HttpContext?.User;

            if (user is not null
                && string.Equals(user.Identity?.AuthenticationType, MtlsAuthenticationOptions.DefaultScheme, StringComparison.Ordinal))
            {
                return new MtlsUserContext(user);
            }

            if (previousUserContext?.ImplementationFactory is { } previousFactory)
            {
                return (IUserContext)previousFactory(sp);
            }

            return AnonymousUserContext.Instance;
        });
    }
}
