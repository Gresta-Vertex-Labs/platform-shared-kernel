<div align="center">

# OrderApi

**The reference service shape: four projects, each seeing only the kernel packages its layer may, and a test that keeps it that way.**

<sub>📂 <code>samples/OrderApi</code> · <a href="../README.md">all samples</a> · needs no infrastructure</sub>

</div>

## What it shows

- **The four-project shape.** Domain, Application, Infrastructure and Api, asserted against the real restore graph by
  `OrderApi.Tests/ArchitectureTests.cs` — the test to copy into a new service.
- **Use cases through the pipeline.** Commands and queries sent through the kernel's `ISender`, a FluentValidation
  validator run by the validation step, and `[RequirePermission("orders.cancel")]` proven 401/403/204 over HTTP.
- **One error shape.** Handlers return `Result`; endpoints map it with one typed-result call, and every failure is an
  RFC 9457 problem with `errorCode`, `traceId` and `correlationId`.
- **A versioned API that documents itself.** One OpenAPI 3.1 document per version and a Scalar reference, in
  Development only.
- **Readiness from the adapters.** Each adapter registers its probe; the `StartupGate` holds `/health/ready` at 503
  until startup work is done.

**Packages it uses:**
[SharedKernel.Domain](../../src/Model/Domain/SharedKernel.Domain/README.md) ·
[SharedKernel.Application](../../src/Application/SharedKernel.Application/README.md) ·
[SharedKernel.Validation.FluentValidation](../../src/Foundation/SharedKernel.Validation.FluentValidation/README.md) ·
[SharedKernel.Application.Pipeline](../../src/Application/SharedKernel.Application.Pipeline/README.md) ·
[SharedKernel.Application.Mediator.MediatR](../../src/Application/SharedKernel.Application.Mediator.MediatR/README.md) ·
[SharedKernel.ServiceDefaults](../../src/Hosting/ServiceDefaults/SharedKernel.ServiceDefaults/README.md) ·
[SharedKernel.ServiceDefaults.Security](../../src/Hosting/ServiceDefaults/SharedKernel.ServiceDefaults.Security/README.md) ·
[SharedKernel.Presentation.WebApi](../../src/Hosting/Presentation/SharedKernel.Presentation.WebApi/README.md) ·
[SharedKernel.Presentation.OpenApi](../../src/Hosting/Presentation/SharedKernel.Presentation.OpenApi/README.md) ·
tests: [SharedKernel.Application.Testing](../../src/Application/SharedKernel.Application.Testing/README.md)

Capabilities: [Domain](../../src/Model/Domain/README.md) · [Application](../../src/Application/README.md) ·
[Service defaults](../../src/Hosting/ServiceDefaults/README.md) · [Presentation](../../src/Hosting/Presentation/README.md)

## The shape

```
OrderApi.Domain          → SharedKernel.Domain
OrderApi.Application     → OrderApi.Domain, SharedKernel.Application (+ FluentValidation, the library)
OrderApi.Infrastructure  → OrderApi.Application, SharedKernel.Validation.FluentValidation (adapters)
OrderApi.Api             → OrderApi.Application, OrderApi.Infrastructure,
                           SharedKernel.ServiceDefaults, .ServiceDefaults.Security,
                           .Application.Pipeline, .Application.Mediator.MediatR,
                           .Presentation.WebApi (+ .Presentation.Core), .Presentation.OpenApi
OrderApi.Tests           → OrderApi.Api, SharedKernel.Application.Testing (test project only)
```

| Project | Tier it may see | What lives there |
|---|---|---|
| `OrderApi.Domain` | Foundation, Model | `Order` (`AggregateRoot<OrderId>`, with `Place` and `Cancel`), `Money` (`ValueObject`), `OrderId` (`StronglyTypedId<Guid>`), `OrderPlacedEvent`, `OrderCancelledEvent` |
| `OrderApi.Application` | + Abstractions | `Features/Orders/`: `PlaceOrderCommand`, `GetOrderQuery` and `CancelOrderCommand` (`[RequirePermission("orders.cancel")]`), each next to its handler; the FluentValidation validator; the `IOrderRepository` port |
| `OrderApi.Infrastructure` | + Adapter | `InMemoryOrderRepository`, `OrderStoreReadinessProbe` (`IReadinessProbe`), `AddOrderInfrastructure()` — which also registers the application's validators through the FluentValidation → `IRequestValidator<T>` bridge |
| `OrderApi.Api` | + Host | `Program.cs` (the composition root and the canonical pipeline), `OrderEndpoints` (an `IEndpointModule`) |

