# 08.Storage — State Map

> Living board for this domain: what exists, what is open. Completed phase detail is archived outside the repository; `git log` records every change.

## Legend

○ Not started · ◐ In progress · ● Done · ⚑ Blocked · ⊘ Declined / superseded

## Package Board

| Package | Tier | Status | Notes |
| --- | --- | --- | --- |
| `SharedKernel.Storage.Abstractions` | Abstractions | ● | Named stores (`AddSharedKernelStorage().AddS3(configuration).AddStore("invoices")`, keyed `IFileStorage`, `IFileStorageFactory`), tenant stores (`ITenantFileStorage.ForTenant(TenantId)`, keys under `tenants/{id}/`), streaming, ranges, conditional writes, checksums, presigned GET/PUT/POST/multipart, `storage.*` codes, one `storage-{store}` readiness probe per store. |
| `SharedKernel.Storage.S3` | Adapter | ● | AWS S3, MinIO and any S3-compatible store; default AWS credential chain; `S3Compatibility`. |
| `SharedKernel.Storage.Obs` | Adapter | ● | Huawei OBS as configuration over S3 (declared Obs → S3 adapter edge); unsupported features refused as `storage.not_supported`. |

## Phase Key Registry

| Phase key | Phase | Status |
| --- | --- | --- |
| `SK.08.Design` | Design | ● |
| `SK.08.Scaffold` | Scaffold | ● |
| `SK.08.Core` | Core | ● |
| `SK.08.Tests` | Tests | ● |
| `SK.08.Docs` | Docs | ● |
| `SK.08.Published` | Published | ● |

## Open Work

None — every phase in this domain is complete. The first public release ships with root P-577 (release train).

## Blocked

None.

## Cross-Domain Dependencies

None open.

## Completed Phases

- WO-086 ● Foundation refactor — tenants are `TenantId` (prefix `tenants/{guid:D}/`); `IFileStorageHealthProbe` replaced by per-store `IReadinessProbe`s; in-memory store moved to `SharedKernel.Storage.Testing`; tiers declared (P-565, P-569, P-571, P-575) (2026-09-26)
- P-559 ● Pre-publish redesign of all three packages — named and tenant-scoped stores, conditional writes, checksums, presigning, OBS over S3; verified live through `samples/DocumentsApi` (100 scenarios, MinIO and AWS S3) (2026-09-22)
- P-265–P-267 ● WO-043 domain build — abstractions finalization, S3 and OBS providers (with 16.Testing P-268/P-269 and 00.Governance P-271) (2026-07-18)
- Earlier phases (WO-055 follow-ups, SK.08.* build tasks) — archived.

## Changelog

- [2026-09-28] State map slimmed to a living board; completed phase detail archived outside the repository — public-release cleanup
- [2026-09-26] WO-086 foundation refactor (P-565, P-569, P-571, docs P-575) — `TenantId` tenants, `storage-{store}` readiness probes, `SharedKernel.ServiceDefaults.Storage` deleted, in-memory store in `SharedKernel.Storage.Testing`, tiers declared
- [2026-09-22] P-559 documentation pass — domain README rewritten as the GitHub landing page
- [2026-09-22] P-559 live verification through `samples/DocumentsApi` (100 scenarios: 50 MinIO, 50 AWS S3)
- [2026-09-22] P-559 pre-publish redesign of all three packages — named and tenant-scoped stores, conditional writes, OBS reduced to configuration over S3; 109 tests (66 unit, 43 MinIO)
