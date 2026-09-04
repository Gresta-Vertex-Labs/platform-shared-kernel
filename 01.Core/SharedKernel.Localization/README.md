# SharedKernel.Localization

A culture-keyed message catalog contract for the Platform.SharedKernel ecosystem. Depends on `SharedKernel.Primitives` and the first-party `Microsoft.Extensions.Localization.Abstractions` NuGet package only — never a bespoke `.resx` pipeline.

## Included

- **`ILocalizationCatalog`** — `TryGetString(string code, CultureInfo culture, out string? value) → bool`, keyed on the same `code` string every `Error` factory in `SharedKernel.Primitives` already requires.
- **`InMemoryLocalizationCatalog`** — the dictionary-backed, zero-config default. Seeded via a chained `AddTranslation(code, culture, value)` builder.
- **`StringLocalizerLocalizationCatalog`** — wraps a caller-supplied `IStringLocalizerFactory`, letting a service with full `.resx` tooling compose behind the same seam.
- **`LocalizationServiceCollectionExtensions`** — `AddInMemoryLocalizationCatalog(...)` / `AddStringLocalizerCatalog<TResource>()`.

## The fallback contract — never blank, never this package's job to apply it

**AN UNTRANSLATED ERROR MESSAGE FALLS BACK TO THE ORIGINAL THROW-SITE STRING, IT IS NEVER BLANK.** `ILocalizationCatalog.TryGetString` only ever returns `false`/`null` for an unregistered or untranslated `(code, culture)` pair — it never throws for that outcome, and it never returns an empty string. Applying the throw-site-message fallback when a lookup misses is entirely the caller's responsibility. In practice that caller is `14.Presentation`'s `Error.ToProblemDetails()` (P-484) — this package ships no HTTP integration of its own, and `01.Core.Primitives.Error` itself is completely unchanged by this package's existence.

```csharp
using SharedKernel.Localization;
using System.Globalization;

ILocalizationCatalog catalog = new InMemoryLocalizationCatalog()
    .AddTranslation("user.not_found", CultureInfo.GetCultureInfo("tr"), "Kullanıcı bulunamadı.");

string throwSiteMessage = "User not found.";
string resolvedMessage = catalog.TryGetString("user.not_found", CultureInfo.GetCultureInfo("tr-TR"), out string? translated)
    ? translated!
    : throwSiteMessage; // never blank — the caller (14.Presentation) applies exactly this fallback
```

## InMemoryLocalizationCatalog — dictionary-backed default

```csharp
using SharedKernel.Localization;
using System.Globalization;

var catalog = new InMemoryLocalizationCatalog()
    .AddTranslation("order.not_found", CultureInfo.GetCultureInfo("en-US"), "Order not found.")
    .AddTranslation("order.not_found", CultureInfo.GetCultureInfo("tr"), "Sipariş bulunamadı.");

catalog.TryGetString("order.not_found", CultureInfo.GetCultureInfo("en-US"), out string? en); // "Order not found."
catalog.TryGetString("order.not_found", CultureInfo.GetCultureInfo("tr-TR"), out string? tr); // "Sipariş bulunamadı." — see fallback below
catalog.TryGetString("order.not_found", CultureInfo.GetCultureInfo("de-DE"), out string? de); // null — no de/de-DE/invariant entry registered
```

### Culture fallback — the one behavior the original design left unspecified

Keying by `(code, CultureInfo.Name)` alone says nothing about what happens when a request asks for `tr-TR` but only `tr` was registered — the single most likely real-world case, since a request's culture is usually the specific one (`Accept-Language: tr-TR`) while a service typically seeds only the neutral one it actually translated (`tr`). `InMemoryLocalizationCatalog` resolves this the same way `ResourceManager`/`IStringLocalizer` do: a lookup for a specific culture (`tr-TR`) walks up through each parent culture (`tr`) and finally `CultureInfo.InvariantCulture` before giving up. Seed a translation under `CultureInfo.InvariantCulture` to provide one universal default reached by every culture with no more specific entry of its own:

