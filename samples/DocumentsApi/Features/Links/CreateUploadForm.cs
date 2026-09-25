using SharedKernel.Application;
using SharedKernel.Core.Extensions;
using SharedKernel.Primitives.Results;
using SharedKernel.Storage;

namespace DocumentsApi.Features.Links;

/// <summary>Signs a browser upload form; the provider enforces the size range and content type.</summary>
/// <param name="Store">The store.</param>
/// <param name="Key">The key, relative to the store.</param>
/// <param name="Options">The expiry, maximum size and content type.</param>
public sealed record CreateUploadForm(StoreAddress Store, string Key, PresignedPostOptions Options) : ICommand<PresignedPost>;

public sealed class CreateUploadFormHandler(IFileStorageFactory factory) : ICommandHandler<CreateUploadForm, PresignedPost>
{
    public Task<Result<PresignedPost>> Handle(CreateUploadForm command, CancellationToken cancellationToken) =>
        Stores.Resolve(factory, command.Store)
            .Bind(files => files.CreateUploadFormAsync(command.Key, command.Options, cancellationToken));
}
