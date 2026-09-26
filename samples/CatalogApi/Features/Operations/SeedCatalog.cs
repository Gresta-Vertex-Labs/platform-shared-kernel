using SharedKernel.Application.Messaging;
using SharedKernel.Core.Extensions;
using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Abstractions;
using SharedKernel.Search.Abstractions.Models;

namespace CatalogApi.Features.Operations;

/// <summary>How many documents of one kind were submitted and indexed.</summary>
public sealed record SeedCount(int Submitted, int Succeeded);

/// <summary>What <see cref="SeedCatalog"/> wrote.</summary>
public sealed record SeedReport(SeedCount Products, SeedCount OrderLines);

/// <summary>
/// Seeds both engines. SearchWriteConsistency.Searchable blocks until the write is visible, so the read endpoints can be
/// called immediately afterwards without a sleep. The order lines are written only once the products are; the first
/// failure is the answer.
/// </summary>
public sealed record SeedCatalog : ICommand<SeedReport>;

public sealed class SeedCatalogHandler(ISearchIndex<ProductDocument> products, ISearchIndex<OrderLineDocument> orderLines)
    : ICommandHandler<SeedCatalog, SeedReport>
{
    public Task<Result<SeedReport>> Handle(SeedCatalog command, CancellationToken cancellationToken) =>
        products.IndexManyAsync(SeedData.Products, SearchWriteConsistency.Searchable, cancellationToken)
            .Bind(productWrite => orderLines.IndexManyAsync(SeedData.OrderLines, SearchWriteConsistency.Searchable, cancellationToken)
                .Map(orderWrite => new SeedReport(
                    new SeedCount(SeedData.Products.Count, productWrite.SucceededCount),
                    new SeedCount(SeedData.OrderLines.Count, orderWrite.SucceededCount))));
}
