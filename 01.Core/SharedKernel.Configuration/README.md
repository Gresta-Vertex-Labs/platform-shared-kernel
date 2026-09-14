# SharedKernel.Configuration

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![License: MIT](https://img.shields.io/badge/license-MIT-blue)
![Third-party dependencies: 0](https://img.shields.io/badge/third--party%20dependencies-0-brightgreen)
![Trimming: declared, not hidden](https://img.shields.io/badge/trimming-declared%2C%20not%20hidden-yellow)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

**Options-pattern registration that fails at startup, not at first use.**

A missing connection string should fail the deployment, not whichever request first needs it,
hours later. `AddValidatedOptions` binds a configuration section, validates it, and makes
`IHost.StartAsync()` throw when it is wrong:

- **Validation:** with Data Annotations, a custom `IValidateOptions<T>`, an
  `[OptionsValidator]`-generated validator, or several at once.
- **Section paths declared on the type:** `ISectionBoundOptions` puts the path on the options
  class, so no call site retypes it or passes the wrong one.
- **Opt-in strictness:** `OptionsStrictness` rejects a misspelled section path or a misspelled
  key instead of silently binding defaults.
- **Named options:** each named instance is bound and validated on its own.

## Contents

- [Install](#install)
- [At a glance](#at-a-glance)
- [Rules](#rules)
- [Usage](#usage)
  - [Data Annotations](#data-annotations)
  - [Let the type name its own section](#let-the-type-name-its-own-section)
  - [Catching typos: OptionsStrictness](#catching-typos-optionsstrictness)
  - [A rule Data Annotations cannot express](#a-rule-data-annotations-cannot-express)
  - [Validation with no reflection](#validation-with-no-reflection)
  - [Nested objects and collections](#nested-objects-and-collections)
  - [Named instances](#named-instances)
- [Which overload](#which-overload)
- [What fails, when, and as what](#what-fails-when-and-as-what)
- [Traps](#traps)
- [Trimming and AOT](#trimming-and-aot)
- [Compatibility and guarantees](#compatibility-and-guarantees)
- [Deliberately not included](#deliberately-not-included)

## Install

```shell
dotnet add package SharedKernel.Configuration
```

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Dependencies | `Microsoft.Extensions.Options.DataAnnotations`, `Microsoft.Extensions.Options.ConfigurationExtensions`, `Microsoft.Extensions.Configuration.Abstractions`, `Microsoft.Extensions.DependencyInjection.Abstractions`. No other SharedKernel package. |
| Namespaces | `SharedKernel.Configuration` (`ISectionBoundOptions`, `OptionsStrictness`) and `SharedKernel.Configuration.Extensions` (`AddValidatedOptions`) |

## At a glance

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

With this registration, each of these stops the host from starting:

| Configuration | Fails with |
| --- | --- |
| `ConnectionString` missing | `OptionsValidationException`: the `ConnectionString` field is required |
| `"MaxConnections": 5000` | `OptionsValidationException`: out of range |
| `"MaxConnections": "lots"` | `InvalidOperationException`: cannot convert `'lots'` |
| Section written as `"MyService:Databse"` | `OptionsValidationException`: section `'MyService:Database'` does not exist |
| Key written as `"MaxConections"` | `InvalidOperationException`: `'MaxConections'` not found on `DatabaseOptions` |

## Rules

| Rule | What enforces it |
| --- | --- |
| Register every options type through `AddValidatedOptions`, never `services.Configure<T>(section)` | Convention. `Configure<T>` binds without validating, so failures surface at first use |
| Declare the section path on the options type, not at the call site | `ISectionBoundOptions.SectionName`, a `static abstract` member: the compiler rejects a type that lacks it, and a null or blank value throws at registration |
| Never retype a section path as a literal in `GetSection("…")` | `SK0022` (`SharedKernel.Analyzers`) flags raw literals at magic-string call sites |
| Per-property rules go in Data Annotations; cross-property rules go in an `IValidateOptions<T>` | The `TValidator` overloads. An attribute cannot see a second property |
| A validator is one of *many*, never "the" validator | Registration uses `TryAddEnumerable`: the pipeline runs every registered validator |
| Consume named options through `IOptionsMonitor<T>` or `IOptionsSnapshot<T>` | `IOptions<T>` resolves only the default instance, silently, with defaults |
| Treat `SectionName` as a deployment contract | Changing it stops binding every deployed `appsettings.json` and environment variable that targets the old path |

## Usage

### Data Annotations

```csharp
builder.Services.AddValidatedOptions<DatabaseOptions>(
    builder.Configuration.GetSection("MyService:Database"));
```

Registers `IOptions<T>`, `IOptionsSnapshot<T>` and `IOptionsMonitor<T>`, and arms
`ValidateOnStart` so `IHost.StartAsync()` throws on bad configuration.

Calling it twice for the same type and name registers the Data Annotations validator once, so each
failure is reported once. The BCL's own `ValidateDataAnnotations()` duplicates both.

### Let the type name its own section

```csharp
public sealed class DatabaseOptions : ISectionBoundOptions
{
    public static string SectionName => "MyService:Database";

    [Required] public string ConnectionString { get; set; } = string.Empty;
}

builder.Services.AddValidatedOptions<DatabaseOptions>(builder.Configuration);
```

There is no section argument, so no call site can pass the wrong one. `SectionName` is read through
a generic type parameter, which compiles to a direct static call with no reflection.

Declare `SectionName` as a `static` property, not a `const` field: a field cannot satisfy a
`static abstract` property. It stays readable as `DatabaseOptions.SectionName`, so existing
`GetSection(DatabaseOptions.SectionName)` calls keep compiling.

A `SectionName` that is `null`, empty or whitespace throws `InvalidOperationException` naming the
type when `AddValidatedOptions` is called.

Passing an explicit `IConfigurationSection` still works for a type that implements the interface,
and wins overload resolution. Use it to point one options type at a different section.

### Catching typos: OptionsStrictness

By default, both of these mistakes start the host with the options at their defaults:

```jsonc
{
  "MyService": {
    "Databse": { "ConnectionString": "..." },   // misspelled section: nothing binds
    "Database": { "MaxConections": 50 }         // misspelled key: silently ignored
  }
}
```

If every property has a default and passes validation, nothing reports either one. Opt in per
options type:

```csharp
builder.Services.AddValidatedOptions<DatabaseOptions>(
    builder.Configuration,
    strictness: OptionsStrictness.RequireSection | OptionsStrictness.RejectUnknownKeys);
```

| Flag | Rejects | Fails as |
| --- | --- | --- |
| `RequireSection` | A section that does not exist. An empty object (`"Database": {}`) and `"Database": null` count as missing; a lone environment variable such as `MyService__Database__Port` counts as present. | `OptionsValidationException`, collected with the instance's other validation failures |
| `RejectUnknownKeys` | A key with no matching property, including inside nested objects. Matching is case-insensitive; keys under a dictionary property are always accepted. | `InvalidOperationException` from the binder, naming every unknown key |

**Why both are off by default.** `RequireSection` would refuse to start a service whose options are
fully defaulted and deliberately have no section in some environment. `RejectUnknownKeys` crashes
old pods during a rolling deployment if configuration for the next release, with a key the old code
does not know, goes live before every pod is upgraded. Turn each on where neither applies.

Both checks run every time the instance is validated, so they also apply after a configuration
reload.

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

// validateDataAnnotations: true keeps [Range] enforced alongside the custom rule.
builder.Services.AddValidatedOptions<PoolOptions, PoolOptionsValidator>(
    builder.Configuration.GetSection("MyService:Pool"),
    validateDataAnnotations: true);
```

### Validation with no reflection

```csharp
[OptionsValidator]
public sealed partial class DatabaseOptionsValidator : IValidateOptions<DatabaseOptions>
{
}

builder.Services.AddValidatedOptions<DatabaseOptions, DatabaseOptionsValidator>(
    builder.Configuration);
```

`[OptionsValidator]` ships in `Microsoft.Extensions.Options`, so no extra reference is needed. The
BCL source generator writes `Validate(...)` at compile time from the same attributes, so validation
does no reflection. Leave `validateDataAnnotations` at its `false` default here: the generated
validator already covers the attributes, and turning it on reports every failure twice.

### Nested objects and collections

Data Annotations on a nested object's properties, or on collection items, **are not validated by
default**. A `[Required]` inside a nested class never fires. Mark the property:

```csharp
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

Both attributes ship in `Microsoft.Extensions.Options` and are honoured by the Data Annotations
path and by an `[OptionsValidator]`-generated validator.

### Named instances

```csharp
builder.Services.AddValidatedOptions<ClientOptions>(
    builder.Configuration.GetSection("Clients:Primary"), name: "primary");
builder.Services.AddValidatedOptions<ClientOptions>(
    builder.Configuration.GetSection("Clients:Secondary"), name: "secondary");

public sealed class Caller(IOptionsMonitor<ClientOptions> options)
{
    private ClientOptions Primary => options.Get("primary");
}
```

Each instance is validated on its own. An invalid one fails startup and names itself in
`OptionsValidationException.OptionsName`; its valid siblings are unaffected. The `RequireSection`
check is also scoped to the name it was registered for.

## Which overload

| You have | Call |
| --- | --- |
| Attributes only, a section in hand | `AddValidatedOptions<T>(section, name?, strictness?)` |
| Attributes only, `T : ISectionBoundOptions` | `AddValidatedOptions<T>(configuration, name?, strictness?)` |
| A custom or generated validator, a section in hand | `AddValidatedOptions<T, TValidator>(section, validateDataAnnotations?, name?, strictness?)` |
| A custom or generated validator, `T : ISectionBoundOptions` | `AddValidatedOptions<T, TValidator>(configuration, validateDataAnnotations?, name?, strictness?)` |

All four return the same `IServiceCollection`. Pass the optional arguments by name.

## What fails, when, and as what

| When | Exception | Cause |
| --- | --- | --- |
| At the `AddValidatedOptions` call | `ArgumentNullException` | `services`, `section` or `configuration` is null |
| At the `AddValidatedOptions` call | `ArgumentOutOfRangeException` | `strictness` contains an undefined bit |
| At the `AddValidatedOptions` call | `InvalidOperationException` | `ISectionBoundOptions.SectionName` is null, empty or whitespace |
| At `IHost.StartAsync()` | `OptionsValidationException` | An attribute, a validator, or `RequireSection` rejected the values; all failures are in `Failures` |
| At `IHost.StartAsync()` | `InvalidOperationException` | Binding failed before validation: an unconvertible value, or an unknown key under `RejectUnknownKeys` |

## Traps

Each of these was measured, not inferred.

**`ValidateOnStart()` needs a real host.** With a bare `ServiceCollection` and
`BuildServiceProvider()`, such as a worker with no `IHost` or a unit test, nothing validates
eagerly. Resolving `IOptions<T>` succeeds, and the first read of `.Value` throws.

**A bad reload throws from `Reload()` itself.** Once anything has resolved `IOptionsMonitor<T>`,
its change callback re-validates eagerly, so `IConfigurationRoot.Reload()` throws
`AggregateException` wrapping `OptionsValidationException`. With `reloadOnChange: true` that
happens on the file watcher's thread, where nothing catches it. With no monitor resolved,
`Reload()` is silent and the failure waits for the next read.

**A custom validator is type-wide; Data Annotations are per name.** A `TValidator` is registered
once against `IValidateOptions<T>`, so it runs for **every** named instance of that type. To apply
it to one name, check the `name` argument and return `ValidateOptionsResult.Skip` for the others.

**An explicit null overwrites a property initializer.** A key that is present but null binds as the
type's default: `public string Name { get; set; } = string.Empty;` holds `null` despite its
non-nullable declaration, and an `int` initialized to `30` becomes `0`. An *absent* key leaves the
initializer intact. Validate rather than trusting the initializer.

**A property with a non-public setter is never bound**, and `RejectUnknownKeys` does not report its
key, because the key does match a property.

## Trimming and AOT

This package is **not** trim- or AOT-safe, and says so. Configuration binding is reflective: the
BCL's own `OptionsBuilder<T>.Bind` carries `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]`.
A generic library wrapper cannot avoid that, because the configuration-binding source generator
intercepts `Bind` calls in the *calling* assembly and cannot specialize one that lives in a library.

So every overload declares both attributes instead of suppressing them, and every `TOptions` carries
`[DynamicallyAccessedMembers]` so a trimmer keeps the properties and parameterless constructor the
binder needs. The package itself builds with zero IL warnings under `EnableTrimAnalyzer`,
`EnableAotAnalyzer` and `EnableSingleFileAnalyzer`. The remaining risk sits with the caller: members
of complex types nested inside `TOptions` can be trimmed. For a trimmed or native-AOT publish, keep
such options flat or bind them by hand in the application, where the source generator can see the
concrete type.

## Compatibility and guarantees

- **Public API is tracked** with `Microsoft.CodeAnalysis.PublicApiAnalyzers`. Any addition, removal or
  signature change fails the build until it is recorded.
- **Every public member is documented**, including every exception it can throw. The XML
  documentation ships in the package.
- **Registration is idempotent.** Repeating a call for the same type, name and section never
  duplicates a failure message.
- **Nothing is on by default that could reject configuration a service previously accepted.** Both
  strictness checks are opt-in.

## Deliberately not included

- **No return of `OptionsBuilder<T>`.** Every SharedKernel registration method returns
  `IServiceCollection`. For `.PostConfigure(...)` or a lambda `.Validate(...)`, also call
  `services.AddOptions<T>(name)`: it returns a builder for the same named instance, and what you add
  there composes with the registration.
- **No `IConfiguration` wrapper, provider or section-name builder.** This package adds the validation
  guarantee and nothing else.
- **No secrets provider.** Key Vault as a configuration source is
  `SharedKernel.ServiceDefaults.Configuration.KeyVault`.
- **No FluentValidation adapter.** `IValidateOptions<T>` is the seam, and an adapter is one short
  class. For IBAN, PAN and national-ID formats, see `SharedKernel.Validation`.
- **No `Result<T>`.** Options validation is a startup-time fail-fast concern, so it throws. That is
  also why this package does not depend on `SharedKernel.Primitives`.

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel).
