---
name: project-nuget-packaging-parity
description: WO-050 Phase 38 (NuGetPackagingParity, COMPLETE) — NuGet global-packages-cache same-version gotcha, consumer-verify PackageReference rebuild pattern, README authoring shape
metadata:
  type: project
---

# WO-050 Phase 38 — NuGetPackagingParity (COMPLETE, 2026-07-29)

Brought all 7 shipped `02.Caching` packages to full NuGet metadata parity, authored 5
missing `README.md`s, and rebuilt `02.Caching/consumer-verify` from scratch (it had
silently rotted since the Phase 14 rename/WO-023 split — imported retired namespaces,
referenced a retired pre-rename `PackageId` pair). See [[project_caching_domain]] and
[[project_redis_package_split]] for the broader package architecture this sits on top of.

## Critical gotcha: NuGet global-packages-cache same-version immutability

After `dotnet pack`-ing all 7 packages to `./nupkgs` (repo-root local feed) with fresh
content at the SAME version number (`1.0.0` — this repo has no versioning scheme yet,
see [[reference_local_nuget_feed]]), `consumer-verify`'s `dotnet build` failed with
`CS0433`/`CS1929`/`CS0121` errors pointing at an OLD, retired namespace
(`SharedKernel.Caching.Extensions.ICachingBuilder`) that no longer exists in the source
tree at all. Root cause: NuGet's global-packages cache
(`dotnet nuget locals global-packages --list` → `~/.nuget/packages/`) treats a given
`PackageId`+`Version` as immutable and permanently cached — it does **not** re-check the
local feed's file content on restore once that exact version has been extracted once.
Two of the 7 packages (`SharedKernel.Caching`, the pre-Phase-14-rename retired
`PackageId`, and `SharedKernel.Caching.Redis`) had stale `1.0.0` content cached from
months earlier, and `dotnet restore` silently kept serving that stale cache instead of
the freshly-packed nupkg sitting right there in `./nupkgs`.

**Fix:** delete the specific stale package folders from the global cache before
re-restoring:
```
cd ~/.nuget/packages
rm -rf sharedkernel.caching sharedkernel.caching.abstractions sharedkernel.caching.fusioncache \
       sharedkernel.caching.redis sharedkernel.caching.redis.core \
       sharedkernel.caching.redis.distributedlocking sharedkernel.caching.redis.hashstore \
       sharedkernel.caching.redis.pubsub
```
(folder names are always lowercased PackageId). Then `rm -rf obj bin` in the consuming
project and `dotnet restore --force` before rebuilding. **Apply this proactively any
time a phase re-packs an already-cached PackageId+Version** — don't wait for the
confusing downstream compile error to diagnose it from scratch again. This is a NuGet
behavior, not something `NuGet.Config`/`dotnet pack` flags can prevent.

## consumer-verify rebuild pattern (post-rot)

