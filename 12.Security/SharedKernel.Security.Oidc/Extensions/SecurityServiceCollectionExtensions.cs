using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using SharedKernel.Configuration.Extensions;
using SharedKernel.Security.Abstractions.Abstractions;
using SharedKernel.Security.Oidc.Mapping;
using SharedKernel.Security.Oidc.Options;

namespace SharedKernel.Security.Oidc.Extensions;

/// <summary>
/// <see cref="IServiceCollection"/> extension methods for registering SharedKernel security services.
/// </summary>
public static class SecurityServiceCollectionExtensions
{
    /// <summary>
    /// Registers JWT Bearer authentication, <see cref="IUserContext"/> (scoped), and
    /// <see cref="ITenantProvider"/> (scoped) for standard OIDC / Microsoft Entra ID scenarios.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">
    /// The application configuration. Must contain a <c>Security</c> section with <c>Jwt.Authority</c>
    /// and <c>Jwt.Audience</c>; the application fails at startup when these are missing.
    /// </param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <remarks>
    /// <para>
    /// Call this method after <c>AddAuthentication()</c> in the host startup pipeline.
    /// </para>
    /// <para>
    /// JWT Bearer defaults enforced:
    /// <list type="bullet">
    ///   <item><c>ValidateIssuer = true</c></item>
    ///   <item><c>ValidateAudience = true</c></item>
    ///   <item><c>ValidateLifetime</c> — driven by <see cref="SecurityOptions.JwtOptions.ValidateLifetime"/> (defaults to <see langword="true"/>)</item>
    /// </list>
    /// Any relaxation of these defaults must be explicit and documented at the call site.
    /// </para>
    /// <para>
    /// <see cref="IUserContext"/> resolves to <see cref="AnonymousUserContext"/> when no
    /// <see cref="Microsoft.AspNetCore.Http.HttpContext"/> is present (e.g. background workers,
    /// console hosts, or unit-test DI containers). Callers must check
    /// <see cref="IUserContext.IsAuthenticated"/> before consuming <see cref="IUserContext.UserId"/>.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddSharedKernelSecurity(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Bind and eagerly validate SecurityOptions at startup via AddValidatedOptions.
        services.AddValidatedOptions<SecurityOptions>(
            configuration.GetSection(SecurityOptions.SectionKey));

        services.AddHttpContextAccessor();

        // Register IUserContext as Scoped — one instance per HTTP request.
        // Falls back to AnonymousUserContext when HttpContext is null (background jobs, unit tests).
        services.AddScoped<IUserContext>(sp =>
        {
            var accessor = sp.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
            var user = accessor.HttpContext?.User;
            return user is not null
                ? new OidcUserContext(user)
                : AnonymousUserContext.Instance;
        });

        // Register ITenantProvider as Scoped — one instance per HTTP request.
        // Returns Guid.Empty when HttpContext is null.
        services.AddScoped<ITenantProvider>(sp =>
        {
            var accessor = sp.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
            var user = accessor.HttpContext?.User;
            return user is not null
                ? new OidcTenantProvider(user)
                : new OidcTenantProvider(new System.Security.Claims.ClaimsPrincipal());
        });

        // Configure JWT Bearer. Authority and Audience are resolved at options-resolution time
        // via IOptionsMonitor<SecurityOptions>, not at service-collection configuration time,
        // so no BuildServiceProvider() is needed (avoids the secondary-container anti-pattern).
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme);

        // Post-configure JwtBearerOptions from SecurityOptions — runs after startup validation,
        // so SecurityOptions is guaranteed valid when the options are first consumed.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<SecurityOptions>>((jwtBearerOptions, securityOptions) =>
            {
                var jwt = securityOptions.Value.Jwt;

                jwtBearerOptions.Authority = jwt.Authority;
                jwtBearerOptions.Audience = jwt.Audience;

                jwtBearerOptions.TokenValidationParameters = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
                {
                    // ValidateIssuer = true is the default — made explicit to document the intent.
                    ValidateIssuer = true,
                    // ValidateAudience = true is the default — made explicit to document the intent.
                    ValidateAudience = true,
                    ValidateLifetime = jwt.ValidateLifetime,
                    ClockSkew = TimeSpan.FromSeconds(jwt.ClockSkewSeconds),
                };
            });

        return services;
    }

    /// <summary>
    /// Registers JWT Bearer authentication configured for Azure B2C / Microsoft Entra External ID,
    /// along with <see cref="IUserContext"/> (scoped) and <see cref="ITenantProvider"/> (scoped).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">
    /// The application configuration. Must contain an <c>AzureAdB2C</c> section consumed by
    /// <c>Microsoft.Identity.Web</c>, and a <c>Security</c> section for <see cref="SecurityOptions"/>.
    /// </param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <remarks>
    /// <para>
    /// Uses <c>Microsoft.Identity.Web</c> for Azure B2C authority/audience resolution.
    /// </para>
    /// <para>
    /// <b>AOT note:</b> <c>Microsoft.Identity.Web</c> is not fully AOT-safe. This method is isolated
    /// so that services using standard Entra ID (non-B2C) can call <see cref="AddSharedKernelSecurity"/>
    /// instead, avoiding the AOT blast radius entirely.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddAzureB2CAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Bind and eagerly validate SecurityOptions at startup.
        services.AddValidatedOptions<SecurityOptions>(
            configuration.GetSection(SecurityOptions.SectionKey));

        services.AddHttpContextAccessor();

        // Register IUserContext as Scoped. Falls back to AnonymousUserContext when HttpContext is null.
        services.AddScoped<IUserContext>(sp =>
        {
            var accessor = sp.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
            var user = accessor.HttpContext?.User;
            return user is not null
                ? new OidcUserContext(user)
                : AnonymousUserContext.Instance;
        });

        // Register ITenantProvider as Scoped. Returns Guid.Empty when HttpContext is null.
        services.AddScoped<ITenantProvider>(sp =>
        {
            var accessor = sp.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
            var user = accessor.HttpContext?.User;
            return user is not null
                ? new OidcTenantProvider(user)
                : new OidcTenantProvider(new System.Security.Claims.ClaimsPrincipal());
        });

        // Azure B2C / Entra External ID wiring via Microsoft.Identity.Web.
        // AOT note: Microsoft.Identity.Web is not fully AOT-safe — isolated here intentionally.
        services.AddMicrosoftIdentityWebApiAuthentication(configuration, "AzureAdB2C");

        return services;
    }
}
