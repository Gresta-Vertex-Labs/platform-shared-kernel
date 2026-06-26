---
name: reference_upstream_contracts
description: Exact confirmed API shapes of the four upstream surfaces 15.Integration depends on, as verified by direct source read on 2026-06-26
type: reference
---

Confirmed by direct source read during WO-032's P-200 Design lock (2026-06-26) — not from CLAUDE.md prose, which can drift. Re-verify by reading these files again if a future phase's assumptions feel stale; do not assume these stay true indefinitely.

**`SharedKernel.Primitives`** (`01.Core\SharedKernel.Primitives\Results\Result.cs`, `...\Errors\Error.cs`):
`Result<T>` (namespace `SharedKernel.Primitives.Results`) and `Error` (namespace `SharedKernel.Primitives.Errors`, `public sealed record Error(string Code, string Message, ErrorType Type)` with `Error.None`) both exist as expected. Neither is consumed by `15.Integration`'s public surface — `WebhookDeliveryResult` is its own domain-specific outcome shape, not a `Result<T>` wrapper. The `SharedKernel.Primitives` ProjectReference exists for future-proofing only.

**`SharedKernel.Configuration`** (`01.Core\SharedKernel.Configuration\Extensions\OptionsExtensions.cs`):
Only API is `AddValidatedOptions<TOptions>(this IServiceCollection, IConfigurationSection) where TOptions : class` → `.Bind(section).ValidateDataAnnotations().ValidateOnStart()`. DataAnnotations-based. No `IValidateOptions<T>` contract, no `[ValidateOptions]` attribute, no standalone validator base class exists in this package despite what some CLAUDE.md prose elsewhere implies.

**`SharedKernel.Contracts`** (`04.Contracts\SharedKernel.Contracts\Events\IIntegrationEvent.cs`, `...\EventEnvelope.cs`):
`IIntegrationEvent` (namespace `SharedKernel.Contracts.Events`) is exactly:
```csharp
public interface IIntegrationEvent
{
    Guid EventId { get; }
    DateTimeOffset OccurredOn { get; }
}
```
Two members only. `EventEnvelope<TEvent>` (sealed record, constrains `where TEvent : IDomainEvent` — note: `IDomainEvent` from `03.Domain`, NOT `IIntegrationEvent`) has `EventId`, `OccurredOn`, `EventType`, `EventVersion`, `CorrelationId`, `CausationId`, `SourceService`, `Payload`. `.EventType` is derived as `typeof(TEvent).Name` inside the static `EventEnvelope.Wrap<TEvent>` factory (line ~205) — confirms the convention `IWebhookDispatcher` mirrors, but it is a convention parallel only, not a shared generic constraint (different constraining interfaces).

**`SharedKernel.Messaging.Abstractions`** (`07.Messaging\SharedKernel.Messaging.Abstractions\EventPublisher\IEventPublisher.cs`):
```csharp
public interface IEventPublisher
{
    Task PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken ct) where TEvent : class;
    Task PublishAsync<TEvent>(TEvent integrationEvent, Action<PublishContext> configure, CancellationToken ct) where TEvent : class;
}
```
Both overloads constrain only `where TEvent : class` — no `IIntegrationEvent`/`IDomainEvent` requirement. Implementing `IIntegrationEvent` on `WebhookDeliveryExhaustedEvent` is a cross-domain convention choice (uniform `EventId`/`OccurredOn` shape across the platform), not something `IEventPublisher.PublishAsync` itself demands.
