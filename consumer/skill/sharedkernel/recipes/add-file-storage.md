---
proven-by:
  - samples/Shop/Catalog/Shop.Catalog.Application/Images/ProductImages.cs
  - samples/Shop/Catalog/Shop.Catalog.Application/Search/CatalogIndexes.cs
  - samples/Shop/Catalog/Shop.Catalog.Infrastructure/CatalogInfrastructure.cs
  - samples/Shop/Catalog/Shop.Catalog.Api/Program.cs
  - samples/Shop/Catalog/Shop.Catalog.Tests/CatalogPipelineTests.cs
  - samples/Shop/Reports/Shop.Reports.Api/Program.cs
unproven:
  - Shared store (AddStore and IFileStorage): every store of the reference platform is a tenant store
  - Presigned uploads (CreateUploadUrlAsync, CreateUploadFormAsync): the reference platform uploads through the service and presigns downloads only
  - Multipart uploads: the reference platform's files fit one request
---
# Add file storage

**Use when:** a use case uploads, downloads, lists or presigns files in S3, an S3-compatible store (MinIO) or
Huawei OBS, usually one key prefix per tenant. **Not when:** the file is a generated export (CSV, Excel, PDF);
add-report-export.md streams it into a store for you.

## Packages

| Project | Package | Why |
| --- | --- | --- |
| Application | `SharedKernel.Storage.Abstractions` | `ITenantFileStorage`, `IFileStorage`, the upload and presign options |
| Infrastructure | `SharedKernel.Storage.S3` | Amazon S3, MinIO and other S3-compatible services |
| Infrastructure | `SharedKernel.Storage.Obs` | Huawei Cloud OBS (only if you use it) |
| Tests | `SharedKernel.Storage.Testing` | In-memory stores registered like the real ones |

## Steps

1. **Application: name the store, inject it keyed, choose the tenant per call.** A store is a name in code and a
   bucket in configuration. Keep the name in a constant: registration and every handler use it.

```csharp
public static class CatalogIndexes
{
    /// <summary>The tenant file store holding product images (S3).</summary>
    public const string ImageStore = "product-images";
}
```

   A tenant store is registered as a keyed `ITenantFileStorage`, never as `IFileStorage`, so a handler cannot touch
   it without choosing a tenant. Take the tenant from `IRequestContext` and fail closed when there is none:

```csharp
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Application.Messaging;
using SharedKernel.Execution.Context;
using SharedKernel.Persistence.Abstractions.Repositories;
using SharedKernel.Primitives.Results;
using SharedKernel.Storage;

public sealed class UploadProductImageHandler(
    IRequestContext caller,
    IRepository<Product, ProductId> products,
    [FromKeyedServices(CatalogIndexes.ImageStore)] ITenantFileStorage images
) : ICommandHandler<UploadProductImageCommand, string>
{
    public async Task<Result<string>> Handle(UploadProductImageCommand command, CancellationToken cancellationToken)
    {
        var tenant = CallerTenant.Of(caller);   // Result<TenantId>: Forbidden when the caller has no tenant
        if (tenant.IsFailure)
        {
            return Result<string>.Failure(tenant.Error);
        }

        // ...load the product, NotFound when missing...
        string key = $"{product.Id.Value:D}/main";
        var uploaded = await images
            .ForTenant(tenant.Value)
            .UploadAsync(
                key,
                command.Content,
                new FileUploadOptions { ContentType = command.ContentType },
                cancellationToken
            );
        if (uploaded.IsFailure)
        {
            return Result<string>.Failure(uploaded.Error);
        }

        product.AttachImage(key);
        return Result<string>.Success(key);
    }
}
```

   Keys are relative to the tenant: `ForTenant` adds the tenant's prefix, and no key, listing or copy can leave it.
   Store the key on the aggregate, never a URL.

2. **Application: hand out a short-lived download URL instead of streaming the file through the service.**

