# SharedKernel.Application.Mediator.MediatR

MediatR as the transport behind `SharedKernel.Application`'s `ISender`. This is the only SharedKernel
package that references MediatR (pinned to 12.4.1, the last MIT-licensed release).

```csharp
builder.Services.AddSharedKernelMediatR(typeof(PlaceOrderHandler).Assembly);

builder.Services
    .AddSharedKernelApplicationBehaviors()   // SharedKernel.Application.Pipeline
    .AddDefaultBehaviors()
    .Build();
```

`AddSharedKernelMediatR(assemblies)` finds every kernel handler in the assemblies —
`ICommandHandler`/`IQueryHandler` (any `IRequestHandler<,>`), `IStreamQueryHandler<,>` and
`IDomainEventHandler<>` — and registers, per request type, a MediatR handler that runs the kernel
`RequestPipeline<TRequest, TResponse>`: the behaviors registered by `ApplicationBehaviorsBuilder`, in
their fixed stage order, then the handler. It also registers the native `DomainEventDispatcher`.

Application code depends only on `SharedKernel.Application` (`ICommand`, `IQuery`, `ISender`,
`IPipelineBehavior`, …). Replacing MediatR means another `ISender` implementation; no command, query,
handler or behavior changes.

- It registers no behaviors; MediatR's own pipeline stays empty.
- A request can be sent only when its handler was found in a scanned assembly; anything else throws
  `InvalidOperationException` naming the request type.
- A class implementing MediatR's `IRequestHandler` or `INotificationHandler` is not discovered —
  implement the kernel interfaces.
