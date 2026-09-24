# OrderApi — sample service

A minimal order-taking API composed from five SharedKernel domains, running with **no
database, cache, or broker**. It exists to answer one question: do the packed packages
actually work for a consumer who only has the published artifacts?

## What it demonstrates

| Layer | Package | What the sample shows |
|---|---|---|
| Host | `SharedKernel.ServiceDefaults` | `AddServiceDefaults()` — OpenTelemetry and health wiring in one call; `MapDefaultHealthCheckEndpoints()`; the `StartupGate` readiness contract |
| Presentation | `SharedKernel.Presentation.WebApi` | `AddSharedKernelWebApi()` + `UseSharedKernelWebApi()` — the whole HTTP boundary in two calls; `Result<T>` → typed results with `ToCreated()`/`ToOk()`; RFC 9457 error bodies on every path |
| Presentation | `SharedKernel.Presentation.OpenApi` | `AddSharedKernelOpenApi()` + `MapSharedKernelOpenApi()` — a versioned API, one OpenAPI 3.1 document per version and a Scalar reference, in Development only |
| Application | `SharedKernel.Application` | `AddSharedKernelApplication(typeof(Program).Assembly)` — one call registers MediatR with the handlers and validators of the assembly and the always-on behaviors (tracing, logging, metrics, validation); `ICommand<T>`/`IQuery<T>` handlers returning `Result<T>`; a FluentValidation validator whose failures come back as a `Result`, not an exception |
| Domain | `SharedKernel.Domain` | `AggregateRoot<TId>`, `StronglyTypedId`, `ValueObject`, a domain event |
| Core | `SharedKernel.Primitives` | `Result<T>`, `Error`, `IClock` |

## Run it

```bash
dotnet pack Platform.SharedKernel.slnx -c Release   # samples consume packed output
dotnet run --project samples/OrderApi -- --urls http://localhost:5199 --environment Development
```

```bash
curl -X POST http://localhost:5199/orders -H 'Content-Type: application/json' \
  -d '{"customer":"Acme Ltd","amount":149.50,"currency":"eur","lines":["Widget x2"]}'
# 201, Location: /orders/01a03953-…
# {"id":"01a03953-4f9b-7626-8a4e-adefdfbef579"}

curl http://localhost:5199/orders/{id}
# 200 {"id":"...","customer":"Acme Ltd","amount":149.50,"currency":"EUR","lines":["Widget x2"]}
```

Note `"eur"` comes back as `"EUR"` — `Money` normalises and validates in its own constructor,
so an invalid instance cannot exist.

In Development the API describes itself: `http://localhost:5199/openapi/v1.json` is the OpenAPI
document and `http://localhost:5199/scalar` the interactive reference. In any other environment
neither is mapped — publishing an API description is a decision
(`SharedKernel:Presentation:OpenApi:ExposeInProduction`), not a default.

## The parts worth reading

**The HTTP boundary is two calls.** `builder.AddSharedKernelWebApi()` registers it and
`app.UseSharedKernelWebApi()`, called before any endpoint is mapped, adds it to the pipeline:
correlation ids (`X-Correlation-Id`, the trace id when the caller sends none), security headers,
the exception handler, problem bodies for the framework's own error statuses (an unknown route,
a wrong method), routing and authorization — in the order they must run.

**Errors never choose a status code.** A handler returns `Error.NotFound(...)` or
`Error.Validation(...)`; the endpoint sends the command or query through `ISender`, maps the `Result`
with one call and never inspects `IsSuccess`. The endpoints live in an endpoint module
(`Api/OrderEndpoints.cs`, an `IEndpointModule`), and `Program.cs` maps every module of the assembly
with one generated call, `app.MapEndpoints()`; the use cases live in `Features/Orders/`, each command
or query next to its handler and validator:

```csharp
orders.MapPost("/", (PlaceOrderCommand command, ISender sender, CancellationToken ct) =>
    sender.Send(command, ct).ToCreated(id => $"/orders/{id}", id => new OrderPlaced(id)));

orders.MapGet("/{id:guid}", (Guid id, ISender sender, CancellationToken ct) =>
    sender.Send(new GetOrderQuery(id), ct).ToOk());
```

The typed result (`Results<Created<OrderPlaced>, ErrorHttpResult>`) is also what lets the OpenAPI
document state the 201 body without annotations. Every failure is an RFC 9457
`application/problem+json` body with the error code, trace id and correlation id:

```json
{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.5","title":"Not Found","status":404,
 "detail":"Order 01999999-0000-7000-8000-000000000000 was not found.",
 "instance":"/orders/01999999-0000-7000-8000-000000000000","errorCode":"order.notFound",
 "correlationId":"d1a12575…","traceId":"00-d1a12575…-d6222221b369746b-01"}
```

**Validation never throws.** `ValidationBehavior` runs `PlaceOrderCommandValidator` before the
handler, collects every failing rule into one `Error.Validation(errors)`, and returns it as a
failed `Result<Guid>`. The handler does the same with `Money.Create`'s errors, so both paths
produce one 400 whose `errors` map lists each field, and `errorCodes` the code of each message:

```json
{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"Bad Request","status":400,
 "detail":"3 validation errors occurred.","instance":"/orders","errorCode":"validation.failed",
 "correlationId":"772e0498…","traceId":"00-772e0498…-01",
 "errors":{"Customer":["'Customer' must not be empty."],"Currency":["'Currency' must not be empty."],
           "Lines":["'Lines' must not be empty."]},
 "errorCodes":{"Customer":["NotEmptyValidator"],"Currency":["NotEmptyValidator"],"Lines":["NotEmptyValidator"]}}
```

**The API is versioned.** The endpoints belong to version 1.0 (`NewVersionedApi("Orders")` …
`HasApiVersion(1.0)`). A request that names no version gets the default, so `/orders` works as it
is; `X-Api-Version: 1.0` asks for it explicitly, every response reports `api-supported-versions`,
and an unsupported version is a 400 problem like any other error.

**`ValueObject` validates explicitly.** `Money` assigns every member in its constructor and calls
`EnsureValid()` last, so `Validate()` sees the fully built object. `Money.Create` returns a
`ValidationResult<Money>` carrying every error rather than the first.

**Readiness starts unhealthy.** `/health/ready` returns 503 until `StartupGate.MarkReady()`
is called, so Kubernetes will not route traffic to a pod still migrating or seeding. The
sample has no startup work, so it signals immediately — but the call is shown rather than
hidden, because omitting it leaves readiness at 503 forever.

**Time is injected.** `IClock`, never `DateTime.UtcNow` — analyzer SK0001 enforces this
platform-wide.

## What it deliberately omits

No database (`IOrderRepository` is an in-memory dictionary), no cache, no messaging, no auth —
so the OpenAPI document declares no security scheme (`Bearer = false`). Each would pull in
infrastructure and obscure the composition. For the same reason the pipeline keeps to the
always-on behaviors: `WithAuthorization()`, `WithIdempotency()`, `WithTransactions()`,
`WithAuditing()` and `WithCaching()` each need a seam (`IRequestContext`; `IRequestIdempotencyStore`
and `IRequestContext`, since keys are reserved per tenant and caller; `IUnitOfWork`;
`IAuditTrailWriter`; `ICacheService`), and the host refuses to start when one is missing. A real service swaps
`InMemoryOrderRepository` for `SharedKernel.Persistence.EfCore`'s `EfRepository<Order, OrderId>`.
