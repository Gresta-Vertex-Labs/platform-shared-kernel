# OrderApi — the reference service shape

A minimal order-taking API, split the way every service on the kernel should be: four projects, each
referencing exactly the kernel packages its layer is allowed to see. It runs with **no database, cache,
or broker**, so what you read is the composition, not infrastructure.

```
OrderApi.Domain          → SharedKernel.Domain
OrderApi.Application     → OrderApi.Domain, SharedKernel.Application (+ FluentValidation, the library)
OrderApi.Infrastructure  → OrderApi.Application, SharedKernel.Validation.FluentValidation (adapters)
OrderApi.Api             → OrderApi.Application, OrderApi.Infrastructure,
                           SharedKernel.ServiceDefaults, .ServiceDefaults.Security,
                           .Application.Pipeline, .Application.Mediator.MediatR, .Presentation.WebApi
OrderApi.Tests           → OrderApi.Api, SharedKernel.Application.Testing (test project only)
```

| Project | Tier it may see | What lives there |
|---|---|---|
| `OrderApi.Domain` | Foundation, Model | `Order` (`AggregateRoot<OrderId>`), `Money` (`ValueObject`), `OrderId` (`StronglyTypedId<Guid>`), `OrderPlacedEvent` |
| `OrderApi.Application` | + Abstractions | `PlaceOrderCommand`/`GetOrderQuery` and their handlers, the FluentValidation validator, the `IOrderRepository` port, `AddOrderApplication()` |
| `OrderApi.Infrastructure` | + Adapter | `InMemoryOrderRepository`, `OrderStoreReadinessProbe` (`IReadinessProbe`), `AddOrderInfrastructure()` — which also wires the FluentValidation → `IRequestValidator<T>` bridge |
| `OrderApi.Api` | + Host | `Program.cs` (the composition root and the middleware order), `OrderEndpoints` |

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
dotnet run --project samples/OrderApi/OrderApi.Api --urls http://localhost:5199
dotnet test samples/OrderApi/OrderApi.Tests
```

```bash
curl -i -X POST http://localhost:5199/orders -H 'Content-Type: application/json' \
  -d '{"customer":"Acme Ltd","amount":149.50,"currency":"eur","lines":["Widget x2"]}'
# 201 {"id":"01a03953-4f9b-7626-8a4e-adefdfbef579"}   X-Correlation-Id: <generated>

curl http://localhost:5199/orders/{id}
# 200 {"id":"...","customer":"Acme Ltd","amount":149.50,"currency":"EUR","lines":["Widget x2"]}
```

Note `"eur"` comes back as `"EUR"` — `Money` normalises and validates in its own constructor, so an
invalid instance cannot exist.

## The parts worth reading

**Each layer registers what it owns.** `Program.cs` calls `AddOrderApplication()` and
`AddOrderInfrastructure()` and never names a validator, a repository or a probe. Handlers are not
registered by anyone: `AddSharedKernelMediatR(typeof(PlaceOrderCommand).Assembly)` discovers them, so the
Application never learns which mediator runs it.

**The middleware order is the canonical one.** `UseSharedKernelRequestContext()` goes first, so every
response — error responses included — carries the request's `X-Correlation-Id` and every later
middleware runs with the caller in scope; then security headers, then the exception handler, then
(in a service with authentication) `UseAuthentication()`/`UseAuthorization()`, then the endpoints.
The sample has no authentication, so it registers `AnonymousUserContext` as the `IUserContext`; a real
service calls `AddOidcAuthentication(configuration)` instead and nothing else changes.

**Errors never choose a status code.** A handler returns `Error.NotFound(...)` or
`Error.Validation(...)`; `ToProblemDetailsResult()` maps it. The endpoint never inspects `IsSuccess`:

```json
{"type":"https://httpstatuses.io/404","title":"order.notFound","status":404,
 "detail":"Order ... was not found.","traceId":"00-7642...","errorCode":"order.notFound"}
```

**Validation never throws.** `ValidationBehavior` runs `PlaceOrderCommandValidator` (through the
FluentValidation bridge) before the handler, collects every failing rule into one
`Error.Validation(errors)`, and returns it as a failed `Result<Guid>`. The handler does the same with
`Money.Create`'s errors, so both paths produce one 400 whose `errors` map lists each field:

```json
{"type":"https://httpstatuses.io/400","title":"validation.failed","status":400,
 "detail":"3 validation errors occurred.","errorCode":"validation.failed",
 "errors":{"Customer":["'Customer' must not be empty."],"Currency":["'Currency' must not be empty."],
           "Lines":["'Lines' must not be empty."]}}
```

**Readiness is every adapter's probe.** Infrastructure registers `OrderStoreReadinessProbe` with
`AddReadinessProbe<T>()`; the Api maps every registered probe with `AddSharedKernelReadiness()` and
never lists them. `/health/ready` also returns 503 until `StartupGate.MarkReady()` is called, so
Kubernetes will not route traffic to a pod still migrating or seeding. The sample has no startup
work, so it signals immediately — but the call is shown rather than hidden.

**Time is injected.** `IClock`, never `DateTime.UtcNow` — analyzer SK0001 enforces this. The tests
replace it with `SharedKernel.Testing`'s `FakeClock` and assert the domain event's timestamp.

**Tests use the packed testing packages.** `PlaceOrderTests` runs the Application through the real
pipeline with `SharedKernel.Application.Testing`'s `ApplicationPipelineTestHarness` — the same
behaviors in the same order as the Api, no HTTP. `HttpTests` drives the real `Program` with
`WebApplicationFactory`.

## What it deliberately omits

No database (`IOrderRepository` is an in-memory dictionary), no cache, no messaging, no auth. Each
would pull in infrastructure and obscure the composition. For the same reason the pipeline stops at
the preset: `AddAuthorizationBehavior()`, `AddIdempotencyBehavior()`, `AddTransactionBehavior()`,
`AddAuditingBehavior()` and `AddCachingBehaviors()` each need a seam (`IRequestContext`,
`IIdempotencyStore` for `IdempotencyPurpose.Request`, `IUnitOfWork`, `IAuditTrailWriter`,
`ICacheService`) registered first, and `Build()` throws if it is missing. A real service swaps
`InMemoryOrderRepository` for `SharedKernel.Persistence.EfCore`'s repositories in Infrastructure — see
`samples/BillingApi`.
