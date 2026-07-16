# 08.Storage — State Map

> **What this file is:** Phase and task tracker for all work within `08.Storage`.
> **What it is not:** The root tracker — that lives at `state-map.md`.
> **Sync policy:** When all tasks under a Phase Key are `●`, run `/state-map-phase` with `phase_key: SK.08.{Phase}` to propagate that milestone to the root state-map.

---

## Legend

| Symbol | Meaning |
| --- | --- |
| `○` | Not started |
| `◐` | In progress |
| `●` | Complete |
| `⚑` | Blocked |
| `—` | N/A / Skipped |

---

## Phase Key Registry

> Phase keys are the sync bridge between this sub-state-map and the root `state-map.md`.
> Each key maps a local milestone to a root-level phase. When a key's Promotion Condition is met, the root is updated via `/state-map-phase`.

| Phase Key | Maps to Root Phase | Promotion Condition |
| --- | --- | --- |
| `SK.08.Design` | Design | All tasks in Phase: Design are `●` |
| `SK.08.Scaffold` | Scaffold | All tasks in Phase: Scaffold are `●` |
| `SK.08.Core` | Core | All tasks in Phase: Core are `●` |
| `SK.08.Tests` | Tests | All tasks in Phase: Tests are `●` |
| `SK.08.Docs` | Docs | All tasks in Phase: Docs are `●` |
| `SK.08.Published` | Published | All tasks in Phase: Published are `●` |

---

## Active Work

_Nothing in progress._

<!--
Format when active — replace placeholder with table:
| Task | Phase Key | Package | State |
|------|-----------|---------|:-----:|
| Implement IFileStorage | SK.08.Core | SharedKernel.Storage.Abstractions | ◐ |
-->

---

## Blocked

_No blockers._

<!--
Format when blocked — replace placeholder with table:
| Task | Phase Key | Blocker |
|------|-----------|---------|
| Example blocked task | SK.08.Core | Waiting on upstream decision |
-->

---

## Package Board

| Package | Current Phase | State | Notes |
| --- | --- | :---: | --- |
| `SharedKernel.Storage.Abstractions` | Design | `○` | `IFileStorage` (now ten members — Upload/Download/Delete/Exists/GetMetadata unchanged, plus new CopyAsync/DeleteManyAsync/streaming ListAsync/CheckHealthAsync per P-265) / `IBlobUriGenerator` + seven models (six original + new `FileDeleteOutcome`) + nine-member `StorageErrors` (six original + `.CopyFailed`/`.BatchDeleteFailed`/`.ConnectivityFailure`); references `SharedKernel.Primitives` only; zero third-party NuGet. A bare placeholder `.csproj` (TargetFramework/ImplicitUsings/Nullable only, no references, no content) already exists and is registered in `Platform.SharedKernel.slnx` from an earlier repo-wide bootstrap pass — Scaffold fleshes it out rather than creating from scratch |
| `SharedKernel.Storage.S3` | Design | `○` | AWS S3 / MinIO via `AWSSDK.S3` (MinIO through `ServiceUrl` + `ForcePathStyle`); references Abstractions + Configuration; implements the finalized ten-member `IFileStorage` including server-side `CopyObjectAsync`-backed copy, chunked `DeleteObjectsAsync`-backed batch delete (`S3StorageConstants.MaxBatchDeleteKeys = 1000`), manually-paged `IAsyncEnumerable` streaming list (no `IAmazonS3` paginator helper — AOT rationale), and `HeadBucketAsync`-backed connectivity probe. Bare placeholder `.csproj` + empty `.Tests.csproj` already exist and are registered in the `.slnx` |
| `SharedKernel.Storage.Obs` | Design | `○` | Huawei Cloud OBS over its S3-compatible endpoint via `AWSSDK.S3`; sibling of `.S3` (never references it, independently declares its own `ObsStorageConstants`); references Abstractions + Configuration; mirrors `.S3`'s implementation shape one-for-one against the OBS endpoint. Package and Tests project do not exist yet — net-new in Scaffold, unlike Abstractions/S3 |

---

## Cross-Domain Dependencies

