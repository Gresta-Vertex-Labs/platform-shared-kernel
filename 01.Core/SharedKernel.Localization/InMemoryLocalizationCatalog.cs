using System.Globalization;
using System.Threading;

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
/// <para>
/// <b>SEED-ONCE-THEN-IMMUTABLE LIFECYCLE:</b> the backing dictionary is a plain, unsynchronized
/// <see cref="Dictionary{TKey,TValue}"/> — cheap on the read-hot <see cref="TryGetString"/> path,
/// but only safe once no further mutation can race a concurrent reader. Call <see cref="Seal"/>
/// once every <see cref="AddTranslation"/> call has been made; every subsequent
/// <see cref="AddTranslation"/> call then throws <see cref="InvalidOperationException"/> instead
/// of silently racing readers, and every <see cref="TryGetString"/> call after that point is
/// safe to call concurrently from any number of threads with zero lock overhead, because the
/// dictionary is never touched again. <see cref="AddInMemoryLocalizationCatalog"/> — the one
/// shipped construction path for this type — already calls <see cref="Seal"/> automatically
/// immediately after its seeding callback returns, so a consumer using that DI extension needs to
/// take no action at all to get this guarantee. A consumer who constructs this type directly and
/// genuinely wants a runtime-mutable catalog simply never calls <see cref="Seal"/> — but is then,
/// by design, responsible for their own concurrency discipline around <see cref="AddTranslation"/>;
/// the sanctioned answer for a fully dynamic, runtime-mutable catalog is to implement
/// <see cref="ILocalizationCatalog"/> directly rather than lean on this type's deliberately
/// seed-once shape. This lifecycle decision does not affect
/// <see cref="StringLocalizerLocalizationCatalog"/> — that type has no mutator of its own to
/// protect; it is a read-only wrapper over <c>IStringLocalizerFactory</c> from construction.
/// </para>
/// </remarks>
public sealed class InMemoryLocalizationCatalog : ILocalizationCatalog
{
    private readonly Dictionary<(string Code, string CultureName), string> _translations = [];

    // Interlocked-backed rather than merely `volatile` so Seal() reads back a guaranteed
    // consistent post-write value even if called concurrently — Seal() itself is idempotent by
    // design, so a torn/racy write here would only ever produce "still sealed", never "unsealed".
    private int _sealed;

    /// <summary>
    /// Whether <see cref="Seal"/> has been called on this catalog. Once <see langword="true"/>,
    /// every <see cref="AddTranslation"/> call throws <see cref="InvalidOperationException"/> and
    /// every <see cref="TryGetString"/> call is safe to call concurrently from any number of
    /// threads.
    /// </summary>
    public bool IsSealed => Volatile.Read(ref _sealed) != 0;

    /// <summary>
    /// Freezes this catalog: every subsequent <see cref="AddTranslation"/> call throws
    /// <see cref="InvalidOperationException"/>, and every subsequent <see cref="TryGetString"/>
    /// call becomes safe to call concurrently from any number of threads with zero lock overhead,
    /// because the backing dictionary is never mutated again.
    /// </summary>
    /// <remarks>
    /// Idempotent — calling <see cref="Seal"/> more than once (including concurrently from
    /// multiple threads) is a harmless no-op after the first call and never throws.
    /// <see cref="AddInMemoryLocalizationCatalog"/> already calls this automatically once its
    /// seeding callback returns, so most consumers never need to call it directly.
    /// </remarks>
    public void Seal() => Interlocked.Exchange(ref _sealed, 1);

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
    /// <exception cref="InvalidOperationException"><see cref="Seal"/> has already been called on
    /// this catalog — see the seed-once-then-immutable lifecycle documented on this type.</exception>
    public InMemoryLocalizationCatalog AddTranslation(string code, CultureInfo culture, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentNullException.ThrowIfNull(culture);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (IsSealed)
        {
            throw new InvalidOperationException(
                $"This {nameof(InMemoryLocalizationCatalog)} has been sealed and can no longer be "
                    + $"mutated. {nameof(Seal)}() is called automatically by "
                    + $"{nameof(LocalizationServiceCollectionExtensions.AddInMemoryLocalizationCatalog)}"
                    + " once its seeding callback returns; a fully dynamic, runtime-mutable catalog "
                    + $"should implement {nameof(ILocalizationCatalog)} directly instead.");
        }

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
