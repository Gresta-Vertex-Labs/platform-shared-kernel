# 16.Testing — Shared Test Infrastructure Brain

## What This Domain Is

The shared test-infrastructure layer. Every `.Tests` project in this repository — and every downstream microservice's test suite — references `SharedKernel.Testing` for fakes, in-memory test doubles, and container fixtures instead of hand-rolling them per project. This domain is exempt from the normal layering direction: it may reference **any** numbered layer, because it is test-only and is never shipped inside a production artifact.

Philosophy: **Deterministic, dependency-light, conformance-first.** A fake's job is to satisfy the exact interface contract of the thing it replaces — nothing more. No fake here may introduce flakiness (real clocks, real sleeps, unseeded randomness) or silently diverge from the production implementation's documented behavior.

> **Why this package exists:** without it, every `.Tests` project across `02`–`14` independently reinvents `FakeCacheService`-shaped classes, container bootstrapping, and auth stand-ins — with subtle behavioral drift between copies. `16.Testing` is the single source of truth for "what does a fake `ICacheService` look like," so a behavioral fix only has to happen once.

---

## Packages

| Package | Role | References |
|---------|------|------------|
| `SharedKernel.Testing` | Fakes, in-memory test doubles, Testcontainers fixtures, and Bogus faker conventions consumed by every `.Tests` project | Any layer's `.Abstractions` package (and, where a planning pass has justified it, a non-`.Abstractions` package — e.g. `SharedKernel.Persistence.EfCore`, `SharedKernel.Messaging.MassTransit` — on demand, as each capability area is added). Currently implemented: `SharedKernel.Caching.Abstractions`. Planned, per this pass: `SharedKernel.Primitives`, `SharedKernel.Domain`, `SharedKernel.Contracts`, `SharedKernel.Security.Abstractions`, `SharedKernel.Messaging.Abstractions`, `SharedKernel.Messaging.MassTransit` (test-only), `SharedKernel.Persistence.Abstractions`, `SharedKernel.Persistence.EfCore`, `SharedKernel.Communication.Internal`. |
| `SharedKernel.Testing.SelfTests` | Self-contained unit tests for standalone testing-infrastructure helpers that have no owning consuming-domain interface to anchor against (fluent builders, assertion-helper classes, faker-seeding conventions, recorder/double self-checks) — see Test Rules below for the decision rule | `SharedKernel.Testing` + the Standard Test Package Set (xUnit runner, FluentAssertions, NSubstitute) as direct package references |

There is no `.Abstractions`/`.{Provider}` split for `SharedKernel.Testing` itself — it is the "provider" of test doubles, and nothing downstream re-implements it. `SharedKernel.Testing.SelfTests` is a narrow, documented exception added in WO-029 — not a general-purpose `.Tests` project for the whole domain (see Test Rules below).

Targets `net10.0`. `SharedKernel.Testing` itself has **no nested `.Tests` project** — see Test Rules below for why; `SharedKernel.Testing.SelfTests` exists alongside it for a deliberately narrow purpose.

### Publishing / Consumption Model

Both packages in this domain are **never packed or published as a `.nupkg`**. `SharedKernel.Testing.csproj` and `SharedKernel.Testing.SelfTests.csproj` both carry `<IsPackable>false</IsPackable>` explicitly. Consumption is **`ProjectReference`-only**, within this mono-repo: every `.Tests` project across `02`–`14` adds a direct `ProjectReference` to `16.Testing/SharedKernel.Testing/SharedKernel.Testing.csproj` (per root `CLAUDE.md` Test Project Rules), not a NuGet package reference. This is a deliberate decision, not an oversight — packing remains a future work order if/when a downstream consumer outside this repo needs `SharedKernel.Testing` as an installable package. `SharedKernel.Testing.SelfTests` carries the same non-shipping status for the same reason — it is a self-test project only and has no reason to ever be packed.

---

## Technology Stack

| Concern | Technology |
|---------|------------|
| Fakes / in-memory test doubles | Pure C# 13 — implements the target interface directly; zero mocking-framework dependency (no NSubstitute/Moq inside this package) |
| Container orchestration | `Testcontainers` (base) + `Testcontainers.PostgreSql`, `Testcontainers.Redis`, `Testcontainers.RabbitMq` — one NuGet package per engine, added only when that capability area's fixture is implemented |
| Deterministic fake data | `Bogus` — seeding convention only; concrete `Faker<TEntity>` definitions for business aggregates stay in each consuming service's own test project |
| xUnit lifetime contract | `xunit.core` (the `Xunit.IAsyncLifetime` contract only) — added solely so container fixtures can implement `IAsyncLifetime` directly; no test runner, no `Xunit.Assert`, no `xunit.runner.visualstudio` |
| Thread-safe state | `System.Collections.Concurrent` (`ConcurrentDictionary`, `ConcurrentQueue`) — every stateful fake must tolerate parallel xUnit test collections |

