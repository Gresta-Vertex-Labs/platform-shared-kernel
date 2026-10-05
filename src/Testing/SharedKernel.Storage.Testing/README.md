# SharedKernel.Storage.Testing

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Tier: Testing](https://img.shields.io/badge/tier-Testing-e36209)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

> **An in-memory object store registered exactly like a real storage provider, so file-handling code runs in a unit
> test with named stores, tenant views, validation, conditional writes and presigned URLs — without MinIO or a cloud
> account.**

| You get | So that |
| --- | --- |
| `AddInMemoryStore(name)` / `AddInMemoryTenantStore(name)` on `AddSharedKernelStorage()` | Code under test resolves `[FromKeyedServices(name)] IFileStorage`, `ITenantFileStorage` and `IFileStorageFactory` as in production |
| The real storage registry in front of the store | Key validation and `tenants/{id}/` isolation are the production code paths, not a copy |
| A faithful `IFileStorage` (`InMemoryFileStorage`) | Write conditions, SHA-256 checksums, ranges, listing with paging, copies and multipart uploads return the real `storage.*` errors |
| Recordings (`UploadedKeys`, `DeletedKeys`, `CopiedPairs`, `IssuedDownloadUrls`, …) | A test asserts what the code did to the store |
| `Seed`, `GetContent`, `TenantKey` | Arrange and inspect objects without going through the API |
| `SimulateFailure` / `SimulateUnavailable` | Outage paths and the `storage-{store}` readiness probe are testable |

## Contents

- [Install](#install)
- [Quick start](#quick-start)
- [How it works](#how-it-works)
- [Recipes](#recipes)
- [Reference](#reference)
- [Testing](#testing)
- [Pitfalls](#pitfalls)

## Install

```xml
<PackageReference Include="SharedKernel.Storage.Testing" />
```

The version comes from your central `SharedKernelVersion` property — every SharedKernel package ships at the same
version. See [Using the packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel#using-the-packages).

Add it to a **test project** only. A production project that references it fails the architecture rule
`TestingNeverReferencedByProduction`.

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Testing — reference it from your **test projects** only |
| Depends on | `SharedKernel.Storage.Abstractions`, `Microsoft.Extensions.DependencyInjection` (no cloud SDK) |
| Namespaces | `SharedKernel.Testing.Storage` |

## Quick start

```csharp
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Storage;
using SharedKernel.Testing.Storage;
using Xunit;

public sealed class InvoiceArchiveTests
{
    [Fact]
    public async Task Archiving_an_invoice_uploads_it_once()
    {
        var services = new ServiceCollection();
        services.AddSharedKernelStorage().AddInMemoryStore("invoices");
        services.AddScoped<InvoiceArchive>();   // your class, taking [FromKeyedServices("invoices")] IFileStorage
        using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<InvoiceArchive>().ArchiveAsync(invoiceId: 7, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var store = provider.GetInMemoryStore("invoices");
        Assert.True(store.WasUploaded(result.Value.Key));
        Assert.Single(store.UploadedKeys);
    }
}
```

`GetInMemoryStore(name)` returns the raw store behind the registry, for inspection and seeding.

## How it works

- **Registry first.** The `Add*` extensions register the store through `IStorageBuilder.AddStore`, so what the code
  under test receives is the registry's store, not `InMemoryFileStorage` itself. The raw store is also a keyed
  singleton under its name.
- **Tenant stores.** A tenant view writes under `tenants/{tenantId}/{key}`; one tenant cannot see another's objects,
  and a tenant store cannot be opened as a shared store. The inspection helpers take **store** keys — build them with
  `InMemoryFileStorage.TenantKey(tenantId, key)`.
- **Faithful behaviour.** `IfNotExists`/`IfMatch` conditions, `ChecksumSha256` verification, range reads, prefix and
  folder listing with continuation tokens, idempotent batch delete, copies (in memory between in-memory stores,
  streamed into any other store) and multipart upload completion all return the production error codes.
- **Deterministic.** ETags are quoted and come from an internal sequence (a new one per write). Times come from
  `InMemoryFileStorageOptions.Clock`, or a fixed 2024-01-01T00:00:00Z when none is set — never the wall clock.
- **Simplified:** no object versioning (`VersionId` is always `null`); presigned URLs are deterministic
  `memory://{store}/{key}?method=…&expires=…` values that cannot be fetched; the POST form's `policy` field is not a
  signed policy; multipart part sizes are not checked; parts are uploaded with `UploadPart`, standing in for the
  client's `PUT`. Content is buffered in memory.
- **Thread-safe.** Every operation and recording is guarded by a lock; recordings are returned as snapshots.

## Recipes

### 1. Arrange an existing file and assert a download

```csharp
var invoices = provider.GetInMemoryStore("invoices");
invoices.Seed("2026/07.pdf", "%PDF-1.7"u8.ToArray(), contentType: "application/pdf");   // not recorded as an upload

var result = await sut.DownloadInvoiceAsync("2026/07.pdf", CancellationToken.None);
Assert.True(result.IsSuccess);
```

### 2. Assert tenant isolation

```csharp
services.AddSharedKernelStorage().AddInMemoryTenantStore("documents");
// … run the code under test for tenant A …

var raw = provider.GetInMemoryStore("documents");
Assert.Equal([InMemoryFileStorage.TenantKey(tenantA, "contract.pdf")], raw.Keys);
```

### 3. Test the outage path

```csharp
provider.GetInMemoryStore("invoices").SimulateFailure = true;

var result = await sut.ArchiveAsync(invoiceId: 7, CancellationToken.None);

Assert.Equal(StorageErrorCodes.Unavailable, result.Error.Code);
```

`SimulateFailure` fails uploads, copies, deletes and starting or completing a multipart upload (a batch delete reports
every key as failed); reads, listing and presigning keep working. `SimulateUnavailable` fails only the readiness probe.

### 4. Assert a presigned link was issued

```csharp
var store = provider.GetInMemoryStore("invoices");
var (key, request) = Assert.Single(store.IssuedDownloadUrls);
Assert.Equal("2026/07.pdf", key);
Assert.Equal("GET", request.Method);
```

Set `o => o.MaxPresignExpiry = …` on `AddInMemoryStore` to match your production store; a longer expiry returns
`storage.expiry_too_long`.

### 5. Build a factory without a container

```csharp
var reports = new InMemoryFileStorage("reports");
IFileStorageFactory factory = InMemoryStorage.CreateFactory(reports);

await new ReportWriter(factory).WriteAsync(CancellationToken.None);
Assert.True(reports.WasUploaded("export.csv"));
```

## Reference

### Registration

| Method | Registers |
| --- | --- |
| `AddInMemoryStore(this IStorageBuilder, string name, Action<InMemoryFileStorageOptions>? configure = null)` | A shared store `name` and its `storage-{name}` probe |
| `AddInMemoryTenantStore(this IStorageBuilder, string name, Action<InMemoryFileStorageOptions>? configure = null)` | A tenant-scoped store `name` (`ITenantFileStorage`) and its probe |
| `AddInMemoryStore(this IStorageBuilder, InMemoryFileStorage store)` / `AddInMemoryTenantStore(…, InMemoryFileStorage store)` | The same, over an instance the test keeps |
| `GetInMemoryStore(this IServiceProvider, string name)` | Returns the raw `InMemoryFileStorage` |
| `InMemoryStorage.CreateFactory(params InMemoryFileStorage[] stores)` | An `IFileStorageFactory` over shared stores, no host |
| `InMemoryStorage.CreateFactory(Action<IStorageBuilder> configure)` | An `IFileStorageFactory` over any registration |

Store names must be 1–64 characters from `A-Z a-z 0-9 . _ -`, starting with a letter or digit; a duplicate name throws.

### Types

| Type | Implements / purpose |
| --- | --- |
| `InMemoryFileStorage` | `IFileStorage` — the raw store; constructor `(string storeName = "default", InMemoryFileStorageOptions? options = null)` |
| `InMemoryFileStorageOptions` | `MaxPresignExpiry` (default 1 hour) and `Clock` (`IClock?`, default a fixed instant) |
| `InMemoryStorage` | Static `CreateFactory` helpers |
| `InMemoryStorageBuilderExtensions` | The registration methods above |

### Inspection and setup — `InMemoryFileStorage`

| Member | Purpose |
| --- | --- |
| `UploadedKeys` / `WasUploaded(key)` | Every key written by upload, copy or completed multipart upload; never pruned |
| `DeletedKeys` / `WasDeleted(key)` | Every key actually deleted |
| `CopiedPairs` / `WasCopied(sourceKey, destinationKey)` | Successful copies out of this store |
| `IssuedDownloadUrls` / `IssuedUploadUrls` | `(Key, PresignedRequest)` for each URL issued |
| `Keys` | Current object keys, ordinal order |
| `GetContent(key)` | A copy of the bytes; throws `KeyNotFoundException` if absent |
| `Seed(key, byte[] or Stream, contentType?, metadata?)` | Stores an object without recording an upload |
| `UploadPart(upload, partNumber, content)` | Uploads a multipart part, returns its ETag |
| `TenantKey(tenantId, key)` (static) | `tenants/{tenantId}/{key}` |
| `SimulateFailure` / `SimulateUnavailable` | See recipe 3 |
| `Reset()` | Clears objects, uploads, recordings and both flags |

### Errors

The store returns `StorageErrorCodes` from `SharedKernel.Storage.Abstractions`: `storage.not_found`,
`storage.invalid_key`, `storage.invalid_request`, `storage.expiry_too_long`, `storage.already_exists`,
`storage.precondition_failed`, `storage.checksum_mismatch`, `storage.invalid_range` and `storage.unavailable`.

## Testing

This package is the test double; its own self-tests live in
[`SharedKernel.Storage.Testing.Tests`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Testing/SharedKernel.Storage.Testing/SharedKernel.Storage.Testing.Tests),
which prove the store and its registration against the `IFileStorage` contract (conditions, checksums, listing,
multipart, tenant isolation, the readiness probe). Pair it with
[`SharedKernel.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Testing/SharedKernel.Testing/README.md)
— pass its `FakeClock` as `InMemoryFileStorageOptions.Clock` to control `LastModified` and presign expiry, and use
`TestRequestContext` for the caller. Test S3-specific behaviour against the real provider.

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Reference this package from a production project | Reference it from test projects only | `TestingNeverReferencedByProduction` fails the build's architecture tests |
| Inject `InMemoryFileStorage` into the code under test | Let it resolve `IFileStorage`/`ITenantFileStorage`; inspect with `GetInMemoryStore` | The registry's validation and tenant prefixing only run on the registered path |
| Assert on a tenant view's key against `Keys` or `GetContent` | Use `InMemoryFileStorage.TenantKey(tenantId, key)` | The raw store holds store keys under `tenants/{id}/` |
| Follow a presigned URL in a test | Assert on `IssuedDownloadUrls` / `IssuedUploadUrls` | `memory://` URLs are not reachable |
| Rely on object versions or S3 part-size limits | Test them against the real provider | The double keeps no versions and does not check part sizes |
| Share one store across tests without clearing | Build a new provider per test or call `Reset()` | Recordings are never pruned |

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) · [16.Testing domain](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Testing/README.md) · [MIT license](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
