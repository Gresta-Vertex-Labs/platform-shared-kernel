# SharedKernel.Configuration

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Tier: Foundation](https://img.shields.io/badge/tier-Foundation-2ea44f)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)
![Trimming: declared, not hidden](https://img.shields.io/badge/trimming-declared%2C%20not%20hidden-yellow)

> **Options-pattern registration that fails at startup, not at first use: `AddValidatedOptions` binds a section,
> validates it, and makes `IHost.StartAsync()` throw when it is wrong.**

A missing connection string should fail the deployment, not whichever request first needs it, hours later.

| You get | So that |
| --- | --- |
| `AddValidatedOptions<T>(…)`, always `ValidateOnStart` | Bad configuration stops the host instead of failing the first request |
| Data Annotations, a custom `IValidateOptions<T>`, an `[OptionsValidator]`-generated validator, or several at once | Per-property and cross-property rules both run, each failure reported once |
| `ISectionBoundOptions` (`static string SectionName`) | The section path lives on the options type; no call site retypes it or passes the wrong one |
| `OptionsStrictness.RequireSection` / `RejectUnknownKeys` | A misspelled section path or key is rejected instead of silently binding defaults |
| Named instances | Each named instance is bound and validated on its own |

## Contents

- [Install](#install)
- [Quick start](#quick-start)
- [How it works](#how-it-works)
- [Recipes](#recipes)
- [Reference](#reference)
- [Testing](#testing)
- [Pitfalls](#pitfalls)
- [Design decisions](#design-decisions)

## Install

```xml
<PackageReference Include="SharedKernel.Configuration" />
```

The version comes from your central `SharedKernelVersion` property — every SharedKernel package ships at the same
version. See [Using the packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel#using-the-packages).

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Foundation — reference it from **any** project |
| Depends on | `Microsoft.Extensions.Options.DataAnnotations`, `Microsoft.Extensions.Options.ConfigurationExtensions`, `Microsoft.Extensions.Configuration.Abstractions`, `Microsoft.Extensions.DependencyInjection.Abstractions`; no other SharedKernel package |
| Namespaces | `SharedKernel.Configuration` (`ISectionBoundOptions`, `OptionsStrictness`), `SharedKernel.Configuration.Extensions` (`AddValidatedOptions`) |

## Quick start

```csharp
using System.ComponentModel.DataAnnotations;
using SharedKernel.Configuration;
using SharedKernel.Configuration.Extensions;

public sealed class DatabaseOptions : ISectionBoundOptions
{
    public static string SectionName => "MyService:Database";

    [Required]       public string ConnectionString { get; set; } = string.Empty;
    [Range(1, 1000)] public int    MaxConnections   { get; set; } = 10;
}

builder.Services.AddValidatedOptions<DatabaseOptions>(
    builder.Configuration,
    strictness: OptionsStrictness.RequireSection | OptionsStrictness.RejectUnknownKeys);
```

```json
{
  "MyService": {
    "Database": { "ConnectionString": "Host=db;Database=orders", "MaxConnections": 50 }
  }
}
```

```csharp
using Microsoft.Extensions.Options;

public sealed class Repository(IOptions<DatabaseOptions> options)
{
    private readonly string connectionString = options.Value.ConnectionString;
}
```

With this registration, each of these stops the host from starting:

| Configuration | Fails with |
| --- | --- |
| `ConnectionString` missing | `OptionsValidationException`: the `ConnectionString` field is required |
| `"MaxConnections": 5000` | `OptionsValidationException`: out of range |
| `"MaxConnections": "lots"` | `InvalidOperationException`: cannot convert `'lots'` |
| Section written as `"MyService:Databse"` | `OptionsValidationException`: section `'MyService:Database'` does not exist |
| Key written as `"MaxConections"` | `InvalidOperationException`: `'MaxConections'` not found on `DatabaseOptions` |

## How it works

- Every overload registers `IOptions<T>`, `IOptionsSnapshot<T>` and `IOptionsMonitor<T>`, and calls `ValidateOnStart`,
  so `IHost.StartAsync()` throws on bad configuration.
- The Data Annotations validator is registered once per type and name (a pre-built instance with a duplicate check), so
  calling twice never reports a failure twice. The BCL's own `ValidateDataAnnotations()` duplicates both.
- Custom validators are added with `TryAddEnumerable`: a validator is one of *many*, and the pipeline runs every one.
- `ISectionBoundOptions.SectionName` is a `static abstract` property, read through a generic type parameter — a direct
  static call, no reflection. A `null`, empty or whitespace value throws `InvalidOperationException` naming the type at
  registration. Declare it as a `static` property, not a `const` (a field cannot satisfy it); it stays readable as
  `DatabaseOptions.SectionName`. An explicit `IConfigurationSection` overload still wins overload resolution.
- Strictness checks run every time the instance is validated, so they also apply after a configuration reload.

| Flag | Rejects | Fails as |
| --- | --- | --- |
| `RequireSection` | A section that does not exist. `"Database": {}` and `"Database": null` count as missing; a lone environment variable such as `MyService__Database__Port` counts as present | `OptionsValidationException`, collected with the instance's other failures |
| `RejectUnknownKeys` | A key with no matching property, including inside nested objects. Case-insensitive; keys under a dictionary property are always accepted | `InvalidOperationException` from the binder, naming every unknown key |

## Recipes

### 1. A rule Data Annotations cannot express

```csharp
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;
using SharedKernel.Configuration.Extensions;

public sealed class PoolOptions
{
    [Range(1, 1000)] public int MinSize { get; set; } = 1;
    [Range(1, 1000)] public int MaxSize { get; set; } = 10;
}

public sealed class PoolOptionsValidator : IValidateOptions<PoolOptions>
{
    public ValidateOptionsResult Validate(string? name, PoolOptions options) =>
        options.MinSize <= options.MaxSize
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("MinSize must not exceed MaxSize.");
}

// validateDataAnnotations: true keeps [Range] enforced alongside the custom rule.
builder.Services.AddValidatedOptions<PoolOptions, PoolOptionsValidator>(
    builder.Configuration.GetSection("MyService:Pool"),
    validateDataAnnotations: true);
```

### 2. Validate with no reflection

```csharp
using Microsoft.Extensions.Options;
using SharedKernel.Configuration.Extensions;

[OptionsValidator]
public sealed partial class DatabaseOptionsValidator : IValidateOptions<DatabaseOptions>
{
}

builder.Services.AddValidatedOptions<DatabaseOptions, DatabaseOptionsValidator>(builder.Configuration);
```

`[OptionsValidator]` ships in `Microsoft.Extensions.Options`; the BCL source generator writes `Validate(...)` from the
same attributes. Leave `validateDataAnnotations` at `false` here, or every failure is reported twice.

### 3. Validate nested objects and collections

Data Annotations on a nested object's properties, or on collection items, **are not validated by default**. Mark them:

```csharp
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

public sealed class Endpoint
{
    [Required] public string? Host { get; set; }
}

public sealed class GatewayOptions
{
    [ValidateObjectMembers]   public Endpoint       Primary   { get; set; } = new();
    [ValidateEnumeratedItems] public List<Endpoint> Fallbacks { get; set; } = [];
}
```

### 4. Register named instances

```csharp
using Microsoft.Extensions.Options;
using SharedKernel.Configuration.Extensions;

builder.Services.AddValidatedOptions<ClientOptions>(
    builder.Configuration.GetSection("Clients:Primary"), name: "primary");
builder.Services.AddValidatedOptions<ClientOptions>(
    builder.Configuration.GetSection("Clients:Secondary"), name: "secondary");

public sealed class Caller(IOptionsMonitor<ClientOptions> options)
{
    private ClientOptions Primary => options.Get("primary");
}
```

An invalid instance fails startup and names itself in `OptionsValidationException.OptionsName`; valid siblings are
unaffected. `RequireSection` is scoped to the name it was registered for.

### 5. Add a post-configure step

Every registration returns `IServiceCollection`, not `OptionsBuilder<T>`. For `.PostConfigure(...)` or a lambda
`.Validate(...)`, also call `services.AddOptions<T>(name)` — it returns a builder for the same named instance.

## Reference

### Registration

| You have | Call |
| --- | --- |
| Attributes only, a section in hand | `AddValidatedOptions<T>(section, name?, strictness?)` |
| Attributes only, `T : ISectionBoundOptions` | `AddValidatedOptions<T>(configuration, name?, strictness?)` |
| A custom or generated validator, a section in hand | `AddValidatedOptions<T, TValidator>(section, validateDataAnnotations: false, name?, strictness?)` |
| A custom or generated validator, `T : ISectionBoundOptions` | `AddValidatedOptions<T, TValidator>(configuration, validateDataAnnotations: false, name?, strictness?)` |

`OptionsStrictness` is a flags enum: `None` (default), `RequireSection`, `RejectUnknownKeys`. Pass optional arguments by name.

### Failures

| When | Exception | Cause |
| --- | --- | --- |
| At the `AddValidatedOptions` call | `ArgumentNullException` | `services`, `section` or `configuration` is null |
| At the `AddValidatedOptions` call | `ArgumentOutOfRangeException` | `strictness` contains an undefined bit |
| At the `AddValidatedOptions` call | `InvalidOperationException` | `ISectionBoundOptions.SectionName` is null, empty or whitespace |
| At `IHost.StartAsync()` | `OptionsValidationException` | An attribute, a validator or `RequireSection` rejected the values; all failures are in `Failures` |
| At `IHost.StartAsync()` | `InvalidOperationException` | Binding failed before validation: an unconvertible value, or an unknown key under `RejectUnknownKeys` |

### Logging

The package does not log.

## Testing

Validation is armed by `ValidateOnStart`, which runs only under a real host. Assert configuration failures at
`StartAsync`:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SharedKernel.Configuration.Extensions;

var builder = Host.CreateApplicationBuilder();
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["MyService:Database:MaxConnections"] = "5000",
});
builder.Services.AddValidatedOptions<DatabaseOptions>(builder.Configuration);

using var host = builder.Build();
await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
```

With a bare `ServiceCollection`, resolving `IOptions<T>` succeeds and the first read of `.Value` throws instead.

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| `services.Configure<T>(section)` | `AddValidatedOptions<T>(…)` | `Configure<T>` binds without validating, so failures surface at first use |
| Retype a section path in `GetSection("…")` | Implement `ISectionBoundOptions` | One declaration; `SK0022` flags raw literals at `GetSection` |
| Inject `IOptions<T>` for a named instance | `IOptionsMonitor<T>.Get(name)` or `IOptionsSnapshot<T>` | `IOptions<T>` resolves only the default instance, silently, with defaults |
| Expect a `TValidator` to apply to one name | Check `name` and return `ValidateOptionsResult.Skip` for others | A custom validator is type-wide; Data Annotations are per name |
| Trust a property initializer | Validate the value | A key present but `null` binds as the type's default (`int` initialized to `30` becomes `0`); an absent key keeps the initializer |
| Rely on binding for a property with a non-public setter | Give it a public setter | It is never bound, and `RejectUnknownKeys` does not report its key |
| Ignore reload failures | Treat a reload with invalid values as an error path | Once `IOptionsMonitor<T>` is resolved, `IConfigurationRoot.Reload()` throws `AggregateException`; with `reloadOnChange: true` on the file watcher's thread |
| Rename `SectionName` casually | Treat it as a deployment contract | Every deployed `appsettings.json` and environment variable targets the old path |
| Turn on `RejectUnknownKeys` everywhere by default | Opt in where configuration and code roll out together | New keys going live before every pod is upgraded crash the old pods |

## Design decisions

**Why are both strictness flags off by default?** `RequireSection` would refuse a service whose options are fully
defaulted and deliberately have no section in some environment; `RejectUnknownKeys` breaks rolling deployments. Nothing
is on by default that could reject configuration a service previously accepted.

**Why is the package not trim- or AOT-safe?** Configuration binding is reflective (`OptionsBuilder<T>.Bind` carries
`[RequiresUnreferencedCode]`/`[RequiresDynamicCode]`), and the binding source generator intercepts `Bind` calls only in
the *calling* assembly. So every overload declares both attributes instead of suppressing them, and every `TOptions`
carries `[DynamicallyAccessedMembers]`. Members of complex types nested inside `TOptions` can still be trimmed: for a
native-AOT publish keep options flat or bind them by hand in the application.

**Why exceptions and not `Result`?** Options validation is a startup fail-fast concern — which is also why this package
does not depend on `SharedKernel.Primitives`.

**What is deliberately not here?** No `IConfiguration` wrapper or section-name builder; no secrets provider (Key Vault as
a configuration source is `SharedKernel.ServiceDefaults.Configuration.KeyVault`); no FluentValidation adapter —
`IValidateOptions<T>` is the seam.

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) ·
[Foundation packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/src/Foundation/README.md) ·
[MIT license](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
