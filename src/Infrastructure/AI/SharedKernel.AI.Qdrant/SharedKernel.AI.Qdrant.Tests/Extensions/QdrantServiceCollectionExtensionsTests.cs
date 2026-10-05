using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Qdrant.Client;
using SharedKernel.AI.Abstractions.Abstractions;
using SharedKernel.AI.Abstractions.Models;
using SharedKernel.AI.Qdrant.Extensions;
using SharedKernel.AI.Qdrant.Options;
using SharedKernel.AI.Qdrant.Quantization;
using SharedKernel.AI.Qdrant.Raw;
using SharedKernel.AI.Qdrant.Sparse;
using SharedKernel.AI.Qdrant.Tests.TestSupport;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Testing.Clocks;

namespace SharedKernel.AI.Qdrant.Tests.Extensions;

/// <summary>
/// DI-registration and options-validation tests (container-free). The provider extension deliberately
/// registers neither <see cref="ILogger{TCategoryName}"/> nor <see cref="IClock"/> — every test here
/// registers both itself, mirroring the platform-wide precedent.
/// </summary>
public sealed class QdrantServiceCollectionExtensionsTests
{
    private static IConfiguration BuildValidConfig() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{QdrantOptions.SectionName}:Host"] = "localhost",
            [$"{QdrantOptions.SectionName}:Port"] = "6334",
        })
        .Build();

    private static ServiceProvider BuildProviderWithOneCollection(Action<QdrantBuilder>? configureBuilder = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IClock>(new FakeClock());

        var builder = services.AddSharedKernelQdrant(BuildValidConfig())
            .AddCollection<TestVectorRecord>("products", b => b.EmbeddingModel("model-a", 2).DistanceMetric(VectorDistanceMetric.Cosine));

        configureBuilder?.Invoke(builder);
        builder.Build();

        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddCollection_Resolves_IVectorCollection_AsScoped_DifferentInstancesAcrossScopes()
    {
        var provider = BuildProviderWithOneCollection();

        using var scopeA = provider.CreateScope();
        using var scopeB = provider.CreateScope();
        var first = scopeA.ServiceProvider.GetRequiredService<IVectorCollection<TestVectorRecord>>();
        var second = scopeB.ServiceProvider.GetRequiredService<IVectorCollection<TestVectorRecord>>();
        var sameScopeAgain = scopeA.ServiceProvider.GetRequiredService<IVectorCollection<TestVectorRecord>>();

        first.Should().NotBeSameAs(second);
        first.Should().BeSameAs(sameScopeAgain);
    }

    [Fact]
    public void QdrantClient_Resolves_AsSingleton_SameInstanceAcrossResolutions()
    {
        var provider = BuildProviderWithOneCollection();

        var first = provider.GetRequiredService<QdrantClient>();
        var second = provider.GetRequiredService<QdrantClient>();
        var viaInterface = provider.GetRequiredService<IQdrantClient>();

        first.Should().BeSameAs(second);
        viaInterface.Should().BeSameAs(first);
    }

    [Fact]
    public void IVectorCollectionProvisioner_Resolves_AsSingleton()
    {
        var provider = BuildProviderWithOneCollection();

        var first = provider.GetRequiredService<IVectorCollectionProvisioner>();
        var second = provider.GetRequiredService<IVectorCollectionProvisioner>();

        first.Should().BeSameAs(second);
    }

    [Fact]
    public void IVectorProviderDescriptor_Resolves_AsSingleton_WithRegisteredCollectionName()
    {
        var provider = BuildProviderWithOneCollection();

        var descriptor = provider.GetRequiredService<IVectorProviderDescriptor>();

        descriptor.ProviderName.Should().Be("qdrant");
        descriptor.RegisteredCollections.Should().Contain("products");
    }

    [Fact]
    public void IQdrantQuantizationProfileAccessor_AlwaysResolves()
    {
        var provider = BuildProviderWithOneCollection();

        var accessor = provider.GetService<IQdrantQuantizationProfileAccessor>();

        accessor.Should().NotBeNull();
    }

    [Fact]
    public void IQdrantHybridQueryAccessor_Resolves_ForRegisteredCollection()
    {
        var provider = BuildProviderWithOneCollection();

        using var scope = provider.CreateScope();
        var accessor = scope.ServiceProvider.GetService<IQdrantHybridQueryAccessor<TestVectorRecord>>();

        accessor.Should().NotBeNull();
    }

    [Fact]
    public void IQdrantRawClientAccessor_IsNotResolvable_WithoutAllowRawClientAccess()
    {
        var provider = BuildProviderWithOneCollection();

        var accessor = provider.GetService<IQdrantRawClientAccessor>();

        accessor.Should().BeNull();
    }

    [Fact]
    public void IQdrantRawClientAccessor_IsResolvable_WithAllowRawClientAccess()
    {
        var provider = BuildProviderWithOneCollection(b => b.AllowRawClientAccess());

        var accessor = provider.GetService<IQdrantRawClientAccessor>();

        accessor.Should().NotBeNull();
    }
}
