# SharedKernel.Storage.Abstractions

Object-storage abstraction contracts for Platform.SharedKernel microservices. Defines `IFileStorage` (upload/download/delete/exists/metadata/copy/batch-delete/streaming-list/health-probe) and `IBlobUriGenerator` (presigned upload/download URLs), plus the `StorageErrors` factory. **Zero third-party NuGet dependencies** — references only `SharedKernel.Primitives`. Implemented by `SharedKernel.Storage.S3` (AWS S3 / MinIO) and `SharedKernel.Storage.Obs` (Huawei Cloud OBS).

Application code should always inject `IFileStorage` / `IBlobUriGenerator` from this package — never a concrete cloud SDK type (`IAmazonS3` or similar) directly.

## Included Types

- `IFileStorage` — nine-member provider-agnostic contract: `UploadAsync`, `DownloadAsync`, `DeleteAsync`, `ExistsAsync`, `GetMetadataAsync`, `CopyAsync`, `DeleteManyAsync`, `ListAsync`, `CheckHealthAsync`
- `IBlobUriGenerator` — `GeneratePresignedUploadUrl` / `GeneratePresignedDownloadUrl`
- `StorageErrors` — static `Error` factory: `NotFound`, `AccessDenied`, `InvalidBucket`, `InvalidKey`, `ExpiryTooLong`, `UploadFailed`, `CopyFailed`, `BatchDeleteFailed`, `ConnectivityFailure`
- `Models/` — `FileUploadRequest`, `FileReference`, `FileDownload` (`IAsyncDisposable`), `FileMetadata`, `FileDeleteOutcome`, `PresignedUrlRequest`, `PresignedUrl`

## Install

```xml
<ProjectReference Include="..\SharedKernel.Storage.Abstractions\SharedKernel.Storage.Abstractions.csproj" />
```

Or, once published, reference the NuGet package `SharedKernel.Storage.Abstractions`, plus a provider package (`SharedKernel.Storage.S3` or `SharedKernel.Storage.Obs`) to actually resolve `IFileStorage`/`IBlobUriGenerator` — this package ships no DI extensions and no implementation.

## Design principles

- **Stream-first, always.** Payloads flow as `Stream` from caller to provider and back. There is no `byte[]` upload/download overload — large objects never fully materialize in managed memory.
- **`Result`-valued expected failures.** Not-found, access-denied, validation, and provider-rejection are `Error` values via `StorageErrors` (`01.Core/SharedKernel.Primitives`) — never thrown exceptions. Only genuinely exceptional transport faults (socket reset, DNS failure) propagate as exceptions.
- **Caller-owned upload stream, caller-disposed download stream.** `FileUploadRequest.Content` is never disposed by `UploadAsync` — the caller owns its lifetime and controls the starting read position. `DownloadAsync` returns a `FileDownload` whose `Content` is a provider-backed network stream the caller must dispose (`FileDownload` is `IAsyncDisposable`).
- **`ListAsync` is a deliberate exception to the `Result`-first rule** — it returns `IAsyncEnumerable<FileMetadata>` directly for constant-memory streaming, not `Task<Result<IAsyncEnumerable<FileMetadata>>>`. A provider fault mid-enumeration propagates as a thrown exception from `MoveNextAsync`, not an `Error` value — mirrors `06.Persistence`'s `IReadRepository.StreamAsync` precedent.

## Quick start — upload / download

```csharp
public sealed class DocumentUploader(IFileStorage fileStorage)
{
    public async Task<Result<FileReference>> UploadAsync(Stream content, CancellationToken ct)
    {
        var request = new FileUploadRequest
        {
            Bucket = "documents",
            Key = $"invoices/{Guid.NewGuid():D}.pdf",
            Content = content,             // caller-owned — UploadAsync never disposes it
            ContentType = "application/pdf",
        };

        return await fileStorage.UploadAsync(request, ct);
    }

    public async Task<Result> StreamToResponseAsync(string bucket, string key, Stream destination, CancellationToken ct)
    {
        var downloadResult = await fileStorage.DownloadAsync(bucket, key, ct);
        if (downloadResult.IsFailure)
        {
            return Result.Failure(downloadResult.Error);
        }

        // await using disposes the provider network stream once consumption is complete.
        await using var download = downloadResult.Value;
        await download.Content.CopyToAsync(destination, ct);
        return Result.Success();
    }
}
```