What each kernel package contributes:

| Package | Tier | What the sample uses |
|---|---|---|
| `SharedKernel.Domain` (brings `Primitives`, `Core`, `Execution`) | Model | `AggregateRoot<TId>`, `StronglyTypedId`, `ValueObject`, domain events; `Result<T>`, `Error`, `IClock` |
| `SharedKernel.Application` | Abstractions | `ICommand`/`ICommand<T>`/`IQuery<T>` and their handlers returning `Result`, `ISender`, `[RequirePermission]` |
| `SharedKernel.Validation.FluentValidation` | Adapter | `AddFluentValidationRequestValidators(assembly)` — FluentValidation validators run by the pipeline's validation step |
| `SharedKernel.ServiceDefaults` | Host | `AddServiceDefaults()` (OpenTelemetry, health), `MapDefaultHealthCheckEndpoints()`, `AddSharedKernelReadiness()`, the `StartupGate` |
| `SharedKernel.ServiceDefaults.Security` | Host | `AddSharedKernelRequestContext()` — the one `IRequestContext` over `IUserContext` — and `UseSharedKernelRequestContext()`, first in the pipeline |
| `SharedKernel.Application.Pipeline` + `.Mediator.MediatR` | Host | `AddSharedKernelApplication(typeof(PlaceOrderCommand).Assembly, app => app.UseMediatR())` — the handlers, MediatR behind the kernel's `ISender`, and the always-on behaviors (tracing, logging, metrics, authorization, validation) |
| `SharedKernel.Presentation.WebApi` (brings `.Presentation.Core`) | Host | `AddSharedKernelWebApi()` + `UseSharedKernelWebApi()` — the HTTP boundary in two calls; endpoint modules and the generated `MapEndpoints()`; typed results (`ToCreated`, `ToOk`, `ToNoContent`); RFC 9457 error bodies on every path |
| `SharedKernel.Presentation.OpenApi` | Host | `AddSharedKernelOpenApi()` + `MapSharedKernelOpenApi()` — a versioned API, one OpenAPI 3.1 document per version and a Scalar reference, in Development only |

