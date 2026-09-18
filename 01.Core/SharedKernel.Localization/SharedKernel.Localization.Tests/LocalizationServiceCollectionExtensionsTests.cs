using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using NSubstitute;
using Xunit;

namespace SharedKernel.Localization.Tests;

public sealed class LocalizationServiceCollectionExtensionsTests
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr");

    [Fact]
    public void AddLocalizationCatalog_RegistersOneSingleton_AsInterfaceAndConcreteType()
    {
        var services = new ServiceCollection();
        services.AddLocalizationCatalog(catalog => catalog.Add("greeting", Turkish, "Merhaba"));

        using ServiceProvider provider = services.BuildServiceProvider();
        ILocalizationCatalog catalog = provider.GetRequiredService<ILocalizationCatalog>();

        Assert.Same(catalog, provider.GetRequiredService<ILocalizationCatalog>());
        Assert.Same(catalog, provider.GetRequiredService<InMemoryLocalizationCatalog>());
        Assert.True(catalog.TryGetString("greeting", Turkish, out string? value));
        Assert.Equal("Merhaba", value);
    }

    [Fact]
    public void AddLocalizationCatalog_BrokenTranslation_FailsDuringRegistration_NotOnFirstUse()
    {
        var services = new ServiceCollection();

        Assert.Throws<FormatException>(
            () => services.AddLocalizationCatalog(catalog => catalog.Add("a", Turkish, "{0}")));
        Assert.Empty(services);
    }

    [Fact]
    public void AddStringLocalizerCatalog_ResolvesOverTheRegisteredFactory()
    {
        var services = new ServiceCollection();
        IStringLocalizerFactory factory = Substitute.For<IStringLocalizerFactory>();
        factory.Create(typeof(Resource)).Returns(Substitute.For<IStringLocalizer>());
        services.AddSingleton(factory);
        services.AddStringLocalizerCatalog<Resource>();

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.IsType<StringLocalizerLocalizationCatalog>(provider.GetRequiredService<ILocalizationCatalog>());
    }

    [Fact]
    public void AddStringLocalizerCatalog_WithoutAFactory_FailsOnResolve()
    {
        var services = new ServiceCollection();
        services.AddStringLocalizerCatalog<Resource>();

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<ILocalizationCatalog>());
    }

    [Fact]
    public void RegisteringASecondCatalog_Throws_InEitherOrder()
    {
        var first = new ServiceCollection().AddLocalizationCatalog(_ => { });
        var second = new ServiceCollection().AddStringLocalizerCatalog<Resource>();

        Assert.Throws<InvalidOperationException>(() => first.AddStringLocalizerCatalog<Resource>());
        Assert.Throws<InvalidOperationException>(() => second.AddLocalizationCatalog(_ => { }));
        Assert.Throws<InvalidOperationException>(() => first.AddLocalizationCatalog(_ => { }));
    }

    [Fact]
    public void NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddLocalizationCatalog(_ => { }));
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddLocalizationCatalog(null!));
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddStringLocalizerCatalog<Resource>());
    }

    private sealed class Resource;
}
