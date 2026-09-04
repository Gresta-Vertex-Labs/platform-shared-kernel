using System.Globalization;

namespace SharedKernel.Localization;

/// <summary>
/// A dictionary-backed, in-process default implementation of <see cref="ILocalizationCatalog"/> —
/// the zero-config choice for a service translating a handful of well-known message codes without
/// any <c>.resx</c>/<c>IStringLocalizer</c> tooling.
/// </summary>
/// <remarks>
/// <para>
/// AN UNTRANSLATED ERROR MESSAGE FALLS BACK TO THE ORIGINAL THROW-SITE STRING, IT IS NEVER BLANK —
/// see <see cref="ILocalizationCatalog"/>. This type only ever returns <see langword="false"/>/
/// <see langword="null"/> for an unregistered/untranslated <c>(code, culture)</c> pair; applying
/// the throw-site fallback is never this type's responsibility.
/// </para>
/// <para>
/// CULTURE FALLBACK: a lookup for a specific culture (e.g. <c>tr-TR</c>) that has no exact entry
/// falls back through each parent culture (e.g. <c>tr</c>) and finally
/// <see cref="CultureInfo.InvariantCulture"/>, mirroring the standard .NET resource-fallback
/// behavior used by <c>ResourceManager</c>/<c>IStringLocalizer</c>. Seed a translation under
/// <see cref="CultureInfo.InvariantCulture"/> to provide a single universal default reached by
/// every culture that has no more specific entry of its own — this is the deliberate design
/// choice for this type (the interface contract itself does not mandate fallback; a different
/// <see cref="ILocalizationCatalog"/> implementation is free to require an exact match only).
/// </para>
/// <para>
/// Lookup is case-sensitive (ordinal) on <c>code</c>, consistent with how <c>Error.Code</c>
/// string values are compared everywhere else on the platform. The culture key is
/// <see cref="CultureInfo.Name"/>, which .NET already normalizes to a canonical casing
/// (e.g. <c>"tr-TR"</c>) regardless of how the <see cref="CultureInfo"/> was constructed.
/// </para>
/// </remarks>
public sealed class InMemoryLocalizationCatalog : ILocalizationCatalog
{
    private readonly Dictionary<(string Code, string CultureName), string> _translations = [];

    /// <summary>
    /// Registers (or overwrites) the translation for <paramref name="code"/> in
    /// <paramref name="culture"/>.
    /// </summary>
    /// <param name="code">The message/error code being translated. Must not be null, empty, or
    /// whitespace-only.</param>
    /// <param name="culture">The culture the translation applies to.</param>
    /// <param name="value">The translated message. Must not be null, empty, or whitespace-only —
    /// this catalog never stores a blank translation, since a blank stored value would defeat the
    /// platform's never-blank fallback contract just as surely as returning one directly would.</param>
    /// <returns>The same catalog instance, so calls can be chained while seeding the catalog.</returns>
    /// <exception cref="ArgumentException"><paramref name="code"/> or <paramref name="value"/> is
    /// null, empty, or whitespace-only.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="culture"/> is null.</exception>
    public InMemoryLocalizationCatalog AddTranslation(string code, CultureInfo culture, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentNullException.ThrowIfNull(culture);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        _translations[(code, culture.Name)] = value;
        return this;
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException"><paramref name="code"/> is null, empty, or
    /// whitespace-only.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="culture"/> is null.</exception>
    public bool TryGetString(string code, CultureInfo culture, out string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentNullException.ThrowIfNull(culture);

        for (CultureInfo current = culture; ; current = current.Parent)
        {
            if (_translations.TryGetValue((code, current.Name), out string? found))
            {
                value = found;
                return true;
            }

            if (current.Equals(CultureInfo.InvariantCulture))
            {
                break;
            }
        }

        value = null;
        return false;
    }
}
