# 08.Storage — Object Storage Brain

## What This Domain Is

Provider-neutral object storage for SharedKernel services: files (documents, images, exports, attachments) in
**named stores**, optionally **tenant-scoped**, with streaming I/O, conditional writes, integrity checks, listing,
copies and presigned client transfers. Application code depends only on `SharedKernel.Storage.Abstractions`; the
host picks a provider.

Philosophy: **named stores, not buckets. Tenant isolation by construction. Stream-first. Every rule validated
before I/O, every expected failure a `Result`.**

Entry points: `README.md` (overview), each package `README.md` (usage), this file (maintainer rules). Released with
the repo-wide release train.

---

## Packages

| Package | Role | References |
| --- | --- | --- |
| `SharedKernel.Storage.Abstractions` (Abstractions tier) | Contracts (`IFileStorage`, `ITenantFileStorage`, `IFileStorageFactory`), models, `StorageErrors`/`StorageErrorCodes`/`StorageException`, `StorageValidation`, `StorageReadinessProbeNames`, and the store registry (`AddSharedKernelStorage`, `IStorageBuilder`, `FileStoreRegistration`, internal `FileStorageFactory`/`ScopedFileStorage`/`TenantFileStorage`/`FileStoreReadinessProbe`) | `SharedKernel.Primitives`, `SharedKernel.Execution`, `Microsoft.Extensions.DependencyInjection.Abstractions` |
| `SharedKernel.Storage.S3` (Adapter tier) | The S3 implementation (AWS, MinIO, any S3-compatible service): `AddS3(configuration)`, `AddS3(configuration, connectionName)`, `AddS3Compatible`, `S3StorageBuilder` (`AddStore`/`AddTenantStore`), `S3StorageOptions`, `S3StoreOptions`, `S3Encryption`, `S3Compatibility`; internal `S3FileStorage`, `S3Connection`, telemetry, logging | Abstractions, `SharedKernel.Configuration`, `AWSSDK.S3`, `Microsoft.Extensions.Logging.Abstractions` |
| `SharedKernel.Storage.Obs` (Adapter tier) | Huawei Cloud OBS: `AddObs`, `ObsStorageOptions`, the OBS compatibility profile — nothing else | `SharedKernel.Storage.S3` |

All three track their public API (`PublicAPI.*.txt`, RS0016/RS0017 as errors) and require XML docs (CS1591). Every
public type lives in the flat namespace `SharedKernel.Storage`, except the S3/OBS option types
(`SharedKernel.Storage.S3`, `SharedKernel.Storage.Obs`).

**Tiers:** Abstractions references Foundation packages only (`Primitives`, `Execution`). `SharedKernel.Storage.Obs →
SharedKernel.Storage.S3` is the one adapter→adapter edge, declared in Obs's `<SharedKernelAllowedAdapterReferences>`
(SKTIER002 otherwise), and it is deliberate: the two implementations were ~500 lines of identical code maintained
twice. `S3` must never reference `Obs`; Abstractions never references a cloud SDK, S3, OBS or
`SharedKernel.Configuration` (`StorageTopologyRules`).

---

## How It Fits Together

```text
app code ──> IFileStorage / ITenantFileStorage.ForTenant(TenantId) (keyed singletons, one per store name)
                 │
                 ▼
          ScopedFileStorage  (Abstractions, internal)   validates keys/options, applies "tenants/{id}/",
                 │                                        strips it from results and error messages
                 ▼
          S3FileStorage      (S3, internal)             bucket + store KeyPrefix, compatibility checks,
                 │                                        SDK calls, error mapping, telemetry, logging
                 ▼
          S3Connection       (keyed by connection name)  one IAmazonS3 + TransferUtility per connection
```

- A provider contributes a store with `IStorageBuilder.AddStore(new FileStoreRegistration(name, tenantScoped,
  factory, probe))`. The factory returns the provider's **raw** store (`StoreName == name`, `TenantId == null`, whole
  store); the registry checks that and wraps it. The raw store is never handed out, so tenant stores cannot be used
  without a tenant and every copy between handed-out stores can be unwrapped to raw stores for a server-side copy.
- `AddStore` also registers one `IReadinessProbe` per store, named `storage-{store}`
  (`StorageReadinessProbeNames.ForStore`), which runs the registration's `probe` delegate (S3: `HEAD` on the bucket).
  The host maps every probe with `services.AddHealthChecks().AddSharedKernelReadiness()` (ServiceDefaults base);
  this domain ships no `IHealthCheck` and no probe interface of its own.
- `IFileStorage` unkeyed resolves only when exactly one shared store is registered (same for `ITenantFileStorage`);
  otherwise it throws naming the stores. Store names: 1–64 of `A-Z a-z 0-9 . _ -`, unique ignoring case.
- Configuration: connection at `SharedKernel:Storage:S3` (connection `S3`), `SharedKernel:Storage:S3:{name}` (named
  connection, `AddS3(configuration, name)`) or `SharedKernel:Storage:Obs`; each store at
  `SharedKernel:Storage:Stores:{name}` (named `S3StoreOptions`), then the `configure` delegate. All validated on start.
