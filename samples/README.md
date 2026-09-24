# samples

Runnable services built on the SharedKernel packages.

Unlike the per-domain `consumer-verify` harnesses — which prove a single package's
dependency graph resolves — a sample is a working service that composes several domains the
way a real microservice would.

| Sample | Domains exercised | External infrastructure |
|---|---|---|
| [`OrderApi`](OrderApi/) | `01.Core`, `03.Domain`, `05.Application`, `13.ServiceDefaults`, `14.Presentation` (+ the OpenAPI add-on) | none |
| [`BillingApi`](BillingApi/) | `06.Persistence` (all six packages + `SharedKernel.Persistence.Testing`), `01.Core`, `03.Domain`, `05.Application`, `12.Security`, `13.ServiceDefaults`, `14.Presentation` | PostgreSQL (Docker Compose, or Testcontainers in its tests) |
| [`DocumentsApi`](DocumentsApi/) | `08.Storage` (all three packages, two S3 connections + OBS, a tenant store), `05.Application`, `13.ServiceDefaults`, `14.Presentation` | MinIO (Testcontainers); optionally real Amazon S3 and Huawei Cloud OBS (`SK_LIVE_*`) |
| [`CatalogApi`](CatalogApi/) | `09.Search` (all three packages — both engines side by side against different document types, the neutral contracts plus each engine's exclusive ones), `05.Application`, `13.ServiceDefaults` (+`.Search`), `14.Presentation` | Meilisearch and Elasticsearch (Docker, see its README) |
| [`ShippingApi`](ShippingApi/) | `07.Messaging` (both packages — publish, send, delayed delivery, idempotency, inbound caller identity, retry, fault consumer, readiness), `04.Contracts`, `05.Application`, `13.ServiceDefaults`, `14.Presentation` | RabbitMQ (Testcontainers, `masstransit/rabbitmq` for the delayed-exchange plugin); Docker Compose for running it by hand |

## The HTTP boundary, the same way in every sample

Every sample takes the same path from HTTP to its dependencies:

```csharp
builder.Services.AddSharedKernelApplication(typeof(Program).Assembly);   // MediatR, handlers, validators, behaviors
builder.AddSharedKernelWebApi();
// …
app.UseSharedKernelWebApi();
app.MapEndpoints();                                                      // every IEndpointModule of the assembly

public sealed class OrderEndpoints : IEndpointModule
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/orders/{id:guid}", (Guid id, ISender sender, CancellationToken ct) =>
            sender.Send(new GetOrderQuery(id), ct).ToOk());
}
```

Endpoints live in endpoint modules (`IEndpointModule`; the generator in the WebApi package writes `MapEndpoints()` at
compile time). An endpoint translates the request into a command or query, sends it through MediatR's `ISender`, and
maps the `Result` with one typed-result call — `ToOk`, `ToCreated`, `ToAccepted`, `ToNoContent`, `ToOkWithETag`,
`ToHttpResult` — never branching on `IsSuccess` or choosing a status code for a failure. It never calls a repository,
store, search engine or bus itself: the handler does, next to its command or query in a `Features/` folder.
Permissions are declared once, on the command or query (`[RequirePermission]`), and enforced by the pipeline on every
path. CatalogApi's two corpus walks are stream queries (`ISender.CreateStream`), streamed as they are read. Every error,
returned or thrown, is an RFC 9457 `application/problem+json` body carrying `errorCode`, `traceId` and `correlationId`.
The one exception is CatalogApi's deployment reports (`/ops/provision`, `/ops/indexes`, `/ops/verify`), which list an
outcome per index in their own body; `/ops/verify` answers 503 with that report when an index is not ready.
A conflict that shows a precondition the request sent in a header (`If-Match`, `If-None-Match`) to be false — a stale
version, a file that already exists — is 412; every other conflict is 409.

Beyond that, each sample shows what its domain needs from the boundary:

| Sample | Shows |
|---|---|
| `OrderApi` | A versioned API with OpenAPI documents and a Scalar reference (`AddSharedKernelOpenApi`, Development only) |
| `BillingApi` | Optimistic concurrency with opaque versions (`ToOkWithETag`, an `IfMatch<EntityVersion>` parameter: 304, 428, 400, 412); `Paging`/`CursorPaging` parameters; `[RequirePermission]` on every command and query, enforced by the pipeline (401/403 problems) |
| `DocumentsApi` | Lifting the 4 MiB request-body limit for one streaming endpoint (`WithRequestSizeLimit`); storage preconditions from `If-None-Match`/`If-Match` as 412, the same conflict without a header as 409 |
| `CatalogApi` | An engine outage as 503 and a timeout as 504, with internal detail shown only in Development |
| `ShippingApi` | 202 Accepted with a `Location` for asynchronous work (`ToAccepted`); a broker outage as 503 |

## Why these use PackageReference

Every sample resolves `SharedKernel.*` from the local NuGet feed (`nupkgs/`) via
`PackageReference`, never `ProjectReference`. The point is to prove the **packed** packages
work for a consumer who has only the published artifacts — a `ProjectReference` would bypass
exactly the thing under test.

That has two consequences, both deliberate:

1. Samples are **excluded from `Platform.SharedKernel.slnx`**. They cannot build until the
   packages they consume have been packed, so a solution-wide build would fail on a clean
   checkout.
2. CI builds and runs them in the `packaging-verify` job, after `dotnet pack` — the same
   pattern the `consumer-verify` harnesses use. `OrderApi` is smoke-tested over HTTP;
   `BillingApi`, `DocumentsApi` and `ShippingApi` run their end-to-end test suites.
   `CatalogApi` needs two search engines and has no test suite: run it by hand (its README).

To run one locally, pack first and pass the packed version (see `CatalogApi`'s README for why
the exact version matters):

```bash
dotnet pack Platform.SharedKernel.slnx -c Release
dotnet run --project samples/OrderApi -p:SharedKernelPackageVersion=<the packed version> -- --environment Development
```
