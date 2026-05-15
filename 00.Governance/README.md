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
- [SharedKernel.ArchitectureTests — Layering Rules](#sharedkernelarchitecturetests--layering-rules)
  - [Referencing the Package](#referencing-the-package)
  - [Using SharedKernelLayeringRules](#using-sharedkernellayeringrules)
  - [Subclassing ArchitectureRuleBase](#subclassing-architecturerulebase)
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

All five rules (SK0001–SK0005) are enabled by default at `Warning` severity. To suppress a
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
