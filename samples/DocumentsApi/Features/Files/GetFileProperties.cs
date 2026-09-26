using SharedKernel.Application.Messaging;
using SharedKernel.Core.Extensions;
using SharedKernel.Primitives.Results;
using SharedKernel.Storage;

namespace DocumentsApi.Features.Files;

/// <summary>Reads a file's properties (size, content type, ETag, metadata) without its content.</summary>
/// <param name="Store">The store.</param>
/// <param name="Key">The key, relative to the store.</param>
public sealed record GetFileProperties(StoreAddress Store, string Key) : IQuery<FileProperties>;

public sealed class GetFilePropertiesHandler(IFileStorageFactory factory) : IQueryHandler<GetFileProperties, FileProperties>
{
    public Task<Result<FileProperties>> Handle(GetFileProperties query, CancellationToken cancellationToken) =>
        Stores.Resolve(factory, query.Store)
            .Bind(files => files.GetPropertiesAsync(query.Key, cancellationToken));
}
