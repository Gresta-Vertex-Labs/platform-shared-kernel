using System.Globalization;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using NSubstitute;
using Xunit;

namespace SharedKernel.Localization.Tests;

public sealed class LocalizationServiceCollectionExtensionsTests
{
    private static readonly CultureInfo EnglishUs = CultureInfo.GetCultureInfo("en-US");

    [Fact]
    public void AddInMemoryLocalizationCatalog_Resolves_InMemoryLocalizationCatalog()
    {
        var services = new ServiceCollection();

        services.AddInMemoryLocalizationCatalog();

        using ServiceProvider provider = services.BuildServiceProvider();
        ILocalizationCatalog catalog = provider.GetRequiredService<ILocalizationCatalog>();

        Assert.IsType<InMemoryLocalizationCatalog>(catalog);
    }

    [Fact]
    public void AddInMemoryLocalizationCatalog_ConfigureCallback_SeedsCatalogBeforeItResolves()
    {
        var services = new ServiceCollection();

        services.AddInMemoryLocalizationCatalog(catalog =>
            catalog.AddTranslation("greeting", EnglishUs, "Hello"));

        using ServiceProvider provider = services.BuildServiceProvider();
        ILocalizationCatalog catalog = provider.GetRequiredService<ILocalizationCatalog>();

        bool found = catalog.TryGetString("greeting", EnglishUs, out string? value);

        Assert.True(found);
        Assert.Equal("Hello", value);
    }

    [Fact]
    public void AddInMemoryLocalizationCatalog_RegistersAsSingleton()
    {
        var services = new ServiceCollection();

        services.AddInMemoryLocalizationCatalog();

        using ServiceProvider provider = services.BuildServiceProvider();

        ILocalizationCatalog first = provider.GetRequiredService<ILocalizationCatalog>();
        ILocalizationCatalog second = provider.GetRequiredService<ILocalizationCatalog>();

        Assert.Same(first, second);
    }

    [Fact]
    public void AddInMemoryLocalizationCatalog_NullServices_ThrowsArgumentNullException()
    {
        IServiceCollection? services = null;

        Assert.Throws<ArgumentNullException>(() => services!.AddInMemoryLocalizationCatalog());
    }

    [Fact]
    public void AddStringLocalizerCatalog_Resolves_StringLocalizerLocalizationCatalog()
    {
        var services = new ServiceCollection();
        IStringLocalizerFactory factory = Substitute.For<IStringLocalizerFactory>();
        factory.Create(typeof(ConsumerResource)).Returns(Substitute.For<IStringLocalizer>());
        services.AddSingleton(factory);

        services.AddStringLocalizerCatalog<ConsumerResource>();

        using ServiceProvider provider = services.BuildServiceProvider();
        ILocalizationCatalog catalog = provider.GetRequiredService<ILocalizationCatalog>();

        Assert.IsType<StringLocalizerLocalizationCatalog>(catalog);
    }

    [Fact]
    public void AddStringLocalizerCatalog_NoFactoryRegistered_ThrowsOnResolve()
    {
        var services = new ServiceCollection();

        services.AddStringLocalizerCatalog<ConsumerResource>();

        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Throws<InvalidOperationException>(() =>
        {
            provider.GetRequiredService<ILocalizationCatalog>();
        });
    }

    [Fact]
    public void AddStringLocalizerCatalog_NullServices_ThrowsArgumentNullException()
    {
        IServiceCollection? services = null;

        Assert.Throws<ArgumentNullException>(() => services!.AddStringLocalizerCatalog<ConsumerResource>());
    }

    // ── Name-collision guard (T-61) ──────────────────────────────────────────────────────────
    // AddSharedKernelLocalization() is reserved for 13.ServiceDefaults's culture-resolution
    // middleware (P-483) and must never be declared by this package. A reflection-based scan over
    // every public static method this assembly exposes makes that structural, not a promise kept
    // only by review.

    [Fact]
    public void ThisAssembly_DeclaresNoMethodNamed_AddSharedKernelLocalization()
    {
        Assembly assembly = typeof(LocalizationServiceCollectionExtensions).Assembly;

        IEnumerable<MethodInfo> allPublicStaticMethods = assembly.GetTypes()
            .Where(t => t.IsPublic)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly));

        Assert.DoesNotContain(allPublicStaticMethods, m => m.Name == "AddSharedKernelLocalization");
    }

    [Fact]
    public void ThisAssembly_DoesDeclare_TheTwoSanctionedRegistrationMethods()
    {
        // Companion to the guard above: proves the scan itself is not vacuously passing because it
        // found zero extension methods at all.
        Assembly assembly = typeof(LocalizationServiceCollectionExtensions).Assembly;

        IEnumerable<string> allPublicStaticMethodNames = assembly.GetTypes()
            .Where(t => t.IsPublic)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Select(m => m.Name);

        Assert.Contains("AddInMemoryLocalizationCatalog", allPublicStaticMethodNames);
        Assert.Contains("AddStringLocalizerCatalog", allPublicStaticMethodNames);
    }

    private sealed class ConsumerResource;
}
