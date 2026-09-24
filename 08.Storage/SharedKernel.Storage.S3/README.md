# SharedKernel.Storage.S3

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
[![Amazon S3](https://img.shields.io/badge/Amazon%20S3-verified-569A31?logo=amazons3&logoColor=white)](#what-happens-on-the-wire)
[![MinIO](https://img.shields.io/badge/MinIO-verified-C72E49?logo=minio&logoColor=white)](#minio-and-other-s3-compatible-services)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

> **Amazon S3, MinIO and any S3-compatible service for
> [`SharedKernel.Storage.Abstractions`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/08.Storage/SharedKernel.Storage.Abstractions/README.md):
> IAM-role credentials, KMS encryption, streaming multipart uploads, conditional writes and presigned uploads, with
> nothing S3-specific in your application code.**

Application code injects `IFileStorage` / `ITenantFileStorage` — the Abstractions README explains the programming
model. This package connects named stores to buckets: configuration, credentials, encryption, and the translation of
every call into S3 requests and of every S3 answer into a `storage.*` result.

| You get | So that |
| --- | --- |
| The AWS default credential chain | Pods use their IAM role (IRSA, EKS Pod Identity); no keys in configuration |
| Named connections — `AddS3(configuration, "Private")` | Buckets with different IAM users, accounts or regions in one service |
| `Encryption = Kms` / `S3Managed` per store | Every object written is encrypted the way the store requires |
| `KeyPrefix` per store | Several stores share a bucket without seeing each other |
| `ExpectedBucketOwner` | A deleted and re-created bucket in someone else's account is never written to |
| `S3Compatibility` flags | A service that lacks a feature refuses the request instead of silently ignoring it |
| `SharedKernel.Storage` traces and metrics | Every operation measured, without ever recording an object key |

## Contents

- [Install](#install)
- [Quick start](#quick-start)
- [Configuration](#configuration)
- [Credentials](#credentials)
- [MinIO and other S3-compatible services](#minio-and-other-s3-compatible-services)
- [What happens on the wire](#what-happens-on-the-wire)
- [Recipes](#recipes)
- [Logging and telemetry](#logging-and-telemetry)
- [IAM permissions](#iam-permissions)
- [Reference](#reference)
- [Pitfalls](#pitfalls)
- [Design decisions](#design-decisions)
- [AI quick reference](#ai-quick-reference)
- [Compatibility and guarantees](#compatibility-and-guarantees)

## Install

```shell
dotnet add package SharedKernel.Storage.S3
```

Reference it from the host only. It brings `SharedKernel.Storage.Abstractions`, `SharedKernel.Configuration` and
`AWSSDK.S3`.

## Quick start

```json
"SharedKernel": {
  "Storage": {
    "S3": { "Region": "eu-central-1" },
    "Stores": {
      "invoices":  { "Bucket": "acme-invoices", "Encryption": "Kms", "KmsKeyId": "alias/invoices" },
      "documents": { "Bucket": "acme-shared", "KeyPrefix": "documents/", "MaxPresignExpiry": "00:15:00" }
    }
  }
}
```

```csharp
builder.Services.AddSharedKernelStorage()
    .AddS3(builder.Configuration)
    .AddStore("invoices")
    .AddTenantStore("documents");

builder.Services.AddHealthChecks().AddStorageReadinessCheck("invoices");   // SharedKernel.ServiceDefaults.Storage
builder.WithStorageTelemetry();                                             // SharedKernel.ServiceDefaults
```

Every setting is validated when the host starts. A bad bucket name, a missing region or half a key pair fails
`IHost.StartAsync()` naming the connection or store and the setting.

## Configuration

### The connection — `SharedKernel:Storage:S3` (`S3StorageOptions`)

A named connection, `AddS3(configuration, "Private")`, reads `SharedKernel:Storage:S3:Private` instead.

| Setting | Default | |
| --- | --- | --- |
| `Region` | — | Required on AWS, e.g. `eu-central-1`. With `ServiceUrl`, the region requests are signed for |
| `ServiceUrl` | — | Endpoint of an S3-compatible service, e.g. `http://minio:9000`. Leave unset for AWS |
| `ForcePathStyle` | `false` | Address buckets as a path segment (MinIO and most self-hosted services) |
| `AccessKeyId` / `SecretAccessKey` / `SessionToken` | — | Static credentials. Leave unset on AWS; set both keys or neither |
| `MaxRetries` | `3` | SDK retries of throttled and failed requests (standard retry mode), 0 to 10 |
| `RequestTimeout` | `00:01:40` | Per HTTP request, 1 second to 1 hour. Each multipart part is its own request |
| `Compatibility` | everything on | What the endpoint supports — see [below](#minio-and-other-s3-compatible-services) |

### A store — `SharedKernel:Storage:Stores:{name}` (`S3StoreOptions`)

Read from configuration, then from the `configure` delegate of `AddStore(name, configure)`.

| Setting | Default | |
| --- | --- | --- |
| `Bucket` | — | Required: 3 to 63 lower-case letters, digits, `.` and `-` |
| `KeyPrefix` | — | A prefix every key of the store lives under, ending with `/`; the application never sees it |
| `Encryption` | `BucketDefault` | `S3Managed` (SSE-S3) or `Kms` (SSE-KMS) on every object written; `BucketDefault` sends no header |
| `KmsKeyId` | the `aws/s3` key | Key id, ARN or alias; only with `Encryption = Kms` |
| `DefaultTier` | `Default` | `InfrequentAccess` writes `STANDARD_IA` unless a request chooses otherwise |
| `MaxPresignExpiry` | 1 hour | The longest expiry of any presigned URL or form of the store, up to 7 days |
| `ExpectedBucketOwner` | — | The AWS account id the bucket must belong to; every request fails otherwise |
| `MultipartPartSize` | 16 MiB | Part size of multipart uploads, 5 MiB to 5 GiB; at most one part is buffered |

## Credentials

Leave `AccessKeyId` unset on AWS. The SDK's default credential chain then uses, in order: environment variables, the
shared profile, a web identity token (IRSA), EKS Pod Identity, the ECS task role and the EC2 instance profile — all
with temporary, automatically rotated credentials.

Use static keys only for MinIO and local development, and bind them from a secret store or environment variables
(`SharedKernel__Storage__S3__AccessKeyId`), never from a committed file.

Each connection creates one S3 client on first use, shares it among its stores and disposes it with the host. The
client is **not** registered as `IAmazonS3`, so it never collides with a client your service registers itself.

## MinIO and other S3-compatible services

```json
"S3": { "ServiceUrl": "http://minio:9000", "ForcePathStyle": true, "Region": "us-east-1",
        "AccessKeyId": "…", "SecretAccessKey": "…" }
```

Current MinIO releases support everything except conditional deletes: MinIO (`RELEASE.2025-09-07`) ignores `If-Match`
on `DeleteObject`, so a stale conditional delete removes the current object; conditional writes are honored.
`ConditionalWrites` switches off writes and deletes together, so there is no way yet to refuse only the delete (an open
follow-up, P-562). For a service that lacks a feature, switch it off: a request that needs it
then fails with `storage.not_supported` **before it is sent**, instead of being silently ignored.

| `Compatibility` flag | Default | Switch off when the service… |
| --- | --- | --- |
| `ConditionalWrites` | on | ignores `If-None-Match` / `If-Match` on writes and deletes |
| `Sha256Checksums` | on | ignores or rejects `x-amz-checksum-sha256` |
| `ObjectTags` | on | does not support object tagging |
| `KmsEncryption` | on | does not support SSE-KMS |
| `PresignedPost` | on | does not support browser form uploads |
| `ETagIsContentMd5` | on | returns ETags that are not the content's MD5 (full downloads are then requested as `bytes=0-`, which the SDK does not check against the ETag) |

```json
"S3": { "ServiceUrl": "https://s3.example.net", "Region": "auto", "Compatibility": { "ConditionalWrites": false } }
```

Huawei Cloud OBS has its own package with a verified profile:
[`SharedKernel.Storage.Obs`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/08.Storage/SharedKernel.Storage.Obs/README.md).

## What happens on the wire

| Operation | S3 requests |
| --- | --- |
| `UploadAsync` | One `PutObject` for content of known length up to `MultipartPartSize` (pass `ContentLength` for a request body); otherwise a multipart upload in `MultipartPartSize` parts. With `ChecksumSha256`, always one `PutObject` (≤ 5 GiB) that the provider verifies |
| `WriteCondition` | `If-None-Match: *` / `If-Match` on `PutObject` and `CompleteMultipartUpload` |
| `DownloadAsync` | `GetObject` with `Range`, `If-Match` and `versionId` as requested |
| `GetPropertiesAsync` / `ExistsAsync` | `HeadObject` |
| `DeleteAsync` / `DeleteManyAsync` | `DeleteObject` / `DeleteObjects` (1,000 keys per request) |
| `CopyAsync` / `CopyToAsync` | `CopyObject` when both stores share a connection and no destination condition is set (≤ 5 GiB); otherwise download and conditional upload, streamed, because several S3-compatible services ignore conditions on copies |
| `ListAsync` / `ListPageAsync` | `ListObjectsV2` (with a `/` delimiter for folders) |
| Presigned URLs, forms, part URLs | Signed locally with SigV4; the credentials are resolved asynchronously (no request to S3) |
| Health probe | `HeadBucket` |

| S3 answer | Result |
| --- | --- |
| 404 | `storage.not_found` |
| 403 | `storage.access_denied` (logged with the request id) |
| 412, 409 `ConditionalRequestConflict` | `storage.already_exists` (create-only) or `storage.precondition_failed` |
| 416 | `storage.invalid_range` |
| 400 `BadDigest` / checksum mismatch | `storage.checksum_mismatch` |
| 503 `SlowDown`, other 5xx, 429, timeouts, network failures | `storage.unavailable`, after the SDK's retries |
| 301 / wrong region | `storage.provider_error`, logged as EventId 8105 naming the fix |
| Anything else | `storage.provider_error`, logged with status, code and request id |

## Recipes

### 1. Run on EKS with an IAM role

```json
"S3": { "Region": "eu-central-1" },
"Stores": { "uploads": { "Bucket": "acme-uploads", "ExpectedBucketOwner": "123456789012" } }
```

Annotate the service account with the role (IRSA) or create a Pod Identity association; no key goes into the
configuration.

### 2. Buckets with different IAM users

```json
"S3": {
  "Public":  { "Region": "eu-central-1", "AccessKeyId": "…", "SecretAccessKey": "…" },
  "Private": { "Region": "eu-central-1", "AccessKeyId": "…", "SecretAccessKey": "…" }
}
```

```csharp
IStorageBuilder storage = builder.Services.AddSharedKernelStorage();
storage.AddS3(builder.Configuration, "Public").AddStore("assets");
storage.AddS3(builder.Configuration, "Private").AddTenantStore("documents");
```

Each named connection has its own client and credentials. Copies between stores of different connections stream
through the service.

### 3. Encrypt with a customer-managed KMS key

```json
"Stores": { "invoices": { "Bucket": "acme-invoices", "Encryption": "Kms", "KmsKeyId": "alias/invoices" } }
```

The key policy then also decides who can read the objects. Presigned upload URLs return the encryption headers the
client must send.

### 4. Several stores in one bucket

```json
"Stores": {
  "exports":  { "Bucket": "acme-data", "KeyPrefix": "exports/" },
  "imports":  { "Bucket": "acme-data", "KeyPrefix": "imports/", "DefaultTier": "InfrequentAccess" }
}
```

A store never sees keys outside its prefix, and copies between the two run server-side.

### 5. Local development with MinIO

```yaml
# docker-compose.yml
minio:
  image: quay.io/minio/minio:RELEASE.2025-09-07T16-13-09Z
  command: server /data
  ports: ["9000:9000"]
```

```json
"S3": { "ServiceUrl": "http://localhost:9000", "ForcePathStyle": true, "Region": "us-east-1",
        "AccessKeyId": "minioadmin", "SecretAccessKey": "minioadmin" }
```

### 6. Configure a store in code

```csharp
builder.Services.AddSharedKernelStorage()
    .AddS3(builder.Configuration, s3 => s3.MaxRetries = 5)
    .AddStore("reports", store =>
    {
        store.Bucket = "acme-reports";
        store.MaxPresignExpiry = TimeSpan.FromDays(1);
    });
```

## Logging and telemetry

Object keys are never logged, traced or measured — they often contain user data. Bucket names and S3 request ids are
logged so a failure can be traced with AWS support.

| EventId | Level | When |
| --- | --- | --- |
| 8100 | Warning | S3 denied an operation (403) |
| 8101 | Warning | S3 was unreachable, throttling or failing (`storage.unavailable`) |
| 8102 | Error | S3 rejected a request for another reason (`storage.provider_error`) |
| 8103 | Debug | An expected answer: not found, a failed condition, a bad range or checksum |
| 8104 | Warning | A store failed its readiness probe |
| 8105 | Error | The bucket is not served by the connection's region or endpoint |

`WithStorageTelemetry()` (`SharedKernel.ServiceDefaults`) exports:

| Signal | Name | Tags |
| --- | --- | --- |
| Span | `storage {operation}` (client) | `storage.store`, `storage.operation`, `storage.provider`, `error.type` on failure |
| Histogram | `storage.client.operation.duration` (s) | the same |
| Counter | `storage.client.bytes` (By) | `storage.store`, `storage.provider`, `storage.direction` (`upload`/`download`) |

## IAM permissions

| Feature | Actions (objects: `arn:aws:s3:::bucket/*`, listing and probe: `arn:aws:s3:::bucket`) |
| --- | --- |
| Download, properties, download URLs | `s3:GetObject` |
| Upload, upload URLs and forms, multipart | `s3:PutObject`, `s3:AbortMultipartUpload` |
| Object tags | `s3:PutObjectTagging` |
| Delete | `s3:DeleteObject` |
| Listing, readiness probe, and `ExistsAsync` returning `false` instead of `access_denied` | `s3:ListBucket` |
| `Encryption = Kms` | `kms:GenerateDataKey`, `kms:Decrypt` on the key |

Scope a store's policy to its `KeyPrefix` when several stores share a bucket.

## Reference

### Registration

| Method | Reads | Returns |
| --- | --- | --- |
| `IStorageBuilder.AddS3(configuration, configure?)` | `SharedKernel:Storage:S3`, connection name `S3` | `S3StorageBuilder` |
| `IStorageBuilder.AddS3(configuration, connectionName, configure?)` | `SharedKernel:Storage:S3:{connectionName}` | `S3StorageBuilder` |
| `IStorageBuilder.AddS3Compatible(connectionName, configuration, sp => (client, compatibility))` | a client you build | `S3StorageBuilder` |
| `S3StorageBuilder.AddStore(name, configure?)` | `SharedKernel:Storage:Stores:{name}` | the same builder |
| `S3StorageBuilder.AddTenantStore(name, configure?)` | `SharedKernel:Storage:Stores:{name}` | the same builder |

### Registered services

| Service | Lifetime | Notes |
| --- | --- | --- |
| `IFileStorage` keyed by store name | Singleton | Shared stores |
| `ITenantFileStorage` keyed by store name | Singleton | Tenant stores |
| `IFileStorage` / `ITenantFileStorage` unkeyed | Singleton | Resolve only when exactly one store of that kind exists |
| `IFileStorageFactory`, `IFileStorageHealthProbe` | Singleton | From `AddSharedKernelStorage()` |
| `IOptionsMonitor<S3StorageOptions>` (default, or named by connection), `IOptionsMonitor<S3StoreOptions>` (named by store) | Singleton | Validated on start |

`IAmazonS3` is deliberately not registered.

### Exceptions

| Exception | Thrown by | When |
| --- | --- | --- |
| `OptionsValidationException` | `IHost.StartAsync()`, or the first resolution of a store | A connection or store setting is invalid; every problem is listed |
| `ArgumentException` | `AddS3`, `AddStore`, `AddTenantStore` | An invalid connection or store name |
| `InvalidOperationException` | `AddS3`, `AddS3Compatible`, `AddStore` | A connection or store name registered twice |

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Put access keys in `appsettings.json` | IAM roles on AWS; a secret store elsewhere | Keys in files leak and never rotate |
| Point a connection at the wrong region | Set `Region` to the bucket's region | S3 answers 301; you get `storage.provider_error` and EventId 8105 |
| Register an `IAmazonS3` for these stores | Configure the connection | The package manages its own client per connection |
| Share one IAM user between stores that need different access | Use named connections | Each connection has its own credentials |
| Enable `ConditionalWrites` on a service that ignores them | Switch the flag off | Otherwise "create only" silently overwrites |
| Rely on `ExistsAsync` to catch a misnamed bucket | Add `AddStorageReadinessCheck(store)` | A `HEAD` answer has no error code: a missing bucket looks like a missing object there (other operations return `storage.provider_error`) |
| Expect server-side copies above 5 GiB | Copy by streaming (different connection) or upload again | `CopyObject` is limited to 5 GiB |

## Design decisions

**Why not register `IAmazonS3`?** A service often has its own S3 client. A shared registration made the last one
registered win, which is how S3 stores once talked to OBS. Each connection now owns a private client.

**Why refuse unsupported features instead of degrading?** An S3-compatible service that accepts `If-None-Match` and
ignores it turns "create only" into "overwrite". Refusing is the only safe answer.

**Why stream conditional copies instead of using `CopyObject`?** MinIO (and possibly others) ignore conditions on
`CopyObject`. A download followed by a conditional `PutObject` is enforced everywhere.

**Why checksum only with a known length?** A whole-object SHA-256 can only be verified by a single `PutObject`, which
needs the length; multipart checksums are checksums of parts. Asking for `ContentLength` keeps the promise honest.

**Why `WHEN_REQUIRED` checksum settings?** Several S3-compatible services reject the SDK's newer default checksum
headers. Checksums are sent when an operation needs one or a request asks for it.

## AI quick reference

```text
REGISTER       services.AddSharedKernelStorage().AddS3(configuration).AddStore("name").AddTenantStore("name2");
NAMED          AddS3(configuration, "Private") -> config SharedKernel:Storage:S3:Private. One per IAM user/account/region.
CONFIG         Connection: Region (AWS) | ServiceUrl + ForcePathStyle + Region (MinIO). Store: SharedKernel:Storage:Stores:{name}:Bucket.
CREDENTIALS    AWS: omit keys (default chain / IRSA / Pod Identity). MinIO/dev: AccessKeyId + SecretAccessKey from secrets.
ENCRYPTION     Store Encryption = "Kms" (+ KmsKeyId) | "S3Managed" | "BucketDefault".
PREFIX         Store KeyPrefix = "folder/" (ends with '/'); stores in one bucket copy server-side.
LIMITS         Store MaxPresignExpiry (default 1h, max 7d); MultipartPartSize 5 MiB..5 GiB (default 16 MiB).
COMPATIBILITY  Connection Compatibility { ConditionalWrites, Sha256Checksums, ObjectTags, KmsEncryption, PresignedPost, ETagIsContentMd5 }.
HEALTH         services.AddHealthChecks().AddStorageReadinessCheck("name") (SharedKernel.ServiceDefaults.Storage).
TELEMETRY      builder.WithStorageTelemetry() (SharedKernel.ServiceDefaults).
FORBIDDEN      Registering or injecting IAmazonS3 for these stores; keys in committed config; string buckets in application code.
```

## Compatibility and guarantees

- **Public API is tracked** with `Microsoft.CodeAnalysis.PublicApiAnalyzers`; every public member is documented.
- **Tested against real services.** Every behaviour runs against MinIO in CI (Testcontainers), and the
  [`DocumentsApi` sample](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/samples/DocumentsApi/README.md)
  runs its 50 scenarios against Amazon S3 (`eu-central-1`, two IAM users) before release.
- **`AWSSDK.S3` 4.x.** The SDK version is pinned centrally for the whole repository.
- **Never references `SharedKernel.Storage.Obs`.** OBS builds on this package, not the other way round (architecture
  test).
