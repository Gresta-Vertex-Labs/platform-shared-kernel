using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace SharedKernel.Localization;

/// <summary>
/// An immutable catalog of translations held in memory. Build one with
/// <see cref="LocalizationCatalogBuilder"/>, or register one with
/// <see cref="LocalizationServiceCollectionExtensions.AddLocalizationCatalog"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Culture fallback.</b> A lookup for <c>tr-TR</c> that has no exact entry tries <c>tr</c>,
/// then <see cref="CultureInfo.InvariantCulture"/>, the same order .NET resource files use. So a
/// service usually translates into neutral cultures (<c>tr</c>, <c>de</c>) and every regional
/// variant finds them.
/// </para>
/// <para>
/// Every template was parsed and validated when the catalog was built, so a lookup does no
/// parsing. The catalog never changes after it is built and is safe to read from any number of
/// threads without locking.
/// </para>
/// </remarks>
public sealed class InMemoryLocalizationCatalog : ILocalizationCatalog
{
    private readonly FrozenDictionary<(string Code, string Culture), MessageTemplate> _templates;

    internal InMemoryLocalizationCatalog(
        FrozenDictionary<(string Code, string Culture), MessageTemplate> templates,
        IReadOnlyList<CultureInfo> cultures)
    {
        _templates = templates;
        Cultures = cultures;
    }

    /// <summary>Gets the number of translations, counting each code once per culture.</summary>
    public int Count => _templates.Count;

    /// <summary>
    /// Gets the cultures that have at least one translation, sorted by name. Useful as the
    /// supported UI cultures of ASP.NET Core's request localization.
    /// </summary>
    public IReadOnlyList<CultureInfo> Cultures { get; }

    /// <inheritdoc />
    public bool TryGetTemplate(string code, CultureInfo culture, [NotNullWhen(true)] out MessageTemplate? template)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentNullException.ThrowIfNull(culture);

        for (CultureInfo current = culture; ; current = current.Parent)
        {
            if (_templates.TryGetValue((code, current.Name), out template))
            {
                return true;
            }

            if (current.Name.Length == 0)
            {
                return false;
            }
        }
    }
}
