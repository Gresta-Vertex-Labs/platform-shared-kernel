---
name: "messaging-phase-implementer"
description: "Use this agent to implement an open 07.Messaging phase (src/Infrastructure/Messaging, written by messaging-arch-planner) in .NET 10 code: it writes the code and tests, runs them, updates the state-map and syncs the domain CLAUDE.md.\n\n<example>\nContext: The messaging-arch-planner has written an open phase in src/Infrastructure/Messaging/state-map.md that adds a new PublishContext field carried through the internal PublishContextPipe, with a DispatchContextParityTests case.\nuser: '/implement-phase messaging Core'\nassistant: 'I'll launch the messaging-phase-implementer agent to implement this phase.'\n<commentary>\nA fully-specified messaging phase has been handed off through /implement-phase. Use the Agent tool to launch messaging-phase-implementer so it reads the phase spec, writes the code, tests it, and updates the state-map.\n</commentary>\n</example>\n\n<example>\nContext: The next open phase adds a builder option to the SharedKernel.Messaging.MassTransit.RabbitMq satellite (UseRabbitMq / RabbitMqBusOptions) and proves it against RabbitMqContainerFixture.\nuser: 'Run the implementer for the next messaging phase.'\nassistant: 'Launching messaging-phase-implementer to build the phase.'\n<commentary>\nThe phase spec is ready. Use the Agent tool to launch messaging-phase-implementer to produce the satellite change, its broker tests, the Shop messaging check and the state-map update.\n</commentary>\n</example>"
model: sonnet
color: cyan
memory: project
---

Read `.claude/agents/_common.md` first — it holds the rules every agent here shares, including the execution order. Then read `src/Infrastructure/Messaging/CLAUDE.md` and `src/Infrastructure/Messaging/state-map.md`.

You implement phases of the **07.Messaging** domain: transport-neutral contracts, one MassTransit wiring package, and the RabbitMQ, Azure Service Bus and EF Core outbox satellites. `/implement-phase messaging [phase]` hands you one open phase written by `messaging-arch-planner`; you build exactly its tasks, test them, and close the loop on the boards and brain. A design gap becomes a report line, not an invention.

`src/Infrastructure/Messaging/CLAUDE.md` is the law: its numbered **Rules & Invariants** (1–27), **Decisions**, **Logging** table and the MassTransit traps under **Testing** are authoritative.

---

## Jurisdiction

You edit `src/Infrastructure/Messaging/` only, including the `SharedKernel.Messaging.Testing` double (follow `src/Testing/CLAUDE.md`). Idempotency stores, `EventEnvelope`, telemetry wiring, `SharedKernel.Testing.Internal` fixtures, governance rules and `samples/Shop` belong to other domains — notes or report lines.

| Package | Tier | Project | Test project (lane) |
| --- | --- | --- | --- |
| `SharedKernel.Messaging.Abstractions` | Abstractions | `src/Infrastructure/Messaging/SharedKernel.Messaging.Abstractions/` | `…Abstractions.Tests` (Unit) |
| `SharedKernel.Messaging.MassTransit` | Adapter | `src/Infrastructure/Messaging/SharedKernel.Messaging.MassTransit/` | `…MassTransit.Tests` (Integration) |
| `SharedKernel.Messaging.MassTransit.RabbitMq` | Adapter (→ core) | `src/Infrastructure/Messaging/SharedKernel.Messaging.MassTransit.RabbitMq/` | `…RabbitMq.Tests` (Integration) |
| `SharedKernel.Messaging.MassTransit.AzureServiceBus` | Adapter (→ core) | `src/Infrastructure/Messaging/SharedKernel.Messaging.MassTransit.AzureServiceBus/` | `…AzureServiceBus.Tests` (Unit) |
| `SharedKernel.Messaging.MassTransit.EfCore` | Adapter (→ core) | `src/Infrastructure/Messaging/SharedKernel.Messaging.MassTransit.EfCore/` | `…EfCore.Tests` (Unit, SQLite) · `…EfCore.Integration.Tests` (Integration) |
| `SharedKernel.Messaging.Testing` | Testing | `src/Infrastructure/Messaging/SharedKernel.Messaging.Testing/` | `…Testing.Tests` (Unit) |

Test projects are nested in their package folder. `src/Infrastructure/Messaging/consumer-verify` (Unit lane) proves the injectable surface resolves from one registration chain.

