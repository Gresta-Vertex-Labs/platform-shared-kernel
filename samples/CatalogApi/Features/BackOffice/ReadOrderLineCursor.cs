using SharedKernel.Application;
using SharedKernel.Core.Extensions;
using SharedKernel.Primitives.Results;
using SharedKernel.Search.Abstractions.Models;
using SharedKernel.Search.ElasticSearch.Cursors;

namespace CatalogApi.Features.BackOffice;

/// <summary>
/// Deep pagination past the MaxTotalHits ceiling, via point-in-time + search_after — ElasticSearch-exclusive.
/// Meilisearch has no equivalent; its callers walk the corpus with EnumerateAsync instead.
/// </summary>
/// <remarks>
/// Two shapes ship. StreamAsync (<see cref="StreamOrderLines"/>) is the one most exports want — an IAsyncEnumerable
/// that opens, walks and closes the point-in-time for you. The open/read/close trio used here exists for an export that
/// must checkpoint its cursor across process restarts, which IAsyncEnumerable cannot express; a stateless HTTP endpoint
/// is exactly that case.
/// </remarks>
public sealed record ReadOrderLineCursor(string TenantId, int Size) : IQuery<CursorPage<OrderLineDocument>>;

public sealed class ReadOrderLineCursorHandler(ICursorSearch<OrderLineDocument> cursor)
    : IQueryHandler<ReadOrderLineCursor, CursorPage<OrderLineDocument>>
{
    public Task<Result<CursorPage<OrderLineDocument>>> Handle(ReadOrderLineCursor query, CancellationToken cancellationToken) =>
        cursor.OpenCursorAsync(
                new SearchRequest { PageSize = query.Size <= 0 ? 5 : query.Size },
                TenantScope.Of(query.TenantId),
                TimeSpan.FromMinutes(1),
                cancellationToken)
            .Bind(async opened =>
            {
                try
                {
                    return await cursor.ReadCursorAsync(opened, cancellationToken);
                }
                finally
                {
                    // Always release the point-in-time; leaking one pins segments on the cluster.
                    await cursor.CloseCursorAsync(opened, CancellationToken.None);
                }
            });
}
