# 08.Storage — Object Storage Brain

## What This Domain Is

The object-storage abstraction and provider-wiring layer. Downstream microservices depend on `SharedKernel.Storage.Abstractions` to upload, download, delete, copy, batch-delete, stream-list, and generate presigned URLs for binary blobs (documents, images, exports, attachments), plus probe basic connectivity for K8s readiness — never on a concrete cloud SDK. Concrete provider packages (`SharedKernel.Storage.S3`, `SharedKernel.Storage.Obs`) wire the vendor SDK, map its results onto the abstraction contracts, and own all provider-specific configuration.

Philosophy: **Thin abstractions. Provider-swappable. Stream-first. Presigned-URL-native. No domain coupling.**

> `08.Storage` may only reference `01.Core`. It must never reference `03.Domain`, `04.Contracts`, `06.Persistence`, `07.Messaging`, or any other capability domain. Blob bytes never flow through the application/domain layers as in-memory buffers — the storage surface is `Stream`-based end to end so large objects never materialize fully in managed memory.

---

## Packages

| Package | Role | References |
| --- | --- | --- |
| `SharedKernel.Storage.Abstractions` | `IFileStorage`, `IBlobUriGenerator` interfaces plus their request/result models (`FileUploadRequest`, `FileReference`, `FileDownload`, `FileMetadata`, `FileDeleteOutcome`, `PresignedUrlRequest`, `PresignedUrl`) and `StorageErrors` factory — the only types application code should ever inject | `SharedKernel.Primitives` |
| `SharedKernel.Storage.S3` | Concrete AWS S3 / MinIO implementation: `S3FileStorage`, `S3BlobUriGenerator`, `S3StorageOptions`, `AddSharedKernelS3Storage()` DI extension | `SharedKernel.Storage.Abstractions`, `SharedKernel.Configuration`, `AWSSDK.S3`, `Microsoft.Extensions.Logging.Abstractions` |
| `SharedKernel.Storage.Obs` | Concrete Huawei Cloud OBS implementation over OBS's S3-compatible endpoint: `ObsFileStorage`, `ObsBlobUriGenerator`, `ObsStorageOptions`, `AddSharedKernelObsStorage()` DI extension | `SharedKernel.Storage.Abstractions`, `SharedKernel.Configuration`, `AWSSDK.S3`, `Microsoft.Extensions.Logging.Abstractions` |

All packages target `net10.0`, `ImplicitUsings` enabled, `Nullable` enabled. Test sub-folders live inside each project folder (never in a top-level `tests/`). `SharedKernel.Storage.Abstractions` has **zero third-party NuGet dependencies** — only a `SharedKernel.Primitives` project reference.

> **Provider role note:** `SharedKernel.Storage.S3` and `SharedKernel.Storage.Obs` are sibling `.{Provider}` packages, not a `.{Provider}.Core` / `.{Provider}.{Role}` split. They must never reference each other. Both happen to sit on `AWSSDK.S3` because Huawei OBS exposes an S3-compatible API (see Technology Stack) — but they remain independent packages with independent options types and DI entry points so a future native-SDK swap inside `.Obs` never touches `.S3`.

---

## Technology Stack

| Concern | Technology |
| --- | --- |
| Storage abstractions | Pure C# 13 interfaces + `Stream` — zero external NuGet dependencies |
| Outcome type | `Result<T>` / `Result` / `Error` from `SharedKernel.Primitives` — storage operations never throw for expected failures (not-found, access-denied) |
| AWS S3 provider | `AWSSDK.S3` pinned `4.0.101.1` (latest stable on nuget.org confirmed at Scaffold-phase implementation time) — `Amazon.S3.IAmazonS3`, `TransferUtility` |
| MinIO | Served by `SharedKernel.Storage.S3` via a configurable `ServiceUrl` + `ForcePathStyle = true` — MinIO is S3-API-compatible, so it needs no separate package |
| Huawei Cloud OBS provider | `AWSSDK.S3` pinned `4.0.101.1` — same exact version as `.S3`, pointed at the OBS S3-compatible endpoint (`ServiceUrl` = region OBS endpoint, `ForcePathStyle` per bucket-addressing mode) |
| Presigned URLs | Provider-native presign (`GetPreSignedURL` / `AmazonS3` request presigning) — no custom HMAC signing |
| Configuration | Options-pattern via `SharedKernel.Configuration.AddValidatedOptions` — misconfiguration fails at startup, not first upload |
| DI composition | Per-provider `AddSharedKernelS3Storage()` / `AddSharedKernelObsStorage()` extensions |
| Logging | `[LoggerMessage]` source-generated pattern, `EventId` range **8000–8999** (`LoggingEventIdRanges.Storage`, from `01.Core`); 100-wide sub-blocks per package in declaration order (Abstractions 8000–8099, S3 8100–8199, Obs 8200–8299). `.S3`/`.Obs` require an explicit `Microsoft.Extensions.Logging.Abstractions` `10.0.9` `PackageReference` (provides `ILogger<T>`/`LogLevel`/the `LoggerMessage` attribute) — it is not pulled in transitively by `AWSSDK.S3` or `SharedKernel.Configuration`; omitting it fails the build with `ILogger`/`LoggerMessage` unresolved even though the `[LoggerMessage]`-attributed code itself is otherwise complete |
| Narrow error-mapping test mocking | `NSubstitute` `5.3.0` — `SharedKernel.Storage.S3.Tests`-only `PackageReference` (matches the version already pinned by `06.Persistence.EfCore.Tests`), used exclusively to substitute `IAmazonS3` for status-code → `StorageErrors` mapping assertions where inducing a real 403/500 against a live backend is impractical; never used for behavioral/round-trip coverage |

