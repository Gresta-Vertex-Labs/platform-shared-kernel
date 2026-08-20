using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace SharedKernel.Presentation.WebApi.Cors;

/// <summary>
/// DI extension for wiring the platform's named CORS policy.
/// </summary>
public static class CorsExtensions
{
    /// <summary>
    /// Registers <c>Microsoft.AspNetCore.Cors</c> with a single named policy
    /// (<see cref="CorsPolicyNames.Default"/>) built from <paramref name="configure"/>, plus a
    /// startup-time fail-fast guard rejecting the <c>AllowCredentials</c> + empty/wildcard-origin
    /// misconfiguration.
    /// </summary>
    /// <param name="services">The service collection to add registrations to.</param>
    /// <param name="configure">Configures the CORS policy's origins, methods, headers, and credentials.</param>
    /// <returns>The same <paramref name="services"/> instance, for chaining.</returns>
    /// <remarks>
    /// <see cref="CorsPolicyOptions"/> is also registered through the standard
    /// <c>Microsoft.Extensions.Options</c> pipeline with <c>ValidateOnStart()</c> so
    /// <see cref="CorsPolicyOptionsValidator"/> runs — and fails fast — at
    /// <c>IHost.StartAsync()</c>, never deferred to the first real credentialed cross-origin
    /// request.
    /// </remarks>
    public static IServiceCollection AddSharedKernelCors(
        this IServiceCollection services,
        Action<CorsPolicyOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<CorsPolicyOptions>().Configure(configure).ValidateOnStart();
        services.AddSingleton<IValidateOptions<CorsPolicyOptions>, CorsPolicyOptionsValidator>();

        var policyOptions = new CorsPolicyOptions();
        configure(policyOptions);

        services.AddCors(corsOptions => corsOptions.AddPolicy(CorsPolicyNames.Default, policyBuilder =>
        {
            if (policyOptions.AllowedOrigins.Count > 0)
                policyBuilder.WithOrigins(policyOptions.AllowedOrigins.ToArray());

            if (policyOptions.AllowedMethods.Count > 0)
                policyBuilder.WithMethods(policyOptions.AllowedMethods.ToArray());
            else
                policyBuilder.AllowAnyMethod();

            if (policyOptions.AllowedHeaders.Count > 0)
                policyBuilder.WithHeaders(policyOptions.AllowedHeaders.ToArray());
            else
                policyBuilder.AllowAnyHeader();

            if (policyOptions.AllowCredentials)
                policyBuilder.AllowCredentials();
        }));

        return services;
    }
}