- Upload path: one `PutObject` when a `ChecksumSha256` is given or the length is known (seekable stream or
  `FileUploadOptions.ContentLength`) and at most `MultipartPartSize`; otherwise `TransferUtility` multipart.

---

## Contract Rules (do not break)

1. **Keys are validated before any I/O** by `StorageValidation.ValidateKey`: no empty key, leading `/`, `\`, empty/`.`/`..`
   segment, control character, or more than 1024 UTF-8 bytes (checked again by the provider on the full key). Tenants
   are `SharedKernel.Execution.Tenancy.TenantId` values (a non-empty GUID); `ForTenant(default)` throws.
2. **Tenant keys are `{store prefix}tenants/{tenantId:D}/{key}`.** A tenant view returns relative keys and rewrites
   error messages so the prefix never reaches a caller. A tenant store is never resolvable as `IFileStorage`.
3. **Expected failures are `Result` values with a `StorageErrorCodes` code** — built only through `StorageErrors`.
   Throttling, 5xx, timeouts and network failures are `storage.unavailable` (after the SDK's own retries), never an
   exception. Only caller cancellation throws; `ListAsync` throws `StorageException` (an async stream has no `Result`).
   `access_denied` is `ErrorType.Forbidden`.
4. **Error messages name the store and key the caller passed — never the bucket, endpoint or provider request id.**
   Those go to logs. **Object keys are never logged or put on spans/metrics** (they carry user data).
5. **Streams are never buffered.** Upload reads the caller's stream from its position, never disposes or rewinds it;
   unknown-length streams go multipart with at most one part in memory. Downloads return the provider's stream.
6. **A feature the endpoint lacks fails with `storage.not_supported` before the request is sent** (`S3Compatibility`),
   never silently degraded. This covers conditional writes/deletes/create-only presigned PUTs, SHA-256 checksums,
   object tags, SSE-KMS and presigned POST. `ETagIsContentMd5 = false` changes behaviour instead of refusing: full
   downloads are requested as `bytes=0-` (a 416 answer means an empty object).
7. **Every presigned URL/form is capped by the store's `MaxPresignExpiry`** (default 1 hour, max 7 days). A presigned
   `PUT` returns every header the client must send (content type, metadata, SSE, `If-None-Match`, checksum, storage
   class); the signature covers them. Browser uploads that need a size limit use the presigned POST form.
8. **The S3 client is never registered as `IAmazonS3`.** It lives in the internal keyed `S3Connection`, created on
   first use and disposed with the host, so it cannot collide with a service's own client.
9. **Credentials:** no static keys → the AWS default chain (IRSA, Pod Identity, ECS, EC2). `AccessKeyId`/`SecretAccessKey`
   are both set or neither.
10. **A caller-supplied `ChecksumSha256` on a stream that cannot report its length needs `ContentLength`**; without it
   the upload fails with `storage.invalid_request` naming the fix, never with an opaque provider error.

---

## Decisions and Why

| Decision | Why |
| --- | --- |
| Named stores instead of a bucket argument | Bucket names are configuration; per-call buckets spread them through code and made the unkeyed `IAmazonS3` collide when S3 and OBS were both registered (the old OBS client silently served S3 stores) |
| Tenant isolation in Abstractions (`ScopedFileStorage`), not per provider | One implementation for every provider and the in-memory fake; providers only see validated, prefixed keys |
| `ITenantFileStorage.ForTenant(TenantId)` returning a view, not a tenant parameter on 17 members | Same explicitness as `ITenantCacheService` with one entry point; a view cannot be used without a tenant |
| Presigned members on `IFileStorage`, `IBlobUriGenerator` removed | One object per store; tenant views cover presigning for free |
| OBS over the S3 implementation (sibling rule reversed) | The two packages were identical except for configuration; OBS differences are a compatibility profile |
| Conditional copies stream through a conditional PUT | MinIO (and possibly others) ignore `If-None-Match` on `CopyObject` and overwrite — verified against MinIO 2025-09 |
| SHA-256 requested only for known-length uploads | The SDK cannot attach part checksums to an unknown-length multipart upload (`checksum missing` from MinIO) |
| A caller-supplied `ChecksumSha256` forces a single `PutObject` | Only a single PUT verifies a whole-object SHA-256; multipart checksums are checksums of parts |
| `RequestChecksumCalculation`/`ResponseChecksumValidation = WHEN_REQUIRED` | Several S3-compatible services reject the SDK's default flexible-checksum headers |
| Only `Default`/`InfrequentAccess` tiers | Archive tiers need a restore step before reads; left to lifecycle rules |
| `FileUploadOptions.ContentLength` | An ASP.NET Core request body cannot report its length; the SDK's single `PutObject` (the only way to verify a whole-object SHA-256) then failed with "Could not determine content length" — found by the live run |
| `S3Compatibility.ETagIsContentMd5` + `bytes=0-` full downloads | OBS ETags of encrypted objects are not content MD5s and the SDK's legacy MD5 check failed every full download; the only SDK switch is process-wide (`AWSConfigsS3.DisableDefaultChecksumValidation`), which a library must not flip |
| Named S3 connections | Buckets owned by different IAM users could not be configured together — found by the live run |
| OBS profile: conditions and checksums refused, tags on | Measured on OBS `tr-west-1`: `If-None-Match`, `If-Match` and `x-amz-checksum-sha256` are accepted and ignored (overwrite, unchecked bytes); tagging works |
| `DefaultBucket`, `CheckHealthAsync(bucket)`, `FileMetadata` with empty content type removed | Dead setting; health belongs to the per-store `IReadinessProbe`; listings honestly have no content type (`FileListItem`) |

---

## Logging and Telemetry

- `[LoggerMessage]` only. EventIds: Abstractions 8000–8099 (none yet), S3 8100–8199 (`S3StorageLog`: 8100 access
  denied, 8101 unavailable, 8102 provider error, 8103 expected failure at Debug, 8104 probe failed, 8105 bucket not served by the configured region/endpoint), OBS 8200–8299
  (none; it logs through S3).
- `ActivitySource`/`Meter` `"SharedKernel.Storage"` (S3 package): spans `storage {operation}` (kind Client) with
  `storage.store`, `storage.operation`, `storage.provider` and `error.type` (the storage code) on failure; histogram
  `storage.client.operation.duration` (s); counter `storage.client.bytes` (`storage.direction` upload/download).
  Wired by `WithStorageTelemetry()` in `SharedKernel.ServiceDefaults`.

---

## Test Rules

- `SharedKernel.Storage.Abstractions.Tests` — validation, registry resolution and tenant isolation against a recording
  fake store (no Docker).
- `SharedKernel.Storage.S3.Tests` / `.Obs.Tests` — real MinIO (`quay.io/minio/minio:RELEASE.2025-09-07T16-13-09Z`,
  Testcontainers) for every behaviour: round trips, non-seekable multipart upload, ranges, conditions, checksums, batch
  delete, copies across stores and tenants, listing and paging, presigned GET/PUT/POST/multipart through `HttpClient`,
  tenant isolation, probe, outage → `unavailable`, cancellation, telemetry. Never mock `IAmazonS3` for behaviour.
- The in-memory store for consuming services' tests is `16.Testing/SharedKernel.Storage.Testing`
  (`AddInMemoryStore`/`AddInMemoryTenantStore`, namespace `SharedKernel.Testing.Storage`). The storage test projects
  do not reference it (it references Abstractions; keeping them apart keeps the build graph acyclic).
- `consumer-verify/` composes S3 and OBS stores in a real host and checks start-up validation.

## Known Limits

- MinIO does not report a stored SHA-256 (`FileProperties.ChecksumSha256` is `null` there); verification of a supplied
  checksum is tested and works.
- SSE-S3 is exercised live (S3 `documents` and OBS `archive` stores of `samples/DocumentsApi`); SSE-KMS only by its
  headers.
- The OBS profile was verified against OBS `tr-west-1`: it ignores `If-None-Match`/`If-Match`/`x-amz-checksum-sha256`
  (so they are refused), supports tags, and returns non-MD5 ETags for encrypted objects (`ETagIsContentMd5 = false`).
- Server-side copy is limited to 5 GiB by S3 `CopyObject`.
- `ExistsAsync`/`GetPropertiesAsync` use `HEAD`, whose 404 carries no error code: a missing bucket reads as a missing
  object there. Every other operation checks `NoSuchBucket` (`IsMissingObject`) and returns `storage.provider_error`;
  the readiness probe reports the bucket at startup.

## Documentation Rules

Four audiences, four artefacts — keep them in sync in the same change as the code:

| Artefact | Reader | Rules |
| --- | --- | --- |
| `08.Storage/README.md` | GitHub visitors choosing the domain | Explains each package, the architecture, a 10-minute path, the provider matrix, guarantees. Relative links (not packed) |
| `SharedKernel.Storage.*/README.md` | GitHub and nuget.org readers (packed into the `.nupkg`) | Caching-style gold standard: pitch, contents, install, quick start, how it works, numbered recipes, reference tables (types, options, error codes, exceptions), pitfalls, design decisions, **AI quick reference**, guarantees. **Absolute** GitHub links only — relative links break on nuget.org. Every claim verified by a test or the live run |
| XML docs on every public member | IntelliSense, "Go to definition", AI tools reading the package | Purpose, defaults, limits, the `storage.*` code or exception of each failure, a `<code>` example on entry points. CS1591 is an error |
| `<Description>` in each `.csproj` | nuget.org search, IDE package manager | One paragraph: what it is, what it guarantees, the registration call |

The AI quick reference blocks are rules, one per line; update them whenever a registration, option or error code
changes. Never document a provider behaviour that no test or live run has shown.

## Live Verification

`samples/DocumentsApi/DocumentsApi.Tests` runs every capability through an HTTP API against MinIO, and against real
Amazon S3 (two IAM users, two buckets) and Huawei Cloud OBS when `SK_LIVE_*` is set (see that README). Run it after
any provider change: MinIO accepted three defects that only the real services exposed (P-559 live pass, recorded in `state-map.md`).

---

## History

Design history (P-559 redesign and live pass, WO-086 foundation refactor) is in `state-map.md`.