```csharp
var catalog = new InMemoryLocalizationCatalog()
    .AddTranslation("generic.error", CultureInfo.InvariantCulture, "Something went wrong.")
    .AddTranslation("generic.error", CultureInfo.GetCultureInfo("tr"), "Bir şeyler yanlış gitti.");

catalog.TryGetString("generic.error", CultureInfo.GetCultureInfo("tr-TR"), out string? v1); // "Bir şeyler yanlış gitti." (tr, not invariant — nearest match wins)
catalog.TryGetString("generic.error", CultureInfo.GetCultureInfo("fr-FR"), out string? v2); // "Something went wrong." (falls all the way to invariant)
```

This fallback behavior is specific to `InMemoryLocalizationCatalog` — the `ILocalizationCatalog` interface contract itself does not mandate it; a different implementation is free to require an exact `(code, culture)` match only.

Code lookup (`code`) is case-sensitive (ordinal), consistent with how `Error.Code` string values are compared everywhere else on the platform.

## StringLocalizerLocalizationCatalog — composing with `.resx` tooling

```csharp
using Microsoft.Extensions.Localization;
using SharedKernel.Localization;
using System.Globalization;

public sealed class ErrorMessages; // marker type — matches ErrorMessages.resx / ErrorMessages.tr.resx, etc.

// Composition root (e.g. a service already using Microsoft.Extensions.Localization for .resx files):
services.AddLocalization(options => options.ResourcesPath = "Resources");
services.AddStringLocalizerCatalog<ErrorMessages>();

// Anywhere ILocalizationCatalog is injected:
public sealed class ExampleUsage(ILocalizationCatalog catalog)
{
    public string Resolve(string code, CultureInfo culture, string throwSiteMessage) =>
        catalog.TryGetString(code, culture, out string? translated) ? translated! : throwSiteMessage;
}
```

`StringLocalizerLocalizationCatalog` never forwards `LocalizedString.Value` blindly. When a resource key is missing, `IStringLocalizer` returns a `LocalizedString` whose `Value` falls back to the requested key itself (e.g. the raw `"user.not_found"` code) with `ResourceNotFound = true`. Forwarding that unchecked would make `TryGetString` report a "found" translation that is really just the error code echoed back — the exact inversion of the never-blank contract above. `TryGetString` always checks `ResourceNotFound` first and returns `false`/`null` whenever it is `true`, regardless of what `Value` contains.

Because `IStringLocalizer`'s indexer in this framework version carries no per-call culture parameter, `TryGetString` temporarily sets the ambient `CultureInfo.CurrentUICulture` to the requested culture for the duration of the (fully synchronous) lookup and restores the original value in a `finally` block.

## Naming — deliberately not `AddSharedKernelLocalization`

This package's registration methods are `AddInMemoryLocalizationCatalog(...)` and `AddStringLocalizerCatalog<TResource>()` — never `AddSharedKernelLocalization()`. That name is reserved for `13.ServiceDefaults`'s culture-*resolution* middleware entry point (P-483): resolving which culture an inbound request is in (`UserPreference` claim → tenant `DefaultCulture` → `Accept-Language` header). This package answers a different question — given an already-known `(code, culture)` pair, what is the translated message — and the two names must never collide across domains.

## Rules

- `TryGetString` never throws for an unregistered/untranslated lookup — only for a genuinely invalid call (null/empty `code`, null `culture`), which is a programming-error guard, not a "not found" outcome.
- Neither implementation ever returns an empty or whitespace-only string as a "found" translation.
- This package applies no fallback itself — it only ever signals "found" or "not found". The throw-site-message fallback is applied entirely by the caller (`14.Presentation`'s `Error.ToProblemDetails()`, P-484).
- `01.Core.Primitives.Error` is completely unchanged by this package's existence — no new property, no breaking change.

## Package

Part of [Platform.SharedKernel](https://github.com/Gresta-Vertex-Labs/platform-shared-kernel) — see the [01.Core README](../README.md) for the full capability overview.
