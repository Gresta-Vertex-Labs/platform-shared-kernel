using SharedKernel.Execution.Context;
using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SharedKernel.Security.Abstractions;
using SharedKernel.Security.Mtls.Authentication;
using SharedKernel.Security.Mtls.Options;
using SharedKernel.Security.Mtls.Validation;

namespace SharedKernel.Security.Mtls.Extensions;

/// <summary>Registers client certificate authentication.</summary>
public static class MtlsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <c>Certificate</c> authentication scheme with <typeparamref name="TValidator"/>, and
    /// <see cref="IUserContext"/> when not already registered.
    /// </summary>
    /// <typeparam name="TValidator">Decides which client a trusted certificate belongs to.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Adjusts the certificate checks; validated at startup.</param>
    /// <returns>The same service collection.</returns>
    /// <remarks>
    /// <para>
    /// The scheme is not made the default. Select it per endpoint, for example
    /// <c>[Authorize(AuthenticationSchemes = MtlsAuthenticationDefaults.AuthenticationScheme)]</c>. Kestrel must request
    /// client certificates, or the proxy that terminates TLS must forward them.
    /// </para>
    /// <para>
    /// A certificate caller is a <see cref="ActorKind.Service"/> whose subject id is the client id from the
    /// validator. Tokens bound to a certificate (RFC 8705) are checked by <c>SharedKernel.Security.Oidc</c>.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddMtlsAuthentication<TValidator>(
        this IServiceCollection services,
        Action<MtlsAuthenticationOptions>? configure = null)
        where TValidator : class, IMtlsCertificateValidator
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<MtlsAuthenticationOptions>()
            .Configure(configure ?? (_ => { }))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<MtlsAuthenticationOptions>, MtlsSettingsValidator>());

        services.AddHttpContextAccessor();
        services.TryAddScoped<IMtlsCertificateValidator, TValidator>();

        services.AddAuthentication().AddCertificate(MtlsAuthenticationDefaults.AuthenticationScheme);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<CertificateAuthenticationOptions>, ConfigureCertificateOptions>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<CertificateAuthenticationOptions>, ConfigureCertificateOptions>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<CertificateAuthenticationOptions>, ConfigureCertificateOptions>());
        services.AddOptions<CertificateAuthenticationOptions>(MtlsAuthenticationDefaults.AuthenticationScheme).ValidateOnStart();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IUserContextMapper, MtlsUserContextMapper>());
        RemoveAnonymousPlaceholder(services);
        services.TryAddScoped<IUserContext>(ResolveUserContext);

        return services;
    }

    // A registered AnonymousUserContext instance is a placeholder (for example from the persistence builder) that an
    // authentication package replaces, whichever was registered first.
    private static void RemoveAnonymousPlaceholder(IServiceCollection services)
    {
        foreach (ServiceDescriptor placeholder in services
            .Where(d => d.ServiceType == typeof(IUserContext) && !d.IsKeyedService && d.ImplementationInstance is AnonymousUserContext)
            .ToList())
        {
            services.Remove(placeholder);
        }
    }

    private static IUserContext ResolveUserContext(IServiceProvider services) =>
        UserContextResolver.Resolve(
            services.GetRequiredService<IHttpContextAccessor>().HttpContext?.User,
            services.GetServices<IUserContextMapper>());

    private sealed class MtlsSettingsValidator : IValidateOptions<MtlsAuthenticationOptions>
    {
        public ValidateOptionsResult Validate(string? name, MtlsAuthenticationOptions options) =>
            ConfigureCertificateOptions.ValidateSettings(options);
    }
}
