# samples — how to consume the kernel

Runnable services built only from the **packed** SharedKernel packages. Together they are the reference
for how a service on the kernel is shaped: which projects it has, which package goes in which project,
how the version is pinned, and in what order `Program.cs` composes things. Each one also proves, in CI,
that a family of packages works end to end for a consumer that has nothing but the published artifacts.

| Sample | What it is the reference for | External infrastructure |
|---|---|---|
| [`OrderApi`](OrderApi/) | **The four-project shape**, with an architecture test that enforces it; the application pipeline, validation, `Result` → ProblemDetails, readiness | none |
| [`BillingApi`](BillingApi/) | The whole persistence stack: EF Core + Dapper in one transaction, row-level security, field encryption, the audit ledger, authorization/transaction/auditing behaviors | PostgreSQL (Docker Compose, or Testcontainers in its tests) |
| [`ShippingApi`](ShippingApi/) | Messaging: publish/send over RabbitMQ, delayed delivery, consumer idempotency, retries and faults, the caller's tenant, actor and correlation id carried to the consumer | RabbitMQ (Testcontainers, `masstransit/rabbitmq` for the delayed-exchange plugin) |
| [`DocumentsApi`](DocumentsApi/) | Object storage: named and tenant stores on two S3 connections plus OBS, presigned links and forms, multipart | MinIO (Testcontainers); optionally real Amazon S3 and Huawei Cloud OBS (`SK_LIVE_*`) |
| [`CatalogApi`](CatalogApi/) | Search: both engines side by side against different document types, the neutral contracts plus each engine's exclusive ones | Meilisearch and Elasticsearch (Docker, see its README) |

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
| **Application** | `SharedKernel.Application` (`ICommand`/`IQuery`, handlers, `ISender`, the pipeline markers); `SharedKernel.Idempotency.Abstractions` / `SharedKernel.Caching.Abstractions` when it uses the idempotency or caching markers; other `*.Abstractions` it needs (`Persistence.Abstractions` for `IRepository<,>`, `Messaging.Abstractions` for `IEventPublisher`, `Storage.Abstractions`, `Search.Abstractions`, …); `Execution` for `IRequestContext`/`IUnitOfWork` | `Application.Pipeline`, `Application.Mediator.MediatR`, MediatR itself, any adapter, any host package |
| **Infrastructure** | adapters: `Persistence.EfCore` (+ `.Auditing`, `.Encryption`), `Persistence.Dapper`, `Messaging.MassTransit` + a transport satellite (`.RabbitMq` / `.AzureServiceBus`) and `.EfCore` for the outbox, `Storage.S3` / `.Obs`, `Search.Meilisearch` / `.ElasticSearch`, `Caching.FusionCache` / `.Redis`, `Idempotency.Redis` / `.EfCore`, `Communication.Rest` / `.Grpc`, `Validation.FluentValidation`, `Security.Oidc` | host packages |
| **Api / Worker** | host: `ServiceDefaults`, `ServiceDefaults.Security` (request context), `ServiceDefaults.Persistence`, `MultiTenancy`, `Application.Pipeline` (+ `.Caching`), `Application.Mediator.MediatR`, `Presentation.WebApi` / `.Grpc` / `.SignalR` / `.GraphQL` | business logic |
| **Tests** | `SharedKernel.Testing` (fakes: `FakeClock`, `TestRequestContext`, fakers) and the per-capability `*.Testing` package of what the test touches — `Application.Testing` (pipeline harness), `Persistence.Testing`, `Messaging.Testing`, `Storage.Testing`, `Idempotency.Testing`, … | being referenced by a production project |

`samples/OrderApi` is this shape exactly, and `OrderApi.Tests/ArchitectureTests.cs` asserts it against
the real restore graph. Copy that test into a new service and fill in its project names.

The other samples are single-project hosts on purpose: each is about one family of packages, and the
split would add projects without adding anything that `OrderApi` does not already show.

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

// Who is calling: an authentication package produces IUserContext, and AddSharedKernelRequestContext()
// turns it into the one IRequestContext every behavior, adapter and outbound call reads.
builder.Services.AddOidcAuthentication(builder.Configuration);
builder.Services.AddSharedKernelRequestContext();

