---
name: scaffold_preexisting_build_noise
description: Pre-existing build warnings/errors in the repo that are unrelated to 15.Integration work and must not be misattributed
metadata:
  type: project
---

Running `dotnet build Platform.SharedKernel.slnx` produces 6 NU1605 (package downgrade) errors in unrelated `02.Caching.Redis*` test projects, plus NU1903 (SQLitePCLRaw advisory) warnings on every `.Tests` project that references `SharedKernel.Testing` (via its EF Core Sqlite chain).

**Why this matters:** Confirmed via `git stash` + rebuild that these exist on `main` before any 15.Integration scaffold change — they are not introduced by this domain's work. Don't waste a phase chasing them, and don't report them as failures caused by 15.Integration changes. Always build the **specific target project(s)** for this domain (not the whole `.slnx`) to get a clean signal: `dotnet build 15.Integration/SharedKernel.Integration.Webhooks/SharedKernel.Integration.Webhooks.csproj` and the `.Tests` csproj individually report 0 errors.

**How to apply:** For S-06-style "verify dotnet build succeeds with zero errors and zero warnings" tasks, scope the build command to the package(s) actually in this phase's deliverable list, not the full solution — the full-solution build will always show pre-existing unrelated noise from other domains' in-flight or known-vulnerable dependencies.
