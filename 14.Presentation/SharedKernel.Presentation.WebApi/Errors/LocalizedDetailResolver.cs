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
    /// for <c>(error.Code, CultureInfo.CurrentUICulture)</c>, filled with
    /// <see cref="Error.MessageArguments"/>, when a registered <see cref="ILocalizationCatalog"/>
    /// has a usable one; otherwise <see cref="Error.Message"/> verbatim.
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

        return catalog is null ? error.Message : catalog.Localize(error, CultureInfo.CurrentUICulture);
    }

    /// <summary>
    /// Builds the <c>Extensions["errors"]</c> value shared by every multi-error
    /// <c>ProblemDetails</c> path: <paramref name="errors"/> grouped by
    /// <see cref="Error.Code"/> (<see cref="StringComparer.Ordinal"/>), each value an array of
    /// per-error localized/fallback messages resolved via <see cref="ResolveDetail"/>.
    /// </summary>
    /// <param name="errors">The child errors to group and resolve. Never mutated.</param>
    /// <param name="context">
    /// The current <see cref="HttpContext"/>, forwarded to <see cref="ResolveDetail"/> for every
    /// error. May be <see langword="null"/>.
    /// </param>
    /// <returns>
    /// A <see cref="Dictionary{TKey, TValue}"/> of <see cref="Error.Code"/> to the array of
    /// resolved messages for that code. One error may translate while a sibling falls back to its
    /// raw <see cref="Error.Message"/> in the same result — never an all-or-nothing decision. This
    /// is the single implementation shared by <see cref="ErrorProblemDetailsExtensions"/> (for
    /// <see cref="Error.Details"/>) and <see cref="ValidationProblemDetailsExtensions"/> (for
    /// <see cref="SharedKernel.Core.Exceptions.ValidationException.Errors"/>), so both paths
    /// produce byte-identical shapes for the same input errors.
    /// </returns>
    public static Dictionary<string, string[]> BuildErrorsExtension(IReadOnlyList<Error> errors, HttpContext? context)
        => errors
            .GroupBy(error => error.Code, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(error => ResolveDetail(error, context)).ToArray(),
                StringComparer.Ordinal);
}
