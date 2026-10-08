# samples — how to consume the kernel

One reference platform, [`Shop`](Shop/), built only from the **packed** SharedKernel packages. It is the reference
for how a service on the kernel is shaped: which projects it has, which package goes in which project, how the
version is pinned, in what order `Program.cs` composes things, and how a request travels from HTTP to a handler. It is
also the proof that the packages work together for a consumer that has nothing but the published artifacts: its
services reference 103 of the kernel's 104 packages directly, run against real infrastructure in containers under a
.NET Aspire AppHost, and have end-to-end flows across all of them. [`Shop/README.md`](Shop/README.md) has every
service, container and flow, and how to run them.

| Shop service | What it is the reference for |
|---|---|
| [`Catalog`](Shop/Catalog/) | **The four-project shape**, with architecture tests that enforce it; caching with an L2 and a backplane across replicas, Redis Pub/Sub, both search engines, semantic search and a chat model, presigned uploads, GraphQL, OpenAPI, localization, feature flags |
| [`Ordering`](Shop/Ordering/) | The four-project shape with the whole persistence stack (row-level security, field encryption, the audit ledger), the EF Core outbox on RabbitMQ, idempotent submissions, a Temporal workflow with compensation, gRPC over mutual TLS, REST with an API key, SignalR, TOTP step-up |
| [`Inventory`](Shop/Inventory/) | A single-project service: Dapper under row-level security, a gRPC server behind mutual TLS, Redis hashes and distributed locks, a job that runs once per occurrence across replicas |
| [`Billing`](Shop/Billing/) | Key Vault (secrets as configuration, signing, envelope encryption), API-key clients, validated IBAN/VAT, personal data marked and redacted, GDPR export and erasure, signed webhooks |
| [`Merchant`](Shop/Merchant/) | Receiving a webhook and verifying its signature |
| [`Notify`](Shop/Notify/) | A worker host: messaging consumers, email (SendGrid) and SMS (Twilio) notifications |
| [`Reports`](Shop/Reports/) | Streaming exports (CSV, Excel, PDF) into S3 and OBS stores behind presigned downloads, HTML to PDF through Gotenberg |

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

