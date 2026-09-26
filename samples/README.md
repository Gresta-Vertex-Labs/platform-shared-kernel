# samples — how to consume the kernel

Runnable services built only from the **packed** SharedKernel packages. Together they are the reference
for how a service on the kernel is shaped: which projects it has, which package goes in which project,
how the version is pinned, in what order `Program.cs` composes things, and how a request travels from HTTP
to a handler. Each one also proves, in CI, that a family of packages works end to end for a consumer that
has nothing but the published artifacts.

| Sample | What it is the reference for | External infrastructure |
|---|---|---|
| [`OrderApi`](OrderApi/) | **The four-project shape**, with an architecture test that enforces it; the application pipeline, validation, a use case protected by `[RequirePermission]`, `Result` → ProblemDetails, a versioned API with OpenAPI documents, readiness | none |
| [`BillingApi`](BillingApi/) | The whole persistence stack: EF Core + Dapper in one transaction, row-level security, field encryption, the audit ledger, transactions and auditing in the pipeline, `[RequirePermission]` on every use case, ETags and paging | PostgreSQL (Docker Compose, or Testcontainers in its tests) |
| [`ShippingApi`](ShippingApi/) | Messaging: publish/send over RabbitMQ, delayed delivery, consumer idempotency, retries and faults, the caller's tenant, actor and correlation id carried to the consumer | RabbitMQ (Testcontainers, `masstransit/rabbitmq` for the delayed-exchange plugin); Docker Compose for running it by hand |
| [`DocumentsApi`](DocumentsApi/) | Object storage: named and tenant stores on two S3 connections plus OBS, presigned links and forms, multipart; reporting: CSV/Excel/PDF exports picked at runtime and HTML → PDF, streamed into a store | MinIO and Gotenberg (Testcontainers); optionally real Amazon S3 and Huawei Cloud OBS (`SK_LIVE_*`) |
| [`CatalogApi`](CatalogApi/) | Search: both engines side by side against different document types, the neutral contracts plus each engine's exclusive ones, stream queries | Meilisearch and Elasticsearch (Docker, see its README) |

## The shape of a service

A service is four projects plus its tests. Each project references only the kernel **tiers** its layer
may see — the same tier matrix the kernel enforces on itself (`eng/SharedKernelTiers.targets`):

```
{Service}.Domain          Foundation, Model           entities, value objects, ids, domain events, rules
{Service}.Application     + Abstractions              commands, queries, handlers, validators, the ports they need
{Service}.Infrastructure  + Adapter                   implementations of those ports, on the kernel's adapters
{Service}.Api (or Worker) + Host                      Program.cs: composition root, middleware, endpoints/consumers
{Service}.Tests           + Testing                   test projects only
```

```
Api ──► Application ──► Domain
 │           ▲
 └──► Infrastructure
```

Api → Application is correct and expected: the Api sends commands. Infrastructure references the
Application because it implements the Application's ports. Nothing references the Api.

### Which package goes in which project

| Project | Kernel packages (examples) | Never |
|---|---|---|
| **Domain** | `SharedKernel.Domain` — which brings `Primitives`, `Core` and `Execution` (`TenantId`, `IClock`, `Result`, `Error`) | anything else of the kernel, ASP.NET Core, a logger |
| **Application** | `SharedKernel.Application` (`ICommand`/`IQuery`, handlers, `ISender`, `[RequirePermission]`, the pipeline markers); `SharedKernel.Idempotency.Abstractions` / `SharedKernel.Caching.Abstractions` when it uses the idempotency or caching markers; other `*.Abstractions` it needs (`Persistence.Abstractions` for `IRepository<,>`, `Messaging.Abstractions` for `IEventPublisher`, `Storage.Abstractions`, `Search.Abstractions`, …); `Execution` for `IRequestContext`/`IUnitOfWork` | `Application.Pipeline`, `Application.Mediator.MediatR`, MediatR itself, any adapter, any host package |
| **Infrastructure** | adapters: `Persistence.EfCore` (+ `.Auditing`, `.Encryption`), `Persistence.Dapper`, `Messaging.MassTransit` + a transport satellite (`.RabbitMq` / `.AzureServiceBus`) and `.EfCore` for the outbox, `Storage.S3` / `.Obs`, `Search.Meilisearch` / `.ElasticSearch`, `Caching.FusionCache` / `.Redis`, `Idempotency.Redis` / `.EfCore`, `Communication.Rest` / `.Grpc`, `Validation.FluentValidation` | host packages |
| **Api / Worker** | host: `ServiceDefaults`, `ServiceDefaults.Security` (request context), `ServiceDefaults.Persistence`, `MultiTenancy`, `Security.Oidc` (or `.ApiKey`, `.Mtls`), `Application.Pipeline` (+ `.Caching`), `Application.Mediator.MediatR`, `Presentation.WebApi` (brings `Presentation.Core`) / `.OpenApi` / `.Grpc` / `.SignalR` / `.GraphQL` | business logic |
| **Tests** | `SharedKernel.Testing` (fakes: `FakeClock`, `TestRequestContext`, fakers) and the per-capability `*.Testing` package of what the test touches — `Application.Testing` (pipeline harness), `Persistence.Testing`, `Messaging.Testing`, `Storage.Testing`, `Idempotency.Testing`, … | being referenced by a production project |

