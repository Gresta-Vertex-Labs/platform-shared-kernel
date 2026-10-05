# 08.Storage — Domain Brain

> Provider-neutral object storage: files (documents, images, exports, attachments) in **named stores**, optionally
> **tenant-scoped**, with streaming I/O, byte ranges, conditional writes, SHA-256 checksums, listing, copies, batch
> delete and presigned client transfers (GET, PUT, POST form, multipart). Application code depends only on
> `SharedKernel.Storage.Abstractions`; the host picks S3 (AWS, MinIO, any S3-compatible service) or Huawei OBS. This
> domain does **not** own upload validation or virus scanning (clients upload through presigned URLs), lifecycle or
> archive-tier restore (bucket lifecycle rules), or a health-check type of its own (it registers `IReadinessProbe`s).

## Packages

| Package | Tier | Purpose |
| --- | --- | --- |
| `SharedKernel.Storage.Abstractions` | Abstractions | Contracts (`IFileStorage`, `ITenantFileStorage`, `IFileStorageFactory`), models (`FileReference`, `FileProperties`, `FileListItem`/`FileListPage`, `ByteRange`, `WriteCondition`, upload/download/copy/delete/presign options), `StorageErrors`/`StorageErrorCodes`/`StorageException`, `StorageValidation`, `StorageReadinessProbeNames`, and the store registry (`AddSharedKernelStorage`, `IStorageBuilder`, `FileStoreRegistration`; internal `FileStorageFactory`, `ScopedFileStorage`, `TenantFileStorage`, `FileStoreReadinessProbe`). References `SharedKernel.Primitives`, `SharedKernel.Execution` only |
| `SharedKernel.Storage.S3` | Adapter | The S3 implementation: `AddS3`, `AddS3Compatible`, `S3StorageBuilder` (`AddStore`/`AddTenantStore`), `S3StorageOptions`, `S3StoreOptions`, `S3Compatibility`; internal `S3FileStorage`, `S3Connection`, telemetry, logging. `AWSSDK.S3`, `SharedKernel.Configuration` |
| `SharedKernel.Storage.Obs` | Adapter | Huawei Cloud OBS: `AddObs`, `ObsStorageOptions` and the OBS compatibility profile over the S3 implementation — nothing else. Declares `SharedKernel.Storage.S3` in `<SharedKernelAllowedAdapterReferences>` |

All three track their public API (`PublicAPI.*.txt`, RS0016/RS0017 as errors) and require XML docs (CS1591). Public
types live in the flat namespace `SharedKernel.Storage`, except the option types (`SharedKernel.Storage.S3`,
`SharedKernel.Storage.Obs`). `consumer-verify/` is an untiered harness.

## Public Entry Points

### Abstractions

- `services.AddSharedKernelStorage()` → `IStorageBuilder`; providers extend it. A custom provider contributes a store
  with `builder.AddStore(new FileStoreRegistration(name, tenantScoped, factory, probe))`
  (`FileStoreRegistration.IsValidStoreName`).
- Consume a shared store with `[FromKeyedServices("invoices")] IFileStorage`; a tenant store with
  `[FromKeyedServices("documents")] ITenantFileStorage` then `.ForTenant(TenantId)`. Unkeyed `IFileStorage` /
  `ITenantFileStorage` resolve only when exactly one store of that kind is registered, else throw naming the stores.
- `IFileStorageFactory`: `GetStore(name)`, `GetTenantStore(name)`, `IsTenantScoped(name)`, `StoreNames`,
  `Open(FileReference)` (reopen a persisted reference, tenant included).
- `IFileStorage` verbs: `UploadAsync`/`DownloadAsync` (streams, `ByteRange`), `ExistsAsync`, `GetPropertiesAsync`,
  `CopyAsync` (same store) / `CopyToAsync` (another store), `DeleteAsync`/`DeleteManyAsync` (→ `BatchDeleteResult`),
  `ListAsync` (async stream) / `ListPageAsync`, `CreateDownloadUrlAsync`, `CreateUploadUrlAsync`,
  `CreateUploadFormAsync` (→ `PresignedPost`), and presigned multipart (`StartMultipartUploadAsync`,
  `CreateUploadPartUrlAsync`, `CompleteMultipartUploadAsync`, `AbortMultipartUploadAsync`). Every verb except
  `ListAsync` returns `Result`/`Result<T>`.
- Probe names: `StorageReadinessProbeNames.ForStore(name)` = `storage-{store}`.

### S3 (`SharedKernel.Storage.S3`)

- `builder.AddS3(configuration)` — connection `S3` (`S3StorageBuilderExtensions.S3ConnectionName`) from
  `SharedKernel:Storage:S3` (`S3StorageOptions.SectionName`).
- `builder.AddS3(configuration, connectionName)` — a named connection from `SharedKernel:Storage:S3:{name}`
  (e.g. buckets owned by different IAM users).
