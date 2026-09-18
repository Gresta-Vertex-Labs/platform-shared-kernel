using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using SharedKernel.Primitives.Errors;

namespace SharedKernel.Localization;

/// <summary>
/// Translates errors and messages through an <see cref="ILocalizationCatalog"/>, falling back to
/// the original text whenever no usable translation exists.
/// </summary>
public static class LocalizationCatalogExtensions
{
    private static readonly IReadOnlyDictionary<string, object?> NoArguments =
        new Dictionary<string, object?>(0, StringComparer.Ordinal).AsReadOnly();

    /// <summary>
    /// Returns <paramref name="error"/>'s message in <paramref name="culture"/>: the translation of
    /// <c>error.Code</c> filled with <c>error.MessageArguments</c>, or <c>error.Message</c> when
    /// there is no translation or it cannot be filled.
    /// </summary>
    /// <param name="catalog">The catalog to look the translation up in.</param>
    /// <param name="error">The error to translate.</param>
    /// <param name="culture">The culture to translate into, usually <see cref="CultureInfo.CurrentUICulture"/>.</param>
    /// <returns>The translated message, or <c>error.Message</c>. Never throws for a missing or broken translation.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// A translation that uses a placeholder the error has no value for (for example a
    /// translation of <c>"validation.required"</c> that mentions <c>{field}</c>, used with an
    /// error created without arguments) is treated as missing, so the caller sees the original
    /// message rather than a raw <c>{field}</c>.
    /// </remarks>
    public static string Localize(this ILocalizationCatalog catalog, Error error, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(culture);

        if (string.IsNullOrWhiteSpace(error.Code))
        {
            return error.Message;
        }

        return catalog.TryFormat(error.Code, culture, error.MessageArguments, out string? message)
            ? message
            : error.Message;
    }

    /// <summary>
    /// Attempts to find the translation of <paramref name="code"/> in <paramref name="culture"/>
    /// and fill its placeholders with <paramref name="arguments"/>.
    /// </summary>
    /// <param name="catalog">The catalog to look the translation up in.</param>
    /// <param name="code">The message code.</param>
    /// <param name="culture">The culture to translate into; also used to format the values.</param>
    /// <param name="arguments">The placeholder values, keyed by placeholder name.</param>
    /// <param name="message">The formatted translation when this method returns <see langword="true"/>.</param>
    /// <returns>
    /// <see langword="false"/> when there is no translation, a placeholder has no value, or a
    /// format does not suit its value; otherwise <see langword="true"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="catalog"/>, <paramref name="culture"/> or <paramref name="arguments"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="code"/> is null, empty, or whitespace-only.</exception>
    public static bool TryFormat(
        this ILocalizationCatalog catalog,
        string code,
        CultureInfo culture,
        IReadOnlyDictionary<string, object?> arguments,
        [NotNullWhen(true)] out string? message)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(arguments);

        if (catalog.TryGetTemplate(code, culture, out MessageTemplate? template))
        {
            return template.TryFormat(culture, arguments, out message);
        }

        message = null;
        return false;
    }

    /// <summary>
    /// Attempts to find the translation of a message that has no placeholders.
    /// </summary>
    /// <param name="catalog">The catalog to look the translation up in.</param>
    /// <param name="code">The message code.</param>
    /// <param name="culture">The culture to translate into.</param>
    /// <param name="message">The translation when this method returns <see langword="true"/>.</param>
    /// <returns>
    /// <see langword="false"/> when there is no translation, or the translation has placeholders
    /// and therefore needs values; otherwise <see langword="true"/>.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="catalog"/> or <paramref name="culture"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="code"/> is null, empty, or whitespace-only.</exception>
    public static bool TryGetString(
        this ILocalizationCatalog catalog,
        string code,
        CultureInfo culture,
        [NotNullWhen(true)] out string? message)
        => catalog.TryFormat(code, culture, NoArguments, out message);
}
