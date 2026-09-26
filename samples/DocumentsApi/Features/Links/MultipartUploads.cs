using SharedKernel.Application.Messaging;
using SharedKernel.Core.Extensions;
using SharedKernel.Primitives.Results;
using SharedKernel.Storage;

namespace DocumentsApi.Features.Links;

// Very large files go straight to the provider in parts: start, one signed URL per part, then complete (or abort).
// The four steps of one flow live together.

/// <summary>Starts a multipart upload.</summary>
/// <param name="Store">The store.</param>
/// <param name="Key">The key, relative to the store.</param>
/// <param name="ContentType">The file's content type, when known.</param>
public sealed record StartMultipartUpload(StoreAddress Store, string Key, string? ContentType) : ICommand<MultipartUpload>;

public sealed class StartMultipartUploadHandler(IFileStorageFactory factory) : ICommandHandler<StartMultipartUpload, MultipartUpload>
{
    public Task<Result<MultipartUpload>> Handle(StartMultipartUpload command, CancellationToken cancellationToken) =>
        Stores.Resolve(factory, command.Store)
            .Bind(files => files.StartMultipartUploadAsync(
                command.Key, new MultipartUploadOptions { ContentType = command.ContentType }, cancellationToken));
}

/// <summary>Signs the PUT of one part.</summary>
/// <param name="Store">The store.</param>
/// <param name="Upload">The upload the part belongs to.</param>
/// <param name="PartNumber">The part's number, from 1.</param>
/// <param name="Expiry">How long the URL is valid (capped by the store's MaxPresignExpiry).</param>
public sealed record CreatePartUploadLink(StoreAddress Store, MultipartUpload Upload, int PartNumber, TimeSpan Expiry) : ICommand<PresignedRequest>;

public sealed class CreatePartUploadLinkHandler(IFileStorageFactory factory) : ICommandHandler<CreatePartUploadLink, PresignedRequest>
{
    public Task<Result<PresignedRequest>> Handle(CreatePartUploadLink command, CancellationToken cancellationToken) =>
        Stores.Resolve(factory, command.Store)
            .Bind(files => files.CreateUploadPartUrlAsync(command.Upload, command.PartNumber, command.Expiry, cancellationToken));
}

/// <summary>Completes an upload from its uploaded parts.</summary>
/// <param name="Store">The store.</param>
/// <param name="Upload">The upload.</param>
/// <param name="Parts">The parts the client uploaded, with their ETags.</param>
public sealed record CompleteMultipartUpload(StoreAddress Store, MultipartUpload Upload, IReadOnlyCollection<UploadedPart> Parts) : ICommand<FileReference>;

public sealed class CompleteMultipartUploadHandler(IFileStorageFactory factory) : ICommandHandler<CompleteMultipartUpload, FileReference>
{
    public Task<Result<FileReference>> Handle(CompleteMultipartUpload command, CancellationToken cancellationToken) =>
        Stores.Resolve(factory, command.Store)
            .Bind(files => files.CompleteMultipartUploadAsync(command.Upload, command.Parts, cancellationToken: cancellationToken));
}

/// <summary>Aborts an upload, discarding its parts.</summary>
/// <param name="Store">The store.</param>
/// <param name="Upload">The upload.</param>
public sealed record AbortMultipartUpload(StoreAddress Store, MultipartUpload Upload) : ICommand;

public sealed class AbortMultipartUploadHandler(IFileStorageFactory factory) : ICommandHandler<AbortMultipartUpload>
{
    public Task<Result> Handle(AbortMultipartUpload command, CancellationToken cancellationToken) =>
        Stores.Resolve(factory, command.Store)
            .Bind(files => files.AbortMultipartUploadAsync(command.Upload, cancellationToken));
}
