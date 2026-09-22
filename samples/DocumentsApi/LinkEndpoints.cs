using SharedKernel.Presentation.WebApi.Results;
using SharedKernel.Storage;

namespace DocumentsApi;

/// <summary>
/// Presigned transfers: the service only signs, and the client moves the bytes directly with the provider. Every
/// expiry is capped by the store's MaxPresignExpiry.
/// </summary>
public static class LinkEndpoints
{
    public static void MapLinkEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/links/{store}/download", (string store, DownloadLinkRequest request, HttpContext http, IFileStorageFactory factory, CancellationToken ct) =>
            FileEndpoints.WithStore(factory, store, http, async files =>
                (await files.CreateDownloadUrlAsync(request.Key, new PresignedDownloadOptions
                {
                    Expiry = TimeSpan.FromSeconds(request.ExpirySeconds),
                    ContentDisposition = request.FileName is null ? null : $"attachment; filename=\"{request.FileName}\"",
                }, ct)).ToProblemDetailsResult()));

        // One known file: the client PUTs it with every returned header.
        app.MapPost("/links/{store}/upload", (string store, UploadLinkRequest request, HttpContext http, IFileStorageFactory factory, CancellationToken ct) =>
            FileEndpoints.WithStore(factory, store, http, async files =>
                (await files.CreateUploadUrlAsync(request.Key, new PresignedUploadOptions
                {
                    Expiry = TimeSpan.FromSeconds(request.ExpirySeconds),
                    ContentType = request.ContentType,
                    CreateOnly = request.CreateOnly,
                }, ct)).ToProblemDetailsResult()));

        // A browser form: the provider enforces the size range and content type.
        app.MapPost("/links/{store}/form", (string store, UploadFormRequest request, HttpContext http, IFileStorageFactory factory, CancellationToken ct) =>
            FileEndpoints.WithStore(factory, store, http, async files =>
                (await files.CreateUploadFormAsync(request.Key, new PresignedPostOptions
                {
                    Expiry = TimeSpan.FromSeconds(request.ExpirySeconds),
                    MaxSize = request.MaxSize,
                    ContentType = request.ContentType,
                }, ct)).ToProblemDetailsResult()));

        // Very large files: start, one URL per part, complete (or abort).
        app.MapPost("/multipart/{store}/start", (string store, StartMultipartRequest request, HttpContext http, IFileStorageFactory factory, CancellationToken ct) =>
            FileEndpoints.WithStore(factory, store, http, async files =>
                (await files.StartMultipartUploadAsync(request.Key, new MultipartUploadOptions { ContentType = request.ContentType }, ct))
                    .ToProblemDetailsResult()));

        app.MapPost("/multipart/{store}/part-url", (string store, PartUrlRequest request, HttpContext http, IFileStorageFactory factory, CancellationToken ct) =>
            FileEndpoints.WithStore(factory, store, http, async files =>
                (await files.CreateUploadPartUrlAsync(request.Upload, request.PartNumber, TimeSpan.FromSeconds(request.ExpirySeconds), ct))
                    .ToProblemDetailsResult()));

        app.MapPost("/multipart/{store}/complete", (string store, CompleteMultipartRequest request, HttpContext http, IFileStorageFactory factory, CancellationToken ct) =>
            FileEndpoints.WithStore(factory, store, http, async files =>
                (await files.CompleteMultipartUploadAsync(request.Upload, request.Parts, cancellationToken: ct)).ToProblemDetailsResult()));

        app.MapPost("/multipart/{store}/abort", (string store, MultipartUpload upload, HttpContext http, IFileStorageFactory factory, CancellationToken ct) =>
            FileEndpoints.WithStore(factory, store, http, async files =>
                (await files.AbortMultipartUploadAsync(upload, ct)).ToProblemDetailsResult()));
    }
}

public sealed record DownloadLinkRequest(string Key, int ExpirySeconds, string? FileName = null);

public sealed record UploadLinkRequest(string Key, string ContentType, int ExpirySeconds, bool CreateOnly = false);

public sealed record UploadFormRequest(string Key, string ContentType, long MaxSize, int ExpirySeconds);

public sealed record StartMultipartRequest(string Key, string? ContentType = null);

public sealed record PartUrlRequest(MultipartUpload Upload, int PartNumber, int ExpirySeconds);

public sealed record CompleteMultipartRequest(MultipartUpload Upload, IReadOnlyCollection<UploadedPart> Parts);
