using SharedKernel.Application;
using SharedKernel.Core.Extensions;
using SharedKernel.Primitives.Results;
using SharedKernel.Storage;

namespace DocumentsApi.Features.Files;

/// <summary>Streams <paramref name="Content"/> into the store under <paramref name="Key"/>, never buffering it.</summary>
/// <param name="Store">The store.</param>
/// <param name="Key">The key, relative to the store.</param>
/// <param name="Content">The bytes — the request body, read as it arrives.</param>
/// <param name="Options">Content headers, metadata, checksum and write condition, read from the request.</param>
public sealed record UploadFile(StoreAddress Store, string Key, Stream Content, FileUploadOptions Options) : ICommand<FileReference>;

public sealed class UploadFileHandler(IFileStorageFactory factory) : ICommandHandler<UploadFile, FileReference>
{
    public Task<Result<FileReference>> Handle(UploadFile command, CancellationToken cancellationToken) =>
        Stores.Resolve(factory, command.Store)
            .Bind(files => files.UploadAsync(command.Key, command.Content, command.Options, cancellationToken));
}
