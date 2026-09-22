using Microsoft.Net.Http.Headers;
using SharedKernel.Presentation.WebApi.Results;
using SharedKernel.Primitives.Results;
using SharedKernel.Storage;

namespace DocumentsApi;

/// <summary>Server-side file operations: the bytes flow through the service, streamed in both directions.</summary>
public static class FileEndpoints
{
    /// <summary>Request header carrying the expected base64 SHA-256 of an upload.</summary>
    public const string ChecksumHeader = "X-Checksum-Sha256";

    /// <summary>Prefix of request headers stored as user metadata, e.g. <c>X-Meta-Order-Id</c>.</summary>
    public const string MetadataHeaderPrefix = "X-Meta-";

    public static void MapFileEndpoints(this IEndpointRouteBuilder app)
    {
        // Upload: the request body is streamed straight into the store (never buffered).
        // If-None-Match: * → create only; If-Match: "etag" → replace only that version.
        app.MapPut("/files/{store}/{**key}", (string store, string key, HttpContext http, IFileStorageFactory factory, CancellationToken ct) =>
            WithStore(factory, store, http, async files =>
            {
                Result<FileReference> uploaded = await files.UploadAsync(key, http.Request.Body, UploadOptions(http.Request), ct);
                return uploaded.ToProblemDetailsResult(reference => Results.Created($"/files/{store}/{key}", reference));
            }));

        // Download: streamed back; a single Range header returns 206 with Content-Range.
        app.MapGet("/files/{store}/{**key}", (string store, string key, HttpContext http, IFileStorageFactory factory, CancellationToken ct) =>
            WithStore(factory, store, http, async files =>
            {
                var options = new FileDownloadOptions
                {
                    Range = ParseRange(http.Request.Headers.Range),
                    IfMatch = http.Request.Headers.IfMatch.FirstOrDefault(),
                };
                Result<FileDownload> download = await files.DownloadAsync(key, options, ct);
                return download.ToProblemDetailsResult(file => new FileDownloadResult(file));
            }));

        app.MapGet("/properties/{store}/{**key}", (string store, string key, HttpContext http, IFileStorageFactory factory, CancellationToken ct) =>
            WithStore(factory, store, http, async files => (await files.GetPropertiesAsync(key, ct)).ToProblemDetailsResult()));

        app.MapDelete("/files/{store}/{**key}", (string store, string key, HttpContext http, IFileStorageFactory factory, CancellationToken ct) =>
            WithStore(factory, store, http, async files =>
                (await files.DeleteAsync(key, new FileDeleteOptions { IfMatch = http.Request.Headers.IfMatch.FirstOrDefault() }, ct))
                    .ToProblemDetailsResult()));

        app.MapPost("/delete-many/{store}", (string store, string[] keys, HttpContext http, IFileStorageFactory factory, CancellationToken ct) =>
            WithStore(factory, store, http, async files => (await files.DeleteManyAsync(keys, ct)).ToProblemDetailsResult()));

        app.MapGet("/list/{store}", (string store, string? prefix, bool? recursive, int? pageSize, string? continuationToken, HttpContext http, IFileStorageFactory factory, CancellationToken ct) =>
            WithStore(factory, store, http, async files =>
            {
                var request = new FileListRequest
                {
                    Prefix = prefix ?? string.Empty,
                    Recursive = recursive ?? true,
                    PageSize = pageSize ?? FileListRequest.MaxPageSize,
                    ContinuationToken = continuationToken,
                };
                return (await files.ListPageAsync(request, ct)).ToProblemDetailsResult();
            }));

        // Copy within a store or into another one (server-side when both share a connection).
        app.MapPost("/copy", (CopyRequest request, HttpContext http, IFileStorageFactory factory, CancellationToken ct) =>
            WithStore(factory, request.FromStore, http, source => WithStore(factory, request.ToStore, http, async destination =>
            {
                var options = new FileCopyOptions { Condition = request.CreateOnly ? WriteCondition.IfNotExists : null };
                Result<FileReference> copied = await source.CopyToAsync(request.FromKey, destination, request.ToKey, options, ct);
                return copied.ToProblemDetailsResult();
            })));
    }

    internal static async Task<IResult> WithStore(
        IFileStorageFactory factory,
        string storeName,
        HttpContext http,
        Func<IFileStorage, Task<IResult>> action)
    {
        Result<IFileStorage> store = Stores.Resolve(factory, storeName, http);
        return store.IsSuccess ? await action(store.Value) : store.ToProblemDetailsResult();
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
