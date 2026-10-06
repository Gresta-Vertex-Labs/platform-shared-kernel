using System.Runtime.CompilerServices;
using SharedKernel.Application.Messaging;
using SharedKernel.Application.Streaming;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Models;

namespace CatalogApi.Features.Storefront;

/// <summary>One product of the export.</summary>
public sealed record ProductSummary(string DocumentId, string Name, double Price);

/// <summary>
/// The corpus walk, as a stream query (<c>ISender.CreateStream</c>). Not <c>Result</c>-wrapped — the domain's one
/// documented exception to the Result-first rule, following the 06.Persistence/08.Storage streaming precedent.
/// </summary>
public sealed record ExportProducts(TenantId TenantId) : IStreamQuery<ProductSummary>;

public sealed class ExportProductsHandler(ISearchIndex<ProductDocument> index) : IStreamQueryHandler<ExportProducts, ProductSummary>
{
    public async IAsyncEnumerable<ProductSummary> Handle(
        ExportProducts query,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var product in index.EnumerateAsync(
            filter: null, TenantScope.For(query.TenantId), batchSize: 4, cancellationToken))
        {
            yield return new ProductSummary(product.DocumentId, product.Name, product.Price);
        }
    }
}