> **Why `AWSSDK.S3` inside `SharedKernel.Storage.Obs` (not the native Huawei SDK):** Huawei's officially-documented .NET package (`HuaweiCloud.ESDK.OBS.Core`) was last published November 2022, targets .NET Standard 2.0, is owned by a personal NuGet account, and carries no AOT story. OBS's endpoint is S3-API-compatible, so `AWSSDK.S3` (actively maintained, `net8.0`+, ~11M downloads/day) drives OBS through its own options + DI seam without the stale dependency. The trade-off: OBS-only features outside the S3 API surface are out of scope for this package. If a native-SDK need ever arises, it is isolated to `.Obs` behind the same `IFileStorage`/`IBlobUriGenerator` contracts — the root brain's "place non-AOT-safe third-party packages behind an abstraction" guidance is pre-satisfied.

---

## Interface Contracts

### `SharedKernel.Storage.Abstractions` — public surface

> Zero third-party NuGet dependencies. References only `SharedKernel.Primitives`.
> Every operation returns `Result`/`Result<T>` — expected failures (not-found, access-denied, checksum-mismatch) are `Error` values, never exceptions. Only truly exceptional transport faults surface as thrown exceptions.

#### File storage (`Abstractions/`)

```text
IFileStorage
    .UploadAsync(FileUploadRequest request, CancellationToken ct)              → Task<Result<FileReference>>
    .DownloadAsync(string bucket, string key, CancellationToken ct)            → Task<Result<FileDownload>>
    .DeleteAsync(string bucket, string key, CancellationToken ct)              → Task<Result>
    .ExistsAsync(string bucket, string key, CancellationToken ct)              → Task<Result<bool>>
    .GetMetadataAsync(string bucket, string key, CancellationToken ct)         → Task<Result<FileMetadata>>
    .CopyAsync(string sourceBucket, string sourceKey,
               string destinationBucket, string destinationKey,
               CancellationToken ct)                                          → Task<Result<FileReference>>
    .DeleteManyAsync(string bucket, IReadOnlyCollection<string> keys,
                      CancellationToken ct)                                    → Task<Result<IReadOnlyList<FileDeleteOutcome>>>
    .ListAsync(string bucket, string prefix,
               [EnumeratorCancellation] CancellationToken ct)                  → IAsyncEnumerable<FileMetadata>
    .CheckHealthAsync(string bucket, CancellationToken ct)                     → Task<Result>
    NOTE: Stream-first. UploadAsync consumes FileUploadRequest.Content (a caller-owned Stream) and
          streams it to the provider — the payload is never buffered whole in managed memory.
          DownloadAsync returns a FileDownload whose Content is a provider-backed Stream the CALLER
          must dispose (FileDownload is IAsyncDisposable). ExistsAsync issues a HEAD-style metadata
          probe — never downloads the object body. DeleteAsync is idempotent: deleting an absent key
          succeeds (Result.Success). bucket/key are validated non-empty; invalid input returns
          StorageErrors.InvalidKey / InvalidBucket, not an exception.

          CopyAsync (P-265) is a server-side object copy — the provider issues a native copy call
          (e.g. S3 CopyObject) so object bytes never round-trip through application memory. Returns
          the destination FileReference (new ETag/VersionId); the source object is left untouched.

          DeleteManyAsync (P-265) is a batch/multi-key delete backed by the provider's native
          multi-object delete where available. The outer Result fails ONLY when the batch call
          itself cannot be attempted (empty/null keys, transport fault); the inner
          IReadOnlyList<FileDeleteOutcome> carries each key's individual success/failure so a
          partial batch failure never masquerades as one opaque error. Providers chunk internally
          at their own native per-request key limit (invisible to the caller).

          ListAsync (P-265) streams FileMetadata via constant-memory IAsyncEnumerable, mirroring
          06.Persistence's IReadRepository.StreamAsync precedent (P-149) — supersedes the originally
          sketched Task<Result<IReadOnlyList<FileMetadata>>> shape, which was never implemented.
          DELIBERATE, DOCUMENTED DEVIATION from this domain's own Result-first convention: the
          method is NOT Result-wrapped. A provider fault mid-enumeration (e.g. credentials revoked
          between pages) propagates as a thrown exception from MoveNextAsync, not an Error value.
          This mirrors the one existing platform precedent for streaming reads rather than
          inventing a new streaming-plus-Result shape.

          CheckHealthAsync (P-265) is a lightweight, non-generic Result connectivity/reachability
          probe scoped to a bucket — mirrors ExistsAsync's bucket-scoped, no-body shape minus the
          key. Never requires a specific object key to exist, never touches object bytes. Backing
          for a K8s readiness health check (13.ServiceDefaults) that proves "storage is reachable"
          without side-effecting a real object operation. Considered-and-rejected alternative: a
          richer Result<StorageHealthProbe> payload carrying latency, mirroring 06.Persistence's
          DatabaseReadinessResult — rejected because a plain Result is sufficient and
          13.ServiceDefaults's eventual IHealthCheck adapter (P-270) can derive Healthy/Unhealthy
          plus a description directly from Result.IsSuccess/Error.Description; a richer payload
          remains a strictly additive future change if ever needed.
```

