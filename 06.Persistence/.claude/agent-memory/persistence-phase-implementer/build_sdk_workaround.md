---
name: build-sdk-workaround
description: How to run dotnet build/test/pack in this repo when the pinned SDK isn't installed
type: feedback
---

`global.json` at the repo root pins SDK `10.0.300`, but this machine only has `10.0.103` and
`10.0.400` installed (confirmed 2026-09-02, 03.Domain and 06.Persistence sessions both hit this).
Running `dotnet build/test/pack` from inside the repo tree fails immediately with
"Requested SDK version: 10.0.300 ... not found."

**Why:** `global.json` SDK resolution walks up from the current working directory looking for
the file. Once outside the repo tree entirely (e.g. `cd C:/Users/<user>` or any non-repo
directory), no `global.json` is found and the highest installed SDK (10.0.400) resolves instead.

**How to apply:** Always invoke `dotnet build`/`dotnet test`/`dotnet pack` with the working
directory set OUTSIDE the repo (e.g. run from the user's home directory) and pass the full
absolute path to the target `.csproj`. Do not modify `global.json` — that's a devops-lead/
PLATFORM.md concern, not something a phase-implementer session should touch.

Example:
```
cd /c/Users/<user> && dotnet build "C:/.../06.Persistence/SharedKernel.Persistence.EfCore/SharedKernel.Persistence.EfCore.csproj" -c Release
```
