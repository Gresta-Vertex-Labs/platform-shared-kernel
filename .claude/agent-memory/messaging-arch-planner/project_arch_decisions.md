---
name: project-arch-decisions
description: Critical architecture decisions, package boundary rules (post-WO-086 tiers), MassTransit API discoveries, and the P-254 logging/EventId allocation for the 07.Messaging domain
metadata:
  type: project
---

> WO-086 (2026-09): the 07→05 grant is gone — caller identity is `SharedKernel.Execution` (`PropagatedRequestContext`); transports/outbox moved to satellites `.RabbitMq`/`.AzureServiceBus`/`.EfCore`; `IMessageBusProbe` was deleted (the `"messaging"` `IReadinessProbe`). Routing slips, sagas and `RequestAsync` were removed earlier (P-560).

## Package Split Rules (current)
- `SharedKernel.Messaging.Abstractions` — Abstractions tier: references Foundation/Model only (`SharedKernel.Primitives`, `SharedKernel.Execution`, `SharedKernel.Contracts`), third-party limited to `Microsoft.Extensions.*.Abstractions` (SKTIER003). All transport-agnostic interfaces and option POCOs live here.
- `SharedKernel.Messaging.MassTransit` — Adapter tier: MassTransit core wiring, consumer base, builder, DI extensions, idempotency (`IIdempotencyStore` keyed `IdempotencyPurpose.Message`, from `SharedKernel.Idempotency.Abstractions`) and request-context filters, the `"messaging"` `IReadinessProbe`.
- Satellites `SharedKernel.Messaging.MassTransit.RabbitMq` / `.AzureServiceBus` (transports) and `.EfCore` (outbox) — Adapter tier, each with the one declared adapter edge → `SharedKernel.Messaging.MassTransit`.
- No `SharedKernel.Messaging.*` package references `06.Persistence.*` (outbox via generic `TDbContext` only), `SharedKernel.Caching.*` (and back), `SharedKernel.Application`/`.Application.Pipeline`, or MediatR. The build enforces the tiers (SKTIER001–006 errors) — see root `CLAUDE.md` "Tiers & Dependency Rules".
- `AddSharedKernelMessaging` extension method and `MessagingBusBuilder` live in the MassTransit package (not Abstractions), because Abstractions is a zero-DI-extension library.
- `MessagingOptionsValidator` (IValidateOptions<MessagingOptions>) lives in MassTransit package — Abstractions does not reference `Microsoft.Extensions.Options`.

## MassTransit API Discoveries
> Recorded against MassTransit 9.1.2; the platform is now pinned to **8.5.x** (last Apache-2.0 line, P-560). Re-verify each item against 8.5.x before relying on it.
- EF Core outbox package: `MassTransit.EntityFrameworkCore` (NOT `MassTransit.EntityFrameworkCoreIntegration`)
- Test package: `MassTransit.TestFramework` (NOT `MassTransit.Testing`)
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
- OutboxMessage, IOutboxWriter, and all outbox types are owned by MassTransit in 07.Messaging (`SharedKernel.Messaging.MassTransit.EfCore`)
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

## OTel ActivitySource Ownership (P-172, WO-027)

- `MessagingDiagnostics.ActivitySource` ("SharedKernel.Messaging", "1.0.0") is created and owned in `SharedKernel.Messaging.MassTransit` — BCL `System.Diagnostics`, zero new NuGet deps
- A static `readonly ActivitySource` is an explicit, documented exception to the "no static mutable state" hard rule — it's the platform-standard .NET diagnostics instrument pattern (same shape as a static logger category/Meter), carries no mutable business state
- `13.ServiceDefaults` NEVER creates an `ActivitySource` or `Meter` on behalf of another domain — it only registers already-existing source names with the host's `TracerProvider`/`MeterProvider`. P-132 (pending, 13.ServiceDefaults) had wrongly assumed this source already existed in 07.Messaging; P-172 fixes the false premise so P-132 has a real dependency
- `ConsumerBase<TMessage>.Consume()` starts child Activity "Consumer.Consume" tagged `messaging.message_type`; also enriches log scope with `messaging.destination` (from `ConsumeContext.DestinationAddress?.AbsolutePath`) + `messaging.message_type`
- `MassTransitEventPublisher.PublishAsync<TEvent>()` starts child Activity "EventPublisher.Publish" tagged `messaging.event_type`
- Pattern to watch for generally: when another domain's pending phase assumes a 07.Messaging instrumentation/contract exists that doesn't, fix it as a same-domain 07.Messaging phase first, not a workaround elsewhere

## Logging Standard and EventId Allocation (P-254, WO-041)

