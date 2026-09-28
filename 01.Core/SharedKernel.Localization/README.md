# SharedKernel.Localization

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Tier: Foundation](https://img.shields.io/badge/tier-Foundation-2ea44f)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

> **Translated error messages with typed, named arguments. Define a message once and the compiler checks every use;
> translations come from JSON or `.resx` files, validated at startup, and a missing translation falls back to the
> original text — never a blank or a raw `{placeholder}`.**

`Error.NotFound("order.not_found", $"Order {id} was not found.")` is easy to write and impossible to translate: the value
is baked into an English string. This package keeps the value next to the message, so `Message` still reads naturally in
logs and the HTTP boundary can render `"3f2a… numaralı sipariş bulunamadı."` for a Turkish caller from the same error.

| You get | So that |
| --- | --- |
| `LocalizedMessage.Define<T1…T4>(code, text, argumentNames…)` | Every call site is checked for argument count and types; a definition whose names don't match its text fails when the type loads |
| Named placeholders with .NET formats (`{amount:N2}`, `{date:d}`) | Translators can reorder values, and numbers and dates format for the caller's culture |
| `Error.MessageArguments` filled by `ToError(...)` | `SharedKernel.Presentation` translates ProblemDetails `detail` (and SignalR/gRPC error messages) with the values, with no handler code |
| JSON translation files, from disk or embedded in an assembly | Translations live next to the service or ship inside a library, one file per culture |
| Validation when the catalog is built | A malformed template, duplicate key or bad file name stops startup instead of reaching a user |
| Culture fallback (`tr-TR` → `tr` → invariant) and `.resx` support | Translate once per language; services on `.resx` use the same interface |

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
<PackageReference Include="SharedKernel.Localization" />
```

The version comes from your central `SharedKernelVersion` property — every SharedKernel package ships at the same
version. See [Using the packages](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel#using-the-packages).

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Tier | Foundation — reference it from **any** project (messages are often defined in Domain or Application) |
| Depends on | `SharedKernel.Primitives`, `Microsoft.Extensions.Localization.Abstractions` |
| Namespaces | `SharedKernel.Localization` |
| Globalization | ICU cultures (the .NET default); an app published with `InvariantGlobalization` has no cultures to translate into |

## Quick start

**1. Define each message once**, next to the code that raises it:

```csharp
using SharedKernel.Localization;

public static class OrderMessages
{
    public static readonly LocalizedMessage<Guid> NotFound = LocalizedMessage.Define<Guid>(
        "order.not_found", "Order {orderId} was not found.", "orderId");

    public static readonly LocalizedMessage<decimal, decimal> OverLimit = LocalizedMessage.Define<decimal, decimal>(
        "order.over_limit", "The order total {total:N2} exceeds your limit of {limit:N2}.", "total", "limit");

    public static readonly LocalizedMessage CannotCancel = LocalizedMessage.Define(
        "order.cannot_cancel", "A shipped order cannot be cancelled.");
}
```

**2. Build errors from the definitions.** The compiler checks the arguments:

```csharp
using SharedKernel.Primitives.Errors;
using SharedKernel.Primitives.Results;

public sealed class OrderService
{
    public Result<Order> Find(Guid orderId, IReadOnlyDictionary<Guid, Order> orders) =>
        orders.TryGetValue(orderId, out Order? order)
            ? order
            : OrderMessages.NotFound.ToError(ErrorType.NotFound, orderId);
}
```

The error's `Message` is `"Order 3f2a… was not found."` (the default text, invariant culture); `MessageArguments` holds `orderId`.

**3. Add a JSON file per language**, for example `Localization/tr.json`:

```json
{
  "order": {
    "not_found": "{orderId} numaralı sipariş bulunamadı.",
    "over_limit": "Sipariş tutarı {total:N2}, {limit:N2} olan limitinizi aşıyor.",
    "cannot_cancel": "Kargolanan sipariş iptal edilemez."
  }
}
```

A Web SDK project already copies `.json` files to the output; any other project needs
`<Content Include="Localization\*.json" CopyToOutputDirectory="PreserveNewest" />` (in a Web SDK project that line fails
the build with a duplicate `Content` item).

**4. Register the catalog.** It is built and validated during this call:

```csharp
builder.Services.AddLocalizationCatalog(catalog =>
    catalog.AddJsonDirectory(Path.Combine(AppContext.BaseDirectory, "Localization")));
```

`SharedKernel.Presentation.WebApi` resolves the catalog when it turns an error into ProblemDetails and translates `detail`
into the request's culture (recipe 1). `Accept-Language: tr-TR` gets
`{ "status": 404, "detail": "3f2a… numaralı sipariş bulunamadı.", "errorCode": "order.not_found" }`; a language without a
translation gets the original text. The package has no configuration section.

## How it works

### Translation files

One JSON object per culture; keys are message codes, values are templates. Nested objects are joined with dots
(`{"order":{"not_found":"…"}}` equals `{"order.not_found":"…"}`); comments and trailing commas are allowed. **Later
sources win**, so add a library's translations first and override single messages after them.

Everything is checked when the catalog is built, and throws naming the file and code: invalid JSON or a value that is
neither string nor object; a code defined twice in one file (including `"a.b"` next to `{ "a": { "b": … } }`); an empty
translation; a template syntax error; a file name that is not a culture; a directory or embedded prefix with no files.

### Message syntax

| Write | Means |
| --- | --- |
| `{orderId}` | The value named `orderId`, formatted with the caller's culture |
| `{total:N2}` | The .NET format `N2`: `1,500.50` in English, `1.500,50` in Turkish |
| `{date:d}` / `{date:yyyy-MM-dd}` | A culture's short date, or a fixed pattern |
| `{{` and `}}` | A literal `{` or `}` |
| `{0}` | **Rejected.** Placeholders are named |

Names start with a letter or underscore and are case-sensitive. `null` renders as empty; `IFormattable` values use the
format and culture, anything else `ToString()`. A translation may drop a value but not invent one: a translation that
mentions a name the error doesn't carry is treated as missing.

### Lookup and fallback

`InMemoryLocalizationCatalog` tries the requested culture, then each parent, then invariant — the same order `.resx`
files use, so translate into neutral cultures (`tr.json`) and add regional files only where text really differs.
`catalog.Localize(error, culture)` returns the translation filled with `error.MessageArguments`, or `error.Message` when
there is no translation, a placeholder has no value (for example an error from plain `Error.NotFound(...)`), or a format
does not suit its value. It never throws, and never returns blank.

### Arguments stay in one process

`MessageArguments` is not part of `Error` equality and not serialized — the values are ordinary .NET objects. Nothing is
lost across a process boundary, because `Message` already contains the values; translation happens in the process that
turns the error into a response.

### Definitions are checked when they load

`Define` throws `ArgumentException` when the names don't match the placeholders exactly, a name repeats, or a format
doesn't suit its argument's type (found by formatting the type's default value). The definitions are `static readonly`
fields, so the mistake surfaces from the type initializer the first time anything touches the class.

## Recipes

### 1. Choose the culture for each request

ASP.NET Core's request localization chooses **only a culture in its supported list**, so pass the catalog's cultures.
With `SharedKernel.Presentation.WebApi`, add it in the `BeforeAuthorization` hook so 401, 403 and 429 answers are
translated too:

```csharp
using Microsoft.AspNetCore.Builder;
using SharedKernel.Localization;
using SharedKernel.Presentation.WebApi;

var app = builder.Build();

string[] cultures = [.. app.Services.GetRequiredService<InMemoryLocalizationCatalog>().Cultures
    .Select(culture => culture.Name)
    .Prepend("en")
    .Distinct()];

app.UseSharedKernelWebApi(pipeline => pipeline.BeforeAuthorization(web => web.UseRequestLocalization(options => options
    .SetDefaultCulture("en")
    .AddSupportedCultures(cultures)
    .AddSupportedUICultures(cultures))));
```

`Accept-Language: tr-TR` resolves to `tr` (`FallBackToParentUICultures` is on by default).
`SharedKernel.ServiceDefaults.Localization`'s `AddSharedKernelLocalization()` adds user-preference and tenant-default
strategies in front of the header; its supported cultures are configured the same way. Outside Development, server
errors (500, 503, 504) show a generic sentence, translatable under `unexpected.exception`, `unavailable.default` and
`timeout.default`.

### 2. Translate outside HTTP: emails, notifications, exports

```csharp
public sealed class OrderEmails(ILocalizationCatalog catalog)
{
    public string OverLimitLine(CultureInfo customerCulture, decimal total, decimal limit) =>
        OrderMessages.OverLimit.Format(catalog, customerCulture, total, limit);
}
```

To translate an `Error` you already have, use `catalog.Localize(error, culture)`.

### 3. Ship translations inside a library

```xml
<ItemGroup>
  <EmbeddedResource Include="Localization\*.json" WithCulture="false" />
</ItemGroup>
```

`WithCulture="false"` stops MSBuild treating `tr.json` as a satellite resource. The resource name is the root namespace,
folder and file name. The application adds the library's translations first and its own after:

```csharp
builder.Services.AddLocalizationCatalog(catalog => catalog
    .AddEmbeddedJson(typeof(OrderMessages).Assembly, "Orders.Contracts.Localization.")
    .AddJsonDirectory(Path.Combine(AppContext.BaseDirectory, "Localization")));
```

### 4. Keep using `.resx` files

```csharp
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
builder.Services.AddStringLocalizerCatalog<ErrorMessages>();   // ErrorMessages.resx, ErrorMessages.tr.resx, …

public sealed class ErrorMessages;
```

Resource values use the same named placeholders. They are read at lookup, not validated at startup; an empty or
malformed value counts as missing and falls back to the original message.

### 5. Report every invalid field in the caller's language

Each child of `Error.Validation(errors)` keeps its own arguments and is translated separately:

```csharp
public static class FieldMessages
{
    public static readonly LocalizedMessage<string> Required = LocalizedMessage.Define<string>(
        "field.required", "{field} is required.", "field");
}

Error error = Error.Validation(
[
    FieldMessages.Required.ToError(ErrorType.Validation, "Name"),
    FieldMessages.Required.ToError(ErrorType.Validation, "Email"),
]);
// tr.json: { "field.required": "{field} alanı zorunludur." }
// errors: { "field.required": ["Name alanı zorunludur.", "Email alanı zorunludur."] }
```

## Reference

### Registration

| Method | Registers |
| --- | --- |
| `AddLocalizationCatalog(Action<LocalizationCatalogBuilder>)` | The built `InMemoryLocalizationCatalog` as itself and as `ILocalizationCatalog` (singleton); a second catalog throws |
| `AddStringLocalizerCatalog<TResource>()` | `StringLocalizerLocalizationCatalog` over `IStringLocalizerFactory` (call `AddLocalization()` first) |

`LocalizationCatalogBuilder` sources: `AddJsonDirectory(dir)` (culture from each file name), `AddJsonFile(path)` /
`AddJsonFile(path, culture)`, `AddEmbeddedJson(assembly, prefix)`, `AddJson(stream, culture)`, `Add(code, culture, template)`, `Build()`.

### Types

| Type | Purpose |
| --- | --- |
| `LocalizedMessage`, `LocalizedMessage<T1>` … `<T1, T2, T3, T4>` | `Define(...)`; `ToError(type, args…)`; `Format(catalog, culture, args…)`; `Code`, `DefaultTemplate` |
| `ILocalizationCatalog` | `TryGetTemplate(code, culture, out template)`; implement it to read translations from elsewhere |
| `InMemoryLocalizationCatalog` | The immutable catalog built from JSON and code; `Count`, `Cultures` |
| `MessageTemplate` | `Parse`, `TryParse`, `Format`, `TryFormat`, `Text`, `PlaceholderNames` |
| `LocalizationCatalogExtensions` | `Localize(error, culture)`, `TryFormat(code, culture, arguments, out message)`, `TryGetString(code, culture, out message)` |

### Exceptions

| Thrown by | Exception | When |
| --- | --- | --- |
| `LocalizedMessage.Define` | `ArgumentException` | Names don't match placeholders, a name repeats or is blank, a format doesn't suit its type, or the text is invalid |
| `ToError` | `ArgumentOutOfRangeException` | The type is `ErrorType.None` or undefined |
| `LocalizationCatalogBuilder` | `FormatException` | Invalid JSON or template, a duplicate code in one file, an empty translation |
| `LocalizationCatalogBuilder` | `ArgumentException`, `FileNotFoundException`, `DirectoryNotFoundException` | A bad culture file name, no matching files, a missing file or directory |
| `AddLocalizationCatalog`, `AddStringLocalizerCatalog` | `InvalidOperationException` | A catalog is already registered |
| `MessageTemplate.Format` | `ArgumentException`, `FormatException` | A missing value or an unsuitable format — use `TryFormat` on an error path |

`Localize`, `TryFormat`, `TryGetString` and every `TryGetTemplate` never throw for a missing or broken translation.

### Logging

The package does not log.

## Testing

Build a catalog in the test with `new LocalizationCatalogBuilder().Add(code, culture, template).Build()` and assert on
`catalog.Localize(error, culture)`. To run code under a given `CurrentCulture`/`CurrentUICulture`, use `CultureScope`
from [`SharedKernel.Testing`](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/16.Testing/SharedKernel.Testing/README.md)
(`SharedKernel.Testing.Localization`): `using var scope = new CultureScope("tr-TR");`.

## Pitfalls

| Don't | Do | Why |
| --- | --- | --- |
| Leave supported cultures unconfigured | Pass the catalog's `Cultures` to `UseRequestLocalization` (recipe 1) | ASP.NET Core ignores a culture not in `SupportedUICultures`, so nothing is translated |
| Interpolate the value into a plain error | Use a definition and `ToError` | The translation cannot include the value, and one mentioning `{orderId}` falls back to English |
| Put values in the code (`$"order.{id}.not_found"`) | A fixed code; values in arguments | It cannot be translated or grouped in dashboards |
| Use positional placeholders (`{0}`) | Named placeholders | Rejected everywhere; in `.resx` the value counts as missing |
| Register two catalogs | Combine sources in one builder | The second call throws |
| Publish with `InvariantGlobalization` | Keep ICU enabled | Loading `tr.json` fails because `tr` is not a known culture |
| Expect arguments across services | Translate where the error becomes a response | `MessageArguments` is not serialized |

## Design decisions

**Why explicit argument names in `Define`?** Taking names from placeholder order would be shorter, but rewording
`"{account}: {amount}"` would silently swap two values of the same type.

**Why carry arguments on `Error` instead of formatting at the call site?** Formatting at the call site needs the caller's
culture in every handler and cannot cover errors raised in the domain; carrying values lets one place translate every error.

**Why keep `MessageArguments` out of equality and JSON?** The values are already in the message, and arbitrary objects
would not survive serialization with their types.

**Why an immutable catalog built up front?** A frozen dictionary removes the thread-safety question and moves every
validation to startup. One catalog per application — a second registration throws, because its translations would never appear.

**Why no plural rules?** Error messages rarely need them; the syntax leaves room for ICU-style `{count, plural, …}` later.

---

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) ·
[Core domain](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/01.Core/README.md) ·
[MIT license](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
