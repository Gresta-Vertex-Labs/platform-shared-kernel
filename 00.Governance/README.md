# 00.Governance — Usage Guide

This document covers how to consume each package in the `00.Governance` capability domain:
`SharedKernel.Analyzers`, `SharedKernel.ArchitectureTests`, `SharedKernel.Linter`, and
`SharedKernel.Benchmarks`.

---

## Contents

- [SharedKernel.Analyzers — Roslyn Diagnostics](#sharedkernelanalyzers--roslyn-diagnostics)
  - [Referencing the Analyzer Package](#referencing-the-analyzer-package)
  - [SK0001 — DirectDateTimeUsage](#sk0001-directdatetimeusage)
  - [SK0002 — DirectMicrosoftFeatureManagerUsage](#sk0002-directmicrosoftfeaturemanagerusage)
  - [SK0003 — RawExceptionThrow](#sk0003-rawexceptionthrow)
  - [SK0004 — NullErrorReturn](#sk0004-nullerrorreturn)
  - [SK0005 — StringOnlyExceptionConstructor](#sk0005-stringonlyexceptionconstructor)
  - [SK0006 — GuardClauseThrow](#sk0006-guardclausethrow)
  - [SK0007 — RedisChannelServiceMessagingSubstitute](#sk0007-redischannelservicemessagingsubstitute)
  - [SK0008 — AggregateRootDispatchCoupling](#sk0008-aggregaterootdispatchcoupling)
  - [SK0009 — DomainEventMissingVersionAttribute](#sk0009-domaineventmissingversionattribute)
  - [SK0010 — SpecificationOrderingConflict](#sk0010-specificationorderingconflict)
  - [SK0201 — TenantedDbContextOnModelCreatingGuard](#sk0201-tenanteddbcontextonmodelcreatingguard)
  - [SK0202 — IgnoreQueryFiltersOutsideTenantedRepository](#sk0202-ignorequeryfiltersoutsidetenantedrepository)
  - [SK0703 — MessageBusSingletonRegistration](#sk0703-messagebussingletonregistration)
  - [SK0704 — HardcodedQueueUriInGetSendEndpoint](#sk0704-hardcodedqueueuriingetsendendpoint)
  - [SK0705 — FaultConsumerDirectRegistration](#sk0705-faultconsumerdirectregistration)
  - [SK0706 — DirectMassTransitSchedulerInjection](#sk0706-directmasstransitschedulerinjection)
  - [SK0707 — SagaStateMustExtendSagaStateBase](#sk0707-sagastatemustextendsagastatebase)
  - [SK0708 — BatchConsumerRegisteredViaAddConsumer](#sk0708-batchconsumerregisteredviaaddconsumer)
- [SharedKernel.ArchitectureTests — Layering Rules](#sharedkernelarchitecturetests--layering-rules)
  - [Referencing the Package](#referencing-the-package)
  - [Using SharedKernelLayeringRules](#using-sharedkernellayeringrules)
  - [Subclassing ArchitectureRuleBase](#subclassing-architecturerulebase)
  - [GuardPurityRules — Guard Clause Purity Enforcement](#guardpurityrules--guard-clause-purity-enforcement)
  - [CachingAbstractionRules — Caching Boundary Enforcement](#cachingabstractionrules--caching-boundary-enforcement)
  - [DomainLayerPurityRules — Domain Layer Purity Enforcement](#domainlayerpurityrules--domain-layer-purity-enforcement)
  - [DomainGoldStandardRules — Domain Convention Enforcement](#domaingoldstandardrules--domain-convention-enforcement)
  - [ContractsPurityRules — Contracts Layer Purity Enforcement](#contractspurityrules--contracts-layer-purity-enforcement)
  - [MessagingArchitectureRules — Messaging Boundary Enforcement](#messagingarchitecturerules--messaging-boundary-enforcement)
  - [ExtendedMessagingArchitectureRules — Extended Messaging Misuse Enforcement](#extendedmessagingarchitecturerules--extended-messaging-misuse-enforcement)
- [SharedKernel.Linter — EditorConfig and CSharpier](#sharedkernellinter--editorconfig-and-csharpier)
  - [Applying the Linter Package](#applying-the-linter-package)
  - [CI Enforcement](#ci-enforcement)
  - [Skipping the CSharpier Check Locally](#skipping-the-csharpier-check-locally)
- [SharedKernel.Benchmarks — Benchmark Configuration](#sharedkernelbenchmarks--benchmark-configuration)

---

## SharedKernel.Analyzers — Roslyn Diagnostics

`SharedKernel.Analyzers` is a Roslyn analyzer NuGet package. It ships no runtime DLL — only
the analyzer assembly (targeting `netstandard2.0`) that the compiler host loads. Diagnostics
appear at compile time in the IDE and in `dotnet build` output.

### Referencing the Analyzer Package

Add the reference to any project that should be checked:

```xml
<ItemGroup>
  <PackageReference Include="SharedKernel.Analyzers" Version="1.0.0">
    <!-- Analyzers are development dependencies — they are not transitively inherited -->
    <PrivateAssets>all</PrivateAssets>
    <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
  </PackageReference>
</ItemGroup>
```

All rules (SK0001–SK0010) are enabled by default at `Warning` severity. To suppress a
rule project-wide, add it to `<NoWarn>`:

```xml
<PropertyGroup>
  <!-- Suppress SK0001 for a project that legitimately manages time directly -->
  <NoWarn>$(NoWarn);SK0001</NoWarn>
</PropertyGroup>
```

To suppress a single occurrence inline, use a `#pragma` directive:

```csharp
#pragma warning disable SK0001
var now = DateTime.UtcNow; // intentional — this class implements IClock
#pragma warning restore SK0001
```

---

### SK0001 — DirectDateTimeUsage

**Category:** Usage  
**Severity:** Warning

#### Rationale

Direct access to `DateTime.UtcNow`, `DateTime.Now`, or `DateTimeOffset.UtcNow` couples code
to the system clock, making it impossible to control time in unit tests. All time-dependent
code should obtain the current instant via `IClock` (from `SharedKernel.FeatureManagement`),
injected via DI.

The rule is suppressed automatically for code inside the `SharedKernel.Primitives` namespace,
where the clock interface itself is defined.

#### Violating Example

```csharp
public class OrderService
{
    public Order CreateOrder()
    {
        // SK0001: Direct access to 'DateTime.UtcNow' is not allowed
        return new Order { CreatedAt = DateTime.UtcNow };
    }
}
```

#### Compliant Fix

```csharp
public class OrderService
{
    private readonly IClock _clock;

    public OrderService(IClock clock) => _clock = clock;

    public Order CreateOrder() =>
        new Order { CreatedAt = _clock.UtcNow };
}
```

#### Suppression Instructions

Suppress inline with `#pragma warning disable SK0001 / restore SK0001`, or project-wide via
`<NoWarn>$(NoWarn);SK0001</NoWarn>` in the `.csproj`.

---

### SK0002 — DirectMicrosoftFeatureManagerUsage

**Category:** Usage  
**Severity:** Warning

#### Rationale

`Microsoft.FeatureManagement.IFeatureManager` is a concrete infrastructure interface tied to
Microsoft's feature flag implementation. Depending on it directly in application or domain
code locks the codebase to that implementation and prevents swapping feature flag providers.
Use `SharedKernel.FeatureManagement.IFeatureManager` — the SharedKernel abstraction — instead.

#### Violating Example

```csharp
using Microsoft.FeatureManagement;

public class FeatureService
{
    // SK0002: Do not reference 'Microsoft.FeatureManagement.IFeatureManager' directly
    private readonly IFeatureManager _featureManager;

    public FeatureService(IFeatureManager featureManager)
        => _featureManager = featureManager;
}
```

#### Compliant Fix

```csharp
using SharedKernel.FeatureManagement;

public class FeatureService
{
    private readonly IFeatureManager _featureManager;

    public FeatureService(IFeatureManager featureManager)
        => _featureManager = featureManager;
}
```

#### Suppression Instructions

Suppress inline with `#pragma warning disable SK0002 / restore SK0002`, or project-wide via
`<NoWarn>$(NoWarn);SK0002</NoWarn>`.

---

### SK0003 — RawExceptionThrow

**Category:** Design  
**Severity:** Warning

#### Rationale

`throw new Exception("message")` and `throw new ApplicationException("message")` are raw
exception throws that carry no structured error information. SharedKernel uses the `Result<T>`
pattern for expected failure paths and typed exceptions (e.g., `DomainException`,
`NotFoundException`) carrying an `Error` payload for unexpected failures. Throwing raw base
exceptions bypasses both mechanisms and makes error handling inconsistent.

The rule fires **only** when the concrete thrown type is exactly `System.Exception` or
`System.ApplicationException` — not on subclasses. `throw new ArgumentException(...)` is
not flagged.

#### Violating Example

```csharp
public void ProcessOrder(Order order)
{
    if (order is null)
        // SK0003: Throwing 'Exception' directly is not allowed
        throw new Exception("Order cannot be null");
}
```

#### Compliant Fix — functional path

```csharp
public Result<Order> ProcessOrder(Order? order)
{
    if (order is null)
        return Result<Order>.Failure(Error.Validation("Order.Null", "Order cannot be null"));

    return Result<Order>.Success(order);
}
```

#### Compliant Fix — typed exception

```csharp
public void ProcessOrder(Order order)
{
    if (order is null)
        throw new DomainException(Error.Validation("Order.Null", "Order cannot be null"));
}
```

#### Suppression Instructions

Suppress inline with `#pragma warning disable SK0003 / restore SK0003`, or project-wide via
`<NoWarn>$(NoWarn);SK0003</NoWarn>`.

---

### SK0004 — NullErrorReturn

**Category:** Design  
**Severity:** Warning

#### Rationale

`Error` is a value type designed to convey structured failure information. Returning `null`
from a method declared to return `Error` or `Error?` is semantically incorrect: it signals
"no error" via nullability rather than via `Error.None`, which is the canonical sentinel.
Using `Error.None` keeps the intent explicit and avoids null-checks at call sites.

#### Violating Example

```csharp
public Error? ValidateAge(int age)
{
    if (age < 0)
        return Error.Validation("Age.Negative", "Age must be non-negative");

    // SK0004: Returning null for type 'Error' is not allowed
    return null;
}
```

#### Compliant Fix

```csharp
public Error? ValidateAge(int age)
{
    if (age < 0)
        return Error.Validation("Age.Negative", "Age must be non-negative");

    return Error.None;
}
```

#### Suppression Instructions

Suppress inline with `#pragma warning disable SK0004 / restore SK0004`, or project-wide via
`<NoWarn>$(NoWarn);SK0004</NoWarn>`.

---

### SK0005 — StringOnlyExceptionConstructor

**Category:** Design  
**Severity:** Warning

#### Rationale

`SharedKernelException` subclasses (e.g., `DomainException`, `NotFoundException`) are designed
to carry a structured `Error` payload that includes a code, message, and optional metadata.
Constructing them with a plain string bypasses the Error system, producing exceptions that
cannot be correlated with error codes or translated into `ProblemDetails` responses.

The rule fires when a type derived from `SharedKernelException` is constructed with exactly
one argument that is a string literal.

#### Violating Example

```csharp
public void FindOrder(Guid id)
{
    // SK0005: 'NotFoundException' is constructed with a string-only argument
    throw new NotFoundException($"Order {id} was not found");
}
```

#### Compliant Fix

```csharp
public void FindOrder(Guid id)
{
    throw new NotFoundException(
        Error.NotFound("Order.NotFound", $"Order {id} was not found")
    );
}
```

#### Suppression Instructions

Suppress inline with `#pragma warning disable SK0005 / restore SK0005`, or project-wide via
`<NoWarn>$(NoWarn);SK0005</NoWarn>`.

---

### SK0006 — GuardClauseThrow

**Category:** Design  
**Severity:** Warning

#### Rationale

The SharedKernel guard system follows a strict two-path contract:

- **Functional path** (`Guard.Against.*`) — extension methods on `IGuardClause` must be pure:
  they return `Error?` (null on pass, non-null on violation) and must **never throw**. Throwing
  on the functional path breaks railway-oriented composition and forces callers to wrap every
  guard call in a try/catch.

- **Imperative path** (`Guard.Throw.*`) — the `Guard.Throw` nested companion class is the only
  sanctioned location where throwing `DomainException` is permitted.

SK0006 fires when any method that is part of the functional path (a method on a type
implementing `IGuardClause`, or an extension method whose first `this` parameter is
`IGuardClause`) contains a `throw` statement or throw expression. The `Guard.Throw` companion
class is excluded: methods declared in a class named `Throw` nested inside a class named
`Guard` are always exempt.

#### Two-Path Contract

```text
Guard.Against.*    →  IGuardClause extension methods  →  return Error?   (pure — NO throw)
Guard.Throw.*      →  Guard.Throw static nested class  →  throw DomainException  (imperative)
```

#### Exclusion List

The following types are **never** flagged by SK0006:

| Type                                                    | Reason                                               |
|---------------------------------------------------------|------------------------------------------------------|
| Any type named `Throw` nested inside a type named `Guard` | Legitimate imperative path — throwing is its purpose |

#### Violating Example

```csharp
using SharedKernel.Guards.Clauses;

namespace MyProject.Guards
{
    public static class AgeGuardExtensions
    {
        // SK0006: method on IGuardClause extension must not throw
        public static Error? NegativeAge(this IGuardClause guard, int age, string paramName)
        {
            if (age < 0)
                throw new ArgumentOutOfRangeException(paramName, "Age cannot be negative");

            return null;
        }
    }
}
```

#### SK0006 Compliant Fix — functional path

```csharp
using SharedKernel.Guards.Clauses;
using SharedKernel.Primitives.Errors;

namespace MyProject.Guards
{
    public static class AgeGuardExtensions
    {
        // Functional path: return Error? — null means no violation
        public static Error? NegativeAge(this IGuardClause guard, int age, string paramName) =>
            age < 0
                ? Error.Validation($"{paramName}.Negative", $"'{paramName}' must be non-negative")
                : null;
    }
}
```

#### SK0006 Compliant Fix — imperative path (for scenarios where throwing is desired)

```csharp
namespace MyProject.Guards
{
    public static partial class Guard
    {
        public static class Throw
        {
            // Imperative path: throwing inside Guard.Throw is permitted — SK0006 excludes this
            public static void NegativeAge(int age, string paramName)
            {
                var error = Against.NegativeAge(age, paramName);
                if (error is not null)
                    throw new DomainException(error);
            }
        }
    }
}
```

#### Suppression Instructions

Suppress inline with `#pragma warning disable SK0006 / restore SK0006`, or project-wide via
`<NoWarn>$(NoWarn);SK0006</NoWarn>`.

Note: Suppression should be rare. If your method legitimately needs to throw, move it to a
`Guard.Throw`-equivalent companion class rather than suppressing the diagnostic.

---

### SK0007 — RedisChannelServiceMessagingSubstitute

**Category:** Design  
**Severity:** Warning

#### Rationale

`IRedisChannelService` is a Redis pub/sub channel abstraction designed exclusively for
**ephemeral, non-durable** signals — cache invalidation hints, presence notifications, and
other fire-and-forget messages where loss is acceptable. It is **not a message bus**.

When `IRedisChannelService` is injected into a class whose name or enclosing namespace
signals durable-messaging intent — anything containing `Command`, `Event`, `DomainEvent`,
or `IntegrationEvent` — it is almost certainly a substitute for `IMessageBus`
(`SharedKernel.Messaging.Abstractions`). This substitution produces silent message loss
under Redis failure, broker restarts, or network partition, with no dead-letter queue, no
retry, and no audit trail.

SK0007 fires on the injection site (constructor parameter, field, or property declaration)
rather than on every use, so it is triggered once per structural coupling point.

#### Suppression Namespace

The rule is automatically suppressed inside the `SharedKernel.Caching` and
`SharedKernel.Caching.Redis` namespaces. These are the only locations where `IRedisChannelService`
is defined and legitimately referenced at the structural level. Any additional exemption must
be documented in `00.Governance/CLAUDE.md` under the `CachingAbstractionRules` exemption list
before applying a suppress pragma.

#### Violating Example

```csharp
namespace Application.Commands
{
    // SK0007: 'IRedisChannelService' is injected in a messaging-context class
    public class PlaceOrderCommandHandler
    {
        private readonly IRedisChannelService _channel;

        public PlaceOrderCommandHandler(IRedisChannelService channel)
            => _channel = channel;
    }
}
```

#### Compliant Fix

```csharp
using SharedKernel.Messaging.Abstractions;

namespace Application.Commands
{
    public class PlaceOrderCommandHandler
    {
        private readonly IMessageBus _bus;

        public PlaceOrderCommandHandler(IMessageBus bus)
            => _bus = bus;
    }
}
```

For cache invalidation signals that genuinely belong in a command handler, inject both
`IMessageBus` (for the command/event) and `IRedisChannelService` (for the cache hint) — but
give the Redis channel field a name that makes its ephemeral purpose clear (e.g.,
`_cacheInvalidation`).

#### Suppression Instructions

Suppress inline with `#pragma warning disable SK0007 / restore SK0007`, or project-wide via
`<NoWarn>$(NoWarn);SK0007</NoWarn>`.

Before suppressing, confirm that the `IRedisChannelService` usage is genuinely for cache
invalidation, not as a substitute for a durable bus. If in doubt, use `IMessageBus`.

---

### SK0008 — AggregateRootDispatchCoupling

**Category:** Design  
**Severity:** Warning

#### Rationale

Infrastructure dispatch code — interceptors, event publishers, outbox processors, and
dispatchers — needs to raise domain events from aggregates. It does not need the full
`IAggregateRoot<TId>` surface (entity identity, version, invariant checking). Injecting
`IAggregateRoot<TId>` in dispatch-context classes creates an unnecessary coupling to the
aggregate identity contract; if the `IAggregateRoot<TId>` interface changes (e.g., `TId`
is renamed, the version contract is extended), all dispatch code breaks.

The `IHasDomainEvents` interface is the narrowest correct coupling: dispatch code only needs
to dequeue and publish domain events, which is exactly what `IHasDomainEvents` exposes.

SK0008 fires when a constructor parameter is typed as `IAggregateRoot<>` (simple name check)
inside a class whose name or any enclosing namespace contains `Interceptor`, `Publisher`,
`Outbox`, or `Dispatcher`.

#### Violating Example

```csharp
namespace Infrastructure.Messaging
{
    // SK0008: Use 'IHasDomainEvents' instead of 'IAggregateRoot<TId>' in dispatch code
    public class DomainEventPublisher
    {
        public DomainEventPublisher(IAggregateRoot<Guid> aggregate) { }
    }
}
```

#### Compliant Fix

```csharp
namespace Infrastructure.Messaging
{
    public class DomainEventPublisher
    {
        public DomainEventPublisher(IHasDomainEvents aggregate) { }
    }
}
```

#### Suppression Instructions

Suppress inline with `#pragma warning disable SK0008 / restore SK0008`, or project-wide via
`<NoWarn>$(NoWarn);SK0008</NoWarn>`.

---

### SK0009 — DomainEventMissingVersionAttribute

**Category:** Design  
**Severity:** Warning

#### Rationale

Domain events are part of the public, versioned schema of a bounded context. When a domain
event property is added, removed, or renamed, every consumer that deserializes the old wire
format is silently broken. `[DomainEventVersion]` encodes the current schema version
on each concrete event type and forces the developer to acknowledge every breaking change
(by incrementing the version number), creating a visible audit trail in the git history.

SK0009 fires on every non-abstract class or record that declares `IDomainEvent` in its
base list but does not carry a `[DomainEventVersion]` attribute. Abstract base event
classes are exempt because they do not represent wire-format contracts — only their concrete
subclasses do.

#### Violating Example

```csharp
// SK0009: 'OrderCreatedEvent' implements 'IDomainEvent' but is missing '[DomainEventVersion]'
public record OrderCreatedEvent(Guid OrderId, DateTimeOffset CreatedAt) : IDomainEvent;
```

#### Compliant Fix

```csharp
[DomainEventVersion(1)]
public record OrderCreatedEvent(Guid OrderId, DateTimeOffset CreatedAt) : IDomainEvent;
```

When a breaking property change is made (add, remove, or rename a property), increment the
version:

```csharp
[DomainEventVersion(2)]  // bumped: added 'CustomerId' property
public record OrderCreatedEvent(
    Guid OrderId,
    Guid CustomerId,
    DateTimeOffset CreatedAt) : IDomainEvent;
```

#### Suppression Instructions

Suppress inline with `#pragma warning disable SK0009 / restore SK0009`, or project-wide via
`<NoWarn>$(NoWarn);SK0009</NoWarn>`.

---

### SK0010 — SpecificationOrderingConflict

**Category:** Design  
**Severity:** Warning

#### Rationale

`Specification<T>` constructors may call `ApplyOrderBy(...)` and/or
`ApplyOrderByDescending(...)` to set the primary sort direction. Calling both in the same
constructor body creates an ordering conflict: the query engine will use whichever call
is applied last, producing non-deterministic sort results that depend on source code order
rather than explicit intent.

SK0010 fires on the constructor identifier when both `ApplyOrderBy` and
`ApplyOrderByDescending` are called within the same constructor body. Calling only one is
always clean.

#### Violating Example

```csharp
public class ActiveOrdersSpec : Specification<Order>
{
    public ActiveOrdersSpec()
    {
        AddCriteria(o => o.IsActive);
        // SK0010: Constructor calls both 'ApplyOrderBy' and 'ApplyOrderByDescending'
        ApplyOrderBy(o => o.CreatedAt);
        ApplyOrderByDescending(o => o.Total);
    }
}
```

#### Compliant Fix

Choose a single primary sort direction. Use `ThenBy` / `ThenByDescending` for secondary
sorts if the specification supports it:

```csharp
public class ActiveOrdersSpec : Specification<Order>
{
    public ActiveOrdersSpec()
    {
        AddCriteria(o => o.IsActive);
        ApplyOrderByDescending(o => o.Total);
    }
}
```

#### Suppression Instructions

Suppress inline with `#pragma warning disable SK0010 / restore SK0010`, or project-wide via
`<NoWarn>$(NoWarn);SK0010</NoWarn>`.

---

### SK0201 — TenantedDbContextOnModelCreatingGuard

**Category:** Design | **Severity:** Warning | **ID Block:** 02xx (multi-tenancy)

#### Rationale

`TenantedDbContext.OnModelCreating` registers the global EF Core query filter that restricts
all queries to the current tenant's rows. Any subclass that overrides `OnModelCreating` and
omits `base.OnModelCreating(...)` silently removes this filter — all subsequent queries return
rows across all tenant boundaries without any error, warning, or runtime exception. This is a
silent data-leak class of bug.

SK0201 enforces that every `TenantedDbContext` override of `OnModelCreating` contains at least
one of:

- `base.OnModelCreating(modelBuilder)` — the normal case
- `ApplyTenantFilters(modelBuilder)` (simple name, or any explicit receiver) — for advanced
  multi-context patterns where the base call must be deferred

**Limitation:** The check is syntax-only within a single file. If the inheritance chain spans
multiple files (e.g., `MyContext → IntermediateContext → TenantedDbContext` across three
files), only the immediate `BaseList` is inspected. For such chains, document the ancestry in
a comment near the override.

#### Violating Example

```csharp
public class OrderDbContext : TenantedDbContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // SK0201: 'OrderDbContext.OnModelCreating' overrides TenantedDbContext but does not
        // call 'base.OnModelCreating' or 'ApplyTenantFilters' — the global tenant query
        // filter will be silently removed
        modelBuilder.Entity<Order>().ToTable("Orders");
    }
}
```

#### Compliant Fix

```csharp
public class OrderDbContext : TenantedDbContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder); // ← registers the global tenant filter
        modelBuilder.Entity<Order>().ToTable("Orders");
    }
}
```

Or when the filter must be applied explicitly:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    ApplyTenantFilters(modelBuilder); // ← explicit filter registration
    modelBuilder.Entity<Order>().ToTable("Orders");
}
```

#### CI Configuration

To escalate to an error in CI:

```xml
<PropertyGroup>
  <WarningsAsErrors>$(WarningsAsErrors);SK0201</WarningsAsErrors>
</PropertyGroup>
```

Or via `.editorconfig`:

```
[*.cs]
dotnet_diagnostic.SK0201.severity = error
```

#### Suppression Instructions

Suppress per-site with `#pragma warning disable SK0201` when the tenant filter is intentionally
omitted or applied via a mechanism not detectable at syntax level. Always add an inline comment
explaining the rationale:

```csharp
#pragma warning disable SK0201 // Tenant filter applied via custom OnModelFinalized — see TenantBootstrap.cs
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    // ... custom wiring that defers filter registration
}
#pragma warning restore SK0201
```

---

### SK0202 — IgnoreQueryFiltersOutsideTenantedRepository

**Category:** Design | **Severity:** Warning | **ID Block:** 02xx (multi-tenancy)

#### Rationale

`IgnoreQueryFilters()` bypasses **all** EF Core global query filters registered on a
`DbContext`, including the tenant isolation filter applied by `TenantedDbContext`. Calling it
from application-layer services, MediatR handlers, or arbitrary repositories silently returns
rows across all tenant boundaries without any audit trail or intentional decision record.

The only sanctioned call sites are:

1. **Inside `SharedKernel.Persistence.EfCore*` namespaces** — the persistence implementation
   layer may use `IgnoreQueryFilters()` deliberately (e.g., soft-delete cleanup jobs,
   cross-tenant admin APIs that are purpose-built within the platform).
2. **Inside a class named `TenantedRepository` (exact match)** — the designated cross-tenant
   repository base class is the single sanctioned call site outside the platform namespace.

#### Violating Example

```csharp
namespace Application.Repositories
{
    public class OrderQueryService
    {
        private readonly IQueryable<Order> _query;

        public IQueryable<Order> GetAllOrdersAcrossTenants()
        {
            // SK0202: 'IgnoreQueryFilters()' must only be called inside the
            // 'SharedKernel.Persistence.EfCore' namespace or from 'TenantedRepository'
            return _query.IgnoreQueryFilters();
        }
    }
}
```

#### Compliant Fix — use TenantedRepository

```csharp
// In SharedKernel.Persistence.EfCore (or a class named TenantedRepository):
public abstract class TenantedRepository
{
    protected IQueryable<T> CrossTenantQuery<T>(IQueryable<T> source)
        => source.IgnoreQueryFilters(); // ← exempt: inside TenantedRepository
}

// In application layer — call the named method, never IgnoreQueryFilters() directly:
public class AdminOrderService : TenantedRepository
{
    public IReadOnlyList<Order> GetAllOrders()
        => CrossTenantQuery(_orders).ToList();
}
```

#### Exemption List

| Exemption | Scope | Rationale |
|-----------|-------|-----------|
| Namespace prefix `SharedKernel.Persistence.EfCore` | All sub-namespaces | Platform persistence layer — deliberate cross-tenant operations are permitted here |
| Class name `TenantedRepository` (exact) | Single designated base class | Provides controlled cross-tenant query access with a named, auditable surface |

To add a new exemption, document it in `00.Governance/CLAUDE.md` under the SK0202 rule entry
**before** applying a `#pragma warning disable SK0202` suppression.

#### CI Configuration

To escalate to an error in CI:

```xml
<PropertyGroup>
  <WarningsAsErrors>$(WarningsAsErrors);SK0202</WarningsAsErrors>
</PropertyGroup>
```

Or via `.editorconfig`:

```
[*.cs]
dotnet_diagnostic.SK0202.severity = error
```

#### Suppression Instructions

Suppress per-site with `#pragma warning disable SK0202`. Always document the business reason
and who approved the cross-tenant access:

```csharp
#pragma warning disable SK0202 // Cross-tenant purge — approved by infra-team, ticket INF-4421
var deletedCount = await _context.Set<AuditLog>()
    .IgnoreQueryFilters()
    .Where(x => x.CreatedAt < cutoff)
    .ExecuteDeleteAsync(ct);
#pragma warning restore SK0202
```

---

### SK0703 — MessageBusSingletonRegistration

**Category:** Usage | **Severity:** Warning | **ID block:** 07xx (messaging-domain)

SK0703 fires when `AddSingleton` is invoked with a type argument whose simple name starts with
`"IMessageBus"` or `"IEventPublisher"`.

**Rationale:** MassTransit's consume pipeline creates a new scope for each consumed message.
Registering `IMessageBus` or `IEventPublisher` as a singleton causes scope pollution and race
conditions under concurrent load — the singleton instance is shared across all consume scopes
and loses per-scope state (outbox entries, correlation IDs, etc.). The correct lifetime is
`Scoped`, which aligns with the MassTransit per-consume-scope model.

**Offending pattern:**

```csharp
// SK0703: Singleton registration causes scope pollution under concurrent message processing
services.AddSingleton<IMessageBus, MassTransitMessageBus>();
services.AddSingleton<IEventPublisher, MassTransitEventPublisher>();
```

**Compliant pattern:**

```csharp
// Correct: Scoped lifetime aligns with MassTransit's per-consume-scope model
services.AddScoped<IMessageBus, MassTransitMessageBus>();
services.AddScoped<IEventPublisher, MassTransitEventPublisher>();
```

**Suppression:** Per-call-site only — use `#pragma warning disable SK0703` exclusively when
the DI container semantics are provably equivalent to scoped behaviour. Always document the
reason inline:

```csharp
#pragma warning disable SK0703 // Test harness uses a no-op singleton stub — not a real MassTransit bus
services.AddSingleton<IMessageBus, NullMessageBus>();
#pragma warning restore SK0703
```

**CI configuration:**
```xml
<WarningsAsErrors>$(WarningsAsErrors);SK0703</WarningsAsErrors>
```

---

### SK0704 — HardcodedQueueUriInGetSendEndpoint

**Category:** Usage | **Severity:** Warning | **ID block:** 07xx (messaging-domain)

SK0704 fires when `GetSendEndpoint` is called with a `new Uri(...)` argument whose string
literal value starts with `"queue:"` or `"exchange:"` (case-insensitive).

**Rationale:** Hardcoded queue or exchange URI strings tie producers to a specific broker
topology. When the queue name, exchange name, or transport changes (e.g., RabbitMQ →
Azure Service Bus, or a queue rename during a rolling deployment), every call site must be
updated manually. Convention-based endpoint resolution via
`IEndpointNameFormatter.GetDestinationAddress<TMessage>()` centralises the naming concern
and survives broker configuration changes automatically.

**Offending pattern:**
```csharp
// SK0704: hardcoded "queue:" URI ties producer to RabbitMQ topology
var endpoint = await provider.GetSendEndpoint(new Uri("queue:order-commands"));

// SK0704: hardcoded "exchange:" URI also flagged
var endpoint = await provider.GetSendEndpoint(new Uri("exchange:order-events"));
```

**Compliant pattern:**
```csharp
// Convention-based resolution — survives broker changes and rename refactors
var address = _formatter.GetDestinationAddress<OrderCommand>();
var endpoint = await provider.GetSendEndpoint(address);
```

**Non-literal form (not flagged):**
```csharp
// No diagnostic: the argument is a variable, not a string literal
var endpoint = await provider.GetSendEndpoint(new Uri(configuredAddress));
```

**Suppression:** Per-call-site only — use `#pragma warning disable SK0704` when a fixed,
environment-invariant queue address is genuinely required (e.g., a dead-letter queue URI in
an isolated test fixture). Always document the rationale inline:

```csharp
#pragma warning disable SK0704 // Dead-letter queue — fixed broker-internal address, not service-configurable
var dlq = await provider.GetSendEndpoint(new Uri("queue:dead-letter"));
#pragma warning restore SK0704
```

**CI configuration:**
```xml
<WarningsAsErrors>$(WarningsAsErrors);SK0704</WarningsAsErrors>
```

---

### SK0705 — FaultConsumerDirectRegistration

**Category:** Usage | **Severity:** Warning | **ID block:** 07xx (messaging-domain)

SK0705 fires when `AddScoped` or `AddSingleton` is invoked with a type argument that is a
generic `IFaultConsumer<TMessage>` reference — covering both
`AddScoped<IFaultConsumer<TMessage>, TImpl>()` and `AddSingleton<IFaultConsumer<TMessage>>()`.

**Rationale:** Fault consumers must be wired through
`MessagingBusBuilder.AddFaultConsumer<TMessage, TConsumer>()`, which registers the
MassTransit `Fault<T>` adapter that translates a raw MassTransit fault context into the
platform `IFaultConsumer<T>` abstraction. Direct DI registration via `AddScoped` or
`AddSingleton` bypasses this adapter chain entirely — no MassTransit consumer subscribes to
`Fault<T>` on the registered type's behalf, so the fault consumer is never invoked.

**Offending pattern:**
```csharp
// SK0705: bypasses the Fault<T> adapter chain — OrderFaultConsumer is never invoked
services.AddScoped<IFaultConsumer<OrderPlaced>, OrderFaultConsumer>();
services.AddSingleton<IFaultConsumer<OrderPlaced>>();
```

**Compliant pattern:**
```csharp
// Correct: wires the MassTransit Fault<T> adapter to the platform abstraction
builder.AddFaultConsumer<OrderPlaced, OrderFaultConsumer>();
```

**Suppression:** Per-call-site only — use `#pragma warning disable SK0705` only when
explicitly bypassing the builder is intentional. Document the rationale inline:

```csharp
#pragma warning disable SK0705 // Test double registered for unit-test DI container only
services.AddScoped<IFaultConsumer<OrderPlaced>, FakeFaultConsumer>();
#pragma warning restore SK0705
```

**CI configuration:**
```xml
<WarningsAsErrors>$(WarningsAsErrors);SK0705</WarningsAsErrors>
```

---

### SK0706 — DirectMassTransitSchedulerInjection

**Category:** Design | **Severity:** Warning | **ID block:** 07xx (messaging-domain)

SK0706 is implemented as a NetArchTest `ICustomRule`
(`NoDirectSchedulerInjectionOutsideMessagingPredicate`), not a per-call-site Roslyn analyzer
— it is enforced at the assembly level via
`ExtendedMessagingArchitectureRules.NoDirectMassTransitSchedulerInjection`. It fires when a
constructor parameter is typed `MassTransit.IMessageScheduler` (`ParameterType.Name ==
"IMessageScheduler"` AND `ParameterType.Namespace.StartsWith("MassTransit")`) in a type
whose namespace does **not** start with `SharedKernel.Messaging`.

**Rationale:** `MassTransit.IMessageScheduler` is an implementation detail of the
MassTransit transport. Injecting it directly in application handlers, domain services, or
controllers couples that code to a specific scheduler implementation, making transport
swaps impossible and creating an invisible MassTransit dependency in layers that should be
transport-agnostic. `SharedKernel.Messaging.Abstractions.IMessageScheduler` is the only
permitted scheduler injection point outside `SharedKernel.Messaging.*`.

**Exemption list:**

- Types whose `TypeDefinition.Namespace` starts with `"SharedKernel.Messaging"` —
  the messaging adapter layer (`SharedKernel.Messaging.Abstractions` and
  `SharedKernel.Messaging.MassTransit`) may reference `MassTransit.IMessageScheduler`
  freely for internal adapter wiring. Any additional exemption must be documented here
  before it is applied in code.

**Offending pattern:**
```csharp
using MassTransit;

// SK0706: injects the MassTransit transport scheduler directly
public class ScheduleReminderHandler(IMessageScheduler scheduler)
{
}
```

**Compliant pattern:**
```csharp
using SharedKernel.Messaging.Abstractions;

// Correct: injects the platform scheduler abstraction — transport-independent
public class ScheduleReminderHandler(IMessageScheduler scheduler)
{
}
```

**Failure message:** `"{offendingType} injects MassTransit.IMessageScheduler directly. Use
SharedKernel.Messaging.Abstractions.IMessageScheduler to preserve transport independence."`

---

### SK0707 — SagaStateMustExtendSagaStateBase

**Category:** Design | **Severity:** Warning | **ID block:** 07xx (messaging-domain)

SK0707 is implemented as a NetArchTest `ICustomRule`
(`SagaStateMustExtendSagaStateBasePredicate`), enforced at the assembly level via
`ExtendedMessagingArchitectureRules.SagaStatesMustExtendSagaStateBase`. It fires when a
type whose `TypeDefinition.Interfaces` contains an entry with `InterfaceType.Name ==
"ISaga"` does not have `SagaStateBase` anywhere in its `BaseType` inheritance chain.

**Rationale:** `SagaStateBase` (from `SharedKernel.Messaging.MassTransit`) provides the
platform-standard `CorrelationId`, `Version` (optimistic concurrency counter), `CreatedAt`,
and `ModifiedAt` audit fields required for correct saga state persistence and
version-conflict resolution. A saga state class that implements `ISaga` without extending
`SagaStateBase` is missing these fields, causing saga persistence to fail
version-conflict detection and breaking the platform observability pipeline.

**Exemption list:** None — every `ISaga` implementor must extend `SagaStateBase`.
`SagaStateBase` itself is self-exempt (it implements `ISaga` but cannot extend itself).

**Fail-open limitation:** if `BaseType.Resolve()` returns `null` at any step in the
inheritance chain walk (the base type lives in an assembly that was not loaded), the
predicate treats the type as possibly-compliant (`true`) to avoid false positives in
assembly-isolation test setups. This is a documented limitation, not an exemption — if a
saga state type's non-loadable base is itself a `SagaStateBase` descendant, this rule will
not catch a missing `SagaStateBase` further up an unresolved chain.

**Offending pattern:**
```csharp
// SK0707: implements ISaga but does not extend SagaStateBase — no Version/audit fields
public class OrderSagaState : ISaga
{
    public Guid CorrelationId { get; set; }
}
```

**Compliant pattern:**
```csharp
// Correct: extends SagaStateBase — carries CorrelationId, Version, CreatedAt, ModifiedAt
public class OrderSagaState : SagaStateBase
{
}
```

**Failure message:** `"{offendingType} implements ISaga but does not extend
SagaStateBase. All saga state classes must extend SagaStateBase to carry correlation ID,
version, and audit fields."`

---

### SK0708 — BatchConsumerRegisteredViaAddConsumer

**Category:** Usage | **Severity:** Warning | **ID block:** 07xx (messaging-domain)

SK0708 fires when `AddConsumer<T>()` is called with a single type argument whose identifier
text contains `"BatchConsumer"` as a substring (case-sensitive).

**Rationale:** `AddConsumer<T>()` registers a consumer that processes messages one at a
time and ignores any `MessageLimit` / `TimeLimit` batch configuration.
`MessagingBusBuilder.AddBatchConsumer<T>()` is the correct registration method — it applies
the configured batch window so the consumer receives a `Batch<T>` of messages.

**Naming-convention limitation:** SK0708 is a **naming-convention-guided heuristic** — it
fires only when the type argument's identifier text contains `"BatchConsumer"`. Batch
consumer implementation classes **must** contain `"BatchConsumer"` in their class name
(e.g., `OrderBatchConsumer`, `InvoiceLineBatchConsumer`) for this rule to provide coverage.
A class named `OrderProcessor` that is, in fact, a batch consumer will **not** be detected
— this is a documented false-negative limitation, not a bug. Recommend the naming
convention `{Purpose}BatchConsumer` as an enforcement aid.

**Offending pattern:**
```csharp
// SK0708: OrderBatchConsumer registered one-at-a-time — MessageLimit/TimeLimit ignored
builder.AddConsumer<OrderBatchConsumer>();
```

**Compliant pattern:**
```csharp
// Correct: applies the configured batch window
builder.AddBatchConsumer<OrderBatchConsumer>();
```

**Pass-through (not flagged):**
```csharp
// OrderCommandConsumer's name does not contain "BatchConsumer" — SK0708 does not apply
builder.AddConsumer<OrderCommandConsumer>();
```

**Suppression:** Per-call-site only — use `#pragma warning disable SK0708` when a batch
consumer class genuinely must be registered individually (e.g., a test fixture that
processes one message at a time by design):

```csharp
#pragma warning disable SK0708 // Test fixture intentionally processes one message at a time
builder.AddConsumer<OrderBatchConsumer>();
#pragma warning restore SK0708
```

**CI configuration:**
```xml
<WarningsAsErrors>$(WarningsAsErrors);SK0708</WarningsAsErrors>
```

---

## SharedKernel.ArchitectureTests — Layering Rules

`SharedKernel.ArchitectureTests` is a test-only package providing NetArchTest-based base
classes and pre-built layering rule predicates for the SharedKernel architecture.

### Referencing the Package

Add the reference to your architecture test project with `PrivateAssets="all"` to ensure
it never leaks into production dependency graphs:

```xml
<ItemGroup>
  <PackageReference Include="SharedKernel.ArchitectureTests" Version="1.0.0"
                    PrivateAssets="all" />
  <PackageReference Include="FluentAssertions" Version="6.*" />
  <PackageReference Include="xunit" Version="2.*" />
</ItemGroup>
```

### Using SharedKernelLayeringRules

`SharedKernelLayeringRules` is a static class of pre-built NetArchTest predicates. Each
factory method corresponds 1:1 to a constraint in the root `CLAUDE.md` layering table and
returns a `ConditionList` ready for assertion.

```csharp
using System.Reflection;
using FluentAssertions;
using SharedKernel.ArchitectureTests.Rules;
using Xunit;

public class LayeringTests
{
    private static readonly Assembly DomainAssembly =
        typeof(SomeEntity).Assembly; // replace with a type from your Domain assembly

    [Fact]
    public void Domain_MustNot_ReferencePersistence()
    {
        var result = SharedKernelLayeringRules
            .DomainNeverReferencesPersistence(DomainAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "Domain must not depend on Persistence");
    }

    [Fact]
    public void Domain_MustNot_ReferenceMessaging()
    {
        var result = SharedKernelLayeringRules
            .DomainNeverReferencesMessaging(DomainAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "Domain must not depend on Messaging");
    }

    [Fact]
    public void Application_MustNot_ReferenceConcreteInfrastructure()
    {
        var applicationAssembly = typeof(SomeHandler).Assembly;
        var result = SharedKernelLayeringRules
            .ApplicationNeverReferencesConcreteInfrastructure(applicationAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "Application must depend only on abstractions, not concrete providers");
    }
}
```

Available rule factories:

| Method | Layering Constraint |
|--------|-------------------|
| `CoreReferencesNothing(assembly)` | 01.Core must not depend on any other SharedKernel domain |
| `CachingReferencesOnlyCore(assembly)` | 02.Caching must not reference Domain, Contracts, or infrastructure layers |
| `DomainReferencesOnlyCore(assembly)` | 03.Domain must not reference Caching, Contracts, or infrastructure |
| `ContractsReferencesOnlyCoreAndDomain(assembly)` | 04.Contracts must not reference infrastructure layers |
| `DomainNeverReferencesPersistence(assembly)` | **Hard rule**: Domain must never depend on Persistence |
| `DomainNeverReferencesMessaging(assembly)` | **Hard rule**: Domain must never depend on Messaging |
| `ApplicationNeverReferencesConcreteInfrastructure(assembly)` | **Hard rule**: Application must only depend on abstractions |
| `TestingNeverReferencedByProduction(assembly)` | **Hard rule**: Testing packages must never appear in production code |

### Subclassing ArchitectureRuleBase

For custom rules, subclass `ArchitectureRuleBase` in your test project:

```csharp
using System.Reflection;
using SharedKernel.ArchitectureTests.Helpers;
using Xunit;

public class MyDomainArchitectureTests : ArchitectureRuleBase
{
    private static readonly Assembly DomainAssembly = typeof(SomeEntity).Assembly;

    [Fact]
    public void Domain_MustNot_ReferenceStorage()
    {
        // Use the ShouldNotReference helper for custom namespace checks
        var conditionList = ShouldNotReference(DomainAssembly, "SharedKernel.Storage");
        AssertRule(conditionList);
    }
}
```

`ArchitectureRuleBase` provides:
- `GetAssemblyTypes(assembly)` — opens the NetArchTest fluent predicate scope for an assembly.
- `ShouldNotReference(assembly, forbiddenNamespace)` — builds a `ConditionList` asserting no
  type in the assembly has a dependency on the forbidden namespace.
- `AssertRule(conditionList)` — calls `.GetResult()` on the condition list and asserts
  `IsSuccessful` via FluentAssertions, printing failing type names on failure.

---

### GuardPurityRules — Guard Clause Purity Enforcement

`GuardPurityRules` is a static class that enforces the two-path guard clause contract at
assembly level. Its single factory method inspects the `SharedKernel.Guards` assembly for
throw opcodes in `IGuardClause` extension methods.

```csharp
using System.Reflection;
using FluentAssertions;
using SharedKernel.ArchitectureTests.Rules;
using SharedKernel.Guards; // must be referenced to supply typeof(IGuardClause)
using Xunit;

public class GuardPurityTests
{
    [Fact]
    public void GuardAgainst_Methods_MustNot_Throw()
    {
        var guardsAssembly = typeof(IGuardClause).Assembly;
        var result = GuardPurityRules
            .GuardAgainstMethodsMustNotThrow(guardsAssembly)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "IGuardClause extension methods must never throw — use the Guard.Throw path");
    }
}
```

The `Guard.Throw` companion class (full CLR name `Guard+Throw`) is explicitly excluded from
the check — methods in that class are the sanctioned throw location.

---

### CachingAbstractionRules — Caching Boundary Enforcement

`CachingAbstractionRules` asserts that only explicitly permitted assemblies may take a
direct binary reference to concrete caching packages (`SharedKernel.Caching` or
`SharedKernel.Caching.Redis`). All other assemblies must use `SharedKernel.Caching.Abstractions`.

#### Exemption List

The following assemblies are **always exempt** from this rule:

| Assembly | Reason |
|----------|--------|
| `SharedKernel.Caching` | The package itself — it is the abstraction + default implementation |
| `SharedKernel.Caching.Redis` | The concrete Redis L2 provider — legitimately references itself |
| `SharedKernel.ServiceDefaults` | The composition root — the only place providers are wired to abstractions |

Any additional exemption requires prior documentation in `00.Governance/CLAUDE.md` under
the `CachingAbstractionRules` exemption list. Adding an ad-hoc exemption in test code
without CLAUDE.md documentation is a governance violation.

#### Usage

```csharp
using System.Reflection;
using FluentAssertions;
using SharedKernel.ArchitectureTests.Rules;
using Xunit;

public class CachingBoundaryTests
{
    // Pass the assemblies under test — do NOT pass exempt assemblies here.
    private static readonly Assembly[] ProductionAssemblies =
    [
        typeof(SomeApplicationHandler).Assembly,
        typeof(SomeDomainService).Assembly,
    ];

    [Fact]
    public void Production_Assemblies_MustNot_Reference_ConcreteCaching()
    {
        var result = CachingAbstractionRules
            .OnlyAllowedAssembliesMayReferenceConcreteCaching(ProductionAssemblies)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "Only exempt assemblies may reference concrete caching packages");
    }
}
```

The rule uses `.Should().NotHaveDependencyOn("SharedKernel.Caching")` — NetArchTest matches
this as a substring of referenced assembly names, so it catches both `SharedKernel.Caching`
(the main package) and `SharedKernel.Caching.Redis` with a single call.

#### Adding a Documented Exemption

If a non-standard composition root legitimately needs to reference concrete caching:

1. Open `00.Governance/CLAUDE.md` and add the assembly to the `CachingAbstractionRules`
   exemption list with a documented rationale.
2. In your architecture test, exclude that assembly from the `ProductionAssemblies` array
   passed to `OnlyAllowedAssembliesMayReferenceConcreteCaching`.
3. Do not suppress the test or widen the exemption without updating CLAUDE.md.

---

### DomainLayerPurityRules — Domain Layer Purity Enforcement

`DomainLayerPurityRules` provides four predicates that collectively protect `03.Domain` from
infrastructure contamination. Each predicate targets a distinct class of violation.

Cross-reference: the root `CLAUDE.md` layering rules state that `03.Domain` must never
reference `06.Persistence`, `07.Messaging`, or any infrastructure layer. These predicates
encode those hard rules as build-time checks.

#### Rule 1 — DomainAssembliesNeverReferenceInfrastructure

Asserts that no type in the domain assembly has a binary reference to any of the forbidden
infrastructure libraries: EntityFramework, MassTransit, Redis (StackExchange.Redis), or
RabbitMQ.

**Rationale:** A single EF Core attribute added for "convenience" propagates a hard
dependency on a specific ORM to every service that references the domain. Domain models
must be persistence-ignorant.

**Offending pattern:**
```csharp
using Microsoft.EntityFrameworkCore; // in a domain entity file

[Owned]  // EF Core attribute — creates a binary reference to EntityFrameworkCore
public class Address : ValueObject { ... }
```

**Compliant pattern:**
```csharp
public class Address : ValueObject { ... }
// EF Core mapping belongs in a separate configuration class in 06.Persistence
```

#### Rule 2 — DomainAssembliesNeverContainEventHandlers

Asserts that no type in the domain assembly implements `IDomainEventHandler<TEvent>`.

**Rationale:** Domain event handlers orchestrate responses to domain events — they belong in
`05.Application` (orchestration) or `07.Messaging` (integration). Placing handlers inside
the domain creates a circular coupling between the event definition and its handling,
preventing independent evolution.

**Offending pattern:**
```csharp
// In 03.Domain:
public class OrderCreatedHandler : IDomainEventHandler<OrderCreatedEvent>
{
    public Task Handle(OrderCreatedEvent @event, CancellationToken ct) { ... }
}
```

**Compliant pattern:**
```csharp
// In 05.Application:
public class OrderCreatedHandler : IDomainEventHandler<OrderCreatedEvent>
{
    public Task Handle(OrderCreatedEvent @event, CancellationToken ct) { ... }
}
```

#### Rule 3 — DomainAssembliesNeverCallSystemClock

Asserts that no method in the domain assembly calls `DateTime.UtcNow`, `DateTime.Now`,
`DateTimeOffset.UtcNow`, or `DateTimeOffset.Now` directly (detected via IL inspection).

**Rationale:** Direct system-clock calls make domain methods non-deterministic: tests cannot
control time without monkey-patching the system clock. `IClock.UtcNow` (from
`SharedKernel.Primitives`) is the only permitted time source — inject it via DI so tests
can substitute a fixed instant.

Companion rule: SK0001 catches these at individual call-site level during development;
this architecture rule provides the assembly-level gating enforcement that SK0001 cannot.

**Offending pattern:**
```csharp
public class Order : AggregateRoot<Guid>
{
    public bool IsExpired() => ExpiresAt < DateTime.UtcNow; // direct clock call
}
```

**Compliant pattern:**
```csharp
public class Order : AggregateRoot<Guid>
{
    public bool IsExpired(IClock clock) => ExpiresAt < clock.UtcNow;
}
```

#### Rule 4 — DomainServicesHaveNoInfrastructureConstructorParameters

Asserts that no type implementing `IDomainService` has constructor parameters whose type
namespace starts with `Microsoft.EntityFrameworkCore`, `MassTransit`, `StackExchange.Redis`,
or `RabbitMQ.Client`.

**Rationale:** Domain services that accept infrastructure types as constructor parameters
cannot be tested without the full infrastructure stack. They also couple the domain layer
to a specific technology choice, making provider swaps risky. Domain services must accept
only `IClock`, other domain interfaces, and `01.Core` primitives.

**Offending pattern:**
```csharp
public class PricingService : DomainService
{
    // IDomainService with EF Core repository in constructor — violates purity
    public PricingService(IRepository<Product> repo, IClock clock) { }
}
```

**Compliant pattern:**
```csharp
public class PricingService : DomainService
{
    // Domain repository interface (in 03.Domain or 01.Core), not EF Core
    public PricingService(IProductRepository repo, IClock clock) { }
}
```

#### Usage

```csharp
using System.Reflection;
using FluentAssertions;
using SharedKernel.ArchitectureTests.Rules;
using SharedKernel.Domain; // reference needed for typeof(IDomainService)
using Xunit;

public class DomainPurityTests
{
    private static readonly Assembly DomainAssembly = typeof(Order).Assembly;

    [Fact]
    public void Domain_MustNot_ReferenceInfrastructure()
    {
        var result = DomainLayerPurityRules
            .DomainAssembliesNeverReferenceInfrastructure(DomainAssembly)
            .GetResult();
        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Domain_MustNot_ContainEventHandlers()
    {
        var result = DomainLayerPurityRules
            .DomainAssembliesNeverContainEventHandlers(DomainAssembly)
            .GetResult();
        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Domain_MustNot_CallSystemClock()
    {
        var result = DomainLayerPurityRules
            .DomainAssembliesNeverCallSystemClock(DomainAssembly)
            .GetResult();
        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void DomainServices_MustNot_HaveInfrastructureConstructorParameters()
    {
        var result = DomainLayerPurityRules
            .DomainServicesHaveNoInfrastructureConstructorParameters(DomainAssembly)
            .GetResult();
        result.IsSuccessful.Should().BeTrue();
    }
}
```

---

### DomainGoldStandardRules — Domain Convention Enforcement

`DomainGoldStandardRules` enforces the WO-011 domain conventions at assembly level. Rule 1
is a NetArchTest predicate; Rules 2–4 are Roslyn analyzers (SK0008–SK0010) documented in the
Analyzers section above.

#### Rule 1 — DomainServicesMustExtendAbstractBase

Asserts that every non-abstract type implementing `IDomainService` also inherits from the
`DomainService` abstract base class.

**Rationale:** `DomainService` provides `CheckRule(IBusinessRule)` access and acts as the
DI anchor for all domain services. Directly implementing `IDomainService` without inheriting
`DomainService` bypasses `CheckRule`, forcing consumers to duplicate business-rule validation
logic across services.

**Offending pattern:**
```csharp
// Directly implements IDomainService — bypasses DomainService.CheckRule
public class PricingService : IDomainService
{
    public decimal CalculatePrice(Order order) { ... }
}
```

**Compliant pattern:**
```csharp
// Inherits DomainService — gets CheckRule and the standard DI anchor
public class PricingService : DomainService
{
    public decimal CalculatePrice(Order order)
    {
        CheckRule(new PricingEligibilityRule(order));
        ...
    }
}
```

**Usage:**
```csharp
[Fact]
public void DomainServices_Must_ExtendAbstractBase()
{
    var result = DomainGoldStandardRules
        .DomainServicesMustExtendAbstractBase(typeof(IDomainService).Assembly)
        .GetResult();

    result.IsSuccessful.Should().BeTrue(
        because: $"Failing types: {string.Join(", ", result.FailingTypeNames ?? [])}");
}
```

The `DomainService` abstract base itself is excluded via `.AreNotAbstract()` in the
NetArchTest predicate chain — it passes cleanly without appearing as a violation.

---

### ContractsPurityRules — Contracts Layer Purity Enforcement

`ContractsPurityRules` provides four predicates that protect `04.Contracts` from domain
logic leakage, domain type exposure, `Result<T>` misuse, and non-sealed integration events.
A fifth rule is a documented guideline (not enforced by NetArchTest).

#### Rule 1 — ContractsAssembliesHaveNoNonTrivialMethods

Asserts that no type in the contracts assembly contains a non-trivial method — defined as
any method that is not a constructor, property getter/setter, static operator (`op_` prefix),
or one of `ToString`/`Equals`/`GetHashCode`.

**Rationale:** DTOs and event payloads carry state, not behaviour. Any non-trivial method
in `04.Contracts` signals domain logic leakage into the contracts layer, creating an
invisible coupling between the wire format and domain rules.

**Offending pattern:**
```csharp
public class OrderDto
{
    public Guid Id { get; set; }
    public DateTimeOffset Deadline { get; set; }

    // Non-trivial method — domain logic in a DTO
    public bool IsExpired() => Deadline < DateTime.UtcNow;
}
```

**Compliant pattern:**
```csharp
public record OrderDto(Guid Id, DateTimeOffset Deadline);
// Expiry logic belongs in the domain or application layer, not the DTO
```

#### Rule 2 — ContractsAssembliesHaveNoDomainTypeOnPublicSurface

Asserts no public type in the contracts assembly has a binary reference to the
`SharedKernel.Domain` assembly.

**Rationale:** Exposing `Entity<TId>`, `AggregateRoot<TId>`, `ValueObject`, or
`Specification<T>` on a contracts public surface ties the wire format to the domain model,
breaking polyglot consumers that do not reference `SharedKernel.Domain`.

**Exemption:** `EventEnvelope<TEvent> where TEvent : IDomainEvent` — the generic constraint
references `IDomainEvent`. If NetArchTest's dependency scanner picks this up as a domain
reference, the `EventEnvelope` type is explicitly excluded from the scan. This exemption is
documented in the architecture test fixture.

**Offending pattern:**
```csharp
public class OrderSummaryDto
{
    // Domain type on a contracts public surface — breaks polyglot consumers
    public Order DomainOrder { get; set; }
}
```

**Compliant pattern:**
```csharp
public record OrderSummaryDto(Guid OrderId, string Status, DateTimeOffset CreatedAt);
```

#### Rule 3 — ContractsAssembliesHaveNoResultTypeOnPublicSurface

Asserts no public type in the contracts assembly has a binary reference to
`SharedKernel.Primitives` (where `Result<T>` and `Result` live).

**Rationale:** `Result<T>` is an intra-service discriminated union. `Envelope<T>` is the
cross-service HTTP wrapper. Exposing `Result<T>` in a serialized response payload causes
deserialization failures in any JSON client that does not share `SharedKernel.Primitives`,
breaking the polyglot contract model.

**Offending pattern:**
```csharp
public class CreateOrderResponse
{
    // Result<T> on a cross-service DTO — breaks polyglot deserialization
    public Result<Guid> OrderId { get; set; }
}
```

**Compliant pattern:**
```csharp
public record CreateOrderResponse(Guid OrderId);
// Use Envelope<T> for HTTP wrapping — not Result<T>
```

#### Rule 4 — IntegrationEventImplementationsMustBeSealed

Asserts every non-abstract type implementing `IIntegrationEvent` is sealed (or a record,
which is sealed in IL).

**Rationale:** Non-sealed integration events are an inheritance trap. A sub-event changes
the wire format without incrementing `[DomainEventVersion]`, causing silent schema drift
that breaks consumers relying on exact type discrimination.

**Offending pattern:**
```csharp
// Non-sealed — a subtype could silently extend the wire format
public class OrderCreatedEvent : IIntegrationEvent
{
    public Guid OrderId { get; init; }
}
```

**Compliant pattern:**
```csharp
public sealed record OrderCreatedEvent(Guid OrderId) : IIntegrationEvent;
```

#### Rule 5 — Microservices Must Not Reference SharedKernel.Domain Directly (Guideline)

This is a **documentation-only guideline** — it is not enforced by a NetArchTest predicate
at the mono-repo level.

Microservices that are not DDD-domain services must not take a `<PackageReference>` on
`SharedKernel.Domain`. Cross-service DTO types live in `SharedKernel.Contracts`; domain
types (`Entity`, `ValueObject`, `AggregateRoot`) are internal to the service that owns the
domain. Referencing `SharedKernel.Domain` from a CRUD microservice or a reporting service
creates an invisible coupling to the domain model that breaks silently when the domain
evolves.

**Architectural rationale:** The contracts package is the stable public surface between
services. `SharedKernel.Domain` is an internal building block for services that
implement domain logic. Consuming it from non-domain services collapses the contracts/domain
separation and forces every downstream service to rebuild when the domain model changes.

#### Usage

```csharp
using System.Reflection;
using FluentAssertions;
using SharedKernel.ArchitectureTests.Rules;
using SharedKernel.Contracts; // reference needed to supply the contracts assembly
using Xunit;

public class ContractsPurityTests
{
    private static readonly Assembly ContractsAssembly = typeof(PagedList<>).Assembly;

    [Fact]
    public void Contracts_MustNot_HaveNonTrivialMethods()
    {
        var result = ContractsPurityRules
            .ContractsAssembliesHaveNoNonTrivialMethods(ContractsAssembly)
            .GetResult();
        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Contracts_MustNot_ExposeDomainTypesOnPublicSurface()
    {
        var result = ContractsPurityRules
            .ContractsAssembliesHaveNoDomainTypeOnPublicSurface(ContractsAssembly)
            .GetResult();
        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Contracts_MustNot_ExposeResultTypeOnPublicSurface()
    {
        var result = ContractsPurityRules
            .ContractsAssembliesHaveNoResultTypeOnPublicSurface(ContractsAssembly)
            .GetResult();
        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void IntegrationEvents_Must_BeSealed()
    {
        var result = ContractsPurityRules
            .IntegrationEventImplementationsMustBeSealed(ContractsAssembly)
            .GetResult();
        result.IsSuccessful.Should().BeTrue();
    }
}
```

### PersistenceLayerProtectionRules — EF Core Persistence Layer Contract Enforcement

`PersistenceLayerProtectionRules` provides three predicates that protect the EF Core
persistence layer contract established in WO-013. They enforce the three hard constraints
that keep the interceptor chain intact, the query surface clean, and the domain layer
ignorant of persistence infrastructure.

#### Rule 1 — OnlyEfUnitOfWorkMayCallSaveChanges

Asserts that no type **outside** the `SharedKernel.Persistence.EfCore` namespace calls
`DbContext.SaveChanges` or `DbContext.SaveChangesAsync` directly. The check is implemented
by `NoDirectSaveChangesPredicate` (a Mono.Cecil `ICustomRule` that walks
`MethodDefinition.Body.Instructions` for `Call`/`Callvirt` opcodes whose operand is a
`MethodReference` on `DbContext` named `SaveChanges` or `SaveChangesAsync`). Types whose
`TypeDefinition.Namespace` starts with `"SharedKernel.Persistence.EfCore"` are exempted
unconditionally inside the predicate — `EfUnitOfWork` is the sole legitimate commit site.

**Rationale:** Calling `SaveChangesAsync` directly bypasses the full EF Core interceptor
chain — `AuditInterceptor` (created/modified audit fields), `SoftDeleteInterceptor`
(converts hard deletes to soft deletes), `OutboxInterceptor` (publishes domain events to
the outbox), and `ConcurrencyInterceptor` (optimistic concurrency token enforcement). Only
`EfUnitOfWork.CommitAsync()` is the correct commit path; all application handlers must
inject `IUnitOfWork` and call `CommitAsync()`. This is a hard rule from the root
`CLAUDE.md` layering table.

**Cross-reference:** Root `CLAUDE.md` — "05.Application must never reference a concrete
infrastructure package — only abstractions." Direct `DbContext.SaveChangesAsync` in
application code is equivalent to referencing a concrete infrastructure API.

**Offending pattern:**
```csharp
// Application layer directly committing — bypasses all EF Core interceptors
public class CreateOrderHandler : IRequestHandler<CreateOrderCommand>
{
    private readonly AppDbContext _dbContext;

    public async Task Handle(CreateOrderCommand cmd, CancellationToken ct)
    {
        _dbContext.Orders.Add(new Order(cmd.Id));
        await _dbContext.SaveChangesAsync(ct); // SK violation: bypasses interceptors
    }
}
```

**Compliant pattern:**
```csharp
public class CreateOrderHandler : IRequestHandler<CreateOrderCommand>
{
    private readonly IRepository<Order, Guid> _repository;
    private readonly IUnitOfWork _unitOfWork;

    public async Task Handle(CreateOrderCommand cmd, CancellationToken ct)
    {
        await _repository.AddAsync(new Order(cmd.Id), ct);
        await _unitOfWork.CommitAsync(ct); // correct: runs the full interceptor chain
    }
}
```

#### Rule 2 — RepositoriesMustNotExposeIQueryable

Asserts that no type implementing an `IRepository`-prefixed interface has a method
returning `IQueryable` or `IQueryable<T>`. The check is implemented by
`NoIQueryableReturnPredicate` (a Mono.Cecil `ICustomRule`) that scopes to types whose
`TypeDefinition.Interfaces` contains an entry with `InterfaceType.Name` starting with
`"IRepository"`, then inspects all non-constructor, non-getter methods for an
`IQueryable` return type.

**Rationale:** `IQueryable<T>` leaks EF Core expression-tree execution semantics into the
application layer. Handler code that receives an `IQueryable<Order>` is implicitly coupled
to EF Core's LINQ provider — swapping the persistence technology (e.g., to Dapper) breaks
every handler that consumed the queryable. The query surface belongs exclusively on
`IReadRepository<T,TId>` via `Specification<T>`; the write-side `IRepository<T,TId>` is
scoped to mutation operations only.

**Cross-reference:** Root `CLAUDE.md` layering rules: `06.Persistence` may reference
`01–05` but application handlers in `05.Application` must not be coupled to EF Core
execution semantics.

**Offending pattern:**
```csharp
public class OrderRepository : IRepository<Order, Guid>
{
    // Exposes EF Core execution semantics to callers — coupling violation
    public IQueryable<Order> GetAll() => _dbContext.Orders.AsQueryable();
}
```

**Compliant pattern:**
```csharp
public class OrderRepository : IRepository<Order, Guid>
{
    // Query surface exposed via Specification<T> only
    public Task<IReadOnlyList<Order>> FindAsync(
        ISpecification<Order> spec,
        CancellationToken ct = default) { ... }
}
```

#### Rule 3 — DomainAssembliesNeverReferencePersistenceStack

Asserts that no type in the supplied domain assembly has a binary dependency on any of the
persistence-stack assembly name substrings: `"Microsoft.EntityFrameworkCore"`, `"Npgsql"`,
`"SharedKernel.Persistence"`. The check uses iterative
`.Should().NotHaveDependencyOn(term)` calls — one per forbidden term — consistent with the
pattern in `DomainLayerPurityRules.DomainAssembliesNeverReferenceInfrastructure`. This rule
is **additive**, not replacing, that rule: it adds Npgsql and the in-repo persistence
packages as a WO-013-scoped gate.

**Rationale:** Any EF Core, Npgsql, or `SharedKernel.Persistence.*` reference inside
`03.Domain` destroys DDD isolation. Domain entities annotated with EF Core attributes
(e.g., `[Key]`, `[Column]`) cannot be tested without a running database context, making
unit tests expensive or impossible. Npgsql references in domain code tie the bounded
context to a specific database engine. This is a hard rule from the root `CLAUDE.md`
layering table: `03.Domain` must never reference `06.Persistence` or any infrastructure
layer.

**Cross-reference:** Root `CLAUDE.md` — "Hard rules: `03.Domain` must never reference
`06.Persistence`, `07.Messaging`, or any infrastructure layer."

**Offending pattern:**
```csharp
// Domain entity polluted with EF Core infrastructure annotations
using Microsoft.EntityFrameworkCore; // EF Core reference in 03.Domain — violation

[Index(nameof(TenantId), nameof(Email))] // EF Core attribute on a domain type
public class User : Entity<UserId>
{
    [Key] // EF Core attribute — persistence concern in domain layer
    public UserId Id { get; private set; }
}
```

**Compliant pattern:**

```csharp
// Domain entity — zero infrastructure annotations
public class User : Entity<UserId>
{
    public UserId Id { get; private set; }
    public Email Email { get; private set; }
    public TenantId TenantId { get; private set; }
}

// EF Core mapping lives exclusively in 06.Persistence
// IEntityTypeConfiguration<User> in SharedKernel.Persistence.EfCore
public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(u => u.Id);
        builder.HasIndex(u => new { u.TenantId, u.Email });
    }
}
```

#### Usage Example

```csharp
using System.Reflection;
using FluentAssertions;
using SharedKernel.ArchitectureTests.Rules;
using SharedKernel.Domain; // reference needed to supply the domain assembly
using SharedKernel.Persistence.EfCore; // reference needed for the persistence assembly
using Xunit;

public class PersistenceLayerProtectionTests
{
    private static readonly Assembly DomainAssembly = typeof(Order).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(CreateOrderHandler).Assembly;

    [Fact]
    public void OnlyEfUnitOfWork_MayCall_SaveChanges()
    {
        var result = PersistenceLayerProtectionRules
            .OnlyEfUnitOfWorkMayCallSaveChanges(ApplicationAssembly)
            .GetResult();
        result.IsSuccessful.Should().BeTrue(
            because: "only EfUnitOfWork.CommitAsync() is the permitted commit path");
    }

    [Fact]
    public void Repositories_MustNot_ExposeIQueryable()
    {
        var result = PersistenceLayerProtectionRules
            .RepositoriesMustNotExposeIQueryable(typeof(EfRepository<,>).Assembly)
            .GetResult();
        result.IsSuccessful.Should().BeTrue(
            because: "IQueryable leaks EF Core execution semantics into the application layer");
    }

    [Fact]
    public void Domain_MustNot_ReferencePersistenceStack()
    {
        var result = PersistenceLayerProtectionRules
            .DomainAssembliesNeverReferencePersistenceStack(DomainAssembly)
            .GetResult();
        result.IsSuccessful.Should().BeTrue(
            because: "03.Domain must be persistence-ignorant per the root layering hard rules");
    }
}
```

---

### ExtendedMessagingArchitectureRules — Extended Messaging Misuse Enforcement

`ExtendedMessagingArchitectureRules` provides two predicates (added in WO-021 P-133) that
close gaps left by per-call-site analyzers: a transport-coupling check that only static
type analysis can catch reliably, and a structural saga-state contract that cannot be
expressed as a single-statement diagnostic.

#### Rule 1 — NoDirectMassTransitSchedulerInjection (SK0706)

Asserts that no constructor parameter across the supplied assemblies is typed
`MassTransit.IMessageScheduler`. The check is implemented by
`NoDirectSchedulerInjectionOutsideMessagingPredicate` (a Mono.Cecil `ICustomRule` that
inspects every `MethodDefinition` named `.ctor` and checks each
`ParameterDefinition.ParameterType` for `Name == "IMessageScheduler"` AND
`Namespace.StartsWith("MassTransit")`). Types whose `TypeDefinition.Namespace` starts with
`"SharedKernel.Messaging"` are exempted unconditionally — the messaging adapter layer is
the only place permitted to reference the MassTransit transport scheduler directly.

**Rationale:** `MassTransit.IMessageScheduler` is an implementation detail of the
MassTransit transport. Injecting it directly in application handlers, domain services, or
controllers couples that code to a specific scheduler implementation, making transport
swaps impossible and creating an invisible MassTransit dependency in layers that should be
transport-agnostic.

**Cross-reference:** Root `CLAUDE.md` — "07.Messaging may reference 01-04 (not
06.Persistence directly)" and the `SharedKernel.Messaging.Abstractions` /
`SharedKernel.Messaging.MassTransit` abstraction split: microservices depend on the
abstraction package, never on the concrete MassTransit transport types.

**Offending pattern:**
```csharp
using MassTransit;

// Violation: application handler injects MassTransit.IMessageScheduler directly
public class ScheduleReminderHandler(IMessageScheduler scheduler)
{
}
```

**Compliant pattern:**
```csharp
using SharedKernel.Messaging.Abstractions;

// Correct: injects the platform scheduler abstraction — transport-independent
public class ScheduleReminderHandler(IMessageScheduler scheduler)
{
}
```

#### Rule 2 — SagaStatesMustExtendSagaStateBase (SK0707)

Asserts that every type implementing `ISaga` (`TypeDefinition.Interfaces` contains an
entry with `InterfaceType.Name == "ISaga"`) has `SagaStateBase` somewhere in its
`BaseType` inheritance chain. The check is implemented by
`SagaStateMustExtendSagaStateBasePredicate` (a Mono.Cecil `ICustomRule` that walks the
`BaseType` chain via iterative `TypeReference.Resolve()` calls until it finds
`SagaStateBase`, reaches `Object`, or encounters an unresolved reference). `SagaStateBase`
itself is self-exempt — it implements `ISaga` directly and cannot extend itself.
Unresolved base types in the chain are treated fail-open (possibly-compliant) to avoid
false positives when an assembly's transitive dependencies are not loaded into the test
context.

**Rationale:** `SagaStateBase` (from `SharedKernel.Messaging.MassTransit`) provides the
platform-standard `CorrelationId`, `Version` (optimistic concurrency counter), `CreatedAt`,
and `ModifiedAt` audit fields required for correct saga state persistence and
version-conflict resolution. A saga state class that implements `ISaga` without extending
`SagaStateBase` is missing these fields, breaking version-conflict detection and the
platform observability pipeline for that saga.

**Cross-reference:** Root `CLAUDE.md` "What Goes Where" — saga state persistence relies on
the same audit/concurrency conventions as `TenantedAuditableAggregateRoot<TId>` in
`03.Domain`; `SagaStateBase` is the `07.Messaging` equivalent for saga state classes.

**Offending pattern:**
```csharp
using SharedKernel.Messaging.MassTransit;

// Violation: implements ISaga but does not extend SagaStateBase — no Version/audit fields
public class OrderSagaState : ISaga
{
    public Guid CorrelationId { get; set; }
}
```

**Compliant pattern:**
```csharp
using SharedKernel.Messaging.MassTransit;

// Correct: extends SagaStateBase — carries CorrelationId, Version, CreatedAt, ModifiedAt
public class OrderSagaState : SagaStateBase
{
}
```

#### Usage Example

```csharp
using System.Reflection;
using FluentAssertions;
using SharedKernel.ArchitectureTests.Rules;
using SharedKernel.Messaging.MassTransit; // reference needed to supply the messaging assembly
using Xunit;

public class ExtendedMessagingArchitectureTests
{
    private static readonly Assembly ApplicationAssembly = typeof(ScheduleReminderHandler).Assembly;
    private static readonly Assembly MessagingAssembly = typeof(SagaStateBase).Assembly;

    [Fact]
    public void Application_MustNot_InjectMassTransitSchedulerDirectly()
    {
        var result = ExtendedMessagingArchitectureRules
            .NoDirectMassTransitSchedulerInjection(ApplicationAssembly)
            .GetResult();
        result.IsSuccessful.Should().BeTrue(
            because: "use SharedKernel.Messaging.Abstractions.IMessageScheduler instead of " +
                     "MassTransit.IMessageScheduler to preserve transport independence");
    }

    [Fact]
    public void SagaStates_MustExtend_SagaStateBase()
    {
        var result = ExtendedMessagingArchitectureRules
            .SagaStatesMustExtendSagaStateBase(MessagingAssembly)
            .GetResult();
        result.IsSuccessful.Should().BeTrue(
            because: "every ISaga implementor must extend SagaStateBase to carry " +
                     "correlation ID, version, and audit fields");
    }
}
```

---

## SharedKernel.Linter — EditorConfig and CSharpier

`SharedKernel.Linter` is a content-only NuGet package that distributes:
- `.editorconfig` — indent style, charset, line endings, C# language preferences.
- `.csharpierrc.json` — CSharpier print width (120), tab width (4), no tabs.
- `SharedKernel.Linter.props` — MSBuild props wiring `<CSharpierVersion>` and
  `<AdditionalFiles>` for the distributed `.editorconfig`.
- `SharedKernel.Linter.targets` — MSBuild target `CSharpierCheck` that runs
  `dotnet csharpier --check` in CI.

### Applying the Linter Package

```xml
<ItemGroup>
  <!-- PrivateAssets="all" prevents this from leaking as a transitive dependency -->
  <PackageReference Include="SharedKernel.Linter" Version="1.0.0"
                    PrivateAssets="all" />
</ItemGroup>
```

On `dotnet restore`, the `.editorconfig` and `.csharpierrc.json` are copied into the
consuming project directory. The `.props` and `.targets` files are auto-imported by MSBuild.

### CI Enforcement

The `CSharpierCheck` target only activates when `$(ContinuousIntegrationBuild)` is `true`.
Most CI providers set this automatically (GitHub Actions, Azure DevOps). If your CI does not
set it, add it to the build invocation:

```bash
dotnet build -p:ContinuousIntegrationBuild=true
```

The target installs the pinned CSharpier version into the project's intermediate output
folder and runs `dotnet-csharpier --check` against the project directory. The build fails
if any file would be reformatted.

### Skipping the CSharpier Check Locally

The check is guarded by `$(ContinuousIntegrationBuild)` and does not run on local builds.
To force-skip it even in CI (e.g., during an emergency fix), set:

```bash
dotnet build -p:ContinuousIntegrationBuild=true -p:SkipCSharpierCheck=true
```

---

## SharedKernel.Benchmarks — Benchmark Configuration

`SharedKernel.Benchmarks` is a dev-only project (not published to the production NuGet feed).
It provides `SharedKernelBenchmarkConfig` and `[SharedKernelBenchmark]` for consistent
benchmark configuration across all SharedKernel micro-benchmarks.

**Important:** Never run benchmarks via `dotnet test`. Always use `BenchmarkRunner.Run<T>()`
in a dedicated console application or benchmark runner entry point.

```csharp
using BenchmarkDotNet.Running;
using SharedKernel.Benchmarks.Configurations;

// Apply the attribute to the benchmark class:
[SharedKernelBenchmark]
public class ResultBenchmarks
{
    [Benchmark]
    public Result<int> Success() => Result<int>.Success(42);

    [Benchmark]
    public Result<int> Failure() => Result<int>.Failure(Error.Failure("E", "msg"));
}

// In Program.cs:
BenchmarkRunner.Run<ResultBenchmarks>();
```

`SharedKernelBenchmarkConfig` configures:
- A short-run job (1 warmup, 3 iterations) — fast enough for CI gates.
- `MemoryDiagnoser` — tracks Gen0/Gen1/Gen2 GC collections and allocated bytes.
- `MarkdownExporter.GitHub` — deterministic Markdown output for CI artifact comparison.
- HardwareCounters explicitly disabled — unstable in CI containers.