- Domain reserved range: `7000-7999` (`07 * 1000`, from `SharedKernel.Primitives.Logging.LoggingEventIdRanges.Messaging` in 01.Core, P-249)
- Logging lives in `SharedKernel.Messaging.MassTransit` (7001–7009, 7011–7012) and the `.AzureServiceBus` satellite (7010 `DeadLetterPolicyAdvisoryHostedService`); Abstractions has zero logging deps. The whole domain shares one sequential `70xx` sequence rather than per-package sub-blocks
- Final allocation: 7001 `ConsumerBase.ConsumerConsumeError`, 7002 `BatchConsumerBase.BatchConsumeEntry`, 7003 `BatchConsumerBase.BatchConsumeError`, 7004 `FaultConsumerAdapter.FaultConsumerHandling`, 7005 `FaultConsumerAdapter.FaultConsumerError` (new — replaced a raw `_logger.LogError` call), 7006 `RoutingSlipActivityBase.RoutingSlipExecuteError`, 7007 `RoutingSlipActivityBase.RoutingSlipCompensateError`, 7008 `VersionTranslatingConsumer.VersionTranslating`, 7009 `TranslatorRegistrationValidator.VersionTranslatorNoConsumer`. 7006/7007 (routing slip) were retired with routing slips (P-560); 7010 `DeadLetterPolicyAdvisory`, 7011–7012 request-context registration advisory followed — continue sequentially from 7013
- Pre-P-254 state had 3 confirmed internal collisions from raw integer `EventId` literals (1, 2, 3) reused across unrelated `LoggerMessage.Define<>()` delegates — a good example of why the mechanical `[LoggerMessage]` + registry-range convention exists
- New shared `MessagingLogScope.Create(Guid? correlationId) → Dictionary<string,object?>` (`Logging/MessagingLogScope.cs`, MassTransit package) is the single approved seed for any `BeginScope` dictionary in this package — always seeds `["CorrelationId"]`. Replaces four previously-independent hand-rolled implementations in `ConsumerBase`, `BatchConsumerBase`, `FaultConsumerAdapter`, `RoutingSlipActivityBase`
- (Historical — `RoutingSlipActivityBase` was removed by P-560.) Its retrofit is a deliberate behavior change, not a pure refactor: its `BeginScope` dictionary never carried a `CorrelationId` key before P-254 (it only tagged `Activity.Current`) — now it does, via the shared helper seeded from `context.TrackingNumber`
- `VersionTranslatingConsumer` and `TranslatorRegistrationValidator` never used `BeginScope` — out of scope for `MessagingLogScope`, only their `LoggerMessage.Define` calls needed converting
- Pattern to watch for: when a future domain phase adds a new consumer/activity base type that logs, check whether it needs `MessagingLogScope.Create` too — the four-type list is not automatically closed

## WO-081 Cryptography Migration (P-499, SK.07.PayloadTransformAad) — structural finding, not a preference

- **`MassTransit.IMessageSerializer.GetMessageBody<T>(SendContext<T>)` and `IMessageDeserializer.Deserialize(MessageBody, Headers, Uri?)`/`.Deserialize(ReceiveContext)` are ALL hard-synchronous** — confirmed by direct .NET reflection against the installed `MassTransit.Abstractions` 9.1.2 assembly (`MetadataLoadContext` didn't work under this environment's Windows PowerShell 5.1 — used a throwaway `dotnet run` console app referencing the real NuGet package instead, since real dependency resolution is needed for reflection `ToString()`/member enumeration to work). No async overload exists anywhere in this pipeline stage — this is a permanent MassTransit architectural fact, not a version-specific gap likely to be fixed later.
- **Consequence for any future feature touching the `ISerializerFactory`/`IMessageSerializer`/`IMessageDeserializer` decorator trio (P-346's payload-transform, and anything built the same way in future)**: it can NEVER call an async dependency without blocking a thread. The existing P-346 payload-transform code already silently discovered this in its own implementation (it calls sync `Compress`/`Encrypt`/`Decrypt`/`Decompress` despite this domain's own historical PT-02 design-task prose claiming `CompressAsync`/`EncryptAsync` — that prose was wrong even at ship time). Check this fact BEFORE designing any new capability against this extensibility point.
- **`SendContext<T>.Headers` (`SendHeaders.Set(string,string)`) is mutable at the point `GetMessageBody<T>` runs, and `IMessageDeserializer.Deserialize`'s `Headers headers` parameter is literally `ReceiveContext.TransportHeaders` forwarded unmodified** — confirmed by reflection. This is the one channel that can carry information from the synchronous-serializer publish side to the synchronous-serializer consume side when the consume side lacks the generic `T` the publish side has (e.g., AAD derived from message type name). Use this pattern again if a future capability needs publish→consume context transfer through this exact pipeline stage.
- **AAD design for P-499**: message CLR type name (`typeof(T).FullName ?? typeof(T).Name`) transmitted via a new plaintext header (`PayloadTransformHeaders.MessageTypeAad`), domain-local constant (not `01.Core.WellKnownHeaders` — both ends live in this one package). Rolling-deploy fallback: header absent → `Array.Empty<byte>()` AAD (byte-identical to AES-GCM's pre-migration implicit no-AAD behavior) so old-producer → new-consumer stays safe; new-producer → old-consumer is NOT safe (genuine AEAD auth failure) and is documented as an operational "consumers upgrade before producers" requirement, not solved in code.
- **`Build()`-time validation for capability markers (like `ISynchronousEncryptionKeyProvider`) that require an actual registered INSTANCE, not just a descriptor type**: can only be checked eagerly for `ServiceDescriptor.ImplementationType`/`ImplementationInstance`-shaped registrations (a plain `is`/`IsAssignableFrom` check on the type/instance, no DI resolution needed) — `ImplementationFactory`-shaped registrations cannot be statically inspected without invoking the factory, which `Build()` must never do (the established P-130 anti-`BuildServiceProvider()` rule). Pattern: eager check where statically provable, defer to the real runtime exception (already thrown deeper in `01.Core`) as the guaranteed backstop otherwise. Reusable whenever a future phase needs to validate a capability-marker interface at `Build()` time.

Related: [[project-messaging-domain]]
