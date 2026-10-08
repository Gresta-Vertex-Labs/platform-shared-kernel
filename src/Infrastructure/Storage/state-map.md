# 08.Storage — State Map

> Living board for this domain: what exists and what is open. Completed work is not kept here; `git log` is the record.

## Legend

○ Not started · ◐ In progress · ● Done · ⚑ Blocked · ⊘ Declined / superseded

## Package Board

| Package | Tier | Status | Notes |
| --- | --- | --- | --- |
| `SharedKernel.Storage.Abstractions` | Abstractions | ● | Named stores (`AddSharedKernelStorage().AddS3(configuration).AddStore("invoices")`, keyed `IFileStorage`, `IFileStorageFactory`), tenant stores (`ITenantFileStorage.ForTenant(TenantId)`, keys under `tenants/{id}/`), streaming, ranges, conditional writes, checksums, presigned GET/PUT/POST/multipart, `storage.*` codes, one `storage-{store}` readiness probe per store. |
| `SharedKernel.Storage.S3` | Adapter | ● | AWS S3, MinIO and any S3-compatible store; default AWS credential chain; `S3Compatibility`. |
| `SharedKernel.Storage.Obs` | Adapter | ● | Huawei OBS as configuration over S3 (declared Obs → S3 adapter edge); unsupported features refused as `storage.not_supported`. |

## Phase Key Registry

No open phase keys. Closed keys live in `git log`: check a new key is unused with `git log --oneline -S"SK.NN.Key"`.

## Open Work

None — every phase in this domain is complete. The first public release ships with root P-577 (release train).

## Blocked

None.

## Cross-Domain Dependencies

None open.