`samples/OrderApi` is this shape exactly, and `OrderApi.Tests/ArchitectureTests.cs` asserts it against
the real restore graph. Copy that test into a new service and fill in its project names.

The other samples are single-project hosts on purpose: each is about one family of packages, and the
split would add projects without adding anything that `OrderApi` does not already show. They use the
same use-case layout — endpoints in `IEndpointModule`s, each command or query with its handler in a
`Features/` folder.

## Pinning the version

A service names **one** kernel version, and every `SharedKernel.*` package points at it (the release
train publishes every package at every version, so the set can never be mixed):

```xml
<!-- Directory.Packages.props (the consuming service) -->
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <!-- The one SharedKernel release this service builds against. Upgrade = change this line. -->
    <SharedKernelVersion>1.0.0</SharedKernelVersion>
  </PropertyGroup>
  <ItemGroup>
    <PackageVersion Include="SharedKernel.Domain" Version="$(SharedKernelVersion)" />
    <PackageVersion Include="SharedKernel.Application" Version="$(SharedKernelVersion)" />
    <PackageVersion Include="SharedKernel.Validation.FluentValidation" Version="$(SharedKernelVersion)" />
    <PackageVersion Include="SharedKernel.ServiceDefaults" Version="$(SharedKernelVersion)" />
    <!-- ...one line per SharedKernel package the service references, always $(SharedKernelVersion). -->
    <PackageVersion Include="SharedKernel.Application.Testing" Version="$(SharedKernelVersion)" />
  </ItemGroup>
</Project>
```

Map `SharedKernel.*` to the kernel's feed with `packageSourceMapping`, so no other source can supply a
package of that name. The full `NuGet.Config`, with the GitHub Packages credentials read from the
environment, is in [`PLATFORM.md`](../PLATFORM.md) → "Consuming the kernel". Never pin one
`SharedKernel.*` package to a different version from the rest, and never float the version.

**Inside this repository** the samples do the same thing with the repository's own files: the root
`Directory.Packages.props` points every `SharedKernel.*` package at one property,
`$(SharedKernelPackageVersion)`, and the root `NuGet.Config` maps `SharedKernel.*` to the local `nupkgs/`
feed that `dotnet pack` writes. The property floats (`*-*`) for convenience; pass the exact packed version,
as CI does, because a stale local build can outrank the one you just packed.

## Composing `Program.cs`

Registrations, in this order (each sample shows the parts it uses):

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();                          // telemetry, health endpoints, the startup gate
builder.WithMessagingTelemetry();                      // ...and each With*Telemetry the service needs

builder.Services.AddSingleton<IClock, SystemClock>();  // the only time source (SK0001)

// Who is calling: an authentication package produces IUserContext, and AddSharedKernelRequestContext() turns it
// into the one IRequestContext every behavior, adapter and outbound call reads — and that [RequirePermission] is
// checked against.
builder.Services.AddOidcAuthentication(builder.Configuration);
builder.Services.AddSharedKernelRequestContext();

// The service's own adapters. Each registers its own readiness probe.
builder.Services.AddOrderInfrastructure();             // e.g. AddSharedKernelPostgres<T>(...), AddSharedKernelStorage()...

// The application layer in ONE call: the handlers of the assembly, the mediator behind the kernel's ISender, the
// always-on behaviors (tracing, logging, metrics, authorization, validation) and the opt-ins, each needing its seam.
// Seams are checked when the host starts, so registration order does not matter.
builder.Services.AddSharedKernelApplication(typeof(PlaceOrderCommand).Assembly, app => app
    .UseMediatR()
    .WithTransactions()                                // IUnitOfWork
    .WithAuditing());                                  // IAuditTrailWriter
