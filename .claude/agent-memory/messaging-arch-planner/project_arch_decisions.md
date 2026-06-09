---
name: project-arch-decisions
description: Critical architecture decisions, package boundary rules, and MassTransit 9.x API discoveries for the 07.Messaging domain
metadata:
  type: project
---

## Package Split Rules
- `SharedKernel.Messaging.Abstractions` — zero transport NuGet dependencies; only `Microsoft.Extensions.DependencyInjection.Abstractions`. All interfaces and option POCOs that are transport-agnostic live here.
- `SharedKernel.Messaging.MassTransit` — all MassTransit wiring, transport adapters, consumer base, builder, DI extensions. No reference to `SharedKernel.Persistence.*` packages — outbox wired via generic `TDbContext` type parameter only.
- `AddSharedKernelMessaging` extension method and `MessagingBusBuilder` live in the MassTransit package (not Abstractions), because Abstractions is a zero-DI-extension library.
- `MessagingOptionsValidator` (IValidateOptions<MessagingOptions>) lives in MassTransit package — Abstractions cannot reference `Microsoft.Extensions.Options`.

## MassTransit 9.x API Discoveries (critical — do not assume 8.x behavior)
- Package is `MassTransit` 9.1.2 (not 8.x as initially documented)
- EF Core outbox package: `MassTransit.EntityFrameworkCore` (NOT `MassTransit.EntityFrameworkCoreIntegration`)
- Test package: `MassTransit.TestFramework` 9.1.2 (NOT `MassTransit.Testing`)
- `IClientFactory.CreateRequestClient<T>(CancellationToken)` does not exist in 9.x — use `IServiceProvider.CreateRequestClient<T>()` from `MassTransit.RequestClientExtensions`
- `IServiceBusBusFactoryConfigurator` has no `TransportType` — set it on `IServiceBusHostConfigurator` instead
- `BindConfiguration` not available (no ConfigurationExtensions transitive dep) — bind config manually
- `ConsumerBase.Consume` is interface implementation, not virtual override — `sealed` keyword does not apply
- `KebabCaseEndpointNameFormatter` strips `Consumer` suffix from consumer type names in 9.x
- `IConsumerTestHarness<T>.Consumer.InputAddress` does not exist in 9.x

## Test Discoveries (critical for test authoring)
- Never use `file` modifier on consumer/message/DbContext types in tests — MassTransit splits on `<` in mangled CLR names
- Always `await using var sp = services.BuildServiceProvider(...)` — MassTransit 9.x `UsageTracker` is `IAsyncDisposable` only
- SQLite outbox: do NOT start the delivery worker in row-insertion tests — SQLite cannot handle `RepeatableRead` nested transactions
- Outbox rows are written DURING `SaveChangesAsync`, not before
- `PublishContext` name conflict in MassTransit package — alias as `MessagingPublishContext`

## Outbox Ownership (hard boundary)
- OutboxMessage, IOutboxWriter, and all outbox types are owned by MassTransit in 07.Messaging
- Must NEVER be defined in 06.Persistence
- Consuming service bridges at its own composition root by passing its DbContext as a generic type parameter

## Scoped Lifetime Enforcement
- `IMessageBus` and `IEventPublisher` must always be registered as scoped — never singleton
- Singleton lifetime breaks MassTransit's per-consume-scope semantics

## Build() Anti-Pattern (P-130 — critical fix)
- Build() must NEVER call `Services.BuildServiceProvider()` — creates second root IServiceProvider, double-registers singletons
- Fix: capture `Action<MessagingOptions>?` delegate at `AddSharedKernelMessaging()` time; apply inline to local instance for validation; no DI container construction
- Deferred path: when options bound from config section (no inline action), Build() does not throw; defers to `ValidateOnStart()`

## CloudEvents Envelope Fields
- CorrelationId: Activity.Current?.TraceId as Guid if available; else Guid.NewGuid()
- SourceService: MessagingOptions.ServiceName from IOptions
- SchemaVersion: DomainEventVersionHelper.GetVersion(typeof(TEvent)); defaults to 1 when [DomainEventVersion] absent
- MassTransitEventPublisher uses reflection-cached delegate (ConcurrentDictionary<Type,Delegate>) to bridge `where TEvent:class` (IEventPublisher) to `where TEvent:IDomainEvent` (EventEnvelope<TEvent>)

## Domain Event Boundary (never violate)
- IEventPublisher is for integration events only — events that cross service boundaries
- Domain events (IDomainEvent) dispatched by IDomainEventDispatcher (03.Domain)
- Correct flow: domain event → IDomainEventDispatcher → application handler → IEventPublisher
- Never call IMessageBus or IEventPublisher from domain entities, value objects, or AggregateRoot

Related: [[project-messaging-domain]]
