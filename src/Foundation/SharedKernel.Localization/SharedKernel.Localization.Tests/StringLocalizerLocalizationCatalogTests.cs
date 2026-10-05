using System.Globalization;
using Microsoft.Extensions.Localization;
using NSubstitute;
using Xunit;

namespace SharedKernel.Localization.Tests;

public sealed class StringLocalizerLocalizationCatalogTests
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr");

    private static StringLocalizerLocalizationCatalog CatalogReturning(string key, string value, bool notFound)
    {
        IStringLocalizer localizer = Substitute.For<IStringLocalizer>();
        localizer[key].Returns(new LocalizedString(key, value, resourceNotFound: notFound));

        IStringLocalizerFactory factory = Substitute.For<IStringLocalizerFactory>();
        factory.Create(typeof(Resource)).Returns(localizer);

        return new StringLocalizerLocalizationCatalog(factory, typeof(Resource));
    }

    [Fact]
    public void TryGetTemplate_Found_ReturnsParsedTemplate()
    {
        StringLocalizerLocalizationCatalog catalog = CatalogReturning("order.not_found", "{orderId} bulunamadı.", notFound: false);

        Assert.True(catalog.TryGetTemplate("order.not_found", Turkish, out MessageTemplate? template));
        Assert.Equal(["orderId"], template.PlaceholderNames);
    }

    [Fact]
    public void TryGetTemplate_ResourceNotFound_ReturnsFalse_DoesNotEchoTheKey()
    {
        // A missing key comes back with the key itself as the value.
        StringLocalizerLocalizationCatalog catalog = CatalogReturning("missing.key", "missing.key", notFound: true);

        Assert.False(catalog.TryGetTemplate("missing.key", Turkish, out MessageTemplate? template));
        Assert.Null(template);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Broken {0} template")]
    public void TryGetTemplate_BlankOrInvalidValue_CountsAsMissing(string value)
    {
        StringLocalizerLocalizationCatalog catalog = CatalogReturning("code", value, notFound: false);

        Assert.False(catalog.TryGetTemplate("code", Turkish, out _));
    }

    [Fact]
    public void TryGetTemplate_LooksUpInTheRequestedCulture_AndRestoresTheAmbientOne()
    {
        CultureInfo original = CultureInfo.CurrentUICulture;
        CultureInfo? observed = null;

        IStringLocalizer localizer = Substitute.For<IStringLocalizer>();
        localizer[Arg.Any<string>()].Returns(call =>
        {
            observed = CultureInfo.CurrentUICulture;
            return new LocalizedString((string)call[0], "value", resourceNotFound: false);
        });
        IStringLocalizerFactory factory = Substitute.For<IStringLocalizerFactory>();
        factory.Create(typeof(Resource)).Returns(localizer);

        new StringLocalizerLocalizationCatalog(factory, typeof(Resource)).TryGetTemplate("code", Turkish, out _);

        Assert.Equal(Turkish, observed);
        Assert.Equal(original, CultureInfo.CurrentUICulture);
    }

    [Fact]
    public void TryGetTemplate_LocalizerThrows_StillRestoresTheAmbientCulture()
    {
        CultureInfo original = CultureInfo.CurrentUICulture;
        IStringLocalizer localizer = Substitute.For<IStringLocalizer>();
        localizer[Arg.Any<string>()].Returns(_ => throw new InvalidOperationException("boom"));
        IStringLocalizerFactory factory = Substitute.For<IStringLocalizerFactory>();
        factory.Create(typeof(Resource)).Returns(localizer);
        var catalog = new StringLocalizerLocalizationCatalog(factory, typeof(Resource));

        Assert.Throws<InvalidOperationException>(() => catalog.TryGetTemplate("code", Turkish, out _));
        Assert.Equal(original, CultureInfo.CurrentUICulture);
    }

    [Fact]
    public void Constructor_And_TryGetTemplate_RejectInvalidArguments()
    {
        IStringLocalizerFactory factory = Substitute.For<IStringLocalizerFactory>();
        factory.Create(typeof(Resource)).Returns(Substitute.For<IStringLocalizer>());
        var catalog = new StringLocalizerLocalizationCatalog(factory, typeof(Resource));

        Assert.Throws<ArgumentNullException>(() => new StringLocalizerLocalizationCatalog(null!, typeof(Resource)));
        Assert.Throws<ArgumentNullException>(() => new StringLocalizerLocalizationCatalog(factory, null!));
        Assert.ThrowsAny<ArgumentException>(() => catalog.TryGetTemplate(" ", Turkish, out _));
        Assert.Throws<ArgumentNullException>(() => catalog.TryGetTemplate("code", null!, out _));
    }

    private sealed class Resource;
}