#### Presigned URL generation (`Abstractions/`)

```text
IBlobUriGenerator
    .GeneratePresignedUploadUrl(PresignedUrlRequest request)                   → Result<PresignedUrl>
    .GeneratePresignedDownloadUrl(PresignedUrlRequest request)                 → Result<PresignedUrl>
    NOTE: Synchronous — presigning is a local cryptographic operation against provider credentials,
          no network round-trip. Presigned upload URLs let a browser/client PUT directly to storage,
          bypassing the service process for large payloads. Expiry is bounded by the provider's
          maximum (7 days for S3-family signatures); requests exceeding it return
          StorageErrors.ExpiryTooLong. The returned PresignedUrl carries the absolute ExpiresAt so
          callers never recompute it from a relative TimeSpan.
```

#### Request / result models (`Models/`)

```text
FileUploadRequest  (sealed record)
    .Bucket        → string                       (required, non-empty)
    .Key           → string                       (required, non-empty; the object path/name)
    .Content       → Stream                        (required; caller-owned, read from current position)
    .ContentType   → string                        (required; MIME type, e.g. "application/pdf")
    .Metadata      → IReadOnlyDictionary<string,string>?  (optional user metadata; null = none)
    NOTE: Content is NOT disposed by UploadAsync — the caller owns the stream lifetime.

FileReference  (sealed record)
    .Bucket   → string
    .Key      → string
    .ETag     → string?         (provider entity tag for the stored object)
    .VersionId → string?        (provider version id when bucket versioning is enabled; else null)
    NOTE: The canonical handle to a stored object. Persist this (not a presigned URL) as the durable
          pointer — presigned URLs expire, FileReference does not.

FileDownload  (sealed class, IAsyncDisposable)
    .Content       → Stream                        (provider-backed; CALLER disposes via FileDownload)
    .ContentType   → string
    .ContentLength → long
    .Metadata      → IReadOnlyDictionary<string,string>
    NOTE: await using the FileDownload (or await DisposeAsync) to release the underlying network stream.

FileMetadata  (sealed record)
    .Bucket        → string
    .Key           → string
    .ContentType   → string
    .ContentLength → long
    .LastModified  → DateTimeOffset
    .ETag          → string?
    NOTE: Returned by GetMetadataAsync and each element streamed by ListAsync — never carries
          object bytes.

FileDeleteOutcome  (sealed record)
    .Key        → string
    .Succeeded  → bool
    .Error      → Error?          (null when Succeeded == true)
    NOTE: One element per requested key in DeleteManyAsync's result list — mirrors the provider's
          native multi-object-delete response shape (e.g. S3 DeleteObjects' Deleted[]/Errors[]
          arrays), so a caller can distinguish "key 3 of 10 failed" from "the whole batch failed."

PresignedUrlRequest  (sealed record)
    .Bucket → string                               (required, non-empty)
    .Key    → string                               (required, non-empty)
    .Expiry → TimeSpan                              (required; time-to-live from now)
    NOTE: HTTP verb is implied by the generator method (upload = PUT, download = GET).

PresignedUrl  (sealed record)
    .Url       → Uri
    .ExpiresAt → DateTimeOffset                     (absolute expiry — derived once, never recomputed)
```

#### Storage errors (`Errors/`)

