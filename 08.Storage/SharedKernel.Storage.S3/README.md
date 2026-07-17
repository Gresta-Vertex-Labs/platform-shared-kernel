# SharedKernel.Storage.S3

AWS S3 / MinIO implementation of [`SharedKernel.Storage.Abstractions`](../SharedKernel.Storage.Abstractions/README.md). Provides `S3FileStorage` (`IFileStorage`) and `S3BlobUriGenerator` (`IBlobUriGenerator`), backed by `AWSSDK.S3`'s `IAmazonS3`. MinIO is served through the **identical** code path — no MinIO-specific branch exists anywhere in this package — via `ServiceUrl` + `ForcePathStyle` configuration.

## Included Types

- `S3FileStorage` — sealed `IFileStorage` implementation; `UploadAsync` uses `TransferUtility` for multipart-aware streaming; `CopyAsync`/`DeleteManyAsync`/`ListAsync`/`CheckHealthAsync` map to `CopyObjectAsync`/chunked `DeleteObjectsAsync`/hand-paged `ListObjectsV2Async`/`HeadBucketAsync`
- `S3BlobUriGenerator` — sealed `IBlobUriGenerator` implementation; presigns via `IAmazonS3`'s native request presigning, clamped to the S3-family 7-day expiry maximum
- `S3StorageOptions` — Options-pattern configuration, validated at startup
- `S3StorageConstants` — internal magic-string discipline constants (`MaxBatchDeleteKeys = 1000`)
- `AddSharedKernelS3Storage(IConfiguration)` — DI registration entry point

## Install

```xml
<ProjectReference Include="..\SharedKernel.Storage.S3\SharedKernel.Storage.S3.csproj" />
```

Or, once published, reference the NuGet package `SharedKernel.Storage.S3` (which brings in `SharedKernel.Storage.Abstractions` transitively).

## Setup

```csharp
services.AddSharedKernelS3Storage(configuration);

// Application code injects the abstraction, never Amazon.S3.IAmazonS3 directly:
public sealed class DocumentService(IFileStorage fileStorage, IBlobUriGenerator blobUriGenerator)
{
    // ...
}
```

`AddSharedKernelS3Storage` binds and validates `S3StorageOptions`, registers `IAmazonS3` as a **singleton** (thread-safe and connection-pooled — never scoped/transient), and registers `IFileStorage`/`IBlobUriGenerator` as singletons backed by `S3FileStorage`/`S3BlobUriGenerator`. A misconfigured section fails at `IHost.StartAsync()`, not at first upload.

## Configuration reference

Binds from the `SharedKernel:Storage:S3` section (`S3StorageOptions.SectionName`):

| Property | Type | Required | Notes |
| --- | --- | --- | --- |
| `AccessKeyId` | `string` | Yes | Access key id used to authenticate against the provider. |
| `SecretAccessKey` | `string` | Yes | Secret access key used to authenticate against the provider. |
| `ServiceUrl` | `string?` | Conditional | `null` targets real AWS S3 (via `Region`); set to a MinIO/custom S3-compatible endpoint URL to redirect the identical code path there. |
| `Region` | `string?` | Conditional | AWS region system name (e.g. `"eu-central-1"`). **Required when `ServiceUrl` is `null`**; ignored otherwise. |
| `ForcePathStyle` | `bool` | No (default `false`) | `true` addresses buckets as path segments (`https://host/bucket/key`) instead of subdomains — required for MinIO and most self-hosted S3-compatible endpoints. |
| `DefaultBucket` | `string?` | No | Optional convenience default bucket for single-bucket services. Not read by `AddSharedKernelS3Storage` itself — a convenience for consuming-service code. |

```json
{
  "SharedKernel": {
    "Storage": {
      "S3": {
        "AccessKeyId": "AKIA...",
        "SecretAccessKey": "...",
        "Region": "eu-central-1",
        "ForcePathStyle": false
      }
    }
  }
}
```

## MinIO configuration

Point `ServiceUrl` at the MinIO endpoint and set `ForcePathStyle = true`. `Region` is ignored once `ServiceUrl` is set — everything else (upload, download, copy, batch-delete, streaming list, presigned URLs, connectivity probe) runs through the exact same `S3FileStorage`/`S3BlobUriGenerator` code as real AWS:

```json
{
  "SharedKernel": {
    "Storage": {
      "S3": {
        "ServiceUrl": "http://localhost:9000",
        "AccessKeyId": "minioadmin",
        "SecretAccessKey": "minioadmin",
        "ForcePathStyle": true
      }
    }
  }
}
```

> Presigned URLs against a plain-HTTP MinIO endpoint work correctly — `S3BlobUriGenerator` derives the presign `Protocol` from the client's own `ServiceURL` scheme rather than defaulting to HTTPS unconditionally.

## Usage

See [`SharedKernel.Storage.Abstractions`'s README](../SharedKernel.Storage.Abstractions/README.md) for the full `IFileStorage`/`IBlobUriGenerator` usage guide (upload/download/copy/batch-delete/streaming-list/health-probe/presigned URLs) — this package is a pure implementation and adds no members beyond the abstraction's own contract.

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) — see [08.Storage/CLAUDE.md](../CLAUDE.md) for the full interface contracts, status-code error mapping, and AOT posture.
