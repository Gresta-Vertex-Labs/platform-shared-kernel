# SharedKernel.Application.Behaviors

Opt-in MediatR pipeline behaviors for Platform.SharedKernel microservices: Validation, Logging, Metrics, Transaction, Caching, Authorization, and Idempotency, composed in a fixed canonical order via `ApplicationBehaviorsBuilder`. References `SharedKernel.Application`, `SharedKernel.Primitives`, `SharedKernel.Core`, `SharedKernel.Caching.Abstractions`, `MediatR`, and `FluentValidation` — never `06.Persistence`, `07.Messaging`, or `12.Security`.

This package ships **no MediatR registration of its own** — the consuming service already registers MediatR; `ApplicationBehaviorsBuilder` only appends behaviors to the already-registered pipeline.

## Canonical pipeline order (non-negotiable)

```text
1. LoggingBehavior        ← outermost
2. MetricsBehavior
3. ValidationBehavior
4. AuthorizationBehavior  ← commands AND queries
5. CachingBehavior        ← queries only (ICacheableQuery<TResponse>)
6. IdempotentCommandBehavior  ← commands only (ICommandBase, IIdempotentRequest)
7. TransactionBehavior    ← commands only (ICommandBase); innermost
```

`ApplicationBehaviorsBuilder.Build()` always registers behaviors in this order, regardless of the order `.AddXBehavior()` was called in.

## The local-seam bridging pattern

`TransactionBehavior` (`IUnitOfWork`), `AuthorizationBehavior` (`IAuthorizationContext`), and `IdempotentCommandBehavior` (`IIdempotencyKeyStore`) each define a **minimal interface owned by this package** — never a direct reference to the "real" infrastructure (`06.Persistence`, `12.Security`, `07.Messaging` respectively, none of which this package may reference). The consuming service bridges each local seam to its real implementation at the composition root. This is the same pattern applied three times, not three different patterns.

## Install

```xml
<ProjectReference Include="..\SharedKernel.Application.Behaviors\SharedKernel.Application.Behaviors.csproj" />
```

Or, once published, reference the NuGet package `SharedKernel.Application.Behaviors`.

## Quick Start — full seven-behavior registration

```csharp
// Composition root — MediatR registration is NOT this package's responsibility
services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<Program>());
services.AddValidatorsFromAssemblyContaining<Program>();   // FluentValidation's own scanning, not ours

services.AddSharedKernelApplication();   // 05.Application's domain-event bridge

// Opt-in pipeline behaviors — fixed execution order regardless of call order
services
    .AddSharedKernelApplicationBehaviors()
    .AddLoggingBehavior()
    .AddMetricsBehavior()
    .AddValidationBehavior()
    .AddAuthorizationBehavior()    // requires IAuthorizationContext registered (see below)
    .AddCachingBehavior()          // requires SharedKernel.Caching.Abstractions.ICacheService registered
    .AddIdempotencyBehavior()      // requires IIdempotencyKeyStore registered (see below)
    .AddTransactionBehavior()      // requires SharedKernel.Application.Behaviors.IUnitOfWork registered (see below)
    .Build();
```

`Build()` throws `InvalidOperationException` at registration time if `.AddTransactionBehavior()`, `.AddCachingBehavior()`, `.AddAuthorizationBehavior()`, or `.AddIdempotencyBehavior()` was called without its required dependency already registered in `IServiceCollection`.

## Bridging the local seams at the composition root

```csharp
// IUnitOfWork — bridge this package's minimal interface to 06.Persistence's concrete IUnitOfWork.
// Never reference 06.Persistence directly from inside SharedKernel.Application.Behaviors itself.
services.AddScoped<SharedKernel.Application.Behaviors.IUnitOfWork>(sp =>
    new EfUnitOfWorkAdapter(sp.GetRequiredService<SharedKernel.Persistence.Abstractions.IUnitOfWork>()));

// IAuthorizationContext — bridge to 12.Security's real IUserContext/ITenantProvider.
services.AddScoped<SharedKernel.Application.Behaviors.IAuthorizationContext>(sp =>
    new UserContextAuthorizationAdapter(sp.GetRequiredService<SharedKernel.Security.Abstractions.IUserContext>()));

// IIdempotencyKeyStore — the consuming service supplies its own implementation
// (e.g. backed by the same distributed store 07.Messaging's IIdempotencyStore uses,
// or a dedicated table/cache key). Never a 07.Messaging reference from this package.
services.AddScoped<SharedKernel.Application.Behaviors.IIdempotencyKeyStore, RedisIdempotencyKeyStore>();
```

## Declaring requests that opt into a behavior

```csharp
// A cacheable query
public sealed record GetOrderByIdQuery(Guid OrderId)
    : IQuery<OrderDto>, ICacheableQuery<Result<OrderDto>>
{
    public CachePolicy CachePolicy => CachePolicy.Default;
    public string CacheKey => $"orders:{OrderId:D}";
}

// An authorized, idempotent command
public sealed record PlaceOrderCommand(string IdempotencyKey, Guid CustomerId, decimal Total)
    : ICommand<Guid>, IAuthorizeRequest, IIdempotentRequest
{
    public string Requirement => "orders:create";
}
```

Requests that do not implement `ICacheableQuery<TResponse>` / `IAuthorizeRequest` / `IIdempotentRequest` simply never resolve the corresponding behavior into their pipeline — this is a DI-level fact, not a runtime branch.

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) — see [05.Application/CLAUDE.md](../CLAUDE.md) for the full interface contracts, hard violations, and AOT notes.
