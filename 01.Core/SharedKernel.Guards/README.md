# SharedKernel.Guards

Two-path guard clause system for the Platform.SharedKernel ecosystem. Zero external dependencies, AOT-compatible. Depends on `SharedKernel.Primitives` and `SharedKernel.Core`.

## Included

Every guard exists on both paths, with identical names and parameters. Pick the path that matches the caller's error model — never mix them for the same check.

**`Guard.Against.*` — functional path.** Returns `Error?`. `null` means the guard passed; a non-null `Error` means it was violated. Use inside `Result<T>`-returning code, factory methods, and anywhere a failure is an expected outcome rather than a bug.

**`Guard.Throw.*` — imperative path.** Throws `DomainException` on violation. Use at constructor and method boundaries where a violation means the caller has a defect.

### Guards available on both paths

| Group | Guards |
|---|---|
| Null / empty | `Null`, `NullOrEmpty`, `NullOrWhiteSpace` |
| String length | `ShorterThan`, `LongerThan` |
| Numeric (`int`, `long`, `decimal`) | `Negative`, `NegativeOrZero`, `NotPositive`, `OutOfRange` |
| Value | `Default`, `InvalidGuid`, `InvalidFormat`, `Email` |
| Collections | `Empty`, `MinCount`, `MaxCount` |
| Conditions | `True`, `False` (both take a caller-supplied `Error`) |
| Smart enums | `InvalidSmartEnum<TEnum, TValue>` |

## Quick Start

```csharp
using SharedKernel.Guards;

// Functional path — compose into a Result<T> without throwing
public static Result<Customer> Create(string name, string email, int creditLimit)
{
    var error = Guard.Against.NullOrWhiteSpace(name, nameof(name))
             ?? Guard.Against.Email(email, nameof(email))
             ?? Guard.Against.Negative(creditLimit, nameof(creditLimit));

    return error is not null
        ? Result<Customer>.Failure(error)
        : Result<Customer>.Success(new Customer(name, email, creditLimit));
}

// Imperative path — a violation here means the caller has a bug
public Customer(string name, string email, int creditLimit)
{
    Guard.Throw.NullOrWhiteSpace(name, nameof(name));
    Guard.Throw.Email(email, nameof(email));
    Guard.Throw.Negative(creditLimit, nameof(creditLimit));
}
```

## Rules

- Guard clauses are pure — `Guard.Against.*` never throws, never logs, never performs I/O. Enforced by analyzer **SK0006**.
- `Guard.Throw.*` throws `DomainException` carrying the same `Error` the functional path would have returned, so the two paths stay behaviourally identical.
- No DI registration — `Guard` is a static entry point.

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) — see the [01.Core README](../README.md) for the full capability overview.
