# SharedKernel.Configuration

Options-pattern validation helpers for the Platform.SharedKernel ecosystem. Depends on `SharedKernel.Primitives`.

## Included

**`AddValidatedOptions<TOptions>(IConfiguration section)`** — DI extension that:
1. Binds configuration section to `TOptions`
2. Registers `IOptions<T>`, `IOptionsSnapshot<T>`, `IOptionsMonitor<T>`
3. Calls `.ValidateDataAnnotations()` — enforces `[Required]`, `[Range]`, etc.
4. Calls `.ValidateOnStart()` — misconfigured apps fail at startup, not at first use

## Quick Start

```csharp
// Define options
public sealed class DatabaseOptions
{
    [Required] public string ConnectionString { get; init; } = "";
    [Range(1, 1000)] public int MaxConnections { get; init; } = 10;
}

// Register (Program.cs)
builder.Services.AddValidatedOptions<DatabaseOptions>(
    builder.Configuration.GetSection("Database"));

// Consume
public class MyService(IOptions<DatabaseOptions> opts) { }
```

Missing or invalid configuration causes the host to throw at startup — not silently at the first consumer request.

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) — see the [01.Core README](../README.md) for the full capability overview.
