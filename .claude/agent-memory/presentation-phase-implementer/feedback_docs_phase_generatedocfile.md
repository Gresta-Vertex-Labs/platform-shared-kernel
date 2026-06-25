---
name: docs-phase-generatedocfile
description: GenerateDocumentationFile must be explicitly enabled to validate XML doc comments — Core-phase "complete" docs can still hide unresolved cref warnings until the Docs phase turns the flag on
type: feedback
---

When a Docs phase task says "complete XML doc comments... zero CS1591 warnings," check whether the
`.csproj` actually has `<GenerateDocumentationFile>true</GenerateDocumentationFile>` set. Without it,
the compiler does not validate `///` comments at all — CS1591 (missing doc) and CS1574/CS1580
(unresolved `cref`) warnings are silently skipped, even though prose-complete comments already
exist from the Core phase.

**Why:** In WO-031's `14.Presentation` Docs phase, all public types/members already had full XML
doc prose written during Core phase — no stubs were found. But the flag wasn't set yet, so the
"zero CS1591" acceptance criterion was unverified. Turning it on surfaced 4 real unresolved-`cref`
warnings (`IHostEnvironment.IsDevelopment()`, `MapOpenApi(IEndpointRouteBuilder, string)`,
`HubOptions.HubFilters`, and an ambiguous `HttpContext` lacking a `using`) that had been sitting
invisible since Core.

**How to apply:** In any Docs-phase task, add `<GenerateDocumentationFile>true</GenerateDocumentationFile>`
to the PropertyGroup first, then rebuild — don't assume "the comments look complete" satisfies the
acceptance criterion. Fix unresolved crefs with `<c>plain text</c>` or fully-qualified cref paths
rather than adding new `using` directives that pull unrelated types into scope just to make a cref
resolve.
