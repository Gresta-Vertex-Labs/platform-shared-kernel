using System.Globalization;
using Microsoft.Extensions.Localization;
using NSubstitute;
using Xunit;

namespace SharedKernel.Localization.Tests;

public sealed class StringLocalizerLocalizationCatalogTests
{
    private static readonly CultureInfo EnglishUs = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr");

    [Fact]
    public void TryGetString_KeyFound_ReturnsTrueAndTranslatedValue()
    {
        IStringLocalizer localizer = Substitute.For<IStringLocalizer>();
        localizer["greeting"].Returns(new LocalizedString("greeting", "Merhaba", resourceNotFound: false));

        IStringLocalizerFactory factory = Substitute.For<IStringLocalizerFactory>();
        factory.Create(typeof(ConsumerResource)).Returns(localizer);

        var catalog = new StringLocalizerLocalizationCatalog(factory, typeof(ConsumerResource));

        bool found = catalog.TryGetString("greeting", EnglishUs, out string? value);

        Assert.True(found);
        Assert.Equal("Merhaba", value);
    }

    [Fact]
    public void TryGetString_ResourceNotFound_ReturnsFalseAndNull_DespiteLocalizedStringCarryingTheRawKeyAsValue()
    {
        // IStringLocalizer's real behavior when a key is missing: LocalizedString.Value falls back
        // to the key itself, with ResourceNotFound = true. Forwarding .Value blindly here would
        // return the raw error code ("missing.key") as if it were a translation — the exact
        // inversion of the platform's "never blank" fallback contract that this type's own XML
        // docs call out. This test proves TryGetString checks ResourceNotFound, not just Value.
        IStringLocalizer localizer = Substitute.For<IStringLocalizer>();
        localizer["missing.key"].Returns(new LocalizedString("missing.key", "missing.key", resourceNotFound: true));

        IStringLocalizerFactory factory = Substitute.For<IStringLocalizerFactory>();
        factory.Create(typeof(ConsumerResource)).Returns(localizer);

        var catalog = new StringLocalizerLocalizationCatalog(factory, typeof(ConsumerResource));

        bool found = catalog.TryGetString("missing.key", EnglishUs, out string? value);

        Assert.False(found);
        Assert.Null(value);
    }

    [Fact]
    public void TryGetString_SetsAmbientCurrentUICultureForDurationOfCall_ThenRestoresIt()
    {
        CultureInfo original = CultureInfo.CurrentUICulture;
        CultureInfo? observedDuringCall = null;

        try
        {
            IStringLocalizer localizer = Substitute.For<IStringLocalizer>();
            localizer[Arg.Any<string>()].Returns(callInfo =>
            {
                observedDuringCall = CultureInfo.CurrentUICulture;
                return new LocalizedString((string)callInfo[0], "value", resourceNotFound: false);
            });

            IStringLocalizerFactory factory = Substitute.For<IStringLocalizerFactory>();
            factory.Create(typeof(ConsumerResource)).Returns(localizer);

            var catalog = new StringLocalizerLocalizationCatalog(factory, typeof(ConsumerResource));

            catalog.TryGetString("greeting", Turkish, out _);

            Assert.Equal(Turkish, observedDuringCall);
            Assert.Equal(original, CultureInfo.CurrentUICulture);
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Fact]
    public void TryGetString_RestoresAmbientCulture_EvenWhenLocalizerThrows()
    {
        CultureInfo original = CultureInfo.CurrentUICulture;

        try
        {
            IStringLocalizer localizer = Substitute.For<IStringLocalizer>();
            localizer[Arg.Any<string>()].Returns(_ => throw new InvalidOperationException("boom"));

            IStringLocalizerFactory factory = Substitute.For<IStringLocalizerFactory>();
            factory.Create(typeof(ConsumerResource)).Returns(localizer);

            var catalog = new StringLocalizerLocalizationCatalog(factory, typeof(ConsumerResource));

            Assert.Throws<InvalidOperationException>(() => catalog.TryGetString("greeting", Turkish, out _));
            Assert.Equal(original, CultureInfo.CurrentUICulture);
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Fact]
    public void Constructor_NullFactory_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new StringLocalizerLocalizationCatalog(null!, typeof(ConsumerResource)));
    }

    [Fact]
    public void Constructor_NullResourceType_ThrowsArgumentNullException()
    {
        IStringLocalizerFactory factory = Substitute.For<IStringLocalizerFactory>();

        Assert.Throws<ArgumentNullException>(() => new StringLocalizerLocalizationCatalog(factory, null!));
    }

    [Fact]
    public void TryGetString_NullCode_ThrowsArgumentNullException()
    {
        IStringLocalizerFactory factory = Substitute.For<IStringLocalizerFactory>();
        factory.Create(typeof(ConsumerResource)).Returns(Substitute.For<IStringLocalizer>());
        var catalog = new StringLocalizerLocalizationCatalog(factory, typeof(ConsumerResource));

        Assert.Throws<ArgumentNullException>(() => catalog.TryGetString(null!, EnglishUs, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TryGetString_EmptyOrWhitespaceCode_ThrowsArgumentException(string code)
    {
        IStringLocalizerFactory factory = Substitute.For<IStringLocalizerFactory>();
        factory.Create(typeof(ConsumerResource)).Returns(Substitute.For<IStringLocalizer>());
        var catalog = new StringLocalizerLocalizationCatalog(factory, typeof(ConsumerResource));

        Assert.Throws<ArgumentException>(() => catalog.TryGetString(code, EnglishUs, out _));
    }

    [Fact]
    public void TryGetString_NullCulture_ThrowsArgumentNullException()
    {
        IStringLocalizerFactory factory = Substitute.For<IStringLocalizerFactory>();
        factory.Create(typeof(ConsumerResource)).Returns(Substitute.For<IStringLocalizer>());
        var catalog = new StringLocalizerLocalizationCatalog(factory, typeof(ConsumerResource));

        Assert.Throws<ArgumentNullException>(() => catalog.TryGetString("code", null!, out _));
    }

    /// <summary>A stand-in resource-owning marker type — mirrors a real service's own resource class.</summary>
    private sealed class ConsumerResource;
}
