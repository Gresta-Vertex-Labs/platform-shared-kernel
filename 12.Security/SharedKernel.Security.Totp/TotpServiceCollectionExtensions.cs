using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SharedKernel.Cryptography.Extensions;
using SharedKernel.Cryptography.Totp;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Security.Abstractions;

namespace SharedKernel.Security.Totp;

/// <summary>Registers TOTP step-up authentication.</summary>
public static class TotpServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="TotpEnrollmentService"/>, <see cref="TotpChallengeService"/>,
    /// <see cref="TotpStepUpClaimsTransformation"/> and <c>ITotpVerifier</c>.
    /// </summary>
    /// <typeparam name="TStepUpStore">Records step-ups per session.</typeparam>
    /// <typeparam name="TRecoveryCodeStore">Holds hashed recovery codes.</typeparam>
    /// <param name="cryptography">The builder from <c>AddSharedKernelCryptography</c>.</param>
    /// <param name="configure">Adjusts <see cref="TotpStepUpOptions"/>; validated at startup.</param>
    /// <returns>The same builder.</returns>
    /// <remarks>
    /// <para>
    /// Also register an <see cref="ITotpReplayGuard"/> shared by every replica, which <c>ITotpVerifier</c> needs, and an
    /// authentication package that registers an <see cref="IUserContextMapper"/>. Register an
    /// <see cref="ITotpAttemptThrottle"/> to limit attempts, as RFC 4226 section 7.3 requires.
    /// </para>
    /// <para>
    /// The step-up needs a session id on the caller. Identity providers that issue no <c>sid</c> claim get the token id
    /// instead, so a step-up then ends when the access token is refreshed.
    /// </para>
    /// </remarks>
    public static ICryptographyBuilder AddTotpStepUp<TStepUpStore, TRecoveryCodeStore>(
        this ICryptographyBuilder cryptography,
        Action<TotpStepUpOptions>? configure = null)
        where TStepUpStore : class, ITotpStepUpStore
        where TRecoveryCodeStore : class, IRecoveryCodeStore
    {
        ArgumentNullException.ThrowIfNull(cryptography);
        IServiceCollection services = cryptography.Services;

        cryptography.AddTotpVerification();
        services.AddClock();

        services.AddOptions<TotpStepUpOptions>()
            .Configure(configure ?? (_ => { }))
            .Validate(
                options => options.FreshnessWindow >= TimeSpan.FromMinutes(1) && options.FreshnessWindow <= TimeSpan.FromHours(24),
                "TotpStepUpOptions.FreshnessWindow must be between one minute and 24 hours.")
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.AuthenticationMethodClaimType) && !string.IsNullOrWhiteSpace(options.AuthenticationMethod),
                "TotpStepUpOptions.AuthenticationMethodClaimType and AuthenticationMethod must not be empty.")
            .ValidateOnStart();

        services.TryAddScoped<ITotpStepUpStore, TStepUpStore>();
        services.TryAddScoped<IRecoveryCodeStore, TRecoveryCodeStore>();
        services.TryAddScoped<TotpEnrollmentService>();
        services.TryAddScoped<TotpChallengeService>();

        if (!services.Any(descriptor => descriptor.ServiceType == typeof(TotpStepUpRegistration)))
        {
            services.AddSingleton<TotpStepUpRegistration>();
            RegisterClaimsTransformation(services);
        }

        return cryptography;
    }

    // ASP.NET Core resolves one IClaimsTransformation, so wrap the one already registered instead of replacing it. The
    // wrapped registration moves to a private key with its original lifetime, so the container still creates, shares
    // and disposes it as the application registered it.
    private static void RegisterClaimsTransformation(IServiceCollection services)
    {
        ServiceDescriptor? existing = services.LastOrDefault(descriptor =>
            descriptor.ServiceType == typeof(IClaimsTransformation) && !descriptor.IsKeyedService);

        bool wrap = existing is not null && existing.ImplementationType?.Name != "NoopClaimsTransformation";
        if (existing is not null)
        {
            services.Remove(existing);
        }

        if (wrap)
        {
            services.Add(ToInnerDescriptor(existing!));
        }

        services.AddScoped<IClaimsTransformation>(provider => new TotpStepUpClaimsTransformation(
            provider.GetServices<IUserContextMapper>(),
            provider.GetRequiredService<ITotpStepUpStore>(),
            provider.GetRequiredService<IClock>(),
            provider.GetRequiredService<IOptions<TotpStepUpOptions>>(),
            wrap ? provider.GetRequiredKeyedService<IClaimsTransformation>(InnerTransformationKey) : null));
    }

    private static ServiceDescriptor ToInnerDescriptor(ServiceDescriptor descriptor) => descriptor switch
    {
        { ImplementationInstance: { } instance } =>
            new ServiceDescriptor(typeof(IClaimsTransformation), InnerTransformationKey, instance),
        { ImplementationFactory: { } factory } =>
            new ServiceDescriptor(typeof(IClaimsTransformation), InnerTransformationKey, (provider, _) => factory(provider), descriptor.Lifetime),
        _ => new ServiceDescriptor(typeof(IClaimsTransformation), InnerTransformationKey, descriptor.ImplementationType!, descriptor.Lifetime),
    };

    private static readonly object InnerTransformationKey = new();

    private sealed class TotpStepUpRegistration;
}
