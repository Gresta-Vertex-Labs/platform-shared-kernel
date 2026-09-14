using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharedKernel.MultiTenancy.Catalog;
using SharedKernel.ServiceDefaults.Logging;

namespace SharedKernel.ServiceDefaults.Localization;

/// <summary>
/// Opt-in, precedence-ordered request-culture resolution, composed on top of ASP.NET Core's own
/// <c>RequestLocalizationMiddleware</c>.
/// </summary>
public static class LocalizationExtensions
{
    /// <summary>
    /// Configures <see cref="RequestLocalizationOptions.RequestCultureProviders"/> per
    /// <paramref name="configure"/>'s <see cref="LocalizationResolutionOptions.StrategyOrder"/>.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <param name="configure">Optional configuration delegate for <see cref="LocalizationResolutionOptions"/>.</param>
    /// <returns>The same <paramref name="builder"/> instance, for fluent chaining.</returns>
    /// <remarks>
    /// <para>
    /// <b>Wraps, never reimplements, ASP.NET Core's own <c>RequestLocalizationMiddleware</c>.</b>
    /// This method configures <see cref="RequestLocalizationOptions.RequestCultureProviders"/>
    /// with one <see cref="IRequestCultureProvider"/> per configured
    /// <see cref="LocalizationResolutionOptions.StrategyOrder"/> entry — the real BCL
    /// <see cref="AcceptLanguageHeaderRequestCultureProvider"/> for the
    /// <see cref="LocalizationResolutionStrategy.AcceptLanguageHeader"/> step. It does not itself
    /// register the middleware into the request pipeline — mirroring
    /// <c>SharedKernel.MultiTenancy.AddSharedKernelMultiTenancy</c>'s "register services here,
    /// wire the middleware separately" split — the consumer still calls the real
    /// <c>app.UseRequestLocalization()</c> explicitly in their own <c>Program.cs</c>, which then
    /// operates against the <see cref="RequestLocalizationOptions"/> configured here.
    /// </para>
    /// <para>
    /// Resolution order at request time: (1) <see cref="LocalizationResolutionStrategy.UserPreference"/> —
    /// reads <c>IUserContext.Claims</c> (<c>12.Security.Abstractions</c>) for
    /// <see cref="LocalizationResolutionOptions.UserPreferenceClaimType"/>, skipped if
    /// unconfigured or absent; (2) <see cref="LocalizationResolutionStrategy.TenantDefault"/> —
    /// resolves <see cref="ITenantCatalog"/> <b>optionally</b>, calls
    /// <c>GetByIdAsync(tenantId, ct)?.DefaultCulture</c>, skips cleanly (never throws) when no
    /// <see cref="ITenantCatalog"/> is registered; (3)
    /// <see cref="LocalizationResolutionStrategy.AcceptLanguageHeader"/> — delegates to the real
    /// BCL provider. Requires <c>TenantResolutionMiddleware</c>/<c>UseAuthentication()</c> to run
    /// <b>before</b> <c>UseRequestLocalization()</c> in the pipeline, so the
    /// <see cref="LocalizationResolutionStrategy.UserPreference"/>/
    /// <see cref="LocalizationResolutionStrategy.TenantDefault"/> steps have a populated
    /// <c>IUserContext</c>/ambient tenant id to read.
    /// </para>
    /// <para>
    /// <b>Startup warning:</b> when <see cref="LocalizationResolutionOptions.UserPreferenceClaimType"/>
    /// is left unconfigured <i>and</i> no <see cref="ITenantCatalog"/> is registered in DI at all,
    /// a one-time <see cref="LocalizationLog.LocalizationNoDynamicStrategyCanResolve"/> warning
    /// fires at startup (forced by <c>ValidateOnStart()</c>, never deferred to first request) —
    /// mirroring this platform's "misconfiguration surfaces at startup, not silently" convention.
    /// This check is deliberately independent of whether
    /// <see cref="LocalizationResolutionStrategy.AcceptLanguageHeader"/> is present in
    /// <see cref="LocalizationResolutionOptions.StrategyOrder"/>: it flags "did you forget to
    /// configure the per-user/per-tenant steps you presumably wanted," not "can this host resolve
    /// any culture at all" — the header fallback being present does not mean the two smarter,
    /// deliberately-configured steps are actually doing anything.
    /// </para>
    /// <para>
    /// <b>Cross-references:</b> this method resolves culture <i>precedence</i> only — it does not
    /// translate anything. <c>01.Core</c>'s <c>SharedKernel.Localization</c> (<c>ILocalizationCatalog</c>)
    /// and <c>14.Presentation</c>'s (queued) <c>Error.ToProblemDetails()</c> localization (P-484)
    /// are the pieces that actually consume the resolved <see cref="System.Globalization.CultureInfo"/>
    /// to look up a translated string.
    /// </para>
    /// <para>
    /// <b>First-ever intra-domain reference:</b> resolving <see cref="ITenantCatalog"/>'s type is
    /// why <c>SharedKernel.ServiceDefaults.csproj</c> now carries a <c>ProjectReference</c> to its
    /// sibling package <c>SharedKernel.MultiTenancy</c> — a deliberate design decision (WO-078/D-36),
    /// not an oversight.
    /// </para>
    /// </remarks>
    public static IHostApplicationBuilder AddSharedKernelLocalization(
        this IHostApplicationBuilder builder,
        Action<LocalizationResolutionOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new LocalizationResolutionOptions();
        configure?.Invoke(options);

        var services = builder.Services;

        services.Configure<RequestLocalizationOptions>(rlo =>
        {
            rlo.RequestCultureProviders = BuildProviders(options);
        });

        services
            .AddOptions<RequestLocalizationOptions>()
            .PostConfigure<ILoggerFactory>((_, loggerFactory) =>
            {
                var tenantCatalogRegistered = services.Any(d => d.ServiceType == typeof(ITenantCatalog));
                if (options.UserPreferenceClaimType is null && !tenantCatalogRegistered)
                {
                    var logger = loggerFactory.CreateLogger(
                        "SharedKernel.ServiceDefaults.Localization.RequestCultureResolution");
                    LocalizationLog.LocalizationNoDynamicStrategyCanResolve(logger);
                }
            })
            .ValidateOnStart();

        return builder;
    }

    private static IList<IRequestCultureProvider> BuildProviders(LocalizationResolutionOptions options)
    {
        var providers = new List<IRequestCultureProvider>(options.StrategyOrder.Count);

        foreach (var strategy in options.StrategyOrder)
        {
            IRequestCultureProvider provider = strategy switch
            {
                LocalizationResolutionStrategy.UserPreference =>
                    new UserPreferenceRequestCultureProvider(options.UserPreferenceClaimType),
                LocalizationResolutionStrategy.TenantDefault =>
                    new TenantDefaultRequestCultureProvider(),
                LocalizationResolutionStrategy.AcceptLanguageHeader =>
                    new AcceptLanguageHeaderRequestCultureProvider(),
                _ => throw new InvalidOperationException(
                    $"Unknown {nameof(LocalizationResolutionStrategy)} value '{strategy}'."),
            };

            providers.Add(provider);
        }

        return providers;
    }
}
