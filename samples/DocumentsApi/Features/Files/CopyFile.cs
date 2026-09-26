using SharedKernel.Application.Messaging;
using SharedKernel.Core.Extensions;
using SharedKernel.Primitives.Results;
using SharedKernel.Storage;

namespace DocumentsApi.Features.Files;

/// <summary>
/// Copies a file within a store or into another one — server-side when both share a connection. A create-only copy
/// asks in its body, not in a header, so an existing destination is a 409.
/// </summary>
/// <param name="From">The source store.</param>
/// <param name="FromKey">The source key.</param>
/// <param name="To">The destination store.</param>
/// <param name="ToKey">The destination key.</param>
/// <param name="CreateOnly">Refuse to overwrite an existing destination.</param>
public sealed record CopyFile(StoreAddress From, string FromKey, StoreAddress To, string ToKey, bool CreateOnly) : ICommand<FileReference>;

public sealed class CopyFileHandler(IFileStorageFactory factory) : ICommandHandler<CopyFile, FileReference>
{
    public Task<Result<FileReference>> Handle(CopyFile command, CancellationToken cancellationToken) =>
        Stores.Resolve(factory, command.From)
            .Bind(source => Stores.Resolve(factory, command.To)
                .Bind(destination => source.CopyToAsync(
                    command.FromKey,
                    destination,
                    command.ToKey,
                    new FileCopyOptions { Condition = command.CreateOnly ? WriteCondition.IfNotExists : null },
                    cancellationToken)));
}
