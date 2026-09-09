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

## Source-Generated Options Validation (opt-in, AOT-clean)

The `AddValidatedOptions<TOptions>` overload above remains the platform **default** — every existing
consumer already depends on its Data Annotations + reflection-based validation, and nothing about it
changes.

For a strict AOT/trimming posture, or simply to avoid reflection at validation time entirely, an
additive **`AddValidatedOptions<TOptions, TValidator>(IConfiguration section)`** overload is also
available. It binds the section with **no** `.ValidateDataAnnotations()` call, registers your
`TValidator` via `TryAddSingleton<IValidateOptions<TOptions>, TValidator>()`, and still calls
`.ValidateOnStart()` — identical fail-at-startup semantics to the default path.

`TValidator` is typically a `partial class` annotated with the in-box BCL
[`[OptionsValidator]`](https://learn.microsoft.com/dotnet/api/microsoft.extensions.options.optionsvalidatorattribute)
source generator (part of the base `Microsoft.Extensions.Options` package — no extra NuGet reference
needed). The generator reads the same `[Required]`/`[Range]`/etc. attributes on `TOptions` and emits the
`Validate` method body at **compile time**, so the resulting validator is zero-reflection at runtime. Any
other hand-written `IValidateOptions<TOptions>` works too — this overload does not require or reference
the generator itself, only the resulting interface.

```csharp
using Microsoft.Extensions.Options;

// Same TOptions class as the DataAnnotations example above — no changes needed.
public sealed class DatabaseOptions
{
    [Required] public string ConnectionString { get; init; } = "";
    [Range(1, 1000)] public int MaxConnections { get; init; } = 10;
}

// A partial class annotated [OptionsValidator] — the generator fills in Validate(...).
[OptionsValidator]
public partial class DatabaseOptionsValidator : IValidateOptions<DatabaseOptions>
{
}

// Register (Program.cs) — note the second generic argument, TValidator.
builder.Services.AddValidatedOptions<DatabaseOptions, DatabaseOptionsValidator>(
    builder.Configuration.GetSection("Database"));
```

Choose the source-generated path when reflection at startup is undesirable; keep using the default
Data Annotations overload otherwise — both call `.ValidateOnStart()` and fail identically at
`IHost.StartAsync()` for a misconfigured application.

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) — see the [01.Core README](../README.md) for the full capability overview.
