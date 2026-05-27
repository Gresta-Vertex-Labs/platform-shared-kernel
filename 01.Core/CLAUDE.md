# 01.Core — Domain Brain

## What This Domain Is

The foundational building blocks domain. Every other domain in the shared kernel depends on this layer, so it must reference nothing from outside `01.Core`. It ships five independent packages covering: functional primitives (`Result<T>`, `Error`), system abstractions (`IClock`, SmartEnums, base exceptions, BCL extensions), a two-path guard system (`Guard.Against` / `Guard.Throw`), Options-pattern validation, and a Feature Flag abstraction.

Philosophy: **Zero external dependencies for Primitives. Pure C#. AOT-first. Railway-oriented.**

---

## Packages

| Package | Role | References |
|---------|------|-----------|
| `SharedKernel.Primitives` | `Result<T>`, `Error`, `ErrorType`, `IClock`, `SmartEnum<TEnum,TValue>` | nothing |
| `SharedKernel.Core` | Base exceptions, BCL extension methods, `Result<T>` railway extensions | `SharedKernel.Primitives` |
| `SharedKernel.Guards` | Two-path guard system: `Guard.Against.*` (functional) + `Guard.Throw.*` (imperative) | `SharedKernel.Primitives`, `SharedKernel.Core` |
| `SharedKernel.Configuration` | Options-pattern validation, `AddValidatedOptions` DI extension | `SharedKernel.Primitives` |
| `SharedKernel.FeatureManagement` | `IFeatureManager` abstraction + `Microsoft.FeatureManagement` adapter | `SharedKernel.Primitives` |

All five target `net10.0`. Test sub-folders live inside each project folder (never in a top-level `tests/`).

---

## Technology Stack

| Concern | Technology |
|---------|-----------|
| Functional primitives | Pure C# 13 — no NuGet dependencies |
| System abstractions | Pure C# 13 — no NuGet dependencies |
| Guard clauses | Pure C# 13 — no NuGet dependencies; compiled/cached `System.Text.RegularExpressions.Regex` for format/email guards |
| Options validation | `Microsoft.Extensions.Options.DataAnnotations` |
| Feature flags | `Microsoft.FeatureManagement` (abstracted behind `IFeatureManager`) |

---

## Interface Contracts

### `SharedKernel.Primitives` — public surface

```
Result<T>  (sealed class — not struct; zero-value problem with generic struct payloads)
    .Success(T value)                                      → Result<T>
    .Failure(Error error)                                  → Result<T>
    .IsSuccess                                             → bool
    .IsFailure                                             → bool
    .Value                                                 → T   (throws InvalidOperationException if failure)
    .Error                                                 → Error (throws InvalidOperationException if success)
    implicit operator Result<T>(T value)                   → Result<T>.Success
    implicit operator Result<T>(Error error)               → Result<T>.Failure

Result  (non-generic, readonly struct — void operations; no typed value payload)
    .Success()                                             → Result
    .Failure(Error error)                                  → Result
    implicit operator Result(Error error)                  → Result.Failure

Error  (sealed record)
    .None                                                  → Error (sentinel — no error; never use null)
    .Unexpected(string code, string message)               → Error
    .Validation(string code, string message)               → Error
    .NotFound(string code, string message)                 → Error
    .Conflict(string code, string message)                 → Error
    .Unauthorized(string code, string message)             → Error
    .BusinessRule(string code, string message)             → Error  (domain invariant violation — HTTP 422; distinct from Validation)
    .Code                                                  → string
    .Message                                               → string
    .Type                                                  → ErrorType

ErrorType  (enum)
    None | Unexpected | Validation | NotFound | Conflict | Unauthorized | BusinessRule
    — BusinessRule: domain invariant violation; maps to HTTP 422 Unprocessable Entity at presentation layer;
      semantically distinct from Validation (input format/presence) and Unexpected (system fault)

ErrorCodes  (static class — well-known string constants, organized as nested static classes)
    ErrorCodes.Validation.Required
    ErrorCodes.Validation.OutOfRange
    ErrorCodes.NotFound.Default
    ErrorCodes.Conflict.Default
    ErrorCodes.Unauthorized.Default
    ErrorCodes.Domain.RuleViolated                         → "domain.rule.violated"  (canonical code for BusinessRuleViolationException)
    — consuming packages may define additional local constants; no enum versioning problem

ValidationResult  (sealed record — multi-error aggregate, distinct from Result<T>)
    .IsValid                                               → bool
    .Errors                                                → IReadOnlyList<Error>
    .Success()                                             → ValidationResult
    .Failure(IReadOnlyList<Error> errors)                  → ValidationResult

ValidationResult<T>  (sealed record — generic multi-error aggregate)
    .IsValid                                               → bool
    .Errors                                                → IReadOnlyList<Error>
    .Value                                                 → T
    .Success(T value)                                      → ValidationResult<T>
    .Failure(IReadOnlyList<Error> errors)                  → ValidationResult<T>

IClock
    UtcNow                                                 → DateTimeOffset
    Today                                                  → DateOnly

SystemClock  (sealed class, implements IClock)
    — wraps DateTimeOffset.UtcNow; no mutable state

SmartEnum<TEnum, TValue>  (abstract base, TEnum : SmartEnum<TEnum,TValue>)
    .FromValue(TValue value)                               → TEnum   (throws if not found)
    .TryFromValue(TValue value, out TEnum? result)         → bool
    .FromName(string name)                                 → TEnum   (throws if not found)
    .List                                                  → IReadOnlyList<TEnum>  (static compile-time list — no reflection)
    .Name                                                  → string
    .Value                                                 → TValue
```