// The service's own layers. Adapters register their own readiness probes.
builder.Services.AddOrderApplication();
builder.Services.AddOrderInfrastructure();             // e.g. AddSharedKernelPostgres<T>(...), AddSharedKernelStorage()...

// The application pipeline: the mediator adapter, then the behaviors (Build() checks every seam).
builder.Services.AddSharedKernelMediatR(typeof(PlaceOrderCommand).Assembly);
builder.Services.AddSharedKernelApplicationBehaviors()
    .AddDefaultBehaviors()                             // tracing, logging, metrics, validation
    .AddAuthorizationBehavior()                        // opt-ins, each needing its seam
    .AddTransactionBehavior()
    .Build();

// Messaging after the request context: WithInboundRequestContext() must be the last IRequestContext.
builder.Services.AddSharedKernelMessaging(builder.Configuration).UseRabbitMq(...)
    .WithInboundRequestContext().WithAmbientCorrelationPropagation().Build();

builder.Services.AddHealthChecks().AddSharedKernelReadiness();   // every IReadinessProbe, one call
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<SharedKernelExceptionHandler>();
```

The middleware order is fixed:

```csharp
var app = builder.Build();

app.UseSharedKernelRequestContext();   // FIRST: X-Correlation-Id + the request's context scope, on every response
app.UseSharedKernelSecurityHeaders();  // immediately after it
app.UseExceptionHandler();             // RFC 9457 ProblemDetails for anything that escapes
app.UseCors(CorsPolicyNames.Default);  // if the service uses AddSharedKernelCors()
app.UseAuthentication();
app.UseMiddleware<TenantResolutionMiddleware>();  // optional (SharedKernel.MultiTenancy): refines the tenant
app.UseAuthorization();

app.MapDefaultHealthCheckEndpoints();  // /health/live, /health/ready
app.MapOrderEndpoints();

app.Services.GetRequiredService<StartupGate>().MarkReady();   // after startup work; readiness is 503 until then
await app.RunAsync();
```

`UseSharedKernelRequestContext()` goes before authentication on purpose: it reads the caller lazily, the
first time something asks, which is after `UseAuthentication()` has run. A service without authentication
registers `AnonymousUserContext.Instance` as its `IUserContext` (OrderApi, DocumentsApi, CatalogApi).

## A short tour

**[OrderApi](OrderApi/)** — no infrastructure, so the composition is all there is to read. Four projects;
`AddOrderApplication()`/`AddOrderInfrastructure()` register what each layer owns; handlers are discovered
by `AddSharedKernelMediatR`; a FluentValidation validator runs through the kernel's `IRequestValidator<T>`
port; the order store's readiness probe is mapped by `AddSharedKernelReadiness()`. Tests: the
architecture test, the application layer through the real pipeline with `SharedKernel.Application.Testing`
and `FakeClock`, and HTTP through `WebApplicationFactory`.

**[BillingApi](BillingApi/)** — multi-tenant billing on PostgreSQL with the production role split. One
`AddSharedKernelPostgres<BillingDbContext>` registration with multi-tenancy (row-level security), field
encryption and the audit ledger; Dapper joining the same unit of work; the pipeline with authorization,
transaction and auditing; a development-only header authentication scheme that produces a real
`IUserContext`. Tests run over HTTP against Testcontainers PostgreSQL provisioned by
`SharedKernel.Persistence.Testing`, plus a handler test over its fakes.

**[ShippingApi](ShippingApi/)** — `AddSharedKernelMessaging(configuration).UseRabbitMq(...)` in one chain:
CloudEvents publish and point-to-point send, broker-side delayed delivery, at-most-once consumption over
an `IIdempotencyStore`, retries then a visible fault, and the publisher's tenant, actor and correlation id
rebuilt on the consumer so it reads `IRequestContext` exactly as an HTTP handler does. Tests go through a
real RabbitMQ broker.

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
dotnet run --project samples/OrderApi/OrderApi.Api --urls http://localhost:5199 -p:SharedKernelPackageVersion=$V
```

The BillingApi, ShippingApi and DocumentsApi tests need Docker.
