using System.Globalization;
using Microsoft.Extensions.Localization;

namespace SharedKernel.Localization;

/// <summary>
/// An <see cref="ILocalizationCatalog"/> adapter over a caller-supplied
/// <see cref="IStringLocalizerFactory"/> — lets a service with full <c>.resx</c>-file tooling
/// compose behind the same seam as <see cref="InMemoryLocalizationCatalog"/>, instead of this
/// package inventing a second, bespoke resource pipeline.
/// </summary>
/// <remarks>
/// <para>
/// AN UNTRANSLATED ERROR MESSAGE FALLS BACK TO THE ORIGINAL THROW-SITE STRING, IT IS NEVER BLANK —
/// see <see cref="ILocalizationCatalog"/>. This type only ever returns <see langword="false"/>/
/// <see langword="null"/> when the underlying <see cref="IStringLocalizer"/> reports
/// <see cref="LocalizedString.ResourceNotFound"/>; applying the throw-site fallback is never this
/// type's responsibility.
/// </para>
/// <para>
/// DOES NOT BLINDLY FORWARD <see cref="LocalizedString.Value"/>. When a resource key is not
/// found, <see cref="IStringLocalizer"/> returns a <see cref="LocalizedString"/> whose
/// <see cref="LocalizedString.Value"/> falls back to the requested key itself (e.g. the raw
/// <c>"user.not_found"</c> code) — forwarding that blindly would make <see cref="TryGetString"/>
/// return <see langword="true"/> with the error code as the "translation", inverting the
/// never-blank contract into something worse than a blank string. <see cref="TryGetString"/>
/// always checks <see cref="LocalizedString.ResourceNotFound"/> first and returns
/// <see langword="false"/>/<see langword="null"/> whenever it is <see langword="true"/>, regardless
/// of what <see cref="LocalizedString.Value"/> contains.
/// </para>
/// <para>
/// CULTURE SELECTION: <see cref="IStringLocalizer"/>'s indexer in this framework version carries
/// no per-call culture parameter — it always resolves against the ambient
/// <see cref="CultureInfo.CurrentUICulture"/>. <see cref="TryGetString"/> therefore temporarily
/// sets <see cref="CultureInfo.CurrentUICulture"/> to the requested <c>culture</c> for the
/// duration of the (fully synchronous) lookup and restores the original value in a
/// <see langword="finally"/> block. This is safe for sequential calls on one thread — no other
/// work runs on this thread during the swap, since the method never awaits — but relies entirely
/// on <see cref="CultureInfo.CurrentUICulture"/>'s own thread/async-local semantics for concurrent
/// callers running on other threads at the same time.
/// </para>
/// </remarks>
public sealed class StringLocalizerLocalizationCatalog : ILocalizationCatalog
{
    private readonly IStringLocalizer _localizer;

    /// <summary>
    /// Creates a catalog wrapping the <see cref="IStringLocalizer"/> that <paramref name="factory"/>
    /// produces for <paramref name="resourceType"/>.
    /// </summary>
    /// <param name="factory">The string localizer factory (e.g. a <c>.resx</c>-backed
    /// factory registered via ASP.NET Core's <c>services.AddLocalization()</c>).</param>
    /// <param name="resourceType">The resource-owning marker type — the same type passed to
    /// <see cref="IStringLocalizerFactory.Create(Type)"/> when resolving <c>.resx</c> files by
    /// naming convention.</param>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> or
    /// <paramref name="resourceType"/> is null.</exception>
    public StringLocalizerLocalizationCatalog(IStringLocalizerFactory factory, Type resourceType)
    {
        ArgumentNullException.ThrowIfNull(factory);
        ArgumentNullException.ThrowIfNull(resourceType);

        _localizer = factory.Create(resourceType);
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException"><paramref name="code"/> is null, empty, or
    /// whitespace-only.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="culture"/> is null.</exception>
    public bool TryGetString(string code, CultureInfo culture, out string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentNullException.ThrowIfNull(culture);

        CultureInfo originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = culture;

            LocalizedString localized = _localizer[code];
            if (localized.ResourceNotFound)
            {
                value = null;
                return false;
            }

            value = localized.Value;
            return true;
        }
        finally
        {
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }
}