```text
StorageErrors  (static class — canonical Error factory, mirrors the platform Error-value convention)
    .NotFound(bucket, key)          → Error   (Error.NotFound — object or bucket absent)
    .AccessDenied(bucket, key)      → Error   (Error.Forbidden — credentials lack permission)
    .InvalidBucket(bucket)          → Error   (Error.Validation — empty/malformed bucket name)
    .InvalidKey(key)                → Error   (Error.Validation — empty/malformed object key)
    .ExpiryTooLong(requested, max)  → Error   (Error.Validation — presign TTL exceeds provider max)
    .UploadFailed(bucket, key)      → Error   (Error.Failure — provider rejected the write)
    .CopyFailed(sourceBucket, sourceKey,
                destinationBucket, destinationKey)
                                    → Error   (Error.Failure — provider rejected the server-side copy)
    .BatchDeleteFailed(bucket)      → Error   (Error.Failure — outer DeleteManyAsync call-level
                                                failure only; per-key failures inside
                                                FileDeleteOutcome.Error reuse .NotFound/.AccessDenied)
    .ConnectivityFailure(bucket)    → Error   (Error.Failure — CheckHealthAsync's failure path)
    NOTE: Named-constant, single-source error factory — provider implementations return these, they
          never construct ad-hoc Error values inline. Message templates use PascalCase named
          placeholders; no correlation/tenant ids embedded (those flow ambiently — see Logging).
          The three P-265 additions all route through Error.Failure — no new Error kind was needed.
```

---

### `SharedKernel.Storage.S3` — public surface

```text
S3FileStorage  (sealed class, implements IFileStorage)
    — wraps Amazon.S3.IAmazonS3; UploadAsync uses TransferUtility for multipart-aware streaming.
    — maps AmazonS3Exception status codes onto StorageErrors (404 → NotFound, 403 → AccessDenied, …).
    — CopyAsync: IAmazonS3.CopyObjectAsync (native server-side copy, no TransferUtility involved);
      source-404 → NotFound, 403 → AccessDenied, other → CopyFailed.
    — DeleteManyAsync: IAmazonS3.DeleteObjectsAsync, chunked internally at
      S3StorageConstants.MaxBatchDeleteKeys (1000 — S3's hard per-request limit, invisible to the
      caller); DeleteObjectsResponse.DeleteErrors map to per-key FileDeleteOutcome.Error via
      NotFound/AccessDenied; a whole-call transport fault maps to the outer BatchDeleteFailed.
    — ListAsync: hand-written async IAsyncEnumerable<FileMetadata> iterator over
      IAmazonS3.ListObjectsV2Async, paged manually via ContinuationToken — deliberately NOT using
      IAmazonS3's built-in paginator helper, which relies on a reflection-based code path (see AOT
      Compatibility below).
    — CheckHealthAsync: IAmazonS3.HeadBucketAsync; 404/403/timeout → ConnectivityFailure(bucket).

S3BlobUriGenerator  (sealed class, implements IBlobUriGenerator)
    — delegates to IAmazonS3 request presigning (GetPreSignedURL); clamps Expiry to the 7-day max.

S3StorageConstants  (internal static class — magic-string discipline, SK0022)
    MaxBatchDeleteKeys = 1000   (S3's native DeleteObjects per-request key limit)
    NOTE: Plus any S3-specific header/metadata keys used at more than one call site. Independently
          declared from ObsStorageConstants — never shared/imported across the sibling packages.

S3StorageOptions  (sealed class — Options-pattern, validated at startup)
    public const string SectionName = "SharedKernel:Storage:S3"
    .ServiceUrl        → string?         (null = real AWS; set to the MinIO/custom endpoint otherwise)
    .Region            → string?         (AWS region system name, e.g. "eu-central-1")
    .AccessKeyId       → string          (required)
    .SecretAccessKey   → string          (required)
    .ForcePathStyle    → bool  (default false; set true for MinIO / path-style addressing)
    .DefaultBucket     → string?         (optional convenience default for single-bucket services)
    NOTE: SectionName is the single source for the config path — never a bare "SharedKernel:Storage:S3"
          literal at a GetSection call site. Validation: AccessKeyId/SecretAccessKey non-empty; when
          ServiceUrl is null, Region must be non-empty.

AddSharedKernelS3Storage(IConfiguration config)  →  IServiceCollection
    Registers:
      — S3StorageOptions bound + validated from S3StorageOptions.SectionName
      — IAmazonS3 as a singleton built from the options (credentials, ServiceUrl, ForcePathStyle)
      — IFileStorage → S3FileStorage (singleton)
      — IBlobUriGenerator → S3BlobUriGenerator (singleton)
    NOTE: IAmazonS3 is thread-safe and pooled — singleton is correct; never scoped/transient.
```

---

### `SharedKernel.Storage.Obs` — public surface

