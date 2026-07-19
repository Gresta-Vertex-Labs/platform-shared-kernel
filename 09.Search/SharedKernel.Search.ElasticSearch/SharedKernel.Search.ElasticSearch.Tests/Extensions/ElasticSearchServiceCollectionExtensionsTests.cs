using Elastic.Clients.Elasticsearch;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.ElasticSearch.Analytics;
using SharedKernel.Search.ElasticSearch.Cursors;
using SharedKernel.Search.ElasticSearch.Extensions;
using SharedKernel.Search.ElasticSearch.Options;
using SharedKernel.Search.ElasticSearch.Raw;
using SharedKernel.Testing.Clocks;
using SharedKernel.Testing.Logging;

namespace SharedKernel.Search.ElasticSearch.Tests.Extensions;

/// <summary>
/// T-20: DI registration and options-validation tests (container-free) mirroring Meilisearch's T-12 —
/// <see cref="ISearchIndex{TDocument}"/>, <see cref="IAnalyticsSearch{TDocument}"/> and
/// <see cref="ICursorSearch{TDocument}"/> resolve scoped; <see cref="ElasticsearchClient"/> resolves as
/// a singleton; <see cref="IElasticSearchRawClientAccessor"/> is gated on
/// <c>AllowRawClientAccess()</c>; a missing <c>Nodes</c> fails validation; omitting
/// <c>WithSourceSerializerContext</c> logs Warning 9221 at startup; and
/// <c>AllowInvalidCertificates = true</c> logs Warning 9217. Warnings are asserted via <c>16.Testing</c>'s
/// in-memory <see cref="ILogger"/> double on <c>EventId</c> and level — never on a rendered message
/// string.
/// </summary>
/// <remarks>
/// Every config here sets <c>ValidateEngineVersionOnStart=false</c> — the client singleton factory
/// otherwise performs a real, synchronous <c>InfoAsync()</c> network call at first resolution, which
/// would defeat the "container-free" premise of this suite. The engine-version guard itself is a
/// real-backend concern covered by T-22.
/// </remarks>
public sealed class ElasticSearchServiceCollectionExtensionsTests
{
    private sealed class TestDocument : ISearchDocument
    {
        public required string DocumentId { get; init; }
    }

    [Fact]
    public void AddIndex_Resolves_ISearchIndex_AsScoped_DifferentInstancesAcrossScopes()
    {
        var provider = BuildProviderWithOneIndex();

        using var scopeA = provider.CreateScope();
        using var scopeB = provider.CreateScope();
        var first = scopeA.ServiceProvider.GetRequiredService<ISearchIndex<TestDocument>>();
        var second = scopeB.ServiceProvider.GetRequiredService<ISearchIndex<TestDocument>>();
        var sameScopeAgain = scopeA.ServiceProvider.GetRequiredService<ISearchIndex<TestDocument>>();

        first.Should().NotBeSameAs(second);
        first.Should().BeSameAs(sameScopeAgain);
    }

    [Fact]
    public void AddIndex_Resolves_IAnalyticsSearch_AsScoped()
    {
        var provider = BuildProviderWithOneIndex();

        using var scopeA = provider.CreateScope();
        using var scopeB = provider.CreateScope();
        var first = scopeA.ServiceProvider.GetRequiredService<IAnalyticsSearch<TestDocument>>();
        var second = scopeB.ServiceProvider.GetRequiredService<IAnalyticsSearch<TestDocument>>();

        first.Should().NotBeSameAs(second);
    }

    [Fact]
    public void AddIndex_Resolves_ICursorSearch_AsScoped()
    {
        var provider = BuildProviderWithOneIndex();

        using var scopeA = provider.CreateScope();
        using var scopeB = provider.CreateScope();
        var first = scopeA.ServiceProvider.GetRequiredService<ICursorSearch<TestDocument>>();
        var second = scopeB.ServiceProvider.GetRequiredService<ICursorSearch<TestDocument>>();

        first.Should().NotBeSameAs(second);
    }

    [Fact]
    public void ElasticsearchClient_Resolves_AsSingleton_SameInstanceAcrossResolutions()
    {
        var provider = BuildProviderWithOneIndex();

        var first = provider.GetRequiredService<ElasticsearchClient>();
        var second = provider.GetRequiredService<ElasticsearchClient>();

        first.Should().BeSameAs(second);
    }

    [Fact]
    public void ISearchIndexProvisioner_Resolves_AsSingleton()
    {
        var provider = BuildProviderWithOneIndex();

        var first = provider.GetRequiredService<ISearchIndexProvisioner>();
        var second = provider.GetRequiredService<ISearchIndexProvisioner>();

        first.Should().BeSameAs(second);
    }

    [Fact]
    public void ISearchProviderDescriptor_Resolves_AsSingleton()
    {
        var provider = BuildProviderWithOneIndex();

        var first = provider.GetRequiredService<ISearchProviderDescriptor>();
        var second = provider.GetRequiredService<ISearchProviderDescriptor>();

        first.Should().BeSameAs(second);
        first.ProviderName.Should().Be("elasticsearch");
        // AddIndex(readAlias, writeAlias, ...) records the definition under the READ alias — the
        // name callers query and validate SearchRequests against.
        first.RegisteredIndexes.Should().Contain("products-read");
    }

    [Fact]
    public void IElasticSearchRawClientAccessor_IsNotResolvable_WithoutAllowRawClientAccess()
    {
        var provider = BuildProviderWithOneIndex();

        var accessor = provider.GetService<IElasticSearchRawClientAccessor>();

        accessor.Should().BeNull();
    }

