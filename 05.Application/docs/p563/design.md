# P-563 — one application model with a thin HTTP edge

Owner decisions, 2026-09-24. Nothing is in production; breaking changes are expected and allowed. The packages of
05.Application are published only as alphas.

## Why

05.Application and 14.Presentation were each polished on their own rules, and the sum was fragmented:

- The same concern existed twice. Authorization was `[RequirePermission]` on the endpoint *and*
  `IAuthorizeRequest` on the command (BillingApi declared both). Idempotency was a header check in 14 and
  deduplication in 05, with the error code duplicated and a drift test holding them together.
- A typical CQRS HTTP service took 5–6 packages and about eight registration calls with ordering traps
  (`AddMediatR`, validators, `AddSharedKernelApplication`, `AddSharedKernelApplicationBehaviors()…Build()`, which
  throws when a seam is registered after it).
- The public surfaces were wide (about 430 API lines across 05 and WebApi) and the documentation long.

## Decisions

| # | Decision |
| --- | --- |
| A1 | MediatR 12.4.1 stays the engine and stays visible: services use `ISender`, and the platform's `ICommand`/`IQuery`/handler interfaces derive from MediatR's. No facade. |
| A2 | Authorization is declared once, on the use case: `[RequirePermission("a", "b")]` on a command or query (values in one attribute are alternatives, several attributes all apply — the same semantics as 14's attribute). `AuthorizationBehavior` enforces it on every path (HTTP, messages, jobs, workflows). `IAuthorizeRequest` and `PermissionMatch` are removed. 14's attributes and conventions stay for what has no command: hubs, gRPC, endpoints that send nothing, and authentication strength (`RequireFreshAuthentication`, `RequireAuthenticationMethod`), which is an HTTP concern. An endpoint that sends a command does not repeat the command's permissions. |
| A3 | 05.Application becomes three packages: `SharedKernel.Application.Abstractions` (unchanged role: the contracts persistence implements, no MediatR), `SharedKernel.Application` (the vocabulary, the pipeline and every behavior — the former `.Application` and `.Application.Behaviors` merged), and `SharedKernel.Application.Caching` (the former `.Application.Behaviors.Caching`, renamed). `SharedKernel.Application.Behaviors` and `SharedKernel.Application.Behaviors.Caching` stop existing. |
| A4 | One registration call, no `Build()`, no ordering requirement (below). |
| A5 | Behavior classes become internal; the public surface is the contracts a service writes against. |
| A6 | The idempotency error codes move to `01.Core`'s `ErrorCodes.Idempotency` (`KeyRequired`, `KeyInvalid`, `InProgress`, `KeyReused`); `IdempotencyErrorCodes` and the drift test go. |
| P1 | 14.Presentation stays independent of 05: WebApi references no MediatR. The command/query pattern is the single path shown in the READMEs and in all five samples. |
| P2 | Endpoint modules: `IEndpointModule` in WebApi, discovered at compile time by a source generator shipped inside the WebApi package; `app.MapEndpoints()` maps them all. |
| P3 | The WebApi public API is trimmed and flattened (below). |
| P4 | Paging input binds as endpoint parameters (`Paging`, `CursorPaging`) and answers 400 with the `pagination.*` codes before the handler runs. |
| S1 | All five samples use endpoint modules and send commands and queries through `ISender`, including ShippingApi, DocumentsApi and CatalogApi, which called their dependencies from the endpoint. |

## Target: 05.Application

### Registration

```csharp
builder.Services.AddSharedKernelApplication(typeof(Program).Assembly, app => app
    .WithAuthorization()      // needs IRequestContext
    .WithIdempotency()        // needs IRequestIdempotencyStore + IRequestContext
    .WithTransactions()       // needs IUnitOfWork
    .WithAuditing()           // needs IAuditTrailWriter
    .WithCaching()            // SharedKernel.Application.Caching; needs ICacheService + its key provider
    .WithBehavior(typeof(MyBehavior<,>), PipelineStage.Command));
```

- Always registered: MediatR with the handlers of the given assemblies (`params Assembly[]`), the FluentValidation
  validators of the same assemblies (FluentValidation's own `AssemblyScanner`; no new package), the domain-event
  bridge, `ICommandScope`, and the Tracing, Logging, Metrics and Validation behaviors.
- The `With*` behaviors are opt-in, as before, and always land in the canonical order.
- A missing seam is reported at host start (options `ValidateOnStart`), naming every missing service at once. The
  seam may be registered before or after `AddSharedKernelApplication`; nothing throws at registration time.
- Calling `AddSharedKernelApplication` twice throws, naming the fix.
- `AddDomainEventHandler<TEvent, THandler>()` stays.

### Public surface, by namespace

- `SharedKernel.Application` — what every service writes against: `ICommand`, `ICommand<T>`, `IQuery<T>`,
  `ICommandBase`, `IQueryBase`, `ICommandHandler<…>`, `IQueryHandler<…>`, `IStreamQuery<T>`,
  `IStreamQueryHandler<…>`, `RequirePermissionAttribute`, `IIdempotentRequest`, `IAuditableRequest<TResponse>`,
  `ILoggableRequest<TResponse>`, `ICommandScope`, `PipelineStage`, the registration extension and its builder,
  `ApplicationLoggingOptions`, `IDomainEventHandler<T>`, `DomainEventNotification<T>`.
- `SharedKernel.Application.Idempotency` — for store implementers (18.Idempotency): `IRequestIdempotencyStore`,
  `IdempotencyBeginResult`, `IdempotencyBeginStatus`.
- `SharedKernel.Application.Context`, `.Transactions`, `.Auditing` — unchanged, in `.Abstractions`. The type
  forwarders from `SharedKernel.Application` to `.Abstractions` are removed (the namespaces do not change).
- `SharedKernel.Application.Caching` — `ICacheableQuery<T>`, `IInvalidatesCache`, `CacheKeyRef`, `CacheScope`,
  `WithCaching()`.
- Internal: every behavior class, `MediatRDomainEventDispatcher` (registered by the call), the metrics type.

### Unchanged semantics

The pipeline order and every behavior's behavior (short-circuit with `Result`, fail closed, transaction,
command scope, audit halves, idempotency per tenant and caller, caching partitions) do not change. Only
authorization's declaration changes (A2). `SK0040` follows: a request carrying `[RequirePermission]` or
`IIdempotentRequest` must return `Result`/`Result<T>`.

## Target: 14.Presentation WebApi

### Namespace

Everything a service uses is in `SharedKernel.Presentation.WebApi`, options included. The sub-namespaces
`.Errors`, `.Http`, `.Idempotency` and `.Options` go.

### Public surface after the trim

- Setup: `AddSharedKernelWebApi`, `UseSharedKernelWebApi`, `WebApiPipeline`, the options classes.
- Results: `ToOk`, `ToOkWithETag`, `ToCreated`, `ToAccepted`, `ToNoContent`, `ToHttpResult`, `ToErrorResult`,
  `ToProblemDetails`, `ErrorHttpResult`, `OkWithETag<T>`.
- Endpoints: `IEndpointModule` and the generated `MapEndpoints()`.
- Authorization: the four attributes and their conventions.
- Headers: `IdempotencyKey`, `IfMatch<TVersion>`, the `Require`/`Accept` attributes and conventions for both,
  `GetIdempotencyKey()`, `GetIfMatch()`, `SetETag()`, `GetCorrelationId()`.
- Paging: `Paging`, `CursorPaging`.
- Limits and CSP: `WithRequestSizeLimit`, `DisableRequestSizeLimit`, `WithContentSecurityPolicy`.
- The error contract: `PresentationErrorCodes` (constants), `ProblemDetailsExtensionNames`.
- Internal, visible to the OpenApi, SignalR and Grpc add-ons (`InternalsVisibleTo`; the packages version in
  lockstep): `ErrorPresentation`, `ErrorTypeStatusCodeMap`, `PresentationErrorCodes.ForStatus`,
  `AddSharedKernelAuthorization`, the header and ETag metadata interfaces.
- Removed: `GetIfMatchTags()`.

### Endpoint modules

```csharp
public sealed class CustomerEndpoints : IEndpointModule
{
    public static void Map(IEndpointRouteBuilder app) { … }
}

app.MapEndpoints();   // generated: every IEndpointModule of this assembly, in type-name order
```

- `IEndpointModule` declares `static abstract void Map(IEndpointRouteBuilder app)`; no instance, no reflection.
- The generator (`netstandard2.0`, packed under `analyzers/dotnet/cs` of the WebApi package) emits an internal
  `MapEndpoints(this IEndpointRouteBuilder)` extension into the assembly that declares modules, and diagnostics
  for a module it cannot call (abstract, generic, inaccessible).

### Paging parameters

```csharp
invoices.MapGet("/", (Paging paging, ISender sender, CancellationToken ct) =>
    sender.Send(new ListInvoices(paging.Request), ct).ToOk());
```

`Paging` binds `page` and `pageSize` from the query into a validated `PageRequest` (04.Contracts), `CursorPaging`
binds `cursor` and `limit` into a `CursorPageRequest`. Invalid input answers 400 before the handler, with the
`pagination.*` codes in `errorCodes`; OpenAPI documents the query parameters.