```text
ObsFileStorage  (sealed class, implements IFileStorage)
    — same IAmazonS3-backed implementation shape as S3FileStorage, targeting the OBS S3-compatible
      endpoint. Separate type (not a shared base with S3FileStorage) so the two providers stay
      independently swappable per the sibling-package rule.
    — CopyAsync/DeleteManyAsync/ListAsync/CheckHealthAsync mirror S3FileStorage's shape one-for-one
      (CopyObjectAsync / chunked DeleteObjectsAsync / manually-paged IAsyncEnumerable over
      ListObjectsV2Async / HeadBucketAsync) against the OBS endpoint, using
      ObsStorageConstants.MaxBatchDeleteKeys — independently declared, never imported from `.S3`.

ObsBlobUriGenerator  (sealed class, implements IBlobUriGenerator)
    — OBS presigning via IAmazonS3 request presigning against the OBS endpoint.

ObsStorageConstants  (internal static class — magic-string discipline, SK0022)
    MaxBatchDeleteKeys = 1000   (OBS's S3-compatible DeleteObjects per-request key limit)
    NOTE: Independently declared from S3StorageConstants — same value, deliberately duplicated
          rather than shared, per the sibling-package no-cross-reference rule.

ObsStorageOptions  (sealed class — Options-pattern, validated at startup)
    public const string SectionName = "SharedKernel:Storage:Obs"
    .Endpoint          → string          (required; region OBS endpoint, e.g. "obs.ap-southeast-1.myhuaweicloud.com")
    .AccessKeyId       → string          (required; OBS AK)
    .SecretAccessKey   → string          (required; OBS SK)
    .ForcePathStyle    → bool  (default false)
    .DefaultBucket     → string?         (optional)
    NOTE: Distinct SectionName and options type from S3 — a service may configure both providers
          side by side without collision. Validation: Endpoint/AccessKeyId/SecretAccessKey non-empty.

AddSharedKernelObsStorage(IConfiguration config)  →  IServiceCollection
    Registers ObsStorageOptions (validated), an OBS-endpoint IAmazonS3 singleton, IFileStorage →
    ObsFileStorage, IBlobUriGenerator → ObsBlobUriGenerator.
    NOTE: When a service registers BOTH providers, the last IFileStorage registration wins for the
          default resolve; keyed DI (AddKeyedSingleton) is the intended path for multi-provider
          services — documented at design time, not yet implemented.
```

---

## Implementation Rules

- `SharedKernel.Storage.Abstractions` has **zero third-party NuGet dependencies** — references only `SharedKernel.Primitives`. Adding a cloud SDK reference here is a hard violation.
- Every `IFileStorage` / `IBlobUriGenerator` method returns `Result` / `Result<T>` for **expected** outcomes. Not-found, access-denied, validation, and provider-rejection are `Error` values via `StorageErrors` — never thrown exceptions. Only genuinely exceptional transport faults (socket reset, DNS failure) propagate as exceptions.
- **Stream-first, always.** Payloads flow as `Stream` from caller to provider and back. No `byte[]` upload/download convenience overloads that buffer the whole object in memory — large blobs must never fully materialize in the managed heap.
- `FileUploadRequest.Content` is **caller-owned** — `UploadAsync` never disposes it. `FileDownload` is **caller-disposed** — it is `IAsyncDisposable` and owns the provider network stream.
- Provider `IAmazonS3` clients are **singletons** — thread-safe and connection-pooled. Never register scoped or transient.
- All configuration binds through `SharedKernel.Configuration.AddValidatedOptions` — a misconfigured service fails at `IHost.StartAsync()`, not at first upload.
- Config section paths are a `public const string SectionName` on the options type — never a bare `"SharedKernel:Storage:..."` literal at a `GetSection` call site (magic-string convention, `SK0022`).
- Bucket names, object-key prefixes, and any provider header/metadata keys used at more than one call site are named constants in the owning provider package — never retyped literals.
- `SharedKernel.Storage.S3` and `SharedKernel.Storage.Obs` **never reference each other**. Shared shape is duplicated deliberately; extracting a shared base would couple the two providers and defeat the swap-independence the split exists to protect. This includes their `S3StorageConstants`/`ObsStorageConstants` internal constants classes — same values (e.g. `MaxBatchDeleteKeys = 1000`), independently declared, never shared.
- Production logging uses the `[LoggerMessage]` source-generated pattern with explicit `EventId`s in the **8000–8999** range (`LoggingEventIdRanges.Storage`). CorrelationId / TraceId / TenantId are never explicit message-template placeholders — they flow ambiently through the OTel pipeline.
- No static mutable state anywhere in this domain.
- **`ListAsync`'s streaming shape is a deliberate, documented exception to the Result-first rule above** (P-265): it returns `IAsyncEnumerable<FileMetadata>` directly, not `Task<Result<IAsyncEnumerable<FileMetadata>>>` or similar — mirroring `06.Persistence`'s `IReadRepository.StreamAsync` precedent (P-149). A provider fault mid-enumeration propagates as a thrown exception from `MoveNextAsync`. Do not "fix" this by wrapping it in `Result` — that would diverge from the one existing platform precedent for streaming reads instead of following it.
- `CopyAsync` and `DeleteManyAsync` must never be implemented as a hand-rolled download+upload round-trip or an N-call delete loop — both are backed by the provider's native server-side/batch operation (S3 `CopyObject/DeleteObjects`) so object bytes never flow through application memory and a bulk delete costs one provider call (chunked at the provider's own limit), not N.
- `CheckHealthAsync` is scoped to a bucket and never requires a specific object key to exist — it must not download, upload, or otherwise touch object bytes; it is a connectivity/reachability probe only, backed by the provider's cheapest bucket-level call (e.g. S3 `HeadBucketAsync`).