`SharedKernel.Testing` deliberately does **not** reference `FluentAssertions`, `NSubstitute`, or any xUnit runner package. Those belong to the **Standard Test Package Set** added directly by each `.Tests` project (see root `CLAUDE.md` Test Project Rules and each domain's own Test Rules section for the pinned versions). Mixing assertion/mocking libraries into a shared production-shaped dependency would force every consumer onto this package's framework choices.

---

## Folder / Namespace Map

```text
SharedKernel.Testing/
  Clocks/          — SharedKernel.Testing.Clocks         — IClock fake (01.Core) — FakeClock
  Caching/         — SharedKernel.Testing.Caching         — ICacheService / IDistributedLockService / ITenantCacheKeyProvider / ICacheInvalidationBus fakes (02.Caching)
  Domain/          — SharedKernel.Testing.Domain          — assertion helpers over 03.Domain primitives — DomainEventAssertions, BusinessRuleAssertions, SpecificationAssert, DomainVersionAssertions, SpecificationTestBuilder<T>, FakeDomainNotFoundException
  Contracts/       — SharedKernel.Testing.Contracts       — DTO test helpers (04.Contracts) — PagedListBuilder<T>, EnvelopeAssertions, IntegrationEventFaker<TEvent>, EventEnvelopeBuilder<TEvent>, PagedListAssertions
  Security/        — SharedKernel.Testing.Security        — IUserContext / ITenantProvider fakes (12.Security)
  Messaging/       — SharedKernel.Testing.Messaging       — IMessageBus / IEventPublisher in-memory doubles (07.Messaging), TestHarnessFactory
  Persistence/     — SharedKernel.Testing.Persistence     — IDbConnectionFactory fake + EF Core test helpers (06.Persistence) — TestSharedKernelDbContext, AggregateRootFaker, EfContextExtensions, ProjectionSpecificationBuilder, BulkAggregateFaker, WithDeletedSpecification, PersistenceTestHelpers
  Containers/      — SharedKernel.Testing.Containers      — Testcontainers IAsyncLifetime fixtures (PostgreSQL / Redis / RabbitMQ)
  Communication/   — SharedKernel.Testing.Communication   — cross-cutting Communication test doubles (11.Communication) — MockServiceEndpointResolver, FakeHttpContextAccessor, HttpClientHandlerTestFactory, FakeHttpMessageHandler, ambient Activity helper, gRPC ServerCallContext stub, GraphQL test-executor factory
  ServiceDefaults/ — SharedKernel.Testing.ServiceDefaults  — tenant resolution and health check test doubles (13.ServiceDefaults) — StaticTenantProvider, FakeTenantResolutionStrategy, HealthCheckAssertionExtensions
  Fakers/          — SharedKernel.Testing.Fakers          — Bogus deterministic-seeding convention + abstract Faker<T> bases — FakerSeeding, EntityFaker<TEntity,TId>, SingleValueObjectFaker<TValueObject,TValue>
```

Each capability folder maps 1:1 to the numbered domain whose abstraction it fakes. A new capability folder is added only when a concrete consumer needs it — this map is aspirational scaffolding, not a commitment to build every row immediately (see per-type `STATUS` markers below). `Domain/` and `Fakers/` are deliberately split: `Fakers/` holds abstract `Bogus.Faker<T>` base classes (construction-time concerns); `Domain/` holds assertion/verification helpers (post-condition concerns) — both fake sibling-isolation from each other since neither references the other's types.

---

## Interface Contracts

### `Clocks/` — `IClock` fake (01.Core)

```text
FakeClock  (sealed class, implements IClock)
    .UtcNow                                                    → DateTimeOffset  (settable; thread-safe via private Lock)
    .Today                                                     → DateOnly        (derived from UtcNow)
    constructor(DateTimeOffset? initial = null)
        Defaults to a fixed, non-real instant (2024-01-01T00:00:00Z) when initial is omitted —
        never DateTimeOffset.UtcNow.
    .Advance(TimeSpan delta)                                   → void  (UtcNow += delta)
    .Set(DateTimeOffset value)                                 → void
    .SetUtcNow(DateTimeOffset value)                           → void  (alias for Set)
    NOTE: Used wherever production code takes an IClock dependency (AggregateRoot<TId>, audit
          interceptors, DomainEventVersionHelper-adjacent timestamping, etc.). Implemented in
          Clocks/FakeClock.cs; also registered as the IClock singleton by AddFakeDomainServices().
```

### `Caching/` — capability fakes (02.Caching) — **implemented**

```text
FakeCacheService  (sealed class, implements ICacheService)
    .Count                                                     → int
    .GetAsync<T>(string key, CancellationToken ct)             → ValueTask<T?>
    .SetAsync<T>(string key, T value, CachePolicy policy, CancellationToken ct) → ValueTask
    .GetOrSetAsync<T>(string key, Func<CancellationToken, ValueTask<T>> factory,
                       CachePolicy policy, CancellationToken ct) → ValueTask<T>
    .RemoveAsync(string key, CancellationToken ct)             → ValueTask
    .RemoveByTagAsync(string tag, CancellationToken ct)        → ValueTask
    .GetManyAsync<T>(IEnumerable<string> keys, CancellationToken ct) → ValueTask<IReadOnlyDictionary<string, T?>>
    .SetManyAsync<T>(IReadOnlyDictionary<string, T> entries, CachePolicy policy, CancellationToken ct) → ValueTask
    .Clear()                                                   → void
    NOTE: Thread-safe via ConcurrentDictionary<string, object?> + per-key tag tracking. TTL and
          sliding-window timing from CachePolicy are intentionally NOT enforced — this fake verifies
          behavioral correctness (what was cached, under what key/tag), not expiry timing. Every
          requested key in GetManyAsync is present in the result dictionary; missing keys map to default.

FakeDistributedLockService  (sealed class, implements IDistributedLockService)
    .SimulateFailure                                           → bool  (settable; forces null on both acquire paths)
    .AcquireAsync(string resource, TimeSpan expiry, TimeSpan wait, TimeSpan retry, CancellationToken ct) → Task<IAsyncDisposable?>
    .AcquireRenewableAsync(string resource, TimeSpan expiry, TimeSpan wait, TimeSpan retry, CancellationToken ct) → ValueTask<IRenewableLock?>
    NOTE: Locks are always granted immediately — no contention simulation. SimulateFailure is the
          only failure-injection seam.

FakeRenewableLock  (sealed class, implements IRenewableLock)
    .RenewalCount                                              → int  (increments on every successful RenewAsync)
    .SimulateRenewalFailure                                    → bool  (settable; forces RenewAsync → false + IsAcquired → false)
    .IsAcquired                                                → bool  (false after DisposeAsync or a failed renewal)
    .RenewAsync(CancellationToken ct)                          → ValueTask<bool>
    .DisposeAsync()                                            → ValueTask
    NOTE: Returned exclusively by FakeDistributedLockService.AcquireRenewableAsync.

FakeTenantCacheKeyProvider  (sealed class, implements ITenantCacheKeyProvider)
    constructor()                                              — serviceName defaults to "test-svc"
    constructor(string serviceName)
    .BuildTenantKey(string tenantId, string entity, string id, params string[] extraSegments) → string
    .BuildKey(string entity, string id, params string[] extraSegments)                          → string
    .BuildKey(string entity, string id, int version, params string[] extraSegments)             → string
    NOTE: Key format mirrors the production TenantCacheKeyProvider exactly (see 02.Caching's own
          contract). Zero dependency on 12.Security or IHttpContextAccessor — safe in pure unit tests.

FakeCacheInvalidationBus  (sealed class, implements ICacheInvalidationBus)
    .PublishedInvalidations                                    → IReadOnlyList<CacheInvalidationMessage>
    .PublishKeyInvalidationAsync(string[] keys, CancellationToken ct)               → ValueTask
    .PublishTagInvalidationAsync(string[] tags, CancellationToken ct)               → ValueTask
    .PublishBroadcastInvalidationAsync(CancellationToken ct)                        → ValueTask
    .PublishInvalidationAsync(CacheInvalidationMessage message, CancellationToken ct) → ValueTask
    .OnInvalidation(Func<CacheInvalidationMessage, ValueTask> handler)              → void
        Registers a handler invoked synchronously on every publish call (any of the four overloads)
        so a test can chain a FakeCacheService.RemoveAsync as the downstream effect without async
        plumbing of its own.
    .Reset()                                                   → void  (clears recorded messages AND registered handlers)
    NOTE: Pure in-memory — zero dependency on IRedisChannelService or any Redis package, unlike the
          production RedisCacheInvalidationBus. References SharedKernel.Caching.Abstractions only.

AddFakeCachingServices(this IServiceCollection)
    NOTE: First DI convenience extension for Caching/ — registers all four caching fakes
          (FakeCacheService, FakeDistributedLockService, FakeTenantCacheKeyProvider,
          FakeCacheInvalidationBus) as singletons in one call. Supersedes the prior manual
          per-fake AddSingleton<T> pattern shown in DI Registration below (that pattern remains
          valid for callers who want only a subset of the four fakes).
```

### `Domain/` — domain primitive test helpers (03.Domain)

```text
FakeClock  (sealed class, implements IClock)
    .UtcNow                                                    → DateTimeOffset  (settable via SetUtcNow)
    .Today                                                     → DateOnly        (= DateOnly.FromDateTime(UtcNow.DateTime))
    constructor(DateTimeOffset? initial = null)
        Defaults to a fixed, non-real instant when initial is omitted — never DateTimeOffset.UtcNow.
    .SetUtcNow(DateTimeOffset value)                           → void
    .Advance(TimeSpan duration)                                → void  (UtcNow += duration)
    NOTE: Thread-safe via lock/Interlocked over the backing field. Lives in Clocks/ folder (namespace
          SharedKernel.Testing.Clocks), not Domain/ — see Folder/Namespace Map. Registered as IClock
          singleton by AddFakeDomainServices().

EntityFaker<TEntity, TId>  (abstract class, extends Bogus.Faker<TEntity>)
    where TEntity : Entity<TId>  where TId : notnull
    .WithClock(IClock clock)                                   → EntityFaker<TEntity, TId>  (fluent — wires a FakeClock for construction)
    NOTE: Abstract base only — not a complete auto-faker. Concrete fakers in consuming test projects
          declare their own RuleFor(...) definitions because domain invariants must be respected.
          Lives in Fakers/ folder (namespace SharedKernel.Testing.Fakers).

DomainEventAssertions  (static class — extension methods on IReadOnlyCollection<IDomainEvent>)
    .ContainsEventOfType<T>(this IReadOnlyCollection<IDomainEvent> events)           → T  (returns the matching event; throws InvalidOperationException if none found)
    .ContainsExactly<T>(this IReadOnlyCollection<IDomainEvent> events, int count)    → void  (throws if actual count != count)
    .HasNoEvents(this IReadOnlyCollection<IDomainEvent> events)                      → void  (throws if non-empty)
    .HasNoEventsOfType<T>(this IReadOnlyCollection<IDomainEvent> events)             → void  (throws if any present)
    .ContainsEventWithVersion<T>(this IReadOnlyCollection<IDomainEvent> events, int version) → T  [P-181] (asserts a T exists AND DomainEventVersionHelper.GetVersion(typeof(T)) == version)
    .HasRaisedExactlyNEvents(this IReadOnlyCollection<IDomainEvent> events, int n)   → void  [P-181] (any event type, not just T)
    NOTE: Framework-agnostic — throws InvalidOperationException with descriptive messages, zero
          dependency on xUnit/NUnit/FluentAssertions. Designed against AggregateRoot<TId>.DomainEvents'
          exact return type. Lives in Domain/ folder (namespace SharedKernel.Testing.Domain).

BusinessRuleAssertions  (static class — extension methods on IBusinessRule)
    .ShouldBeBroken(this IBusinessRule rule)                   → void  (throws InvalidOperationException with rule.Message if IsBroken() == false)
    .ShouldNotBeBroken(this IBusinessRule rule)                → void  (throws InvalidOperationException with rule.Message if IsBroken() == true)
    NOTE: Framework-agnostic, no test-framework dependency.

SpecificationAssert  (static class)
    .Satisfies<T>(ISpecification<T> spec, T entity)            → void  (throws if Criteria.Compile() applied to entity returns false; null Criteria = always satisfies)
    .DoesNotSatisfy<T>(ISpecification<T> spec, T entity)       → void  (throws if true)
    NOTE: Compiles ISpecification<T>.Criteria via .Compile() — reflection-based expression compilation,
          acceptable in test assemblies only, never production. Lets unit tests prove specification
          logic without a database. Standalone helper, no owning consuming-domain interface —
          proven in SharedKernel.Testing.SelfTests.

SingleValueObjectFaker<TValueObject, TValue>  (abstract class, extends Bogus.Faker<TValueObject>)
    where TValueObject : SingleValueObject<TValue>  where TValue : notnull
    .WithValue(TValue value)                                   → SingleValueObjectFaker<TValueObject, TValue>  (fluent)
    .WithRandomValue(Func<Bogus.Faker, TValue> generator)       → SingleValueObjectFaker<TValueObject, TValue>  (fluent)
    NOTE: Lives in Fakers/ folder.

DomainVersionAssertions  (static class)
    .ShouldHaveVersion<TEvent>(int expectedVersion)            → void  (asserts [DomainEventVersion(N)] present AND N == expectedVersion; throws plain exception, NOT FluentAssertions)
    .ShouldBeVersioned<TEvent>()                               → void  (asserts any [DomainEventVersion] attribute is present)
    NOTE: Plain exceptions with descriptive messages — SharedKernel.Testing itself never depends on
          FluentAssertions. Proven in SharedKernel.Testing.SelfTests (standalone assertion helper).

SpecificationTestBuilder<T>  (sealed class — fluent in-memory specification test helper)
    static .For(ISpecification<T> spec)                        → SpecificationTestBuilder<T>
    .Against(IEnumerable<T> entities)                          → SpecificationTestBuilder<T>  (fluent)
    .ExpectCount(int n)                                         → SpecificationTestBuilder<T>  (fluent)
    .ExpectMatch(Func<T, bool> predicate)                       → SpecificationTestBuilder<T>  (fluent)
    .Assert()                                                   → void  (evaluates spec.IsSatisfiedBy per entity; throws with descriptive failure including entity details)
    NOTE: Wraps Specification<T>.IsSatisfiedBy — in-domain/in-test use only per 03.Domain's own
          documented constraint. No owning consuming-domain interface (it is a builder, not an
          implementation) — proven in SharedKernel.Testing.SelfTests.

FakeDomainNotFoundException  (static factory class)
    static .For<TAggregate>(object id)                          → DomainNotFoundException
        Produces a valid DomainNotFoundException (03.Domain) for repository-fake not-found setups.
    NOTE: Factory, not a fake implementing an interface — proven in SharedKernel.Testing.SelfTests.

AddFakeDomainServices(this IServiceCollection)
    NOTE: Registers FakeClock as IClock singleton. Other domain test helpers above are static or
          plain Bogus-derived classes, not DI-registered — consistent with the Security/Persistence/
          Clocks convention of "DI registration only when swapping in for a production registration."
```

### `Contracts/` — DTO test helpers (04.Contracts)

```text
PagedListBuilder<T>  (sealed class — fluent test builder)
    .WithItems(IEnumerable<T> items)                            → PagedListBuilder<T>  (fluent; also sets default TotalCount = items.Count)
    .WithPage(int page)                                         → PagedListBuilder<T>  (fluent; default 1)
    .WithPageSize(int pageSize)                                 → PagedListBuilder<T>  (fluent; default 10)
    .WithTotalCount(int totalCount)                             → PagedListBuilder<T>  (fluent; overrides the WithItems-derived default)
    .Build()                                                    → PagedList<T>  (calls PagedList<T>.Create(...))
    static .Empty<T>()                                          → PagedList<T>  (zero items, TotalCount=0, Page=1, PageSize=10)
    NOTE: Eliminates repetitive PagedList<T>.Create(...) boilerplate in paged-query test setups.
          Standalone builder, no owning consuming-domain interface — proven in SelfTests.

EnvelopeAssertions  (static class — extension methods on Envelope / Envelope<T>)
    .ShouldBeSuccess(this Envelope envelope)                    → void  (throws with Error details if IsSuccess == false)
    .ShouldBeFailure(this Envelope envelope)                    → void  (throws if IsSuccess == true)
    .ShouldBeSuccess<T>(this Envelope<T> envelope)               → T  (returns Value for chaining; throws if failure)
    .ShouldBeFailure<T>(this Envelope<T> envelope, ErrorType? expectedType = null) → void  (throws if success; optionally asserts ErrorType)
    .ShouldHaveError<T>(this Envelope<T> envelope, string expectedCode)             → void  (throws if failure but code mismatches, or if success)
    NOTE: All throw InvalidOperationException, zero test-framework dependency. Standalone assertion
          helper, no owning consuming-domain interface — proven in SelfTests.

IntegrationEventFaker<TEvent>  (abstract class, extends Bogus.Faker<TEvent>)
    where TEvent : IIntegrationEvent
    protected .RuleForEventId()                                 → void  (pre-wires EventId to f.Random.Guid())
    protected .RuleForOccurredOn()                               → void  (pre-wires OccurredOn to f.Date.RecentOffset())
    NOTE: Subclasses call these helpers in their constructor then add their own RuleFor declarations.
          Eliminates EventId/OccurredOn boilerplate on every integration event faker.

EventEnvelopeBuilder<TEvent>  (sealed class — fluent test builder)  where TEvent : IDomainEvent
    .WithPayload(TEvent @event)                                  → EventEnvelopeBuilder<TEvent>  (fluent)
    .WithSourceService(string name)                              → EventEnvelopeBuilder<TEvent>  (fluent; default "test-service")
    .WithCorrelationId(string id)                                → EventEnvelopeBuilder<TEvent>  (fluent; default Guid.NewGuid().ToString("N"))
    .WithCausationId(string id)                                  → EventEnvelopeBuilder<TEvent>  (fluent; default null)
    .Build()                                                     → EventEnvelope<TEvent>  (wraps EventEnvelope.Wrap<TEvent>(...))
    NOTE: Gives messaging/integration test setups a clean way to construct envelopes without knowing
          every metadata field.

PagedListAssertions  (static class — extension methods on PagedList<T>)
    .ShouldHaveTotalCount(this PagedList<T> list, int expected)  → void
    .ShouldHaveItems(this PagedList<T> list, params T[] expected) → void
    .ShouldBeEmpty(this PagedList<T> list)                       → void
    NOTE: Plain exception-throwing boolean checks — ZERO FluentAssertions reference, correcting an
          earlier superseded-phase draft that specified a FluentAssertions implementation. This
          package's standing hard rule (no assertion-library dependency of its own) takes precedence.
          Proven in SelfTests.

AddFakeContractsServices(this IServiceCollection)                                    [STATUS: Deferred — P-064/WO-012]
    NOTE: Deferred — none of the above Contracts/ helpers currently need DI registration (all are
          static or directly instantiable builders, consistent with the Security/Persistence/Clocks
          convention). Add only if a concrete DI-backed need surfaces in a future phase.
```

### `Security/` — auth mocks (12.Security)

```text
FakeUserContext  (sealed class, implements IUserContext)                            [STATUS: Planned]
    .UserId                                                    → Guid                      (settable; default: a fixed non-empty test Guid)
    .Email                                                     → string?                   (settable)
    .Username                                                  → string?                   (settable)
    .Roles                                                     → IReadOnlyCollection<string> (settable; default: empty)
    .Claims                                                    → IReadOnlyDictionary<string,string> (settable; default: empty)
    .IsAuthenticated                                            → bool                      (settable; default: true)
    .HasRole(string role)                                      → bool  (case-insensitive Roles.Contains)
    NOTE: Distinct from 12.Security's AnonymousUserContext, which is an immutable production
          fallback sentinel (always IsAuthenticated == false). FakeUserContext defaults to an
          authenticated user so most test setups need zero configuration; call the mutators to
          exercise unauthenticated or role-restricted paths explicitly.

FakeTenantProvider  (sealed class, implements ITenantProvider)                      [STATUS: Planned]
    .TenantId                                                  → Guid  (settable)
    constructor(Guid? tenantId = null)                         — defaults to a fixed non-empty test Guid, NOT Guid.Empty
    NOTE: Defaulting to a real tenant id (rather than Guid.Empty) means tenant-scoped code under
          test exercises the tenanted path by default. Set TenantId = Guid.Empty explicitly to
          test the no-tenant-resolved path.
```

### `Messaging/` — in-process bus/publisher doubles (07.Messaging)

```text
InMemoryMessageBus  (sealed class, implements IMessageBus)
    .PublishAsync<T>(T message, CancellationToken ct)                               → Task
    .PublishAsync<T>(T message, Action<PublishContext> configure, CancellationToken ct) → Task
    .SendAsync<T>(T command, CancellationToken ct)                                  → Task
    .RequestAsync<TRequest,TResponse>(TRequest request, CancellationToken ct)       → Task<TResponse>
        Configurable via SetResponseHandler<TRequest,TResponse>(Func<TRequest,TResponse>) — when no
        handler is registered for the requested type pair, throws InvalidOperationException with a
        descriptive message naming the missing TRequest/TResponse pair. CORRECTED (P-183/WO-029):
        supersedes an earlier draft of this contract that specified NotSupportedException; the
        carried-forward, dispatched P-183 spec is authoritative.
    .SetResponseHandler<TRequest,TResponse>(Func<TRequest,TResponse> handler)        → void
        Registers (or replaces) the handler RequestAsync<TRequest,TResponse> delegates to.
    .ExecuteRoutingSlipAsync(object routingSlip, CancellationToken ct)               → Task
        Throws NotSupportedException — true routing-slip orchestration requires a real broker
        round-trip; tests needing that fidelity use MassTransit's own TestHarness instead.
    .ShouldHavePublished<T>()                                  → T  (returns the recorded message; throws if none found)
    .ShouldHaveSent<T>()                                       → T  (returns the recorded message; throws if none found)
    .ShouldHavePublishedOnce<T>()                               → T  (throws if zero or more than one)
    .ShouldNotHavePublished<T>()                                → void  (throws if any recorded)
    NOTE: Records every PublishAsync/SendAsync call (message type + instance) into a thread-safe
          list even when no assertion is ever made. Assertion helpers are read-only queries over
          that list — they never mutate state, and return the matched message for further assertion
          chaining. Root CLAUDE.md WO-022 commitment; carried forward unchanged by P-183/WO-029
          (the canonical spec — supersedes the earlier, less complete P-011/P-124 drafts).

InMemoryEventPublisher  (sealed class, implements IEventPublisher)
    .PublishAsync<TEvent>(TEvent integrationEvent, CancellationToken ct)             → Task
    .PublishAsync<TEvent>(TEvent integrationEvent, Action<PublishContext> configure, CancellationToken ct) → Task
    .Published                                                 → IReadOnlyList<object>
    .PublishedOf<TEvent>()                                     → IReadOnlyList<TEvent>
    NOTE: Does not wrap events in EventEnvelope<TEvent> — that's a MassTransitEventPublisher-specific
          transport concern (07.Messaging). This double records the raw TEvent instances only.
          Thread-safe under concurrent publish.

AddInMemoryMessageBus(this IServiceCollection)
AddInMemoryEventPublisher(this IServiceCollection)
    NOTE: Both register their respective double as a SINGLETON — a deliberate, documented deviation
          from IMessageBus/IEventPublisher's production "scoped" lifetime rule (07.Messaging). Tests
          need the same recorder instance to outlive the DI scope used by the system under test so
          assertions can run after the action completes.

TestHarnessFactory  (static class)
    .CreateAsync(string serviceName, Action<IBusRegistrationConfigurator>? configure = null) → Task<ITestHarness>
        CORRECTED at implementation time: exposed as CreateAsync (not the originally planned
        synchronous-looking Create) since starting a harness via ITestHarness.Start() is inherently
        asynchronous. Calls AddMassTransitTestHarness, applies SetKebabCaseEndpointNameFormatter and
        the configure callback, builds the provider, then starts and returns the harness.
        Configures a MassTransit.Testing.ITestHarness with platform defaults
        (KebabCaseEndpointNameFormatter, the given serviceName, pre-registered ConsumerBase<T>
        subclasses discovered via the configure callback).
    NOTE: May reference SharedKernel.Messaging.MassTransit as a test-only dependency — this is
          permitted because 16.Testing is never shipped inside a production artifact. InMemoryMessageBus
          and InMemoryEventPublisher themselves remain reference-isolated to
          SharedKernel.Messaging.Abstractions only; TestHarnessFactory is a separate file/type that
          carries the heavier MassTransit reference.
```

### `Persistence/` — connection factory fake and EF Core test helpers (06.Persistence)

```text
FakeDbConnectionFactory  (sealed class, implements IDbConnectionFactory)            [STATUS: Planned — documented gap, see 13.ServiceDefaults Test Rules]
    constructor(Func<IDbConnection> connectionFactory)
    .CreateConnectionAsync(CancellationToken ct)                → Task<IDbConnection>
        Returns connectionFactory() wrapped in Task.FromResult — no real ADO.NET connection opened.
    NOTE: Deliberately takes a caller-supplied Func<IDbConnection> rather than constructing its own
          substitute — this package must never take a hard dependency on a mocking framework
          (NSubstitute, Moq, etc.). The consuming .Tests project builds its own IDbConnection
          substitute (typically via NSubstitute, per the platform's Standard Test Package Set) and
          passes it in. Closes the gap noted in 13.ServiceDefaults's Test Rules ("16.Testing has no
          IDbConnectionFactory fake yet").

TestSharedKernelDbContext  (abstract class, extends SharedKernelDbContext)
    — preconfigured with the SQLite in-memory provider
    — wires a no-op IUserContext (fixed "test-user") so AuditInterceptor resolves without a real HTTP context
    — wires a deterministic IClock (fixed snapshot, never real time) for stable interceptor timestamps
    — calls EnableSensitiveDataLogging() for readable test diagnostics
    .EnsureCreatedAsync()                                       → Task  (no migrations needed for SQLite tests)
    NOTE: Proven alongside 06.Persistence.EfCore's own test suite — extends a production base class
          and exercises real interceptor contracts.

AggregateRootFaker<TAggregate, TId>  (abstract class, extends Bogus.Faker<TAggregate>)
    where TAggregate : AggregateRoot<TId>  where TId : notnull
    — pre-configures CreatedBy / CreatedOn / IsDeleted = false to match EF interceptor expectations

TenantedAggregateFaker<TAggregate, TId>  (abstract class, extends AggregateRootFaker<TAggregate, TId>)
    — additionally populates a non-empty TenantId

EfContextExtensions  (static class)
    .DetachAll(this DbContext context)                          → void  (detaches all tracked entities for a fresh same-database reload within one test)
    .ReloadAsync<T>(this DbContext context, T entity)            → Task<T?>  (loads a fresh copy via a new scoped DbContext instance, asserts round-trip persistence)
    .RegisterOptions(DbContext context, DbContextOptions options) → void  [ADDED during Tests phase, T-21]
        Registers options keyed by context instance (ConditionalWeakTable<DbContext, DbContextOptions>)
        so ReloadAsync<T> can construct an equivalent fresh context. EF Core's internal per-context
        service provider does not register DbContextOptions/DbContextOptions<TContext> as a resolvable
        service for a context built standalone (outside AddDbContext) — ReloadAsync<T> throws
        InvalidOperationException if a context's options were never registered. TestSharedKernelDbContext
        calls this automatically from its own constructor; any other DbContext subclass passed to
        ReloadAsync<T> must call it manually first.

ProjectionSpecificationBuilder<TAggregate, TResult>  (sealed class — fluent builder)
    .WithCriteria(Expression<Func<TAggregate, bool>> criteria)   → ProjectionSpecificationBuilder<TAggregate, TResult>  (fluent)
    .WithSelector(Expression<Func<TAggregate, TResult>> selector) → ProjectionSpecificationBuilder<TAggregate, TResult>  (fluent)
    .Build()                                                     → IProjectionSpecification<TAggregate, TResult>
    NOTE: Produces IProjectionSpecification instances without a full concrete spec class per test.
          Standalone builder, no owning consuming-domain interface (it produces instances of an
          interface, it does not implement one itself) — proven in SelfTests.

BulkAggregateFaker<TAggregate, TId>  (sealed class wrapping an AggregateRootFaker<TAggregate, TId>)
    — generates a configurable-count List<TAggregate> via Bogus with all audit fields populated,
      for seeding bulk AddRangeAsync integration tests. Proven in SelfTests.

WithDeletedSpecification<TAggregate>  (sealed class, extends Specification<TAggregate>)
    static .Wrap(ISpecification<TAggregate> inner)               → ISpecification<TAggregate>
        Returns a copy of inner with IncludeDeleted = true; the original specification instance is
        left untouched. For soft-delete integration tests. Proven in SelfTests.

PersistenceTestHelpers  (static class)
    .AssertEntityTracked<T>(DbContext context, T entity)         → void  (throws if ChangeTracker reports EntityState.Detached)
    .AssertEntityNotTracked<T>(DbContext context, T entity)      → void  (throws if ChangeTracker reports any tracked state)
    NOTE: Fills the coverage gap around AsNoTracking behavioral verification. Proven in SelfTests.

SCOPE LOCK (P-182/WO-029): No OutboxMessageFaker, OutboxAssertions, or PostgreSQL Testcontainer
    dependency is introduced by this set — explicitly out of scope, deferred to a future work order.
```

### `Containers/` — Testcontainers fixtures

```text
PostgreSqlContainerFixture  (sealed class, implements IAsyncLifetime)
    .ConnectionString                                          → string  (throws InvalidOperationException if read before InitializeAsync completes)
    .InitializeAsync()                                         → Task  (starts a pinned postgres image via Testcontainers.PostgreSql)
    .DisposeAsync()                                            → Task  (stops and removes the container)

RedisContainerFixture  (sealed class, implements IAsyncLifetime)
    .ConnectionString                                          → string
    .InitializeAsync() / .DisposeAsync()                       — same shape, wraps Testcontainers.Redis
    NOTE: Must be independently usable by each of the four split 02.Caching capability .Tests
          projects (FusionCache, Redis L2, DistributedLocking, HashStore, PubSub) without requiring
          all four capabilities wired up simultaneously (P-185/WO-029 alignment check).

RabbitMqContainerFixture  (sealed class, implements IAsyncLifetime)
    .ConnectionString                                          → string
    .InitializeAsync() / .DisposeAsync()                       — same shape, wraps Testcontainers.RabbitMq

    NOTE: Each fixture is consumed via xUnit's [CollectionDefinition] + ICollectionFixture<T> — one
          container instance shared across an entire test collection, never started per test method.
          Image tags are pinned (no ":latest") so CI runs are reproducible. These fixtures back the
          Testcontainers requirements already documented in 02.Caching (Redis), 06.Persistence
          (PostgreSQL/Dapper), and 07.Messaging (RabbitMQ) Test Rules sections. Centralizing here does
          not, in this pass, change those domains' existing inline Testcontainers setup — a future
          migration phase in each consuming domain adopts these fixtures.
```

### `Fakers/` — Bogus convention (02.Caching-and-beyond, cross-cutting)

```text
FakerSeeding  (static class)                                                        [STATUS: Planned]
    .Apply(int seed = 8675309)                                 → void
        Sets Bogus.Randomizer.Seed = new Random(seed). Call once per test assembly (e.g. from an
        xUnit AssemblyFixture or module initializer) so every Faker<T> in that run is deterministic
        across CI re-executions.
    NOTE: Concrete Faker<TAggregate> definitions for business entities are NOT defined in this
          package — they depend on each microservice's own aggregate shapes and belong in that
          service's own test project. SharedKernel.Testing ships only the shared determinism
          convention every one of those fakers should opt into.
```

### `Communication/` — cross-cutting Communication test doubles (11.Communication) — merges superseded P-158 + P-168

```text
MockServiceEndpointResolver  (sealed class, implements IServiceEndpointResolver)
    .Configure(string serviceName, Uri uri)                     → void  (registers a fixed resolution result for serviceName)
    .GetResolvedNames()                                         → IReadOnlyList<string>  (every serviceName ever passed to ResolveAsync, for assertion)
    NOTE: Supports per-service failure injection; never throws on an unconfigured name (falls back
          to a deterministic non-throwing default, mirroring the production resolver's "never throws"
          contract). References SharedKernel.Communication.Internal only.

FakeHttpContextAccessor  (sealed class, implements IHttpContextAccessor)
    constructor(HttpContext? context = null)
    — holds a fixed (or null) HttpContext with a configurable TenantId on its backing ITenantProvider
    NOTE: Consolidates ad-hoc duplicate fakes currently in SharedKernel.Communication.Rest.Tests and
          .Grpc.Tests. References Microsoft.AspNetCore.Http and SharedKernel.Security.Abstractions.

HttpClientHandlerTestFactory  (sealed class — fluent builder)
    .WithInnerHandler(HttpMessageHandler handler)                → HttpClientHandlerTestFactory  (fluent)
    .WithCorrelationIdHandler()                                  → HttpClientHandlerTestFactory  (fluent)
    .WithTenantIdHandler(Guid? tenantId)                         → HttpClientHandlerTestFactory  (fluent)
    .Build()                                                     → HttpMessageHandler  (returns the outermost handler for direct HttpClient construction)
    NOTE: Builds a pre-wired DelegatingHandler chain without a full ServiceCollection. References
          Microsoft.Extensions.Http only.

FakeHttpMessageHandler  (sealed class, extends HttpMessageHandler)
    — supports fixed and sequenced response fixtures (e.g., first call 503, second 200) for
      resilience-policy testing
    — allows post-call HttpRequestMessage inspection (which headers were injected)
    NOTE: Folded in from the superseded P-158.

AmbientActivityTestHelper  (sealed class, implements IDisposable)
    static .Start(ActivityTraceId traceId, ActivitySpanId? parentSpanId = null) → AmbientActivityTestHelper
        Sets Activity.Current to a new Activity with the given trace id (and optional parent span id);
        Dispose() restores the prior Activity.Current so no test leaks ambient state into the next one.
    .Activity                                                   → Activity  (the ambient activity created by Start)
    NOTE: Folded in from the superseded P-158. CORRECTED during the Tests phase (T-31): ActivitySource.
          StartActivity returns null when no ActivityListener is sampling the source — the default
          outside an OTel-instrumented host, which is every pure unit test using this helper. Fixed by
          registering a single static always-sampling ActivityListener scoped to this type's private
          ActivitySource only — a second documented exception to the "no static mutable state" rule
          below, same class as FakerSeeding.Apply (process-wide, deliberate, opt-in; never incidental
          shared state).

TestServerCallContext  (static factory / sealed helper)
    — produces a ServerCallContext-equivalent for testing gRPC interceptors in isolation, allowing
      post-execution metadata inspection
    NOTE: Folded in from the superseded P-158.

GraphQLTestExecutorFactory  (static class)
    — wires AddSharedKernelGraphQL() with test-safe defaults (AllowIntrospection = true, MaxPageSize = 10)
      onto an IRequestExecutorBuilder
    NOTE: Folded in from the superseded P-158.

SCOPE LOCK (P-186/WO-029): SharedKernel.Testing must never take a project reference to
    SharedKernel.Communication.Rest, .Grpc, or .GraphQL. MockServiceEndpointResolver may reference
    SharedKernel.Communication.Internal for IServiceEndpointResolver only. Once these helpers exist,
    the ad-hoc duplicate fakes in SharedKernel.Communication.Rest.Tests and .Grpc.Tests must be
    removed in favor of them (tracked as a cross-domain follow-up, not a file edit performed here).
```

### `ServiceDefaults/` — tenant resolution and health check test doubles (13.ServiceDefaults) — carried forward unchanged from superseded P-174

```text
StaticTenantProvider  (sealed class, implements ITenantProvider)
    constructor(Guid tenantId)                                  — also usable with Guid.Empty for the no-tenant case
    .TenantId                                                   → Guid  (fixed at construction)
    NOTE: Implements a 12.Security-owned interface directly — trivial deterministic tenant context
          without standing up AmbientTenantProvider + middleware + HTTP context.

FakeTenantResolutionStrategy  (sealed class — structurally compatible, NOT a direct ITenantResolutionStrategy implementation)
    constructor(Guid? fixedResult = null)
    constructor(Func<HttpContext, CancellationToken, Task<Guid?>> resolver)
    .StrategyName                                               → string  (settable; mirrors ITenantResolutionStrategy.StrategyName shape)
    .TryResolveAsync(HttpContext context, CancellationToken ct)  → Task<Guid?>  (mirrors ITenantResolutionStrategy.TryResolveAsync signature)
    NOTE: Deliberately structural rather than a direct interface implementation — referencing
          ITenantResolutionStrategy directly would require a project reference to
          SharedKernel.MultiTenancy (13.ServiceDefaults), which is out of scope for this package per
          the scope lock below. A consuming service's test project that does take that reference can
          still use this type as a drop-in (duck-typed) substitute since the member shapes match
          exactly.

HealthCheckAssertionExtensions  (static class — extension methods on HealthCheckRegistration)
    .ShouldBeTaggedReady(this HealthCheckRegistration registration)    → void  (throws if "ready" tag absent)
    .ShouldNotBeTaggedLive(this HealthCheckRegistration registration)  → void  (throws if "live" tag present)
    NOTE: Verifies tag composition without booting a WebApplicationFactory. References
          Microsoft.Extensions.Diagnostics.HealthChecks only.

SCOPE LOCK (P-187/WO-029): SharedKernel.Testing must never take a project reference to
    SharedKernel.ServiceDefaults or SharedKernel.MultiTenancy. StaticTenantProvider and
    FakeTenantResolutionStrategy reference only SharedKernel.Security.Abstractions.
```

---

## Implementation Rules

- `SharedKernel.Testing` ships **no test runner, no assertion library, and no mocking framework** as a dependency of its own `.csproj` — only the minimal `Testcontainers.*`, `Bogus`, and `xunit.core` packages strictly required to implement fixtures and faker conventions. The Standard Test Package Set (xUnit runner, `FluentAssertions`, `NSubstitute`) is added per-`.Tests`-project, never transitively through this package. This includes the new `PagedListAssertions` (`Contracts/`) and `DomainVersionAssertions` (`Domain/`) — both are plain exception-throwing helpers, never FluentAssertions-backed, correcting an earlier superseded-phase draft for `PagedListAssertions`.
- Sibling capability folders (`Caching/`, `Domain/`, `Contracts/`, `Security/`, `Messaging/`, `Persistence/`, `Clocks/`, `Containers/`, `Communication/`, `ServiceDefaults/`, `Fakers/`) must **never reference each other**. Each fake depends only on the single abstraction package it implements (e.g., `FakeCacheService` → `SharedKernel.Caching.Abstractions` only). Mirrors the platform's sibling-package-isolation rule already enforced in `02.Caching`. Standalone helpers with no owning abstraction (`SpecificationTestBuilder<T>`, `ProjectionSpecificationBuilder<TAggregate,TResult>`, etc.) depend only on the domain types they operate over, never on a sibling folder's fake types.
- Every fake is a **`sealed` class** — no inheritance extension point. Tests compose behavior via constructor parameters and mutable properties (`SimulateFailure`, `IsAuthenticated`, etc.), never by subclassing a fake. Abstract bases (`EntityFaker<TEntity,TId>`, `SingleValueObjectFaker<TValueObject,TValue>`, `AggregateRootFaker<TAggregate,TId>`, `TenantedAggregateFaker<TAggregate,TId>`, `TestSharedKernelDbContext`) are the deliberate, documented exception — they exist specifically to be subclassed by consuming test projects, unlike fakes which are leaf types.
- Any fake holding mutable shared state (caches, recorded message lists) must use a **thread-safe collection** (`ConcurrentDictionary`, `ConcurrentQueue`) — xUnit runs test collections in parallel by default. `FakeCacheInvalidationBus.PublishedInvalidations` and its registered handler list follow the same rule.
- Fakes simulate **behavioral correctness, not timing** — no fake enforces TTL/expiry/sliding-window semantics from `CachePolicy`, retry backoff, or any other time-based production behavior unless a test explicitly drives a `FakeClock`. Real `Task.Delay`/`Thread.Sleep` is forbidden anywhere in this package.
- Container fixtures implement `IAsyncLifetime` **exclusively** — never a synchronous constructor that blocks on `.Result`/`.Wait()` to start a container. Container images are referenced by a **pinned tag**, never `:latest`.
- Container fixtures are scoped per xUnit `ICollectionFixture<T>` — **one instance per test collection**, never started per test method. This is the same pattern already mandated by `02.Caching`, `06.Persistence`, and `07.Messaging`'s Test Rules for Redis/PostgreSQL/RabbitMQ Testcontainers.
- `InMemoryMessageBus`/`InMemoryEventPublisher` record **every** `PublishAsync`/`SendAsync` call, even when the test never asserts on it. Assertion helpers (`ShouldHavePublished<T>()`, `PublishedOf<TEvent>()`, etc.) are read-only queries over the recorded list — they must never mutate it.
- `InMemoryMessageBus.RequestAsync` is **configurable** via `SetResponseHandler<TRequest,TResponse>(Func<TRequest,TResponse>)`, throwing a descriptive `InvalidOperationException` when no handler is registered for the requested type pair — **corrected** by P-183/WO-029; an earlier draft of this rule specified `NotSupportedException`, which is now superseded. `InMemoryMessageBus.ExecuteRoutingSlipAsync` still throws `NotSupportedException` by design — true routing-slip orchestration requires a real broker round-trip; tests needing that fidelity use MassTransit's `TestHarness` (`07.Messaging`'s own Test Rules, or this package's own `TestHarnessFactory`), not the in-memory double.
- `AddInMemoryMessageBus()`/`AddInMemoryEventPublisher()` register their double as a **singleton**, intentionally diverging from `IMessageBus`/`IEventPublisher`'s production scoped lifetime — documented explicitly at the point of registration so no consumer mistakes this for the production DI shape.
- `TestHarnessFactory` is the **only** type in `Messaging/` permitted to carry a `SharedKernel.Messaging.MassTransit` reference — `InMemoryMessageBus`/`InMemoryEventPublisher` remain isolated to `SharedKernel.Messaging.Abstractions` only.
- `FakeDbConnectionFactory` never constructs its own connection substitute — it wraps a caller-supplied `Func<IDbConnection>` so this package never takes a hard dependency on a mocking framework.
- `FakeClock` must default to a **fixed, non-real `DateTimeOffset`** — never `DateTimeOffset.UtcNow` — so any test that forgets to configure it explicitly still runs deterministically across time zones and CI machines. `TestSharedKernelDbContext`'s wired-in `IClock` follows the same rule.
- `SpecificationAssert`/`SpecificationTestBuilder<T>` use `ISpecification<T>.Criteria.Compile()` — reflection-based expression compilation, acceptable in this test-only package, **never** acceptable in production code per `03.Domain`'s own documented constraint on `Specification<T>.IsSatisfiedBy`.
- `FakeTenantResolutionStrategy` (`ServiceDefaults/`) is **structurally compatible** with `13.ServiceDefaults`'s `ITenantResolutionStrategy`, not a direct interface implementation — this package takes no project reference to `SharedKernel.MultiTenancy`. The same "structural, not direct" pattern applies to any future fake whose owning interface lives in a package this domain has chosen not to reference.
- No static mutable state anywhere in this domain, with two documented exceptions: `Bogus.Randomizer.Seed` set via `FakerSeeding.Apply` (a deliberate, opt-in, process-wide determinism convention), and the single static, always-sampling `ActivityListener` registered by `AmbientActivityTestHelper` scoped to its own private `ActivitySource` (required so `ActivitySource.StartActivity` returns a real `Activity` in pure unit tests with no OTel host listening). Both are deliberate, opt-in, process-wide — never incidental shared state.

---

## DI Registration (expected shape)

```csharp
// Messaging test doubles — singleton by design, see Implementation Rules
services.AddInMemoryMessageBus();
services.AddInMemoryEventPublisher();

// Caching fakes — one call registers all four as singletons
services.AddFakeCachingServices();

// Caching fakes — manual per-fake alternative remains valid for callers who want a subset
services.AddSingleton<ICacheService, FakeCacheService>();
services.AddSingleton<IDistributedLockService, FakeDistributedLockService>();
services.AddSingleton<ITenantCacheKeyProvider, FakeTenantCacheKeyProvider>();
services.AddSingleton<ICacheInvalidationBus, FakeCacheInvalidationBus>();

// Domain test helpers — registers FakeClock as IClock only
services.AddFakeDomainServices();
```

Fakes in `Security/`, `Persistence/`, `Clocks/` (outside `AddFakeDomainServices()`'s narrow `IClock` registration), `Contracts/`, `Communication/`, and `ServiceDefaults/` are intentionally **not** wrapped in `Add*` DI extensions — they are simple `new`-able classes or static helpers with test-controlled constructor parameters, and registering them via DI adds indirection most unit tests don't need. Only doubles that exist specifically to be swapped in for a production DI registration (caching, messaging, the single `IClock` registration in `AddFakeDomainServices()`) ship a convenience extension. `AddFakeContractsServices()` is explicitly **deferred** (P-064/WO-012) — none of the `Contracts/` helpers currently need DI registration; add it only if a concrete need surfaces.

---

## AOT Compatibility

AOT guidance does **not** apply to this domain. `16.Testing` packages are never referenced by production code (root `CLAUDE.md` Hard Rule) and are never published as part of an AOT-compiled service. Fakes, faker conventions, and container fixtures may freely use reflection-based conveniences (e.g., Bogus's expression-tree rule builders, `Activator`-based test object construction) without AOT review. This section exists only for consistency with the other domain brains' structure.

---

## Test Rules

- **This package has no nested `.Tests` project of its own.** A fake's correctness is defined entirely by its conformance to the interface it implements — best verified in the `.Tests` project of the domain that owns that interface, where the real implementation's contract tests already exist as a comparison baseline. Today: `FakeCacheService`/`FakeDistributedLockService`/`FakeRenewableLock`/`FakeTenantCacheKeyProvider` are exercised through `02.Caching`'s own test suites (e.g., `SharedKernel.Caching.Redis.DistributedLocking.Tests` references `FakeRenewableLock` directly via `ProjectReference`), not through a dedicated `SharedKernel.Testing.Tests` project. This is a deliberate, documented exception to the platform's otherwise-universal "every package gets a nested `.Tests` project" rule.
- **`SharedKernel.Testing.SelfTests` (added WO-029) is a narrow, documented exception to the rule above.** It exists exclusively for standalone testing-infrastructure logic that has no consuming-domain-owned interface to anchor against — fluent builders (`SpecificationTestBuilder`, `ProjectionSpecificationBuilder`), assertion-helper classes (`PagedListAssertions`, `DomainVersionAssertions`, `HealthCheckAssertionExtensions`), faker-seeding conventions, and recorder/double self-checks (e.g., `InMemoryMessageBus`'s own `ShouldHavePublished` behavior). Decision rule for every future addition to this domain: does the type implement an interface owned by another numbered domain? If yes, it is still proven via that domain's own contract tests — unchanged, per the rule above. If no — it has no owning domain interface to anchor against — it is proven in `SharedKernel.Testing.SelfTests` instead. This project carries the Standard Test Package Set (xUnit runner, FluentAssertions, NSubstitute) as direct package references; those dependencies must never leak into `SharedKernel.Testing.csproj` itself.
- When a new fake is added to this package, the consuming domain's existing contract/behavioral tests for the real interface are the acceptance bar — the fake must satisfy the same documented pre/post-conditions (e.g., `FakeCacheService.GetManyAsync` must return a dictionary entry for every requested key, exactly like the production `FusionCacheService`/`RedisL2BatchService` behavior documented in `02.Caching`).
- New container fixtures must be proven against the integration test suite of the domain that needs them first (e.g., a new `RedisContainerFixture` proves itself by replacing whatever ad-hoc Testcontainers setup `02.Caching.Redis.Tests` currently rolls inline) before being treated as the canonical shared fixture.
- Any behavioral fix to an existing fake (e.g., correcting `FakeCacheService`'s tag-eviction semantics) must be cross-checked against every domain's test suite that currently consumes it — grep for the fake's type name across `**/*.Tests/` before changing its public behavior, since multiple domains' test suites assert against it without their own copy.
- Determinism is non-negotiable: any test failure traced back to a fake's internal use of real time, real randomness, or real I/O (outside the deliberate `Containers/` fixtures) is a bug in the fake, not a flaky test to retry.
- `StaticTenantProvider` (implements `ITenantProvider` directly) is proven against `12.Security`'s own `ITenantProvider` contract-shape tests where one exists; `SharedKernel.Testing.SelfTests` is the documented fallback only when no such generic contract-shape test exists there.
- `FakeTenantResolutionStrategy` is proven in `SharedKernel.Testing.SelfTests` unconditionally — it is structurally compatible with `ITenantResolutionStrategy`, not a direct implementation, so there is no owning-domain interface to anchor a "prove it there" rule against.
- Cross-cutting `Communication/` test doubles (`MockServiceEndpointResolver` excepted — see below) are proven in `SharedKernel.Testing.SelfTests` because no single `11.Communication` package owns all of them collectively; `MockServiceEndpointResolver` implements `IServiceEndpointResolver` directly and may additionally be exercised by `11.Communication.Internal`'s own suite if a duplicate-removal pass (see `Communication/` SCOPE LOCK note) wires it in.
- **The "prove it in the owning domain's suite" rule has a practical fallback, observed repeatedly during the Tests phase: if no consuming domain's existing `.Tests` project actually exercises the fake/fixture directly, prove it in `SharedKernel.Testing.SelfTests` instead, even when the type implements an interface owned elsewhere.** Confirmed cases: `FakeClock` — `01.Core` can never take a `ProjectReference` to `16.Testing` (it sits below this domain in the layering rules and references nothing), so `SharedKernel.Primitives.Tests` necessarily rolls its own private nested fake rather than referencing this package's `FakeClock`; `FakeCacheService.GetManyAsync`/`SetManyAsync`, `FakeTenantCacheKeyProvider`, `FakeCacheInvalidationBus` — no `02.Caching` test project exercised these three directly (only incidental DI-registration usage existed); `TestSharedKernelDbContext`, `AggregateRootFaker`/`TenantedAggregateFaker`, `EfContextExtensions` — net-new types with no consumer yet in `06.Persistence.EfCore.Tests` despite its `ProjectReference` to this package; `StaticTenantProvider` — `12.Security.Abstractions.Tests` carries no `ProjectReference` to this package and no generic `ITenantProvider` contract-shape test exists there. This is not a workaround — it is the documented fallback the original phase specs (P-181, P-187) anticipated; treat "prove it there" as the default and "no actual consumer exists yet" as the trigger for the `SelfTests` fallback, re-checked at the time each fake is proven rather than assumed from the interface's owning domain alone.

---

## Changelog

> Maintained by `/sync-brain` and a future testing-arch-planner domain agent. One line per significant change.

- [2026-06-22] Domain brain initialized — packages, technology stack, folder/namespace map, full interface contracts (implemented: `Caching/` fakes; planned: `Clocks/`, `Security/`, `Messaging/`, `Persistence/`, `Containers/`, `Fakers/`), implementation rules, DI registration shape, AOT note, test rules. Derived from the root `CLAUDE.md` folder-map entry for `16.Testing`, the root WO-022 commitment to `InMemoryMessageBus`/`InMemoryEventPublisher`, and the cross-domain expectations already load-bearing in `02.Caching`, `06.Persistence`, `07.Messaging`, `11.Communication`, and `13.ServiceDefaults`'s own `CLAUDE.md` files (Testcontainers PostgreSQL/Redis/RabbitMQ, the `FakeCacheService`/`FakeDistributedLockService`/`FakeRenewableLock`/`FakeTenantCacheKeyProvider` already shipped, and the documented `IDbConnectionFactory` fake gap) — no dedicated testing-arch-planner agent exists yet, so this pass is an arch-lead-equivalent placeholder pending formal phase breakdown in `state-map.md` (arch-lead-equivalent pass)
- [2026-06-22] WO-029: `SharedKernel.Testing.SelfTests` package added — narrow exception to the no-nested-`.Tests` rule for standalone helpers with no owning consuming-domain interface; Test Rules amended with the decision rule; consolidates 14 long-pending phases into 9 (P-179–P-187) (arch-lead)
- [2026-06-22] P-035/WO-008 + P-064/WO-012 + P-179..P-187/WO-029 processed in full (137 tasks written to `state-map.md` across all 6 phases): Folder/Namespace Map gains `Domain/`, `Contracts/`, `Communication/`, `ServiceDefaults/` (new) and documents the `Fakers/`-vs-`Domain/` split (construction-time vs. post-condition helpers); full target-shape interface contracts added for `FakeClock`, `EntityFaker<TEntity,TId>`, `DomainEventAssertions` (incl. P-181 extensions `ContainsEventWithVersion<T>`/`HasRaisedExactlyNEvents`), `BusinessRuleAssertions`, `SpecificationAssert`, `AddFakeDomainServices()` (P-035); `PagedListBuilder<T>`, `EnvelopeAssertions`, `IntegrationEventFaker<TEvent>`, `EventEnvelopeBuilder<TEvent>` (P-064); `FakeCacheInvalidationBus` + unified `AddFakeCachingServices()` added to `Caching/` — `FakeCacheService.GetManyAsync`/`SetManyAsync` and `FakeTenantCacheKeyProvider` verified against spec with zero code drift (P-180); `SingleValueObjectFaker<TValueObject,TValue>`, `DomainVersionAssertions`, `SpecificationTestBuilder<T>`, `FakeDomainNotFoundException` (P-181); `TestSharedKernelDbContext`, `AggregateRootFaker`/`TenantedAggregateFaker`, `EfContextExtensions`, `ProjectionSpecificationBuilder<TAggregate,TResult>`, `PagedListAssertions` (corrected to zero-FluentAssertions per standing hard rule), `BulkAggregateFaker`, `WithDeletedSpecification<TAggregate>`, `PersistenceTestHelpers` (P-182, explicitly scoped out: no Outbox fakers, no PostgreSQL Testcontainer dependency); `InMemoryMessageBus`/`InMemoryEventPublisher`/`TestHarnessFactory` finalized with `RequestAsync` **corrected** from an earlier `NotSupportedException` draft to the dispatched `SetResponseHandler<TRequest,TResponse>`-configurable design (P-183, supersedes P-011/P-124); `PostgreSqlContainerFixture`/`RedisContainerFixture`/`RabbitMqContainerFixture` markers updated `[STATUS: Planned]` with design finalized (P-184); Redis topology alignment confirmed, no code drift (P-185); `MockServiceEndpointResolver`, `FakeHttpContextAccessor`, `HttpClientHandlerTestFactory`, `FakeHttpMessageHandler`, ambient `Activity` helper, gRPC `ServerCallContext` stub, GraphQL test-executor factory added to new `Communication/` section, merging superseded P-158+P-168 (P-168 wins on conflict) (P-186); `StaticTenantProvider`, `FakeTenantResolutionStrategy` (structurally compatible, no `SharedKernel.MultiTenancy` reference), `HealthCheckAssertionExtensions` added to new `ServiceDefaults/` section (P-187, carried forward unchanged from superseded P-174). Implementation Rules, DI Registration, and Test Rules sections updated throughout to reflect all of the above (testing-arch-planner)
- [2026-06-23] `SK.16.Core` implemented in full (C-01–C-43): all 43 target-shape contracts from the Design phase landed as real code across `Clocks/`, `Domain/`, `Contracts/`, `Caching/`, `Persistence/`, `Messaging/`, `Containers/`, `Communication/`, `ServiceDefaults/`. Remaining stale `[STATUS: Planned — P-035/WO-008]` tag on the `Domain/`-section `FakeClock` cross-reference removed (the real implementation lives in `Clocks/FakeClock.cs`, already documented without a status tag). `dotnet build` on `SharedKernel.Testing.csproj` succeeds with 0 errors (pre-existing `NU1903`/`CS1574` warnings only, none originating from this package). `[STATUS: Planned]` tags intentionally left in place for `FakeUserContext`/`FakeTenantProvider` (`Security/`), `FakeDbConnectionFactory` (`Persistence/`), and `FakerSeeding` (`Fakers/`) — none of these were in this Core phase's task list (C-01–C-43); they remain future work. `SK.16.Tests` is next (testing-phase-implementer).
- [2026-06-23] `SK.16.Tests` implemented in full (T-01–T-34); 209/209 tests passing in `SharedKernel.Testing.SelfTests` (container-fixture lifecycle tests excluded from this count, requiring Docker). New Test Rules bullet added documenting the SelfTests fallback observed repeatedly in practice — most interface-implementing fakes had no actual consumer in their owning domain's `.Tests` project yet, so they were proven in `SelfTests` instead of "there," even though the original rule's default was "prove it in the owning domain." Two real bugs found and fixed during implementation, now documented in their respective Interface Contracts blocks: `EfContextExtensions` gained a new public `RegisterOptions(DbContext, DbContextOptions)` API (`TestSharedKernelDbContext` calls it automatically) because `ReloadAsync<T>` could not resolve `DbContextOptions` from a standalone context's internal service provider; `AmbientActivityTestHelper` now registers a single static always-sampling `ActivityListener` scoped to its own `ActivitySource` (a second documented exception to the "no static mutable state" rule, alongside `FakerSeeding.Apply`) because `ActivitySource.StartActivity` returns `null` outside an OTel-instrumented host. Stale "(planned —  ...)" parenthetical notes removed from the DI Registration code samples now that `AddFakeCachingServices()`/`AddFakeDomainServices()`/`AddInMemoryMessageBus()` are all implemented. `SK.16.Docs` is next (testing-phase-implementer).
- [2026-06-24] `SK.16.Docs` completed (DO-01–DO-10) without further code changes — every public type's XML doc-comment coverage was verified (not assumed) against the live `.cs` files via a line-count check against public-member declarations, and every doc-only `CLAUDE.md` claim from prior phases ("done in this pass" for DO-03/04/08/09) was independently re-checked against the current file content rather than trusted from earlier agent summaries. `dotnet build` re-confirmed 0 errors. `SK.16.Published` is next (testing-phase-implementer).
- [2026-06-24] `SK.16.Published` completed (P-01, P-02) — formally records the `ProjectReference`-only consumption model for both packages; new "Publishing / Consumption Model" subsection added under Packages documenting this explicitly. Verified rather than assumed: `SharedKernel.Testing.SelfTests.csproj` already had `<IsPackable>false</IsPackable>`, but `SharedKernel.Testing.csproj` had no `IsPackable` property at all — the SDK default (`true`) meant `dotnet pack` would have attempted to pack it, contradicting the documented decision. Fixed by adding `<IsPackable>false</IsPackable>` explicitly to `SharedKernel.Testing.csproj`. `[STATUS: Planned]` markers on `FakeUserContext`/`FakeTenantProvider` (`Security/`) and `FakerSeeding` (`Fakers/`) confirmed still correctly unimplemented on disk (no `Security/` folder exists; no `FakerSeeding.cs` in `Fakers/`) — out of scope for every phase to date, left unflipped. `dotnet build` succeeds 0 errors. All 6 phases of `16.Testing` (Design→Published) now complete — domain closed (WO-008/WO-012/WO-029) (testing-phase-implementer).