### `SharedKernel.Core` — public surface

```
Base exceptions  (all derive from SharedKernelException; string-only constructors are forbidden)
    SharedKernelException(string message, Error error)
    DomainException(Error error)
    ValidationException(IReadOnlyList<Error> errors)       — bridges ValidationResult to exception world
    NotFoundException(Error error)
    ConflictException(Error error)
    UnauthorizedException(Error error)

Result<T> railway extension methods  (static, AOT-safe)
    .Map<TOut>(Func<T, TOut> map)                          → Result<TOut>       (success: transform; failure: pass-through)
    .MapError(Func<Error, Error> map)                      → Result<T>          (failure: transform; success: pass-through)
    .Bind<TOut>(Func<T, Result<TOut>> bind)               → Result<TOut>       (short-circuits on failure)
    .Match<TOut>(Func<T, TOut> onSuccess, Func<Error, TOut> onFailure) → TOut  (fold to single value)
    .Tap(Action<T> action)                                 → Result<T>          (side-effect on success, returns original)

Result (non-generic) railway extension
    .Match(Action onSuccess, Action<Error> onFailure)      → void               (void fold for void operations)

Async railway overloads (this Task<Result<T>> extensions)
    .Map / .MapError / .Bind / .Match / .Tap              → Task<Result<...>> / Task<TOut>
    — sync-lambda and async-lambda (Func<T, Task<TOut>>) overloads both provided
    — outer extension body avoids async/await where only work is awaiting the input (no needless state machine)

BCL extension methods  (all static, no reflection)
    string         : .ToSnakeCase(), .ToCamelCase(), .ToPascalCase(), .IsNullOrWhiteSpace()
    IEnumerable<T> : .ToBatches(int size), .IsNullOrEmpty(), .WhereNotNull()
    DateTimeOffset : .ToUnixMilliseconds(), .StartOfDay(), .EndOfDay()
    Guid           : .IsEmpty()
```

### `SharedKernel.Configuration` — public surface

```
AddValidatedOptions<TOptions>(IConfiguration section)
    → registers IOptions<TOptions>, IOptionsSnapshot<TOptions>,
      IOptionsMonitor<TOptions>, and calls .ValidateDataAnnotations().ValidateOnStart()

[ValidateOptions] attribute
    → marker attribute; triggers DataAnnotations + custom IValidateOptions<T> evaluation at startup
```

### `SharedKernel.Guards` — public surface

```
IGuardClause  (public marker interface — no members)
    — returned by Guard.Against; all guard logic is chained off this interface via extension methods
    — DefaultGuardClause is the private sealed implementation; callers never reference it directly

Guard  (static class)
    .Against                                                  → IGuardClause  (entry point for functional path)

Guard.Throw  (nested static class — imperative path)
    Mirrors every Against.* extension as a void method.
    On non-null Error return: throws DomainException(error).
    On null return (guard passed): returns without throwing.

Guard clause extensions on IGuardClause — all return Error? (null = passed, non-null = violation):

    Null/empty
        .Null<T>(T? value, string paramName)                 → Error?   (reference types only)
        .NullOrEmpty(string? value, string paramName)        → Error?
        .NullOrWhiteSpace(string? value, string paramName)   → Error?

    String length
        .ShorterThan(string value, int minLength, string paramName)   → Error?
        .LongerThan(string value, int maxLength, string paramName)    → Error?

    Numeric  (overloaded for int, decimal, long)
        .NegativeOrZero(T value, string paramName)           → Error?
        .Negative(T value, string paramName)                 → Error?
        .NotPositive(T value, string paramName)              → Error?

    Range
        .OutOfRange<T>(T value, T min, T max, string paramName)  → Error?   (where T : IComparable<T>)

    Default / Guid
        .Default<T>(T value, string paramName)               → Error?   (EqualityComparer<T>.Default — no reflection)
        .InvalidGuid(Guid value, string paramName)           → Error?   (fails on Guid.Empty)

    Format / Email
        .InvalidFormat(string value, string pattern, string paramName)  → Error?
            — uses static compiled Regex field keyed by pattern (ConcurrentDictionary); bounded timeout; zero new Regex per call
        .Email(string? value, string paramName)              → Error?
            — uses same cached-regex strategy; no third-party NuGet

    Collections  (IEnumerable<T> enumerated once per call)
        .Empty<T>(IEnumerable<T> source, string paramName)       → Error?
        .MaxCount<T>(IEnumerable<T> source, int max, string paramName)  → Error?
        .MinCount<T>(IEnumerable<T> source, int min, string paramName)  → Error?

    Boolean predicate  (caller supplies Error — enables arbitrary business-rule guards)
        .True(bool condition, Error error)                   → Error?   (returns error if condition is false)
        .False(bool condition, Error error)                  → Error?   (returns error if condition is true)

    SmartEnum
        .InvalidSmartEnum<TEnum, TValue>(TValue id)          → Error?   (where TEnum : SmartEnum<TEnum,TValue>)
            — calls SmartEnum<TEnum,TValue>.TryFromValue; zero reflection

GuardDescriptions  (internal static class — not public API)
    — all error message templates as const string; {0}/{1} placeholders; string.Format at call site
```

