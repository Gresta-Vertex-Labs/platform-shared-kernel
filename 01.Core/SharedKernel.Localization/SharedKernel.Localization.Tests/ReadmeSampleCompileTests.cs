using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace SharedKernel.Localization.Tests;

/// <summary>
/// Compiles (and exercises) the exact code shown in <c>SharedKernel.Localization/README.md</c> —
/// not a paraphrase — so a doc-sample error is caught here rather than by a consumer copy-pasting
/// it. See the project's own testing conventions: this has caught real signature mismatches in
/// prior 01.Core packages' README samples.
/// </summary>
public sealed class ReadmeSampleCompileTests
{
    [Fact]
    public void FallbackContractSample_Compiles_AndFallsBackWhenTranslationMissing()
    {
        ILocalizationCatalog catalog = new InMemoryLocalizationCatalog()
            .AddTranslation("user.not_found", CultureInfo.GetCultureInfo("tr"), "Kullanıcı bulunamadı.");

        string throwSiteMessage = "User not found.";
        string resolvedMessage = catalog.TryGetString("user.not_found", CultureInfo.GetCultureInfo("tr-TR"), out string? translated)
            ? translated!
            : throwSiteMessage;

        Assert.Equal("Kullanıcı bulunamadı.", resolvedMessage);
    }

    [Fact]
    public void InMemoryLocalizationCatalogSample_Compiles_AndBehavesAsDocumented()
    {
        var catalog = new InMemoryLocalizationCatalog()
            .AddTranslation("order.not_found", CultureInfo.GetCultureInfo("en-US"), "Order not found.")
            .AddTranslation("order.not_found", CultureInfo.GetCultureInfo("tr"), "Sipariş bulunamadı.");

        catalog.TryGetString("order.not_found", CultureInfo.GetCultureInfo("en-US"), out string? en);
        catalog.TryGetString("order.not_found", CultureInfo.GetCultureInfo("tr-TR"), out string? tr);
        catalog.TryGetString("order.not_found", CultureInfo.GetCultureInfo("de-DE"), out string? de);

        Assert.Equal("Order not found.", en);
        Assert.Equal("Sipariş bulunamadı.", tr);
        Assert.Null(de);
    }

    [Fact]
    public void CultureFallbackSample_Compiles_AndBehavesAsDocumented()
    {
        var catalog = new InMemoryLocalizationCatalog()
            .AddTranslation("generic.error", CultureInfo.InvariantCulture, "Something went wrong.")
            .AddTranslation("generic.error", CultureInfo.GetCultureInfo("tr"), "Bir şeyler yanlış gitti.");

        catalog.TryGetString("generic.error", CultureInfo.GetCultureInfo("tr-TR"), out string? v1);
        catalog.TryGetString("generic.error", CultureInfo.GetCultureInfo("fr-FR"), out string? v2);

        Assert.Equal("Bir şeyler yanlış gitti.", v1);
        Assert.Equal("Something went wrong.", v2);
    }

    [Fact]
    public void StringLocalizerCompositionRootSample_Compiles_AndResolves()
    {
        var services = new ServiceCollection();
        services.AddLogging(); // already present in any real ASP.NET Core/Generic Host composition root

        services.AddLocalization(options => options.ResourcesPath = "Resources");
        services.AddStringLocalizerCatalog<ErrorMessages>();

        using ServiceProvider provider = services.BuildServiceProvider();
        ILocalizationCatalog catalog = provider.GetRequiredService<ILocalizationCatalog>();

        var usage = new ExampleUsage(catalog);
        string resolved = usage.Resolve("some.code", CultureInfo.InvariantCulture, "Fallback message.");

        // No .resx file backs "some.code" in this test, so the sample's fallback branch is what
        // proves the composed catalog is wired correctly end to end.
        Assert.Equal("Fallback message.", resolved);
    }

    /// <summary>Marker type — matches <c>ErrorMessages.resx</c> / <c>ErrorMessages.tr.resx</c>, etc.</summary>
    private sealed class ErrorMessages;

    private sealed class ExampleUsage(ILocalizationCatalog catalog)
    {
        public string Resolve(string code, CultureInfo culture, string throwSiteMessage) =>
            catalog.TryGetString(code, culture, out string? translated) ? translated! : throwSiteMessage;
    }
}