Catalog and Ordering are this shape exactly, and their tests assert it against the real restore graph through
[`Shop.TestSupport`](Shop/Shop.TestSupport/)'s `ServiceShape` (for example `OrderingArchitectureTests` in
[`Shop.Ordering.Tests`](Shop/Ordering/Shop.Ordering.Tests/)). Copy those tests into a new service and fill in its
project names. The other Shop services are single-project hosts on purpose: each is about a few families of packages,
and the split would add projects without adding anything Catalog and Ordering do not already show.

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
environment, is in [`CONTRIBUTING.md` → Consuming the packages](../CONTRIBUTING.md#consuming-the-packages). Never pin one
`SharedKernel.*` package to a different version from the rest, and never float the version.

**Inside this repository** the Shop does the same thing with the repository's own files: the root
`Directory.Packages.props` points every `SharedKernel.*` package at one property,
`$(SharedKernelPackageVersion)`, and the root `NuGet.Config` maps `SharedKernel.*` to the local `nupkgs/`
feed that `dotnet pack` writes. `samples/Shop/build.sh` (`build.ps1` on Windows) packs, reads the version it just
packed and passes it on, because a stale local build can outrank the one you just packed.

## Composing `Program.cs`

Registrations, in this order (each Shop service shows the parts it uses):

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();                          // telemetry, health endpoints, the startup gate
builder.WithMessagingTelemetry();                      // ...and each With*Telemetry the service needs

// Who is calling: an authentication package produces IUserContext, and AddSharedKernelRequestContext() turns it
// into the one IRequestContext every behavior, adapter and outbound call reads — and that [RequirePermission] is
// checked against.
builder.Services.AddOidcAuthentication(builder.Configuration);
builder.Services.AddSharedKernelRequestContext();

// The service's own adapters. Each registers its own readiness probe.
builder.AddOrderingInfrastructure();                   // e.g. AddSharedKernelPostgres<T>(...), AddSharedKernelMessaging(...)...

// The application layer in ONE call: the handlers of the assembly, the mediator behind the kernel's ISender, the
// always-on behaviors (tracing, logging, metrics, authorization, validation) and the opt-ins, each needing its seam.
// Seams are checked when the host starts, so registration order does not matter.
builder.Services.AddSharedKernelApplication(typeof(PlaceOrderCommand).Assembly, app => app
    .UseMediatR()
    .WithIdempotency()                                 // IIdempotencyStore (Request purpose)
    .WithTransactions()                                // IUnitOfWork
    .WithAuditing());                                  // IAuditTrailWriter

builder.Services.AddHealthChecks().AddSharedKernelReadiness();   // every IReadinessProbe, one call

builder.AddSharedKernelWebApi();                       // the HTTP boundary (SharedKernel:Presentation:WebApi)
builder.AddSharedKernelOpenApi(o => o.Title = "Catalog API");    // optional: versioning, OpenAPI, Scalar
```

A messaging bus is registered after the request context: `WithInboundRequestContext()` must be the last
`IRequestContext`. It is also what writes the caller (tenant, actor, correlation id) onto outgoing messages, so a
service that only publishes needs it too (Billing).

The middleware order is fixed — the canonical pipeline:

```csharp
var app = builder.Build();

app.UseSharedKernelRequestContext();   // FIRST: X-Correlation-Id, inbound baggage refused, the request's context scope
app.UseSharedKernelWebApi(p => p       // security headers, exception handler, problem bodies, routing, CORS,
    .BeforeAuthorization(a => a.UseMiddleware<TenantResolutionMiddleware>()));   // authentication, (tenant), authorization

app.MapDefaultHealthCheckEndpoints();  // /health/live, /health/ready
app.MapEndpoints();                    // every IEndpointModule of the assembly (generated at compile time)
app.MapSharedKernelOpenApi();          // Development only, unless ExposeInProduction

app.Lifetime.ApplicationStarted.Register(() => app.Services.GetRequiredService<StartupGate>().MarkReady());
await app.RunAsync();
```

`UseSharedKernelRequestContext()` goes before everything on purpose: its scope wraps the WebApi pipeline's exception
handler, so the correlation id is on every log line and every response, errors included. It reads the caller lazily,
the first time something asks, which is after the authentication `UseSharedKernelWebApi()` adds. The tenant-resolution
hook (`SharedKernel.MultiTenancy`) is only for services that use it.

## The HTTP boundary, the same way in every service

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
for a failure. It never calls a repository, store, search engine or bus itself: the handler does. Permissions are
declared once, on the command or query (`[RequirePermission]`), and enforced by the pipeline on every path — HTTP, a
message consumer, a workflow activity, a job — as a 401 problem for an anonymous caller and a 403 for a caller without
the permission. Every error, returned or thrown, is an RFC 9457 `application/problem+json` body carrying `errorCode`,
`traceId` and `correlationId`. A conflict that shows a precondition the request sent in a header (`If-Match`,
`If-None-Match`) to be false is 412; every other conflict is 409.

## Building and running

The Shop resolves `SharedKernel.*` by `PackageReference`, never `ProjectReference`: the point is to prove the packed
packages work for a consumer who has only the published artifacts, and a project reference would bypass exactly the
thing under test. So:

1. The Shop is **excluded from `Platform.SharedKernel.slnx`**; it has its own `Shop.slnx` and cannot build until the
   packages it consumes have been packed.
2. CI builds it in the packaging-verify job of `.github/workflows/verify.yml`, after `dotnet pack`. It finds the
   projects rather than listing them — every tracked `.csproj` that is not in the `.slnx` — so a new service or test
   project is picked up without editing the workflow. Every `*.Tests` project runs; `Shop.E2E`'s flows skip there.
3. The end-to-end flows are local: they need Docker with about 8 GB of memory and start the whole platform.

```bash
samples/Shop/build.sh                      # pack the kernel, build the Shop      (build.ps1 on Windows)
samples/Shop/build.sh --test               # ...and run the unit tests
samples/Shop/build.sh --e2e                # ...and the end-to-end flows (SHOP_E2E=1)
dotnet run --project samples/Shop/Shop.AppHost --launch-profile http   # the platform, with the Aspire dashboard
```