### `SharedKernel.FeatureManagement` — public surface

```
IFeatureManager
    IsEnabledAsync(string feature, CancellationToken ct)                              → bool
    IsEnabledAsync<TContext>(string feature, TContext ctx, CancellationToken ct)      → bool

FeatureDefinition  (sealed record)
    .Name                                                  → string
    .DefaultValue                                          → bool
    .Description                                           → string?

AddSharedKernelFeatureManagement(IConfiguration config)
    → registers IFeatureManager backed by Microsoft.FeatureManagement
```

---

## Implementation Rules

- `SharedKernel.Primitives` has **zero NuGet dependencies** — pure C# only.
- `SharedKernel.Guards` has **zero NuGet dependencies** — references only `SharedKernel.Primitives` and `SharedKernel.Core`.
- `Result<T>` is a **sealed class** (not a struct) — the zero-value problem with generic struct payloads makes struct unsound at scale.
- `Result` (non-generic) may be a **readonly struct** — it carries no typed value payload so the zero-value concern does not apply.
- `Result<T>` must never throw on its own operations (`.IsSuccess`, `.IsFailure`). Only `.Value` and `.Error` accessors throw `InvalidOperationException` on wrong access.
- `Error.None` is the sentinel — never use `null` to represent "no error".
- `ValidationResult` / `ValidationResult<T>` are **distinct** from `Result<T>` — use `ValidationResult` for compound multi-error input validation; use `Result<T>` for single-error operation outcomes. Never conflate the two.
- `ErrorCodes` uses a **nested static class string-constant** approach — not enums. Consuming packages may add local constants without forking the SharedKernel.
- `IClock` is the only permitted source of time in all packages — `DateTime.UtcNow` or `DateTimeOffset.UtcNow` direct usage anywhere in this domain is a hard violation.
- `SmartEnum` value lookup (`FromValue`, `FromName`) must **not** use reflection in the hot path — use a static compile-time list built at type initialization.
- Base exceptions always carry an `Error` payload; string-only constructors are not allowed.
- Async railway extension methods must **not** use `async`/`await` on the outer extension body where the only async work is awaiting the input — avoid unnecessary state machine allocation.
- `AddValidatedOptions` must call `.ValidateOnStart()` — misconfigured apps must fail at startup, not at first access.
- `IFeatureManager` is the only permitted feature-flag interface in consuming services — never inject `Microsoft.FeatureManagement.IFeatureManager` directly.
- Guard extensions return `Error?` — **null means the guard passed**, non-null means violation. Never use `Error.None` as the "passed" sentinel in guard returns; use actual `null` so callers can distinguish cleanly.
- `Guard.Throw.*` methods are thin wrappers: call the matching `Against.*` extension, throw `DomainException(error)` if the result is non-null, otherwise return. No independent logic.
- `IGuardClause` is a public marker interface with no members — `DefaultGuardClause` (the implementation) is `private sealed` to the `Guard` class. Callers must never reference `DefaultGuardClause` directly.
- `InvalidFormat` and `Email` guard extensions must use a **static cached `Regex`** (e.g., via `ConcurrentDictionary<string, Regex>` keyed by pattern) with a bounded `RegexOptions.Compiled` timeout — a new `Regex` instance must never be created per call.
- Collection guards (`Empty`, `MaxCount`, `MinCount`) must enumerate the `IEnumerable<T>` source **at most once** per call — use `Count()` or a single materialization pass.
- `GuardDescriptions` is `internal` — it is not part of the public API and must not be exposed to consumers.
- `InvalidSmartEnum<TEnum, TValue>` must use `SmartEnum<TEnum, TValue>.TryFromValue` — no reflection, no `Enumeration<T>` or parallel type.
- No static mutable state anywhere in this domain.

