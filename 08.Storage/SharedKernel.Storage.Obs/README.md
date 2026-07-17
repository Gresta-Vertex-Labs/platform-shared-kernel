# SharedKernel.Storage.Obs

Huawei Cloud OBS implementation of [`SharedKernel.Storage.Abstractions`](../SharedKernel.Storage.Abstractions/README.md), consumed over OBS's S3-compatible endpoint via `AWSSDK.S3` (the native `HuaweiCloud.ESDK.OBS.Core` SDK was rejected — last published November 2022, .NET Standard 2.0, personal-account maintained, no AOT story). Provides `ObsFileStorage` (`IFileStorage`) and `ObsBlobUriGenerator` (`IBlobUriGenerator`).

**Independent sibling package of [`SharedKernel.Storage.S3`](../SharedKernel.Storage.S3/README.md) — this package never references it, and never will.** Both happen to sit on `AWSSDK.S3` because OBS exposes an S3-compatible API, but they are built, versioned, and configured as fully independent providers.

## Included Types

- `ObsFileStorage` — sealed `IFileStorage` implementation targeting the OBS S3-compatible endpoint; mirrors `S3FileStorage`'s implementation shape one-for-one, but is its own type, not a shared base
- `ObsBlobUriGenerator` — sealed `IBlobUriGenerator` implementation; presigns via `IAmazonS3`'s native request presigning, clamped to the S3-family 7-day expiry maximum
- `ObsStorageOptions` — Options-pattern configuration, validated at startup
- `ObsStorageConstants` — internal magic-string discipline constants (`MaxBatchDeleteKeys = 1000`), independently declared from `S3StorageConstants`
- `AddSharedKernelObsStorage(IConfiguration)` — DI registration entry point

## Install

```xml
<ProjectReference Include="..\SharedKernel.Storage.Obs\SharedKernel.Storage.Obs.csproj" />
```

Or, once published, reference the NuGet package `SharedKernel.Storage.Obs` (which brings in `SharedKernel.Storage.Abstractions` transitively).

## Setup

```csharp
services.AddSharedKernelObsStorage(configuration);

// Application code injects the abstraction, never Amazon.S3.IAmazonS3 directly:
public sealed class DocumentService(IFileStorage fileStorage, IBlobUriGenerator blobUriGenerator)
{
    // ...
}
```

`AddSharedKernelObsStorage` binds and validates `ObsStorageOptions`, registers an OBS-endpoint `IAmazonS3` as a **singleton** (thread-safe and connection-pooled — never scoped/transient), and registers `IFileStorage`/`IBlobUriGenerator` as singletons backed by `ObsFileStorage`/`ObsBlobUriGenerator`. A misconfigured section fails at `IHost.StartAsync()`, not at first upload.

## Configuration reference

Binds from the `SharedKernel:Storage:Obs` section (`ObsStorageOptions.SectionName`):

| Property | Type | Required | Notes |
| --- | --- | --- | --- |
| `Endpoint` | `string` | Yes | The region OBS S3-compatible endpoint (e.g. `"obs.ap-southeast-1.myhuaweicloud.com"`). |
| `AccessKeyId` | `string` | Yes | OBS access key (AK) used to authenticate against the provider. |
| `SecretAccessKey` | `string` | Yes | OBS secret key (SK) used to authenticate against the provider. |
| `ForcePathStyle` | `bool` | No (default `false`) | `true` addresses buckets as path segments (`https://host/bucket/key`) instead of subdomains. |
| `DefaultBucket` | `string?` | No | Optional convenience default bucket for single-bucket services. Not read by `AddSharedKernelObsStorage` itself — a convenience for consuming-service code. |

```json
{
  "SharedKernel": {
    "Storage": {
      "Obs": {
        "Endpoint": "https://obs.ap-southeast-1.myhuaweicloud.com",
        "AccessKeyId": "...",
        "SecretAccessKey": "...",
        "ForcePathStyle": false
      }
    }
  }
}
```

Unlike `S3StorageOptions.Region`, `Endpoint` here is unconditionally required — `CreateClient` assigns it straight to `AmazonS3Config.ServiceURL`, with no region-based branch. Supply the full endpoint including scheme (e.g. `https://...`).

## Usage

