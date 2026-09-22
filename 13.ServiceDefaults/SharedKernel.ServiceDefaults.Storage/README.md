# SharedKernel.ServiceDefaults.Storage

Object-storage readiness check, provider-neutral. One of the `SharedKernel.ServiceDefaults.*` integration packages: add it only if your
service has this dependency.

## Usage

```xml
<PackageReference Include="SharedKernel.ServiceDefaults" />
<PackageReference Include="SharedKernel.ServiceDefaults.Storage" />
```

```csharp
using SharedKernel.ServiceDefaults.Extensions;
using SharedKernel.ServiceDefaults.HealthChecks;
using SharedKernel.Storage;   // AddSharedKernelStorage/AddS3 (SharedKernel.Storage.S3)

builder.AddServiceDefaults();

builder.Services.AddSharedKernelStorage()
    .AddS3(builder.Configuration)
    .AddStore("invoices");

builder.Services.AddHealthChecks()
    .AddStorageReadinessCheck("invoices");   // the store name, not the bucket

var app = builder.Build();
app.MapDefaultHealthCheckEndpoints();
```

Chain onto `AddHealthChecks()`, **not** `AddSharedKernelHealthChecks()`. `AddServiceDefaults()`
already calls `AddSharedKernelHealthChecks()`, which registers the `"startup"` check; calling it a
second time registers `"startup"` twice, and the application throws `ArgumentException: Duplicate
health checks were registered with the name(s): startup` when it starts.

## Behaviour

| | |
| --- | --- |
| Method | `AddStorageReadinessCheck(string storeName, string name = "storage")` |
| Default name | `HealthCheckNames.Storage` (`"storage"`) |
| Tags | `ready`, `storage` — never `live` |
| On failure | `Unhealthy` (an unregistered store name is also reported `Unhealthy`) |
| Invalid store name | `ArgumentException` at registration |
| Resolves | `IFileStorageHealthProbe` (registered by `AddSharedKernelStorage()`) — works against S3, MinIO, or OBS alike |

The probe reads the metadata of the bucket behind the store; it never needs an object to exist. A service with
several stores on different buckets adds one check per store, each with its own `name`:

```csharp
builder.Services.AddHealthChecks()
    .AddStorageReadinessCheck("invoices", name: "storage-invoices")
    .AddStorageReadinessCheck("documents", name: "storage-documents");
```

The check is tagged `ready` and never `live`, so it gates load-balancer rotation through
`/health/ready` without ever causing Kubernetes to restart the pod through `/health/live`.

## Why a separate package

Brings `SharedKernel.Storage.Abstractions` only. The check names a store, never a bucket or a provider option, so it
does not depend on which provider serves the store.

The types keep their `SharedKernel.ServiceDefaults.HealthChecks` namespace from before the WO-084
split, so moving to this package changes a `PackageReference` and no source.

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel). See the
[SharedKernel.ServiceDefaults README](../SharedKernel.ServiceDefaults/README.md) for the composition
base and the full list of integration packages.
