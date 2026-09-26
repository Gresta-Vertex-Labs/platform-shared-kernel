using SharedKernel.Application.Messaging;
using SharedKernel.Core.Extensions;
using SharedKernel.Primitives.Results;
using SharedKernel.Storage;

namespace DocumentsApi.Features.Files;

/// <summary>
/// Opens a file for streaming. The query returns the open <see cref="FileDownload"/> — the endpoint streams it to the
/// response and disposes it — rather than the bytes, so nothing is buffered.
/// </summary>
/// <param name="Store">The store.</param>
/// <param name="Key">The key, relative to the store.</param>
/// <param name="Options">The byte range and <c>If-Match</c> ETag, read from the request.</param>
public sealed record DownloadFile(StoreAddress Store, string Key, FileDownloadOptions Options) : IQuery<FileDownload>;

public sealed class DownloadFileHandler(IFileStorageFactory factory) : IQueryHandler<DownloadFile, FileDownload>
{
    public Task<Result<FileDownload>> Handle(DownloadFile query, CancellationToken cancellationToken) =>
        Stores.Resolve(factory, query.Store)
            .Bind(files => files.DownloadAsync(query.Key, query.Options, cancellationToken));
}
