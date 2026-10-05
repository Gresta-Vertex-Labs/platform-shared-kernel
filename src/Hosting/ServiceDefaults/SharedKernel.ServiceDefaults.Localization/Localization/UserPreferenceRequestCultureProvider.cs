using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Security.Abstractions;

namespace SharedKernel.ServiceDefaults.Localization;

/// <summary>
/// The <see cref="LocalizationResolutionStrategy.UserPreference"/> step: resolves a culture from
/// the current authenticated user's own stored preference claim.
/// </summary>
/// <remarks>
/// Reads <c>IUserContext.FindClaim</c> (<c>12.Security.Abstractions</c>) for
/// <see cref="LocalizationResolutionOptions.UserPreferenceClaimType"/>. Skipped cleanly — never
/// throws — when <see cref="LocalizationResolutionOptions.UserPreferenceClaimType"/> is
/// unconfigured, <c>IUserContext</c> is unresolvable, or the claim is absent/empty.
/// </remarks>
internal sealed class UserPreferenceRequestCultureProvider(string? claimType) : IRequestCultureProvider
{
    /// <inheritdoc/>
    public Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (string.IsNullOrEmpty(claimType))
        {
            return Task.FromResult<ProviderCultureResult?>(null);
        }

        var userContext = httpContext.RequestServices.GetService<IUserContext>();
        if (userContext?.FindClaim(claimType) is not { } culture
            || string.IsNullOrWhiteSpace(culture))
        {
            return Task.FromResult<ProviderCultureResult?>(null);
        }

        return Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(culture));
    }
}
