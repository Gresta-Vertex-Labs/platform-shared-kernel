---
name: project-core-domain
description: 01.Core implementation status — five packages, current phase progress, Guards package details
metadata:
  type: project
---

The 01.Core domain contains five independently publishable packages:
- `SharedKernel.Primitives` — Result<T>, Error, IClock, SmartEnum, ValidationResult
- `SharedKernel.Core` — Base exceptions, railway extensions, BCL helpers
- `SharedKernel.Guards` — Two-path guard system (Against.* functional, Throw.* imperative)
- `SharedKernel.Configuration` — AddValidatedOptions startup-validation pattern
- `SharedKernel.FeatureManagement` — IFeatureManager abstraction over Microsoft.FeatureManagement

As of 2026-05-15 the domain has completed: Design, Scaffold, Core, Tests, Docs phases (SK.01.Docs all 6 tasks ●).
Published phase (SK.01.Published) has 3 tasks remaining: P-07 (NuGet metadata for Guards), P-08 (pack/publish Guards), P-09 (consumer verification).

**Why:** SharedKernel.Guards was added in P-003 after the original four packages were fully published.

**How to apply:** When implementing the Published phase for 01.Core, focus is on Guards package only (P-07, P-08, P-09). The original four packages are already published.
