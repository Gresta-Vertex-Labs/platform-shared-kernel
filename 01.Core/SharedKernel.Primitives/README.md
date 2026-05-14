# SharedKernel.Primitives

Foundational primitives for the Platform.SharedKernel ecosystem. Zero external dependencies. AOT-compatible.

## Included Types

- `Result<T>` — sealed class for single-error railway-oriented results
- `Result` — non-generic readonly struct for void operations
- `Error` / `ErrorType` / `ErrorCodes` — structured error values with factory methods
- `ValidationResult` / `ValidationResult<T>` — multi-error aggregate for input validation
- `IClock` / `SystemClock` — time abstraction (never use `DateTime.UtcNow` directly)
- `SmartEnum<TEnum, TValue>` — AOT-safe strongly-typed enum base

## Quick Start

```csharp
// Result<T>
Result<int> result = Result<int>.Success(42);
Result<int> failure = Result<int>.Failure(Error.NotFound("order.notfound", "Order not found."));

// Error
Error err = Error.Validation("user.name.required", "Name is required.");

// IClock
services.AddClock(); // registers SystemClock

// SmartEnum
public sealed class OrderStatus : SmartEnum<OrderStatus, int>
{
    public static readonly OrderStatus Pending  = new("Pending",  1);
    public static readonly OrderStatus Complete = new("Complete", 2);
    private OrderStatus(string name, int value) : base(name, value) { }
}
```

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) — see the [01.Core README](../README.md) for the full capability overview.
