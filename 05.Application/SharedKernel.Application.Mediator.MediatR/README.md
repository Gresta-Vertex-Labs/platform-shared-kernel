# SharedKernel.Application.Mediator.MediatR

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![License: MIT](https://img.shields.io/badge/license-MIT-blue)
![Tier: Host](https://img.shields.io/badge/tier-Host-5c6bc0)
![MediatR 12.4.1](https://img.shields.io/badge/MediatR-12.4.1%20(last%20MIT)-5c6bc0)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

**MediatR as the transport behind `SharedKernel.Application`'s `ISender` — one registration call that finds your
handlers and routes every send through the kernel request pipeline.**

This is the only SharedKernel package that references MediatR, pinned to 12.4.1, the last MIT-licensed release
(v13 and later are commercially licensed). Commands, queries, handlers and behaviors are written against
[`SharedKernel.Application`](../SharedKernel.Application/README.md), so replacing MediatR means another `ISender`
implementation and changes no application code. `00.Governance`'s
`DependencyGraphRulesTests.MediatR_IsReferencedOnlyByTheMediatorAdapter` keeps it that way.

## Contents

- [Install](#install)
- [Registration](#registration)
- [Sending](#sending)
- [What `AddSharedKernelMediatR` registers](#what-addsharedkernelmediatr-registers)
- [Discovery rules](#discovery-rules)
- [Pitfalls](#pitfalls)
- [Package](#package)

## Install

Reference it from the composition-root (API or worker) project only — never from an application-layer project,
which needs just `SharedKernel.Application`:

```xml
<PackageReference Include="SharedKernel.Application.Mediator.MediatR" />
<PackageReference Include="SharedKernel.Application.Pipeline" />
```

Versions come from your single `SharedKernelVersion` property (the repository's `PLATFORM.md`, "Consuming the
kernel"). MediatR 12.4.1 arrives transitively; do not add a newer MediatR yourself.

## Registration

```csharp
using SharedKernel.Application.Mediator.MediatR;
using SharedKernel.Application.Pipeline.Extensions;

// 1. The transport, the kernel ISender, and every handler in the given assemblies.
builder.Services.AddSharedKernelMediatR(
    typeof(PlaceOrderHandler).Assembly,          // your application-layer assembly
    typeof(OrderPlacedEmailHandler).Assembly);   // any other assembly declaring handlers

// 2. The behaviors, in their fixed stage order (SharedKernel.Application.Pipeline).
builder.Services.AddSharedKernelApplicationBehaviors()
    .AddDefaultBehaviors()                        // Tracing, Logging, Metrics, Validation
    .Build();
```

`AddSharedKernelMediatR` registers no behavior. Without the second call every send still reaches its handler,
through an empty kernel pipeline. The two calls can be made in either order.

Domain events need nothing extra: `AddSharedKernelMediatR` calls `AddSharedKernelDomainEvents()` for you, which
registers the native `DomainEventDispatcher` as `03.Domain`'s `IDomainEventDispatcher`, and it discovers every
`IDomainEventHandler<T>` in the scanned assemblies. `06.Persistence`'s `SharedKernelDbContext` dispatches raised
events through that dispatcher on save. A handler outside the scanned assemblies is added with
`services.AddDomainEventHandler<TEvent, THandler>()`.

## Sending

Inject the kernel `ISender` (`SharedKernel.Application.Messaging`), never MediatR's `IMediator`/`ISender`:

```csharp
app.MapPost("/orders", async (PlaceOrderCommand command, ISender sender, CancellationToken ct) =>
    (await sender.Send(command, ct)).ToProblemDetailsResult());

app.MapGet("/orders/export", (ISender sender, CancellationToken ct) =>
    sender.CreateStream(new ExportOrdersQuery(DateOnly.MinValue), ct));
```

`Send` runs `RequestPipeline<TRequest, TResponse>` — every `IPipelineBehavior<TRequest, TResponse>` registered by
`ApplicationBehaviorsBuilder`, outermost first, then the handler. `CreateStream` runs `StreamRequestPipeline<,>`
with any `IStreamPipelineBehavior<,>` you registered. The sender is transient, so a handler that sends a nested
command stays in the caller's DI scope and shares its `ICommandScope` (only the outermost command commits).

## What `AddSharedKernelMediatR` registers

| Registration | Lifetime | Notes |
| --- | --- | --- |
| MediatR itself | — | Once, scanning only this adapter's assembly; skipped if `IMediator` is already registered |
| `ISender` → internal `MediatRSender` | Transient | `TryAdd`, so a test can supply its own |
| `RequestPipeline<,>`, `StreamRequestPipeline<,>` | — | Via `AddSharedKernelRequestPipeline()` (`SharedKernel.Application.Pipeline`) |
| `IDomainEventDispatcher` → `DomainEventDispatcher` | Scoped | Via `AddSharedKernelDomainEvents()` |
| Each discovered `IRequestHandler<,>` (every `ICommandHandler`/`IQueryHandler`) | Transient | Plus, per request type, an internal MediatR envelope handler that runs the kernel pipeline and a keyed dispatcher singleton |
| Each discovered `IStreamQueryHandler<,>` | Transient | Plus the stream counterpart of the envelope |
| Each discovered `IDomainEventHandler<>` | Scoped | `TryAddEnumerable`, so a handler never runs twice |

MediatR's own pipeline stays empty; the kernel pipeline runs inside the envelope handler. The call is safe to
repeat, with the same or other assemblies. Reflection is used at registration time only: a send resolves the
dispatcher registered for its request type and makes no reflective call.

## Discovery rules

- Discovered: every non-abstract, non-generic class, public or internal, implementing the kernel
  `IRequestHandler<,>`, `IStreamQueryHandler<,>` or `IDomainEventHandler<>`.
- A handler already registered by hand for a request type — a factory, an instance or the same type — is kept, so
  a test or composition root can supply its own.
- Two **different** handler types for one request type throw `InvalidOperationException` at registration.
- Sending a request whose handler was not found throws `InvalidOperationException` naming the request type.
- No assemblies at all throws `ArgumentException`.

## Pitfalls

**Implementing MediatR's interfaces.** A class implementing `MediatR.IRequestHandler` or `INotificationHandler` is
not discovered. Implement the kernel interfaces from `SharedKernel.Application`.

**Registering behaviors on MediatR.** `cfg.AddBehavior(...)`/`AddOpenBehavior(...)` on MediatR's own pipeline run
outside the kernel pipeline and its fixed order. Register behaviors with
`AddSharedKernelApplicationBehaviors().AddBehavior(type, stage)` instead.

**Forgetting an assembly.** Only scanned assemblies are searched. A request whose handler lives elsewhere fails
at its first send — pass every assembly that declares handlers.

**Bumping MediatR.** 13.x is commercially licensed and this repository declares MIT. The pin is deliberate.

## Package

| | |
| --- | --- |
| **Tier** | Host |
| **Depends on** | `SharedKernel.Application`, `SharedKernel.Application.Pipeline`, `MediatR` 12.4.1, `Microsoft.Extensions.DependencyInjection.Abstractions` |
| **Public API** | `MediatRServiceCollectionExtensions.AddSharedKernelMediatR(this IServiceCollection, params Assembly[])` — tracked in `PublicAPI.*.txt` |
| **Target** | `net10.0` |

Related: [`SharedKernel.Application`](../SharedKernel.Application/README.md) (contracts),
[`SharedKernel.Application.Pipeline`](../SharedKernel.Application.Pipeline/README.md) (behaviors),
[`SharedKernel.Application.Pipeline.Caching`](../SharedKernel.Application.Pipeline.Caching/README.md) (query
caching). Maintainer rules live in [`05.Application/CLAUDE.md`](../CLAUDE.md).
