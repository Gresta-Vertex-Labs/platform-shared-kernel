using System.Globalization;

namespace SharedKernel.Localization;

/// <summary>
/// A culture-keyed lookup for translated user-facing messages, keyed on the same <c>code</c>
/// string every <c>SharedKernel.Primitives.Errors.Error</c> factory already requires on every
/// call site (<c>Error.NotFound("customer.not_found", "...")</c>, etc.).
/// </summary>
/// <remarks>
/// <para>
/// AN UNTRANSLATED ERROR MESSAGE FALLS BACK TO THE ORIGINAL THROW-SITE STRING, IT IS NEVER BLANK.
/// This contract itself never applies that fallback — <see cref="TryGetString"/> only ever returns
/// <see langword="false"/> with its <c>value</c> output set to <see langword="null"/> when no
/// translation is registered or found for the requested <c>(code, culture)</c> pair. Applying the
/// throw-site-message fallback when this method returns <see langword="false"/> is entirely the
/// caller's responsibility — in practice, <c>14.Presentation</c>'s <c>Error.ToProblemDetails()</c>
/// (P-484) — never this package's.
/// </para>
/// <para>
/// This method never throws for an unregistered/untranslated lookup. An implementation MAY still
/// throw <see cref="ArgumentException"/>/<see cref="ArgumentNullException"/> for a genuinely
/// invalid call (a null/empty/whitespace-only <c>code</c>, or a null <c>culture</c>) — that is a
/// programming-error guard, distinct from, and never a substitute for, the "translation not
/// found" outcome above, which always returns <see langword="false"/> rather than throwing.
/// </para>
/// </remarks>
public interface ILocalizationCatalog
{
    /// <summary>
    /// Attempts to resolve a translated message for <paramref name="code"/> in
    /// <paramref name="culture"/>.
    /// </summary>
    /// <param name="code">The message/error code to look up — the same string value every
    /// <c>Error</c> factory already requires as its <c>code</c> argument.</param>
    /// <param name="culture">The culture to resolve the translation for.</param>
    /// <param name="value">
    /// Set to the translated message when this method returns <see langword="true"/>; otherwise
    /// set to <see langword="null"/>. NEVER SET TO AN EMPTY OR WHITESPACE-ONLY STRING — a
    /// "translation" that is blank is indistinguishable from a bug in the calling code that
    /// dropped the throw-site fallback, so implementations must treat a blank stored value as
    /// equivalent to "not found" rather than ever surfacing it as a match.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if a non-blank translation was found for <paramref name="code"/> in
    /// <paramref name="culture"/> (or, for an implementation that performs culture fallback, one
    /// of its ancestor cultures); otherwise <see langword="false"/>.
    /// </returns>
    bool TryGetString(string code, CultureInfo culture, out string? value);
}