`OrderApi.Tests/ArchitectureTests.cs` asserts that shape against the real restore graph (this test
assembly's `deps.json`, including transitive packages):

- the project references are exactly Domain ← Application ← Infrastructure, and Api → Application + Infrastructure;
- Domain references only `SharedKernel.Domain`, and its closure holds only Foundation and Model packages;
- Application references only `SharedKernel.Application`, and its closure holds no Adapter or Host package;
- Infrastructure's direct kernel references are all Adapters, and its closure holds no Host package;
- only the Api references Host packages, and all of its direct kernel references are Host packages;
- MediatR appears nowhere below the Api — it is an implementation detail of `SharedKernel.Application.Mediator.MediatR`;
- no production project sees a `*.Testing` package;
- every kernel package in the graph is classified by tier, so a new reference is looked at before it is accepted.

## Run it

```bash
dotnet pack Platform.SharedKernel.slnx -c Release   # samples consume packed output (./nupkgs)
dotnet run --project samples/OrderApi/OrderApi.Api -p:SharedKernelPackageVersion=<the packed version> -- --urls http://localhost:5199 --environment Development
dotnet test samples/OrderApi/OrderApi.Tests -p:SharedKernelPackageVersion=<the packed version>
```

```bash
curl -i -X POST http://localhost:5199/orders -H 'Content-Type: application/json' \
  -d '{"customer":"Acme Ltd","amount":149.50,"currency":"eur","lines":["Widget x2"]}'
# 201, Location: /orders/01a03953-…   X-Correlation-Id: <generated>
# {"id":"01a03953-4f9b-7626-8a4e-adefdfbef579"}

curl http://localhost:5199/orders/{id}
# 200 {"id":"...","customer":"Acme Ltd","amount":149.50,"currency":"EUR","lines":["Widget x2"],"cancelled":false}

curl -i -X POST http://localhost:5199/orders/{id}/cancel
# 401 {"title":"Unauthorized","status":401,"errorCode":"unauthorized.default", …}
```

Note `"eur"` comes back as `"EUR"` — `Money` normalises and validates in its own constructor, so an
invalid instance cannot exist.

In Development the API describes itself: `http://localhost:5199/openapi/v1.json` is the OpenAPI
document and `http://localhost:5199/scalar` the interactive reference. In any other environment
neither is mapped — publishing an API description is a decision
(`SharedKernel:Presentation:OpenApi:ExposeInProduction`), not a default. CI runs the host in Production and checks
that `/openapi/v1.json` is a 404.

## The parts worth reading

**The pipeline is the canonical one.** `Program.cs` calls `UseSharedKernelRequestContext()` first, so every
response — error responses included — carries the request's `X-Correlation-Id` and every later middleware runs with
the caller in scope; then `UseSharedKernelWebApi()`, which adds security headers, the exception handler, problem
bodies for the framework's own error statuses (an unknown route, a wrong method), routing, and authentication and
authorization when they are registered — in the order they must run; then `app.MapEndpoints()`.

**Each layer registers what it owns, and the Api composes.** `AddOrderInfrastructure()` registers the store, its
probe and the validators; one `AddSharedKernelApplication(typeof(PlaceOrderCommand).Assembly, app => app.UseMediatR())`
call discovers the handlers and chooses the mediator, so the Application never learns which mediator runs it.

**Errors never choose a status code.** A handler returns `Error.NotFound(...)` or `Error.Validation(...)`; the
endpoint sends the command or query through `ISender`, maps the `Result` with one call and never inspects
`IsSuccess`. The endpoints live in an endpoint module (`OrderApi.Api/OrderEndpoints.cs`, an `IEndpointModule`), and
`Program.cs` maps every module of the assembly with one generated call, `app.MapEndpoints()`:

```csharp
orders.MapPost("/", (PlaceOrderCommand command, ISender sender, CancellationToken ct) =>
    sender.Send(command, ct).ToCreated(id => $"/orders/{id}", id => new OrderPlaced(id)));

orders.MapGet("/{id:guid}", (Guid id, ISender sender, CancellationToken ct) =>
    sender.Send(new GetOrderQuery(id), ct).ToOk());

orders.MapPost("/{id:guid}/cancel", (Guid id, ISender sender, CancellationToken ct) =>
    sender.Send(new CancelOrderCommand(id), ct).ToNoContent());
```

The typed result (`Results<Created<OrderPlaced>, ErrorHttpResult>`) is also what lets the OpenAPI document state the
201 body without annotations. Every failure is an RFC 9457 `application/problem+json` body with the error code, trace
id and correlation id:

```json
{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.5","title":"Not Found","status":404,
 "detail":"Order 01999999-0000-7000-8000-000000000000 was not found.",
 "instance":"/orders/01999999-0000-7000-8000-000000000000","errorCode":"order.notFound",
 "correlationId":"d1a12575…","traceId":"00-d1a12575…-d6222221b369746b-01"}
```

**Permissions belong to the use case.** `CancelOrderCommand` declares `[RequirePermission("orders.cancel")]`; the
endpoint declares nothing. The pipeline's authorization behavior checks the permission against `IRequestContext`
before the handler runs, on every path the command can be sent from: an anonymous caller gets a 401 problem
(`unauthorized.default`), an authenticated caller without the permission a 403 (`forbidden.insufficient_permission`).
Because a scanned use case declares a permission, the host refuses to start without an `IRequestContext` —
`AddSharedKernelRequestContext()` provides it. The sample has no identity provider, so it registers
`AnonymousUserContext` as the `IUserContext` and every cancel is a 401; a real service calls
`AddOidcAuthentication(configuration)` instead and nothing else changes. `CancelOrderAuthorizationTests` swaps in a
header-driven test authentication scheme and proves all three answers over HTTP (401, 403, 204).

The OpenAPI document reads endpoint metadata only, so it does not know about a permission declared on a command; a
service with authentication adds `.RequireAuthorization()` (or `RequireEndpointPermission`) to such endpoints so the
document lists their security requirement and 401/403.

**Validation never throws.** The pipeline's validation step runs `PlaceOrderCommandValidator` (through the
FluentValidation bridge) before the handler, collects every failing rule into one `Error.Validation(errors)`, and
returns it as a failed `Result<Guid>`. The handler does the same with `Money.Create`'s errors, so both paths produce
one 400 whose `errors` map lists each field, and `errorCodes` the code of each message:

