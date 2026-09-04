---
name: project_18idempotency_first_implementation
description: State of 18.Idempotency after the first implementation session (2026-09-04) covering P-454/P-455 — what shipped, what's verified, what's still open.
type: project
---

On 2026-09-04, `18.Idempotency` went from docs-only (only `CLAUDE.md`/`state-map.md` existed) to two fully-implemented, building, packing packages: `SharedKernel.Idempotency.Redis` and `SharedKernel.Idempotency.EfCore`. This closed the root Phase Backlog's P-454/P-455 (WO-070) at the code level, but NOT yet at the state-map/backlog level.

**Why:** the domain exists to resolve a layering deadlock — see `18.Idempotency/CLAUDE.md`'s "Why This Domain Exists At All" for the full reasoning (never re-derive this from scratch, it's already written).

**What shipped and is verified (build + pack + tests actually run):**
- Both packages build clean with `TreatWarningsAsErrors=true`, zero warnings.
- Both packages `dotnet pack` clean, zero `<Version>` elements (repo-wide MinVer convention respected).
- Design, Scaffold, Core, Docs phases are `●` in `18.Idempotency/state-map.md`.
- 40 unit/DI-resolution tests run green (20 per package) — see [[reference_fail_open_tests_no_docker]].
- T-05/T-10 (fail-open/fail-closed against a genuinely unreachable endpoint) — real passing evidence, no Docker needed.

**What's written but NOT verified (the actual gap):**
- T-01–T-04 (`.Redis`) and T-06–T-09 (`.EfCore`) — the Testcontainers-backed concurrent-reservation/tenant-isolation/expiry-reclaim proofs — are written in full (`RedisIdempotencyConcurrencyTests.cs`, `EfCoreIdempotencyConcurrencyTests.cs`) but have never executed. No Docker daemon was reachable in the implementing session.
- P-03 consumer-verify harness (a real standalone executable proving `IHost.StartAsync()` DI resolution, per the platform's established convention) was not built — only the weaker in-process xUnit equivalent exists.
- `state-map.md` Tests/Published phases are deliberately left `◐`, and root Phase Backlog P-454/P-455 deliberately left untouched — do not force-close these without actually running the Docker-dependent tests first.
- Neither package is registered in `Platform.SharedKernel.slnx` — this was out of scope under the shared-file protocol in effect (other domain implementers were running concurrently and the `.slnx` was explicitly off-limits). New project paths that need registration:
  - `18.Idempotency/SharedKernel.Idempotency.Redis/SharedKernel.Idempotency.Redis.csproj`
  - `18.Idempotency/SharedKernel.Idempotency.Redis/SharedKernel.Idempotency.Redis.Tests/SharedKernel.Idempotency.Redis.Tests.csproj`
  - `18.Idempotency/SharedKernel.Idempotency.EfCore/SharedKernel.Idempotency.EfCore.csproj`
  - `18.Idempotency/SharedKernel.Idempotency.EfCore/SharedKernel.Idempotency.EfCore.Tests/SharedKernel.Idempotency.EfCore.Tests.csproj`

**How to apply:** the very next task in this domain, whenever Docker is available, should be: run both `.Tests` projects in full (not filtered), fix any failures the real containers surface (a written-but-unexecuted concurrency test is unproven, not necessarily correct), then mark `SK.18.Tests` → `●` via `state-map-phase`, build the P-03 consumer-verify harness, and only then let promotion propagate to close P-454/P-455 in the root backlog.
