# SharedKernel.Configuration

One job: an options class reaches your code **already proven valid**, or the service refuses to
start. A missing connection string should fail the deployment, not the first request that happens
to need it.

Two public types — `OptionsExtensions.AddValidatedOptions` and `ISectionBoundOptions`. No
`SharedKernel` dependencies; four first-party `Microsoft.Extensions.*` packages.

## Rules

| Rule | What enforces it |
| --- | --- |
| Register every options type through `AddValidatedOptions` — never `services.Configure<T>(section)` | Convention; `Configure<T>` binds without validating, so failures surface at first use instead of at startup |
| Declare the section path on the options type, not at the call site | `ISectionBoundOptions.SectionName` — a `static abstract` member, so the compiler rejects a type that lacks it |
| Never retype a section path as a literal in `GetSection("…")` | `SK0022` (`00.Governance`) flags raw literals at magic-string call sites; the `IConfiguration` overloads remove the argument entirely |
| Per-property rules go in Data Annotations; cross-property rules go in an `IValidateOptions<T>` | The `TValidator` overloads. An attribute cannot see a second property |
| A validator is one of *many*, never "the" validator | Registration uses `TryAddEnumerable`. `IValidateOptions<T>` is a collection: the pipeline runs every registered validator |
| Consume named options through `IOptionsMonitor<T>`/`IOptionsSnapshot<T>` | `IOptions<T>` resolves only the default instance — silently, with defaults |
| Treat `SectionName` as a deployment contract | Changing it stops binding every deployed `appsettings.json` and env var targeting the old path, and the type then binds to defaults |
| Do not expect these methods to be trim- or AOT-safe | `[RequiresUnreferencedCode]` + `[RequiresDynamicCode]` on every overload — declared, not hidden |

## Usage

### Data Annotations — the common case

```csharp
public sealed class DatabaseOptions
{
    [Required]            public string ConnectionString { get; set; } = string.Empty;
    [Range(1, 1000)]      public int    MaxConnections   { get; set; } = 10;
}

builder.Services.AddValidatedOptions<DatabaseOptions>(
    builder.Configuration.GetSection("SharedKernel:Database"));
```

Registers `IOptions<T>`, `IOptionsSnapshot<T>` and `IOptionsMonitor<T>`, and makes
`IHost.StartAsync()` throw `OptionsValidationException` on bad configuration.

### Let the type name its own section

```csharp
public sealed class DatabaseOptions : ISectionBoundOptions
{
    public static string SectionName => "SharedKernel:Database";

    [Required] public string ConnectionString { get; set; } = string.Empty;
}

builder.Services.AddValidatedOptions<DatabaseOptions>(builder.Configuration);
```

No section path at the call site, so no call site can pass the wrong one. `SectionName` is read
through a generic type parameter, which compiles to a direct static call — no reflection.

**Adopting it:** declare `SectionName` as a `static` property, not a `const` field — a field cannot
satisfy a `static abstract` property. It stays readable as `DatabaseOptions.SectionName`, so
existing `GetSection(DatabaseOptions.SectionName)` call sites keep compiling. Adoption is per
options type and entirely optional; the explicit-section overloads work on any class.

### A rule Data Annotations cannot express

```csharp
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

// validateDataAnnotations: true keeps [Required]/[Range] enforced alongside the custom rule.
builder.Services.AddValidatedOptions<PoolOptions, PoolOptionsValidator>(
    builder.Configuration.GetSection("SharedKernel:Pool"),
    validateDataAnnotations: true);
```

### Validation with no reflection at all

```csharp
[OptionsValidator]
public sealed partial class DatabaseOptionsValidator : IValidateOptions<DatabaseOptions>
{
}

builder.Services.AddValidatedOptions<DatabaseOptions, DatabaseOptionsValidator>(
    builder.Configuration);
```

`[OptionsValidator]` is in the base `Microsoft.Extensions.Options` package — no extra reference.
The BCL source generator writes `Validate(...)` at compile time from the same
`[Required]`/`[Range]` attributes, so **validation** does no reflection. Leave
`validateDataAnnotations` at its `false` default here: the generated validator already covers those
attributes, and turning it on reports every attribute failure twice.

### Named instances

```csharp
public sealed class ClientOptions
{
    [Range(1, 300)] public int TimeoutSeconds { get; set; } = 30;
}

builder.Services.AddValidatedOptions<ClientOptions>(
    builder.Configuration.GetSection("Clients:Primary"), name: "primary");
builder.Services.AddValidatedOptions<ClientOptions>(
    builder.Configuration.GetSection("Clients:Secondary"), name: "secondary");

// Consume with the monitor, never IOptions<T>.
public sealed class Caller(IOptionsMonitor<ClientOptions> options)
{
    private ClientOptions Primary => options.Get("primary");
}
```