```json
{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"Bad Request","status":400,
 "detail":"3 validation errors occurred.","instance":"/orders","errorCode":"validation.failed",
 "correlationId":"772e0498…","traceId":"00-772e0498…-01",
 "errors":{"Customer":["'Customer' must not be empty."],"Currency":["'Currency' must not be empty."],
           "Lines":["'Lines' must not be empty."]},
 "errorCodes":{"Customer":["NotEmptyValidator"],"Currency":["NotEmptyValidator"],"Lines":["NotEmptyValidator"]}}
```

**The API is versioned.** The endpoints belong to version 1.0 (`NewVersionedApi("Orders")` … `HasApiVersion(1.0)`).
A request that names no version gets the default, so `/orders` works as it is; `X-Api-Version: 1.0` asks for it
explicitly, every response reports `api-supported-versions`, and an unsupported version is a 400 problem like any
other error.

**Readiness is every adapter's probe.** Infrastructure registers `OrderStoreReadinessProbe` with
`AddReadinessProbe<T>()`; the Api maps every registered probe with `AddSharedKernelReadiness()` and never lists them.
`/health/ready` also returns 503 until `StartupGate.MarkReady()` is called, so Kubernetes will not route traffic to a
pod still migrating or seeding. The sample has no startup work, so it signals immediately — but the call is shown
rather than hidden.

**`ValueObject` validates explicitly.** `Money` assigns every member in its constructor and calls `EnsureValid()`
last, so `Validate()` sees the fully built object. `Money.Create` returns a `ValidationResult<Money>` carrying every
error rather than the first.

**Time is injected.** `IClock`, never `DateTime.UtcNow` — analyzer SK0001 enforces this. The tests replace it with
`SharedKernel.Testing`'s `FakeClock` and assert the domain event's timestamp.

**Tests use the packed testing packages.** `PlaceOrderTests` runs the Application through the real pipeline with
`SharedKernel.Application.Testing`'s `ApplicationPipelineTestHarness` (`Build<PlaceOrderCommand>()` registers the
assembly with `UseMediatR()`, as the Api does) and `SharedKernel.Testing`'s `TestRequestContext` as the caller — the
same behaviors in the same order as the Api, no HTTP. `HttpTests` and `CancelOrderAuthorizationTests` drive the real
`Program` with `WebApplicationFactory`.

## What it deliberately omits

No database (`IOrderRepository` is an in-memory dictionary), no cache, no messaging, no identity provider — so the
OpenAPI document declares no security scheme (`Bearer = false`). Each would pull in infrastructure and obscure the
composition. For the same reason the pipeline keeps to the always-on behaviors. `WithIdempotency()`,
`WithTransactions()`, `WithAuditing()` and `WithCaching()` each need a seam (an `IIdempotencyStore` for
`IdempotencyPurpose.Request` and `IRequestContext`, since keys are reserved per tenant and caller; `IUnitOfWork`;
`IAuditTrailWriter`; `ICacheService`), and the host refuses to start when one is missing. A real service swaps
`InMemoryOrderRepository` for `SharedKernel.Persistence.EfCore`'s repositories in Infrastructure — see
`samples/BillingApi`.
