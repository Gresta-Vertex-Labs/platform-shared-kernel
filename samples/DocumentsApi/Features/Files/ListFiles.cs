using SharedKernel.Application.Messaging;
using SharedKernel.Core.Extensions;
using SharedKernel.Primitives.Results;
using SharedKernel.Storage;

namespace DocumentsApi.Features.Files;

/// <summary>One page of a store's listing.</summary>
/// <param name="Store">The store.</param>
/// <param name="Request">The prefix, recursion, page size and continuation token.</param>
public sealed record ListFiles(StoreAddress Store, FileListRequest Request) : IQuery<FileListPage>;

public sealed class ListFilesHandler(IFileStorageFactory factory) : IQueryHandler<ListFiles, FileListPage>
{
    public Task<Result<FileListPage>> Handle(ListFiles query, CancellationToken cancellationToken) =>
        Stores.Resolve(factory, query.Store)
            .Bind(files => files.ListPageAsync(query.Request, cancellationToken));
}
