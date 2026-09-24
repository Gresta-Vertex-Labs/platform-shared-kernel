using SharedKernel.Application;
using SharedKernel.Core.Extensions;
using SharedKernel.Primitives.Results;
using SharedKernel.Storage;

namespace DocumentsApi.Features.Links;

/// <summary>Signs a GET the client sends to the provider itself.</summary>
/// <param name="Store">The store.</param>
/// <param name="Key">The key, relative to the store.</param>
/// <param name="Options">The expiry (capped by the store's MaxPresignExpiry) and the download's file name.</param>
public sealed record CreateDownloadLink(StoreAddress Store, string Key, PresignedDownloadOptions Options) : ICommand<PresignedRequest>;

public sealed class CreateDownloadLinkHandler(IFileStorageFactory factory) : ICommandHandler<CreateDownloadLink, PresignedRequest>
{
    public Task<Result<PresignedRequest>> Handle(CreateDownloadLink command, CancellationToken cancellationToken) =>
        Stores.Resolve(factory, command.Store)
            .Bind(files => files.CreateDownloadUrlAsync(command.Key, command.Options, cancellationToken));
}