- **`PackageReference` against the local nupkg feed, never `ProjectReference`** — this
  is `02.Caching`'s deliberate, documented divergence from `08.Storage/consumer-verify`'s
  `ProjectReference` pattern (see that domain's own consumer-verify). The whole point of
  this harness is proving the *packed* artifact resolves/composes for a downstream
  consumer; `ProjectReference` would compile regardless of any packaging defect and
  silently prove nothing.
- All 5 composition surfaces (L1-only / L1+L2 / locking-only / hash-store-only /
  pub/sub-only) must go through a real `Host.CreateApplicationBuilder()` →
  `builder.Build()` → `await host.StartAsync()` — never `new
  ServiceCollection().BuildServiceProvider()` alone.
- One shared `Testcontainers.Redis` container (`new RedisBuilder("redis:7.4").Build()`
  — the parameterless ctor is `[Obsolete]` as of the 4.13.x pin, matches
  `16.Testing`'s own `RedisContainerFixture` image tag) started once, reused across the
  4 Redis-backed surfaces, disposed via `await using` at the very end of the top-level
  script.
- Locking-only and hash-store-only/pub-sub-only surfaces deliberately do NOT take
  FusionCache — they compose the minimal Redis.Core + one capability package only,
  proving the WO-023 split's whole point (a consumer can take any subset). Locking-only
  uses the documented `[Obsolete]` `IServiceCollection` shim
  (`AddCachingCoreOptions(...).AddRedisDistributedLocking(...)`) rather than hand-rolling
  an `ICachingBuilder` — that shim IS the domain's own documented standalone
  no-FusionCache consumption path (Phase 27), warts (`CS0618`) and all; suppress with a
  narrow `#pragma warning disable/restore CS0618` rather than avoiding the shim.
- Hash-store-only and pub/sub-only surfaces need a tiny local `internal sealed class
  ConsumerCachingBuilder(IServiceCollection services) : ICachingBuilder` — this is NOT
  something to import from `16.Testing` (production code, including a consumer-verify
  harness meant to mirror a *real downstream consumer*, must never reference
  `16.Testing`). Every extraction package in this domain already defines its own
  ~3-line copy of this same adapter internally (`RedisLockCachingBuilder`,
  `TestCachingBuilder` in each `.Tests` project) — mirror that, don't share it.
- Pub/sub-only must call `.AddRedisChannelService().AddRedisCacheInvalidationBus()` but
  **never** `.AddCacheInvalidationReceiver()` — that extension's own startup guard
  requires `ICacheService` to already be registered, which a pub/sub-only (no
  FusionCache) consumer never has. This is the exact scenario the Phase 36 package split
  was designed to support.
- Each surface should run inside a `try/catch` wrapper (`RunSurfaceAsync`) that records
  failures into a list and continues to the next surface — a genuine improvement over
  `08.Storage/consumer-verify`'s literal code (which has no such wrapper and would abort
  at the first thrown `Verify` exception); the phase's own acceptance rule explicitly
  asked for "a failure in one surface must not prevent the harness from reporting which
  of the 5 surfaces failed."
- For the L1+L2 surface, don't just assert the `GetOrSetAsync` return value — also
  resolve `IConnectionMultiplexer` and check `db.KeyExistsAsync($"v2:{key}")` (the
  documented Phase 20 L2 key format, `KeyPrefix` empty by default) to prove the write
  actually reached the real Redis L2 backplane, not merely L1. Cheap, and it's exactly
  the kind of check a packaging-parity harness exists to make.

## README.md authoring shape (5 new files, Phase 38)

Mirrors the two pre-existing READMEs (`SharedKernel.Caching.FusionCache/README.md`,
`SharedKernel.Caching.Redis/README.md`) but scaled down for simpler packages: title,
one-paragraph purpose statement, install snippet (`dotnet add package` +
`<PackageReference>`), a minimal DI-registration + usage code sample matching that
package's own documented consumption pattern from `CLAUDE.md`'s "DI Registration"
section, a short "Layering" fenced diagram, and a closing link to the repo README. Do
not attempt a full API reference in the README — `GenerateDocumentationFile` + XML doc
comments already cover that; the README's job is orientation only.

## Metadata bar template (5 packages: Abstractions + 4 Redis.* extraction packages)

Verbatim property order, copied from the already-shipped `Redis.Core`/`.DistributedLocking`/
`.HashStore`/`.PubSub` blocks (Phases 32-36) plus `PackageReadmeFile` inserted right after
`PackageLicenseExpression`:
```xml
<PackageLicenseExpression>MIT</PackageLicenseExpression>
<PackageReadmeFile>README.md</PackageReadmeFile>
<RepositoryType>git</RepositoryType>
<RepositoryUrl>https://github.com/Gresta-Vertex-Labs/platform-shared-kernel</RepositoryUrl>
<PackageProjectUrl>https://github.com/Gresta-Vertex-Labs/platform-shared-kernel</PackageProjectUrl>
<Copyright>Copyright © 2026 Gresta-Vertex-Labs</Copyright>
<GenerateDocumentationFile>true</GenerateDocumentationFile>
<IncludeSymbols>true</IncludeSymbols>
<SymbolPackageFormat>snupkg</SymbolPackageFormat>
```
plus a matching `<ItemGroup><None Include="README.md" Pack="true" PackagePath="\" /></ItemGroup>`
placed right after the main `PackageReference`/`ProjectReference` `ItemGroup`, before the
`<!-- Exclude nested test project -->` `ItemGroup`. `TreatWarningsAsErrors` is explicitly
OUT OF SCOPE for this kind of packaging-parity phase — don't add it as a side effect
even though `08.Storage`'s own csproj files have it; the phase's own rules call this out
explicitly to avoid surfacing unrelated pre-existing warnings as new build failures.

## Mangled em-dash encoding defect

`SharedKernel.Caching.Abstractions.csproj` (and, not yet fixed as of this phase,
`SharedKernel.Caching.FusionCache.csproj`'s `Copyright Â© 2026` line) had a
double-encoding artifact (`â€”` instead of `—`, and `Â©` instead of `©`) in inline XML
comments — a UTF-8-decoded-as-Latin1-then-re-saved-as-UTF-8 mistake from an earlier
session. Only fix the ones explicitly named in the phase's task list — don't
opportunistically fix every instance you notice elsewhere in the domain (FusionCache's
own `Â©` was left untouched this session since NP-01 only named Abstractions's two
inline-comment instances).
