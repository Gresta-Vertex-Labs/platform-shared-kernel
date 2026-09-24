using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using SharedKernel.Core.Extensions;
using SharedKernel.Presentation.WebApi;
using SharedKernel.Presentation.WebApi.Errors;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;
using SharedKernel.Storage;

namespace DocumentsApi;

/// <summary>
/// Server-side file operations: the bytes flow through the service, streamed in both directions. Every endpoint
/// resolves the store (<see cref="Stores.Resolve"/>), binds the storage call to it and maps the
/// <see cref="Result{T}"/> to a typed result, so an unknown store, a missing tenant and every <c>storage.*</c> failure
/// reach the client as the same RFC 9457 problem.
/// </summary>
/// <remarks>
/// Preconditions come from the request: an upload takes <c>If-None-Match: *</c> (create only), and uploads, downloads
/// and deletes take <c>If-Match</c> with an ETag the client read. When the store refuses one — the object exists, or no
/// longer has that ETag — the error is a conflict (<c>storage.already_exists</c>, <c>storage.precondition_failed</c>),
/// and because the client sent the precondition in a header the answer is 412 Precondition Failed. The same errors
/// without a precondition header, such as a create-only copy that asks in its body, stay 409.
/// </remarks>
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
                    .Bind(files => IfMatchETag(http)
                        .Bind(ifMatch => files.UploadAsync(key, http.Request.Body, UploadOptions(http.Request, ifMatch), ct)))
                    .ToCreated(_ => $"/files/{store}/{key}"))
            .WithRequestSizeLimit(MaxUploadBytes);

        // Download: streamed back; a single Range header returns 206 with Content-Range. If-Match pins the version, so
        // range reads of one file cannot mix two versions of it.
        app.MapGet("/files/{store}/{**key}", (string store, string key, HttpContext http, IFileStorageFactory factory, CancellationToken ct) =>
            Stores.Resolve(factory, store, http)
                .Bind(files => IfMatchETag(http)
                    .Bind(ifMatch => files.DownloadAsync(key, DownloadOptions(http.Request, ifMatch), ct)))
                .ToHttpResult(download => new FileDownloadResult(download)));

        app.MapGet("/properties/{store}/{**key}", (string store, string key, HttpContext http, IFileStorageFactory factory, CancellationToken ct) =>
            Stores.Resolve(factory, store, http)
                .Bind(files => files.GetPropertiesAsync(key, ct))
                .ToOk());

        app.MapDelete("/files/{store}/{**key}", (string store, string key, HttpContext http, IFileStorageFactory factory, CancellationToken ct) =>
            Stores.Resolve(factory, store, http)
                .Bind(files => IfMatchETag(http)
                    .Bind(ifMatch => files.DeleteAsync(key, new FileDeleteOptions { IfMatch = ifMatch }, ct)))
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

    /// <summary>
    /// The entity tag the request's <c>If-Match</c> names, or <see langword="null"/> when it sends none. The header is
    /// read with <c>GetIfMatchTags()</c>, which parses it strictly, and must name one strong entity tag: the ETag of
    /// the file as the client read it (the stores take it with its quotes).
    /// </summary>
    /// <remarks>
    /// Any other <c>If-Match</c> is refused with 400 <c>precondition.invalid</c>, the code the platform uses where the
    /// header is required. Ignoring a header the service cannot read would turn the client's conditional request into an
    /// unconditional one, and an unconditional write overwrites whatever another client saved in between.
    /// </remarks>
    private static Result<string?> IfMatchETag(HttpContext http)
    {
        if (StringValues.IsNullOrEmpty(http.Request.Headers.IfMatch))
        {
            return Result<string?>.Success(null);
        }

        return http.GetIfMatchTags() is [{ IsWeak: false } tag] && !tag.Equals(EntityTagHeaderValue.Any)
            ? tag.Tag.ToString()
            : Error.Validation(PresentationErrorCodes.PreconditionInvalid, "If-Match must name one entity tag: the ETag of the file as it was read.");
    }

    private static FileUploadOptions UploadOptions(HttpRequest request, string? ifMatch)
    {
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
            Condition = request.Headers.IfNoneMatch == "*" ? WriteCondition.IfNotExists
                : ifMatch is not null ? WriteCondition.IfMatch(ifMatch)
                : null,
        };
    }

    private static FileDownloadOptions DownloadOptions(HttpRequest request, string? ifMatch) => new()
    {
        Range = ParseRange(request.Headers.Range),
        IfMatch = ifMatch,
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