builder.Services.AddFluentValidationRequestValidators(typeof(PlaceOrderCommand).Assembly);

// Messaging after the request context: WithInboundRequestContext() must be the last IRequestContext.
builder.Services.AddSharedKernelMessaging(builder.Configuration).UseRabbitMq(...)
    .WithInboundRequestContext().WithAmbientCorrelationPropagation().Build();

builder.Services.AddHealthChecks().AddSharedKernelReadiness();   // every IReadinessProbe, one call

builder.AddSharedKernelWebApi();                       // the HTTP boundary (SharedKernel:Presentation:WebApi)
builder.AddSharedKernelOpenApi(o => o.Title = "Orders API");   // optional: versioning, OpenAPI, Scalar
```

The middleware order is fixed — the canonical pipeline:

```csharp
var app = builder.Build();

app.UseSharedKernelRequestContext();   // FIRST: X-Correlation-Id, inbound baggage refused, the request's context scope
app.UseSharedKernelWebApi(p => p       // security headers, exception handler, problem bodies, routing, CORS,
    .BeforeAuthorization(a => a.UseMiddleware<TenantResolutionMiddleware>()));   // authentication, (tenant), authorization

app.MapDefaultHealthCheckEndpoints();  // /health/live, /health/ready
app.MapEndpoints();                    // every IEndpointModule of the assembly (generated at compile time)
app.MapSharedKernelOpenApi();          // Development only, unless ExposeInProduction

