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

| Phase key | Phase | Status |
| --- | --- | --- |

Closed keys live in `git log`: check a new key is unused with `git log --oneline -S"SK.NN.Key"`.

## Open Work

None — every phase in this domain is complete.

## Blocked

None.

## Cross-Domain Dependencies

- **Inbound, 01.Core:** `SK.01.ObservabilityHelpers` (recorder, `TelemetryVersion`, outcomes, `IsInSubBlock`), `SK.01.CoreObservabilityFixes` D-01 (Execution references Core; this domain adds the same reference to Abstractions).
- **Inbound, 16.Testing:** `SK.16.TelemetryCapture` (`ActivityRecorder`, `MetricRecorder`, `LoggerAssertions` conventions).
- **Inbound, 13.ServiceDefaults:** `SK.13.WildcardTelemetry` deletes `WithStorageTelemetry()`; call site `samples/Shop/Catalog/Shop.Catalog.Api/Program.cs` and the ServiceDefaults README mention it (theirs to change in the same pull request). Dashboards on `storage.client.*` must be repointed to `sharedkernel.storage.*`.
- **Outbound, 14.Presentation:** HTTP, gRPC and SignalR status assertions for a thrown `StorageException` per `ErrorType` (`Unavailable` 503/`UNAVAILABLE`, `Forbidden` 403/`PERMISSION_DENIED`, `Validation` 400, `Conflict` 409, `Unexpected` 500) belong to the mapper tests, since Storage test projects reference no host package; the `Error` inputs are pinned here (SK.08.StorageExceptionEdge T-02).
- **Outbound, 16.Testing:** `SharedKernel.Storage.Testing` (`InMemoryFileStorage`) throws the new base type with no API change; no telemetry in the double. Catalogue note only.
- **Outbound, 20.Reporting / 15.Integration:** no code change; they receive `Result` values, and a caller that catches `StorageException` around `ListAsync` now also matches `SharedKernelException`.
- **Outbound, 00.Governance:** the P-583 report-only tests must show zero findings for 08.Storage before P-618 turns them into errors.
