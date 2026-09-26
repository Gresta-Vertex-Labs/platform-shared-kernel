using System.Globalization;
using SharedKernel.Localization;
using SharedKernel.Testing.Localization;

namespace SharedKernel.Testing.SelfTests.Localization;

/// <summary>
/// Proves <see cref="CultureScope"/> composes with <c>01.Core/SharedKernel.Localization</c>'s own
/// <see cref="InMemoryLocalizationCatalog"/> with zero code changes on either side (D-235's audit
/// finding, P-485/WO-078) — the "translation found" and "falls back to original message" cases a
/// consuming service's own test suite needs to prove that its problem responses are localized
/// (P-484) without any real resx resources.
/// </summary>
public sealed class CultureScopeLocalizationCatalogInteropTests
{
    [Fact]
    public void TryGetString_TranslationRegisteredForScopedCulture_ReturnsTranslation()
    {
        var catalog = new LocalizationCatalogBuilder()
            .Add("customer.not_found", CultureInfo.GetCultureInfo("tr-TR"), "Müşteri bulunamadı.")
            .Build();

        using (new CultureScope("tr-TR"))
        {
            var found = catalog.TryGetString("customer.not_found", CultureInfo.CurrentUICulture, out var value);

            Assert.True(found);
            Assert.Equal("Müşteri bulunamadı.", value);
        }
    }

    [Fact]
    public void TryGetString_NoTranslationRegistered_ReturnsFalse_CallerAppliesOriginalMessage()
    {
        var catalog = new LocalizationCatalogBuilder().Build();

        using (new CultureScope("tr-TR"))
        {
            var found = catalog.TryGetString("unregistered.code", CultureInfo.CurrentUICulture, out var value);

            // The catalog itself never applies the throw-site fallback — it only ever reports
            // "not found." Applying "customer not found" (the original Error.Message) is the
            // caller's responsibility, exactly as ILocalizationCatalog's own contract documents.
            Assert.False(found);
            Assert.Null(value);
        }
    }

    [Fact]
    public void TryGetString_MoreSpecificCultureFallsBackToParent_WithinScopedCulture()
    {
        var catalog = new LocalizationCatalogBuilder()
            .Add("order.invalid", CultureInfo.InvariantCulture, "Invalid order.")
            .Build();

        using (new CultureScope("tr-TR"))
        {
            var found = catalog.TryGetString("order.invalid", CultureInfo.CurrentUICulture, out var value);

            Assert.True(found);
            Assert.Equal("Invalid order.", value);
        }
    }

    [Fact]
    public void CultureScope_Restores_AfterCatalogComposition()
    {
        var original = CultureInfo.CurrentUICulture;
        var catalog = new LocalizationCatalogBuilder()
            .Add("x", CultureInfo.GetCultureInfo("de-DE"), "y")
            .Build();

        using (new CultureScope("de-DE"))
        {
            catalog.TryGetString("x", CultureInfo.CurrentUICulture, out _);
        }

        Assert.Equal(original, CultureInfo.CurrentUICulture);
    }
}
