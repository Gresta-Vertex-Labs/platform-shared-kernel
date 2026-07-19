using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Meilisearch.Extensions;
using SharedKernel.Search.Meilisearch.Instant;
using SharedKernel.Search.Meilisearch.Options;
using SharedKernel.Search.Meilisearch.Raw;
using SharedKernel.Search.Meilisearch.Tenancy;
using SharedKernel.Testing.Clocks;

namespace SharedKernel.Search.Meilisearch.Tests.Extensions;

/// <summary>
/// T-12: DI registration and options-validation tests (container-free, <see cref="ServiceCollection"/>
/// plus <see cref="ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(IServiceCollection)"/>).
/// </summary>
public sealed class MeilisearchServiceCollectionExtensionsTests
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
    public void AddIndex_Resolves_IInstantSearch_AsScoped()
    {
        var provider = BuildProviderWithOneIndex();

        using var scopeA = provider.CreateScope();
        using var scopeB = provider.CreateScope();
        var first = scopeA.ServiceProvider.GetRequiredService<IInstantSearch<TestDocument>>();
        var second = scopeB.ServiceProvider.GetRequiredService<IInstantSearch<TestDocument>>();

        first.Should().NotBeSameAs(second);
    }

    [Fact]
    public void MeilisearchClient_Resolves_AsSingleton_SameInstanceAcrossResolutions()
    {
        var provider = BuildProviderWithOneIndex();

        var first = provider.GetRequiredService<global::Meilisearch.MeilisearchClient>();
        var second = provider.GetRequiredService<global::Meilisearch.MeilisearchClient>();

        first.Should().BeSameAs(second);
    }

    [Fact]
    public void ISearchIndexProvisioner_Resolves_AsSingleton_SameInstanceAcrossResolutions()
    {
        var provider = BuildProviderWithOneIndex();

        var first = provider.GetRequiredService<ISearchIndexProvisioner>();
        var second = provider.GetRequiredService<ISearchIndexProvisioner>();

        first.Should().BeSameAs(second);
    }

    [Fact]
    public void ISearchProviderDescriptor_Resolves_AsSingleton_SameInstanceAcrossResolutions()
    {
        var provider = BuildProviderWithOneIndex();

        var first = provider.GetRequiredService<ISearchProviderDescriptor>();
        var second = provider.GetRequiredService<ISearchProviderDescriptor>();

        first.Should().BeSameAs(second);
        first.ProviderName.Should().Be("meilisearch");
        first.RegisteredIndexes.Should().Contain("products");
    }

    [Fact]
    public void IMeilisearchRawClientAccessor_IsNotResolvable_WithoutAllowRawClientAccess()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IClock>(new FakeClock());
        services.AddSharedKernelMeilisearchSearch(BuildValidConfig())
            .AddIndex<TestDocument>("products", b => b.Field("name", Abstractions.Models.SearchFieldKind.Text, searchable: true))
            .Build();
        var provider = services.BuildServiceProvider();

        var accessor = provider.GetService<IMeilisearchRawClientAccessor>();

        accessor.Should().BeNull();
    }

    [Fact]
    public void IMeilisearchRawClientAccessor_IsResolvable_WithAllowRawClientAccess()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IClock>(new FakeClock());
        services.AddSharedKernelMeilisearchSearch(BuildValidConfig())
            .AddIndex<TestDocument>("products", b => b.Field("name", Abstractions.Models.SearchFieldKind.Text, searchable: true))
            .AllowRawClientAccess()
            .Build();
        var provider = services.BuildServiceProvider();

        var accessor = provider.GetService<IMeilisearchRawClientAccessor>();

        accessor.Should().NotBeNull();
    }

    [Fact]
    public void ITenantSearchTokenIssuer_IsNotResolvable_WithoutWithTenantTokens()
    {
        var provider = BuildProviderWithOneIndex();

        var issuer = provider.GetService<ITenantSearchTokenIssuer>();

        issuer.Should().BeNull();
    }

    [Fact]
    public void ITenantSearchTokenIssuer_IsResolvable_WithWithTenantTokens()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IClock>(new FakeClock());
        services.AddSharedKernelMeilisearchSearch(BuildValidConfig())
            .AddIndex<TestDocument>("products", b => b.Field("name", Abstractions.Models.SearchFieldKind.Text, searchable: true))
            .WithTenantTokens()
            .Build();
        var provider = services.BuildServiceProvider();

        using var scope = provider.CreateScope();
        var issuer = scope.ServiceProvider.GetService<ITenantSearchTokenIssuer>();

        issuer.Should().NotBeNull();
    }

    [Fact]
    public void MissingUrl_ThrowsOptionsValidationException_OnFirstAccess()
    {
        var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Search:Meilisearch:ApiKey"] = "test-key",
        });

        var act = () => provider.GetRequiredService<IOptions<MeilisearchOptions>>().Value;

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void MissingApiKey_ThrowsOptionsValidationException_OnFirstAccess()
    {
        var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Search:Meilisearch:Url"] = "http://localhost:7700",
        });

        var act = () => provider.GetRequiredService<IOptions<MeilisearchOptions>>().Value;

        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void ValidConfig_BindsWithoutThrowing()
    {
        var provider = BuildProvider(BuildValidConfigDictionary());

        var options = provider.GetRequiredService<IOptions<MeilisearchOptions>>().Value;

        options.Url.Should().Be("http://localhost:7700");
        options.ApiKey.Should().Be("test-master-key");
    }

    [Fact]
    public void SectionName_MatchesDocumentedConfigurationPath()
    {
        MeilisearchOptions.SectionName.Should().Be("Search:Meilisearch");
    }

    private static IServiceProvider BuildProviderWithOneIndex()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IClock>(new FakeClock());
        services.AddSharedKernelMeilisearchSearch(BuildValidConfig())
            .AddIndex<TestDocument>("products", b => b.Field("name", Abstractions.Models.SearchFieldKind.Text, searchable: true))
            .Build();

        return services.BuildServiceProvider();
    }

    private static IServiceProvider BuildProvider(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<IClock>(new FakeClock());

        services.AddSharedKernelMeilisearchSearch(configuration);

        return services.BuildServiceProvider();
    }

    private static Dictionary<string, string?> BuildValidConfigDictionary() => new()
    {
        ["Search:Meilisearch:Url"] = "http://localhost:7700",
        ["Search:Meilisearch:ApiKey"] = "test-master-key",
    };

    private static IConfiguration BuildValidConfig() =>
        new ConfigurationBuilder().AddInMemoryCollection(BuildValidConfigDictionary()).Build();
}
