# SharedKernel.Localization

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue)](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel/blob/main/LICENSE)
![Dependencies: first-party only](https://img.shields.io/badge/third--party%20dependencies-none-success)
![Public API: tracked](https://img.shields.io/badge/public%20API-tracked-informational)

> **Translated error messages with typed, named arguments. Define a message once, and the compiler checks every place
> that uses it. Translations come from JSON or `.resx` files and are validated at startup. A missing or broken
> translation falls back to the original text, so a user never sees a blank or a raw `{placeholder}`.**

An error such as `Error.NotFound("order.not_found", $"Order {id} was not found.")` is easy to write and impossible to
translate. The value is already baked into an English string, so a Turkish caller gets English, and a translation
keyed by `order.not_found` has no way to say *which* order. This package keeps the value next to the message. The
error's `Message` still reads naturally in logs, and the HTTP boundary can render
`"3f2a… numaralı sipariş bulunamadı."` for a Turkish caller from the same error.

| You get | So that |
| --- | --- |
| `LocalizedMessage.Define<T1…T4>(code, text, argumentNames…)` | Every call site is checked for argument count and types, and a definition whose names don't match its text fails when the type loads |
| Named placeholders with .NET formats (`{amount:N2}`, `{date:d}`) | Translators can reorder values, and numbers and dates are formatted for the caller's culture (`1,500.50` or `1.500,50`) |
| `Error.MessageArguments` filled by `ToError(...)` | `14.Presentation` translates the `detail` of a ProblemDetails response (and a SignalR or gRPC error message) *with* the values, with no extra code in your handler. Outside Development a server error (500, 503, 504) shows a generic sentence instead, translatable under `unexpected.exception`, `unavailable.default` and `timeout.default` |
| JSON translation files, from disk or embedded in an assembly | Translations live next to the service or ship inside a library, one file per culture |
| Validation when the catalog is built | A malformed template, a duplicate key or a bad file name stops startup, instead of reaching a user |
| Fallback to the original message, always | A missing translation, a translation that needs a value the error doesn't have, or a bad format never produces a blank or a raw `{placeholder}` |
| Culture fallback (`tr-TR` → `tr` → invariant) | Translate once per language, and every regional variant finds it |
| `.resx` support through `IStringLocalizerFactory` | A service that already keeps resources in `.resx` files uses them behind the same interface |

## Contents

- [Install](#install)
- [Quick start](#quick-start)
- [Translation files](#translation-files)
- [Message syntax](#message-syntax)
- [How it works](#how-it-works)
- [Recipes](#recipes)
- [Reference](#reference)
- [Pitfalls](#pitfalls)
- [Design decisions](#design-decisions)
- [AI quick reference](#ai-quick-reference)
- [Compatibility and guarantees](#compatibility-and-guarantees)

## Install

```shell
dotnet add package SharedKernel.Localization
```

| Requirement | Value |
| --- | --- |
| Target framework | `net10.0` |
| Depends on | `SharedKernel.Primitives` (`Error`, `ErrorType`), `Microsoft.Extensions.Localization.Abstractions` |
| Namespace | `SharedKernel.Localization` |
| Globalization | ICU cultures, the .NET default. An app published with `InvariantGlobalization` enabled has no cultures to translate into |

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

The error's `Message` is `"Order 3f2a… was not found."`, the default text filled with the invariant culture, so logs
stay readable. Its `MessageArguments` holds `orderId`.

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

An ASP.NET Core project (`Microsoft.NET.Sdk.Web`) already copies `.json` files to the output directory. Any other
project needs one line in its `.csproj`:

```xml
<ItemGroup>
  <Content Include="Localization\*.json" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

In a Web SDK project that line fails the build with a duplicate `Content` item, so leave it out there.

**4. Register the catalog.** It is built and validated during this call:

```csharp
builder.Services.AddLocalizationCatalog(catalog =>
    catalog.AddJsonDirectory(Path.Combine(AppContext.BaseDirectory, "Localization")));
```

**5. That's all the HTTP side needs.** `SharedKernel.Presentation.WebApi` resolves the catalog when it turns an error
into a ProblemDetails response, and translates the `detail` into the culture ASP.NET Core's request localization chose
for the request (else `CultureInfo.CurrentUICulture`); see [recipe 1](#1-choose-the-culture-for-each-request).
A caller with `Accept-Language: tr-TR` gets, abridged:

```json
{ "status": 404, "detail": "3f2a… numaralı sipariş bulunamadı.", "errorCode": "order.not_found" }
```

A caller in any language without a translation gets the original `"Order 3f2a… was not found."`.

## Translation files

One JSON object per culture. Keys are message codes, and values are templates. Nested objects are joined with dots,
so these two files are equivalent:

```json
{ "order.not_found": "{orderId} numaralı sipariş bulunamadı." }
```

```json
{ "order": { "not_found": "{orderId} numaralı sipariş bulunamadı." } }
```

Comments and trailing commas are allowed.

| Source | Culture comes from | Use for |
| --- | --- | --- |
| `AddJsonDirectory(dir)` | Each file name: `tr.json`, `de-DE.json` | A service's own translations |
| `AddJsonFile(path)` / `AddJsonFile(path, culture)` | The file name, or the argument | A single file |
| `AddEmbeddedJson(assembly, prefix)` | The resource name after the prefix | Translations shipped inside a library |
| `AddJson(stream, culture)` | The argument | Any other source, such as a database export or a blob |
| `Add(code, culture, template)` | The argument | A few messages, or tests |

**Later sources win.** When two sources define the same code for the same culture, the one added last is used. That
lets you add a library's translations first and override single messages after them.

**Everything is checked when the catalog is built.** The following all throw, naming the file and the code, so the
service fails at startup instead of on a user's error:

- invalid JSON, or a value that is neither a string nor an object;
- a code defined twice in one file, including `"a.b"` next to `{ "a": { "b": … } }`;
- an empty translation;
- a template with a syntax error;
- a file name that is not a culture name;
- a directory or embedded prefix with no files.

## Message syntax

| Write | Means |
| --- | --- |
| `{orderId}` | The value named `orderId`, formatted with the caller's culture |
| `{total:N2}` | The value formatted with the .NET format `N2`: `1,500.50` in English, `1.500,50` in Turkish |
| `{date:d}` / `{date:yyyy-MM-dd}` | A date in the culture's short format, or a fixed pattern |
| `{{` and `}}` | A literal `{` or `}` |
| `{0}` | **Rejected.** Placeholders are named, so translators can reorder them and a name says what the value is |

A name starts with a letter or underscore, continues with letters, digits or underscores, and is case-sensitive. A
`null` value renders as an empty string. Values that implement `IFormattable` (numbers, dates, `Guid`, enums) use the
format and culture; anything else uses `ToString()`.

A translation doesn't have to use every value. It can drop one ("Sipariş bulunamadı."), but it can't invent one: a
translation that mentions a name the error doesn't carry is treated as missing, and the original message is shown.

## How it works

### Looking up a translation

`ILocalizationCatalog.TryGetTemplate(code, culture)` finds the template for a code. `InMemoryLocalizationCatalog`,
which `AddLocalizationCatalog` builds, tries the requested culture, then each parent, then the invariant culture.
`tr-TR` tries `tr-TR`, `tr` and invariant, the same order .NET resource files use. So translate into neutral
cultures (`tr.json`, `de.json`), and add a regional file only for text that really differs by region.

### Falling back

`catalog.Localize(error, culture)` returns the translation filled with `error.MessageArguments`. It returns
`error.Message` instead when:

- there is no translation for the code in that culture or its parents;
- the translation uses a placeholder the error has no value for, for example an error created with plain
  `Error.NotFound(...)` rather than from a definition;
- a format does not suit its value.

It never throws for any of these, because it runs on the error path. The result is never blank: a blank translation
cannot be stored, and `Error.Message` is whatever the code that raised the error wrote.

### Arguments travel on the error, inside one process

`ToError` stores the values in `Error.MessageArguments`, a property of `SharedKernel.Primitives`' `Error`. It is not
part of equality and not serialized to JSON: the values are ordinary .NET objects that would lose their types.
Nothing is lost when an error crosses a process boundary, because `Message` already contains the values in the default
text. The translation happens in the process that turns the error into a response.

### Definitions are checked when they load

`Define` throws `ArgumentException` when:

- the argument names don't match the placeholders in the default text exactly;
- a name repeats;
- a format doesn't suit its argument's type, for example `{total:Q}` for a `decimal`, found by formatting the type's
  default value.

The definitions are `static readonly` fields, so the mistake surfaces from the type initializer the first time
anything touches the class, including any test that uses one of its messages.

## Recipes

### 1. Choose the culture for each request

ProblemDetails translation reads the culture ASP.NET Core's request localization chose for the request. It chooses
**only a culture in its supported list**; anything else stays at the default, so pass the catalog's cultures. With
`SharedKernel.Presentation.WebApi`, add it in `UseSharedKernelWebApi()`'s `BeforeAuthorization` hook, so the 401, 403
and 429 answers of authorization and rate limiting are translated too:

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

// Without SharedKernel.Presentation.WebApi: app.UseRequestLocalization(options => ...) with the same options.
```

`Accept-Language: tr-TR` then resolves to `tr`, because `FallBackToParentUICultures` is on by default.
`SharedKernel.ServiceDefaults.Localization`'s `AddSharedKernelLocalization()` adds user-preference and tenant-default
strategies in front of the header. Its supported cultures still have to be configured the same way.

### 2. Translate outside HTTP: emails, notifications, exports

`Format` returns the message in any culture, or the default text when there is no translation:

```csharp
public sealed class OrderEmails(ILocalizationCatalog catalog)
{
    public string OverLimitLine(CultureInfo customerCulture, decimal total, decimal limit) =>
        OrderMessages.OverLimit.Format(catalog, customerCulture, total, limit);
}
```

To translate an `Error` you already have, use `catalog.Localize(error, culture)`.

### 3. Ship translations inside a library

Embed the files so they don't depend on being copied next to the application:

```xml
<ItemGroup>
  <EmbeddedResource Include="Localization\*.json" WithCulture="false" />
</ItemGroup>
```

`WithCulture="false"` stops MSBuild from treating `tr.json` as a satellite resource. The resource name is the
assembly's root namespace, the folder and the file name. Let the application add the library's translations first
and its own after them, so it can override any message:

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

Resource values use the same named placeholders (`{orderId}`), not `string.Format`'s `{0}`. Resource files are read
when a message is looked up, not validated at startup. An empty or malformed value counts as missing and falls back
to the original message.

### 5. Report every invalid field in the caller's language

Each child error of `Error.Validation(errors)` keeps its own arguments, and `14.Presentation` translates each one
separately:

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

### Types

| Type | Purpose |
| --- | --- |
| `LocalizedMessage`, `LocalizedMessage<T1>` … `<T1, T2, T3, T4>` | A message definition. `Define(...)` creates one; `ToError(type, args…)` builds an `Error`; `Format(catalog, culture, args…)` returns text. `Code`, `DefaultTemplate` |
| `ILocalizationCatalog` | `TryGetTemplate(code, culture, out template)`. Implement it to read translations from somewhere else |
| `InMemoryLocalizationCatalog` | The immutable catalog built from JSON and code. `Count`, `Cultures` |
| `LocalizationCatalogBuilder` | `Add`, `AddJson`, `AddJsonFile`, `AddJsonDirectory`, `AddEmbeddedJson`, `Build` |
| `StringLocalizerLocalizationCatalog` | A catalog over `.resx` files through `IStringLocalizerFactory` |
| `MessageTemplate` | A parsed template. `Parse`, `TryParse`, `Format`, `TryFormat`, `Text`, `PlaceholderNames` |
| `LocalizationCatalogExtensions` | `Localize(error, culture)`, `TryFormat(code, culture, arguments, out message)`, `TryGetString(code, culture, out message)` |
| `LocalizationServiceCollectionExtensions` | `AddLocalizationCatalog(configure)`, `AddStringLocalizerCatalog<TResource>()` |

### Exceptions

| Thrown by | Exception | When |
| --- | --- | --- |
| `LocalizedMessage.Define` | `ArgumentException` | Names don't match the placeholders, a name repeats or is blank, a format doesn't suit its type, or the text is invalid |
| `ToError` | `ArgumentOutOfRangeException` | The type is `ErrorType.None` or undefined |
| `LocalizationCatalogBuilder` | `FormatException` | Invalid JSON or template, a duplicate code in one file, an empty translation |
| `LocalizationCatalogBuilder` | `ArgumentException`, `FileNotFoundException`, `DirectoryNotFoundException` | A bad culture file name, no matching files, or a missing file or directory |
| `AddLocalizationCatalog`, `AddStringLocalizerCatalog` | `InvalidOperationException` | A catalog is already registered |
| `MessageTemplate.Format` | `ArgumentException`, `FormatException` | A missing value, or a format that doesn't suit its value. Use `TryFormat` on an error path |

`Localize`, `TryFormat`, `TryGetString` and every `TryGetTemplate` never throw for a missing or broken translation.

## Pitfalls

- **Supported cultures not configured.** ASP.NET Core ignores a requested culture that isn't in
  `SupportedUICultures`, and every response stays in the default language. See [recipe 1](#1-choose-the-culture-for-each-request).
- **Interpolating the value into a plain error.** `Error.NotFound("order.not_found", $"Order {id} was not found.")`
  still works, but the translation can't include the id, and a translation that mentions `{orderId}` falls back to the
  English message. Use a definition.
- **Putting values in the code.** `$"order.{id}.not_found"` can't be translated or grouped in dashboards. The code is
  fixed; values go in arguments.
- **Positional placeholders.** `{0}` is rejected everywhere, including in `.resx` values, where it makes the value
  count as missing.
- **Registering two catalogs.** The second call throws. Combine sources in one builder instead.
- **`InvariantGlobalization` enabled.** Every culture then behaves like the invariant one, and loading `tr.json` fails
  because `tr` is not a known culture.
- **Expecting arguments across services.** `MessageArguments` is not serialized. A service that receives an error from
  another service gets the already-formatted message.

## Design decisions

- **Explicit argument names in `Define`.** Taking names from the order of placeholders would be shorter, but rewording
  `"{account}: {amount}"` would silently swap two values of the same type. The names cost one string each and make
  that impossible.
- **Arguments on `Error`, not formatted at the call site.** Formatting at the call site needs the caller's culture
  inside every handler, and it can't cover errors raised in the domain layer. Carrying the values lets one place, the
  HTTP boundary, translate every error.
- **`MessageArguments` outside equality and JSON.** `Error` compares by code, message and type. The arguments are
  already in the message, and serializing arbitrary objects would not survive the trip with their types.
- **Named placeholders only.** A translator can reorder named values, and a name documents what the value is.
- **Immutable catalog built up front.** The earlier design let translations be added until the catalog was sealed.
  Building once into a frozen dictionary removes the thread-safety question, and it moves every validation to
  startup.
- **One catalog per application.** A second registration used to be ignored silently. It now throws, because its
  translations would never appear.
- **No plural rules (yet).** Error messages rarely need them. The template syntax leaves room to add ICU-style
  `{count, plural, …}` later without breaking existing templates.

## AI quick reference

```text
DEFINE      static readonly LocalizedMessage<T1..T4> X = LocalizedMessage.Define<T1..>(code, "Text {name:fmt}", "name", ...);
            LocalizedMessage.Define(code, text) for no arguments. Names must equal the placeholders exactly.
ERROR       X.ToError(ErrorType.NotFound, arg1, ...) -> Error { Code, Message = default text (invariant), MessageArguments }
TEXT        X.Format(ILocalizationCatalog? catalog, CultureInfo culture, arg1, ...) -> translation or default text
TRANSLATE   catalog.Localize(error, culture) -> translation filled with error.MessageArguments, else error.Message. Never throws.
REGISTER    services.AddLocalizationCatalog(c => c.AddJsonDirectory(dir) | AddJsonFile | AddEmbeddedJson(asm, "Ns.Folder.") | AddJson(stream, culture) | Add(code, culture, text));
            or services.AddLocalization(...).AddStringLocalizerCatalog<TResource>(); exactly one catalog, a second throws.
JSON        One file per culture (tr.json, de-DE.json). {"order":{"not_found":"{orderId} ..."}} == {"order.not_found": "..."}.
            Validated at registration: bad JSON/template/duplicate/empty/culture name -> exception at startup.
SYNTAX      {name} {name:N2} {date:d} {{ }}. Positional {0} rejected. Names case-sensitive.
FALLBACK    tr-TR -> tr -> invariant. Missing translation, missing argument or bad format -> original message.
HTTP        SharedKernel.Presentation.WebApi translates ProblemDetails.detail (and SignalR/gRPC error messages) with the
            request culture (IRequestCultureFeature, else CultureInfo.CurrentUICulture). Configure UseRequestLocalization
            with SupportedUICultures = catalog.Cultures, or nothing is translated; with UseSharedKernelWebApi() put it in
            the BeforeAuthorization hook. Outside Development 500/503/504 show a generic sentence (codes
            unexpected.exception, unavailable.default, timeout.default).
FORBIDDEN   Interpolating values into Error codes or messages you want translated; {0}; two catalogs; InvariantGlobalization.
```

## Compatibility and guarantees

- **Public API is tracked** with `Microsoft.CodeAnalysis.PublicApiAnalyzers`, and every public member is documented.
- **Never blank, never raw.** Translation APIs return the translation or the original message, never an empty string
  or an unfilled `{placeholder}`, and never throw for a missing or broken translation.
- **Validated at startup.** JSON translations and message definitions are checked when they load.
- **Thread-safe.** Catalogs, templates and definitions are immutable after construction.
- **No third-party dependencies.** Only `SharedKernel.Primitives` and Microsoft's
  `Microsoft.Extensions.Localization.Abstractions`; JSON is read with `System.Text.Json` without reflection.

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel).