---

## DI Registration (expected shape)

```csharp
// IClock — needed by any service that reads time
services.AddSingleton<IClock, SystemClock>();

// Validated Options — per-options call, section comes from IConfiguration
services.AddValidatedOptions<MyServiceOptions>(configuration.GetSection("MyService"));

// Feature Management
services.AddSharedKernelFeatureManagement(configuration);
```

`SharedKernel.Primitives`, `SharedKernel.Core`, and `SharedKernel.Guards` ship **no DI extensions** — they are pure libraries.

---

## AOT Compatibility

- `Result<T>`, `Result`, `Error`, `ValidationResult`, `ValidationResult<T>` are sealed classes/records — no reflection, fully AOT-safe.
- `ErrorCodes` is a static class of string constants — no runtime lookup, fully AOT-safe.
- `SmartEnum` base uses a static `IReadOnlyList<TEnum>` built at type-initialization — no reflection in value lookup.
- All railway extension methods are static — AOT-safe by default. Async overloads use `Task` continuation patterns to avoid AOT-hostile constructs.
- `Microsoft.Extensions.Options` is AOT-compatible as of .NET 8+ — verify on each upgrade.
- `Microsoft.FeatureManagement` — verify AOT status on each major upgrade; the `IFeatureManager` wrapper allows a swap if needed.
- All BCL extension methods are static — AOT-safe by default.
- `IGuardClause` and all guard extension methods are static — AOT-safe. `DefaultGuardClause` is sealed, no virtual dispatch.
- `EqualityComparer<T>.Default` used in `Default<T>` guard is AOT-safe — it uses static dispatch via generic specialization in .NET 10.
- `InvalidFormat` / `Email` use `Regex` constructed with `RegexOptions.Compiled` in a static field — the compiled delegate is created once at type-initialization, which is AOT-compatible. `ConcurrentDictionary` is used only for pattern-keyed caching of caller-supplied patterns in `InvalidFormat`; the email regex is a fixed static field.
- `OutOfRange<T>` uses the `IComparable<T>` constraint — static generic dispatch, no boxing for value types, AOT-safe.

---

## Test Rules

- Unit tests for each package live in the nested `.Tests/` folder inside that package's folder.
- `SharedKernel.Primitives.Tests/` — Result, Error, IClock, SmartEnum
- `SharedKernel.Core.Tests/` — exceptions, railway extensions, BCL extensions
- `SharedKernel.Guards.Tests/` — guard functional path (Against.*), guard throw path (Throw.*), boundary theories
- `SharedKernel.Configuration.Tests/` — ValidatedOptions eager validation
- `SharedKernel.FeatureManagement.Tests/` — IFeatureManager enable/disable, context variant
- Railway-extension chains must be covered: map → bind → match over both success and failure paths.
- `SmartEnum` must cover: FromValue hit, FromValue miss (throws), TryFromValue, List completeness.
- Validated options test must assert that a misconfigured `TOptions` throws at `IHost.StartAsync()`.
- Guard tests must cover **both paths independently**: functional `Against.*` (assert returned `Error?`) and throw `Throw.*` (assert `DomainException` thrown on violation, no exception on pass).
- Numeric and string-length guard tests must use `[Theory]` with `[InlineData]` for boundary conditions (exactly at limit, one below, one above).
- Collection guard tests must verify single enumeration — use a counting stub/wrapper `IEnumerable<T>` that increments a counter on `GetEnumerator()` calls.

---

## Changelog

> Maintained by the core domain agent. One line per significant change.

- [2026-05-14] Domain brain initialized — packages, interfaces, rules, AOT notes
- [2026-05-14] P-001/P-002 applied — added ValidationResult pair, ErrorCodes static class, clarified Result<T> as sealed class vs Result readonly struct, added MapError + void Match on non-generic Result, added async state machine allocation rule
- [2026-05-14] P-003 applied — added SharedKernel.Guards package: IGuardClause marker, Guard.Against/Guard.Throw entry points, full guard extension surface (null/empty, string length, numeric, range, default, Guid, format, email, collection, boolean predicate, SmartEnum), GuardDescriptions internal class, AOT notes for cached Regex and EqualityComparer<T>.Default, updated test rules with boundary theory and single-enumeration requirements
- [2026-05-27] P-042 applied — added ErrorType.BusinessRule enum member (HTTP 422 / domain-invariant-violation semantics), Error.BusinessRule(string code, string message) factory method, ErrorCodes.Domain nested class with RuleViolated constant; all additive — no existing types changed
