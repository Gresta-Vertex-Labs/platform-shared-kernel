# OrderApi — sample service

A minimal order-taking API composed from five SharedKernel domains, running with **no
database, cache, or broker**. It exists to answer one question: do the packed packages
actually work for a consumer who only has the published artifacts?

## What it demonstrates

| Layer | Package | What the sample shows |
|---|---|---|
| Host | `SharedKernel.ServiceDefaults` | `AddServiceDefaults()` — OpenTelemetry and health wiring in one call; `MapDefaultHealthCheckEndpoints()`; the `StartupGate` readiness contract |
| Presentation | `SharedKernel.Presentation.WebApi` | `Result<T>` → HTTP via `ToProblemDetailsResult()`; RFC 9457 error bodies |
| Application | `SharedKernel.Application[.Behaviors]` | `ICommand<T>`/`IQuery<T>` handlers returning `Result<T>`; the `AddDefaultBehaviors().Build()` preset (tracing, logging, metrics, validation); a FluentValidation validator whose failures come back as a `Result`, not an exception |
| Domain | `SharedKernel.Domain` | `AggregateRoot<TId>`, `StronglyTypedId`, `ValueObject`, a domain event |
| Core | `SharedKernel.Primitives` | `Result<T>`, `Error`, `IClock` |

## Run it

```bash
dotnet pack Platform.SharedKernel.slnx -c Release   # samples consume packed output
dotnet run --project samples/OrderApi
```

```bash
curl -X POST http://localhost:5199/orders -H 'Content-Type: application/json' \
  -d '{"customer":"Acme Ltd","amount":149.50,"currency":"eur","lines":["Widget x2"]}'
# 201 {"id":"01a03953-4f9b-7626-8a4e-adefdfbef579"}

curl http://localhost:5199/orders/{id}
# 200 {"id":"...","customer":"Acme Ltd","amount":149.50,"currency":"EUR","lines":["Widget x2"]}
```

Note `"eur"` comes back as `"EUR"` — `Money` normalises and validates in its own constructor,
so an invalid instance cannot exist.

## The parts worth reading

**Errors never choose a status code.** A handler returns `Error.NotFound(...)` or
`Error.Validation(...)`; `ToProblemDetailsResult()` maps it. The endpoint never inspects
`IsSuccess`:

```json
{"type":"https://httpstatuses.io/404","title":"order.notFound","status":404,
 "detail":"Order ... was not found.","traceId":"00-7642...","errorCode":"order.notFound"}
```

**Validation never throws.** `ValidationBehavior` runs `PlaceOrderCommandValidator` before the
handler, collects every failing rule into one `Error.Validation(errors)`, and returns it as a
failed `Result<Guid>`. The handler does the same with `Money.Create`'s errors, so both paths
produce one 400 whose `errors` map lists each field:

```json
{"type":"https://httpstatuses.io/400","title":"validation.failed","status":400,
 "detail":"3 validation errors occurred.","errorCode":"validation.failed",
 "errors":{"Customer":["'Customer' must not be empty."],"Currency":["'Currency' must not be empty."],
           "Lines":["'Lines' must not be empty."]}}
```

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

No database (`IOrderRepository` is an in-memory dictionary), no cache, no messaging, no auth.
Each would pull in infrastructure and obscure the composition. For the same reason the pipeline
stops at the preset: `AddAuthorizationBehavior()`, `AddIdempotencyBehavior()`,
`AddTransactionBehavior()`, `AddAuditingBehavior()` and `AddCachingBehaviors()` each need a seam
(`IRequestContext`, `IRequestIdempotencyStore`, `IUnitOfWork`, `IAuditTrailWriter`,
`ICacheService`) registered first, and `Build()` throws if it is missing. A real service swaps
`InMemoryOrderRepository` for `SharedKernel.Persistence.EfCore`'s `EfRepository<Order, OrderId>`.