    [Fact]
    public void IElasticSearchRawClientAccessor_IsResolvable_WithAllowRawClientAccess()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IClock>(new FakeClock());
        services.AddSharedKernelElasticSearchSearch(BuildValidConfig())
            .AddIndex<TestDocument>("products-read", "products-write", b => b.Field("name", SearchFieldKind.Text, searchable: true))
            .AllowRawClientAccess()
            .Build();
        var provider = services.BuildServiceProvider();

        var accessor = provider.GetService<IElasticSearchRawClientAccessor>();

        accessor.Should().NotBeNull();
    }

    [Fact]
    public void MissingNodes_ThrowsOptionsValidationException_OnFirstAccess()
    {
        var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Search:ElasticSearch:ValidateEngineVersionOnStart"] = "false",
        });

        var act = () => provider.GetRequiredService<IOptions<ElasticSearchOptions>>().Value;

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void ValidConfig_BindsWithoutThrowing()
    {
        var provider = BuildProvider(BuildValidConfigDictionary());

        var options = provider.GetRequiredService<IOptions<ElasticSearchOptions>>().Value;

        options.Nodes.Should().ContainSingle().Which.Should().Be("http://localhost:9200");
    }

    [Fact]
    public void SectionName_MatchesDocumentedConfigurationPath()
    {
        ElasticSearchOptions.SectionName.Should().Be("Search:ElasticSearch");
    }

    [Fact]
    public void OmittingWithSourceSerializerContext_Logs9221Warning_AtFirstClientResolution()
    {
        var services = new ServiceCollection();
        services.AddInMemoryLoggerFactory();
        services.AddSingleton<IClock>(new FakeClock());
        services.AddSharedKernelElasticSearchSearch(BuildValidConfig())
            .AddIndex<TestDocument>("products-read", "products-write", b => b.Field("name", SearchFieldKind.Text, searchable: true))
            .Build();
        var provider = services.BuildServiceProvider();

        _ = provider.GetRequiredService<ElasticsearchClient>();

        var loggerFactory = (InMemoryLoggerFactory)provider.GetRequiredService<ILoggerFactory>();
        var allRecords = loggerFactory.Loggers.Values.SelectMany(l => l.Records).ToList();
        allRecords.ShouldHaveLogged(new EventId(9221), LogLevel.Warning);
    }

    [Fact]
    public void AllowInvalidCertificates_Logs9217Warning_AtFirstClientResolution()
    {
        var services = new ServiceCollection();
        services.AddInMemoryLoggerFactory();
        services.AddSingleton<IClock>(new FakeClock());
        var configValues = BuildValidConfigDictionary();
        configValues["Search:ElasticSearch:AllowInvalidCertificates"] = "true";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(configValues).Build();

        services.AddSharedKernelElasticSearchSearch(configuration)
            .AddIndex<TestDocument>("products-read", "products-write", b => b.Field("name", SearchFieldKind.Text, searchable: true))
            .Build();
        var provider = services.BuildServiceProvider();

        _ = provider.GetRequiredService<ElasticsearchClient>();

        var loggerFactory = (InMemoryLoggerFactory)provider.GetRequiredService<ILoggerFactory>();
        var allRecords = loggerFactory.Loggers.Values.SelectMany(l => l.Records).ToList();
        allRecords.ShouldHaveLogged(new EventId(9217), LogLevel.Warning);
    }

    [Fact]
    public void AllowRawClientAccess_Logs9216Warning_AtFirstAccessorResolution()
    {
        var services = new ServiceCollection();
        services.AddInMemoryLoggerFactory();
        services.AddSingleton<IClock>(new FakeClock());
        services.AddSharedKernelElasticSearchSearch(BuildValidConfig())
            .AddIndex<TestDocument>("products-read", "products-write", b => b.Field("name", SearchFieldKind.Text, searchable: true))
            .AllowRawClientAccess()
            .Build();
        var provider = services.BuildServiceProvider();

        _ = provider.GetRequiredService<IElasticSearchRawClientAccessor>();

        var loggerFactory = (InMemoryLoggerFactory)provider.GetRequiredService<ILoggerFactory>();
        var allRecords = loggerFactory.Loggers.Values.SelectMany(l => l.Records).ToList();
        allRecords.ShouldHaveLogged(new EventId(9216), LogLevel.Warning);
    }

    private static IServiceProvider BuildProviderWithOneIndex()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IClock>(new FakeClock());
        services.AddSharedKernelElasticSearchSearch(BuildValidConfig())
            .AddIndex<TestDocument>("products-read", "products-write", b => b.Field("name", SearchFieldKind.Text, searchable: true))
            .Build();

        return services.BuildServiceProvider();
    }

    private static IServiceProvider BuildProvider(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IClock>(new FakeClock());

        services.AddSharedKernelElasticSearchSearch(configuration);

        return services.BuildServiceProvider();
    }

    private static Dictionary<string, string?> BuildValidConfigDictionary() => new()
    {
        ["Search:ElasticSearch:Nodes:0"] = "http://localhost:9200",
        ["Search:ElasticSearch:ValidateEngineVersionOnStart"] = "false",
    };

    private static IConfiguration BuildValidConfig() =>
        new ConfigurationBuilder().AddInMemoryCollection(BuildValidConfigDictionary()).Build();
}
