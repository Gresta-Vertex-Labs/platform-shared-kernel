using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Constants;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.Meilisearch.Extensions;
using SharedKernel.Search.Meilisearch.Options;
using SharedKernel.Testing.Clocks;

namespace SharedKernel.Search.Meilisearch.Tests.Extensions;

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
/// Found by the CatalogApi sample, where it made <c>AddSearchReadinessCheck("products")</c> probe
/// ElasticSearch for a Meilisearch index and report a healthy service permanently unready — a symptom
/// with no visible connection to its cause.
/// </para>
/// </remarks>
public sealed class MeilisearchKeyedRegistrationTests
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
                [$"{MeilisearchOptions.SectionName}:Url"] = "http://localhost:7700",
                [$"{MeilisearchOptions.SectionName}:ApiKey"] = "test-key",
            })
            .Build();

        services
            .AddSharedKernelMeilisearchSearch(configuration)
            .AddIndex<TestDocument>("products", index => index
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
            SearchWellKnown.MeilisearchProviderName);

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
            SearchWellKnown.MeilisearchProviderName);

        keyed.Should().BeSameAs(unkeyed);
    }

    [Fact]
    public void Descriptor_ResolvesUnkeyed_AndUnderTheProviderKey_AsTheSameInstance()
    {
        using var provider = BuildProvider();

        var unkeyed = provider.GetRequiredService<ISearchProviderDescriptor>();
        var keyed = provider.GetRequiredKeyedService<ISearchProviderDescriptor>(
            SearchWellKnown.MeilisearchProviderName);

        keyed.Should().BeSameAs(unkeyed);
        keyed.ProviderName.Should().Be(SearchWellKnown.MeilisearchProviderName);
    }

    [Fact]
    public void TheProviderKeyIsTheProviderName_NotAnInventedString()
    {
        // A consuming service resolving the non-generic contracts by key passes this
        // same constant, so the key must be the one already on the public surface.
        using var provider = BuildProvider();

        var descriptor = provider.GetRequiredKeyedService<ISearchProviderDescriptor>(
            SearchWellKnown.MeilisearchProviderName);

        descriptor.ProviderName.Should().Be(SearchWellKnown.MeilisearchProviderName);
    }
}
