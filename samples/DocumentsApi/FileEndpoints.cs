using Microsoft.Net.Http.Headers;
using SharedKernel.Core.Extensions;
using SharedKernel.Presentation.WebApi;
using SharedKernel.Storage;

namespace DocumentsApi;

/// <summary>
/// Server-side file operations: the bytes flow through the service, streamed in both directions. Every endpoint
/// resolves the store (<see cref="Stores.Resolve"/>), binds the storage call to it and maps the
/// <see cref="SharedKernel.Primitives.Results.Result{T}"/> to a typed result, so an unknown store, a missing tenant and
/// every <c>storage.*</c> failure reach the client as the same RFC 9457 problem.
/// </summary>
public static class FileEndpoints
{
    /// <summary>Request header carrying the expected base64 SHA-256 of an upload.</summary>
    public const string ChecksumHeader = "X-Checksum-Sha256";

    /// <summary>Prefix of request headers stored as user metadata, e.g. <c>X-Meta-Order-Id</c>.</summary>
    public const string MetadataHeaderPrefix = "X-Meta-";

    /// <summary>
    /// The largest file <c>PUT /files/…</c> accepts, 1 GiB. The platform caps every request body at 4 MiB
    /// (<c>SharedKernel:Presentation:WebApi:Limits:MaxRequestBodySize</c>); the upload endpoint lifts that for itself
    /// only. Its body streams straight into the store, so the limit caps the object, not memory. Larger files go
    /// directly to the provider through the presigned multipart endpoints.
    /// </summary>
    public const long MaxUploadBytes = 1024L * 1024 * 1024;

    public static void MapFileEndpoints(this IEndpointRouteBuilder app)
    {
        // Upload: the request body is streamed straight into the store (never buffered).
        // If-None-Match: * → create only; If-Match: "etag" → replace only that version.
        app.MapPut("/files/{store}/{**key}", (string store, string key, HttpContext http, IFileStorageFactory factory, CancellationToken ct) =>
                Stores.Resolve(factory, store, http)
                    .Bind(files => files.UploadAsync(key, http.Request.Body, UploadOptions(http.Request), ct))
                    .ToCreated(_ => $"/files/{store}/{key}"))
            .WithRequestSizeLimit(MaxUploadBytes);

        // Download: streamed back; a single Range header returns 206 with Content-Range.
        app.MapGet("/files/{store}/{**key}", (string store, string key, HttpContext http, IFileStorageFactory factory, CancellationToken ct) =>
            Stores.Resolve(factory, store, http)
                .Bind(files => files.DownloadAsync(key, DownloadOptions(http.Request), ct))
                .ToHttpResult(download => new FileDownloadResult(download)));

        app.MapGet("/properties/{store}/{**key}", (string store, string key, HttpContext http, IFileStorageFactory factory, CancellationToken ct) =>
            Stores.Resolve(factory, store, http)
                .Bind(files => files.GetPropertiesAsync(key, ct))
                .ToOk());

        app.MapDelete("/files/{store}/{**key}", (string store, string key, HttpContext http, IFileStorageFactory factory, CancellationToken ct) =>
            Stores.Resolve(factory, store, http)
                .Bind(files => files.DeleteAsync(key, new FileDeleteOptions { IfMatch = http.Request.Headers.IfMatch.FirstOrDefault() }, ct))
                .ToNoContent());

        app.MapPost("/delete-many/{store}", (string store, string[] keys, HttpContext http, IFileStorageFactory factory, CancellationToken ct) =>
            Stores.Resolve(factory, store, http)
                .Bind(files => files.DeleteManyAsync(keys, ct))
                .ToOk());

        app.MapGet("/list/{store}", (string store, string? prefix, bool? recursive, int? pageSize, string? continuationToken, HttpContext http, IFileStorageFactory factory, CancellationToken ct) =>
            Stores.Resolve(factory, store, http)
                .Bind(files => files.ListPageAsync(
                    new FileListRequest
                    {
                        Prefix = prefix ?? string.Empty,
                        Recursive = recursive ?? true,
                        PageSize = pageSize ?? FileListRequest.MaxPageSize,
                        ContinuationToken = continuationToken,
                    },
                    ct))
                .ToOk());

        // Copy within a store or into another one (server-side when both share a connection).
        app.MapPost("/copy", (CopyRequest request, HttpContext http, IFileStorageFactory factory, CancellationToken ct) =>
            Stores.Resolve(factory, request.FromStore, http)
                .Bind(source => Stores.Resolve(factory, request.ToStore, http)
                    .Bind(destination => source.CopyToAsync(
                        request.FromKey,
                        destination,
                        request.ToKey,
                        new FileCopyOptions { Condition = request.CreateOnly ? WriteCondition.IfNotExists : null },
                        ct)))
                .ToOk());
    }

    private static FileUploadOptions UploadOptions(HttpRequest request)
    {
        string? ifNoneMatch = request.Headers.IfNoneMatch.FirstOrDefault();
        string? ifMatch = request.Headers.IfMatch.FirstOrDefault();
        var metadata = request.Headers
            .Where(h => h.Key.StartsWith(MetadataHeaderPrefix, StringComparison.OrdinalIgnoreCase))
            .ToDictionary(h => h.Key[MetadataHeaderPrefix.Length..], h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);

        return new FileUploadOptions
        {
            ContentType = request.ContentType,
            ContentLength = request.ContentLength,
            CacheControl = request.Headers.CacheControl.FirstOrDefault(),
            ContentDisposition = request.Headers.ContentDisposition.FirstOrDefault(),
            Metadata = metadata.Count > 0 ? metadata : null,
            ChecksumSha256 = request.Headers[ChecksumHeader].FirstOrDefault(),
            Condition = ifNoneMatch == "*" ? WriteCondition.IfNotExists
                : ifMatch is not null ? WriteCondition.IfMatch(ifMatch)
                : null,
        };
    }

    private static FileDownloadOptions DownloadOptions(HttpRequest request) => new()
    {
        Range = ParseRange(request.Headers.Range),
        IfMatch = request.Headers.IfMatch.FirstOrDefault(),
    };

    private static ByteRange? ParseRange(string? header)
    {
        if (!RangeHeaderValue.TryParse(header, out RangeHeaderValue? range) || range.Ranges.Count != 1)
        {
            return null;
        }

        RangeItemHeaderValue item = range.Ranges.First();
        return item.From is { } from ? new ByteRange(from, item.To) : null;
    }
}

/// <summary>A copy between two stores; a tenant store uses the request's tenant.</summary>
public sealed record CopyRequest(string FromStore, string FromKey, string ToStore, string ToKey, bool CreateOnly = false);

/// <summary>Streams a <see cref="FileDownload"/> to the response, disposing it when done.</summary>
internal sealed class FileDownloadResult(FileDownload download) : IResult
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        await using (download)
        {
            HttpResponse response = httpContext.Response;
            FileProperties properties = download.Properties;
            response.StatusCode = download.Range is null ? StatusCodes.Status200OK : StatusCodes.Status206PartialContent;
            response.ContentType = properties.ContentType ?? "application/octet-stream";
            response.ContentLength = download.Length;
            if (properties.ETag is { } eTag)
            {
                response.Headers.ETag = eTag;
            }

            if (properties.ContentDisposition is { } disposition)
            {
                response.Headers.ContentDisposition = disposition;
            }

            if (download.Range is { } range)
            {
                response.Headers.ContentRange = new ContentRangeHeaderValue(range.From, range.To ?? properties.ContentLength - 1, properties.ContentLength).ToString();
            }

            await download.Content.CopyToAsync(response.Body, httpContext.RequestAborted);
        }
    }
}
