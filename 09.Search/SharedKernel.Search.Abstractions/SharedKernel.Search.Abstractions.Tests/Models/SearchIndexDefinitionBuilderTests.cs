using FluentAssertions;
using SharedKernel.Search.Abstractions.Models;

namespace SharedKernel.Search.Abstractions.Tests.Models;

/// <summary>
/// T-07 (part 2): <see cref="SearchIndexDefinitionBuilder"/> tests — <c>Build()</c> returns
/// <c>InvalidIndexDefinition</c> for a duplicate field name, an empty field name, and a
/// <see cref="SearchIndexDefinition.TenantField"/> naming a field that is not declared
/// <see cref="SearchFieldDefinition.Filterable"/>.
/// </summary>
public sealed class SearchIndexDefinitionBuilderTests
{
    [Fact]
    public void Build_WithDuplicateFieldName_ReturnsInvalidIndexDefinition()
    {
        var result = new SearchIndexDefinitionBuilder("products")
            .Field("name", SearchFieldKind.Text, searchable: true)
            .Field("name", SearchFieldKind.Keyword, filterable: true)
            .Build();

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("search.invalid_index_definition");
    }

    [Fact]
    public void Build_WithEmptyFieldName_ThrowsArgumentException()
    {
        // SearchIndexDefinitionBuilder.Field guards its own name parameter eagerly with
        // ArgumentException.ThrowIfNullOrWhiteSpace, mirroring TenantScope.For —
        // this is a programming error caught at first test run, not a Result-valued expected failure.
        var builder = new SearchIndexDefinitionBuilder("products");

        var act = () => builder.Field("", SearchFieldKind.Text);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Build_WithTenantFieldNamingAFieldNotDeclaredFilterable_ReturnsInvalidIndexDefinition()
    {
        var result = new SearchIndexDefinitionBuilder("products")
            .TenantField("tenantId")
            .Field("tenantId", SearchFieldKind.Keyword, filterable: false)
            .Build();

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("search.invalid_index_definition");
    }

    [Fact]
    public void Build_WithTenantFieldNamingAnUndeclaredField_ReturnsInvalidIndexDefinition()
    {
        var result = new SearchIndexDefinitionBuilder("products")
            .TenantField("tenantId")
            .Field("name", SearchFieldKind.Text, searchable: true)
            .Build();

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("search.invalid_index_definition");
    }

    [Fact]
    public void Build_WithTenantFieldDeclaredFilterable_Succeeds()
    {
        var result = new SearchIndexDefinitionBuilder("products")
            .TenantField("tenantId")
            .Field("tenantId", SearchFieldKind.Keyword, filterable: true)
            .Build();

        result.IsSuccess.Should().BeTrue();
        result.Value.TenantField.Should().Be("tenantId");
    }

    [Fact]
    public void Build_WithoutExplicitPrimaryKey_UsesWellKnownDefault()
    {
        var result = new SearchIndexDefinitionBuilder("products")
            .Field("name", SearchFieldKind.Text, searchable: true)
            .Build();

        result.IsSuccess.Should().BeTrue();
        result.Value.PrimaryKeyField.Should().Be(SharedKernel.Search.Abstractions.Constants.SearchWellKnown.DefaultPrimaryKeyField);
    }

    [Fact]
    public void Build_WithExplicitPrimaryKeyAndCeilings_AppliesThem()
    {
        var result = new SearchIndexDefinitionBuilder("products")
            .PrimaryKey("sku")
            .MaxTotalHits(5000)
            .MaxFacetValues(200)
            .Field("name", SearchFieldKind.Text, searchable: true)
            .Build();

        result.IsSuccess.Should().BeTrue();
        result.Value.PrimaryKeyField.Should().Be("sku");
        result.Value.MaxTotalHits.Should().Be(5000);
        result.Value.MaxFacetValues.Should().Be(200);
    }

    [Fact]
    public void Constructor_WithNullOrWhiteSpaceName_ThrowsArgumentException()
    {
        var act = () => new SearchIndexDefinitionBuilder("   ");

        act.Should().Throw<ArgumentException>();
    }
}
