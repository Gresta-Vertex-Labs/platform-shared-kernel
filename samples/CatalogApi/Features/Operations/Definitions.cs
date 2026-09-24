using SharedKernel.Search.Abstractions.Models;

namespace CatalogApi.Features.Operations;

/// <summary>
/// The index definitions, rebuilt from the same declarations Program.cs registers.
/// </summary>
/// <remarks>
/// A real service would expose the definitions it registered rather than restating them — the provider
/// builders hold them internally. They are restated here only so <c>/ops/provision</c> can run without
/// a registry type this sample does not need for anything else. Keep them in step with Program.cs;
/// <c>/ops/verify</c> will report a fingerprint mismatch if they drift, which is itself a demonstration.
/// </remarks>
public static class Definitions
{
    public static IReadOnlyList<SearchIndexDefinition> All { get; } = BuildAll();

    private static IReadOnlyList<SearchIndexDefinition> BuildAll()
    {
        var products = new SearchIndexDefinitionBuilder(Catalog.ProductsIndex)
            .PrimaryKey(ProductFields.DocumentId)
            .TenantField(ProductFields.TenantId)
            .Field(ProductFields.DocumentId, SearchFieldKind.Keyword, filterable: true)
            .Field(ProductFields.TenantId, SearchFieldKind.Keyword, filterable: true)
            .Field(ProductFields.Name, SearchFieldKind.Text, searchable: true)
            .Field(ProductFields.Description, SearchFieldKind.Text, searchable: true)
            .Field(ProductFields.Brand, SearchFieldKind.Keyword, searchable: true, filterable: true, facetable: true)
            .Field(ProductFields.Category, SearchFieldKind.Keyword, filterable: true, facetable: true, sortable: true)
            .Field(ProductFields.Price, SearchFieldKind.Decimal, filterable: true, sortable: true)
            .Field(ProductFields.InStock, SearchFieldKind.Boolean, filterable: true)
            .Field(ProductFields.Rating, SearchFieldKind.Decimal, filterable: true, sortable: true)
            .Field(ProductFields.ReleasedOn, SearchFieldKind.DateTimeOffset, filterable: true, sortable: true)
            .MaxTotalHits(8)
            .Synonym("rodent", "mouse")
            .Synonym("notepad", "notebook")
            .StopWords("the", "a", "an", "with", "and")
            .Build();

        var orderLines = new SearchIndexDefinitionBuilder(Catalog.OrderLinesRead)
            .PrimaryKey(OrderLineFields.DocumentId)
            .TenantField(OrderLineFields.TenantId)
            .Field(OrderLineFields.DocumentId, SearchFieldKind.Keyword, filterable: true)
            .Field(OrderLineFields.TenantId, SearchFieldKind.Keyword, filterable: true)
            .Field(OrderLineFields.ProductName, SearchFieldKind.Text, searchable: true)
            .Field(OrderLineFields.Category, SearchFieldKind.Keyword, filterable: true, facetable: true)
            .Field(OrderLineFields.Region, SearchFieldKind.Keyword, filterable: true, facetable: true, sortable: true)
            .Field(OrderLineFields.Quantity, SearchFieldKind.Integer, filterable: true, sortable: true)
            .Field(OrderLineFields.Revenue, SearchFieldKind.Decimal, filterable: true, sortable: true)
            .Field(OrderLineFields.OrderedAt, SearchFieldKind.DateTimeOffset, filterable: true, sortable: true)
            .Synonym("rodent", "mouse")
            .StopWords("the", "a", "an", "with", "and")
            .Build();

        if (products.IsFailure || orderLines.IsFailure)
        {
            throw new InvalidOperationException(
                "A sample index definition is invalid: " +
                (products.IsFailure ? products.Error.Message : orderLines.Error.Message));
        }

        return [products.Value, orderLines.Value];
    }
}
