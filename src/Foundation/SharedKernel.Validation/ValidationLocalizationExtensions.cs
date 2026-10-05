using SharedKernel.Localization;

namespace SharedKernel.Validation;

/// <summary>Adds this package's bundled translations to a localization catalog.</summary>
public static class ValidationLocalizationExtensions
{
    private const string ResourcePrefix = "SharedKernel.Validation.Localization.";

    /// <summary>
    /// Adds the Turkish translation of every message in <see cref="ValidationMessages"/>. English
    /// needs nothing: it is each message's default text.
    /// </summary>
    /// <param name="builder">The catalog builder.</param>
    /// <returns>The same builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="builder"/> is null.</exception>
    /// <remarks>
    /// Add these first and your own translations after them: a later source wins, so any bundled
    /// message can be reworded.
    /// <code>
    /// services.AddLocalizationCatalog(catalog => catalog
    ///     .AddValidationTranslations()
    ///     .AddJsonDirectory(Path.Combine(AppContext.BaseDirectory, "Localization")));
    /// </code>
    /// </remarks>
    public static LocalizationCatalogBuilder AddValidationTranslations(this LocalizationCatalogBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddEmbeddedJson(typeof(ValidationLocalizationExtensions).Assembly, ResourcePrefix);
    }
}
