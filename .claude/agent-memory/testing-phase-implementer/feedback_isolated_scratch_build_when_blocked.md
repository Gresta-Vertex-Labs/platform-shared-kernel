---
name: feedback_isolated_scratch_build_when_blocked
description: How to verify your own 16.Testing code compiles/passes when SharedKernel.Testing.csproj's own build is blocked by an unrelated, out-of-jurisdiction ProjectReference (another domain's dispatched-but-not-yet-landed phase).
type: feedback
---

> WO-086 (2026-09): `SharedKernel.Testing` was split into 20 packable Testing-tier packages (core `SharedKernel.Testing` + 19 `SharedKernel.{Capability}.Testing`) plus the non-packable `SharedKernel.Testing.Internal` (containers, EF/Npgsql/audit helpers, MassTransit harness); `SharedKernel.Testing.SelfTests` became each package's own nested `.Tests` project. Paths and project names below are pre-split history; the technique/lesson still applies.

`SharedKernel.Testing.csproj` takes direct `ProjectReference`s to MANY domains' concrete packages (not just abstractions) — e.g. `SharedKernel.Security.Totp`, `SharedKernel.Messaging.MassTransit`, `SharedKernel.Persistence.EfCore`. When one of those referenced packages has its OWN compile error (a sibling phase in the same coordinated work order, dispatched to that domain but not yet landed), `dotnet build 16.Testing/SharedKernel.Testing/SharedKernel.Testing.csproj` fails even though nothing in `16.Testing` itself is wrong. This is expected and pre-flagged by the phase brief in these coordinated-wave scenarios — do not chase it, do not fix the other domain's file.

**Why:** Confirmed 2026-09-09 on P-527/WO-083: `01.Core`'s P-514 (`ITotpReplayGuard`'s breaking single-atomic-member migration) had a third blast-radius item the commit's own message admitted to — a genuine production call site in `12.Security/SharedKernel.Security.Totp/Challenge/TotpChallengeService.cs` (line 70, `ct` passed positionally into `TotpVerifier.VerifyAsync`'s newly-grown `digits` slot). `SharedKernel.Testing.csproj` references `SharedKernel.Security.Totp.csproj` (needed for `Security/FakeTotpChallengeStore.cs`, unrelated to the phase at hand), so that ONE unrelated CS1503 blocks the whole `SharedKernel.Testing.csproj`/`SharedKernel.Testing.SelfTests.csproj` build chain. Confirmed via an isolated `dotnet build` of `SharedKernel.Security.Totp.csproj` alone that it was the ONLY error anywhere in that dependency graph.

**How to apply:** When your own domain's build is blocked this way and the brief tells you not to touch the other domain's file:
1. Build the suspect referenced project *alone* (`dotnet build <that>.csproj`) to confirm it really is the sole/full error set, not something masking a second problem.
2. To still prove YOUR OWN new/changed `.cs` files are correct (including a concurrency proof, which needs `xunit`/`dotnet test`, not just `dotnet build`), create a throwaway scratch project OUTSIDE the repo (in the session scratchpad dir) that:
   - Targets `net10.0`, sets `ImplicitUsings`/`Nullable` as usual.
   - Takes `ProjectReference`s ONLY to the specific upstream `.csproj` files your changed code actually needs (e.g. `01.Core/SharedKernel.Cryptography.csproj`, `SharedKernel.Primitives.csproj`) — never the broken one.
   - Copies (not moves) your changed `.cs` files and their test file into matching subfolders — the namespaces don't need to change.
   - Adds explicit `PackageReference`s for `xunit`/`xunit.runner.visualstudio`/`Microsoft.NET.Test.Sdk` at the exact versions pinned in the repo's `Directory.Packages.props` (no central package management outside the repo, so versions must be explicit), plus a one-line `GlobalUsings.cs` (`global using Xunit;`) since the repo's real test projects get that from a generated file this scratch project won't have.
3. `dotnet test` the scratch project. A clean pass (including any `Barrier`-synchronized concurrency tests) is real evidence your own code is correct, independent of the unrelated blocker.
4. Delete the scratch project afterward — it's throwaway, never leave it behind (mirrors the `Containers/` "manually smoke-test, never leave a test file behind" rule).
5. Record in `state-map.md`'s phase narrative that the domain's real `.csproj`/`.SelfTests.csproj` build stays red until the other domain's dispatched fix lands, and cite exactly which upstream phase (e.g. `P-528`) is expected to unblock it — don't claim "0 errors" for a build you couldn't actually run end-to-end.

This is a variant of the existing `feedback_check_already_done_before_working`/`feedback_verify_live_source` discipline: verify against reality (an actual compile+test run), never against the brief's framing or a partial signal, even when the "normal" verification path is unavailable.