| This Phase Key | Needs From Domain | What | Status |
| --- | --- | --- | --- |
| `SK.08.Scaffold` | `01.Core` | `SharedKernel.Primitives` ProjectReference (`Result<T>`, `Result`, `Error`) | Available |
| `SK.08.Scaffold` | `01.Core` | `SharedKernel.Configuration` ProjectReference (`AddValidatedOptions`) — provider packages only | Available |
| `SK.08.Core` | `01.Core` | `LoggingEventIdRanges.Storage` (8000–8999) `EventId` range registry for `[LoggerMessage]` logging | Available |
| `SK.08.Tests` | `16.Testing` | Testcontainers MinIO fixture (P-268) + shared helpers for provider round-trip tests — shared by both `.S3` and `.Obs` test suites (OBS's own test suite stands the same MinIO container in for the OBS S3-compatible endpoint, per P-267) | Pending (16.Testing, P-268 `○`) |

> **Downstream note (not an inbound blocker for this domain):** P-265's finalized `IFileStorage`/`IBlobUriGenerator` contract unblocks `16.Testing`'s `InMemoryFileStorage` fake (P-269) and `13.ServiceDefaults`'s storage readiness health-check adapter (P-270), and `00.Governance`'s `StorageTopologyRules`/SK0023 (P-271) has already been designed against this domain's package/namespace shape ahead of any code existing — see that domain's own state-map for status.

---

## Phase: Design <!-- phase-key: SK.08.Design -->

> Finalize all interface shapes, model records, error factory, options contracts, and DI extension signatures before any implementation begins. Covers P-265 (Abstractions contract finalization, D-01–D-08), P-266 (`.S3` provider design, D-09–D-12), and P-267 (`.Obs` provider design, D-13–D-15) — P-266/P-267 depend on P-265's contract being locked first.

**Design (Abstractions contract finalization, P-265) — D-01 through D-08:**

| ID | Task | Package(s) | State |
| --- | --- | --- | :---: |
| D-01 | Ratify the existing six-member `IFileStorage` contract (`UploadAsync`/`DownloadAsync`/`DeleteAsync`/`ExistsAsync`/`GetMetadataAsync` — `ListAsync` is redesigned below) already sketched in `08.Storage/CLAUDE.md` — confirm `Result`/`Result<T>` shape, Stream-first semantics, and caller-owned/caller-disposed stream lifetimes are unchanged by this phase; P-265 only adds four new capabilities, it does not redesign the existing five | SharedKernel.Storage.Abstractions | `○` |
| D-02 | Design `IFileStorage.CopyAsync(string sourceBucket, string sourceKey, string destinationBucket, string destinationKey, CancellationToken ct) → Task<Result<FileReference>>` — server-side object copy; the provider issues a native copy call so object bytes never flow through application memory; returns the destination `FileReference` (new `ETag`/`VersionId`); source object is left untouched (copy, not move) | SharedKernel.Storage.Abstractions | `○` |
| D-03 | Design `IFileStorage.DeleteManyAsync(string bucket, IReadOnlyCollection<string> keys, CancellationToken ct) → Task<Result<IReadOnlyList<FileDeleteOutcome>>>` plus new `FileDeleteOutcome` sealed record (`Key: string`, `Succeeded: bool`, `Error: Error?`) — the outer `Result` fails only when the batch call itself cannot be attempted (empty/null keys, transport fault); the inner per-key list carries each key's individual outcome, mirroring S3's native `DeleteObjects` `Deleted[]`/`Errors[]` response shape, so a partial batch failure never masquerades as one opaque error | SharedKernel.Storage.Abstractions | `○` |
| D-04 | Redesign `IFileStorage.ListAsync` from the originally-sketched `Task<Result<IReadOnlyList<FileMetadata>>>` to `IAsyncEnumerable<FileMetadata> ListAsync(string bucket, string prefix, [EnumeratorCancellation] CancellationToken ct)` — constant-memory streaming enumeration, mirroring `06.Persistence`'s `IReadRepository.StreamAsync` precedent (P-149). Ratify the accepted trade-off that precedent already established: the method is **not** `Result`-wrapped — a provider fault mid-enumeration (e.g., credentials revoked between pages) propagates as a thrown exception from `MoveNextAsync`, not an `Error` value. This is a deliberate, documented exception to this domain's own Result-first convention, justified by consistency with the platform's one existing streaming-read precedent | SharedKernel.Storage.Abstractions | `○` |
| D-05 | Design `IFileStorage.CheckHealthAsync(string bucket, CancellationToken ct) → Task<Result>` — a lightweight, non-generic `Result` connectivity probe scoped to a bucket (mirrors `ExistsAsync`'s bucket-scoped, no-body shape minus the key), backed by the provider's cheapest bucket-reachability call; never requires a specific object key to exist, never touches object bytes. Considered-and-rejected alternative: a richer `Result<StorageHealthProbe>` payload carrying latency, mirroring `06.Persistence`'s `DatabaseReadinessResult` — rejected because P-265's acceptance criteria specifies a plain `Result`, and `13.ServiceDefaults`'s eventual `IHealthCheck` adapter (P-270) can derive Healthy/Unhealthy plus a description directly from `Result.IsSuccess`/`Error.Description`; a richer payload remains a strictly additive future change if ever needed | SharedKernel.Storage.Abstractions | `○` |
| D-06 | Extend `StorageErrors` with three new factory members — `.CopyFailed(sourceBucket, sourceKey, destinationBucket, destinationKey)`, `.BatchDeleteFailed(bucket)` (outer batch-call-level failure only; per-key failures inside `FileDeleteOutcome.Error` reuse the existing `.NotFound`/`.AccessDenied` factories), `.ConnectivityFailure(bucket)` (`CheckHealthAsync` failure path) — all three route through `Error.Failure`, no new `Error` kind introduced | SharedKernel.Storage.Abstractions | `○` |
| D-07 | Lock the final `08.Storage/CLAUDE.md` Interface Contracts section for `SharedKernel.Storage.Abstractions` as the basis for Scaffold — ten-member `IFileStorage` (five unchanged + `CopyAsync`/`DeleteManyAsync`/streaming `ListAsync`/`CheckHealthAsync`), unchanged `IBlobUriGenerator`, seven `Models/` records (six original + `FileDeleteOutcome`), nine-member `StorageErrors` | SharedKernel.Storage.Abstractions | `○` |
| D-08 | Re-confirm `SharedKernel.Storage.Abstractions` still carries zero third-party NuGet dependencies after D-02–D-06 — every new signature uses only BCL types (`string`, `IReadOnlyCollection<string>`, `IAsyncEnumerable<T>`, `CancellationToken`) plus `SharedKernel.Primitives`' `Result`/`Result<T>`/`Error` | SharedKernel.Storage.Abstractions | `○` |

**Design (`.S3` provider, P-266) — D-09 through D-12:**

| ID | Task | Package(s) | State |
| --- | --- | --- | :---: |
| D-09 | Ratify `S3FileStorage`'s implementation shape for the four new `IFileStorage` members against `Amazon.S3.IAmazonS3`: `CopyAsync` → `CopyObjectAsync` (native server-side copy, no `TransferUtility`); `DeleteManyAsync` → `DeleteObjectsAsync` (chunked internally at a provider-owned constant of 1000 keys per call — S3's hard per-request limit — invisible to the `IFileStorage` caller); `ListAsync` → `ListObjectsV2Async` driven page-by-page through a hand-written `await foreach`-friendly async iterator (no `IAmazonS3` paginator helper, to avoid its reflection-based AOT-unfriendly code path); `CheckHealthAsync` → `HeadBucketAsync` | SharedKernel.Storage.S3 | `○` |
| D-10 | Design the `AmazonS3Exception` status-code → `StorageErrors` mapping table for the new members: `CopyAsync` 404-on-source → `.NotFound(sourceBucket, sourceKey)`, 403 → `.AccessDenied`, other → `.CopyFailed`; `DeleteManyAsync` per-key errors from `DeleteObjectsResponse.DeleteErrors` map to `FileDeleteOutcome.Error` via the same `.NotFound`/`.AccessDenied` factories, a whole-call transport fault maps to the outer `.BatchDeleteFailed`; `CheckHealthAsync` 404/403/timeout → `.ConnectivityFailure(bucket)` | SharedKernel.Storage.S3 | `○` |
| D-11 | Re-confirm `S3StorageOptions`/`AddSharedKernelS3Storage()` shape already sketched in `08.Storage/CLAUDE.md` is unchanged by the new members — no new configuration surface required; copy/batch-delete/list/health-check all reuse the same singleton `IAmazonS3` client | SharedKernel.Storage.S3 | `○` |
| D-12 | Design `S3StorageConstants` internal constants class (magic-string discipline, SK0022) holding `MaxBatchDeleteKeys = 1000` plus any S3-specific header/metadata keys used at more than one call site | SharedKernel.Storage.S3 | `○` |

**Design (`.Obs` provider, P-267) — D-13 through D-15:**

| ID | Task | Package(s) | State |
| --- | --- | --- | :---: |
| D-13 | Ratify `ObsFileStorage` mirrors `S3FileStorage`'s shape one-for-one for the four new members (`CopyAsync`/`DeleteManyAsync`/`ListAsync`/`CheckHealthAsync`) against the OBS S3-compatible endpoint via the same `IAmazonS3` surface; confirm OBS's batch-delete limit is also 1000 keys (S3-API-compatible) so `ObsStorageConstants.MaxBatchDeleteKeys` mirrors `S3StorageConstants` as an independently-declared constant, never shared/imported from `.S3` | SharedKernel.Storage.Obs | `○` |
| D-14 | Re-confirm `ObsStorageOptions`/`AddSharedKernelObsStorage()` shape already sketched in `08.Storage/CLAUDE.md` is unchanged by the new members; re-confirm the keyed-DI note for side-by-side `.S3`+`.Obs` registration remains a documented (not-yet-implemented) design intent | SharedKernel.Storage.Obs | `○` |
| D-15 | Ratify zero project/type reference from `SharedKernel.Storage.Obs` to `SharedKernel.Storage.S3` remains true after D-13/D-14 — `ObsStorageConstants` is a wholly independent declaration, not a shared/base type; this is also the exact assumption `00.Governance`'s `StorageTopologyRules`/SK0023 (P-271) has already designed enforcement against | SharedKernel.Storage.Obs | `○` |

---

## Phase: Scaffold <!-- phase-key: SK.08.Scaffold -->

> Wire up .csproj NuGet references, intra-domain project references, folder structure, solution registration, and empty test stubs — no logic yet. Bare placeholder `.csproj` files (TargetFramework/ImplicitUsings/Nullable only, zero references, zero content) already exist for `SharedKernel.Storage.Abstractions` and `SharedKernel.Storage.S3` (+ its `.Tests` project), registered in `Platform.SharedKernel.slnx` from an earlier repo-wide bootstrap pass — S-01/S-05/S-07 flesh these out rather than creating from scratch. `SharedKernel.Storage.Obs` (+ its `.Tests` project) and `SharedKernel.Storage.Abstractions.Tests` do not exist yet and are net-new.

| ID | Task | Package(s) | State |
| --- | --- | --- | :---: |
| S-01 | Flesh out the existing empty `SharedKernel.Storage.Abstractions.csproj` placeholder — add the single `ProjectReference` to `SharedKernel.Primitives`; add zero `PackageReference` entries, confirming the zero-third-party-NuGet rule at the project-file level | SharedKernel.Storage.Abstractions | `○` |
| S-02 | Create folder structure inside `SharedKernel.Storage.Abstractions`: `Abstractions/` (`IFileStorage.cs`, `IBlobUriGenerator.cs`), `Models/` (`FileUploadRequest.cs`, `FileReference.cs`, `FileDownload.cs`, `FileMetadata.cs`, `FileDeleteOutcome.cs`, `PresignedUrlRequest.cs`, `PresignedUrl.cs`), `Errors/` (`StorageErrors.cs`) — empty stub files only, no logic | SharedKernel.Storage.Abstractions | `○` |
| S-03 | Create `SharedKernel.Storage.Abstractions.Tests.csproj` nested inside the package folder (`net10.0` classlib) with `ProjectReference`s to `SharedKernel.Storage.Abstractions` and `SharedKernel.Testing`, standard test package set (`xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `coverlet.collector`, `FluentAssertions`), and `GlobalUsings.cs` with `global using Xunit;` | SharedKernel.Storage.Abstractions.Tests | `○` |
| S-04 | Register `SharedKernel.Storage.Abstractions.Tests.csproj` in `Platform.SharedKernel.slnx` under the `08.Storage` solution folder (the Abstractions main project is already registered) | SharedKernel.Storage.Abstractions.Tests | `○` |
| S-05 | Flesh out the existing empty `SharedKernel.Storage.S3.csproj` placeholder — add `ProjectReference`s to `SharedKernel.Storage.Abstractions` and `SharedKernel.Configuration`; add `PackageReference` to `AWSSDK.S3` (version pinned at implementation time; recorded once confirmed) | SharedKernel.Storage.S3 | `○` |
| S-06 | Create folder structure inside `SharedKernel.Storage.S3`: `FileStorage/` (`S3FileStorage.cs`), `BlobUri/` (`S3BlobUriGenerator.cs`), `Options/` (`S3StorageOptions.cs`), `Constants/` (`S3StorageConstants.cs`), `Logging/` (internal `[LoggerMessage]` partial class), `Extensions/` (`S3StorageServiceCollectionExtensions.cs` — `AddSharedKernelS3Storage`) — empty stub files only | SharedKernel.Storage.S3 | `○` |
| S-07 | Flesh out the existing empty `SharedKernel.Storage.S3.Tests.csproj` placeholder — add `ProjectReference`s to `SharedKernel.Storage.S3` and `SharedKernel.Testing` (for the eventual Testcontainers MinIO fixture, P-268), standard test package set, `GlobalUsings.cs` | SharedKernel.Storage.S3.Tests | `○` |
| S-08 | Create `SharedKernel.Storage.Obs.csproj` from scratch (`net10.0`, `ImplicitUsings` enabled, `Nullable` enabled) with `ProjectReference`s to `SharedKernel.Storage.Abstractions` and `SharedKernel.Configuration`, `PackageReference` to `AWSSDK.S3` (same pinned version as `.S3`) — explicitly no `ProjectReference` to `SharedKernel.Storage.S3` | SharedKernel.Storage.Obs | `○` |
| S-09 | Create folder structure inside `SharedKernel.Storage.Obs` mirroring `.S3`'s shape one-for-one: `FileStorage/` (`ObsFileStorage.cs`), `BlobUri/` (`ObsBlobUriGenerator.cs`), `Options/` (`ObsStorageOptions.cs`), `Constants/` (`ObsStorageConstants.cs`), `Logging/`, `Extensions/` (`ObsStorageServiceCollectionExtensions.cs` — `AddSharedKernelObsStorage`) — empty stub files only | SharedKernel.Storage.Obs | `○` |
| S-10 | Create `SharedKernel.Storage.Obs.Tests.csproj` nested inside the package folder with `ProjectReference`s to `SharedKernel.Storage.Obs` and `SharedKernel.Testing`, standard test package set, `GlobalUsings.cs` | SharedKernel.Storage.Obs.Tests | `○` |
| S-11 | Register `SharedKernel.Storage.Obs.csproj` and `SharedKernel.Storage.Obs.Tests.csproj` in `Platform.SharedKernel.slnx` under the `08.Storage` solution folder | SharedKernel.Storage.Obs, SharedKernel.Storage.Obs.Tests | `○` |
| S-12 | Verify `dotnet build` succeeds with zero errors and zero warnings across all six projects (`Abstractions`, `Abstractions.Tests`, `S3`, `S3.Tests`, `Obs`, `Obs.Tests`) on the empty/stub scaffold | All | `○` |

---

## Phase: Core <!-- phase-key: SK.08.Core -->

> Full implementation of all types, interfaces, extensions, and DI registrations. Split into three independently-verifiable sub-passes — Abstractions (interfaces/models/errors, no provider logic) before `.S3` (the reference provider) before `.Obs` (mirrors `.S3`'s shape against the OBS endpoint) — so each provider is built on top of an already-locked contract rather than co-evolving with it.

**Core (Abstractions) — C-01 through C-06:**

| ID | Task | Package(s) | State |
| --- | --- | --- | :---: |
| C-01 | Implement `IFileStorage` — ten members per D-07's locked contract: `UploadAsync`/`DownloadAsync`/`DeleteAsync`/`ExistsAsync`/`GetMetadataAsync` (unchanged) plus `CopyAsync`/`DeleteManyAsync`/`ListAsync` (now `IAsyncEnumerable<FileMetadata>`)/`CheckHealthAsync` | SharedKernel.Storage.Abstractions | `○` |
| C-02 | Implement `IBlobUriGenerator` — `GeneratePresignedUploadUrl`/`GeneratePresignedDownloadUrl`, unchanged by this phase | SharedKernel.Storage.Abstractions | `○` |
| C-03 | Implement all seven `Models/` records — `FileUploadRequest`, `FileReference`, `FileDownload` (`IAsyncDisposable`), `FileMetadata`, `FileDeleteOutcome` (new, per D-03), `PresignedUrlRequest`, `PresignedUrl` — all sealed, value-equality records except `FileDownload` (sealed class, disposable resource holder) | SharedKernel.Storage.Abstractions | `○` |
| C-04 | Implement `StorageErrors` static factory — nine members: `.NotFound`/`.AccessDenied`/`.InvalidBucket`/`.InvalidKey`/`.ExpiryTooLong`/`.UploadFailed` (unchanged) plus `.CopyFailed`/`.BatchDeleteFailed`/`.ConnectivityFailure` (new, per D-06) | SharedKernel.Storage.Abstractions | `○` |
| C-05 | Confirm zero third-party NuGet dependency and zero reflection anywhere in `SharedKernel.Storage.Abstractions` — only BCL types + the `SharedKernel.Primitives` project reference | SharedKernel.Storage.Abstractions | `○` |
| C-06 | Confirm full AOT compatibility of the Abstractions surface — interfaces, sealed records/classes, `IAsyncEnumerable<T>` over BCL primitives; no `Activator.CreateInstance`, no `Assembly.Load` | SharedKernel.Storage.Abstractions | `○` |

**Core (`.S3`) — C-07 through C-19:**

| ID | Task | Package(s) | State |
| --- | --- | --- | :---: |
| C-07 | Implement `S3FileStorage.UploadAsync`/`DownloadAsync`/`DeleteAsync`/`ExistsAsync`/`GetMetadataAsync` against `IAmazonS3` — `UploadAsync` via `TransferUtility` for multipart-aware streaming; `DownloadAsync` returns a `FileDownload` wrapping the provider response stream as `IAsyncDisposable`; `DeleteAsync` idempotent (absent key succeeds) | SharedKernel.Storage.S3 | `○` |
| C-08 | Implement `S3FileStorage.CopyAsync` via `IAmazonS3.CopyObjectAsync` (per D-09) — server-side copy, zero bytes through managed memory; maps source-404 → `StorageErrors.NotFound`, 403 → `.AccessDenied`, other → `.CopyFailed` (per D-10) | SharedKernel.Storage.S3 | `○` |
| C-09 | Implement `S3FileStorage.DeleteManyAsync` via `IAmazonS3.DeleteObjectsAsync`, internally chunking at `S3StorageConstants.MaxBatchDeleteKeys` (1000) per call — maps `DeleteObjectsResponse.DeleteErrors` to per-key `FileDeleteOutcome.Error` via `.NotFound`/`.AccessDenied`; a whole-call transport fault surfaces as the outer `StorageErrors.BatchDeleteFailed` | SharedKernel.Storage.S3 | `○` |
| C-10 | Implement `S3FileStorage.ListAsync` as an `async IAsyncEnumerable<FileMetadata>` iterator over `IAmazonS3.ListObjectsV2Async`, paging manually via `ContinuationToken` (no `IAmazonS3` paginator helper, per D-09's AOT rationale) — `[EnumeratorCancellation]` on the `CancellationToken` parameter | SharedKernel.Storage.S3 | `○` |
| C-11 | Implement `S3FileStorage.CheckHealthAsync` via `IAmazonS3.HeadBucketAsync` — maps 404/403/timeout → `StorageErrors.ConnectivityFailure(bucket)` (per D-10) | SharedKernel.Storage.S3 | `○` |
| C-12 | Implement `S3BlobUriGenerator` — `GeneratePresignedUploadUrl`/`GeneratePresignedDownloadUrl` via `IAmazonS3` request presigning (`GetPreSignedURL`), clamping `Expiry` to the S3-family 7-day maximum and returning `StorageErrors.ExpiryTooLong` when exceeded; absolute `ExpiresAt` derived once | SharedKernel.Storage.S3 | `○` |
| C-13 | Implement `S3StorageOptions` (`SectionName = "SharedKernel:Storage:S3"`) — `ServiceUrl`, `Region`, `AccessKeyId`, `SecretAccessKey`, `ForcePathStyle`, `DefaultBucket` — validated via `SharedKernel.Configuration.AddValidatedOptions`: `AccessKeyId`/`SecretAccessKey` required; `Region` required when `ServiceUrl` is null | SharedKernel.Storage.S3 | `○` |
| C-14 | Implement `S3StorageConstants` — `MaxBatchDeleteKeys = 1000` plus any S3-specific header/metadata key constants used at more than one call site (SK0022) | SharedKernel.Storage.S3 | `○` |
| C-15 | Implement `AddSharedKernelS3Storage(IConfiguration)` — binds+validates `S3StorageOptions`, registers `IAmazonS3` as a singleton (credentials + `ServiceUrl` + `ForcePathStyle` from options), registers `IFileStorage → S3FileStorage` and `IBlobUriGenerator → S3BlobUriGenerator` as singletons | SharedKernel.Storage.S3 | `○` |
| C-16 | Implement `[LoggerMessage]`-attributed logging inside `S3FileStorage`/`S3BlobUriGenerator` with explicit `EventId`s in the `8100-8199` sub-block (`LoggingEventIdRanges.Storage + 100`..`+199`) — no direct `ILogger` extension-method calls | SharedKernel.Storage.S3 | `○` |
| C-17 | Confirm MinIO is exercised through the identical `S3FileStorage`/`S3BlobUriGenerator` code path as real AWS S3 — no MinIO-specific branch, type, or conditional anywhere in the implementation; only `ServiceUrl`+`ForcePathStyle` configuration differs | SharedKernel.Storage.S3 | `○` |
| C-18 | Confirm `IAmazonS3` is registered and resolves as a singleton — never scoped/transient | SharedKernel.Storage.S3 | `○` |
| C-19 | Confirm zero project or type reference from `SharedKernel.Storage.S3` to `SharedKernel.Storage.Obs` | SharedKernel.Storage.S3 | `○` |

**Core (`.Obs`) — C-20 through C-30:**

| ID | Task | Package(s) | State |
| --- | --- | --- | :---: |
| C-20 | Implement `ObsFileStorage`'s five original members (`UploadAsync`/`DownloadAsync`/`DeleteAsync`/`ExistsAsync`/`GetMetadataAsync`) against `IAmazonS3` pointed at the OBS S3-compatible endpoint — independent type from `S3FileStorage`, not a shared base (per the sibling-package rule) | SharedKernel.Storage.Obs | `○` |
| C-21 | Implement `ObsFileStorage.CopyAsync`/`DeleteManyAsync`/`ListAsync`/`CheckHealthAsync` mirroring `S3FileStorage`'s C-08–C-11 shape one-for-one against the OBS endpoint, using `ObsStorageConstants.MaxBatchDeleteKeys` (independently declared, per D-13) | SharedKernel.Storage.Obs | `○` |
| C-22 | Implement `ObsBlobUriGenerator` mirroring `S3BlobUriGenerator`'s presign/clamp shape (C-12) against the OBS endpoint | SharedKernel.Storage.Obs | `○` |
| C-23 | Implement `ObsStorageOptions` (`SectionName = "SharedKernel:Storage:Obs"`) — `Endpoint`, `AccessKeyId`, `SecretAccessKey`, `ForcePathStyle`, `DefaultBucket` — validated via `AddValidatedOptions`: `Endpoint`/`AccessKeyId`/`SecretAccessKey` required | SharedKernel.Storage.Obs | `○` |
| C-24 | Implement `ObsStorageConstants` — `MaxBatchDeleteKeys = 1000` (independently declared, not shared with `S3StorageConstants`) plus any OBS-specific header/metadata key constants | SharedKernel.Storage.Obs | `○` |
| C-25 | Implement `AddSharedKernelObsStorage(IConfiguration)` — binds+validates `ObsStorageOptions`, registers an OBS-endpoint `IAmazonS3` singleton, registers `IFileStorage → ObsFileStorage` and `IBlobUriGenerator → ObsBlobUriGenerator` as singletons | SharedKernel.Storage.Obs | `○` |
| C-26 | Implement `[LoggerMessage]`-attributed logging inside `ObsFileStorage`/`ObsBlobUriGenerator` with explicit `EventId`s in the `8200-8299` sub-block (`LoggingEventIdRanges.Storage + 200`..`+299`) | SharedKernel.Storage.Obs | `○` |
| C-27 | Confirm zero project or type reference from `SharedKernel.Storage.Obs` to `SharedKernel.Storage.S3` — no shared base class, no shared constants file, no `using` of `.S3` namespaces anywhere | SharedKernel.Storage.Obs | `○` |
| C-28 | Confirm the OBS-pointed `IAmazonS3` is registered and resolves as a singleton | SharedKernel.Storage.Obs | `○` |
| C-29 | Document (carried into the Docs phase) the keyed-DI pattern (`AddKeyedSingleton`) a consuming service uses to register both `.S3` and `.Obs` side by side without the default-resolve collision already noted in `08.Storage/CLAUDE.md` | SharedKernel.Storage.Obs | `○` |
| C-30 | Confirm `SharedKernel.Storage.Obs`'s only third-party NuGet dependency is `AWSSDK.S3` (same package/version as `.S3`) — no native Huawei SDK reference anywhere | SharedKernel.Storage.Obs | `○` |

---

## Phase: Tests <!-- phase-key: SK.08.Tests -->

> Unit and provider (Testcontainers MinIO) test coverage for all packages. Provider round-trip tests (T-05–T-16) depend on `16.Testing`'s MinIO Testcontainers fixture (P-268) — see Cross-Domain Dependencies.

| ID | Task | Package(s) | State |
| --- | --- | --- | :---: |
| T-01 | `StorageErrors` factory tests: all nine members return the correct `Error` kind/code (six original + `.CopyFailed`/`.BatchDeleteFailed`/`.ConnectivityFailure`, all `Error.Failure`) | SharedKernel.Storage.Abstractions.Tests | `○` |
| T-02 | Model equality tests for all seven `Models/` records, including new `FileDeleteOutcome` (`Succeeded == true` ⇒ `Error == null` invariant, value equality by `Key`) | SharedKernel.Storage.Abstractions.Tests | `○` |
| T-03 | `PresignedUrl.ExpiresAt` is absolute — derived once, never recomputed from a relative `TimeSpan` | SharedKernel.Storage.Abstractions.Tests | `○` |
| T-04 | Reflection-based `ContractShapeTests` (mirroring `06.Persistence`'s precedent) asserting `IFileStorage.ListAsync` returns `IAsyncEnumerable<FileMetadata>` (not `Task<Result<IReadOnlyList<FileMetadata>>>`) and `CheckHealthAsync` returns `Task<Result>` (non-generic) — locks the two most novel signatures against silent regression | SharedKernel.Storage.Abstractions.Tests | `○` |
| T-05 | S3 — round-trip tests against a real Testcontainers MinIO instance (P-268): upload → download → copy → batch-delete → not-found | SharedKernel.Storage.S3.Tests | `○` |
| T-06 | S3 — streaming-list test: seed N objects under a prefix, assert `ListAsync` yields all N via `await foreach` without materializing an intermediate list; assert cancellation mid-enumeration stops the underlying paging | SharedKernel.Storage.S3.Tests | `○` |
| T-07 | S3 — connectivity-probe test: `CheckHealthAsync` succeeds against a reachable bucket; fails with `StorageErrors.ConnectivityFailure` against an unreachable/misconfigured endpoint | SharedKernel.Storage.S3.Tests | `○` |
| T-08 | S3 — presigned-URL round-trip test: generate a presigned upload URL, `PUT` directly against it (no `IAmazonS3` call), then verify the object exists; generate a presigned download URL and `GET` directly against it | SharedKernel.Storage.S3.Tests | `○` |
| T-09 | S3 — status-code → `StorageErrors` mapping tests: 404 → `.NotFound`, 403 → `.AccessDenied`, for both the original members and the new ones (Copy/DeleteMany/CheckHealth) | SharedKernel.Storage.S3.Tests | `○` |
| T-10 | S3 — DI registration tests: `IFileStorage`/`IBlobUriGenerator` resolve; `IAmazonS3` resolves as a singleton (same instance across two resolutions) | SharedKernel.Storage.S3.Tests | `○` |
| T-11 | S3 — `S3StorageOptions` validation tests: valid config registers without throw; missing `AccessKeyId`/`SecretAccessKey` fails at startup (`ValidateOnStart`); over-long presign `Expiry` returns `StorageErrors.ExpiryTooLong` | SharedKernel.Storage.S3.Tests | `○` |
| T-12 | S3 — MinIO-path confirmation: the exact same test suite (T-05–T-09) passes with `ServiceUrl`+`ForcePathStyle` configured, proving no MinIO-specific code path exists | SharedKernel.Storage.S3.Tests | `○` |
| T-13 | Obs — round-trip tests (upload → download → copy → batch-delete → not-found) against Testcontainers MinIO standing in for the OBS S3-compatible endpoint (P-268), per P-267's acceptance criteria | SharedKernel.Storage.Obs.Tests | `○` |
| T-14 | Obs — streaming-list, connectivity-probe, and presigned-URL round-trip tests mirroring T-06/T-07/T-08 one-for-one | SharedKernel.Storage.Obs.Tests | `○` |
| T-15 | Obs — `ObsStorageOptions` validation tests mirroring T-11 (`Endpoint`/`AccessKeyId`/`SecretAccessKey` required) | SharedKernel.Storage.Obs.Tests | `○` |
| T-16 | Obs — DI registration tests mirroring T-10 | SharedKernel.Storage.Obs.Tests | `○` |
| T-17 | Obs — sibling-independence regression check at this domain's own test level: no `using SharedKernel.Storage.S3` anywhere in `SharedKernel.Storage.Obs`'s own source or test project (a lightweight local check; the authoritative architecture-test enforcement is `00.Governance`'s own `StorageTopologyRules`/SK0023, P-271, outside this domain's jurisdiction) | SharedKernel.Storage.Obs.Tests | `○` |

---

## Phase: Docs <!-- phase-key: SK.08.Docs -->

> XML doc comments on all public APIs, README with usage examples.

| ID | Task | Package(s) | State |
| --- | --- | --- | :---: |
| DO-01 | XML doc comments on every public type/member across `SharedKernel.Storage.Abstractions` (`Abstractions/`, `Models/`, `Errors/`); enable XML documentation file generation with zero missing-doc warnings | SharedKernel.Storage.Abstractions | `○` |
| DO-02 | XML doc comments on every public type/member across `SharedKernel.Storage.S3`; same zero-warning bar | SharedKernel.Storage.S3 | `○` |
| DO-03 | XML doc comments on every public type/member across `SharedKernel.Storage.Obs`; same zero-warning bar | SharedKernel.Storage.Obs | `○` |
| DO-04 | README for `SharedKernel.Storage.Abstractions` — `IFileStorage`/`IBlobUriGenerator` usage, including the four new operations (copy, batch-delete, streaming list via `await foreach`, connectivity probe) and the Stream-first/caller-owned-stream contract | SharedKernel.Storage.Abstractions | `○` |
| DO-05 | README for `SharedKernel.Storage.S3` — `AddSharedKernelS3Storage()` setup, MinIO configuration note (`ServiceUrl`+`ForcePathStyle`), configuration reference for every `S3StorageOptions` property | SharedKernel.Storage.S3 | `○` |
| DO-06 | README for `SharedKernel.Storage.Obs` — `AddSharedKernelObsStorage()` setup, configuration reference for every `ObsStorageOptions` property, and the documented keyed-DI pattern (C-29) for registering both `.S3` and `.Obs` side by side in one service | SharedKernel.Storage.Obs | `○` |
| DO-07 | Confirm no drift between the shipped Core-phase code and `08.Storage/CLAUDE.md`'s Interface Contracts section once Core lands — update the brain if reality diverged from the D-07/D-15 design | — | `○` |

---

## Phase: Published <!-- phase-key: SK.08.Published -->

> NuGet packaging metadata, pack, publish, and consumer verification.

| ID | Task | Package(s) | State |
| --- | --- | --- | :---: |
| P-01 | Add full NuGet packaging metadata (`PackageId`, `Description`, `Authors`, `RepositoryUrl`, `PackageTags`, etc.) to all three `.csproj` files | SharedKernel.Storage.Abstractions, SharedKernel.Storage.S3, SharedKernel.Storage.Obs | `○` |
| P-02 | Run `dotnet pack` for all three packages producing `.nupkg`+`.snupkg` with zero warnings | SharedKernel.Storage.Abstractions, SharedKernel.Storage.S3, SharedKernel.Storage.Obs | `○` |
| P-03 | Consumer-verify harness proving `AddSharedKernelS3Storage()` resolves `IFileStorage`/`IBlobUriGenerator` with zero DI exceptions | SharedKernel.Storage.S3 | `○` |
| P-04 | Consumer-verify harness proving `AddSharedKernelObsStorage()` resolves `IFileStorage`/`IBlobUriGenerator` with zero DI exceptions | SharedKernel.Storage.Obs | `○` |
| P-05 | Consumer-verify harness proving both `AddSharedKernelS3Storage()` and `AddSharedKernelObsStorage()` can be registered side by side in one service via keyed DI without a resolution collision (validates the C-29/DO-06 design against real code) | SharedKernel.Storage.S3, SharedKernel.Storage.Obs | `○` |
| P-06 | Consumer-verify harness proving a missing/invalid `S3StorageOptions`/`ObsStorageOptions` configuration section fails at `IHost.StartAsync()` with a clear, actionable message — not a silent default or first-upload failure | SharedKernel.Storage.S3, SharedKernel.Storage.Obs | `○` |
| P-07 | Update Package Board to reflect all three packages at `Published`/`●` | SharedKernel.Storage.Abstractions, SharedKernel.Storage.S3, SharedKernel.Storage.Obs | `○` |

---

## Overall Progress

> Counts updated whenever a task state changes.

| Phase Key | Phase | Total | ● Done | ○ Pending | State |
| --- | --- | :---: | :---: | :---: | :---: |
| `SK.08.Design` | Design | 15 | 0 | 15 | `○` |
| `SK.08.Scaffold` | Scaffold | 12 | 0 | 12 | `○` |
| `SK.08.Core` | Core | 30 | 0 | 30 | `○` |
| `SK.08.Tests` | Tests | 17 | 0 | 17 | `○` |
| `SK.08.Docs` | Docs | 7 | 0 | 7 | `○` |
| `SK.08.Published` | Published | 7 | 0 | 7 | `○` |

---

## Changelog

> One line per session. Format: `[YYYY-MM-DD] {what changed} — {trigger}`.

- [2026-07-16] Sub state-map initialized — phase key registry (SK.08.Design → SK.08.Published), package board (Abstractions + S3 + Obs at `○`), cross-domain dependency rows, 6 empty phase sections awaiting arch-lead dispatch (root, user request)
- [2026-07-16] WO-043 P-265/P-266/P-267 dispatched — full six-phase plan authored for all three packages: Abstractions contract finalization (`CopyAsync`, `DeleteManyAsync`, `ListAsync` redesigned to streaming `IAsyncEnumerable<FileMetadata>`, `CheckHealthAsync` connectivity probe, new `FileDeleteOutcome` model, three new `StorageErrors` factory members), full `.S3` provider implementation, and full `.Obs` provider implementation as an independent sibling package. 15 Design + 12 Scaffold + 30 Core + 17 Tests + 7 Docs + 7 Published tasks added (88 total), all `○` Pending. Discovered pre-existing bare-placeholder `.csproj` files for Abstractions and S3 (+ its `.Tests` project) from an earlier repo-wide bootstrap pass, already registered in `Platform.SharedKernel.slnx` with zero content — Scaffold tasks reflect fleshing these out rather than creating from scratch; `SharedKernel.Storage.Obs` (+ its `.Tests` project) and `SharedKernel.Storage.Abstractions.Tests` do not exist yet and are net-new. Cross-Domain Dependencies row updated to reference `16.Testing`'s MinIO Testcontainers fixture by number (P-268); added a downstream-note (non-blocking) that this phase's finalized contract unblocks `16.Testing` P-269, `13.ServiceDefaults` P-270, and `00.Governance`'s already-designed `StorageTopologyRules`/SK0023 (P-271) (arch-lead, WO-043, P-265–P-267)
