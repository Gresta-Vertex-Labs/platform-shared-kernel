<div align="center">

# 🪣 SharedKernel Storage

**Object storage for multi-tenant .NET services — Amazon S3, MinIO and Huawei Cloud OBS behind one contract, with
tenant isolation, safe concurrent writes and presigned uploads that clients cannot abuse.**

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Amazon S3](https://img.shields.io/badge/Amazon%20S3-verified-569A31?logo=amazons3&logoColor=white)](SharedKernel.Storage.S3/README.md)
[![Huawei OBS](https://img.shields.io/badge/Huawei%20OBS-verified-CF0A2C?logo=huawei&logoColor=white)](SharedKernel.Storage.Obs/README.md)
[![MinIO](https://img.shields.io/badge/MinIO-verified-C72E49?logo=minio&logoColor=white)](SharedKernel.Storage.S3/README.md#minio-and-other-s3-compatible-services)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](../LICENSE)
![Packages: 3](https://img.shields.io/badge/packages-3-informational)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

[Packages](#the-packages) · [Architecture](#architecture) · [10-minute start](#a-file-service-in-10-minutes) · [Providers](#what-each-provider-supports) · [Sample](#see-it-run) · [Guarantees](#what-you-can-rely-on)

</div>

---

```csharp
IStorageBuilder storage = builder.Services.AddSharedKernelStorage();

storage.AddS3(builder.Configuration)
    .AddStore("invoices")          // one bucket (or a prefix of one), shared by the whole service
    .AddTenantStore("documents");  // every tenant confined to its own key prefix

storage.AddObs(builder.Configuration)
    .AddStore("archive");          // Huawei Cloud OBS, side by side with S3
```

That is the whole registration. Buckets, prefixes, encryption, credentials and link lifetimes are configuration;
application code only names a store:

```csharp
public sealed class InvoiceFiles([FromKeyedServices("invoices")] IFileStorage invoices)
{
    public Task<Result<FileReference>> SaveAsync(Guid id, Stream pdf, CancellationToken ct) =>
        invoices.UploadAsync($"{id}.pdf", pdf, new FileUploadOptions
        {
            ContentType = "application/pdf",
            Condition = WriteCondition.IfNotExists,   // an issued invoice is never overwritten
        }, ct);
}
```

## Why

Every service that stores files makes the same decisions, and each one fails quietly when it is wrong:

| The problem | What the packages do |
| --- | --- |
| A tenant id or `../` in a key reaches another customer's files | Tenant stores keep each tenant (a typed `TenantId`) under its own prefix; keys are validated before any request, so nothing can leave it |
| Two requests upload the same key and the second silently wins | `WriteCondition.IfNotExists` and `IfMatch(etag)`, enforced atomically by the provider |
| A browser upload URL accepts a 20 GB file or an HTML page | Presigned **forms** carry a signed size range and content-type policy the provider enforces |
| Large uploads are buffered in memory, or pass through the service at all | Streams end to end; multipart for unknown lengths; presigned multipart lets clients upload gigabytes directly |
| Bucket names, regions and credentials are scattered through code | Named stores: code names a purpose, configuration says where it lives; IAM roles by default |
| A throttled or unreachable provider throws in the middle of a request | Every expected failure is a `Result` with a stable `storage.*` code; outages are `storage.unavailable` |
| An S3-compatible service ignores a header and data is silently overwritten | Per-provider feature flags: a request that needs a missing feature fails **before** it is sent |

## The packages

| Package | Reference it from | What it gives you |
| --- | --- | --- |
| [**SharedKernel.Storage.Abstractions**](SharedKernel.Storage.Abstractions/README.md) | application code | `IFileStorage` (upload, download, ranges, properties, delete, batch delete, copy, listing, presigned URLs, forms and multipart), `ITenantFileStorage`, `IFileStorageFactory`, `FileReference`, the `storage.*` error codes, and the store registry. No cloud SDK |
| [**SharedKernel.Storage.S3**](SharedKernel.Storage.S3/README.md) | the host | Amazon S3, MinIO and any S3-compatible service: `AddS3`, named connections, IAM-role or key credentials, SSE-S3/SSE-KMS, key prefixes, feature flags, OpenTelemetry spans and metrics |
| [**SharedKernel.Storage.Obs**](SharedKernel.Storage.Obs/README.md) | the host | Huawei Cloud OBS: `AddObs`, the S3 implementation with OBS configuration and the OBS behaviour it was verified against |

Application code references only the Abstractions; the host references one or both providers. Related packages
outside this folder:

| Package | Adds |
| --- | --- |
| [`SharedKernel.ServiceDefaults`](../13.ServiceDefaults/SharedKernel.ServiceDefaults/README.md) | `AddHealthChecks().AddSharedKernelReadiness()` — maps each store's `storage-{store}` readiness probe to a Kubernetes readiness check; `WithStorageTelemetry()` — exports the `SharedKernel.Storage` traces and metrics |
| [`SharedKernel.Storage.Testing`](../16.Testing/SharedKernel.Storage.Testing/README.md) | `AddInMemoryStore(name)` / `AddInMemoryTenantStore(name)` — an in-memory store with the production rules, for unit tests |

## Architecture

### A request, end to end

```text
application ── [FromKeyedServices("documents")] ITenantFileStorage ── .ForTenant(tenantId) ─┐
                                                                                          ▼
Abstractions     tenant view      validate key and options                 ── invalid → storage.invalid_* (no I/O)
                                  "contracts/nda.pdf" → "tenants/{tenantId}/contracts/nda.pdf"
                                                                                          ▼
S3 provider      store            + store prefix "documents/"  → bucket "acme-documents"
                                  feature check (S3Compatibility)          ── missing → storage.not_supported
                                  span "storage upload", duration metric   (object keys never recorded)
                                                                                          ▼
                 connection       one AWS SDK client per connection: credentials, region, retries, timeouts
                                                                                          ▼
Amazon S3 / MinIO / OBS           PutObject · multipart · CopyObject · presigned SigV4 URLs and forms
                                                                                          ▼
back to the caller                Result<FileReference> with the key "contracts/nda.pdf" — never the prefix
                                  404 → not_found · 412 → already_exists / precondition_failed · 5xx/503 → unavailable
```

### Stores, connections and buckets

```text
connection "S3"  (SharedKernel:Storage:S3)          connection "Obs"  (SharedKernel:Storage:Obs)
   ├─ store "invoices"   → bucket acme-invoices        └─ store "archive" → bucket acme-archive
   └─ store "documents"  → bucket acme-docs, prefix documents/, tenant-scoped
                              └─ tenants/{tenant A}/…   tenants/{tenant B}/…
```

- **A store** is a bucket, optionally narrowed to a key prefix, with its own encryption, default tier and maximum
  link lifetime (`SharedKernel:Storage:Stores:{name}`).
- **A connection** is one set of credentials and one endpoint. Stores on the same connection copy server-side;
  buckets with different IAM users use named connections (`AddS3(configuration, "Private")`).
- **A tenant store** is only reachable through `ForTenant(tenantId)` — a `SharedKernel.Execution.Tenancy.TenantId`,
  never a string; the provider never sees a key outside the tenant's
  prefix, and the caller never sees the prefix.

### Package dependencies

```text
SharedKernel.Storage.Obs ──→ SharedKernel.Storage.S3 ──→ SharedKernel.Storage.Abstractions ──→ SharedKernel.Primitives,
      (Adapter)                  (Adapter)   │           (Abstractions tier)                    SharedKernel.Execution
                                             └──→ AWSSDK.S3, SharedKernel.Configuration            (Foundation)
```

The Abstractions reference Foundation packages only and no cloud SDK; S3 never references OBS (Obs → S3 is the one
declared adapter-to-adapter edge). Tiers are enforced by the build, the rest by architecture tests.

## A file service in 10 minutes

### 1. Packages

```shell
dotnet add package SharedKernel.Storage.S3                 # or SharedKernel.Storage.Obs, or both
dotnet add package SharedKernel.ServiceDefaults            # readiness checks and telemetry (optional)
```

Application and domain projects reference `SharedKernel.Storage.Abstractions` only.

### 2. Configuration

```json
{
  "SharedKernel": {
    "Storage": {
      "S3": { "Region": "eu-central-1" },
      "Stores": {
        "invoices":  { "Bucket": "acme-invoices", "Encryption": "Kms" },
        "documents": { "Bucket": "acme-docs", "KeyPrefix": "documents/", "MaxPresignExpiry": "00:15:00" }
      }
    }
  }
}
```

No credentials: on AWS the SDK's default chain finds the pod's IAM role (IRSA, EKS Pod Identity), the ECS task role
or the instance profile. For MinIO or local development, bind `AccessKeyId`/`SecretAccessKey` from a secret store.
Every setting is validated when the host starts.

### 3. Registration

```csharp
builder.Services.AddSharedKernelStorage()
    .AddS3(builder.Configuration)
    .AddStore("invoices")
    .AddTenantStore("documents");

builder.Services.AddHealthChecks()
    .AddSharedKernelReadiness();   // "storage-invoices" and "storage-documents": a HEAD on each store's bucket

builder.WithStorageTelemetry();
```

Every store registers its own readiness probe (`IReadinessProbe`, named `storage-{store}`);
`AddSharedKernelReadiness()` maps every registered probe to a `ready` health check.

### 4. Upload and download through your API

```csharp
app.MapPut("/invoices/{id:guid}", async (Guid id, HttpRequest request,
    [FromKeyedServices("invoices")] IFileStorage invoices, CancellationToken ct) =>
{
    Result<FileReference> saved = await invoices.UploadAsync($"{id}.pdf", request.Body, new FileUploadOptions
    {
        ContentType = request.ContentType,
        ContentLength = request.ContentLength,   // a request body cannot report its length itself
        Condition = WriteCondition.IfNotExists,
    }, ct);
    return saved.ToCreated(_ => $"/invoices/{id}");   // 201 with the FileReference, or a problem response
});

app.MapGet("/invoices/{id:guid}", async (Guid id, [FromKeyedServices("invoices")] IFileStorage invoices,
    CancellationToken ct) =>
{
    Result<FileDownload> file = await invoices.DownloadAsync($"{id}.pdf", cancellationToken: ct);
    return file.ToHttpResult(f => TypedResults.Stream(f.Content, f.Properties.ContentType)); // disposes the stream
});
```

Nothing is buffered: the request body streams into the provider, and the provider's response streams back.
The typed results (`ToCreated`, `ToHttpResult`; `SharedKernel.Presentation.WebApi`) answer a failure with an RFC 9457
problem whose `errorCode` is the storage code: `storage.not_found` → 404, `storage.unavailable` → 503,
`storage.already_exists` → 409, or 412 when the request itself carried `If-Match` or `If-None-Match`.

### 5. Let the browser upload directly

```csharp
PresignedPost form = (await documents.ForTenant(tenantId).CreateUploadFormAsync($"avatars/{userId}.png",
    new PresignedPostOptions
    {
        Expiry = TimeSpan.FromMinutes(5),
        MaxSize = 2 * 1024 * 1024,     // the provider rejects anything larger
        ContentType = "image/",        // …and anything that is not an image
    }, ct)).Value;
// Return form.Url and form.Fields; the browser POSTs them with the file as the last field, named "file".
```

The file never passes through the service. For files too large for one request, use presigned multipart
(`StartMultipartUploadAsync` → `CreateUploadPartUrlAsync` → `CompleteMultipartUploadAsync`).

### 6. Keep tenants apart

```csharp
public sealed class ContractFiles([FromKeyedServices("documents")] ITenantFileStorage documents, IRequestContext request)
{
    public Task<Result<FileListPage>> ListAsync(CancellationToken ct) =>
        documents.ForTenant(request.TenantId!.Value).ListPageAsync(new FileListRequest { Prefix = "contracts/" }, ct);
}
```

Take the tenant from the authenticated request (`IRequestContext.TenantId`, a `TenantId?`). A view for one tenant
cannot read, list, copy or delete anything of another — not even with a key such as `../{other}/x`, which is
rejected before any request is sent.

### 7. Test without a bucket

```csharp
services.AddSharedKernelStorage()
    .AddInMemoryStore("invoices")
    .AddInMemoryTenantStore("documents");   // SharedKernel.Storage.Testing: same validation and tenant rules
```

## What each provider supports

| Capability | Amazon S3 | MinIO | Huawei OBS |
| --- | :---: | :---: | :---: |
| Upload, download, range reads, properties, metadata, delete, batch delete | ✅ | ✅ | ✅ |
| Copy (server-side on one connection), listing with folders and paging | ✅ | ✅ | ✅ |
| Presigned download and upload URLs, presigned forms, presigned multipart | ✅ | ✅ | ✅ |
| Tenant stores, key prefixes, object tags | ✅ | ✅ | ✅ |
| SSE-S3 / SSE-KMS encryption | ✅ | needs a KMS | ✅ |
| Create-only and `If-Match` writes | ✅ | ✅ | ⛔ `storage.not_supported` — OBS ignores them |
| Provider-verified SHA-256 checksums | ✅ | ✅ | ⛔ `storage.not_supported` — OBS ignores them |
| IAM roles / default AWS credential chain | ✅ | — | — |

⛔ means the request is refused **before** it is sent. OBS accepts those headers and then ignores them, which would
silently overwrite data or store unchecked bytes.

## See it run

[**samples/DocumentsApi**](../samples/DocumentsApi/README.md) is a file service on all three packages: two S3
connections with separate IAM users, a tenant store and an OBS store, behind an HTTP API.

```bash
dotnet pack Platform.SharedKernel.slnx -c Release -o ./nupkgs -p:MinVerVersionOverride=1.0.0-local.1
dotnet test samples/DocumentsApi/DocumentsApi.Tests -p:SharedKernelPackageVersion=1.0.0-local.1
```

Its 50 scenarios run against MinIO in CI, and against real Amazon S3 and Huawei OBS when credentials are supplied.
The last live run passed 100 of 100 (2026-09-22, S3 `eu-central-1`, OBS `tr-west-1`).

## What you can rely on

| Guarantee | How |
| --- | --- |
| **No cross-tenant access** | Tenant views prefix every key with a typed `TenantId`; keys are validated (no `..`, no leading `/`, no escaping tricks); the provider store is never handed out |
| **No silent overwrites** | Conditional writes checked by the provider; conditional copies go through a conditional PUT because some services ignore conditions on copies |
| **No silent degradation** | A feature the provider lacks is refused before the request, never dropped |
| **No surprises at run time** | Buckets, prefixes, credentials, regions and limits are validated when the host starts |
| **No unbounded memory** | Streams in and out; at most one multipart part in memory |
| **No exceptions for expected failures** | `Result` with a stable `storage.*` code; only cancellation throws |
| **No leaked internals** | Errors name the store and your key, never the bucket or endpoint; object keys are never logged or traced |
| **Tested for real** | Every provider against MinIO in CI; every scenario against real S3 and OBS before release |

**Deliberately out of scope:** Azure Blob Storage and Google Cloud Storage (no provider yet), archive tiers that need
a restore step, bucket administration (creation, lifecycle, policies), antivirus scanning, client-side encryption.

## Where to go next

| Topic | Read |
| --- | --- |
| The programming model: stores, tenants, conditions, listing, copies, presigned requests, errors | [Abstractions](SharedKernel.Storage.Abstractions/README.md) |
| AWS and MinIO: configuration, credentials, several connections, encryption, IAM permissions | [S3](SharedKernel.Storage.S3/README.md) |
| Huawei Cloud OBS: configuration, what OBS supports | [Obs](SharedKernel.Storage.Obs/README.md) |
| A complete service, tested against the real clouds | [samples/DocumentsApi](../samples/DocumentsApi/README.md) |
| Readiness checks and telemetry | [ServiceDefaults](../13.ServiceDefaults/SharedKernel.ServiceDefaults/README.md) |
| The in-memory store for tests | [Storage.Testing](../16.Testing/SharedKernel.Storage.Testing/README.md) |
| Maintainer rules and design decisions | [CLAUDE.md](CLAUDE.md) |
