namespace SharedKernel.ServiceDefaults.Localization;

/// <summary>
/// One step in <see cref="LocalizationResolutionOptions.StrategyOrder"/>'s precedence-ordered
/// culture resolution.
/// </summary>
public enum LocalizationResolutionStrategy
{
    /// <summary>
    /// Resolve the culture from the current authenticated user's own stored preference — a claim
    /// on <c>IUserContext.Claims</c> named by <see cref="LocalizationResolutionOptions.UserPreferenceClaimType"/>.
    /// </summary>
    UserPreference,

    /// <summary>
    /// Resolve the culture from the current tenant's <c>TenantDescriptor.DefaultCulture</c>, via
    /// an optionally-registered <c>ITenantCatalog</c>.
    /// </summary>
    TenantDefault,

    /// <summary>
    /// Resolve the culture from the request's standard <c>Accept-Language</c> HTTP header, via
    /// ASP.NET Core's own <c>AcceptLanguageHeaderRequestCultureProvider</c>.
    /// </summary>
    AcceptLanguageHeader,
}
