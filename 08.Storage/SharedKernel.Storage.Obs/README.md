# SharedKernel.Storage.Obs

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
[![Huawei OBS](https://img.shields.io/badge/Huawei%20OBS-verified%20tr--west--1-CF0A2C?logo=huawei&logoColor=white)](#what-obs-supports)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

> **Huawei Cloud OBS for
> [`SharedKernel.Storage.Abstractions`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/08.Storage/SharedKernel.Storage.Abstractions/README.md),
> over OBS's S3-compatible API — with the OBS behaviour that would silently corrupt data handled for you.**

Application code injects `IFileStorage` / `ITenantFileStorage` exactly as it would for S3. This package adds OBS
configuration and a compatibility profile verified against a live OBS region, on top of the
[`SharedKernel.Storage.S3`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/08.Storage/SharedKernel.Storage.S3/README.md)
implementation. OBS and S3 stores can be registered side by side in one service.

## Contents

- [Install](#install)
- [Quick start](#quick-start)
- [Configuration](#configuration)
- [What OBS supports](#what-obs-supports)
- [What the package handles for you](#what-the-package-handles-for-you)
- [Recipes](#recipes)
- [Reference](#reference)
- [Pitfalls](#pitfalls)
- [Design decisions](#design-decisions)
- [AI quick reference](#ai-quick-reference)

## Install

```shell
dotnet add package SharedKernel.Storage.Obs
```

Reference it from the host only. It brings `SharedKernel.Storage.S3` and, through it, the Abstractions.

## Quick start

```json
"SharedKernel": {
  "Storage": {
    "Obs": {
      "Endpoint": "https://obs.tr-west-1.myhuaweicloud.com",
      "AccessKeyId": "<AK from a secret store>",
      "SecretAccessKey": "<SK from a secret store>"
    },
    "Stores": {
      "archive": { "Bucket": "acme-archive", "Encryption": "S3Managed", "MaxPresignExpiry": "01:00:00" }
    }
  }
}
```

```csharp
builder.Services.AddSharedKernelStorage()
    .AddObs(builder.Configuration)
    .AddStore("archive");

builder.Services.AddHealthChecks().AddStorageReadinessCheck("archive");   // SharedKernel.ServiceDefaults.Storage
```

```csharp
public sealed class Archive([FromKeyedServices("archive")] IFileStorage archive) { /* … */ }
```

## Configuration

### The connection — `SharedKernel:Storage:Obs` (`ObsStorageOptions`)

| Setting | Default | |
| --- | --- | --- |
| `Endpoint` | — | Required: the regional endpoint, e.g. `https://obs.tr-west-1.myhuaweicloud.com` |
| `Region` | read from the endpoint | The signing region. Read from `obs.{region}.myhuaweicloud.com`; required for any other host |
| `AccessKeyId` / `SecretAccessKey` | — | Required (AK/SK). Bind them from a secret store |
| `SecurityToken` | — | For temporary credentials, such as an agency's STS token |
| `ForcePathStyle` | `false` | Address buckets as a path segment |
| `MaxRetries` | `3` | Retries of throttled and failed requests, 0 to 10 |
| `RequestTimeout` | `00:01:40` | Per HTTP request, 1 second to 1 hour |

### Stores — `SharedKernel:Storage:Stores:{name}`

Exactly the store settings of `SharedKernel.Storage.S3`: `Bucket`, `KeyPrefix`, `Encryption`, `KmsKeyId`,
`DefaultTier`, `MaxPresignExpiry`, `MultipartPartSize` (see its
[store settings](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/08.Storage/SharedKernel.Storage.S3/README.md#a-store--sharedkernelstoragestoresname-s3storeoptions)).

Everything is validated when the host starts: a missing AK/SK or an endpoint whose region cannot be read fails
`IHost.StartAsync()` naming the setting.

## What OBS supports

Verified against OBS `tr-west-1` on 2026-09-22 with the
[`DocumentsApi` sample](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/samples/DocumentsApi/README.md)
(50 scenarios, all passing).

| Capability | On OBS |
| --- | --- |
| Upload (single and multipart), download, range reads, properties, metadata, exists | ✅ |
| Delete, batch delete, copy (server-side between OBS stores), listing with folders and paging | ✅ |
| Presigned download and upload URLs, presigned forms with size and type limits, presigned multipart | ✅ |
| Tenant stores, key prefixes, object tags, SSE (`S3Managed`, `Kms`) | ✅ |
| `WriteCondition` (create-only, `If-Match`), conditional deletes, create-only upload URLs | ⛔ `storage.not_supported` |
| `FileUploadOptions.ChecksumSha256` | ⛔ `storage.not_supported` |

⛔ requests are refused **before** anything is sent. OBS's S3-compatible API accepts `If-None-Match`, `If-Match` and
`x-amz-checksum-sha256` and then **ignores** them — it overwrites existing objects and stores bytes without checking
them. Pretending those guarantees held would corrupt data silently.

## What the package handles for you

- **Encrypted downloads.** The ETag of an encrypted OBS object is not the MD5 of its content, which makes the AWS SDK
  reject every full download. The OBS profile requests full downloads as `bytes=0-`, which returns the same bytes
  without that check. Empty objects are handled too.
- **The signing region.** Read from standard endpoints, so `Endpoint` is usually the only address setting.

## Recipes

### 1. Keep an OBS archive next to S3 stores

```csharp
IStorageBuilder storage = builder.Services.AddSharedKernelStorage();
storage.AddS3(builder.Configuration).AddStore("uploads");
storage.AddObs(builder.Configuration).AddStore("archive");

// Copies between the two stream through the service (different connections):
Result<FileReference> archived = await uploads.CopyToAsync(key, archive, $"2026/{key}", cancellationToken: ct);
```

### 2. Create-only semantics on OBS

OBS cannot enforce them, so choose a key that cannot collide instead — for example
`$"{Guid.CreateVersion7()}/{fileName}"` — and record the key in your database, whose unique constraint does the
enforcement.

### 3. Browser uploads

`CreateUploadFormAsync` works on OBS with the same size and content-type policy as on S3. Send the file part with a
plain `filename`, as browsers do. OBS rejects the `filename*=` form that .NET's
`MultipartFormDataContent.Add(content, name, fileName)` produces; from .NET, set the part's `ContentDisposition`
yourself:

```csharp
var file = new ByteArrayContent(bytes);
file.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data") { Name = "\"file\"", FileName = "\"photo.png\"" };
```

## Reference

| Member | Purpose |
| --- | --- |
| `IStorageBuilder.AddObs(configuration, configure?)` | Adds the OBS connection (`SharedKernel:Storage:Obs`, connection name `Obs`); returns an `S3StorageBuilder` for `AddStore` / `AddTenantStore` |
| `ObsStorageBuilderExtensions.ObsConnectionName` | `"Obs"` — the `storage.provider` tag on spans and metrics |
| `ObsStorageOptions` | The connection settings above |

Logging, telemetry, error mapping and registered services are those of `SharedKernel.Storage.S3`.

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Rely on `WriteCondition` or checksums on OBS | Handle `storage.not_supported`, or use collision-free keys | OBS ignores them |
| Switch those features on through a custom S3 connection | Use `AddObs` | You would silently overwrite data |
| Use a non-standard endpoint without `Region` | Set `Region` | The signing region cannot be read from the host |
| Post form uploads with `filename*=` | A plain `filename` | OBS rejects the file part |

## Design decisions

**Why build on the S3 package instead of the native Huawei SDK?** The native .NET SDK targets .NET Standard 2.0, was
last released in 2022 and is maintained from a personal account. OBS's S3-compatible API, driven by the actively
maintained `AWSSDK.S3`, covers everything the contracts need. The old standalone OBS implementation was a line-by-line
copy of the S3 one; this package now contains only configuration and the compatibility profile.

**Why refuse conditions and checksums rather than emulate them?** A read-then-write emulation of "create only" races,
and a client-side checksum proves nothing about what OBS stored. Refusing is the only honest answer.

## AI quick reference

```text
REGISTER       services.AddSharedKernelStorage().AddObs(configuration).AddStore("name");  config SharedKernel:Storage:Obs.
CONFIG         Endpoint = "https://obs.{region}.myhuaweicloud.com", AccessKeyId, SecretAccessKey (secrets). Region only for other hosts.
STORES         SharedKernel:Storage:Stores:{name}:Bucket (same settings as S3 stores).
NOT SUPPORTED  WriteCondition, conditional delete, CreateOnly upload URLs, ChecksumSha256 -> storage.not_supported. Do not work around.
SUPPORTED      Everything else in IFileStorage, including tags, SSE, forms and multipart.
FORMS          File part with plain filename (set ContentDisposition by hand from .NET).
```
