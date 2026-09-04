using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Localization;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Presentation.WebApi.Errors;

/// <summary>
/// Resolves the localized (or fallback) <c>Detail</c> string for one <see cref="Error"/>, per
/// <see cref="ErrorProblemDetailsExtensions"/>'s optional localization step (P-484/WO-078).
/// </summary>
/// <remarks>
/// <para>
/// Optional-dependency resolution only — never a hard requirement. Resolves
/// <see cref="ILocalizationCatalog"/> via <see cref="ServiceProviderServiceExtensions.GetService{T}"/>
/// (never <c>GetRequiredService</c>), so a service that has not registered
/// <c>SharedKernel.Localization</c> (or supplies no <see cref="HttpContext"/> at all) resolves
/// nothing and falls back to <see cref="Error.Message"/> verbatim — byte-identical to this
/// package's pre-P-484 output.
/// </para>
/// <para>
/// Culture resolution is explicitly not this package's concern:
/// <see cref="CultureInfo.CurrentUICulture"/> is read as an ambient value only, set (when a
/// consuming service opts in) by <c>13.ServiceDefaults</c>'s culture-resolution middleware
/// (P-483). This package never resolves or sets the ambient culture itself.
/// </para>
/// <para>Internal — not part of this package's public surface.</para>
/// </remarks>
internal static class LocalizedDetailResolver
{
    /// <summary>
    /// Resolves the <c>Detail</c> string for <paramref name="error"/>: the catalog translation
    /// for <c>(error.Code, CultureInfo.CurrentUICulture)</c> when a registered
    /// <see cref="ILocalizationCatalog"/> has one, otherwise <see cref="Error.Message"/> verbatim.
    /// </summary>
    /// <param name="error">The error whose <see cref="Error.Code"/>/<see cref="Error.Message"/> to resolve.</param>
    /// <param name="context">
    /// The current <see cref="HttpContext"/>, used to resolve an optional
    /// <see cref="ILocalizationCatalog"/> from <see cref="HttpContext.RequestServices"/>. May be
    /// <see langword="null"/> — the lookup is then skipped entirely and <see cref="Error.Message"/>
    /// is returned.
    /// </param>
    /// <returns>
    /// The translated string when found; otherwise <paramref name="error"/>'s
    /// <see cref="Error.Message"/> — never blank, never <see langword="null"/>.
    /// </returns>
    public static string ResolveDetail(Error error, HttpContext? context)
    {
        var catalog = context?.RequestServices?.GetService<ILocalizationCatalog>();

        if (catalog is not null && catalog.TryGetString(error.Code, CultureInfo.CurrentUICulture, out var translated))
        {
            return translated!;
        }

        return error.Message;
    }
}
