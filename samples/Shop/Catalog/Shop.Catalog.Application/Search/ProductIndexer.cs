using System.Globalization;
using SharedKernel.AI.Abstractions.Abstractions;
using SharedKernel.AI.Abstractions.Models;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Models;
using Shop.Catalog.Domain;

namespace Shop.Catalog.Application.Search;

/// <summary>
/// Writes a product to every read model: the storefront index, the back-office index and the vector collection.
/// Called by the command handlers after a change, inside the command's transaction, so a failed write fails the
/// command and rolls the change back.
/// </summary>
public sealed class ProductIndexer(
    ISearchIndex<ProductDocument> storefront,
    ISearchIndex<ProductAdminDocument> backOffice,
    IVectorCollection<ProductVector> vectors,
    IEmbeddingGenerator embeddings
)
{
    /// <summary>Indexes the product everywhere; the first failure is returned.</summary>
    public async Task<Result> IndexAsync(Product product, CancellationToken cancellationToken)
    {
        string id = product.Id.Value.ToString("D", CultureInfo.InvariantCulture);
        string tenant = product.TenantId.Value.ToString("D", CultureInfo.InvariantCulture);
        double price = (double)product.Price.Amount;

        var stored = await storefront.IndexAsync(
            new ProductDocument
            {
                DocumentId = id,
                TenantId = tenant,
                Sku = product.Sku,
                Name = product.Name,
                Description = product.Description,
                Brand = product.Brand,
                Category = product.Category,
                Price = price,
                Currency = product.Price.Currency.Code,
            },
            SearchWriteConsistency.Searchable,
            cancellationToken
        );
        if (stored.IsFailure)
        {
            return Result.Failure(stored.Error);
        }

        var admin = await backOffice.IndexAsync(
            new ProductAdminDocument
            {
                DocumentId = id,
                TenantId = tenant,
                Sku = product.Sku,
                Name = product.Name,
                Brand = product.Brand,
                Category = product.Category,
                Price = price,
            },
            SearchWriteConsistency.Searchable,
            cancellationToken
        );
        if (admin.IsFailure)
        {
            return Result.Failure(admin.Error);
        }

        var embedded = await embeddings.EmbedAsync(
            $"{product.Name}. {product.Category}. {product.Description}",
            cancellationToken
        );
        if (embedded.IsFailure)
        {
            return Result.Failure(embedded.Error);
        }

        var vector = new ProductVector(
            id,
            embedded.Value.Vector,
            embedded.Value.ModelId,
            new Dictionary<string, VectorValue>
            {
                [ProductVectorFields.TenantId] = VectorValue.From(tenant),
                [ProductVectorFields.Name] = VectorValue.From(product.Name),
                [ProductVectorFields.Category] = VectorValue.From(product.Category),
            }
        );
        var upserted = await vectors.UpsertAsync(
            vector,
            TenantScope.For(product.TenantId),
            cancellationToken
        );
        return upserted.IsFailure ? Result.Failure(upserted.Error) : Result.Success();
    }
}
