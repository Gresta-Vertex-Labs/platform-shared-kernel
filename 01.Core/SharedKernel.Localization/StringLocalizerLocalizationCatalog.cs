using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.Extensions.Localization;

namespace SharedKernel.Localization;

/// <summary>
/// An <see cref="ILocalizationCatalog"/> over <c>.resx</c> resource files, read through an
/// <see cref="IStringLocalizerFactory"/>. For a service that already keeps its translations in
/// resource files. Register it with
/// <see cref="LocalizationServiceCollectionExtensions.AddStringLocalizerCatalog{TResource}"/>.
/// </summary>
/// <remarks>
/// <para>
/// Resource values use the same named-placeholder syntax as every other template
/// (<c>{orderId}</c>, see <see cref="MessageTemplate"/>), not <c>string.Format</c>'s <c>{0}</c>.
/// </para>
/// <para>
/// A missing resource, an empty value, and a value that is not a valid template all count as "no
/// translation", so the caller falls back to the original message. Resource files are not
/// validated in advance the way <see cref="LocalizationCatalogBuilder"/> validates JSON; test each
/// resource file's templates if that matters to you.
/// </para>
/// <para>
/// The resource manager reads the culture from <see cref="CultureInfo.CurrentUICulture"/> rather
/// than taking it as an argument, so each lookup sets it to the requested culture and restores it
/// before returning. The value is local to the current thread and async flow, so concurrent
/// requests do not affect each other. Parent-culture fallback (<c>tr-TR</c> to <c>tr</c> to the
/// neutral resource file) is done by the resource manager.
/// </para>
/// </remarks>
public sealed class StringLocalizerLocalizationCatalog : ILocalizationCatalog
{
    private readonly IStringLocalizer _localizer;

    /// <summary>
    /// Creates a catalog over the resources that <paramref name="factory"/> finds for
    /// <paramref name="resourceType"/>.
    /// </summary>
    /// <param name="factory">The factory, usually registered by ASP.NET Core's <c>services.AddLocalization()</c>.</param>
    /// <param name="resourceType">
    /// The marker type the resource files are named after: <c>ErrorMessages</c> finds
    /// <c>ErrorMessages.resx</c>, <c>ErrorMessages.tr.resx</c>, and so on.
    /// </param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    public StringLocalizerLocalizationCatalog(IStringLocalizerFactory factory, Type resourceType)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(resourceType);

        _localizer = factory.Create(resourceType);
    }

    /// <inheritdoc />
    public bool TryGetTemplate(string code, CultureInfo culture, [NotNullWhen(true)] out MessageTemplate? template)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentNullException.ThrowIfNull(culture);

        CultureInfo original = CultureInfo.CurrentUICulture;
        LocalizedString localized;
        try
        {
            CultureInfo.CurrentUICulture = culture;
            localized = _localizer[code];
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }

        // A missing key comes back with ResourceNotFound set and the key itself as the value;
        // returning that would show the raw code to a user as if it were a translation.
        if (localized.ResourceNotFound)
        {
            template = null;
            return false;
        }

        return MessageTemplate.TryParse(localized.Value, out template);
    }
}
