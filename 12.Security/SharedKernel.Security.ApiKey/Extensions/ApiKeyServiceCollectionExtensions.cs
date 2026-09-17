using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using SharedKernel.Cryptography.Random;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Security.Abstractions;
using SharedKernel.Security.ApiKey.Authentication;
using SharedKernel.Security.ApiKey.Keys;
using SharedKernel.Security.ApiKey.Options;
using SharedKernel.Security.ApiKey.Validation;

namespace SharedKernel.Security.ApiKey.Extensions;

/// <summary>Registers API key authentication.</summary>
public static class ApiKeyServiceCollectionExtensions
{
    /// <summary>
    /// Registers managed API keys: the <c>ApiKey</c> scheme, <see cref="ApiKeyGenerator"/>, and a validator over
    /// <typeparamref name="TStore"/> that checks the checksum, hash, expiry and revocation.
    /// </summary>
    /// <typeparam name="TStore">The store holding key records.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configureKeys">Sets <see cref="ManagedApiKeyOptions.Prefix"/>, which is validated at startup.</param>
    /// <param name="configureScheme">Adjusts the scheme options, such as the header name.</param>
    /// <returns>The same service collection.</returns>
    /// <remarks>See <see cref="AddApiKeyAuthentication{TValidator}"/> for how the scheme is selected.</remarks>
    public static IServiceCollection AddManagedApiKeyAuthentication<TStore>(
        this IServiceCollection services,
        Action<ManagedApiKeyOptions> configureKeys,
        Action<ApiKeyAuthenticationOptions>? configureScheme = null)
        where TStore : class, IApiKeyStore
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureKeys);

        services.AddOptions<ManagedApiKeyOptions>()
            .Configure(configureKeys)
            .Validate(options => ApiKeyFormat.IsValidPrefix(options.Prefix),
                "ManagedApiKeyOptions.Prefix must be 2-32 lowercase letters, digits and single underscores, starting with a letter.")
            .ValidateOnStart();

        services.AddClock();
        services.TryAddSingleton<ISecureRandomGenerator, SecureRandomGenerator>();
        services.TryAddSingleton<ApiKeyGenerator>();
        services.TryAddScoped<IApiKeyStore, TStore>();

        return services.AddApiKeyAuthentication<ManagedApiKeyValidator>(configureScheme);
    }

    /// <summary>Registers the <c>ApiKey</c> scheme with a custom validator.</summary>
    /// <typeparam name="TValidator">The validator.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configureScheme">Adjusts the scheme options, such as the header name.</param>
    /// <returns>The same service collection.</returns>
    /// <remarks>
    /// <para>
    /// The <c>ApiKeyOrDefault</c> forwarding scheme becomes the default scheme. A request with the API key header is
    /// authenticated by the <c>ApiKey</c> scheme; any other request by the scheme that was the default before (for
    /// example <c>Bearer</c> from <c>AddOidcAuthentication</c>), in whichever order the two were registered.
    /// </para>
    /// <para>
    /// Registers <see cref="IUserContext"/> and <see cref="ITenantProvider"/> when not already registered. An API key
    /// caller is a <see cref="IdentityKind.ServicePrincipal"/> whose subject id is the client id.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddApiKeyAuthentication<TValidator>(
        this IServiceCollection services,
        Action<ApiKeyAuthenticationOptions>? configureScheme = null)
        where TValidator : class, IApiKeyValidator
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpContextAccessor();
        services.TryAddScoped<IApiKeyValidator, TValidator>();

        var forwarding = new ApiKeyForwarding();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPostConfigureOptions<AuthenticationOptions>>(forwarding));

        services.AddAuthentication()
            .AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(
                ApiKeyAuthenticationDefaults.AuthenticationScheme,
                configureScheme ?? (_ => { }))
            .AddPolicyScheme(ApiKeyAuthenticationDefaults.ForwardingScheme, displayName: null, options =>
                options.ForwardDefaultSelector = context => SelectScheme(context, ResolveFallback(context.RequestServices)));

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IUserContextMapper, ApiKeyUserContextMapper>());
        RemoveAnonymousPlaceholder(services);
        services.TryAddScoped<IUserContext>(ResolveUserContext);
        services.TryAddScoped<ITenantProvider, UserContextTenantProvider>();

        return services;
    }

    private static string? ResolveFallback(IServiceProvider services) =>
        services.GetServices<IPostConfigureOptions<AuthenticationOptions>>().OfType<ApiKeyForwarding>().FirstOrDefault()?.FallbackScheme;

    private static string SelectScheme(HttpContext context, string? fallback)
    {
        string header = context.RequestServices
            .GetRequiredService<IOptionsMonitor<ApiKeyAuthenticationOptions>>()
            .Get(ApiKeyAuthenticationDefaults.AuthenticationScheme)
            .HeaderName;

        return context.Request.Headers.TryGetValue(header, out StringValues values) && values.Any(value => !string.IsNullOrWhiteSpace(value))
            ? ApiKeyAuthenticationDefaults.AuthenticationScheme
            : fallback ?? ApiKeyAuthenticationDefaults.AuthenticationScheme;
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
