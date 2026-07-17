---
name: project_wo043_storage_testing
description: WO-043 (P-268 MinioContainerFixture, P-269 InMemoryFileStorage/InMemoryBlobUriGenerator) status — Core phase implemented 2026-07-17.
type: project
---

WO-043 adds two things to `16.Testing`: `Containers/MinioContainerFixture` (P-268, a fourth Testcontainers `IAsyncLifetime` fixture) and a new `Storage/` capability folder (P-269, `InMemoryFileStorage`/`InMemoryBlobUriGenerator`/`AddInMemoryFileStorage()`, faking `08.Storage/SharedKernel.Storage.Abstractions`'s `IFileStorage`/`IBlobUriGenerator`).

**Status as of 2026-07-17 (Core-phase implementation session): `SK.16.Core` is now 63/63 `●`, closed.**
- `Containers/MinioContainerFixture.cs` implemented: `Testcontainers.Minio` 4.1.0, image pinned to `minio/minio:RELEASE.2024-01-16T16-07-38Z`. API confirmed via a throwaway reflection probe (`MinioBuilder.WithUsername/.WithPassword().Build()` → `MinioContainer.GetAccessKey()`/`GetSecretKey()`/`GetConnectionString()`) — no official docs page covers this, had to reflect the actual DLL.
- `Storage/InMemoryFileStorage.cs`, `Storage/InMemoryBlobUriGenerator.cs`, `Storage/StorageServiceCollectionExtensions.cs` (`AddInMemoryFileStorage()`) implemented against the live `08.Storage/SharedKernel.Storage.Abstractions` source — zero drift from the already-locked `16.Testing/CLAUDE.md` target shape.
- `DeleteAsync`'s `SimulateFailure` branch uses `StorageErrors.AccessDenied(bucket, key)`, not `UploadFailed` — `StorageErrors` has no dedicated single-key delete-failure factory; `AccessDenied` mirrors the real `S3FileStorage`/`ObsFileStorage` providers' own only `DeleteAsync` failure path (confirmed by reading `08.Storage/SharedKernel.Storage.S3/FileStorage/S3FileStorage.cs`).
- `FileDeleteOutcome` is constructed via static factories `.Success(key)`/`.Failure(key, error)` (private constructor) — confirmed again, matches prior note.
- Remaining WO-043 work: `SK.16.Tests` (T-46/T-47 — prove both in `SharedKernel.Testing.SelfTests`, no consuming domain has adopted either fake/fixture yet) and `SK.16.Docs` (DO-17/DO-18).

See [[feedback_greendonut_result_ambiguity]] for a build-breaking gotcha hit while implementing this phase.
