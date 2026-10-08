# SharedKernel.Storage.Obs

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Tier: Adapter](https://img.shields.io/badge/tier-Adapter-6f42c1)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)
![Huawei OBS: verified](https://img.shields.io/badge/Huawei%20OBS-verified-CF0A2C?logo=huawei&logoColor=white)

> **Huawei Cloud OBS behind
> [`SharedKernel.Storage.Abstractions`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Infrastructure/Storage/SharedKernel.Storage.Abstractions/README.md),
> over OBS's S3-compatible API — with the OBS behaviour that would silently corrupt data handled for you.**

Application code injects `IFileStorage` / `ITenantFileStorage` exactly as it would for S3. This package adds the OBS
connection settings and a compatibility profile verified against a live OBS region, on top of the
[`SharedKernel.Storage.S3`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Infrastructure/Storage/SharedKernel.Storage.S3/README.md)
implementation. OBS and S3 stores can be registered side by side in one service.

| You get | So that |
| --- | --- |
| `AddObs(configuration)` | One call connects OBS; stores are added exactly as for S3 |
| The signing region read from the endpoint | `Endpoint` and AK/SK are usually the only connection settings |
| Conditions and checksums refused with `storage.not_supported` | OBS's habit of accepting and then ignoring them never overwrites data |
| Encrypted downloads that work | The AWS SDK's MD5 check no longer rejects encrypted OBS objects |
| The S3 package's logging, telemetry, error mapping and readiness probe | OBS stores behave and are observed like every other store |

## Install

```xml
<PackageReference Include="SharedKernel.Storage.Obs" />
```

The version comes from your central `SharedKernelVersion` property — every SharedKernel package ships at the same
version. See [Using the packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel#using-the-packages).

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Adapter — reference it from your **Infrastructure** project (or the Api/Worker host) |
| Depends on | `SharedKernel.Storage.S3` (and through it the Abstractions and `AWSSDK.S3`) |
| Namespaces | `SharedKernel.Storage` (registration), `SharedKernel.Storage.Obs` (options) |

## Quick start

```csharp
using SharedKernel.ServiceDefaults.HealthChecks;
using SharedKernel.Storage;

builder.Services.AddSharedKernelStorage()
    .AddObs(builder.Configuration)          // SharedKernel:Storage:Obs
    .AddStore("archive");                   // SharedKernel:Storage:Stores:archive

builder.Services.AddHealthChecks().AddSharedKernelReadiness();   // "storage-archive"
```

```json
{
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
}
```

```csharp
public sealed class Archive([FromKeyedServices("archive")] IFileStorage archive)
{
    public Task<Result<FileReference>> KeepAsync(string key, Stream content, CancellationToken ct) =>
        archive.UploadAsync(key, content, cancellationToken: ct);
}
```

## How it works

`AddObs` registers the connection named `Obs` through `AddS3Compatible`, with a client built from `ObsStorageOptions`
and this fixed compatibility profile:

| Capability | On OBS |
| --- | --- |
| Upload (single and multipart), download, range reads, properties, metadata, exists | Supported |
| Delete, batch delete, copy (server-side between OBS stores), listing with folders and paging | Supported |
| Presigned download and upload URLs, presigned forms with size and type limits, presigned multipart | Supported |
| Tenant stores, key prefixes, object tags, SSE (`S3Managed`, `Kms`) | Supported |
| `WriteCondition` (create-only, `If-Match`), conditional deletes, create-only upload URLs | `storage.not_supported` |
| `FileUploadOptions.ChecksumSha256` | `storage.not_supported` |

- **Refused before sending.** OBS's S3-compatible API accepts `If-None-Match`, `If-Match` and `x-amz-checksum-sha256`
  and then **ignores** them — it overwrites existing objects and stores bytes without checking them. Those requests
  fail with `storage.not_supported` before anything is sent.
- **Encrypted downloads.** The ETag of an encrypted OBS object is not the MD5 of its content, which makes the AWS SDK
  reject every full download. The profile requests full downloads as `bytes=0-`, which returns the same bytes
  without that check. Empty objects are handled too.
- **The signing region** is read from standard endpoints (`obs.{region}.myhuaweicloud.com`).
- **Credentials** are AK/SK, with `SecurityToken` for temporary (STS) credentials.

The profile was verified against a real OBS bucket in `tr-west-1`.

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

OBS cannot enforce them, so choose a key that cannot collide — for example `$"{Guid.CreateVersion7()}/{fileName}"` —
and record the key in your database, whose unique constraint does the enforcement.

### 3. Browser uploads

`CreateUploadFormAsync` works on OBS with the same size and content-type policy as on S3. Send the file part with a
plain `filename`, as browsers do: OBS rejects the `filename*=` form that .NET's
`MultipartFormDataContent.Add(content, name, fileName)` produces. From .NET, set the part's `ContentDisposition`
yourself:

```csharp
var file = new ByteArrayContent(bytes);
file.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data") { Name = "\"file\"", FileName = "\"photo.png\"" };
```

## Configuration

### The connection — `SharedKernel:Storage:Obs` (`ObsStorageOptions`)

Validated when the host starts: a missing AK/SK or an endpoint whose region cannot be read fails `IHost.StartAsync()`
naming the setting.

| Key | Type | Default | Meaning |
| --- | --- | --- | --- |
| `SharedKernel:Storage:Obs:Endpoint` | `string` | — (required) | The regional endpoint, an absolute `http`/`https` URL, e.g. `https://obs.tr-west-1.myhuaweicloud.com` |
| `SharedKernel:Storage:Obs:Region` | `string` | read from the endpoint | The signing region; required for a host other than `obs.{region}.myhuaweicloud.com` |
| `SharedKernel:Storage:Obs:AccessKeyId` | `string` | — (required) | AK; bind it from a secret store |
| `SharedKernel:Storage:Obs:SecretAccessKey` | `string` | — (required) | SK; bind it from a secret store |
| `SharedKernel:Storage:Obs:SecurityToken` | `string` | — | For temporary credentials, such as an agency's STS token |
| `SharedKernel:Storage:Obs:ForcePathStyle` | `bool` | `false` | Address buckets as a path segment |
| `SharedKernel:Storage:Obs:MaxRetries` | `int` | `3` | Retries of throttled and failed requests, 0 to 10 |
| `SharedKernel:Storage:Obs:RequestTimeout` | `TimeSpan` | `00:01:40` | Per HTTP request, 1 second to 1 hour |

### Stores — `SharedKernel:Storage:Stores:{name}`

Exactly the store settings of `SharedKernel.Storage.S3` — `Bucket`, `KeyPrefix`, `Encryption`, `KmsKeyId`,
`DefaultTier`, `MaxPresignExpiry`, `ExpectedBucketOwner`, `MultipartPartSize` — see its
[configuration](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Infrastructure/Storage/SharedKernel.Storage.S3/README.md#configuration).

## Reference

| Member | Purpose |
| --- | --- |
| `IStorageBuilder.AddObs(configuration, configure?)` | Adds the OBS connection (`SharedKernel:Storage:Obs`); returns an `S3StorageBuilder` for `AddStore` / `AddTenantStore` |
| `ObsStorageBuilderExtensions.ObsConnectionName` | `"Obs"` — the connection name and the `storage.provider` tag on spans and metrics |
| `ObsStorageOptions` | The connection settings above |

Registered services, error mapping, log events (8100–8105), telemetry and the `storage-{store}` readiness probe are
those of [`SharedKernel.Storage.S3`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Infrastructure/Storage/SharedKernel.Storage.S3/README.md#reference).
`AddObs` throws `InvalidOperationException` when called twice (the `Obs` connection is already registered).

## Testing

Application code is tested with the in-memory stores of
[`SharedKernel.Storage.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Infrastructure/Storage/SharedKernel.Storage.Testing/README.md)
(`AddSharedKernelStorage().AddInMemoryStore("archive")`). They support conditions and checksums that OBS refuses, so
cover the `storage.not_supported` path of OBS-backed code with a test of its own. The OBS wiring itself can only be
verified against a real OBS bucket; the [Shop sample](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/samples/Shop/README.md)'s Reports runs the OBS provider against MinIO.

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Rely on `WriteCondition` or checksums on OBS | Handle `storage.not_supported`, or use collision-free keys | OBS ignores them |
| Switch those features on through a custom S3 connection (`AddS3` against the OBS endpoint) | Use `AddObs` | You would silently overwrite data |
| Use a non-standard endpoint without `Region` | Set `Region` | The signing region cannot be read from the host |
| Post form uploads with `filename*=` | A plain `filename` | OBS rejects the file part |
| Commit AK/SK to configuration files | Bind them from a secret store or environment variables | Keys in files leak and never rotate |

## Design decisions

**Why build on the S3 package instead of the native Huawei SDK?** The native .NET SDK targets .NET Standard 2.0 and
is no longer actively released. OBS's S3-compatible API, driven by the maintained `AWSSDK.S3`, covers everything the
contracts need, so this package contains only configuration and the compatibility profile.

**Why refuse conditions and checksums rather than emulate them?** A read-then-write emulation of "create only" races,
and a client-side checksum proves nothing about what OBS stored. Refusing is the only honest answer.

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) ·
[Storage domain](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Infrastructure/Storage/README.md) ·
[MIT license](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