See [`SharedKernel.Storage.Abstractions`'s README](../SharedKernel.Storage.Abstractions/README.md) for the full `IFileStorage`/`IBlobUriGenerator` usage guide (upload/download/copy/batch-delete/streaming-list/health-probe/presigned URLs) — this package is a pure implementation and adds no members beyond the abstraction's own contract.

## Registering both `.S3` and `.Obs` side by side (keyed DI)

`AddSharedKernelS3Storage()` and `AddSharedKernelObsStorage()` each register **unkeyed** singletons for `IAmazonS3`, `IFileStorage`, and `IBlobUriGenerator`. Calling both in the same service collection means the second call's registrations win for every unkeyed resolve — the first provider becomes unreachable, not merely shadowed for one member.

A service that genuinely needs both providers available at once (e.g. migrating buckets from OBS to S3, or routing uploads to one provider and archival reads to the other) must register each provider under a **key** instead of relying on either `AddX` extension's unkeyed registration. Build each provider's `IAmazonS3` client and `IFileStorage`/`IBlobUriGenerator` pair manually via `AddKeyedSingleton`, still binding each options type through `SharedKernel.Configuration.AddValidatedOptions` exactly as the unkeyed extensions do:

```csharp
using Amazon.Runtime;
using Amazon.S3;

// S3 side, keyed "s3"
services.AddValidatedOptions<S3StorageOptions>(configuration.GetSection(S3StorageOptions.SectionName));
services.AddKeyedSingleton<IAmazonS3>("s3", (sp, _) =>
{
    var options = sp.GetRequiredService<IOptions<S3StorageOptions>>().Value;
    var credentials = new BasicAWSCredentials(options.AccessKeyId, options.SecretAccessKey);
    var config = new AmazonS3Config { ForcePathStyle = options.ForcePathStyle };
    if (!string.IsNullOrWhiteSpace(options.ServiceUrl))
        config.ServiceURL = options.ServiceUrl;
    else
        config.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(options.Region);
    return new AmazonS3Client(credentials, config);
});
services.AddKeyedSingleton<IFileStorage>("s3", (sp, key) =>
    new S3FileStorage(sp.GetRequiredKeyedService<IAmazonS3>(key), sp.GetRequiredService<ILogger<S3FileStorage>>()));
services.AddKeyedSingleton<IBlobUriGenerator>("s3", (sp, key) =>
    new S3BlobUriGenerator(sp.GetRequiredKeyedService<IAmazonS3>(key), sp.GetRequiredService<IClock>(), sp.GetRequiredService<ILogger<S3BlobUriGenerator>>()));

// OBS side, keyed "obs"
services.AddValidatedOptions<ObsStorageOptions>(configuration.GetSection(ObsStorageOptions.SectionName));
services.AddKeyedSingleton<IAmazonS3>("obs", (sp, _) =>
{
    var options = sp.GetRequiredService<IOptions<ObsStorageOptions>>().Value;
    var credentials = new BasicAWSCredentials(options.AccessKeyId, options.SecretAccessKey);
    var config = new AmazonS3Config { ServiceURL = options.Endpoint, ForcePathStyle = options.ForcePathStyle };
    return new AmazonS3Client(credentials, config);
});
services.AddKeyedSingleton<IFileStorage>("obs", (sp, key) =>
    new ObsFileStorage(sp.GetRequiredKeyedService<IAmazonS3>(key), sp.GetRequiredService<ILogger<ObsFileStorage>>()));
services.AddKeyedSingleton<IBlobUriGenerator>("obs", (sp, key) =>
    new ObsBlobUriGenerator(sp.GetRequiredKeyedService<IAmazonS3>(key), sp.GetRequiredService<IClock>(), sp.GetRequiredService<ILogger<ObsBlobUriGenerator>>()));

// Consuming code resolves by key, never by the unkeyed IFileStorage/IBlobUriGenerator:
public sealed class MigrationService(
    [FromKeyedServices("s3")] IFileStorage s3Storage,
    [FromKeyedServices("obs")] IFileStorage obsStorage)
{
    // ...
}
```

`ObsFileStorage`/`ObsBlobUriGenerator` and `S3FileStorage`/`S3BlobUriGenerator` are public sealed classes with plain constructor-injected dependencies (`IAmazonS3` [+ `IClock` for the URI generators] + `ILogger<T>`), so they are directly constructible in a keyed factory delegate exactly as shown — no reflection, no internal-visibility workaround needed. Neither `AddSharedKernelS3Storage()` nor `AddSharedKernelObsStorage()` offers a keyed-registration overload today — this pattern is the documented, supported way to compose both providers in one host until such an overload exists.

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) — see [08.Storage/CLAUDE.md](../CLAUDE.md) for the full interface contracts, status-code error mapping, and AOT posture.
