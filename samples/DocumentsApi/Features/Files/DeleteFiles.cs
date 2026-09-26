using SharedKernel.Application.Messaging;
using SharedKernel.Core.Extensions;
using SharedKernel.Primitives.Results;
using SharedKernel.Storage;

namespace DocumentsApi.Features.Files;

/// <summary>Deletes several files in one batch, reporting an outcome per key.</summary>
/// <param name="Store">The store.</param>
/// <param name="Keys">The keys, relative to the store.</param>
public sealed record DeleteFiles(StoreAddress Store, IReadOnlyCollection<string> Keys) : ICommand<BatchDeleteResult>;

public sealed class DeleteFilesHandler(IFileStorageFactory factory) : ICommandHandler<DeleteFiles, BatchDeleteResult>
{
    public Task<Result<BatchDeleteResult>> Handle(DeleteFiles command, CancellationToken cancellationToken) =>
        Stores.Resolve(factory, command.Store)
            .Bind(files => files.DeleteManyAsync(command.Keys, cancellationToken));
}
