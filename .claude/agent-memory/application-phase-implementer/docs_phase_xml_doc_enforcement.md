---
name: docs_phase_xml_doc_enforcement
description: SK.05.Docs phase pattern — enabling GenerateDocumentationFile/TreatWarningsAsErrors as a build-enforced doc-coverage gate, and the two doc gaps it caught
metadata:
  type: project
---

SK.05.Docs phase (2026-06-29) for `SharedKernel.Application`/`SharedKernel.Application.Behaviors`
followed the convention already used by `06.Persistence` and `07.Messaging`: rather than treating
"100% XML doc coverage" as a one-time manual audit, set `<GenerateDocumentationFile>true</
GenerateDocumentationFile>` and `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` on the
production `.csproj` (never the test `.csproj`) so a missing doc comment (CS1591) or broken `<see
cref="..."/>` (CS1574) fails `dotnet build` going forward — doc coverage becomes mechanically
enforced, not just audited at Docs-phase time.

**Full NuGet metadata block added at the same time** (PackageId, Version, Authors, Company,
Product, Description, PackageTags, PackageLicenseExpression, RepositoryType/Url,
PackageProjectUrl, Copyright, IncludeSymbols, SymbolPackageFormat=snupkg) — this technically
overlaps with the Published phase's P-01/P-02 tasks, but is harmless to set early and was needed
because `GenerateDocumentationFile`/`TreatWarningsAsErrors` alone, without the rest of the metadata
block, would look like an incomplete copy of the sibling-domain pattern. Published phase will find
this already done.

**Two real doc gaps this caught in a Core phase that was otherwise "complete":**
1. `DomainEventNotificationHandler.cs`'s `<see cref="ApplicationServiceCollectionExtensions.
   AddDomainEventHandler{TDomainEvent,THandler}"/>` was unqualified — the class lives in
   `SharedKernel.Application.Extensions` but the cref site doesn't import that namespace. Fix:
   fully qualify including the parameter list:
   `SharedKernel.Application.Extensions.ApplicationServiceCollectionExtensions.
   AddDomainEventHandler{TDomainEvent,THandler}(Microsoft.Extensions.DependencyInjection.
   IServiceCollection)`.
2. All seven pipeline behaviors (`ValidationBehavior`, `LoggingBehavior`, `MetricsBehavior`,
   `TransactionBehavior`, `CachingBehavior`, `AuthorizationBehavior`, `IdempotentCommandBehavior`)
   had a fully-documented class-level `<summary>`/`<remarks>` but their `Handle(...)`
   implementation of `IPipelineBehavior<TRequest,TResponse>.Handle` had NO doc comment at all —
   CS1591 fires per-member, not just per-type. Fix: add a bare `/// <inheritdoc/>` immediately
   above each `Handle` method. `<inheritdoc/>` satisfies CS1591 even though
   `IPipelineBehavior<,>.Handle` itself (from MediatR) has no XML doc to actually inherit text
   from — the compiler only checks that *a* doc comment exists, it doesn't require `<inheritdoc/>`
   to resolve to non-empty inherited text.

**Net result:** only 1 cref fix + 7 one-line `<inheritdoc/>` additions were needed across both
packages — the Core phase's doc discipline was otherwise already excellent. Both packages built
0 warnings/0 errors immediately after, and all 50 existing tests (18 + 32) remained green
throughout — no production logic touched.

See also [[seven_step_pipeline_implementation]] for the file layout this phase operated on.
