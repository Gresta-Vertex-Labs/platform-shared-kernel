---
name: phase-numbering-state
description: Last known phase and work order numbers in the root state-map Phase Backlog
metadata:
  type: project
---

As of 2026-06-11, the last phase written to `state-map.md` Phase Backlog is **P-146** under **WO-023**.

Next new phase must be **P-147**. Next new Work Order must be **WO-024**.

**How to apply:** Always read the current Phase Backlog before assigning new IDs — this memory is a starting point, not a substitute for reading the file.

**WO-020 context:** 07.Messaging domain initial delivery (P-115–P-124) — 7 messaging phases + 3 cross-domain (ServiceDefaults health checks P-122, Governance MSG rules P-123, Testing harness helpers P-124).

**WO-021 context:** 07.Messaging deep architectural review gap fill — 9 phases across 4 domains:
- P-125 07.Messaging: IFaultConsumer<T>, FaultExceptionInfo, CircuitBreakerOptions in Abstractions
- P-126 07.Messaging: WithCircuitBreaker(), AddFaultConsumer adapter in MassTransit — depends P-125
- P-127 07.Messaging: IMessageScheduler, SchedulingOptions (Abstractions), MassTransitMessageScheduler, WithInMemoryScheduler(), WithQuartzScheduler() — depends P-125
- P-128 07.Messaging: SagaStateBase, SagaStateMachineBase<TSaga>, AddSaga<T>, WithEntityFrameworkSagaRepository — depends P-125
- P-129 07.Messaging: BatchConsumerBase<T>, BatchOptions, AddBatchConsumer<T>() — no dependency
- P-130 07.Messaging: Fix Build() calling Services.BuildServiceProvider() — critical anti-pattern fix — no dependency
- P-131 07.Messaging: ISendEndpointResolver, ConventionSendEndpointResolver, WithSendEndpointRoute<T> — depends P-125
- P-132 13.ServiceDefaults: WithMessagingTelemetry() — MassTransit + SharedKernel.Messaging ActivitySource wiring — depends P-117, P-118
- P-133 00.Governance: MSG0105-MSG0108 architecture rules — depends P-125, P-126, P-127, P-128

**Key architectural decisions made in WO-021:**
1. Build() ServiceProvider anti-pattern identified: `Services.BuildServiceProvider()` inside `MessagingBusBuilder.Build()` creates a second root container — fix by using captured `Action<MessagingOptions>?` delegate directly for validation
2. IFaultConsumer<T> lives in Abstractions (not MassTransit) so fault handler implementations never need a MassTransit reference; adapter lives in MassTransit package
3. CircuitBreaker ordering rule: retry inner, circuit breaker outer — retry first within current breaker state, then breaker guards against sustained failure
4. IMessageScheduler interface in Abstractions — zero NuGet deps; MassTransit.IMessageScheduler must never be injected directly outside 07.Messaging
5. SagaStateBase is a record (not a class) — EF Core mappable, ISagaVersion compatible, platform audit fields standardized
6. BatchConsumerBase<T> uses IConsumer<Batch<T>> — must be registered via AddBatchConsumer<T>() not AddConsumer<T>() to apply batch configuration
7. ISendEndpointResolver fixes the hardcoded service-name prefix assumption in SendAsync<T>() — per-type route dictionary takes precedence over convention
8. Custom ActivitySource("SharedKernel.Messaging", "1.0.0") added to ConsumerBase and MassTransitEventPublisher for platform-namespaced traces
9. WithMessagingTelemetry() idempotency required — multiple registrations must not duplicate OTel instruments

**Domains touched in WO-021:**
- 07.Messaging: already ● Published — new phases queued in backlog only; no state-map-phase call made
- 13.ServiceDefaults: already ○ Not Started — P-132 queued; state-map-phase not called (P-122 already pending from WO-020 also targets this domain)
- 00.Governance: already ● Complete — P-133 queued in backlog only; no state-map-phase call made

**WO-023 context:** 02.Caching Redis package topology refactor (P-140–P-146) — see [[project_wo023_caching_redis_topology]] for full decision rationale. Splits `SharedKernel.Caching.Redis` into `.Redis.Core` + 4 role packages (`.Redis` L2, `.Redis.DistributedLocking`, `.Redis.HashStore`, `.Redis.PubSub`). New `.{Provider}.Core`/`.{Provider}.{Role}` naming pattern + new root hard rule (02.Caching <-> 07.Messaging mutual exclusion) added to root CLAUDE.md.

**Domains touched in WO-023:**
- 02.Caching: already ● Complete — P-140–P-144 queued in backlog only; no state-map-phase call made
- 00.Governance: already ● Complete — P-145 queued in backlog only; no state-map-phase call made
- 16.Testing: already ◐ In Progress — P-146 queued in backlog only; no state-map-phase call made
