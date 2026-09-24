# DocumentsApi

A small file service built on the `08.Storage` packages, and the end-to-end proof that they work against real
Amazon S3 and Huawei Cloud OBS — not only against MinIO.

| Store | Kind | Connection | What it shows |
| --- | --- | --- | --- |
| `assets` | shared | S3 `Public` (its own IAM user) | Uploads, downloads, links, forms |
| `documents` | tenant-scoped | S3 `Private` (another IAM user), SSE-S3 | Tenant isolation, 15-minute link limit |
| `archive` | shared | Huawei Cloud OBS | The OBS compatibility profile, cross-provider copies |

```csharp
IStorageBuilder storage = builder.Services.AddSharedKernelStorage();
storage.AddS3(builder.Configuration, "Public").AddStore("assets");
storage.AddS3(builder.Configuration, "Private").AddTenantStore("documents");
storage.AddObs(builder.Configuration).AddStore("archive");

builder.Services.AddHealthChecks()
    .AddStorageReadinessCheck("assets", "storage-assets")
    .AddStorageReadinessCheck("documents", "storage-documents")
    .AddStorageReadinessCheck("archive", "storage-archive");
```

Buckets, key prefixes, encryption and link limits are in `appsettings.json`; credentials never are.

## Endpoints

| Endpoint | Storage call |
| --- | --- |
| `PUT /files/{store}/{**key}` | `UploadAsync` — the request body streamed straight in, up to 1 GiB; `If-None-Match: *` = create only, `If-Match` = replace that version, `X-Checksum-Sha256` verified by the provider, `X-Meta-*` stored as metadata |
| `GET /files/{store}/{**key}` | `DownloadAsync` — streamed back; a `Range` header returns 206; `If-Match` pins the version |
| `GET /properties/{store}/{**key}` | `GetPropertiesAsync` |
| `DELETE /files/{store}/{**key}` | `DeleteAsync`; `If-Match` asks for a conditional delete (MinIO ignores it, see below) |
| `POST /delete-many/{store}` | `DeleteManyAsync` |
| `GET /list/{store}?prefix=&recursive=&pageSize=&continuationToken=` | `ListPageAsync` |
| `POST /copy` | `CopyToAsync` — within a store, across stores and across providers |
| `POST /links/{store}/download` · `/upload` · `/form` | `CreateDownloadUrlAsync` · `CreateUploadUrlAsync` · `CreateUploadFormAsync` |
| `POST /multipart/{store}/start` · `/part-url` · `/complete` · `/abort` | Presigned multipart upload |
| `GET /health/ready` | One readiness check per store |

The tenant comes from an `X-Tenant-Id` header so the tests can act as several tenants. A real service takes it from
the authenticated principal, never from a header the caller controls.

## The HTTP boundary

`builder.AddSharedKernelWebApi()` and `app.UseSharedKernelWebApi()` make the API's errors one shape. Every endpoint
resolves the store, binds the storage call to it and maps the `Result` with one call — no `IsSuccess` branch:

```csharp
app.MapGet("/properties/{store}/{**key}", (string store, string key, HttpContext http, IFileStorageFactory factory, CancellationToken ct) =>
    Stores.Resolve(factory, store, http)                 // documents.unknown_store (404), documents.tenant_required (400)
        .Bind(files => files.GetPropertiesAsync(key, ct))
        .ToOk());
```

So an unknown store, a missing tenant and every `storage.*` failure reach the client as the same RFC 9457
`application/problem+json` body with its `errorCode`: `storage.not_found` 404, `storage.checksum_mismatch` 400,
`storage.unavailable` 503 (throttling or an outage), `storage.not_supported` 500 (OBS refusing a conditional write).
The two conflicts, `storage.already_exists` and `storage.precondition_failed`, are 412 or 409 depending on the request
(below).

Request bodies are capped at 4 MiB platform-wide (`SharedKernel:Presentation:WebApi:Limits:MaxRequestBodySize`).
The upload endpoint lifts the cap for itself with `.WithRequestSizeLimit(FileEndpoints.MaxUploadBytes)` (1 GiB):
its body streams into the store, so the limit bounds the object, not memory. Anything larger goes straight to the
provider through the presigned multipart endpoints. Kestrel enforces the limits, which the in-memory test server
does not run, so `FileScenarios.Only_the_upload_endpoint_lifts_the_request_body_limit` pins the override by its
endpoint metadata.

## Preconditions: 412 or 409

