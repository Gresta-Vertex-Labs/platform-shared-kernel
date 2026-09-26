# SharedKernel.Storage.Testing

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![Tier: Testing](https://img.shields.io/badge/tier-Testing-orange)
![Test projects only](https://img.shields.io/badge/use-test%20projects%20only-orange)
![Test framework: any](https://img.shields.io/badge/test%20framework-any-informational)
![License: MIT](https://img.shields.io/badge/license-MIT-blue)

**An in-memory object store for `SharedKernel.Storage.Abstractions`, registered exactly like a real provider.**
Named stores, tenant views, request validation, conditional writes, SHA-256 checksums, copies, listings, multipart
uploads and presigned URLs behave as they do against S3, so file-handling code runs in a unit test without MinIO or
a cloud account.

## Install

Reference it from a **test project only**; an architecture test fails any production project that references a
testing package.

```xml
<PackageReference Include="SharedKernel.Storage.Testing" />
```

Versions come from the single `SharedKernelVersion`. Namespace: `SharedKernel.Testing.Storage`.

## Contents

| Type | What it is |
| --- | --- |
| `InMemoryFileStorage` | An `IFileStorage` over a dictionary. Records `UploadedKeys`, `DeletedKeys`, `CopiedPairs`, `IssuedDownloadUrls`, `IssuedUploadUrls`; `WasUploaded(key)`, `WasDeleted(key)`, `WasCopied(src, dst)`, `GetContent(key)`, `Keys`; `Seed(key, bytes or stream, contentType?, metadata?)` returns the `FileReference`; `UploadPart(upload, n, bytes)` stands in for a client uploading to a presigned part URL; `SimulateFailure` and `SimulateUnavailable` (`storage.unavailable`); `Reset()` |
| `InMemoryFileStorageOptions` | `MaxPresignExpiry` and an optional `IClock` for presign expiry |
| `InMemoryStorageBuilderExtensions` | `AddInMemoryStore(name)`, `AddInMemoryTenantStore(name)` (or pass an existing `InMemoryFileStorage`) on `AddSharedKernelStorage()`'s builder; `provider.GetInMemoryStore(name)` to assert |
| `InMemoryStorage` | `CreateFactory(...)` — an `IFileStorageFactory` without a DI container |

A tenant store lives under `tenants/{id}/`, exactly like production; `InMemoryFileStorage.TenantKey(tenantId, key)`
gives the physical key to assert on. Readiness is not simulated — real providers register a `storage-{store}` probe.

## Registration

```csharp
services.AddSharedKernelStorage()
    .AddInMemoryStore("invoices")
    .AddInMemoryTenantStore("attachments");
```

The code under test resolves `[FromKeyedServices("invoices")] IFileStorage` or
`[FromKeyedServices("attachments")] ITenantFileStorage` as it does in production.

## Example

```csharp
var provider = services.BuildServiceProvider();
var exporter = provider.GetRequiredService<InvoiceExporter>();

var reference = await exporter.ExportAsync(invoiceId, ct);

var store = provider.GetInMemoryStore("invoices");
store.WasUploaded(reference.Value.Key).Should().BeTrue();
store.IssuedDownloadUrls.Should().ContainSingle();
```

## Related packages

- References `SharedKernel.Storage.Abstractions` only; no cloud SDK.
- [`SharedKernel.Testing`](../SharedKernel.Testing/README.md) — `FakeClock` for presign expiry, `TestRequestContext`.
- Real-provider tests use `MinioContainerFixture` in the non-packable `SharedKernel.Testing.Internal`.
