# SharedKernel.Storage.Abstractions

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Cloud SDK dependencies: 0](https://img.shields.io/badge/cloud%20SDK%20dependencies-0-brightgreen)
![Provider: neutral](https://img.shields.io/badge/provider-neutral-informational)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

> **Object-storage contracts for .NET services: named stores, tenant isolation by construction, conditional writes,
> and presigned uploads that clients cannot abuse.**

Object storage goes wrong in quiet ways:

- a tenant id or `../` in a key reaches another tenant's files;
- two requests upload the same key and the second silently overwrites the first;
- a browser upload URL accepts a 20 GB file, or an HTML page served from your bucket;
- a bucket name is hard-coded in fifty call sites and differs between environments;
- a throttled provider throws an exception nobody expected, in the middle of a request.

This package defines the contracts that make those mistakes hard to write. Application code depends only on it; the
host picks a provider — [`SharedKernel.Storage.S3`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/08.Storage/SharedKernel.Storage.S3/README.md)
for Amazon S3 and MinIO, [`SharedKernel.Storage.Obs`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/08.Storage/SharedKernel.Storage.Obs/README.md)
for Huawei Cloud OBS.

| You get | So that |
| --- | --- |
| Named stores — `[FromKeyedServices("invoices")] IFileStorage` | Code names a purpose; buckets, prefixes, encryption and credentials are configuration |
| `ITenantFileStorage.ForTenant(id)` | A tenant's keys live under its own prefix; no key, listing or copy can leave it |
| `WriteCondition.IfNotExists` / `IfMatch(etag)` | Create-only and optimistic-concurrency writes, checked atomically by the provider |
| `FileUploadOptions.ChecksumSha256` | The provider rejects bytes that were corrupted on the way |
| `CreateUploadFormAsync` | Browser uploads limited to a size range and content type, enforced by the provider |
| `StartMultipartUploadAsync` + part URLs | Multi-gigabyte browser uploads that never pass through your service |
| `Result` values with `StorageErrorCodes` | Not found, conflict, throttling and outages are values you handle, not exceptions |

## Contents

- [Install](#install)
- [Quick start](#quick-start)
- [Which type do I need?](#which-type-do-i-need)
- [How it works](#how-it-works)
  - [Stores and keys](#stores-and-keys)
  - [Tenant stores](#tenant-stores)
  - [Results and errors](#results-and-errors)
  - [Streaming](#streaming)
- [Recipes](#recipes)
- [Reference](#reference)
- [Pitfalls](#pitfalls)
- [Design decisions](#design-decisions)
- [AI quick reference](#ai-quick-reference)
- [Compatibility and guarantees](#compatibility-and-guarantees)
- [Deliberately not included](#deliberately-not-included)

## Install

```shell
dotnet add package SharedKernel.Storage.Abstractions   # application and domain-service projects
dotnet add package SharedKernel.Storage.S3             # the host (or SharedKernel.Storage.Obs)
```

The only dependencies are `SharedKernel.Primitives` (for `Result` and `Error`) and
`Microsoft.Extensions.DependencyInjection.Abstractions`.

## Quick start

```csharp
// Program.cs — the host
builder.Services.AddSharedKernelStorage()
    .AddS3(builder.Configuration)          // SharedKernel:Storage:S3
    .AddStore("invoices")                  // SharedKernel:Storage:Stores:invoices
    .AddTenantStore("documents");          // SharedKernel:Storage:Stores:documents
```

```json
"SharedKernel": {
  "Storage": {
    "S3": { "Region": "eu-central-1" },
    "Stores": {
      "invoices":  { "Bucket": "acme-invoices", "Encryption": "Kms" },
      "documents": { "Bucket": "acme-documents", "MaxPresignExpiry": "00:15:00" }
    }
  }
}
```

```csharp
// Application code — only this package
public sealed class InvoiceArchive([FromKeyedServices("invoices")] IFileStorage invoices)
{
    public Task<Result<FileReference>> SaveAsync(Guid invoiceId, Stream pdf, CancellationToken ct) =>
        invoices.UploadAsync($"{invoiceId}.pdf", pdf, new FileUploadOptions
        {
            ContentType = "application/pdf",
            Condition = WriteCondition.IfNotExists,   // never overwrite an issued invoice
        }, ct);

    public Task<Result<PresignedRequest>> LinkAsync(Guid invoiceId, CancellationToken ct) =>
        invoices.CreateDownloadUrlAsync($"{invoiceId}.pdf", new PresignedDownloadOptions
        {
            Expiry = TimeSpan.FromMinutes(10),
            ContentDisposition = $"attachment; filename=\"invoice-{invoiceId}.pdf\"",
        }, ct);
}
```

Persist the returned `FileReference` (store, tenant, key, ETag) — never a presigned URL, which expires, and never a
bucket name, which is configuration.

## Which type do I need?

| You want to | Use |
| --- | --- |
| Read and write files of one store shared by the whole service | `[FromKeyedServices("name")] IFileStorage` |
| …and the service has exactly one shared store | `IFileStorage` (unkeyed; resolving throws if there are several) |
| Read and write files that belong to a tenant | `[FromKeyedServices("name")] ITenantFileStorage` → `ForTenant(tenantId)` |
| Open a store named in data — a stored `FileReference`, a job argument | `IFileStorageFactory.Open(reference)` / `GetStore(name)` / `GetTenantStore(name)` |
| Check a store's bucket is reachable (readiness) | `IFileStorageHealthProbe`, wired by `SharedKernel.ServiceDefaults.Storage` |
| Validate input the way every store does | `StorageValidation` (rarely needed: every member validates already) |
| Write a provider package | `IStorageBuilder`, `FileStoreRegistration`, `StorageErrors` |

## How it works

### Stores and keys

A **store** is a bucket, optionally narrowed to a key prefix, on one provider connection. Each store is a keyed
singleton registered under its name; the name is the only thing application code knows.

- **Keys are relative to the store** (and to the tenant, for a tenant store): `2026/09/invoice-42.pdf`. Use `/` to
  make folders.
- **Keys are validated before any request**: not empty, no leading `/`, no `\`, no empty, `.` or `..` segment, no
  control characters, at most 1,024 UTF-8 bytes including every prefix. An invalid key is `storage.invalid_key`.
- **Stores copy between each other**: `source.CopyToAsync(key, destinationStore, newKey)` copies server-side when both
  stores share a provider connection, and streams through the process otherwise.

### Tenant stores

```text
store "documents"  (bucket acme-docs, prefix documents/)
   ForTenant("acme")    "contracts/nda.pdf"  →  documents/tenants/acme/contracts/nda.pdf
   ForTenant("globex")  "contracts/nda.pdf"  →  documents/tenants/globex/contracts/nda.pdf
```

- A tenant store is **only** reachable through `ForTenant(tenantId)`. Resolving it as `IFileStorage` throws, so it
  cannot be used without choosing a tenant.
- Tenant ids are validated — 1 to 128 characters from `A-Z a-z 0-9 . _ -`, not `.` or `..` — and **never escaped**,
  so two tenant ids can never produce the same prefix. An invalid id throws `ArgumentException`.
- A view returns keys, listings, folders and error messages **without** the prefix, and its `FileReference` carries
  the tenant, so `IFileStorageFactory.Open(reference)` reopens the right view.
- Take the tenant from the authenticated request (`IRequestContext.TenantId`), never from input the caller controls.

### Results and errors

Every member returns `Result` or `Result<T>` (from `SharedKernel.Primitives`). Expected failures — a missing object,
a failed condition, a denied request, a provider outage — are errors with a stable code from `StorageErrorCodes`,
never exceptions. Only three things throw:

- **cancellation** of your `CancellationToken` (`OperationCanceledException`);
- **`ListAsync`**, an async stream with no `Result` to return (`StorageException`, carrying the error);
- **programming errors**: a `null` argument, an invalid tenant id, an unknown store name.

Error messages name the store and the key you passed, never the bucket, endpoint or provider request id — they may
reach an HTTP response. Providers log those details instead.

### Streaming

Nothing is buffered. `UploadAsync` reads your stream from its current position to its end, and never disposes or
rewinds it; streams of unknown length (a request body, a pipe) are sent in parts with at most one part in memory.
`DownloadAsync` returns the provider's response stream inside a `FileDownload` you dispose.

## Recipes

### 1. Accept an upload in an API endpoint

```csharp
app.MapPut("/files/{**key}", async (string key, HttpRequest request,
    [FromKeyedServices("uploads")] IFileStorage store, CancellationToken ct) =>
{
    Result<FileReference> saved = await store.UploadAsync(key, request.Body, new FileUploadOptions
    {
        ContentType = request.ContentType,
        ContentLength = request.ContentLength,   // a request body cannot report its own length
    }, ct);
    return saved.ToCreated(_ => $"/files/{key}");   // SharedKernel.Presentation.WebApi: 201, or a problem response
});
```

Passing `ContentLength` lets small bodies go in a single request and is **required** together with
`ChecksumSha256` on a stream that cannot report its length.

### 2. Never overwrite, or overwrite only what you read

```csharp
// Create only: a concurrent upload of the same key fails with storage.already_exists.
await store.UploadAsync(key, content, new FileUploadOptions { Condition = WriteCondition.IfNotExists }, ct);

// Optimistic concurrency: replace only the version you read.
FileProperties current = (await store.GetPropertiesAsync(key, ct)).Value;
Result<FileReference> saved = await store.UploadAsync(key, updated,
    new FileUploadOptions { Condition = WriteCondition.IfMatch(current.ETag!) }, ct);
// saved.Error.Code == StorageErrorCodes.PreconditionFailed when someone else changed it first
```

### 3. Verify integrity end to end

```csharp
string sha256 = Convert.ToBase64String(SHA256.HashData(bytes));
Result<FileReference> saved = await store.UploadAsync(key, new MemoryStream(bytes),
    new FileUploadOptions { ChecksumSha256 = sha256 }, ct);
// storage.checksum_mismatch when the provider received different bytes; nothing is stored
```

### 4. Stream a download, or part of one

```csharp
await using FileDownload file = (await store.DownloadAsync(key, new FileDownloadOptions
{
    Range = new ByteRange(0, 1_048_575),   // the first MiB
    IfMatch = etag,                        // all ranges from the same version
}, ct)).Value;

await file.Content.CopyToAsync(destination, ct);
// file.Length = bytes in this response; file.Properties.ContentLength = size of the whole object
```

### 5. List a folder, page by page

```csharp
FileListPage page = (await store.ListPageAsync(new FileListRequest
{
    Prefix = "2026/",
    Recursive = false,        // objects directly under 2026/ plus its sub-folders in page.Folders
    PageSize = 100,
    ContinuationToken = token,
}, ct)).Value;

await foreach (FileListItem item in store.ListAsync("2026/09/", ct)) { /* everything, one page in memory */ }
```

### 6. Give a client a download link

```csharp
PresignedRequest link = (await store.CreateDownloadUrlAsync(key, new PresignedDownloadOptions
{
    Expiry = TimeSpan.FromMinutes(10),
    ContentDisposition = "attachment; filename=\"report.csv\"",
}, ct)).Value;
// link.Url works without credentials until link.ExpiresAt
```

### 7. Let a browser upload directly, with limits

```csharp
PresignedPost form = (await store.CreateUploadFormAsync($"avatars/{userId}.png", new PresignedPostOptions
{
    Expiry = TimeSpan.FromMinutes(5),
    MaxSize = 2 * 1024 * 1024,
    ContentType = "image/",       // a prefix ending with '/' allows any image type
}, ct)).Value;
```

The browser posts a `multipart/form-data` body to `form.Url`: every entry of `form.Fields`, a `Content-Type` field, then
the file as the last field, named `file`. The provider rejects anything outside the policy. Use a presigned `PUT`
(`CreateUploadUrlAsync`) only for trusted clients — it cannot limit the size.

### 8. Upload a very large file from the client in parts

```csharp
MultipartUpload upload = (await store.StartMultipartUploadAsync(key, new MultipartUploadOptions { ContentType = "video/mp4" }, ct)).Value;

// For each part (1..n, each at least 5 MiB except the last):
PresignedRequest partUrl = (await store.CreateUploadPartUrlAsync(upload, partNumber, TimeSpan.FromMinutes(30), ct)).Value;
// The client PUTs the part and returns the response's ETag header.

Result<FileReference> done = await store.CompleteMultipartUploadAsync(upload, parts, cancellationToken: ct);
// or: await store.AbortMultipartUploadAsync(upload, ct);
```

Configure a bucket lifecycle rule that aborts incomplete multipart uploads after a few days.

### 9. Move a file out of quarantine into a tenant's folder

```csharp
IFileStorage tenantDocuments = documents.ForTenant(tenantId);
Result<FileReference> moved = await quarantine.CopyToAsync(key, tenantDocuments, $"inbox/{fileName}",
    new FileCopyOptions { Condition = WriteCondition.IfNotExists }, ct);
if (moved.IsSuccess)
{
    await quarantine.DeleteAsync(key, cancellationToken: ct);
}
```

### 10. Delete a tenant's files

```csharp
IFileStorage view = documents.ForTenant(tenantId);
var keys = new List<string>();
await foreach (FileListItem item in view.ListAsync(cancellationToken: ct))
{
    keys.Add(item.Key);
}

BatchDeleteResult result = (await view.DeleteManyAsync(keys, ct)).Value;
// result.Failed lists every key that could not be deleted, with its error
```

## Reference

### Public types

| Type | Kind | Purpose |
| --- | --- | --- |
| `IFileStorage` | Interface | One store (or tenant view): upload, download, properties, exists, delete, batch delete, copy, listing, presigned URLs, forms and multipart |
| `ITenantFileStorage` | Interface | A tenant store; `ForTenant(tenantId)` returns the tenant's `IFileStorage` view |
| `IFileStorageFactory` | Interface | Stores by name: `GetStore`, `GetTenantStore`, `Open(FileReference)`, `StoreNames`, `IsTenantScoped` |
| `IFileStorageHealthProbe` | Interface | `ProbeAsync(storeName)`: is the store's bucket reachable with these credentials |
| `FileReference` | Record | The durable handle: `Store`, `TenantId`, `Key`, `ETag`, `VersionId` |
| `FileProperties` | Record | Size, content type, ETag, version, headers, SHA-256, tier, metadata |
| `FileDownload` | Class | An open download: `Content`, `Properties`, `Length`, `Range`; dispose it |
| `FileListItem` / `FileListRequest` / `FileListPage` | Records | Listing: an object, a page request, a page with `Folders` and `ContinuationToken` |
| `BatchDeleteResult` / `FileDeleteFailure` | Records | `Deleted` keys and `Failed` keys with their errors |
| `FileUploadOptions` / `FileDownloadOptions` / `FileDeleteOptions` / `FileCopyOptions` | Records | Per-call options |
| `WriteCondition` | Record | `IfNotExists` or `IfMatch(etag)` |
| `ByteRange` | Struct | An inclusive byte range; `FirstBytes(n)` |
| `StorageTier` | Enum | `Default`, `InfrequentAccess` |
| `PresignedRequest` / `PresignedDownloadOptions` / `PresignedUploadOptions` | Records | Presigned `GET`/`PUT`: `Url`, `Method`, required `Headers`, `ExpiresAt` |
| `PresignedPost` / `PresignedPostOptions` | Records | Presigned form: `Url`, `Fields`, `ExpiresAt`; size and content-type policy |
| `MultipartUpload` / `MultipartUploadOptions` / `UploadedPart` | Records | Presigned multipart upload |
| `StorageErrorCodes` / `StorageErrors` | Static classes | The error codes, and the factory providers build errors with |
| `StorageException` | Exception | Thrown by `ListAsync`; carries the `Error` |
| `StorageValidation` | Static class | The key, tenant, metadata, tag, header, checksum and expiry rules |
| `StorageServiceCollectionExtensions` | Static class | `AddSharedKernelStorage()`; `AddStore(FileStoreRegistration)` for providers |
| `IStorageBuilder` / `FileStoreRegistration` | Interface / Class | How a provider package contributes stores |

### `FileUploadOptions`

| Option | Default | Rules |
| --- | --- | --- |
| `ContentType` | `application/octet-stream` | A MIME type such as `application/pdf` |
| `ContentDisposition`, `CacheControl`, `ContentEncoding` | none | Printable ASCII, at most 1,024 characters |
| `Metadata` | none | Keys of letters, digits, `-`, `_` (stored lower-case); printable ASCII values; 2 KiB in total |
| `Tags` | none | At most 10; keys up to 128, values up to 256 characters |
| `Tier` | the store's default | `Default` or `InfrequentAccess` |
| `Condition` | none | `WriteCondition.IfNotExists` or `WriteCondition.IfMatch(etag)` |
| `ContentLength` | the stream's length, if it can report one | Not negative; required with `ChecksumSha256` on a stream of unknown length |
| `ChecksumSha256` | none | Base64 of a 32-byte SHA-256 |

### Error codes

| Code | `ErrorType` (HTTP) | Meaning |
| --- | --- | --- |
| `storage.not_found` | NotFound (404) | The object does not exist |
| `storage.access_denied` | Forbidden (403) | The credentials may not do this |
| `storage.already_exists` | Conflict (409; 412 when the request carries `If-Match` or `If-None-Match`) | A create-only write found an object |
| `storage.precondition_failed` | Conflict (409; 412 when the request carries `If-Match` or `If-None-Match`) | An `If-Match` ETag no longer matches (also a conditional delete of a missing object) |
| `storage.invalid_key` | Validation (400) | The key or prefix breaks the key rules |
| `storage.invalid_tenant` | Validation (400) | The tenant id breaks the tenant rules |
| `storage.invalid_request` | Validation (400) | An option is invalid: metadata, tags, headers, page size, part number, missing `ContentLength` |
| `storage.expiry_too_long` | Validation (400) | A presign expiry is not positive or exceeds the store's `MaxPresignExpiry` |
| `storage.checksum_mismatch` | Validation (400) | The provider received bytes that do not match `ChecksumSha256` |
| `storage.invalid_range` | Validation (400) | The range starts beyond the end of the object |
| `storage.not_supported` | Unexpected (500) | The store's provider lacks the feature; nothing was sent |
| `storage.unavailable` | Unavailable (503) | Unreachable, throttled or failing after the provider SDK's retries — retry later |
| `storage.provider_error` | Unexpected (500) | Any other rejection; details are logged, never returned |

The HTTP statuses are the ones `SharedKernel.Presentation.WebApi` answers. The two conflicts become 412 Precondition
Failed only on a request that itself carried `If-Match` or `If-None-Match` (both codes are in its default
`Problems:PreconditionFailedErrorCodes`); the same conflict from a condition the service set itself, such as
`WriteCondition.IfNotExists` on a plain `PUT`, stays 409. Outside Development, 500 and 503 responses keep their
`errorCode` but carry a generic `detail`.

### Exceptions

| Exception | Thrown by | When |
| --- | --- | --- |
| `ArgumentNullException` | Every member | A stream, options object, collection or store is `null` |
| `ArgumentException` | `ForTenant`, `IFileStorageFactory.Open`, `FileStoreRegistration`, `WriteCondition.IfMatch` | An invalid tenant id, store name or empty ETag |
| `ArgumentOutOfRangeException` | `ByteRange`, `FileDownload` | A negative position, or an end before the start |
| `InvalidOperationException` | `IFileStorageFactory`, unkeyed `IFileStorage`/`ITenantFileStorage`, `AddStore` | An unknown store, a tenancy mismatch, an ambiguous unkeyed store, a duplicate store name |
| `OperationCanceledException` | Every async member | Your `CancellationToken` was cancelled |
| `StorageException` | `ListAsync` | A page could not be read; `Error` carries the storage error |

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Persist a presigned URL or a bucket name | Persist the `FileReference` | URLs expire; buckets are configuration |
| Build tenant prefixes by hand (`$"{tenantId}/{key}"`) | `AddTenantStore` + `ForTenant(tenantId)` | Hand-built prefixes are not validated and can be escaped |
| Take the tenant id from a header or the request body | Take it from the authenticated principal | The tenant view isolates tenants only if the tenant is real |
| Read a file to check it exists before writing | `WriteCondition.IfNotExists` | The check and the write race; the condition is atomic |
| Give untrusted clients a presigned `PUT` URL | `CreateUploadFormAsync` with `MaxSize` and `ContentType` | A presigned `PUT` cannot limit size |
| Upload a request body with a checksum and no length | Set `ContentLength = Request.ContentLength` | The single request that verifies a checksum needs the length |
| Forget to dispose a `FileDownload` | `await using` | It holds a pooled HTTP connection |
| Treat `storage.unavailable` as "not found" | Retry later or fail the request | The object may well exist |
| Match on error messages | Match on `Error.Code` | Messages are for people and may change |
| Leave abandoned multipart uploads | Abort them, and add a bucket lifecycle rule | Their parts are stored and billed |
| Send a form upload's file part with `filename*=` (.NET's `MultipartFormDataContent.Add(content, name, fileName)`) | A plain `filename`, as browsers send | Some providers (OBS) reject the encoded form |

## Design decisions

**Why named stores instead of a bucket argument?** Bucket names are configuration and differ per environment. Passing
them on every call spread them through code, and a per-call bucket made one registered client serve every bucket —
the old design silently sent S3 traffic to OBS when both were registered.

**Why is tenant isolation in the contracts package, not in each provider?** One implementation protects every provider
and the in-memory test store alike. Providers only ever see keys that were validated and prefixed.

**Why `ForTenant(id)` instead of a tenant argument on every member?** Same explicitness, one entry point. A view cannot
be used without a tenant, and a tenant store cannot be resolved without a view.

**Why validate tenant ids instead of escaping them?** Escaping schemes invite two ids that map to the same prefix.
A small alphabet with no separators cannot collide.

**Why `Result` instead of exceptions?** A missing file, a lost race or a throttled provider is an outcome the caller
must handle, and `Result` makes the compiler remind them. Exceptions remain for bugs and cancellation.

**Why are presigned members on `IFileStorage`?** One object per store, and a tenant view presigns inside its tenant's
prefix for free.

**Why refuse a feature a provider lacks instead of ignoring it?** Some S3-compatible services accept `If-None-Match`
and checksums and then ignore them. A silently ignored create-only condition overwrites data.

## AI quick reference

Conventions for generating code with this package. Each line is a rule.

```text
INJECT         [FromKeyedServices("name")] IFileStorage | ITenantFileStorage -> .ForTenant(tenantId). Unkeyed only if one store.
REGISTER       services.AddSharedKernelStorage().AddS3(configuration).AddStore("a").AddTenantStore("b"); config at
               SharedKernel:Storage:S3 (or :S3:{name} via AddS3(configuration, name)) and SharedKernel:Storage:Stores:{name}.
KEYS           Relative to the store/tenant: "2026/09/x.pdf". No leading '/', no '..', no '\'. Never add tenant prefixes by hand.
TENANT         ForTenant(requestContext.TenantId) from the authenticated principal. Never from headers or bodies.
PERSIST        FileReference (Store, TenantId, Key, ETag). Reopen: factory.Open(reference). Never persist URLs or buckets.
UPLOAD         UploadAsync(key, stream, new FileUploadOptions { ContentType, ContentLength = Request.ContentLength }, ct).
               Stream is caller-owned: not disposed, not rewound.
CREATE ONLY    Condition = WriteCondition.IfNotExists -> storage.already_exists. Never exists-then-write.
CONCURRENCY    Condition = WriteCondition.IfMatch(properties.ETag) -> storage.precondition_failed.
INTEGRITY      ChecksumSha256 = base64(SHA256(bytes)); non-seekable stream also needs ContentLength.
DOWNLOAD       await using FileDownload f = (await store.DownloadAsync(key, options, ct)).Value; Range = new ByteRange(from, to).
LIST           ListPageAsync(new FileListRequest { Prefix, Recursive = false, PageSize, ContinuationToken }) | ListAsync(prefix).
BROWSER UPLOAD CreateUploadFormAsync(key, new PresignedPostOptions { Expiry, MaxSize, ContentType = "image/" }). Not PUT for untrusted clients.
LARGE UPLOAD   StartMultipartUploadAsync -> CreateUploadPartUrlAsync(upload, n, expiry) -> CompleteMultipartUploadAsync(upload, parts).
COPY           source.CopyToAsync(key, destinationStoreOrView, newKey, options, ct).
ERRORS         Match result.Error.Code against StorageErrorCodes.*. Only cancellation throws; ListAsync throws StorageException.
TESTS          services.AddSharedKernelStorage().AddInMemoryStore("a").AddInMemoryTenantStore("b") (SharedKernel.Testing).
FORBIDDEN      IAmazonS3 or any cloud SDK type in application code; hand-built tenant prefixes; persisted presigned URLs.
```

## Compatibility and guarantees

- **Public API is tracked** with `Microsoft.CodeAnalysis.PublicApiAnalyzers`. Any change fails the build until it is
  recorded.
- **Every public member is documented**, including defaults, limits, error codes and exceptions. The XML
  documentation ships in the package.
- **Provider-neutral by rule.** Architecture tests fail CI if this package references a cloud SDK, a provider package
  or `SharedKernel.Configuration`.
- **One set of rules for every provider.** Keys, tenants, options and expiries are validated here, before a provider
  sees the request, so every provider returns the same errors.
- **Stable codes.** `StorageErrorCodes` values are part of the contract; a change is a breaking change.

## Deliberately not included

- **No implementation.** Providers live in `SharedKernel.Storage.S3` and `SharedKernel.Storage.Obs`; an in-memory
  store for tests lives in `SharedKernel.Testing`.
- **No bucket administration.** Creating buckets, lifecycle rules, policies and CORS belong to infrastructure code.
- **No archive tiers.** Tiers that need a restore step before a read are left to bucket lifecycle rules.
- **No ambient tenant.** The tenant is always an explicit argument, resolved at the edge.
- **No virus scanning or content inspection.** Quarantine uploads in one store and copy them on after scanning
  ([recipe 9](#9-move-a-file-out-of-quarantine-into-a-tenants-folder)).
- **No client-side encryption.** Use the provider's server-side encryption, or `SharedKernel.Cryptography`'s envelope
  encryption before uploading.
