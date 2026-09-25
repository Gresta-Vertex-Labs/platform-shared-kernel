using SharedKernel.Application;
using SharedKernel.Core.Extensions;
using SharedKernel.Primitives.Results;
using SharedKernel.Storage;

namespace DocumentsApi.Features.Files;

/// <summary>Deletes a file; with <paramref name="IfMatch"/>, only the version that ETag names.</summary>
/// <param name="Store">The store.</param>
/// <param name="Key">The key, relative to the store.</param>
/// <param name="IfMatch">The ETag the request's <c>If-Match</c> names, or <see langword="null"/>.</param>
public sealed record DeleteFile(StoreAddress Store, string Key, string? IfMatch) : ICommand;

public sealed class DeleteFileHandler(IFileStorageFactory factory) : ICommandHandler<DeleteFile>
{
    public Task<Result> Handle(DeleteFile command, CancellationToken cancellationToken) =>
        Stores.Resolve(factory, command.Store)
            .Bind(files => files.DeleteAsync(command.Key, new FileDeleteOptions { IfMatch = command.IfMatch }, cancellationToken));
}
