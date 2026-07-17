---
name: project_wo043_storage_testing
description: WO-043 (P-268 MinioContainerFixture, P-269 InMemoryFileStorage/InMemoryBlobUriGenerator) status snapshot as of the 2026-07-17 Design-phase confirmation pass.
type: project
---

WO-043 adds two things to `16.Testing`: `Containers/MinioContainerFixture` (P-268, a fourth Testcontainers `IAsyncLifetime` fixture) and a new `Storage/` capability folder (P-269, `InMemoryFileStorage`/`InMemoryBlobUriGenerator`/`AddInMemoryFileStorage()`, faking `08.Storage/SharedKernel.Storage.Abstractions`'s `IFileStorage`/`IBlobUriGenerator`).

**Why this matters:** P-269 was, at the time it was originally designed, the first genuine hard compile-time cross-domain blocker this domain had ever hit (`08.Storage.Abstractions` was a real empty placeholder project) — see [[domain_16_testing_conventions]] for the general soft-vs-hard blocker distinction this dispatched.

**Status as of 2026-07-17 (Design-phase confirmation pass, `SK.16.Design` D-79–D-96 → 96/96 `●`):**
- `08.Storage`'s `SK.08.Core` is now `●` 30/30 — the blocker cleared. `IFileStorage` (9 members, not 10 — corrected miscount), `IBlobUriGenerator` (2 members), all 7 `Models/` records, and the 9-factory-method `StorageErrors` all exist as real compiled code, verified zero-drift against the already-drafted target shape in `16.Testing/CLAUDE.md`.
- `S3StorageOptions.ServiceUrl`/`.AccessKeyId`/`.SecretAccessKey`/`.ForcePathStyle`/`.DefaultBucket` and `ObsStorageOptions.Endpoint` confirmed matching `MinioContainerFixture`'s planned property names 1:1 — no renaming needed when `08.Storage`'s own test suites eventually adopt the fixture.
- `AWSSDK.S3` confirmed pinned `4.0.101.1` in `08.Storage/SharedKernel.Storage.S3.csproj` — same version `16.Testing`'s own `MinioContainerFixture` should pin (S-22, still pending).
- `C-61`/`C-62`/`C-63`/`T-47`/`DO-18` (the five previously-`⚑`-Blocked tasks) are corrected back to `○` Pending — **not implemented yet**. A future Core-phase session still needs to write `Storage/InMemoryFileStorage.cs`, `Storage/InMemoryBlobUriGenerator.cs`, and the `AddInMemoryFileStorage()` DI extension.
- Also still `○` Pending and unblocked: `S-21`–`S-24` (Scaffold: `Testcontainers.Minio` package ref, `AWSSDK.S3` package ref, `SharedKernel.Storage.Abstractions` project ref, `Storage/` folder), `C-60`/`T-46`/`DO-17` (P-268's `MinioContainerFixture` itself — was never blocked, just not yet implemented by any session).

**How to apply:** the next `16.Testing` session picking up WO-043 should start at Scaffold (S-21–S-24), then Core (C-60 `MinioContainerFixture` first, then C-61–C-63 `Storage/` types) — all fully unblocked now. `FileDeleteOutcome` (one of the 7 model records) is constructed via static factories `.Success(key)`/`.Failure(key, error)`, not an object initializer — worth knowing before writing `InMemoryFileStorage.DeleteManyAsync`.
