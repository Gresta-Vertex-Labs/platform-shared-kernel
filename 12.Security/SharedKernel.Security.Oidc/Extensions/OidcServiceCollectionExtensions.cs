using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SharedKernel.Configuration.Extensions;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Security.Abstractions;
using SharedKernel.Security.Oidc.Authentication;
using SharedKernel.Security.Oidc.Options;

namespace SharedKernel.Security.Oidc.Extensions;

/// <summary>Registers JWT bearer authentication for an OpenID Connect provider.</summary>
public static class OidcServiceCollectionExtensions
{
    /// <summary>
    /// Registers the <c>Bearer</c> authentication scheme as the default scheme, <see cref="IUserContext"/> and
    /// <see cref="ITenantProvider"/> (scoped), and an <see cref="IClock"/> when none is registered.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration root; settings bind from <c>SharedKernel:Security:Oidc</c>.</param>
    /// <returns>A builder for DPoP and token revocation.</returns>
    /// <remarks>
    /// <para>
    /// Tokens must be signed with an allowed asymmetric algorithm, come from the configured issuer and audience, and be
    /// within their lifetime. Tokens bound to a DPoP key or client certificate are accepted only with that proof.
    /// Claims keep the names the provider issued.
    /// </para>
    /// <para>
    /// <see cref="IUserContext"/> and <see cref="ITenantProvider"/> are added only when not already registered, so a
    /// worker host can register <see cref="SystemUserContext"/> first.
    /// </para>
    /// </remarks>
    /// <exception cref="OptionsValidationException">At host startup, when the settings are invalid.</exception>
    [RequiresUnreferencedCode("Binds configuration by reflection.")]
    [RequiresDynamicCode("Binds configuration by reflection.")]
    public static OidcAuthenticationBuilder AddOidcAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddValidatedOptions<OidcAuthenticationOptions, OidcAuthenticationOptionsValidator>(configuration, validateDataAnnotations: true);
        services.AddClock();
        services.AddHttpContextAccessor();

        services
            .AddAuthentication(OidcAuthenticationDefaults.AuthenticationScheme)
            .AddJwtBearer(OidcAuthenticationDefaults.AuthenticationScheme);

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<JwtBearerOptions>, ConfigureOidcJwtBearerOptions>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<JwtBearerOptions>, ConfigureOidcJwtBearerOptions>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<JwtBearerOptions>, ConfigureOidcJwtBearerOptions>());
        services.AddOptions<JwtBearerOptions>(OidcAuthenticationDefaults.AuthenticationScheme).ValidateOnStart();
        services.Configure<AuthenticationOptions>(options =>
            options.SchemeMap[OidcAuthenticationDefaults.AuthenticationScheme].HandlerType = typeof(OidcJwtBearerHandler));

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IUserContextMapper, OidcUserContextMapper>());
        RemoveAnonymousPlaceholder(services);
        services.TryAddScoped<IUserContext>(ResolveUserContext);
        services.TryAddScoped<ITenantProvider, UserContextTenantProvider>();

        return new OidcAuthenticationBuilder(services);
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
}