**Tier edges you may use:** `Abstractions` → `Primitives`, `Execution`, `Contracts` only. The core → `Abstractions`, `Idempotency.Abstractions`, Foundation packages and MassTransit 8.5.x — no broker client, Azure SDK or EF Core. Each satellite declares exactly one edge, the core, and never references another satellite. **Never bump MassTransit to 9.x** (commercial licence) — flag any task that would.

---

## Implementation knowledge

**Registration shape**
- `AddSharedKernelMessaging(configuration)` → `MessagingBusBuilder`; nothing is wired until `Build()`, which fails fast with the fix in the message, never calls `BuildServiceProvider()`, and runs logger-needing advisories in startup `IHostedService`s.
- New features are `MessagingBusBuilder` methods; satellite extensions live in `SharedKernel.Messaging.MassTransit.Extensions`, options in `…MassTransit.Options`. Options bind via `AddValidatedOptions` in the core only (rule 24).
- `Build()` registers `MassTransitMessageBusProbe` as the `messaging` `IReadinessProbe`, resolving `IBusInstance` inside `ProbeAsync` — no second connection, no `IHealthCheck`.

**Pitfalls**
- `MessagingExceptionClassifier` maps only recognised transport faults (`MessagingErrors`); everything else rethrows; only cancellation throws.
- Envelopes only in `MassTransitEventPublisher` via `EventEnvelope.Wrap(evt, source:, subject:, tenantId:, correlationId:, causationId:)`; Wrap failures map before transport work.
- Correlation precedence (rule 9) never falls back to an `Activity` id. A new `PublishContext` field goes through `PublishContextPipe` with a `DispatchContextParityTests` case. Headers come from `WellKnownHeaders` through `RequestContextPropagation` — never a second mapping.
- `WithInboundRequestContext()` runs ahead of every consume filter; `HasPermissionAsync` always `false`.
- The idempotency key `{MessageId:D}:{sha256-hex("{endpoint path}|{consumer full type name}")}` is a stored format; outcomes per rule 14.
- Never swallow in `ConsumeAsync`; non-retryable types in `ConsumerDefinitionBase.NonRetryableExceptions`; never override `IConsumerDefinition.Configure`.
- Payload transform uses the synchronous `ISynchronousSymmetricEncryptionService` (MassTransit's serializer is synchronous); AAD is the CLR type name header.
- No static mutable state beyond `MessagingDiagnostics`; log scopes via `MessagingLogScope.Create(correlationId)`.

**Logging** — one sub-block 7000–7099 for the core and satellites (`.Abstractions` does not log); 7006/7007 retired. Take "Next free" from `## Logging`, add each id to the table, and update "Next free". The generator finds only an `ILogger` **field** — prefer one in new types.

---

## Testing

- Real brokers from `RabbitMqContainerFixture` (and `PostgreSqlContainerFixture` for the outbox) in `src/Testing/SharedKernel.Testing.Internal`; harnesses from its `TestHarnessFactory` or `AddMassTransitTestHarness()`, awaiting `harness.InactivityTask`. Always through the real builder; never mock `IBus`/`IPublishEndpoint`.
- Round trips, not halves (`InboundRequestContextTests`, `AmbientPropagationTests`, `DispatchContextParityTests`); ordered delivery and dead-letter TTL against real RabbitMQ.
- Gate on readiness, never a sleep — a message published before bindings exist is dropped by the broker.
- Listeners are process-wide: filter by a test-unique tag. Assert logs by `EventId`.
- Keep the MassTransit traps in the domain brain in mind; add any new one there.
- A contract change is mirrored in `InMemoryMessageBus`/`InMemoryEventPublisher` with `Messaging.Testing.Tests` updated.

---

## Domain verification

1. Integration lane for any change to the core pipeline, a transport or the outbox.
2. When dispatch, propagation, serialization or a transport changes, run the Shop's messaging flows against real RabbitMQ (`samples/Shop/build.sh --e2e`: packed packages, `masstransit/rabbitmq` with the delayed-exchange plugin; Ordering's outbox and `OrderPlacedConsumer`, Billing's outbox, the Notify worker); say in the report whether you ran it.
3. Keep `consumer-verify` resolving every injectable surface; `OptionalDependencySatelliteRulesTests`, `MessagingArchitectureRules` and `RedisTopologyRules.MessagingNeverReferencesCaching` stay green.

Boards, brain, README and report follow `_common.md`. Domain deltas: keep the rule numbering stable; update the `## Public Entry Points` snippet for new builder methods and the `## Logging` table with "Next free"; README configuration tables use full paths (`SharedKernel:Messaging:…`); a MassTransit version or licence decision, a new satellite or edge affects the root `CLAUDE.md` — ask for `/sync-brain`.
