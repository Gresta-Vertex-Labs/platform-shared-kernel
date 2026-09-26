using System.Runtime.CompilerServices;
using SharedKernel.Application.Messaging;
using SharedKernel.Application.Streaming;
using SharedKernel.Execution.Tenancy;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.ElasticSearch.Cursors;

namespace CatalogApi.Features.BackOffice;

/// <summary>One order line of the stream.</summary>
public sealed record OrderLineSummary(string DocumentId, string ProductName);

/// <summary>
/// The streaming shape of deep pagination, as a stream query (<c>ISender.CreateStream</c>) — no cursor bookkeeping at
/// the call site at all. ElasticSearch-exclusive.
/// </summary>
public sealed record StreamOrderLines(TenantId TenantId) : IStreamQuery<OrderLineSummary>;

public sealed class StreamOrderLinesHandler(ICursorSearch<OrderLineDocument> cursor)
    : IStreamQueryHandler<StreamOrderLines, OrderLineSummary>
{
    public async IAsyncEnumerable<OrderLineSummary> Handle(
        StreamOrderLines query,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var hit in cursor.StreamAsync(
            new SearchRequest { PageSize = 4 },
            TenantScope.For(query.TenantId),
            TimeSpan.FromMinutes(1),
            cancellationToken))
        {
            yield return new OrderLineSummary(hit.Document.DocumentId, hit.Document.ProductName);
        }
    }
}
