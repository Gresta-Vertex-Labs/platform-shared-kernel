# SharedKernel.Core

Core building blocks for the Platform.SharedKernel ecosystem: exception hierarchy, railway extension methods, BCL helpers, and the two-path guard clause system. Depends on `SharedKernel.Primitives`.

> **P-505/WO-082:** `SharedKernel.Guards` was merged into this package (package-count reduction). The `SharedKernel.Guards` / `SharedKernel.Guards.Clauses` / `SharedKernel.Guards.Descriptions` C# namespaces are unchanged — only the physical package/assembly moved. Existing consumers need only swap their `PackageReference` from `SharedKernel.Guards` to `SharedKernel.Core`; no source change required.

## Included Types

**Base Exception Hierarchy** — all carry an `Error` payload (no string-only constructors):

- `SharedKernelException` — base
- `DomainException`, `ValidationException`, `NotFoundException`, `ConflictException`, `UnauthorizedException`

**Result&lt;T&gt; Railway Extensions** — static, AOT-safe:

- `.Map<TOut>`, `.MapError`, `.Bind<TOut>`, `.Match<TOut>`, `.Tap`
- Async overloads: `Task<Result<T>>` variants with both sync and async lambdas

**ResultTry** — exception-boundary wrapping:

- `Try<T>` / `TryAsync<T>`, each with a default and a custom-mapper overload
- Converts any thrown exception (including a flattened `AggregateException`) into `Result<T>.Failure(...)`; never rethrows

**ResultCombine** — multi-result aggregation:

- `Combine(params Result[])` / `Combine(IEnumerable<Result>)` → `ValidationResult`
- `Combine<T>(params Result<T>[])` / `Combine<T>(IEnumerable<Result<T>>)` → `ValidationResult<IReadOnlyList<T>>`
- Evaluates every input — no short-circuit — so a failed aggregate carries every failing `Error`

**BCL Extension Methods**:

- `string`: `.ToSnakeCase()`, `.ToCamelCase()`, `.ToPascalCase()`, `.IsNullOrWhiteSpace()`
- `IEnumerable<T>`: `.ToBatches(int)`, `.IsNullOrEmpty()`, `.WhereNotNull()`
- `DateTimeOffset`: `.ToUnixMilliseconds()`, `.StartOfDay()`, `.EndOfDay()`
- `Guid`: `.IsEmpty()`

**Guard Clause System** (`SharedKernel.Guards` namespace — merged from the former `SharedKernel.Guards` package, P-505/WO-082):

Two-path guard clauses. Every guard exists on both paths, with identical names and parameters. Pick the path that matches the caller's error model — never mix them for the same check.

**`Guard.Against.*` — functional path.** Returns `Error?`. `null` means the guard passed; a non-null `Error` means it was violated. Use inside `Result<T>`-returning code, factory methods, and anywhere a failure is an expected outcome rather than a bug.

**`Guard.Throw.*` — imperative path.** Throws `DomainException` on violation. Use at constructor and method boundaries where a violation means the caller has a defect.

| Group | Guards |
|---|---|
| Null / empty | `Null`, `NullOrEmpty`, `NullOrWhiteSpace` |
| String length | `ShorterThan`, `LongerThan` |
| Numeric (`int`, `long`, `decimal`) | `Negative`, `NegativeOrZero`, `NotPositive`, `OutOfRange` |
| Value | `Default`, `InvalidGuid`, `InvalidFormat`, `Email` |
| Collections | `Empty`, `MinCount`, `MaxCount` |
| Conditions | `True`, `False` (both take a caller-supplied `Error`) |
| Smart enums | `InvalidSmartEnum<TEnum, TValue>` |

The `InvalidFormat`/`Email` compiled-`Regex` cache is bounded (P-522/WO-083) — capped at a fixed maximum number of distinct patterns with oldest-first eviction, so a hypothetical future call site deriving a pattern from configuration or user input cannot grow the cache without bound. Every existing call site passes a literal, compile-time-known pattern, so eviction never triggers in practice.

## Quick Start

```csharp
// Railway chaining
Result<string> result = await GetUserAsync(id)
    .Map(u => u.Email)
    .Bind(email => ValidateEmailAsync(email))
    .Match(
        onSuccess: email => $"Valid: {email}",
        onFailure: err => $"Error: {err.Message}"
    );

// Exceptions
throw new NotFoundException(Error.NotFound("user.notfound", "User not found."));

// Guard clauses — functional path (compose into a Result<T> without throwing)
public static Result<Customer> Create(string name, string email, int creditLimit)
{
    var error = Guard.Against.NullOrWhiteSpace(name, nameof(name))
             ?? Guard.Against.Email(email, nameof(email))
             ?? Guard.Against.Negative(creditLimit, nameof(creditLimit));

    return error is not null
        ? Result<Customer>.Failure(error)
        : Result<Customer>.Success(new Customer(name, email, creditLimit));
}

// Guard clauses — imperative path (a violation here means the caller has a bug)
public Customer(string name, string email, int creditLimit)
{
    Guard.Throw.NullOrWhiteSpace(name, nameof(name));
    Guard.Throw.Email(email, nameof(email));
    Guard.Throw.Negative(creditLimit, nameof(creditLimit));
}
```

## Guard Rules

- Guard clauses are pure — `Guard.Against.*` never throws, never logs, never performs I/O. Enforced by analyzer **SK0006**.
- `Guard.Throw.*` throws `DomainException` carrying the same `Error` the functional path would have returned, so the two paths stay behaviourally identical.
- No DI registration — `Guard` is a static entry point.

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) — see the [01.Core README](../README.md) for the full capability overview.