---

## DI Registration (expected shape)

```csharp
// AWS S3 (or MinIO via ServiceUrl + ForcePathStyle in config):
services.AddSharedKernelS3Storage(configuration);

// Huawei Cloud OBS (S3-compatible endpoint):
services.AddSharedKernelObsStorage(configuration);

// In application code, inject the abstractions — never a cloud SDK type:
//   IFileStorage        → upload / download / delete / exists / metadata / copy / batch-delete /
//                          streaming list / connectivity health probe
//   IBlobUriGenerator   → presigned upload / download URLs for direct client transfer
```

`SharedKernel.Storage.Abstractions` ships **no DI extensions** — it is a pure abstraction library. All registration lives in the provider packages.

---

## AOT Compatibility

- `IFileStorage`, `IBlobUriGenerator`, and all `Models/` records are interface/sealed types over BCL primitives and `Stream` — AOT-safe.
- `StorageErrors` is a static factory returning `Error` values — AOT-safe.
- `S3StorageOptions` / `ObsStorageOptions` bind via `Microsoft.Extensions.Options` — AOT-compatible; verify on each upgrade.
- `AWSSDK.S3` uses reflection in some serialization/paginator code paths — AOT support is partial. Encapsulating it behind `IFileStorage`/`IBlobUriGenerator` limits the AOT blast radius to the registration + provider-implementation path; consuming services stay AOT-clean.
- `ListAsync`'s S3/OBS implementation deliberately hand-writes its own `ContinuationToken` paging loop over `ListObjectsV2Async` instead of using `IAmazonS3`'s built-in paginator helper (`IAmazonS3.Paginators.ListObjectsV2`) — the paginator's `IPaginatedEnumerable<T>` machinery relies on the same reflection-based code path flagged above, and a hand-written loop is both AOT-cleaner and keeps the async-iterator's `[EnumeratorCancellation]` wiring explicit.
- No `Activator.CreateInstance`, no `Assembly.Load`, no reflection in this domain's own code.

---

## Test Rules

