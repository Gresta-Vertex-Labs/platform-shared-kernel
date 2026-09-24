using DocumentsApi.Features.Links;
using MediatR;
using SharedKernel.Presentation.WebApi;
using SharedKernel.Storage;

namespace DocumentsApi;

/// <summary>
/// Presigned transfers: the service only signs, and the client moves the bytes directly with the provider. Each endpoint
/// sends a command (<c>Features/Links</c>); every expiry is capped by the store's MaxPresignExpiry.
/// </summary>
public sealed class LinkEndpoints : IEndpointModule
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/links/{store}/download", (string store, DownloadLinkRequest body, HttpRequest request, ISender sender, CancellationToken ct) =>
            sender.Send(
                    new CreateDownloadLink(
                        StoreAddress.For(store, request),
                        body.Key,
                        new PresignedDownloadOptions
                        {
                            Expiry = TimeSpan.FromSeconds(body.ExpirySeconds),
                            ContentDisposition = body.FileName is null ? null : $"attachment; filename=\"{body.FileName}\"",
                        }),
                    ct)
                .ToOk());

        // One known file: the client PUTs it with every returned header.
        app.MapPost("/links/{store}/upload", (string store, UploadLinkRequest body, HttpRequest request, ISender sender, CancellationToken ct) =>
            sender.Send(
                    new CreateUploadLink(
                        StoreAddress.For(store, request),
                        body.Key,
                        new PresignedUploadOptions
                        {
                            Expiry = TimeSpan.FromSeconds(body.ExpirySeconds),
                            ContentType = body.ContentType,
                            CreateOnly = body.CreateOnly,
                        }),
                    ct)
                .ToOk());

        // A browser form: the provider enforces the size range and content type.
        app.MapPost("/links/{store}/form", (string store, UploadFormRequest body, HttpRequest request, ISender sender, CancellationToken ct) =>
            sender.Send(
                    new CreateUploadForm(
                        StoreAddress.For(store, request),
                        body.Key,
                        new PresignedPostOptions
                        {
                            Expiry = TimeSpan.FromSeconds(body.ExpirySeconds),
                            MaxSize = body.MaxSize,
                            ContentType = body.ContentType,
                        }),
                    ct)
                .ToOk());

        // Very large files: start, one URL per part, complete (or abort).
        app.MapPost("/multipart/{store}/start", (string store, StartMultipartRequest body, HttpRequest request, ISender sender, CancellationToken ct) =>
            sender.Send(new StartMultipartUpload(StoreAddress.For(store, request), body.Key, body.ContentType), ct).ToOk());

        app.MapPost("/multipart/{store}/part-url", (string store, PartUrlRequest body, HttpRequest request, ISender sender, CancellationToken ct) =>
            sender.Send(new CreatePartUploadLink(StoreAddress.For(store, request), body.Upload, body.PartNumber, TimeSpan.FromSeconds(body.ExpirySeconds)), ct).ToOk());

        app.MapPost("/multipart/{store}/complete", (string store, CompleteMultipartRequest body, HttpRequest request, ISender sender, CancellationToken ct) =>
            sender.Send(new CompleteMultipartUpload(StoreAddress.For(store, request), body.Upload, body.Parts), ct).ToOk());

        app.MapPost("/multipart/{store}/abort", (string store, MultipartUpload upload, HttpRequest request, ISender sender, CancellationToken ct) =>
            sender.Send(new AbortMultipartUpload(StoreAddress.For(store, request), upload), ct).ToNoContent());
    }
}

public sealed record DownloadLinkRequest(string Key, int ExpirySeconds, string? FileName = null);

public sealed record UploadLinkRequest(string Key, string ContentType, int ExpirySeconds, bool CreateOnly = false);

public sealed record UploadFormRequest(string Key, string ContentType, long MaxSize, int ExpirySeconds);

public sealed record StartMultipartRequest(string Key, string? ContentType = null);

public sealed record PartUrlRequest(MultipartUpload Upload, int PartNumber, int ExpirySeconds);

public sealed record CompleteMultipartRequest(MultipartUpload Upload, IReadOnlyCollection<UploadedPart> Parts);