- `builder.AddS3Compatible(connectionName, configuration, sp => (client, compatibility))` — bring your own client.
- `.AddStore(name, o => …)` / `.AddTenantStore(name, o => …)` — each store's `S3StoreOptions` from
  `SharedKernel:Storage:Stores:{name}` then the delegate: `Bucket`, `KeyPrefix`, `Encryption`, `KmsKeyId`,
  `ExpectedBucketOwner`, `DefaultTier`, `MaxPresignExpiry`, `MultipartPartSize`.
- Connection options: `ServiceUrl`, `Region`, `ForcePathStyle`, `AccessKeyId`/`SecretAccessKey`/`SessionToken`,
  `MaxRetries`, `RequestTimeout`, `Compatibility` (`S3Compatibility`: `ConditionalWrites`, `Sha256Checksums`,
  `ObjectTags`, `KmsEncryption`, `PresignedPost`, `ETagIsContentMd5`).

### OBS (`SharedKernel.Storage.Obs`)

- `builder.AddObs(configuration)` — connection `Obs` (`ObsConnectionName`) from `SharedKernel:Storage:Obs`
  (`ObsStorageOptions.SectionName`: `Endpoint`, `Region`, credentials, `SecurityToken`, `ForcePathStyle`,
  `MaxRetries`, `RequestTimeout`); returns the same `S3StorageBuilder`, so stores are added identically.

All options are validated on start (`AddValidatedOptions`, named `S3StoreOptions` per store).

## Rules & Invariants