- Unit tests for each package live in its own nested `*.Tests` folder (e.g. `08.Storage/SharedKernel.Storage.Abstractions/SharedKernel.Storage.Abstractions.Tests/`).
- `SharedKernel.Storage.Abstractions` tests: `StorageErrors` factory returns the correct `Error` kind/code for each case (including the three P-265 additions — `.CopyFailed`/`.BatchDeleteFailed`/`.ConnectivityFailure`); model records honor value equality (including `FileDeleteOutcome`'s `Succeeded`/`Error` invariant); `PresignedUrl.ExpiresAt` is absolute; reflection-based `ContractShapeTests` lock `IFileStorage.ListAsync`'s `IAsyncEnumerable<FileMetadata>` return shape and `CheckHealthAsync`'s non-generic `Task<Result>` shape against silent regression (mirrors `06.Persistence`'s `ContractShapeTests` precedent).
- Provider tests exercise the real S3 API against a **Testcontainers MinIO** container (via `16.Testing`, P-268) — never mock `IAmazonS3` for behavioral coverage. Assert round-trip upload → download → copy → batch-delete → not-found, streaming-list (yields all seeded items via `await foreach` without materializing an intermediate list; cancellation mid-enumeration stops paging), connectivity probe (healthy + unreachable), presigned-URL round-trip, and status-code → `StorageErrors` mapping. `SharedKernel.Storage.Obs`'s own suite stands the same MinIO container in for the OBS S3-compatible endpoint — OBS is never available in CI. **Sanctioned exception:** status-code → `StorageErrors` mapping assertions (404/403/500 across Download/GetMetadata/Exists/Delete/Copy/DeleteMany/CheckHealth) may substitute `IAmazonS3` via `NSubstitute` instead of a real backend — inducing a real 403/500 without live IAM/bucket-policy setup is impractical, and both `S3FileStorage`/`ObsFileStorage` take `IAmazonS3` as a plain constructor parameter, making substitution direct. `UploadAsync`'s own status-mapping is NOT reachable this way — it routes through an internally-constructed `TransferUtility`, not the injected client directly — and remains provider-round-trip-only coverage.
- **`16.Testing`'s `MinioContainerFixture` (P-268) may not exist yet when this Tests phase runs** — verified on disk, not assumed, before starting: check `16.Testing/SharedKernel.Testing/Containers/MinioContainerFixture.cs` exists and `16.Testing/state-map.md`'s `SK.16.Core` task `C-60` is `●`. If it does not exist yet, implement every task that genuinely needs no live backend (Abstractions unit tests; DI-registration and options-validation tests, which need only `ServiceCollection`/`BuildServiceProvider()`; the sanctioned status-code-mapping mocks above; the sibling-independence source scan) and mark the true round-trip/streaming/presigned-HTTP/MinIO-path-confirmation tasks `⚑` Blocked in `state-map.md` rather than hand-rolling a competing ad-hoc Testcontainers MinIO setup — that duplication is exactly what P-268 exists to prevent.
- MinIO-path confirmation: the same round-trip/streaming-list/connectivity-probe/presigned-URL suite passes unchanged with `ServiceUrl`+`ForcePathStyle` configured, proving no MinIO-specific code path exists in either provider.
- `S3StorageOptions` / `ObsStorageOptions` validation: valid config registers without throw; missing credentials fail at startup; over-long presign expiry returns `StorageErrors.ExpiryTooLong`. Resolving `IOptions<TOptions>.Value` directly (no `IHost` needed) is sufficient to trigger the `ValidateDataAnnotations()` failure — `SharedKernel.Configuration.AddValidatedOptions`'s `IValidateOptions<T>` runs on first `.Value`/`.CurrentValue` access regardless of whether `.ValidateOnStart()`'s eager host-startup check ever fires. The `Expiry`-too-long check is local/network-free (presigning never calls the network) — test it directly against `S3BlobUriGenerator`/`ObsBlobUriGenerator` with a substituted `IAmazonS3` that is never configured to return anything, since the clamp check runs before any client call.
- DI registration tests: `IFileStorage` / `IBlobUriGenerator` resolve; `IAmazonS3` resolves as a singleton; use `ServiceCollection` + `BuildServiceProvider()` — no web host required for DI-level verification. `AddSharedKernelS3Storage()`/`AddSharedKernelObsStorage()` deliberately do **not** register `ILogger<T>` themselves (that is the consuming host's responsibility, e.g. via `Host.CreateDefaultBuilder()`'s built-in logging) — a DI-only test resolving `IFileStorage`/`IBlobUriGenerator` must additionally register `services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))` itself or the resolve throws `InvalidOperationException` for the unresolved `ILogger<S3FileStorage>`/`ILogger<S3BlobUriGenerator>` (and Obs equivalents).
- `SharedKernel.Storage.Obs`'s test project additionally carries a lightweight sibling-independence check (no `using SharedKernel.Storage.S3` anywhere in its own source/tests) — the authoritative enforcement is `00.Governance`'s `StorageTopologyRules`/SK0023 architecture test, outside this domain's own test suite. Implement this as a `[System.Runtime.CompilerServices.CallerFilePath]`-anchored scan of the package folder for `using`-directive lines specifically (trimmed-line prefix match, not whole-file substring search) — a naive `string.Contains("using SharedKernel.Storage.S3")` over full file text false-positives on the test's own descriptive XML-doc prose mentioning that same phrase.

---

## Changelog

> Maintained by the storage domain agent. One line per significant change.

