using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Constants;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.ElasticSearch.Extensions;
using SharedKernel.Search.ElasticSearch.Options;
using SearchFieldKind = SharedKernel.Search.Abstractions.Models.SearchFieldKind;
using SharedKernel.Testing.Clocks;

namespace SharedKernel.Search.ElasticSearch.Tests.Extensions;

/// <summary>
/// Container-free tests for the keyed registration of the two non-generic neutral contracts.
/// </summary>
/// <remarks>
/// <para>
/// <b>The defect these exist for.</b> <see cref="ISearchIndexProvisioner"/> and
/// <see cref="ISearchProviderDescriptor"/> are non-generic, so in a host that registers both engines the
/// second registration silently shadows the first on an unkeyed resolution. The domain's recorded
/// remedy — "use a distinct <c>TDocument</c> per provider" — disambiguates
/// <c>ISearchIndex&lt;TDocument&gt;</c> and nothing else, so it does not help here at all.
/// </para>
/// <para>
/// Found by a sample service, where it made <c>AddSearchReadinessCheck("products")</c> probe
/// ElasticSearch for a ElasticSearch index and report a healthy service permanently unready — a symptom
/// with no visible connection to its cause.
/// </para>
/// </remarks>
public sealed class ElasticSearchKeyedRegistrationTests
{
    private sealed class TestDocument : ISearchDocument
    {
        public required string DocumentId { get; init; }
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IClock>(new FakeClock());

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"{ElasticSearchOptions.SectionName}:Nodes:0"] = "http://localhost:9200",
            })
            .Build();

        services
            .AddSharedKernelElasticSearchSearch(configuration)
            .AddIndex<TestDocument>("products-read", "products-write", index => index
                .Field("name", SearchFieldKind.Text, searchable: true))
            .Build();

        return services.BuildServiceProvider();
    }

    [Fact]
    public void Provisioner_ResolvesUnkeyed_AndUnderTheProviderKey()
    {
        using var provider = BuildProvider();

        var unkeyed = provider.GetRequiredService<ISearchIndexProvisioner>();
        var keyed = provider.GetRequiredKeyedService<ISearchIndexProvisioner>(
            SearchWellKnown.ElasticSearchProviderName);

        unkeyed.Should().NotBeNull();
        keyed.Should().NotBeNull();
    }

    [Fact]
    public void Provisioner_KeyedAndUnkeyedResolutionsReturnTheSameInstance()
    {
        // Not cosmetic: the provisioner holds per-index state, and the ElasticSearch sibling caches probe
        // results. Two instances would mean two caches that can disagree about whether an index is ready.
        using var provider = BuildProvider();

        var unkeyed = provider.GetRequiredService<ISearchIndexProvisioner>();
        var keyed = provider.GetRequiredKeyedService<ISearchIndexProvisioner>(
            SearchWellKnown.ElasticSearchProviderName);

        keyed.Should().BeSameAs(unkeyed);
    }

    [Fact]
    public void Descriptor_ResolvesUnkeyed_AndUnderTheProviderKey_AsTheSameInstance()
    {
        using var provider = BuildProvider();

        var unkeyed = provider.GetRequiredService<ISearchProviderDescriptor>();
        var keyed = provider.GetRequiredKeyedService<ISearchProviderDescriptor>(
            SearchWellKnown.ElasticSearchProviderName);

        keyed.Should().BeSameAs(unkeyed);
        keyed.ProviderName.Should().Be(SearchWellKnown.ElasticSearchProviderName);
    }

    [Fact]
    public void TheProviderKeyIsTheProviderName_NotAnInventedString()
    {
        // A consuming service resolving the non-generic contracts by key passes this
        // same constant, so the key must be the one already on the public surface.
        using var provider = BuildProvider();

        var descriptor = provider.GetRequiredKeyedService<ISearchProviderDescriptor>(
            SearchWellKnown.ElasticSearchProviderName);

        descriptor.ProviderName.Should().Be(SearchWellKnown.ElasticSearchProviderName);
    }
}
