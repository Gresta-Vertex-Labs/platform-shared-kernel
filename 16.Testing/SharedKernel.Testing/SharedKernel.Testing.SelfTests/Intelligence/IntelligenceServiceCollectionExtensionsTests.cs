using Microsoft.Extensions.DependencyInjection;
using SharedKernel.AI.Abstractions.Abstractions;
using SharedKernel.AI.Abstractions.Models;
using SharedKernel.Testing.Intelligence;

namespace SharedKernel.Testing.SelfTests.Intelligence;

/// <summary>
/// Proves the <c>Intelligence/</c> DI convenience extensions
/// (<c>AddInMemoryEmbeddingGenerator</c>/<c>AddInMemoryVectorCollection&lt;TRecord&gt;</c>/
/// <c>AddInMemoryVectorProvisioning</c>/<c>AddInMemorySemanticKernel</c> registration shapes) -- no
/// consuming domain has adopted these fakes yet, so this self-test is the only behavioral proof today,
/// per the SelfTests routing rule.
/// </summary>
public sealed class IntelligenceServiceCollectionExtensionsTests
{
    [Fact]
    public void AddInMemoryEmbeddingGenerator_ResolvesIEmbeddingGenerator_AsInMemoryEmbeddingGenerator()
    {
        var services = new ServiceCollection();
        services.AddInMemoryEmbeddingGenerator("test-model", 8);
        var provider = services.BuildServiceProvider();

        Assert.IsType<InMemoryEmbeddingGenerator>(provider.GetRequiredService<IEmbeddingGenerator>());
    }

    [Fact]
    public void AddInMemoryEmbeddingGenerator_ConcreteTypeAndInterface_ResolveSameSingletonInstance()
    {
        var services = new ServiceCollection();
        services.AddInMemoryEmbeddingGenerator("test-model", 8);
        var provider = services.BuildServiceProvider();

        var concrete = provider.GetRequiredService<InMemoryEmbeddingGenerator>();
        var viaInterface = provider.GetRequiredService<IEmbeddingGenerator>();

        Assert.Same(concrete, viaInterface);
        Assert.Same(viaInterface, provider.GetRequiredService<IEmbeddingGenerator>());
    }

    [Fact]
    public void AddInMemoryEmbeddingGenerator_NullServices_Throws() =>
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddInMemoryEmbeddingGenerator("test-model", 8));

    [Fact]
    public void AddInMemoryVectorCollection_ResolvesIVectorCollection_AsInMemoryVectorCollection()
    {
        var provider = BuildCollectionProvider();

        Assert.IsType<InMemoryVectorCollection<TestVectorRecord>>(provider.GetRequiredService<IVectorCollection<TestVectorRecord>>());
    }

    [Fact]
    public void AddInMemoryVectorCollection_ConcreteTypeAndInterface_ResolveSameSingletonInstance()
    {
        var provider = BuildCollectionProvider();

        var concrete = provider.GetRequiredService<InMemoryVectorCollection<TestVectorRecord>>();
        var viaInterface = provider.GetRequiredService<IVectorCollection<TestVectorRecord>>();

        Assert.Same(concrete, viaInterface);
        Assert.Same(viaInterface, provider.GetRequiredService<IVectorCollection<TestVectorRecord>>());
    }

    [Fact]
    public void AddInMemoryVectorCollection_NullServices_Throws() =>
        Assert.Throws<ArgumentNullException>(() =>
            ((IServiceCollection)null!).AddInMemoryVectorCollection<TestVectorRecord>(BuildDefinition()));

    [Fact]
    public void AddInMemoryVectorCollection_NullDefinition_Throws()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() => services.AddInMemoryVectorCollection<TestVectorRecord>(null!));
    }

    [Fact]
    public void AddInMemoryVectorProvisioning_ResolvesBothSingletons_WithGivenProviderName()
    {
        var services = new ServiceCollection();
        services.AddInMemoryVectorProvisioning("custom-fake");
        var provider = services.BuildServiceProvider();

        var provisioner = provider.GetRequiredService<IVectorCollectionProvisioner>();
        var descriptor = provider.GetRequiredService<IVectorProviderDescriptor>();

        Assert.IsType<InMemoryVectorCollectionProvisioner>(provisioner);
        Assert.IsType<InMemoryVectorProviderDescriptor>(descriptor);
        Assert.Equal("custom-fake", descriptor.ProviderName);
        Assert.Same(provisioner, provider.GetRequiredService<IVectorCollectionProvisioner>());
        Assert.Same(descriptor, provider.GetRequiredService<IVectorProviderDescriptor>());
    }

    [Fact]
    public void AddInMemoryVectorProvisioning_DefaultProviderName_IsInMemoryFake()
    {
        var services = new ServiceCollection();
        services.AddInMemoryVectorProvisioning();
        var provider = services.BuildServiceProvider();

        Assert.Equal("in-memory-fake", provider.GetRequiredService<IVectorProviderDescriptor>().ProviderName);
    }

    [Fact]
    public void AddInMemoryVectorProvisioning_NullServices_Throws() =>
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddInMemoryVectorProvisioning());

    [Fact]
    public void AddInMemoryVectorProvisioning_NullProviderName_Throws()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() => services.AddInMemoryVectorProvisioning(null!));
    }

    [Fact]
    public void AddInMemorySemanticKernel_ResolvesBothSingletons()
    {
        var services = new ServiceCollection();
        services.AddInMemorySemanticKernel();
        var provider = services.BuildServiceProvider();

        var kernel = provider.GetRequiredService<ISemanticKernel>();
        var descriptor = provider.GetRequiredService<ICompletionProviderDescriptor>();

        Assert.IsType<InMemorySemanticKernel>(kernel);
        Assert.IsType<InMemoryCompletionProviderDescriptor>(descriptor);
        Assert.Same(kernel, provider.GetRequiredService<ISemanticKernel>());
        Assert.Same(descriptor, provider.GetRequiredService<ICompletionProviderDescriptor>());
    }

    [Fact]
    public void AddInMemorySemanticKernel_NullServices_Throws() =>
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddInMemorySemanticKernel());

    [Fact]
    public void VectorCollectionAndProvisioning_AreIndependentlyRegistered_NonCouplingProof()
    {
        var services = new ServiceCollection();
        services.AddInMemoryVectorCollection<TestVectorRecord>(BuildDefinition());
        services.AddInMemoryVectorProvisioning();
        var provider = services.BuildServiceProvider();

        // Non-coupling proof: IVectorCollection<TRecord> and the non-generic provisioner/descriptor
        // are independently registered fakes with no shared backing state (Intelligence/'s scope-lock).
        var collection = provider.GetRequiredService<IVectorCollection<TestVectorRecord>>();
        var descriptor = provider.GetRequiredService<IVectorProviderDescriptor>();
        Assert.Empty(descriptor.RegisteredCollections);
        Assert.Equal("chunks", collection.CollectionName);
    }

    private static ServiceProvider BuildCollectionProvider()
    {
        var services = new ServiceCollection();
        services.AddInMemoryVectorCollection<TestVectorRecord>(BuildDefinition());
        return services.BuildServiceProvider();
    }

    private static VectorCollectionDefinition BuildDefinition() =>
        new VectorCollectionDefinitionBuilder("chunks")
            .EmbeddingModel("test-model", 8)
            .DistanceMetric(VectorDistanceMetric.Cosine)
            .Build()
            .Value;
}
