using SharedKernel.Search.Abstractions.Constants;
using SharedKernel.Search.Abstractions.Errors;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Testing.Search;
using Xunit;

namespace SharedKernel.Testing.SelfTests.Search;

/// <summary>
/// Proves <see cref="InMemorySearchProviderDescriptor"/> against
/// <c>ISearchProviderDescriptor</c>'s documented zero-I/O pre-flight-validation contract — no
/// consuming domain has adopted this fake yet, so this self-test is the only behavioral proof
/// today, per the SelfTests routing rule.
/// </summary>
public sealed class InMemorySearchProviderDescriptorTests
{
    [Fact]
    public void Constructor_Default_UsesInMemoryFakeProviderName_NeverARealEngineName()
    {
        var descriptor = new InMemorySearchProviderDescriptor();

        Assert.Equal("in-memory-fake", descriptor.ProviderName);
        Assert.NotEqual(SearchWellKnown.MeilisearchProviderName, descriptor.ProviderName);
        Assert.NotEqual(SearchWellKnown.ElasticSearchProviderName, descriptor.ProviderName);
    }

    [Fact]
    public void Constructor_CustomProviderName_IsUsed()
    {
        var descriptor = new InMemorySearchProviderDescriptor("custom-fake");

        Assert.Equal("custom-fake", descriptor.ProviderName);
    }

    [Fact]
    public void Constructor_NullProviderName_Throws() =>
        Assert.Throws<ArgumentNullException>(() => new InMemorySearchProviderDescriptor(null!));

    [Fact]
    public void MaxTotalHits_And_MaxFacetValues_DefaultToWellKnownDefaults()
    {
        var descriptor = new InMemorySearchProviderDescriptor();

        Assert.Equal(SearchWellKnown.DefaultMaxTotalHits, descriptor.MaxTotalHits);
        Assert.Equal(SearchWellKnown.DefaultMaxFacetValues, descriptor.MaxFacetValues);
    }

    [Fact]
    public void RegisterIndex_PopulatesRegisteredIndexes()
    {
        var descriptor = new InMemorySearchProviderDescriptor();

        descriptor.RegisterIndex("products", BuildDefinition());

        Assert.Contains("products", descriptor.RegisteredIndexes);
    }

    [Fact]
    public void Validate_UnregisteredIndex_ReturnsIndexNotFound()
    {
        var descriptor = new InMemorySearchProviderDescriptor();

        var result = descriptor.Validate("never-registered", SearchRequest.Default);

        Assert.Equal(SearchErrors.IndexNotFound("never-registered"), result.Error);
    }

    [Fact]
    public void Validate_SortOnUndeclaredField_ReturnsFieldNotSortable()
    {
        var descriptor = new InMemorySearchProviderDescriptor();
        descriptor.RegisterIndex("products", BuildDefinition());

        var result = descriptor.Validate("products", SearchRequest.Default with { Sort = [SearchSort.Ascending("Name")] });

        Assert.Equal(SearchErrors.FieldNotSortable("products", "Name"), result.Error);
    }

    [Fact]
    public void Validate_FilterOnUndeclaredField_ReturnsFieldNotFilterable()
    {
        var descriptor = new InMemorySearchProviderDescriptor();
        descriptor.RegisterIndex("products", BuildDefinition());

        var result = descriptor.Validate("products", SearchRequest.Default with { Filter = SearchFilter.Eq("Name", "Widget") });

        Assert.Equal(SearchErrors.FieldNotFilterable("products", "Name"), result.Error);
    }

    [Fact]
    public void Validate_FacetOnUndeclaredField_ReturnsFieldNotFacetable()
    {
        var descriptor = new InMemorySearchProviderDescriptor();
        descriptor.RegisterIndex("products", BuildDefinition());

        var result = descriptor.Validate("products", SearchRequest.Default with { Facets = ["Name"] });

        Assert.Equal(SearchErrors.FieldNotFacetable("products", "Name"), result.Error);
    }

    [Fact]
    public void Validate_OverCeilingPagination_ReturnsPaginationLimitExceeded()
    {
        var descriptor = new InMemorySearchProviderDescriptor("custom-fake") { MaxTotalHits = 5 };
        descriptor.RegisterIndex("products", BuildDefinition());

        var result = descriptor.Validate("products", SearchRequest.Default with { Page = 10, PageSize = 5 });

        Assert.Equal(SearchErrors.PaginationLimitExceeded(10, 5, 5, "custom-fake"), result.Error);
    }

    [Fact]
    public void Validate_LegalRequest_Succeeds()
    {
        var descriptor = new InMemorySearchProviderDescriptor();
        descriptor.RegisterIndex("products", BuildDefinition());

        var result = descriptor.Validate("products", SearchRequest.Default with { Filter = SearchFilter.Eq("Status", "active") });

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Reset_ClearsRegisteredIndexes_ValidateAgainReportsIndexNotFound()
    {
        var descriptor = new InMemorySearchProviderDescriptor();
        descriptor.RegisterIndex("products", BuildDefinition());

        descriptor.Reset();

        Assert.Empty(descriptor.RegisteredIndexes);
        Assert.Equal(SearchErrors.IndexNotFound("products"), descriptor.Validate("products", SearchRequest.Default).Error);
    }

    private static SearchIndexDefinition BuildDefinition() =>
        new SearchIndexDefinitionBuilder("products")
            .Field("Name", SearchFieldKind.Text, searchable: true)
            .Field("Status", SearchFieldKind.Keyword, filterable: true, facetable: true)
            .Build()
            .Value;
}