Each named instance is validated on its own: one invalid instance fails startup and names itself
in `OptionsValidationException.OptionsName`, while its valid siblings are unaffected.

## Which overload

| You have | Call |
| --- | --- |
| Attributes only, a section in hand | `AddValidatedOptions<T>(section)` |
| Attributes only, `T : ISectionBoundOptions` | `AddValidatedOptions<T>(configuration)` |
| A custom or generated validator, a section in hand | `AddValidatedOptions<T, TValidator>(section)` |
| A custom or generated validator, `T : ISectionBoundOptions` | `AddValidatedOptions<T, TValidator>(configuration)` |

Passing an `IConfigurationSection` always wins over the type's declared path — useful for pointing
one options type at a different section, and the reason both forms can coexist unambiguously.

## Traps

Each of these was measured, not inferred.

**`ValidateOnStart()` needs a real host.** With a bare `ServiceCollection` and
`BuildServiceProvider()` — a worker with no `IHost`, or a unit test — nothing validates eagerly.
Resolving `IOptions<T>` succeeds; the first read of `.Value` throws. The fail-fast guarantee is
`IHost.StartAsync()`'s, not this package's.

**A bad config reload throws from `Reload()` itself.** Once anything has resolved
`IOptionsMonitor<T>`, its change callback re-creates and re-validates eagerly, so
`IConfigurationRoot.Reload()` throws `AggregateException` wrapping `OptionsValidationException`.
Under `reloadOnChange: true` that lands on the file-watcher's thread, where no caller is waiting to
catch it — an edit to a live `appsettings.json` can therefore surface as an unobserved exception
rather than a clean restart. With no monitor resolved, `Reload()` is silent and the failure defers
to the next read.

**A custom validator is type-wide; Data Annotations are per-name.** The BCL's
`DataAnnotationValidateOptions<T>` is scoped to one name and skips the others. A `TValidator` is
registered once against `IValidateOptions<T>`, so it runs for **every** named instance of that
type. A validator that should apply to one name must check its own `name` argument and return
`ValidateOptionsResult.Skip` otherwise.

**An explicitly-null config value overwrites a property initializer.** A key that is present but
null binds as the property type's default, not as the initializer: `public string Name { get; set; }
= string.Empty;` holds `null` at runtime despite its non-nullable declaration, and an
`int` initialized to `30` becomes `0`. Both measured. Validate rather than trusting the
initializer — and note this is distinct from an *absent* key, which leaves the initializer intact.

**A missing section is not an error by itself.** Binding an absent section leaves every property at
its default and reports only whatever validation then rejects — which is why registering without
validation hides a typo in a section path indefinitely.

## Trimming and AOT

This package is **not** trim- or AOT-clean, by nature rather than by omission. Configuration
binding is reflective: the BCL's own `OptionsBuilder<T>.Bind` carries both
`[RequiresUnreferencedCode]` and `[RequiresDynamicCode]`, and a generic library wrapper cannot
avoid that — .NET's configuration-binding source generator intercepts `Bind` calls in the *calling*
assembly, so it can never specialize a `Bind<TOptions>` that lives in a library and is generic over
an options type it has not seen.

So both overloads declare those attributes rather than suppressing them, and every `TOptions`
carries `[DynamicallyAccessedMembers]` so a trimmer preserves the properties and parameterless
constructor the binder needs. Measured: **zero** IL warnings under `EnableTrimAnalyzer` and
`EnableAotAnalyzer`, because the requirement is declared. The residual risk is real and sits with
the caller: an options class whose own properties are complex types can have those nested members
trimmed away. For a trimmed or native-AOT publish, either keep such options flat or bind them by
hand in the application, where the source generator can see the concrete type.

## Deliberately not here

- **No `IConfiguration` wrapper, provider, or section-name builder.** The BCL's configuration stack
  is not improved by another layer over it; this package only adds the validation guarantee.
- **No secrets provider.** Key Vault as a configuration source is
  `13.ServiceDefaults`' `AddSharedKernelKeyVaultConfiguration()`.
- **No FluentValidation adapter.** `IValidateOptions<T>` is the seam; a consumer wanting
  FluentValidation implements it in one short class. For domain-format validators (IBAN, PAN,
  national IDs) see `SharedKernel.Validation`.
- **No `Result<T>`.** Options validation is a startup-time fail-fast concern, so it throws. That is
  also why this package has no `SharedKernel.Primitives` dependency at all.

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) — see
the [01.Core README](../README.md) for the full capability overview.
