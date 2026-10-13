# 08.Storage — Domain Brain

> Provider-neutral object storage: files in **named stores**, optionally **tenant-scoped**, with streaming I/O, byte
> ranges, conditional writes, SHA-256 checksums, listing, copies, batch delete and presigned client transfers (GET,
> PUT, POST form, multipart). Application code depends only on `SharedKernel.Storage.Abstractions`; the host picks S3
> (AWS, MinIO, any S3-compatible service) or Huawei OBS. This domain does **not** own upload validation or virus
> scanning (clients upload through presigned URLs), lifecycle or archive-tier restore (bucket lifecycle rules), or a
> health-check type of its own (it registers `IReadinessProbe`s).

## Packages

| Package | Tier | Purpose |
| --- | --- | --- |
| `SharedKernel.Storage.Abstractions` | Abstractions | Contracts (`IFileStorage`, `ITenantFileStorage`, `IFileStorageFactory`), models, `StorageErrors`/`StorageErrorCodes`/`StorageException`, `StorageValidation`, `StorageTelemetryNames`/`StorageTagKeys` (the provider-neutral telemetry names), `StorageReadinessProbeNames`, and the store registry (`AddSharedKernelStorage`, `IStorageBuilder`, `FileStoreRegistration`; internal `FileStorageFactory`, `ScopedFileStorage`, `TenantFileStorage`, `FileStoreReadinessProbe`). References `SharedKernel.Primitives`, `SharedKernel.Execution` and `SharedKernel.Core` (for `SharedKernelException`) only |
| `SharedKernel.Storage.S3` | Adapter | The S3 implementation: `AddS3`, `AddS3Compatible`, `S3StorageBuilder`, `S3StorageOptions`, `S3StoreOptions`, `S3Compatibility`; internal `S3FileStorage`, `S3Connection`, `Diagnostics/` (`StorageTelemetry`, `S3StorageLog`, `S3StorageLoggingEventIds`). `AWSSDK.S3`, `Microsoft.Extensions.Diagnostics`, `SharedKernel.Configuration` |
| `SharedKernel.Storage.Obs` | Adapter | Huawei Cloud OBS: `AddObs`, `ObsStorageOptions` and the OBS compatibility profile over the S3 implementation — nothing else. Declared edge → `SharedKernel.Storage.S3` |

All three track their public API (`PublicAPI.*.txt`, RS0016/RS0017 as errors). Public types live in the flat namespace
`SharedKernel.Storage`, except the option types (`SharedKernel.Storage.S3`, `SharedKernel.Storage.Obs`).
`consumer-verify/` is an untiered harness.

## Public Entry Points

API detail, option keys and defaults: the `SharedKernel.Storage.Abstractions`, `SharedKernel.Storage.S3` and
`SharedKernel.Storage.Obs` READMEs (`SharedKernel.Storage.*/README.md`).

- `services.AddSharedKernelStorage()` → `IStorageBuilder`; providers extend it. A custom provider contributes a store
  with `builder.AddStore(new FileStoreRegistration(name, tenantScoped, factory, probe))`.
- Consume a shared store with `[FromKeyedServices("invoices")] IFileStorage`; a tenant store with
  `[FromKeyedServices("documents")] ITenantFileStorage` then `.ForTenant(TenantId)`. Unkeyed `IFileStorage` /
  `ITenantFileStorage` resolve only when exactly one store of that kind is registered, else throw naming the stores.
- `IFileStorageFactory` — resolve stores by name; `Open(FileReference)` reopens a persisted reference, tenant included.
- `IFileStorage` — every verb returns `Result`/`Result<T>` except `ListAsync` (async stream).
- S3: `builder.AddS3(configuration[, connectionName])`, `builder.AddS3Compatible(connectionName, configuration, sp => (client, compatibility))`,
  then `.AddStore(name, o => …)` / `.AddTenantStore(name, o => …)` (`S3StoreOptions` from `SharedKernel:Storage:Stores:{name}`).
