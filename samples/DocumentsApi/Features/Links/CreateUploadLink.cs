using SharedKernel.Application.Messaging;
using SharedKernel.Core.Extensions;
using SharedKernel.Primitives.Results;
using SharedKernel.Storage;

namespace DocumentsApi.Features.Links;

/// <summary>Signs a PUT of one known file; the client sends it with every returned header.</summary>
/// <param name="Store">The store.</param>
/// <param name="Key">The key, relative to the store.</param>
/// <param name="Options">The expiry, content type and create-only condition.</param>
public sealed record CreateUploadLink(StoreAddress Store, string Key, PresignedUploadOptions Options) : ICommand<PresignedRequest>;

public sealed class CreateUploadLinkHandler(IFileStorageFactory factory) : ICommandHandler<CreateUploadLink, PresignedRequest>
{
    public Task<Result<PresignedRequest>> Handle(CreateUploadLink command, CancellationToken cancellationToken) =>
        Stores.Resolve(factory, command.Store)
            .Bind(files => files.CreateUploadUrlAsync(command.Key, command.Options, cancellationToken));
}