## Exists / metadata / delete

```csharp
Result<bool> exists = await fileStorage.ExistsAsync("documents", "invoices/123.pdf", ct);   // HEAD-style probe, never downloads the body
Result<FileMetadata> metadata = await fileStorage.GetMetadataAsync("documents", "invoices/123.pdf", ct);
Result deleted = await fileStorage.DeleteAsync("documents", "invoices/123.pdf", ct);          // idempotent — deleting an absent key still succeeds
```

## Server-side copy

`CopyAsync` issues a native provider copy call — object bytes never round-trip through application memory. The source object is left untouched (copy, not move):

```csharp
Result<FileReference> archived = await fileStorage.CopyAsync(
    sourceBucket: "documents", sourceKey: "invoices/123.pdf",
    destinationBucket: "documents-archive", destinationKey: "invoices/123.pdf",
    cancellationToken: ct);
```

## Batch delete

`DeleteManyAsync` is backed by the provider's native multi-object delete, chunked internally at the provider's own per-request key limit — never a hand-rolled N-call delete loop. The outer `Result` fails only when the batch call itself could not be attempted; a partial failure is reported per key:

```csharp
Result<IReadOnlyList<FileDeleteOutcome>> outcomes = await fileStorage.DeleteManyAsync(
    "documents", ["invoices/1.pdf", "invoices/2.pdf", "invoices/3.pdf"], ct);

if (outcomes.IsSuccess)
{
    foreach (var outcome in outcomes.Value)
    {
        if (!outcome.Succeeded)
        {
            // outcome.Error carries the per-key reason (e.g. StorageErrors.AccessDenied)
        }
    }
}
```

## Streaming list

`ListAsync` streams `FileMetadata` in constant memory via `IAsyncEnumerable<T>` — deliberately **not** `Result`-wrapped (see Design principles above). Consume it with `await foreach`, never by materializing an intermediate `List<T>`:

```csharp
await foreach (var item in fileStorage.ListAsync("documents", "invoices/", ct))
{
    // item.Bucket / item.Key / item.ContentType / item.ContentLength / item.LastModified / item.ETag
}
```

## Connectivity probe

`CheckHealthAsync` is a bucket-scoped, non-generic `Result` reachability probe — it never requires a specific object key to exist and never touches object bytes. Intended as the backing primitive for a `13.ServiceDefaults` K8s readiness health check (this package ships no `IHealthCheck` implementation itself):

```csharp
Result health = await fileStorage.CheckHealthAsync("documents", ct);
```

## Presigned URLs

`IBlobUriGenerator`'s two methods are synchronous — presigning is a local cryptographic operation against provider credentials, with no network round-trip. `Expiry` is bounded by the provider's maximum (7 days for S3-family signatures); exceeding it returns `StorageErrors.ExpiryTooLong`. `PresignedUrl.ExpiresAt` is always absolute, derived once at generation time:

```csharp
public sealed class UploadLinkService(IBlobUriGenerator blobUriGenerator)
{
    public Result<PresignedUrl> CreateUploadLink(string bucket, string key) =>
        blobUriGenerator.GeneratePresignedUploadUrl(new PresignedUrlRequest
        {
            Bucket = bucket,
            Key = key,
            Expiry = TimeSpan.FromMinutes(15),
        });
}
```

## Error handling

Every `IFileStorage`/`IBlobUriGenerator` member returns a `Result`/`Result<T>` populated with a `StorageErrors` value on expected failure — check `Result.IsFailure`/`Result.Error` rather than catching exceptions:

```csharp
Result<FileReference> uploadResult = await fileStorage.UploadAsync(request, ct);
if (uploadResult.IsFailure)
{
    return uploadResult.Error.Code switch
    {
        "storage.access_denied" => Result.Failure(uploadResult.Error),
        "storage.upload_failed" => Result.Failure(uploadResult.Error),
        _ => Result.Failure(uploadResult.Error),
    };
}
```

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) — see [08.Storage/CLAUDE.md](../CLAUDE.md) for the full interface contracts, provider implementation rules, and AOT posture.
