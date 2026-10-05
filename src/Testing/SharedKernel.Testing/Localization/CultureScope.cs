using System.Globalization;

namespace SharedKernel.Testing.Localization;

/// <summary>
/// An ambient culture-context test helper: scopes <see cref="CultureInfo.CurrentCulture"/>/
/// <see cref="CultureInfo.CurrentUICulture"/> to a specific culture for the duration of a
/// <see langword="using"/> block, restoring the original values on <see cref="Dispose"/>.
/// </summary>
/// <remarks>
/// <para>
/// Pure BCL <see cref="System.Globalization"/> scoping — takes no dependency on
/// <c>SharedKernel.Localization</c> or any other <c>SharedKernel.*</c> package. A test asserting a
/// localized <c>ProblemDetails</c> response composes this helper with
/// <c>SharedKernel.Localization</c>'s own <c>InMemoryLocalizationCatalog</c> directly, the same way
/// any other test composes two already-shipped types — this package does not duplicate that
/// catalog.
/// </para>
/// <para>
/// Restoration happens even when the scope's <see langword="using"/> block exits via a thrown
/// exception — <see cref="Dispose"/> always restores both original values, unconditionally.
/// </para>
/// </remarks>
public sealed class CultureScope : IDisposable
{
    private readonly CultureInfo _originalCulture;
    private readonly CultureInfo _originalUiCulture;
    private bool _disposed;

    /// <summary>Scopes the ambient culture to <paramref name="culture"/> for both <c>Culture</c> and <c>UICulture</c>.</summary>
    /// <param name="culture">The culture to scope to.</param>
    public CultureScope(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        _originalCulture = CultureInfo.CurrentCulture;
        _originalUiCulture = CultureInfo.CurrentUICulture;

        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    /// <summary>Scopes the ambient culture to the culture named <paramref name="cultureName"/>.</summary>
    /// <param name="cultureName">A culture name (e.g. <c>"tr-TR"</c>) resolved via <see cref="CultureInfo.GetCultureInfo(string)"/>.</param>
    public CultureScope(string cultureName)
        : this(CultureInfo.GetCultureInfo(cultureName ?? throw new ArgumentNullException(nameof(cultureName))))
    {
    }

    /// <summary>Restores the ambient culture to what it was before this scope was created.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        CultureInfo.CurrentCulture = _originalCulture;
        CultureInfo.CurrentUICulture = _originalUiCulture;
        _disposed = true;
    }
}