app.Services.GetRequiredService<StartupGate>().MarkReady();   // after startup work; readiness is 503 until then
await app.RunAsync();
```

`UseSharedKernelRequestContext()` goes before everything on purpose: its scope wraps the WebApi pipeline's exception
handler, so the correlation id is on every log line and every response, errors included. It reads the caller lazily,
the first time something asks, which is after the authentication `UseSharedKernelWebApi()` adds. The tenant-resolution
hook (`SharedKernel.MultiTenancy`) is only for services that use it. A service without authentication registers
`AnonymousUserContext.Instance` as its `IUserContext` (OrderApi, DocumentsApi, CatalogApi).

## The HTTP boundary, the same way in every sample

```csharp
public sealed class OrderEndpoints : IEndpointModule
{
    public static void Map(IEndpointRouteBuilder app) =>
        app.MapGet("/orders/{id:guid}", (Guid id, ISender sender, CancellationToken ct) =>
            sender.Send(new GetOrderQuery(id), ct).ToOk());
}
```

Endpoints live in endpoint modules (`IEndpointModule`; the generator in the WebApi package writes `MapEndpoints()` at
compile time). An endpoint translates the request into a command or query, sends it through the kernel's `ISender`
(MediatR is only the adapter behind it), and maps the `Result` with one typed-result call — `ToOk`, `ToCreated`,
`ToAccepted`, `ToNoContent`, `ToOkWithETag`, `ToHttpResult` — never branching on `IsSuccess` or choosing a status code
for a failure. It never calls a repository, store, search engine or bus itself: the handler does, next to its command
or query in a `Features/` folder. Permissions are declared once, on the command or query (`[RequirePermission]`), and
enforced by the pipeline on every path — HTTP, a message consumer, a job — as a 401 problem for an anonymous caller and a
403 for a caller without the permission. CatalogApi's two corpus walks are stream queries (`ISender.CreateStream`),
streamed as they are read. Every error, returned or thrown, is an RFC 9457 `application/problem+json` body carrying
`errorCode`, `traceId` and `correlationId`. The one exception is CatalogApi's deployment reports (`/ops/provision`,
`/ops/indexes`, `/ops/verify`), which list an outcome per index in their own body; `/ops/verify` answers 503 with that
report when an index is not ready. A conflict that shows a precondition the request sent in a header (`If-Match`,
`If-None-Match`) to be false — a stale version, a file that already exists — is 412; every other conflict is 409.

Beyond that, each sample shows what its domain needs from the boundary:

| Sample | Shows |
|---|---|
| `OrderApi` | A versioned API with OpenAPI documents and a Scalar reference (`AddSharedKernelOpenApi`, Development only); a `[RequirePermission]` use case proven 401/403/204 over HTTP |
| `BillingApi` | Optimistic concurrency with opaque versions (`ToOkWithETag`, an `IfMatch<EntityVersion>` parameter: 304, 428, 400, 412); `Paging`/`CursorPaging` parameters; `[RequirePermission]` on every command and query, enforced by the pipeline (401/403 problems) |
| `DocumentsApi` | Lifting the 4 MiB request-body limit for one streaming endpoint (`WithRequestSizeLimit`); storage preconditions from `If-None-Match`/`If-Match` as 412, the same conflict without a header as 409 |
| `CatalogApi` | An engine outage as 503 and a timeout as 504, with internal detail shown only in Development |
| `ShippingApi` | 202 Accepted with a `Location` for asynchronous work (`ToAccepted`); a broker outage as 503 |

## A short tour

**[OrderApi](OrderApi/)** — no infrastructure, so the composition is all there is to read. Four projects;
`AddOrderInfrastructure()` registers the adapters; one `AddSharedKernelApplication(..., app => app.UseMediatR())` call
discovers the handlers; a FluentValidation validator runs through the kernel's `IRequestValidator<T>` port; the order
store's readiness probe is mapped by `AddSharedKernelReadiness()`; `CancelOrderCommand` declares
`[RequirePermission("orders.cancel")]`. Tests: the architecture test, the application layer through the real
pipeline with `SharedKernel.Application.Testing`, `FakeClock` and `TestRequestContext`, and HTTP through
`WebApplicationFactory` with a test authentication scheme.

**[BillingApi](BillingApi/)** — multi-tenant billing on PostgreSQL with the production role split. One
`AddSharedKernelPostgres<BillingDbContext>` registration with multi-tenancy (row-level security), field
encryption and the audit ledger; Dapper joining the same unit of work; the pipeline with transactions and
auditing; a development-only header authentication scheme that produces a real `IUserContext`. Tests run over
HTTP against Testcontainers PostgreSQL provisioned by `SharedKernel.Persistence.Testing`, plus a handler test
over its fakes.

**[ShippingApi](ShippingApi/)** — `AddSharedKernelMessaging(configuration).UseRabbitMq(...)` in one chain
(`UseRabbitMq` from the `SharedKernel.Messaging.MassTransit.RabbitMq` satellite): CloudEvents publish and
point-to-point send, broker-side delayed delivery, at-most-once consumption over an `IIdempotencyStore`, retries
then a visible fault, and the publisher's tenant, actor and correlation id rebuilt on the consumer so it reads
`IRequestContext` exactly as an HTTP handler does. Tests go through a real RabbitMQ broker.

**[DocumentsApi](DocumentsApi/)** — three named stores on three connections (two S3 IAM users and OBS),
one of them tenant-scoped (`ITenantFileStorage.ForTenant(TenantId)`); streaming up- and download, ranges,
conditional writes, copies across providers, presigned links, forms and multipart; one readiness probe per
store. Tests run against MinIO, and against real S3 and OBS when credentials are supplied.

**[CatalogApi](CatalogApi/)** — Meilisearch for the storefront and Elasticsearch for the back office in
one host, each against its own document type; tenant scoping on every read (tenants are `TenantId` GUIDs
in the route), qualified counts, index-level synonyms and stop words, drift verification, and the
engine-exclusive contracts that turn a provider swap into build errors. CI smoke-tests it against both
engines.

## Building and running them

Samples resolve `SharedKernel.*` by `PackageReference`, never `ProjectReference`: the point is to prove the
packed packages work for a consumer who has only the published artifacts, and a project reference would
bypass exactly the thing under test. So:

1. Samples are **excluded from `Platform.SharedKernel.slnx`**. They cannot build until the packages they
   consume have been packed.
2. CI builds and runs them in the packaging-verify job of `.github/workflows/verify.yml`, after
   `dotnet pack`. It finds them rather than listing them — every tracked `.csproj` that is not in the
   `.slnx` — so a new sample, or a new project in one, is picked up without editing the workflow. Every
   `*.Tests` project among them runs; OrderApi (its one Web-SDK project) and CatalogApi are also
   smoke-tested over HTTP.

Locally:

```bash
dotnet pack Platform.SharedKernel.slnx -c Release          # writes ./nupkgs
V=$(ls nupkgs/SharedKernel.Primitives.*.nupkg | sed -E 's/.*Primitives\.(.*)\.nupkg/\1/')
dotnet test samples/OrderApi/OrderApi.Tests -c Release -p:SharedKernelPackageVersion=$V
dotnet run --project samples/OrderApi/OrderApi.Api -p:SharedKernelPackageVersion=$V -- --urls http://localhost:5199 --environment Development
```

The BillingApi, ShippingApi and DocumentsApi tests need Docker.
