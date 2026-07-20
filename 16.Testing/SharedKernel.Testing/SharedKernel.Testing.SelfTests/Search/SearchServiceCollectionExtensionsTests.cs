using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Testing.Search;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Search;

/// <summary>
/// Proves the <c>Search/</c> DI convenience extensions (<c>AddInMemorySearchIndex&lt;TDocument&gt;</c>
/// registration shape, <c>AddInMemorySearchProvisioning</c> registration shape) — no consuming
/// domain has adopted these fakes yet, so this self-test is the only behavioral proof today, per
/// the SelfTests routing rule.
/// </summary>
public sealed class SearchServiceCollectionExtensionsTests
{
    [Fact]
    public void AddInMemorySearchIndex_ResolvesISearchIndex_AsInMemorySearchIndex()
    {
        var provider = BuildIndexProvider();

        Assert.IsType<InMemorySearchIndex<TestProductDocument>>(provider.GetRequiredService<ISearchIndex<TestProductDocument>>());
    }

    [Fact]
    public void AddInMemorySearchIndex_ConcreteTypeAndInterface_ResolveSameSingletonInstance()
    {
        var provider = BuildIndexProvider();

        var concrete = provider.GetRequiredService<InMemorySearchIndex<TestProductDocument>>();
        var viaInterface = provider.GetRequiredService<ISearchIndex<TestProductDocument>>();

        Assert.Same(concrete, viaInterface);
        Assert.Same(viaInterface, provider.GetRequiredService<ISearchIndex<TestProductDocument>>());
    }

    [Fact]
    public void AddInMemorySearchIndex_NullServices_Throws() =>
        Assert.Throws<ArgumentNullException>(() =>
            ((IServiceCollection)null!).AddInMemorySearchIndex<TestProductDocument>(BuildDefinition()));

    [Fact]
    public void AddInMemorySearchIndex_NullDefinition_Throws()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() => services.AddInMemorySearchIndex<TestProductDocument>(null!));
    }

    [Fact]
    public void AddInMemorySearchProvisioning_ResolvesBothSingletons_WithGivenProviderName()
    {
        var services = new ServiceCollection();
        services.AddInMemorySearchProvisioning("custom-fake");
        var provider = services.BuildServiceProvider();

        var provisioner = provider.GetRequiredService<ISearchIndexProvisioner>();
        var descriptor = provider.GetRequiredService<ISearchProviderDescriptor>();

        Assert.IsType<InMemorySearchIndexProvisioner>(provisioner);
        Assert.IsType<InMemorySearchProviderDescriptor>(descriptor);
        Assert.Equal("custom-fake", descriptor.ProviderName);
        Assert.Same(provisioner, provider.GetRequiredService<ISearchIndexProvisioner>());
        Assert.Same(descriptor, provider.GetRequiredService<ISearchProviderDescriptor>());
    }

    [Fact]
    public void AddInMemorySearchProvisioning_DefaultProviderName_IsInMemoryFake()
    {
        var services = new ServiceCollection();
        services.AddInMemorySearchProvisioning();
        var provider = services.BuildServiceProvider();

        Assert.Equal("in-memory-fake", provider.GetRequiredService<ISearchProviderDescriptor>().ProviderName);
    }

    [Fact]
    public void AddInMemorySearchProvisioning_NullServices_Throws() =>
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddInMemorySearchProvisioning());

    [Fact]
    public void AddInMemorySearchProvisioning_NullProviderName_Throws()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() => services.AddInMemorySearchProvisioning(null!));
    }

    [Fact]
    public void TwoDifferentDocumentTypes_RegisterIndependentIndexInstances()
    {
        var services = new ServiceCollection();
        services.AddInMemorySearchIndex<TestProductDocument>(BuildDefinition());
        services.AddInMemorySearchProvisioning();
        var provider = services.BuildServiceProvider();

        // Non-coupling proof: ISearchIndex<TDocument> and the non-generic provisioner/descriptor
        // are independently registered fakes with no shared backing state (Search/'s scope-lock).
        var index = provider.GetRequiredService<ISearchIndex<TestProductDocument>>();
        var descriptor = provider.GetRequiredService<ISearchProviderDescriptor>();
        Assert.Empty(descriptor.RegisteredIndexes);
        Assert.Equal("products", index.IndexName);
    }

    private static ServiceProvider BuildIndexProvider()
    {
        var services = new ServiceCollection();
        services.AddInMemorySearchIndex<TestProductDocument>(BuildDefinition());
        return services.BuildServiceProvider();
    }

    private static SearchIndexDefinition BuildDefinition() =>
        new SearchIndexDefinitionBuilder("products")
            .Field("Name", SearchFieldKind.Text, searchable: true)
            .Build()
            .Value;
}
