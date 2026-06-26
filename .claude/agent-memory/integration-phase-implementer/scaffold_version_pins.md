---
name: scaffold_version_pins
description: Version pins chosen for SharedKernel.Integration.Webhooks's new NuGet dependencies at Scaffold (SK.15.Scaffold)
metadata:
  type: project
---

`Microsoft.Extensions.Http` pinned to `10.0.9`; `Microsoft.Extensions.Http.Resilience` pinned to `10.7.0` in `15.Integration/SharedKernel.Integration.Webhooks/SharedKernel.Integration.Webhooks.csproj`.

**Why:** `Microsoft.Extensions.Http` 10.0.9 matches the platform-wide floor already used for every other `Microsoft.Extensions.*` package (confirmed via `SharedKernel.Messaging.Abstractions.csproj` and others — `10.0.5`→`10.0.9` bumps happened repo-wide to satisfy transitive floors from `SharedKernel.Testing`'s EF Core Sqlite chain). `Microsoft.Extensions.Http.Resilience` has no prior pin anywhere in the repo — `10.7.0` was the latest stable on the .NET 10 line at the time (checked via NuGet flat-container index: 10.0.0 → 10.7.0 available). AOT status of `.Http.Resilience` is unverified — `15.Integration/CLAUDE.md` already carries an explicit caveat to re-verify on every major bump (third-party, not BCL).

**How to apply:** Future phases (Core/Dispatch, C-13–C-14) wire `AddHttpClient(...).AddStandardResilienceHandler(...)` against this pinned version. If bumping either package, re-check the platform-wide `Microsoft.Extensions.*` floor first — `06.Persistence`/`07.Messaging` test projects have hit NU1605 downgrade errors when one project pins below what `SharedKernel.Testing`'s transitive chain demands.

See also [[scaffold_preexisting_build_noise]].
