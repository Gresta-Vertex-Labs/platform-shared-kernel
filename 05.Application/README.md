# 05.Application

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
[![MediatR 12.4](https://img.shields.io/badge/MediatR-12.4%20(last%20MIT)-5c6bc0)](https://github.com/jbogard/MediatR)

> **The use-case layer of a SharedKernel service: commands and queries sent through MediatR, and one pipeline around
> every handler — tracing, logging, metrics, authorization, validation, idempotency, transactions, auditing and
> caching — in a fixed order, registered with one call.**

A handler takes a command or query and returns a `Result`. Everything around it is a pipeline behavior. Authorization
is declared on the use case itself (`[RequirePermission]`), so it holds whether the command arrives over HTTP, from a
message consumer, a scheduled job or a workflow.

## Packages

| Package | What it is | You reference it |
| --- | --- | --- |
| [`SharedKernel.Application`](SharedKernel.Application/README.md) | `ICommand`, `IQuery<T>`, their handlers, `[RequirePermission]`, the markers (`IIdempotentRequest`, `IAuditableRequest<T>`, `ILoggableRequest<T>`), `ICommandScope`, the pipeline and its one registration call | Always |
| [`SharedKernel.Application.Caching`](SharedKernel.Application.Caching/README.md) | Query caching (`ICacheableQuery<T>`) and eviction after the commit (`IInvalidatesCache`), over `02.Caching` | When you cache a query |
| [`SharedKernel.Application.Abstractions`](SharedKernel.Application.Abstractions/README.md) | The ports infrastructure implements: `IRequestContext` (who is calling), `IUnitOfWork`, `IAuditTrailWriter`. No MediatR | Not directly: it comes with `SharedKernel.Application`, and `06.Persistence` and `13.ServiceDefaults` implement it |

MediatR 12.4.1 (the last MIT release) is the engine and stays visible: you send with MediatR's `ISender`, and
`ICommand`/`IQuery<T>` derive from its `IRequest<T>`. There is no facade.

## The 10-minute path

### 1. Register

```csharp
using SharedKernel.Application;
using SharedKernel.ServiceDefaults.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSharedKernelRequestContext();   // IRequestContext over 12.Security (13.ServiceDefaults)
// builder.AddSharedKernelPostgres<OrdersDbContext>("orders", …) registers IUnitOfWork and IAuditTrailWriter

builder.Services.AddSharedKernelApplication(typeof(Program).Assembly, app => app
    .WithAuthorization()     // [RequirePermission] on every command and query
    .WithTransactions()      // one retry-safe transaction per command
    .WithAuditing());        // an audit record for IAuditableRequest<T> commands
```

`AddSharedKernelApplication` registers MediatR with the handlers of the assemblies you pass, their FluentValidation
validators, the domain-event bridge, `ICommandScope`, and the tracing, logging, metrics and validation behaviors. The
`With…` calls add the rest. Neither the call order nor registering a seam before or after this call matters: every
service an opted-in behavior needs is checked when the host starts, and a missing one fails the start with one message
naming each service. Calling `AddSharedKernelApplication` twice throws; pass every assembly to the one call.

### 2. Write a use case

One file per use case: the command, its permission, its validator and its handler.

```csharp
using FluentValidation;
using SharedKernel.Application;
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

[RequirePermission("orders.write")]
public sealed record PlaceOrder(string Customer, decimal Amount) : ICommand<Guid>;

public sealed class PlaceOrderValidator : AbstractValidator<PlaceOrder>
{
    public PlaceOrderValidator()
    {
        RuleFor(c => c.Customer).NotEmpty();
        RuleFor(c => c.Amount).GreaterThan(0);
    }
}

public sealed class PlaceOrderHandler(IOrderRepository orders) : ICommandHandler<PlaceOrder, Guid>
{
    public async Task<Result<Guid>> Handle(PlaceOrder command, CancellationToken cancellationToken)
    {
        if (await orders.IsBlockedAsync(command.Customer, cancellationToken))
            return Error.BusinessRule("order.customer_blocked", "The customer may not place orders.");

        return await orders.AddAsync(command.Customer, command.Amount, cancellationToken);
    }
}
```

The handler has no permission check, no validation, no `try`/`catch` and no `SaveChanges`. An expected failure is a
returned `Error`, never an exception.

### 3. Send it

Over HTTP, an endpoint module of `14.Presentation` turns the request into the command and the `Result` into the
response (see [14.Presentation](../14.Presentation/README.md)):

```csharp
using MediatR;
using SharedKernel.Presentation.WebApi;

public sealed record PlaceOrderRequest(string Customer, decimal Amount);

public sealed class OrderEndpoints : IEndpointModule
{
    public static void Map(IEndpointRouteBuilder app)
    {
        app.MapPost("/orders", (PlaceOrderRequest body, ISender sender, CancellationToken ct) =>
            sender.Send(new PlaceOrder(body.Customer, body.Amount), ct).ToCreated(id => $"/orders/{id}"));
    }
}
```

Permissions go on the use case with `[RequirePermission]`; the endpoint does not repeat them. `14.Presentation`'s
`[RequireEndpointPermission]` is only for hubs, gRPC services and endpoints that send no command. A consumer, a job or
a workflow activity sends the same command with `ISender`, and the same checks apply.

| The caller… | Gets | The handler |
| --- | --- | --- |
| is not signed in | 401 `unauthorized.default` | never runs |
| lacks `orders.write` | 403 `forbidden.insufficient_permission` | never runs |
| sends an empty customer | 400 `validation.failed`, each field in `errors` | never runs |
| names a blocked customer | 422 `order.customer_blocked`, nothing committed | runs and returns the error |
| succeeds | 201 with `Location`, committed and audited | runs |

## The pipeline

Outermost first. The order is fixed, whatever order the `With…` calls are made in. A query stops after the query
stage; only a command (`ICommand`, `ICommand<T>`) enters the command stage.

| Stage | Behavior | Applies to | Registered by |
| --- | --- | --- | --- |
| Observability | Tracing, Logging, Metrics | every request | always |
| Authorization | Authorization | requests with `[RequirePermission]` | `WithAuthorization()` |
| Validation | Validation | requests with a FluentValidation validator | always |
| Query | Caching | queries implementing `ICacheableQuery<T>` | `WithCaching()` |
| Command | Command scope | commands | automatic with any command-stage behavior |
| Command | Idempotency | commands implementing `IIdempotentRequest` | `WithIdempotency()` |
| Command | Auditing, failure half (after the rollback) | commands implementing `IAuditableRequest<T>` | `WithAuditing()` |
| Command | Transaction: everything below runs inside it | commands | `WithTransactions()` |
| Command | Auditing, success half (inside the transaction) | commands implementing `IAuditableRequest<T>` | `WithAuditing()` |
| Command | Cache eviction, queued for after the commit | commands implementing `IInvalidatesCache` | `WithCaching()` |

Authorization runs before validation on purpose: a caller who may not run a use case must not learn its validation
rules, and a validator may query the database.

### The opt-ins and what each needs

| Call | Needs a registered | Usually from |
| --- | --- | --- |
| `WithAuthorization()` | `IRequestContext` | `13.ServiceDefaults`' `AddSharedKernelRequestContext()`; a `SystemRequestContext` for a worker |
| `WithIdempotency()` | `IRequestIdempotencyStore`, `IRequestContext` | `18.Idempotency`'s Redis or EF Core store |
| `WithTransactions()` | `IUnitOfWork` | `06.Persistence`'s `AddSharedKernelPostgres` |
| `WithAuditing()` | `IAuditTrailWriter` | `06.Persistence`'s `UseAuditTrail()` |
| `WithCaching()` (`SharedKernel.Application.Caching`) | `ICacheService`, `ITenantCacheKeyProvider` | `02.Caching`'s `AddSharedKernelCaching()` |
| `WithBehavior(typeof(MyBehavior<,>), stage, services…)` | the services you list | your own behavior, placed in a stage |

### Commands, transactions and the command scope

- **One commit, at the outermost command.** A command sent from inside another command's handler joins the outer
  transaction and skips idempotency. A failed nested command makes the outer transaction rollback-only.
- **Handlers must be re-runnable.** The unit of work may replay the whole transaction after a transient database
  failure. Load inside the handler; keep HTTP calls and messages out of it.
- **Work after the commit** goes through `ICommandScope.OnCompleted`. It runs once, after the outermost command
  committed, and never after a failure.
- **Only a success persists anything.** A failed `Result` or an exception commits nothing, caches nothing, evicts
  nothing and releases the idempotency key. The audit trail records the attempt either way.

## Markers

A use case opts into a behavior by what it declares:

| Declare | On | To |
| --- | --- | --- |
| `[RequirePermission("a", "b")]` | command or query | require a permission; the values of one attribute are alternatives, several attributes all apply |
| `IIdempotentRequest` (`IdempotencyKey`, optional `Fingerprint`) | command | replay the first response to a retry by the same caller; keys are reserved per tenant and caller |
| `IAuditableRequest<TResponse>` | command | record success and failure in the audit trail |
| `ILoggableRequest<TResponse>` | command or query | log the fields you choose; nothing else is logged |
| `ICacheableQuery<TValue>` | query | serve it from the cache |
| `IInvalidatesCache` | command | evict named queries' entries after the commit |

A request that declares `[RequirePermission]` or `IIdempotentRequest` must return `Result` or `Result<T>`, because a
denial is returned as a failed result. Analyzer `SK0040` flags any other response type.

## Outside HTTP

The pipeline reads the caller from `IRequestContext`. A worker with no HTTP request registers a named identity with
exactly the permissions it needs:

```csharp
builder.Services.AddScoped<IRequestContext>(_ => new SystemRequestContext(
    permissions: ["orders.expire"],
    identity: "orders-worker"));
```

`AnonymousRequestContext.Instance` is the unauthenticated caller. `17.Workflows`' `CommandActivity<TCommand>` and
`19.Scheduling`'s `ScheduledCommandJob<TCommand>` send commands through the same pipeline.

## Analyzers that guard this layer

`SharedKernel.Analyzers` (`00.Governance`) reports these in the services that use this layer:

| Rule | Flags |
| --- | --- |
| SK0016 | `typeof(T).Name` as a metric tag, log scope or cache key |
| SK0017, SK0018 | A command implementing `ICacheableQuery`; a query implementing `IInvalidatesCache` |
| SK0030 | A `Result` returned by a call and never checked |
| SK0040 | `[RequirePermission]` or `IIdempotentRequest` on a request that does not return `Result`/`Result<T>` |
| SK0041 | Two `ICacheableQuery` types with the same simple name |

## Removed before first publish

| Removed | Use instead |
| --- | --- |
| The `SharedKernel.Application.Behaviors` package | `SharedKernel.Application`, which contains the behaviors |
| `SharedKernel.Application.Behaviors.Caching` | `SharedKernel.Application.Caching`, opted into with `WithCaching()` |
| `AddSharedKernelApplicationBehaviors()…Build()`, `AddMediatR`, `AddValidatorsFromAssembly…`, `AddCachingBehaviors()` | One `AddSharedKernelApplication(assembly, app => …)` |
| `IAuthorizeRequest`, `PermissionMatch` | `[RequirePermission]` on the command or query |
| `IdempotencyErrorCodes` | `ErrorCodes.Idempotency` (`01.Core`): `KeyRequired`, `KeyInvalid`, `InProgress`, `KeyReused` |
| Type forwarders from `SharedKernel.Application` to `.Abstractions` | Nothing: the namespaces (`SharedKernel.Application.Context`, `.Transactions`, `.Auditing`) are unchanged |
| The pipeline's `authorization.unauthenticated` and `authorization.no_permissions_declared` | `unauthorized.default`, as at the HTTP edge; a request without the attribute is not checked |
| Fire-and-forget dispatch, `ResilienceBehavior`, streaming behaviors, parallel domain-event dispatch, `DualApprovalBehavior` | Your own background queue; the resilience of the call a handler makes (`11.Communication`); the stream handler itself; serial dispatch; an approval modelled in your domain |

## Build and test

```shell
dotnet test 05.Application/SharedKernel.Application.Abstractions/SharedKernel.Application.Abstractions.Tests -c Release
dotnet test 05.Application/SharedKernel.Application/SharedKernel.Application.Tests -c Release
dotnet test 05.Application/SharedKernel.Application.Caching/SharedKernel.Application.Caching.Tests -c Release
```

## Docs in this folder

| File | What it is |
| --- | --- |
| [`CLAUDE.md`](CLAUDE.md) | Maintainer rules: what may change and what breaks elsewhere |
| [`CLAUDE.history.md`](CLAUDE.history.md) | Superseded designs and why they changed; never a description of the code today |
| [`docs/p563/`](docs/p563/) | The P-563 design (one application model, a thin HTTP edge) and how it ran |
| [`state-map.md`](state-map.md) | Phase and task history |

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel).
