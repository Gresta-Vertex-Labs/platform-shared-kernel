---
name: project-istringlocalizer-no-withculture
description: Microsoft.Extensions.Localization.Abstractions 10.0.11's IStringLocalizer has no per-call culture parameter — confirmed by reflecting over the installed assembly during P-482
type: project
---

`Microsoft.Extensions.Localization.Abstractions` 10.0.11 (the version pinned in this repo's
`Directory.Packages.props`) ships `IStringLocalizer` with exactly these members: `this[string name]`,
`this[string name, object[] arguments]`, and `GetAllStrings(bool includeParentCultures)`. There is no
`WithCulture(CultureInfo)` method on the interface in this framework version — it was removed from
`IStringLocalizer` in .NET Core 3.0 and never came back. Confirmed by loading the real DLL
(`pkg/lib/net10.0/Microsoft.Extensions.Localization.Abstractions.dll`, fetched straight from
nuget.org's flat-container API) and reflecting over its public members, not assumed from memory or
older docs that still show `WithCulture`.

**Why this matters:** any adapter wrapping `IStringLocalizer` that needs to resolve a translation for
an explicit, caller-supplied `CultureInfo` (rather than whatever the ambient culture happens to be)
must temporarily set `CultureInfo.CurrentUICulture` before the lookup and restore it in a `finally`
block — there is no cleaner per-call alternative. `SharedKernel.Localization`'s
`StringLocalizerLocalizationCatalog` (`01.Core/SharedKernel.Localization/StringLocalizerLocalizationCatalog.cs`)
does exactly this, and its XML docs/tests document the caveat: safe for a single synchronous call
(nothing else runs on the thread mid-swap), but relies entirely on `CurrentUICulture`'s own
thread/async-local semantics for concurrent callers.

**How to apply:** any future 01.Core/12.Security/13.ServiceDefaults/14.Presentation work that wraps
`IStringLocalizer`/`IStringLocalizerFactory` directly should assume this same constraint rather than
re-discovering it — check the actual installed assembly's members first if the pinned version ever
changes, since a hypothetical future SDK version could reintroduce a per-call culture parameter.
