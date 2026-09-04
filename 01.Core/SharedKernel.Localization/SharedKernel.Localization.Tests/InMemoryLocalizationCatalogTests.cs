using System.Globalization;
using Xunit;

namespace SharedKernel.Localization.Tests;

public sealed class InMemoryLocalizationCatalogTests
{
    private static readonly CultureInfo TurkishTurkey = CultureInfo.GetCultureInfo("tr-TR");
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr");
    private static readonly CultureInfo EnglishUs = CultureInfo.GetCultureInfo("en-US");

    [Fact]
    public void TryGetString_RegisteredCodeAndCulture_ReturnsTrueAndTranslation()
    {
        var catalog = new InMemoryLocalizationCatalog()
            .AddTranslation("user.not_found", EnglishUs, "User not found.");

        bool found = catalog.TryGetString("user.not_found", EnglishUs, out string? value);

        Assert.True(found);
        Assert.Equal("User not found.", value);
    }

    [Fact]
    public void TryGetString_UnregisteredCode_ReturnsFalseAndNull_NeverThrows()
    {
        var catalog = new InMemoryLocalizationCatalog();

        bool found = catalog.TryGetString("nothing.registered", EnglishUs, out string? value);

        Assert.False(found);
        Assert.Null(value);
    }

    [Fact]
    public void TryGetString_UnregisteredCode_NeverReturnsEmptyString()
    {
        var catalog = new InMemoryLocalizationCatalog();

        catalog.TryGetString("nothing.registered", EnglishUs, out string? value);

        Assert.NotEqual(string.Empty, value);
    }

    [Fact]
    public void AddTranslation_ReturnsSameInstance_ForChaining()
    {
        var catalog = new InMemoryLocalizationCatalog();

        InMemoryLocalizationCatalog result = catalog.AddTranslation("code.one", EnglishUs, "One");

        Assert.Same(catalog, result);
    }

    [Fact]
    public void AddTranslation_ChainedCalls_AllResolve()
    {
        var catalog = new InMemoryLocalizationCatalog()
            .AddTranslation("code.one", EnglishUs, "One")
            .AddTranslation("code.two", EnglishUs, "Two")
            .AddTranslation("code.one", Turkish, "Bir");

        Assert.True(catalog.TryGetString("code.one", EnglishUs, out string? one));
        Assert.Equal("One", one);
        Assert.True(catalog.TryGetString("code.two", EnglishUs, out string? two));
        Assert.Equal("Two", two);
        Assert.True(catalog.TryGetString("code.one", Turkish, out string? bir));
        Assert.Equal("Bir", bir);
    }

    [Fact]
    public void AddTranslation_SameCodeAndCultureAgain_OverwritesPreviousValue()
    {
        var catalog = new InMemoryLocalizationCatalog()
            .AddTranslation("code.one", EnglishUs, "First")
            .AddTranslation("code.one", EnglishUs, "Second");

        catalog.TryGetString("code.one", EnglishUs, out string? value);

        Assert.Equal("Second", value);
    }

    // ── Culture fallback — the sharpest edge the design left unspecified. Decision: a lookup for a
    // specific culture (tr-TR) falls back through each parent culture (tr) and finally
    // CultureInfo.InvariantCulture, mirroring standard .NET ResourceManager/IStringLocalizer
    // fallback semantics. Documented in this type's own XML docs.

    [Fact]
    public void TryGetString_SpecificCultureNotRegistered_FallsBackToParentCulture()
    {
        var catalog = new InMemoryLocalizationCatalog()
            .AddTranslation("greeting", Turkish, "Merhaba");

        bool found = catalog.TryGetString("greeting", TurkishTurkey, out string? value);

        Assert.True(found);
        Assert.Equal("Merhaba", value);
    }

    [Fact]
    public void TryGetString_NeitherSpecificNorParentRegistered_FallsBackToInvariantCulture()
    {
        var catalog = new InMemoryLocalizationCatalog()
            .AddTranslation("greeting", CultureInfo.InvariantCulture, "Hello");

        bool found = catalog.TryGetString("greeting", TurkishTurkey, out string? value);

        Assert.True(found);
        Assert.Equal("Hello", value);
    }

    [Fact]
    public void TryGetString_MoreSpecificCultureRegistered_PreferredOverParentOrInvariant()
    {
        var catalog = new InMemoryLocalizationCatalog()
            .AddTranslation("greeting", CultureInfo.InvariantCulture, "Hello")
            .AddTranslation("greeting", Turkish, "Merhaba")
            .AddTranslation("greeting", TurkishTurkey, "Merhaba (Turkiye)");

        bool found = catalog.TryGetString("greeting", TurkishTurkey, out string? value);

        Assert.True(found);
        Assert.Equal("Merhaba (Turkiye)", value);
    }

    [Fact]
    public void TryGetString_NoFallbackRegisteredAtAll_ReturnsFalse()
    {
        var catalog = new InMemoryLocalizationCatalog();

        bool found = catalog.TryGetString("greeting", TurkishTurkey, out string? value);

        Assert.False(found);
        Assert.Null(value);
    }

    [Fact]
    public void TryGetString_InvariantCultureRequestedDirectly_DoesNotLoopForever()
    {
        var catalog = new InMemoryLocalizationCatalog()
            .AddTranslation("greeting", CultureInfo.InvariantCulture, "Hello");

        bool found = catalog.TryGetString("greeting", CultureInfo.InvariantCulture, out string? value);

        Assert.True(found);
        Assert.Equal("Hello", value);
    }

    [Fact]
    public void TryGetString_CodeLookupIsCaseSensitive()
    {
        var catalog = new InMemoryLocalizationCatalog()
            .AddTranslation("user.not_found", EnglishUs, "User not found.");

        bool found = catalog.TryGetString("USER.NOT_FOUND", EnglishUs, out string? value);

        Assert.False(found);
        Assert.Null(value);
    }

    [Fact]
    public void TryGetString_NullCode_ThrowsArgumentNullException()
    {
        var catalog = new InMemoryLocalizationCatalog();

        Assert.Throws<ArgumentNullException>(() => catalog.TryGetString(null!, EnglishUs, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TryGetString_EmptyOrWhitespaceCode_ThrowsArgumentException(string code)
    {
        var catalog = new InMemoryLocalizationCatalog();

        Assert.Throws<ArgumentException>(() => catalog.TryGetString(code, EnglishUs, out _));
    }

    [Fact]
    public void TryGetString_NullCulture_ThrowsArgumentNullException()
    {
        var catalog = new InMemoryLocalizationCatalog();

        Assert.Throws<ArgumentNullException>(() => catalog.TryGetString("code", null!, out _));
    }

    [Fact]
    public void AddTranslation_NullValue_ThrowsArgumentNullException()
    {
        var catalog = new InMemoryLocalizationCatalog();

        Assert.Throws<ArgumentNullException>(() => catalog.AddTranslation("code", EnglishUs, null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AddTranslation_EmptyOrWhitespaceValue_ThrowsArgumentException(string value)
    {
        var catalog = new InMemoryLocalizationCatalog();

        Assert.Throws<ArgumentException>(() => catalog.AddTranslation("code", EnglishUs, value));
    }

    [Fact]
    public void AddTranslation_NullCulture_ThrowsArgumentNullException()
    {
        var catalog = new InMemoryLocalizationCatalog();

        Assert.Throws<ArgumentNullException>(() => catalog.AddTranslation("code", null!, "value"));
    }
}