`storage.already_exists` and `storage.precondition_failed` are conflicts. When the client sent the condition in a
request header, the failure is exactly what 412 Precondition Failed means — the precondition it sent is false — so that
is the answer; without such a header the same error is an ordinary 409. The endpoints never choose between the two:
the status comes from the error and the request.

| Request | Error | Answer |
| --- | --- | --- |
| `PUT` with `If-None-Match: *`, and the file exists | `storage.already_exists` | 412 |
| `PUT` or `GET` with the `If-Match` of an older version | `storage.precondition_failed` | 412 |
| `POST /copy` with `"createOnly": true`, and the destination exists (the condition is in the body) | `storage.already_exists` | 409 |
| an `If-Match` that is not one strong entity tag: an ETag without its quotes, `*`, a list | `precondition.invalid` | 400 |

The endpoints read `If-Match` with the presentation package's `GetIfMatchTags()` and refuse a header that does not name
exactly one strong entity tag rather than ignore it: ignoring it would turn the client's conditional write into an
unconditional one. A store that cannot honor a precondition refuses it too: OBS answers `storage.not_supported`. One
gap is not the sample's to close: **MinIO ignores `If-Match` on deletes** (`RELEASE.2025-09-07`, the image the tests
use), so a delete pinned to an older version removes the current file. The tests pin conditional reads and writes only.

## Run the tests

```bash
dotnet pack Platform.SharedKernel.slnx -c Release -o ./nupkgs -p:MinVerVersionOverride=1.0.0-local.1
dotnet test samples/DocumentsApi/DocumentsApi.Tests -p:SharedKernelPackageVersion=1.0.0-local.1
```

Without further setup the scenarios run against MinIO (Docker, via Testcontainers); MinIO also stands in for OBS
with the OBS compatibility profile. To run every scenario against the real clouds as well, set:

| Variable | Example |
| --- | --- |
| `SK_LIVE_S3_REGION` | `eu-central-1` |
| `SK_LIVE_S3_PUBLIC_BUCKET` / `_ACCESS_KEY` / `_SECRET_KEY` | the `assets` bucket and its IAM user |
| `SK_LIVE_S3_PRIVATE_BUCKET` / `_ACCESS_KEY` / `_SECRET_KEY` | the `documents` bucket and its IAM user |
| `SK_LIVE_OBS_ENDPOINT` | `https://obs.tr-west-1.myhuaweicloud.com` |
| `SK_LIVE_OBS_BUCKET` / `_ACCESS_KEY` / `_SECRET_KEY` | an OBS bucket and AK/SK |

Every run writes only under `sharedkernel-samples/{run id}/` in each bucket and deletes everything it wrote, so the
tests can share buckets that hold other data. The IAM users need `s3:GetObject`, `s3:PutObject`, `s3:DeleteObject`,
`s3:ListBucket` and `s3:AbortMultipartUpload` on their bucket.

To run the service itself, supply credentials the usual .NET way, e.g.
`SharedKernel__Storage__S3__Public__AccessKeyId`, and set real bucket names in `appsettings.json`.

## What the live run established (2026-09-22)

100 scenarios passed: 50 against MinIO, 50 against AWS S3 (`eu-central-1`, two IAM users) and OBS (`tr-west-1`).
Running against the real services found three package defects the MinIO suites could not, all fixed:

| Found | Fix |
| --- | --- |
| A SHA-256-verified upload of an ASP.NET request body (a stream of unknown length) failed with an opaque provider error | `FileUploadOptions.ContentLength`: pass `Request.ContentLength`; without it such a request fails with `storage.invalid_request` naming the fix |
| Every full download of an SSE-encrypted object from OBS failed the SDK's MD5 check (OBS's ETag is not the content MD5 then) | `S3Compatibility.ETagIsContentMd5 = false` for OBS: full downloads are requested as `bytes=0-` |
| Two S3 buckets with different IAM users could not be configured | Named S3 connections: `AddS3(configuration, "Private")` reads `SharedKernel:Storage:S3:Private` |

And three facts about the services themselves:

- **OBS silently ignores `If-None-Match`, `If-Match` and `x-amz-checksum-sha256`** — it overwrites and stores
  unchecked bytes. The OBS profile therefore refuses them with `storage.not_supported`; tags do work on OBS.
- **OBS rejects form uploads whose file part uses `filename*=`** (what .NET's `MultipartFormDataContent.Add(content,
  name, fileName)` sends). Browsers send a plain `filename`, which works; see `PresignedScenarios.PostFormAsync`.
- **The "public" bucket serves every new object anonymously** (an unsigned GET returned 200), so nothing private may
  be written to it.
