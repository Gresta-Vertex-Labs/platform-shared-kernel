using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Security.Totp.Challenge;
using SharedKernel.Security.Totp.Enrollment;
using SharedKernel.Security.Totp.StepUp;

namespace SharedKernel.Security.Totp.Extensions;

/// <summary>
/// <see cref="IServiceCollection"/> extension methods for registering TOTP second-factor
/// enrollment/challenge orchestration and the step-up claims transformation.
/// </summary>
public static class TotpServiceCollectionExtensions
{
    /// <summary>
    /// Registers <typeparamref name="TChallengeStore"/> as the scoped <see cref="ITotpChallengeStore"/>,
    /// <see cref="TotpStepUpOptions"/>, an <see cref="IClaimsTransformation"/> →
    /// <see cref="TotpStepUpClaimsTransformation"/>, and <see cref="TotpChallengeService"/>/
    /// <see cref="TotpEnrollmentService"/>.
    /// </summary>
    /// <typeparam name="TChallengeStore">
    /// The consumer-supplied <see cref="ITotpChallengeStore"/> implementation. This package never
    /// dictates a storage mechanism.
    /// </typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Optional configuration for <see cref="TotpStepUpOptions"/>.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    /// <remarks>
    /// <para>
    /// NOT chained onto <c>SharedKernel.Security.Oidc.Extensions.SecurityAuthenticationBuilder</c> —
    /// that type is owned by <c>.Oidc</c>, and this package cannot reference <c>.Oidc</c> under the
    /// sibling-packages-never-reference-each-other rule. A plain <see cref="IServiceCollection"/>
    /// extension, mirroring <c>AddApiKeyAuthentication</c>/<c>AddMtlsAuthentication</c>'s own
    /// independent-extension shape exactly.
    /// </para>
    /// <para>
    /// Call this AFTER <c>AddSharedKernelSecurity</c>/<c>AddAzureB2CAuthentication</c> (so an
    /// authenticated JWT Bearer principal exists for the transformation to observe) AND AFTER
    /// <c>AddSharedKernelCryptography</c> (so <c>ITotpGenerator</c>/<c>TotpVerifier</c> are already
    /// registered — this method does not register them itself, and does not register
    /// <c>ITotpReplayGuard</c>, which the consuming service must also supply separately).
    /// </para>
    /// </remarks>
    public static IServiceCollection AddTotpStepUp<TChallengeStore>(
        this IServiceCollection services,
        Action<TotpStepUpOptions>? configureOptions = null)
        where TChallengeStore : class, ITotpChallengeStore
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new TotpStepUpOptions();
        configureOptions?.Invoke(options);

        services.AddSingleton(options);
        services.AddScoped<ITotpChallengeStore, TChallengeStore>();
        services.AddScoped<IClaimsTransformation, TotpStepUpClaimsTransformation>();
        services.AddScoped<TotpChallengeService>();
        services.AddSingleton<TotpEnrollmentService>();

        return services;
    }
}