- [2026-07-16] Domain brain initialized — packages (Abstractions + S3 + Obs), technology stack (AWSSDK.S3 for both S3/MinIO and Huawei OBS's S3-compatible endpoint), `IFileStorage`/`IBlobUriGenerator` interface surface, Result-valued error convention, options/DI shape, AOT + test rules; OBS packaged as its own `SharedKernel.Storage.Obs` over `AWSSDK.S3` (native `HuaweiCloud.ESDK.OBS.Core` rejected as stale/.NET Standard 2.0/personal-account) (root, user request)
- [2026-07-16] WO-043 P-265/P-266/P-267 — `IFileStorage` finalized to nine members before any provider code exists: added `CopyAsync` (server-side copy), `DeleteManyAsync` (batch delete with per-key `FileDeleteOutcome` results), redesigned `ListAsync` from a fully-materialized `Task<Result<IReadOnlyList<FileMetadata>>>` to a constant-memory streaming `IAsyncEnumerable<FileMetadata>` (documented Result-first exception, mirrors `06.Persistence` P-149), and added `CheckHealthAsync` (bucket-scoped, non-generic `Result` connectivity probe backing the future `13.ServiceDefaults` readiness check). `StorageErrors` gained `.CopyFailed`/`.BatchDeleteFailed`/`.ConnectivityFailure`. `S3FileStorage`/`ObsFileStorage` implementation shapes specified for all four new members (`CopyObjectAsync`/chunked `DeleteObjectsAsync`/hand-paged `ListObjectsV2Async`/`HeadBucketAsync`), plus new `S3StorageConstants`/`ObsStorageConstants` (`MaxBatchDeleteKeys = 1000`, independently declared per sibling-package rule) and the AOT rationale for hand-writing the list-paging loop instead of using `IAmazonS3`'s reflection-based paginator helper. Full six-phase task plan (88 tasks) added to `state-map.md` (arch-lead, WO-043, P-265–P-267)
- [2026-07-16] Design phase (SK.08.Design, D-01–D-15) verified complete against this file — corrected a repeated "ten-member `IFileStorage`" miscount to the accurate nine (five original + `CopyAsync`/`DeleteManyAsync`/`ListAsync`/`CheckHealthAsync`) in this changelog and mirrored in `state-map.md`; added the previously-undocumented "considered-and-rejected `Result<StorageHealthProbe>`" rationale to the `CheckHealthAsync` contract note for full design traceability. No interface, model, or rule changes — contract confirmed locked as the Scaffold-phase basis (storage-phase-implementer, WO-043)
- [2026-07-16] Scaffold phase (SK.08.Scaffold, S-01–S-12) complete — all three packages' `.csproj` files wired to the design-locked reference shape; `AWSSDK.S3` version confirmed and pinned to `4.0.101.1` (latest stable on nuget.org at implementation time) for both `.S3` and `.Obs`, updating the prior "4.x" placeholder in the Technology Stack table to the exact confirmed version; net-new `SharedKernel.Storage.Abstractions.Tests` and the entire `SharedKernel.Storage.Obs`(+`.Tests`) package created from scratch, folder-for-folder mirroring `.S3`'s shape (`FileStorage/`, `BlobUri/`, `Options/`, `Constants/`, `Logging/`, `Extensions/`) with zero `ProjectReference` to `.S3`; all six projects registered in `Platform.SharedKernel.slnx` (verified via `dotnet sln list`) and build with 0 errors. Folder/file stubs carry no logic per phase scope — Core phase (C-01 onward) is next (storage-phase-implementer, WO-043)
- [2026-07-17] Core phase (SK.08.Core, C-01–C-30) complete — Abstractions and `.S3` were found already fully implemented (production-quality, full XML docs) from an earlier uncommitted session, verified correct against this file's locked D-07 contract with zero interface/model/error deviations; found and fixed one real defect — both `.S3`'s and `.Obs`'s `.csproj` files were missing a `Microsoft.Extensions.Logging.Abstractions` `PackageReference`, so neither project actually compiled despite complete `[LoggerMessage]` code (`ILogger<T>`/`LogLevel`/`LoggerMessage` all unresolved); fixed by adding the package (see Technology Stack/Packages table updates above). `SharedKernel.Storage.Obs` implemented net-new this session — `ObsFileStorage`/`ObsBlobUriGenerator`/`ObsStorageOptions`/`ObsStorageConstants`/`AddSharedKernelObsStorage`/`ObsStorageLog` (EventId 8200-8299) — mirroring `.S3`'s already-shipped shape one-for-one with zero deviation from this file's already-locked `.Obs` public-surface spec. Grep-verified zero project/type reference between `.S3` and `.Obs` in either direction. All six projects build with 0 errors individually. Test-writing deliberately deferred to the already-fully-specified Tests phase (SK.08.Tests) — Core's own task list contained only Implement/Confirm tasks (storage-phase-implementer, WO-043)
- [2026-07-17] Tests phase partially complete (10/17 tasks, 91/91 tests passing) — verified on disk that `16.Testing`'s `MinioContainerFixture` (P-268) does not exist yet, so all genuinely container-free tasks were implemented (Abstractions unit tests; S3/Obs DI-registration and options-validation tests; S3 status-code mapping via the newly-sanctioned `NSubstitute` exception; Obs sibling-independence source scan) and the seven real-backend round-trip/streaming/presigned-HTTP/MinIO-path tasks left `⚑` Blocked in `state-map.md` rather than hand-rolling a competing container setup. New Technology Stack row for `NSubstitute` `5.3.0` (scoped to `S3.Tests` narrow error-mapping only); Test Rules gained the sanctioned-mocking-exception clarification, the missing-fixture pre-check instruction, the `IOptions<T>.Value`-triggers-validation-without-a-host technique, the `AddSharedKernel{S3,Obs}Storage()`-does-not-register-`ILogger<T>` DI gotcha, and the `[CallerFilePath]`-anchored line-scan technique for the sibling-independence check (storage-phase-implementer, WO-043)