1. **Keys are validated before any I/O** (`StorageValidation.ValidateKey`): no empty key, leading `/`, `\`,
   empty/`.`/`..` segment, control character, or more than 1024 UTF-8 bytes (checked again on the full key by the
   provider).
2. **Tenant keys are `{store KeyPrefix}tenants/{tenantId:D}/{key}`**, applied only by `ScopedFileStorage`. A tenant
   view returns relative keys and rewrites error messages so the prefix never reaches the caller. Tenants are
   `SharedKernel.Execution.Tenancy.TenantId`; never accept a `string`/`Guid` tenant or hand-build a prefix.
3. **A tenant store is never resolvable as `IFileStorage`**, and a provider's raw store is never handed out — the
   registry checks the factory's result (`StoreName == name`, no tenant) and always wraps it.
4. **Expected failures are `Result` values built only through `StorageErrors`** with a `StorageErrorCodes` code.
   Throttling, 5xx, timeouts and network failures become `storage.unavailable` (after the SDK's own retries), never an
   exception. Only caller cancellation throws; `ListAsync` throws `StorageException` (an async stream has no
   `Result`). `storage.access_denied` is `ErrorType.Forbidden`.
5. **Error messages name the store and the caller's key — never bucket, endpoint or provider request id** (those go
   to logs). **Object keys are never logged or put on spans/metrics.**
6. **Streams are never buffered.** Upload reads the caller's stream from its position and never disposes or rewinds
   it; unknown-length uploads go multipart with at most one part in memory. Downloads return the provider stream.
7. **Upload path:** one `PutObject` when `ChecksumSha256` is given or the length is known (seekable, or
   `FileUploadOptions.ContentLength`) and at most `MultipartPartSize`; otherwise `TransferUtility` multipart. A
   supplied `ChecksumSha256` on a length-less stream without `ContentLength` fails `storage.invalid_request`.
8. **A feature the endpoint lacks fails `storage.not_supported` before the request is sent** (`S3Compatibility`):
   conditional writes/deletes/create-only presigned PUT, SHA-256 checksums, tags, SSE-KMS, presigned POST. Never
   silently degrade.
9. **Every presigned URL/form is capped by the store's `MaxPresignExpiry`** (default 1 hour, max 7 days;
   `storage.expiry_too_long`). A presigned PUT returns every header the client must send; the signature covers them.
10. **The S3 client is never registered as `IAmazonS3`.** It lives in the internal keyed `S3Connection` (one client +
    `TransferUtility` per connection name), created on first use and disposed with the host.
11. **Credentials:** no static keys → the AWS default chain (IRSA, Pod Identity, ECS, EC2). `AccessKeyId` and
    `SecretAccessKey` are both set or neither.
12. **Abstractions never references a cloud SDK, S3, OBS or `SharedKernel.Configuration`; S3 never references OBS**
    (`StorageTopologyRules` in `00.Governance`).
13. **Store names:** 1–64 of `A-Z a-z 0-9 . _ -`, unique ignoring case; one `storage-{store}` probe per store.

## Decisions

| Decision | Why |
| --- | --- |
| Named stores, not a bucket argument | Buckets are configuration; per-call buckets spread them through code and made clients collide across providers |
| Tenant isolation in Abstractions (`ScopedFileStorage`) | One implementation for every provider and the in-memory fake; providers only see validated, prefixed keys |
| `ITenantFileStorage.ForTenant(TenantId)` returns a view | Same explicitness as `ITenantCacheService` with one entry point; a view cannot be used without a tenant |
| Presigning lives on `IFileStorage` | One object per store; tenant views get presigning for free |
| OBS built on the S3 package (declared adapter edge) | The two were identical apart from configuration; OBS differences are a compatibility profile |
| Conditional copies stream through a conditional PUT | MinIO ignores `If-None-Match` on `CopyObject` and overwrites |
| SHA-256 only for known-length uploads; a supplied checksum forces a single PUT | The SDK cannot checksum unknown-length multipart parts; only a single PUT verifies a whole-object SHA-256 |
| `RequestChecksumCalculation`/`ResponseChecksumValidation = WHEN_REQUIRED` | Several S3-compatible services reject the SDK's default flexible-checksum headers |
| `ETagIsContentMd5 = false` → full downloads requested as `bytes=0-` | OBS ETags of encrypted objects are not MD5s; the only SDK switch is process-wide, which a library must not flip |
| Only `Default`/`InfrequentAccess` tiers | Archive tiers need a restore step before reads; left to lifecycle rules |
| OBS profile refuses conditions and checksums, allows tags | Measured on OBS: `If-None-Match`, `If-Match`, `x-amz-checksum-sha256` are accepted and ignored |
| No in-library upload validation | Presigned uploads bypass the service; validate on read or with bucket policy |

## Logging

EventId block **8000–8999** (`LoggingEventIdRanges.Storage`):

| Sub-block | Package | In use |
| --- | --- | --- |
| 8000–8099 | Abstractions | none |
| 8100–8199 | S3 (`S3StorageLog`) | 8100 access denied, 8101 unavailable, 8102 provider error, 8103 expected failure (Debug), 8104 probe failed, 8105 bucket not served by the configured region/endpoint |
| 8200–8299 | OBS | none (logs through S3) |

Telemetry (S3 package): `ActivitySource`/`Meter` `"SharedKernel.Storage"`; spans `storage {operation}` (Client) with
`storage.store`, `storage.operation`, `storage.provider`, `error.type` (the storage code) on failure; histogram
`storage.client.operation.duration` (s); counter `storage.client.bytes` (`storage.direction`). Wired by
`WithStorageTelemetry()` in `SharedKernel.ServiceDefaults`.

## Cross-Domain Couplings

- **01.Core:** `Result`/`Error` (Primitives), `TenantId` (Execution), `AddValidatedOptions` (Configuration, S3 only),
  `IReadinessProbe` (Primitives.Health) — one per store.
- **13.ServiceDefaults:** `AddSharedKernelReadiness()` maps the `storage-{store}` probes; `WithStorageTelemetry()`.
- **15.Integration:** `Notifications.Abstractions` references Storage.Abstractions — attachments are storage
  references.
- **20.Reporting:** `Reporting.Abstractions` delivers exports to a named store (`ReportDestination`) with a presigned
  link.
- **14.Presentation:** no dependency; endpoints hand out presigned URLs instead of accepting uploads.
- **16.Testing:** `SharedKernel.Storage.Testing` implements the abstractions in memory.
- Verified end to end by `samples/DocumentsApi`; `samples/OrderApi.Infrastructure` also wires a store.

## Testing

- `SharedKernel.Storage.Abstractions.Tests` — **Unit** lane: validation, registry resolution, tenant isolation
  against a recording fake store.
- `SharedKernel.Storage.S3.Tests`, `SharedKernel.Storage.Obs.Tests` — **Integration** lane: real MinIO
  (Testcontainers) for every behaviour — round trips, non-seekable multipart, ranges, conditions, checksums, batch
  delete, copies across stores and tenants, listing, presigned GET/PUT/POST/multipart through `HttpClient`, probe,
  outage → `unavailable`, cancellation, telemetry. Never mock `IAmazonS3` for behaviour.
- Consumers: `src/Testing/SharedKernel.Storage.Testing` (`AddInMemoryStore`/`AddInMemoryTenantStore`, namespace
  `SharedKernel.Testing.Storage`). The storage test projects do not reference it (keeps the graph acyclic).
- `consumer-verify/` composes S3 and OBS stores in a real host and checks start-up validation.
- `samples/DocumentsApi/DocumentsApi.Tests` runs every capability over HTTP against MinIO, and against real Amazon S3
  and Huawei OBS when the `SK_LIVE_*` variables are set (see its README). Run it after any provider change — MinIO
  accepts behaviour the real services reject.

Documentation lives in four places, kept in sync with the code: `src/Infrastructure/Storage/README.md` (relative links), each package
`README.md` (packed; absolute GitHub links; ends with an AI quick reference), XML docs on every public member, and the
csproj `<Description>`. Never document a provider behaviour no test or live run has shown.

## Known Limitations

- MinIO does not report a stored SHA-256 (`FileProperties.ChecksumSha256` is `null` there); verifying a supplied
  checksum works.
- SSE-S3 is exercised live; SSE-KMS only by its headers.
- Server-side copy is limited to 5 GiB (S3 `CopyObject`).
- `ExistsAsync`/`GetPropertiesAsync` use `HEAD`, whose 404 has no error code: a missing bucket reads as a missing
  object there. Other operations detect `NoSuchBucket` and return `storage.provider_error`; the readiness probe
  reports a missing bucket at startup.
