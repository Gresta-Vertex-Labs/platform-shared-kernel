# SharedKernel.Core

Core building blocks for the Platform.SharedKernel ecosystem: exception hierarchy, railway extension methods, and BCL helpers. Depends on `SharedKernel.Primitives`.

## Included Types

**Base Exception Hierarchy** — all carry an `Error` payload (no string-only constructors):
- `SharedKernelException` — base
- `DomainException`, `ValidationException`, `NotFoundException`, `ConflictException`, `UnauthorizedException`

**Result&lt;T&gt; Railway Extensions** — static, AOT-safe:
- `.Map<TOut>`, `.MapError`, `.Bind<TOut>`, `.Match<TOut>`, `.Tap`
- Async overloads: `Task<Result<T>>` variants with both sync and async lambdas

**BCL Extension Methods**:
- `string`: `.ToSnakeCase()`, `.ToCamelCase()`, `.ToPascalCase()`, `.IsNullOrWhiteSpace()`
- `IEnumerable<T>`: `.ToBatches(int)`, `.IsNullOrEmpty()`, `.WhereNotNull()`
- `DateTimeOffset`: `.ToUnixMilliseconds()`, `.StartOfDay()`, `.EndOfDay()`
- `Guid`: `.IsEmpty()`

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
```

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) — see the [01.Core README](../README.md) for the full capability overview.
