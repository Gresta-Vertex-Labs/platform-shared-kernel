using System.Collections.Concurrent;
using System.Globalization;
using Xunit;

namespace SharedKernel.Localization.Tests;

public sealed class InMemoryLocalizationCatalogTests
{
    private static readonly CultureInfo TurkishTurkey = CultureInfo.GetCultureInfo("tr-TR");
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr");

    private static string? Lookup(ILocalizationCatalog catalog, string code, CultureInfo culture)
        => catalog.TryGetTemplate(code, culture, out MessageTemplate? template) ? template.Text : null;

    [Fact]
    public void TryGetTemplate_ExactCulture_Wins_OverParentAndInvariant()
    {
        InMemoryLocalizationCatalog catalog = new LocalizationCatalogBuilder()
            .Add("greeting", CultureInfo.InvariantCulture, "Hello")
            .Add("greeting", Turkish, "Merhaba")
            .Add("greeting", TurkishTurkey, "Merhaba (Türkiye)")
            .Build();

        Assert.Equal("Merhaba (Türkiye)", Lookup(catalog, "greeting", TurkishTurkey));
    }

    [Fact]
    public void TryGetTemplate_FallsBackToParent_ThenInvariant()
    {
        InMemoryLocalizationCatalog catalog = new LocalizationCatalogBuilder()
            .Add("a", Turkish, "tr")
            .Add("b", CultureInfo.InvariantCulture, "invariant")
            .Build();

        Assert.Equal("tr", Lookup(catalog, "a", TurkishTurkey));
        Assert.Equal("invariant", Lookup(catalog, "b", TurkishTurkey));
        Assert.Equal("invariant", Lookup(catalog, "b", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void TryGetTemplate_Missing_ReturnsFalseAndNull()
    {
        InMemoryLocalizationCatalog catalog = new LocalizationCatalogBuilder().Add("a", Turkish, "tr").Build();

        Assert.False(catalog.TryGetTemplate("a", CultureInfo.GetCultureInfo("de-DE"), out MessageTemplate? template));
        Assert.Null(template);
        Assert.False(catalog.TryGetTemplate("unknown", TurkishTurkey, out _));
    }

    [Fact]
    public void TryGetTemplate_CodeIsCaseSensitive()
    {
        InMemoryLocalizationCatalog catalog = new LocalizationCatalogBuilder().Add("user.not_found", Turkish, "x").Build();

        Assert.Null(Lookup(catalog, "USER.NOT_FOUND", Turkish));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void TryGetTemplate_BlankCode_Throws(string? code)
    {
        InMemoryLocalizationCatalog catalog = new LocalizationCatalogBuilder().Build();

        Assert.ThrowsAny<ArgumentException>(() => catalog.TryGetTemplate(code!, Turkish, out _));
    }

    [Fact]
    public void TryGetTemplate_NullCulture_Throws()
    {
        InMemoryLocalizationCatalog catalog = new LocalizationCatalogBuilder().Build();

        Assert.Throws<ArgumentNullException>(() => catalog.TryGetTemplate("a", null!, out _));
    }

    [Fact]
    public void CountAndCultures_DescribeTheContent()
    {
        InMemoryLocalizationCatalog catalog = new LocalizationCatalogBuilder()
            .Add("a", Turkish, "1")
            .Add("b", Turkish, "2")
            .Add("a", CultureInfo.GetCultureInfo("de"), "3")
            .Build();

        Assert.Equal(3, catalog.Count);
        Assert.Equal(["de", "tr"], catalog.Cultures.Select(c => c.Name));
    }

    [Fact]
    public void TryGetTemplate_ConcurrentReaders_AlwaysSeeConsistentResults()
    {
        InMemoryLocalizationCatalog catalog = new LocalizationCatalogBuilder()
            .Add("greeting", Turkish, "Merhaba")
            .Add("greeting", CultureInfo.InvariantCulture, "Hello")
            .Build();
        var failures = new ConcurrentBag<string>();

        Parallel.For(0, 20_000, i =>
        {
            CultureInfo culture = i % 2 == 0 ? TurkishTurkey : CultureInfo.GetCultureInfo("de-DE");
            string expected = i % 2 == 0 ? "Merhaba" : "Hello";
            if (Lookup(catalog, "greeting", culture) != expected)
            {
                failures.Add(culture.Name);
            }
        });

        Assert.Empty(failures);
    }
}
