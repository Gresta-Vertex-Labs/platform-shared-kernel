using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Cryptography.Extensions;
using SharedKernel.Cryptography.Totp;
using SharedKernel.Security.Totp.Challenge;
using SharedKernel.Security.Totp.Enrollment;
using SharedKernel.Security.Totp.StepUp;

namespace SharedKernel.Security.Totp.Extensions;

/// <summary>
/// Extension methods for registering TOTP second-factor enrollment/challenge orchestration and the
/// step-up claims transformation.
/// </summary>
public static class TotpServiceCollectionExtensions
{
    /// <summary>
    /// Registers <c>01.Core</c>'s <see cref="ITotpVerifier"/> (via
    /// <see cref="CryptographyServiceCollectionExtensions.AddTotpVerification"/>),
    /// <typeparamref name="TChallengeStore"/> as the scoped <see cref="ITotpChallengeStore"/>,
    /// <see cref="TotpStepUpOptions"/>, an <see cref="IClaimsTransformation"/> →
    /// <see cref="TotpStepUpClaimsTransformation"/>, and <see cref="TotpChallengeService"/>/
    /// <see cref="TotpEnrollmentService"/>.
    /// </summary>
    /// <typeparam name="TChallengeStore">
    /// The consumer-supplied <see cref="ITotpChallengeStore"/> implementation. This package never
    /// dictates a storage mechanism.
    /// </typeparam>
    /// <param name="cryptography">
    /// The builder returned by <c>services.AddSharedKernelCryptography(configuration)</c>, which registers the
    /// <see cref="ITotpGenerator"/>, <see cref="IRecoveryCodeGenerator"/> and random generator this package composes.
    /// </param>
    /// <param name="configureOptions">Optional configuration for <see cref="TotpStepUpOptions"/>.</param>
    /// <returns>The same <paramref name="cryptography"/> builder for chaining.</returns>
    /// <remarks>
    /// <para>
    /// Chained onto <c>01.Core</c>'s <see cref="ICryptographyBuilder"/> so the cryptography services it needs
    /// cannot be forgotten. NOT chained onto <c>SharedKernel.Security.Oidc.Extensions.SecurityAuthenticationBuilder</c>
    /// — that type is owned by <c>.Oidc</c>, and this package cannot reference <c>.Oidc</c> under the
    /// sibling-packages-never-reference-each-other rule.
    /// </para>
    /// <para>
    /// Call this AFTER <c>AddSharedKernelSecurity</c>/<c>AddAzureB2CAuthentication</c> (so an
    /// authenticated JWT Bearer principal exists for the transformation to observe). Register an
    /// <see cref="ITotpReplayGuard"/> as a singleton over a store shared by every replica as well — the
    /// consuming service supplies it, and <see cref="ITotpVerifier"/> cannot be resolved without it.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddSingleton&lt;ITotpReplayGuard, RedisTotpReplayGuard&gt;();
    /// services.AddSharedKernelCryptography(configuration)
    ///     .AddTotpStepUp&lt;RedisTotpChallengeStore&gt;();
    /// </code>
    /// </example>
    public static ICryptographyBuilder AddTotpStepUp<TChallengeStore>(
        this ICryptographyBuilder cryptography,
        Action<TotpStepUpOptions>? configureOptions = null)
        where TChallengeStore : class, ITotpChallengeStore
    {
        ArgumentNullException.ThrowIfNull(cryptography);

        var options = new TotpStepUpOptions();
        configureOptions?.Invoke(options);

        cryptography.AddTotpVerification();

        IServiceCollection services = cryptography.Services;
        services.AddSingleton(options);
        services.AddScoped<ITotpChallengeStore, TChallengeStore>();
        services.AddScoped<IClaimsTransformation, TotpStepUpClaimsTransformation>();
        services.AddScoped<TotpChallengeService>();
        services.AddSingleton<TotpEnrollmentService>();

        return cryptography;
    }
}
