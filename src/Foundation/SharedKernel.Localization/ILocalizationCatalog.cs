using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace SharedKernel.Localization;

/// <summary>
/// Looks up the translated message template for a message code in a culture. The code is the
/// same string an <c>Error</c> carries in <c>Error.Code</c>, such as <c>"order.not_found"</c>.
/// </summary>
/// <remarks>
/// <para>
/// A lookup that finds nothing returns <see langword="false"/>; it never throws and never returns
/// a blank template. Deciding what to show instead is the caller's job, and the answer is always
/// the original message: <see cref="LocalizationCatalogExtensions.Localize"/> falls back to
/// <c>Error.Message</c>, and <see cref="LocalizedMessage"/> falls back to its default text.
/// </para>
/// <para>
/// Implementations may fall back from a specific culture to its parents (<c>tr-TR</c> to
/// <c>tr</c>) the way .NET resource files do. Both implementations in this package do.
/// </para>
/// <para>
/// Most code does not call this interface directly. Use
/// <see cref="LocalizationCatalogExtensions.Localize"/> to translate an <c>Error</c>, or a
/// <see cref="LocalizedMessage"/> definition's <c>Format</c> method for any other message.
/// </para>
/// </remarks>
public interface ILocalizationCatalog
{
    /// <summary>
    /// Attempts to find the message template for <paramref name="code"/> in
    /// <paramref name="culture"/>.
    /// </summary>
    /// <param name="code">The message code, for example <c>"order.not_found"</c>. Compared ordinally.</param>
    /// <param name="culture">The culture to find a translation for.</param>
    /// <param name="template">The template when this method returns <see langword="true"/>.</param>
    /// <returns>
    /// <see langword="true"/> when a translation exists for <paramref name="code"/> in
    /// <paramref name="culture"/>, or in a parent culture for an implementation that falls back;
    /// otherwise <see langword="false"/>.
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="code"/> is null, empty, or whitespace-only.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="culture"/> is null.</exception>
    bool TryGetTemplate(string code, CultureInfo culture, [NotNullWhen(true)] out MessageTemplate? template);
}
