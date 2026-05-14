# 01.Core — Domain Brain

## What This Domain Is

The foundational building blocks domain. Every other domain in the shared kernel depends on this layer, so it must reference nothing. It ships four independent packages covering: functional primitives (`Result<T>`, `Error`), system abstractions (`IClock`, SmartEnums, base exceptions, BCL extensions), Options-pattern validation, and a Feature Flag abstraction.

Philosophy: **Zero external dependencies for Primitives. Pure C#. AOT-first. Railway-oriented.**

---

## Packages

| Package | Role | References |
|---------|------|-----------|
| `SharedKernel.Primitives` | `Result<T>`, `Error`, `ErrorType`, `IClock`, `SmartEnum<TEnum,TValue>` | nothing |
| `SharedKernel.Core` | Base exceptions, BCL extension methods, `Result<T>` railway extensions | `SharedKernel.Primitives` |
| `SharedKernel.Configuration` | Options-pattern validation, `AddValidatedOptions` DI extension | `SharedKernel.Primitives` |
| `SharedKernel.FeatureManagement` | `IFeatureManager` abstraction + `Microsoft.FeatureManagement` adapter | `SharedKernel.Primitives` |

All four target `net10.0`. Test sub-folders live inside each project folder (never in a top-level `tests/`).

---

## Technology Stack

| Concern | Technology |
|---------|-----------|
| Functional primitives | Pure C# 13 — no NuGet dependencies |
| System abstractions | Pure C# 13 — no NuGet dependencies |
| Options validation | `Microsoft.Extensions.Options.DataAnnotations` |
| Feature flags | `Microsoft.FeatureManagement` (abstracted behind `IFeatureManager`) |

---

## Interface Contracts

### `SharedKernel.Primitives` — public surface

```
Result<T>  (sealed)
    .Success(T value)                                      → Result<T>
    .Failure(Error error)                                  → Result<T>
    .IsSuccess                                             → bool
    .IsFailure                                             → bool
    .Value                                                 → T   (throws InvalidOperationException if failure)
    .Error                                                 → Error (throws InvalidOperationException if success)
    implicit operator Result<T>(T value)                   → Result<T>.Success
    implicit operator Result<T>(Error error)               → Result<T>.Failure

Result  (non-generic, for void operations)
    .Success()                                             → Result
    .Failure(Error error)                                  → Result

Error  (sealed record)
    .None                                                  → Error (sentinel — no error)
    .Unexpected(string code, string message)               → Error
    .Validation(string code, string message)               → Error
    .NotFound(string code, string message)                 → Error
    .Conflict(string code, string message)                 → Error
    .Unauthorized(string code, string message)             → Error
    .Code                                                  → string
    .Message                                               → string
    .Type                                                  → ErrorType

ErrorType  (enum)
    None | Unexpected | Validation | NotFound | Conflict | Unauthorized

IClock
    UtcNow                                                 → DateTimeOffset
    Today                                                  → DateOnly

SystemClock  (sealed class, implements IClock)
    — wraps DateTimeOffset.UtcNow

SmartEnum<TEnum, TValue>  (abstract base, TEnum : SmartEnum<TEnum,TValue>)
    .FromValue(TValue value)                               → TEnum   (throws if not found)
    .TryFromValue(TValue value, out TEnum? result)         → bool
    .FromName(string name)                                 → TEnum   (throws if not found)
    .List                                                  → IReadOnlyList<TEnum>
    .Name                                                  → string
    .Value                                                 → TValue
```

### `SharedKernel.Core` — public surface

```
Base exceptions  (all derive from SharedKernelException)
    SharedKernelException(string message, Error error)
    DomainException(Error error)
    ValidationException(IReadOnlyList<Error> errors)
    NotFoundException(Error error)
    ConflictException(Error error)
    UnauthorizedException(Error error)

Result<T> railway extension methods
    .Map<TOut>(Func<T, TOut> map)                          → Result<TOut>
    .MapError(Func<Error, Error> map)                      → Result<T>
    .Bind<TOut>(Func<T, Result<TOut>> bind)               → Result<TOut>
    .Match<TOut>(Func<T, TOut> onSuccess, Func<Error, TOut> onFailure) → TOut
    .Tap(Action<T> action)                                 → Result<T>

Async railway overloads (Task<Result<T>> extensions)
    .Map / .MapError / .Bind / .Match / .Tap              → Task<Result<...>>

BCL extension methods
    string  : .ToSnakeCase(), .ToCamelCase(), .ToPascalCase(), .IsNullOrWhiteSpace()
    IEnumerable<T> : .ToBatches(int size), .IsNullOrEmpty(), .WhereNotNull()
    DateTimeOffset  : .ToUnixMilliseconds(), .StartOfDay(), .EndOfDay()
    Guid            : .IsEmpty()
```

### `SharedKernel.Configuration` — public surface

```
AddValidatedOptions<TOptions>(IConfiguration section)
    → registers IOptions<TOptions>, IOptionsSnapshot<TOptions>,
      IOptionsMonitor<TOptions>, and calls .ValidateDataAnnotations().ValidateOnStart()

[ValidateOptions] attribute
    → marker attribute; triggers DataAnnotations + custom IValidateOptions<T> evaluation at startup
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
- `Result<T>` must never throw on its own operations (`.IsSuccess`, `.IsFailure`). Only `.Value` and `.Error` accessors throw on wrong access.
- `Error.None` is the sentinel — never use `null` to represent "no error".
- `IClock` is the only permitted source of time in all packages — `DateTime.UtcNow` direct usage is a bug.
- `SmartEnum` value lookup (`FromValue`, `FromName`) must **not** use reflection in the hot path — use a static compile-time list.
- Base exceptions always carry an `Error` payload; string-only constructors are not allowed.
- `AddValidatedOptions` must call `.ValidateOnStart()` — misconfigured apps must fail at startup, not at first access.
- `IFeatureManager` is the only permitted feature-flag interface in consuming services — never inject `Microsoft.FeatureManagement.IFeatureManager` directly.
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

`SharedKernel.Primitives` and `SharedKernel.Core` ship **no DI extensions** — they are pure libraries.

---

## AOT Compatibility

- `Result<T>` and `Error` are sealed records/classes — no reflection, fully AOT-safe.
- `SmartEnum` base uses a static `IReadOnlyList<TEnum>` built at type-initialization — no reflection in value lookup.
- `Microsoft.Extensions.Options` is AOT-compatible as of .NET 8+ — verify on each upgrade.
- `Microsoft.FeatureManagement` — verify AOT status on each major upgrade; the `IFeatureManager` wrapper allows a swap if needed.
- All extension methods are static — AOT-safe by default.

---

## Test Rules

- Unit tests for each package live in the nested `.Tests/` folder inside that package's folder.
- `SharedKernel.Primitives.Tests/` — Result, Error, IClock, SmartEnum
- `SharedKernel.Core.Tests/` — exceptions, railway extensions, BCL extensions
- `SharedKernel.Configuration.Tests/` — ValidatedOptions eager validation
- `SharedKernel.FeatureManagement.Tests/` — IFeatureManager enable/disable, context variant
- Railway-extension chains must be covered: map → bind → match over both success and failure paths.
- `SmartEnum` must cover: FromValue hit, FromValue miss (throws), TryFromValue, List completeness.
- Validated options test must assert that a misconfigured `TOptions` throws at `IHost.StartAsync()`.

---

## Changelog

> Maintained by the core domain agent. One line per significant change.

- [2026-05-14] Domain brain initialized — packages, interfaces, rules, AOT notes
