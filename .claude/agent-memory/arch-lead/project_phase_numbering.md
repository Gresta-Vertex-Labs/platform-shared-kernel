---
name: phase-numbering-state
description: Last known phase and work order numbers in the root state-map Phase Backlog
metadata:
  type: project
---

As of 2026-06-19, the last phase written to `state-map.md` Phase Backlog is **P-178** under **WO-028**.

Next new phase must be **P-179**. Next new Work Order must be **WO-029**.

**How to apply:** Always read the current Phase Backlog before assigning new IDs — this memory is a starting point, not a substitute for reading the file. This memory file itself had drifted stale once already (still said P-159/WO-025 when the real file was at P-174/WO-027) — always verify against `grep -n "^### P-" state-map.md | tail` before trusting this note's numbers.

**WO-027 context:** 13.ServiceDefaults initial delivery (P-169–P-171) + 2 cross-domain (07.Messaging ActivitySource P-172, 00.Governance liveness/readiness + composition-root rules P-173) + 16.Testing test doubles (P-174). Status at time of WO-028 audit: P-169–P-173 all `◐ Dispatched`; Core C-01–C-18 actually implemented and tested (41 tests green), C-19 correctly `⚑` blocked on real P-172 dependency.

**WO-028 context:** 13.ServiceDefaults gold-standard hardening audit (P-175–P-178) — see [[project_wo028_servicedefaults_audit]] for full findings. Triggered by a direct user request to audit already-shipped code for bad practices/magic strings/multitenancy correctness, not a new-capability request. Found a broken strategy-extensibility mechanism masked by a false-confidence test, a blocking sync DB call in an async path, two generations of magic-string drift, and a documented-but-unimplemented health check adapter pair.

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

**WO-024 context:** 06.Persistence deep architectural review gap fill (P-147–P-153) — 7 phases across 3 domains:
- P-147 06.Persistence: Fix encryption key rotation reflection violation (MakeGenericMethod/Invoke in LoadBatchAsync) and global EncryptionOptions.CurrentVersion mutable-coupling during RotateAsync; also corrects EncryptedValueConverter<T> doc drift (actual type is non-generic) — no dependency
- P-148 06.Persistence: Set-based bulk update/delete via ExecuteUpdateAsync/ExecuteDeleteAsync on IRepository, spec-driven (criteria/IncludeDeleted only — Includes/Ordering/Paging rejected); documents interceptor + domain-event bypass — no dependency
- P-149 06.Persistence: IAsyncEnumerable streaming reads on IReadRepository for ISpecification<T> and IProjectionSpecification<T,TResult>; always forces AsNoTracking (documented deviation) — no dependency
- P-150 06.Persistence: DB readiness probe primitives (SharedKernelDbContext for EF Core; IDbConnectionFactory-based for Dapper/PostgreSQL) — ships no IHealthCheck itself, 13.ServiceDefaults wraps — no dependency
- P-151 06.Persistence: IDataSeeder<TContext> + EfCorePersistenceBuilder.WithMigrationsOnStartup()/.WithSeeders() — opt-in startup orchestration, advisory/distributed-lock guarded for multi-replica K8s; optional 02.Caching.Redis.DistributedLocking integration point (no hard reference) — no dependency
- P-152 03.Domain: StronglyTypedIdJsonConverterFactory for StronglyTypedId<TValue> (Guid/int/long/string) — supersedes prior "no STJ converter, BYO" doc note; zero new NuGet dep (System.Text.Json is shared-framework) — no dependency
- P-153 00.Governance: New SK0xxx NetArchTest/Roslyn rule banning GetMethod/MakeGenericMethod/Invoke platform-wide except via documented exception mechanism; motivated directly by the P-147 finding — depends P-147

**Key architectural decisions made in WO-024:**
1. Reflection-based generic dispatch (`GetMethod` + `MakeGenericMethod` + `Invoke`) is now a platform-wide prohibition, not just a 06.Persistence convention — P-147 found the exact forbidden pattern shipped inside the package that documents it as forbidden elsewhere (TenantedDbContext uses expression trees as the sanctioned alternative)
2. Encryption rotation must decouple per-operation target version from steady-state `EncryptionOptions.CurrentVersion` — the original WO-019 design's "set CurrentVersion globally before rotating" instruction was a latent multi-instance production incident
3. Bulk set-based mutations (ExecuteUpdate/ExecuteDelete) are a distinct capability from UpdateRangeAsync/DeleteRangeAsync — explicitly bypass interceptors/domain events, spec pipeline restricted to criteria + IncludeDeleted only
4. Streaming reads always force AsNoTracking regardless of spec flag — the one documented deviation from "the spec's AsNoTracking is honored"
5. DB readiness probes live in 06.Persistence as primitives only; IHealthCheck wiring stays a 13.ServiceDefaults concern (consistent with existing health-check placement rule)
6. Migration/seed runner is opt-in via EfCorePersistenceBuilder, explicitly NOT a migration-authoring tool nor a compiled-model (P-106) replacement; distributed lock for multi-replica races is an optional integration point with 02.Caching.Redis.DistributedLocking, never a hard reference
7. StronglyTypedId STJ converter factory is zero-new-dependency since System.Text.Json ships in the net10.0 shared framework — does not violate SharedKernel.Domain's zero-external-NuGet rule

**Domains touched in WO-024:**
- 06.Persistence: already ● Published — P-147–P-151 queued in backlog only; no state-map-phase call made
- 03.Domain: already ● Published — P-152 queued in backlog only; no state-map-phase call made
- 00.Governance: already ● Complete — P-153 queued in backlog only; no state-map-phase call made

Root CLAUDE.md synced via sync-brain for WO-024: 6 new "What Goes Where" rows + 1 changelog line (2026-06-12).