```csharp
using SharedKernel.Primitives.Results;
using SharedKernel.Storage;

var url = await images
    .ForTenant(tenant.Value)
    .CreateDownloadUrlAsync(
        product.ImageKey,
        new PresignedDownloadOptions { Expiry = TimeSpan.FromMinutes(5) },
        cancellationToken
    );
return url.IsFailure ? Result<Uri>.Failure(url.Error) : Result<Uri>.Success(url.Value.Url);
```

   Every storage call returns `Result`/`Result<T>`; a missing object, a refused write or an outage is a failure value,
   not an exception.

3. **Infrastructure: register the provider and the store.** One `AddSharedKernelStorage()`, one provider call per
   connection, one `AddTenantStore` per store:

```csharp
using SharedKernel.Storage;

services
    .AddSharedKernelStorage()
    .AddS3(configuration)
    .AddTenantStore(CatalogIndexes.ImageStore);
```

   Two providers in one service share the storage builder; each store belongs to the provider it was added to:

```csharp
using SharedKernel.Storage;

var storage = builder.Services.AddSharedKernelStorage();
storage.AddS3(builder.Configuration).AddTenantStore(ReportStores.Downloads);
storage.AddObs(builder.Configuration).AddTenantStore(ReportStores.Archive);
```

   A store shared by the whole service (not per tenant) is registered with `AddStore("name")` and injected as
   `[FromKeyedServices("name")] IFileStorage`. The reference platform does not use one; the
   `SharedKernel.Storage.Abstractions` README shows it. Presigned uploads (`CreateUploadUrlAsync`,
   `CreateUploadFormAsync`) and multipart uploads are not exercised by the reference platform either: read the README
   before using them.

4. **Api/Worker: map readiness.** Each store registers its own readiness probe; the host maps them all:

```csharp
using SharedKernel.ServiceDefaults.HealthChecks;

builder.Services.AddHealthChecks().AddSharedKernelReadiness();
```

5. **Configuration:** the provider connection is `SharedKernel:Storage:S3` (or `SharedKernel:Storage:Obs`), and each
   store is `SharedKernel:Storage:Stores:{name}`. The keys (bucket, prefix, encryption, credentials) are in the
   provider package's README.

## Test it

Register an in-memory tenant store under the same name; the handler under test does not change.

```csharp
using SharedKernel.Storage;
using SharedKernel.Testing.Storage;

services.AddSharedKernelStorage().AddInMemoryTenantStore(CatalogIndexes.ImageStore);
```

```csharp
using FluentAssertions;

using var png = new MemoryStream([0x89, 0x50, 0x4E, 0x47]);
var uploaded = await harness.SendAsync(new UploadProductImageCommand(id, png, "image/png"));
var url = await harness.SendAsync(new GetProductImageUrlQuery(id));

uploaded.IsSuccess.Should().BeTrue();
url.IsSuccess.Should().BeTrue();
```

The in-memory store keeps the tenant views apart and issues presigned URLs, so a cross-tenant read fails in the
test as it would in production. The pipeline harness is in write-tests.md; a real bucket belongs in an
integration test against MinIO.

## Check

- **SK0030**: an `UploadAsync`, `DeleteAsync` or `CopyAsync` whose `Result` nobody reads.
- **SK0023**: `IAmazonS3` registered as scoped or transient; let `SharedKernel.Storage.S3` register it.
- No analyzer catches a misspelled store name: use one constant for registration and injection, and a wrong name
  fails when the handler is resolved.
- Resolving `IFileStorage` for a store added with `AddTenantStore` fails: a tenant store is only an
  `ITenantFileStorage`.
- A misnamed bucket shows up as the `storage-{store}` readiness probe failing on `/health/ready`.
- Architecture tests shaped like the reference platform's fail when `SharedKernel.Storage.S3` or
  `SharedKernel.Storage.Obs` is referenced from the Application project.

## Read next

- `SharedKernel.Storage.Abstractions`
- `SharedKernel.Storage.S3`
- `SharedKernel.Storage.Obs`
- `SharedKernel.Storage.Testing`
