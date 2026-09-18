using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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
    private const string ErrorsExtensionName = "errors";

    private const string ErrorCodesExtensionName = "errorCodes";

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
    /// Adds the per-error <c>errors</c> and <c>errorCodes</c> extension members shared by every
    /// multi-error <c>ProblemDetails</c> path.
    /// </summary>
    /// <param name="problemDetails">The response body to add the two members to.</param>
    /// <param name="errors">The child errors to group and resolve. Never mutated.</param>
    /// <param name="context">
    /// The current <see cref="HttpContext"/>, forwarded to <see cref="ResolveDetail"/> for every
    /// error. May be <see langword="null"/>.
    /// </param>
    /// <remarks>
    /// <para>
    /// Both members are dictionaries with the same keys, grouped with
    /// <see cref="StringComparer.Ordinal"/> in the order the keys first appear. An error's key is
    /// its <see cref="ErrorArgumentNames.PropertyPath"/> argument (the field it refers to, such as
    /// <c>Accounts[0].Iban</c>) when that is a non-empty string, and its <see cref="Error.Code"/>
    /// otherwise, so a domain error that names no field is still reported under its code.
    /// </para>
    /// <para>
    /// <c>errors</c> maps each key to the messages for that key, each resolved independently by
    /// <see cref="ResolveDetail"/> from the error's own code and arguments, so one error may
    /// translate while a sibling falls back to its raw <see cref="Error.Message"/>.
    /// <c>errorCodes</c> maps each key to the <see cref="Error.Code"/> of the same errors, in the
    /// same order: <c>errorCodes[key][i]</c> is the code of the error whose message is
    /// <c>errors[key][i]</c>. Keying <c>errors</c> by field keeps the shape of ASP.NET Core's
    /// <see cref="ValidationProblemDetails.Errors"/>, which form-binding libraries and generated
    /// clients already read; <c>errorCodes</c> gives clients the stable codes to branch on.
    /// </para>
    /// <para>
    /// This is the single implementation shared by <see cref="ErrorProblemDetailsExtensions"/>
    /// (for <see cref="Error.Details"/>) and <see cref="ValidationProblemDetailsExtensions"/> (for
    /// <see cref="SharedKernel.Core.Exceptions.ValidationException.Errors"/>), so both paths
    /// produce identical members for the same input errors.
    /// </para>
    /// </remarks>
    public static void AddErrorsExtensions(ProblemDetails problemDetails, IReadOnlyList<Error> errors, HttpContext? context)
    {
        var groups = errors.GroupBy(GroupingKey, StringComparer.Ordinal).ToArray();

        problemDetails.Extensions[ErrorsExtensionName] = groups.ToDictionary(
            group => group.Key,
            group => group.Select(error => ResolveDetail(error, context)).ToArray(),
            StringComparer.Ordinal);

        problemDetails.Extensions[ErrorCodesExtensionName] = groups.ToDictionary(
            group => group.Key,
            group => group.Select(error => error.Code).ToArray(),
            StringComparer.Ordinal);
    }

    private static string GroupingKey(Error error)
        => error.MessageArguments.TryGetValue(ErrorArgumentNames.PropertyPath, out var path)
            && path is string { Length: > 0 } fieldPath
                ? fieldPath
                : error.Code;
}
