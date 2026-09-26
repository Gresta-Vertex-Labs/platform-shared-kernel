# SharedKernel.Application.Mediator.MediatR

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![License: MIT](https://img.shields.io/badge/license-MIT-blue)
![Tier: Host](https://img.shields.io/badge/tier-Host-5c6bc0)
![MediatR 12.4.1](https://img.shields.io/badge/MediatR-12.4.1%20(last%20MIT)-5c6bc0)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

**MediatR as the transport behind `SharedKernel.Application`'s `ISender` — one builder call, `UseMediatR()`, on the
application registration.**

This is the only SharedKernel package that references MediatR, pinned to 12.4.1, the last MIT-licensed release
(v13 and later are commercially licensed). Commands, queries, handlers and behaviors are written against
[`SharedKernel.Application`](../SharedKernel.Application/README.md), so replacing MediatR means another `ISender`
implementation plugged in the same way and changes no application code. `00.Governance`'s
`DependencyGraphRulesTests.MediatR_IsReferencedOnlyByTheMediatorAdapter` keeps it that way.

## Contents

- [Install](#install)
- [Registration](#registration)
- [Sending](#sending)
- [What `UseMediatR` registers](#what-usemediatr-registers)
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
using SharedKernel.Application.Pipeline;

builder.Services.AddSharedKernelApplication(typeof(PlaceOrderHandler).Assembly, app => app
    .UseMediatR()          // this package: the kernel ISender over MediatR
    .WithTransactions());  // any opt-in behaviors (SharedKernel.Application.Pipeline)
```

That is the whole registration. `AddSharedKernelApplication` (`SharedKernel.Application.Pipeline`) finds the
handlers, validators and domain-event handlers of the given assemblies and registers the behaviors;
`UseMediatR()` registers the transport for every request type whose handler it found. Without a mediator the host
start fails naming `ISender`, because nothing could send a command.

## Sending

Inject the kernel `ISender` (`SharedKernel.Application.Messaging`), never MediatR's `IMediator`/`ISender`:

```csharp
orders.MapPost("/", (PlaceOrderCommand command, ISender sender, CancellationToken ct) =>
    sender.Send(command, ct).ToCreated(id => $"/orders/{id}"));

orders.MapGet("/export", (ISender sender, CancellationToken ct) =>
    sender.CreateStream(new ExportOrdersQuery(DateOnly.MinValue), ct));
```

`Send` runs `RequestPipeline<TRequest, TResponse>` — every registered `IPipelineBehavior<TRequest, TResponse>`,
outermost first, then the handler. `CreateStream` runs `StreamRequestPipeline<,>` with the stream behaviors
(`[RequirePermission]` is checked there too). The sender is transient, so a handler that sends a nested command stays
in the caller's DI scope and shares its `ICommandScope` (only the outermost command commits).

## What `UseMediatR` registers

| Registration | Lifetime | Notes |
| --- | --- | --- |
| MediatR itself | — | Once, scanning only this adapter's assembly; skipped if `IMediator` is already registered |
| `ISender` → internal `MediatRSender` | Transient | `TryAdd`, so a test can supply its own |
| Per request type of the scanned assemblies | Transient + keyed singleton | An internal MediatR envelope handler that runs the kernel pipeline, and a dispatcher keyed by the request type |
| Per streaming-query type | Transient + keyed singleton | The stream counterpart |

The handlers themselves, `RequestPipeline<,>`, the domain-event dispatcher and the behaviors are registered by
`AddSharedKernelApplication`. MediatR's own pipeline stays empty; the kernel pipeline runs inside the envelope
handler. Reflection is used at registration time only: a send resolves the dispatcher registered for its request type
and makes no reflective call. Calling `UseMediatR()` twice is harmless.

## Pitfalls

**Implementing MediatR's interfaces.** A class implementing `MediatR.IRequestHandler` or `INotificationHandler` is
not discovered. Implement the kernel interfaces from `SharedKernel.Application`.

**Registering behaviors on MediatR.** `cfg.AddBehavior(...)`/`AddOpenBehavior(...)` on MediatR's own pipeline run
outside the kernel pipeline and its fixed order. Use `app.WithBehavior(type, stage)` instead.

**Forgetting an assembly.** Only the assemblies passed to `AddSharedKernelApplication` are searched. Sending a
request whose handler lives elsewhere throws `InvalidOperationException` naming the request type.

**Bumping MediatR.** 13.x is commercially licensed and this repository declares MIT. The pin is deliberate.

## Package

| | |
| --- | --- |
| **Tier** | Host |
| **Depends on** | `SharedKernel.Application`, `SharedKernel.Application.Pipeline`, `MediatR` 12.4.1 |
| **Public API** | `MediatRServiceCollectionExtensions.UseMediatR(this ApplicationPipelineBuilder)` — tracked in `PublicAPI.*.txt` |
| **Target** | `net10.0` |

Before P-579 the entry point was `services.AddSharedKernelMediatR(assemblies)`, which also scanned the handlers; the
scan moved to `AddSharedKernelApplication` so one call registers the application layer whatever the mediator.

Related: [`SharedKernel.Application`](../SharedKernel.Application/README.md) (contracts),
[`SharedKernel.Application.Pipeline`](../SharedKernel.Application.Pipeline/README.md) (registration and behaviors),
[`SharedKernel.Application.Pipeline.Caching`](../SharedKernel.Application.Pipeline.Caching/README.md) (query
caching). Maintainer rules live in [`05.Application/CLAUDE.md`](../CLAUDE.md).