- OBS: `builder.AddObs(configuration)` returns the same `S3StorageBuilder`; stores are added identically.
- Probe names: `StorageReadinessProbeNames.ForStore(name)` = `storage-{store}`.

## Rules & Invariants

1. **Keys are validated before any I/O** (`StorageValidation.ValidateKey`): no empty key, leading `/`, `\`,
   empty/`.`/`..` segment, control character, or more than 1024 UTF-8 bytes (checked again on the full key by the
   provider).
2. **Tenant keys are `{store KeyPrefix}tenants/{tenantId:D}/{key}`**, applied only by `ScopedFileStorage`. A tenant
   view returns relative keys and rewrites error messages so the prefix never reaches the caller. Never hand-build a
   tenant prefix.
3. **A tenant store is never resolvable as `IFileStorage`**, and a provider's raw store is never handed out — the
   registry checks the factory's result (`StoreName == name`, no tenant) and always wraps it.
4. **Expected failures are `Result` values built only through `StorageErrors`** with a `StorageErrorCodes` code.
   Throttling, 5xx, timeouts and network failures become `storage.unavailable` (after the SDK's own retries), never an
   exception. Only caller cancellation throws; `ListAsync` throws `StorageException`. `storage.access_denied` is
   `ErrorType.Forbidden`.
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
    (`StorageTopologyRules`).
13. **Store names:** 1–64 of `A-Z a-z 0-9 . _ -`, unique ignoring case (`FileStoreRegistration.IsValidStoreName`);
    one `storage-{store}` probe per store.
14. Never document a provider behaviour that no test or live run has shown.
15. **`StorageException` is a sealed `SharedKernelException`** (ctors `(Error)` and
    `(Error, Exception)`), so every edge maps it by `Error.Type` instead of answering 500. No endpoint, host, port or
    object key reaches an `Error`, an exception message, a log record or a span, and no bucket reaches an `Error` or an
    exception message (log records name the bucket, rule 5; Obs start-up validation messages never quote the endpoint).
    A tenant view rewrites the error of a `StorageException` raised during enumeration, keeping the original as
    `InnerException`, so `tenants/{id}/` never leaves it. Abstractions references `SharedKernel.Core` as well as
    Primitives and Execution.
16. **Telemetry and logs follow root "Observability, exceptions and error codes".**
    The source and meter name and the tag keys are public constants in Abstractions (`StorageTelemetryNames`,
    `StorageTagKeys`); instruments are `sharedkernel.storage.*` with units; failures are recorded through the shared
    Primitives recorder (`error.code` = the `storage.*` code, `error.type` = the `ErrorType` name), with an `outcome` tag
    from `WellKnownOutcomes`; the meter comes from `IMeterFactory`, the `ActivitySource` is static; every operation,
    including a request refused before any I/O, records one span and one duration, and an unexpected exception or the
    caller's cancellation is recorded and rethrown unchanged. Log templates are one sentence with quoted identifiers and
    the exception last; EventIds come from `S3StorageLoggingEventIds`. Exception messages (E3) are sentences that end in a
    period, quote values and name the remedy.

## Decisions

| Decision | Why |
| --- | --- |
| Named stores, not a bucket argument | Buckets are configuration; per-call buckets spread them through code |
| Tenant isolation in Abstractions (`ScopedFileStorage`) | One implementation for every provider and the in-memory fake; providers only see validated, prefixed keys |
| `ITenantFileStorage.ForTenant(TenantId)` returns a view | Same explicitness as `ITenantCacheService`; a view cannot be used without a tenant |
| Presigning lives on `IFileStorage` | One object per store; tenant views get presigning for free |
| OBS built on the S3 package (declared adapter edge) | The two are identical apart from configuration; OBS differences are a compatibility profile |
| Conditional copies stream through a conditional PUT | MinIO ignores `If-None-Match` on `CopyObject` and overwrites |
| SHA-256 only for known-length uploads; a supplied checksum forces a single PUT | The SDK cannot checksum unknown-length multipart parts; only a single PUT verifies a whole-object SHA-256 |
| `RequestChecksumCalculation`/`ResponseChecksumValidation = WHEN_REQUIRED` | Several S3-compatible services reject the SDK's default flexible-checksum headers |
| `ETagIsContentMd5 = false` → full downloads requested as `bytes=0-` | OBS ETags of encrypted objects are not MD5s; the only SDK switch is process-wide, which a library must not flip |
| Only `Default`/`InfrequentAccess` tiers | Archive tiers need a restore step before reads; left to lifecycle rules |
| OBS profile refuses conditions and checksums, allows tags | OBS accepts and ignores `If-None-Match`, `If-Match`, `x-amz-checksum-sha256` |
| No in-library upload validation | Presigned uploads bypass the service; validate on read or with bucket policy |
| `StorageErrors` stays a public factory | Providers and the in-memory double live in other assemblies; root C2 asks for an internal factory (recorded deviation) |
| Timeouts stay under `storage.unavailable` | After the SDK's retries a timeout is retry-later; root C3 asks for `Timeout` (recorded deviation; a `storage.timeout` code is an addendum if arch-lead rules so) |
| Caller-driven outcomes (`not_found`, `already_exists`, `precondition_failed`, `invalid_range`, `checksum_mismatch`) log at Debug | Warning would log every missing-object read; root L6 says Warning (recorded deviation, open for arch-lead: one attribute and one test to switch) |
| `NotSupported` stays `ErrorType.Unexpected` | A caller choosing a feature the configured store lacks is a usage defect, not a retryable condition |
| The telemetry singleton is registered by the S3 package (`AddS3Compatible`), not by `AddSharedKernelStorage()` | `AddMetrics()` lives in `Microsoft.Extensions.Diagnostics`, which an Abstractions package may not reference (SKTIER003); only the provider creates instruments, and a second provider registers its own |
| `StorageValidation` and the in-memory double return `Error?` (null = valid) and carry an in-source `SK0004` suppression | The helpers are public API chained with `??` and `is Error`; an `Error.None` sentinel would break every consumer (recorded deviation) |
| `WrongRegion` (8105) carries the exception | An Error-level record without one breaks root L4; both call sites have the redirect exception |

## Logging

EventId block **8000–8999** (`LoggingEventIdRanges.Storage`):

| Sub-block | Package | In use |
| --- | --- | --- |
| 8000–8099 | Abstractions | none |
| 8100–8199 | S3 (`S3StorageLog`) | 8100 access denied, 8101 unavailable, 8102 provider error, 8103 expected failure (Debug), 8104 probe failed, 8105 bucket not served by the configured region/endpoint |
| 8200–8299 | OBS | none (logs through S3) |

Templates are one sentence with quoted identifiers (`Store '{Store}' on bucket '{Bucket}' ...`), the exception parameter is last, and the standard names `{Operation}`, `{StatusCode}`, `{ErrorCode}` are used; the bucket and the provider request id are logged, never the key, endpoint or tenant. Levels: 8100 Warning, 8101 Warning, 8102 Error, 8103 Debug (caller-driven outcomes, see Decisions), 8104 Warning, 8105 Error. The EventIds are the constants of `S3StorageLoggingEventIds` (class in `Diagnostics/`, with `S3StorageLog`); OBS 8200-8299 are reserved and nothing is declared (it logs through S3); Abstractions 8000-8099 is unused. The logger category is `ILogger<S3FileStorage>`.

Telemetry: `ActivitySource` and `Meter` `StorageTelemetryNames.SourceName` = `MeterName` = `"SharedKernel.Storage"`, version from `TelemetryVersion.Of`, the meter created through `IMeterFactory` (the S3 package calls `AddMetrics()`); `AddServiceDefaults()` subscribes by the `SharedKernel.*` wildcard (there is no `WithStorageTelemetry()`). Spans `storage {operation}` (Client) tagged `storage.store`, `storage.operation`, `storage.provider` (the connection name); a `Result` failure sets status Error, `error.code` (the `storage.*` code) and `error.type` (the `ErrorType` name); an unexpected exception adds the full exception event and is rethrown; the caller's cancellation records no error. Instruments: histogram `sharedkernel.storage.operation.duration` (`s`; tags store, operation, provider, `outcome`, plus `error.type` and `error.code` on failures) and counter `sharedkernel.storage.bytes` (`By`; tags store, provider, direction). Outcomes come from `WellKnownOutcomes`.

## Cross-Domain Couplings

- **01.Core:** `Result`/`Error`, `TenantId`, `AddValidatedOptions` (S3 only), `IReadinessProbe` — one per store.
- **13.ServiceDefaults:** `AddSharedKernelReadiness()` maps the `storage-{store}` probes; `AddServiceDefaults()` subscribes to `SharedKernel.Storage` by wildcard.
- **15.Integration:** `Notifications.Abstractions` and `Notifications.Email.SendGrid` reference Storage.Abstractions —
  attachments are storage references.
- **20.Reporting:** `Reporting.Abstractions` delivers exports to a named store (`ReportDestination`) with a presigned link.
- **14.Presentation:** no dependency; endpoints hand out presigned URLs instead of accepting uploads.
  A thrown `StorageException` is a `SharedKernelException`, so the HTTP, gRPC and SignalR
  mappers answer by `Error.Type`; their status assertions belong to 14.
- Verified end to end by the Shop (`samples/Shop`): Catalog (presigned S3 uploads) and Reports (an S3 store and an OBS archive, presigned downloads).

## Testing

- **Unit** lane: `SharedKernel.Storage.Abstractions.Tests` — validation, registry resolution, tenant isolation against
  a recording fake store.
- **Integration** lane: `SharedKernel.Storage.S3.Tests`, `SharedKernel.Storage.Obs.Tests` — real MinIO through the
  suite's own Testcontainers fixture (`SharedKernel.Storage.S3.Tests/Infrastructure/MinioFixture.cs`) for every
  behaviour, including presigned transfers through `HttpClient`, outage → `unavailable`, cancellation and telemetry.
  Never mock `IAmazonS3` for behaviour.
  Telemetry and log assertions use `SharedKernel.Testing` (`ActivityRecorder`, `MetricRecorder`, `InMemoryLoggerFactory`);
  the S3 and OBS test projects reference it (not `SharedKernel.Storage.Testing`). The one `IAmazonS3` stand-in is a
  throwing proxy that drives the unexpected-exception path, which no real endpoint produces.
- Fakes: `SharedKernel.Storage.Testing` — catalogue in `src/Testing/CLAUDE.md`. The storage test projects do not
  reference it (keeps the graph acyclic).
- `consumer-verify/` composes S3 and OBS stores in a real host and checks start-up validation.
- The Shop's `Shop.E2E` (`CatalogFlowTests`, `ReportsFlowTests`; `samples/Shop/build.sh --e2e`) runs S3 and OBS over
  HTTP against MinIO. Run it after any provider change. No harness runs against real Amazon S3 or Huawei OBS any
  more, and MinIO accepts behaviour the real services reject — check provider-visible changes live by hand.

## Known Limitations

- MinIO does not report a stored SHA-256 (`FileProperties.ChecksumSha256` is `null` there); verifying a supplied
  checksum works.
- SSE-S3 is exercised live; SSE-KMS only by its headers.
- Server-side copy is limited to 5 GiB (S3 `CopyObject`).
- `ExistsAsync`/`GetPropertiesAsync` use `HEAD`, whose 404 has no error code: a missing bucket reads as a missing
  object there. Other operations return `storage.provider_error` on `NoSuchBucket`; the readiness probe reports a
  missing bucket at startup.
