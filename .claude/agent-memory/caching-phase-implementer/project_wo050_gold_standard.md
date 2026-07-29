---
name: project-wo050-gold-standard
description: WO-050 gold-standard follow-up (Phases 38-41) — Testcontainers.Redis version-pin gotcha, confirmed FusionCache tag-invalidation mechanism, MONITOR diagnostic technique
metadata:
  type: project
---

# WO-050 — Caching Gold-Standard Follow-Up (Phases 38-41)

Four independent phases dispatched from a direct root architecture review, none blocking
each other: Phase 38 (P-301, NuGetPackagingParity, complete), Phase 39 (P-302,
CrossInstanceTagInvalidation, complete), Phase 40 (P-303, BatchOperationsParallelization,
pending), Phase 41 (P-304, OtelTracingSpans, pending).

## Testcontainers.Redis version-pin gotcha (found + fixed in Phase 39)

**Check this FIRST before running any Redis-container-based test in this domain** — it may
regress again if a `.Tests.csproj` is copy-pasted from an old template.

- `16.Testing/SharedKernel.Testing.csproj` and `02.Caching/consumer-verify` both pin
  `Testcontainers.Redis` at `4.13.0`.
- All four Redis-container `.Tests` projects (`SharedKernel.Caching.Redis.Tests`,
  `.Redis.DistributedLocking.Tests`, `.Redis.HashStore.Tests`, `.Redis.PubSub.Tests`) had a
  **stale direct `<PackageReference Include="Testcontainers.Redis" Version="4.4.0" />`**
  left over from early project scaffolding (project_test_patterns.md's old "Testcontainers
  Redis package version: 4.4.0" note — now corrected there too).
- The conflict is silent at `dotnet build` (only an `MSB3277`/`NU1605` warning, not an
  error) but fatal at test **runtime**: MSBuild copies the lower direct-referenced
  `Testcontainers.Redis.dll` (4.4.0) into the test output next to the higher transitive
  core `Testcontainers.dll`/`DotNet.Testcontainers` (4.13.0, pulled in via
  `SharedKernel.Testing`), and `RedisConfiguration`'s constructor in the old
  `Testcontainers.Redis` 4.4.0 calls a `ContainerConfiguration` constructor overload that
  no longer exists in the 4.13.0 core — `MissingMethodException` inside every single
  `[Fact]`'s constructor (the container field initializer), not a clean build failure.
- **Fix:** bump the direct `PackageReference` to `Testcontainers.Redis` `4.13.0` in all
  four `.Tests.csproj` files. Zero production code involved — test-project package pin
  only. `SharedKernel.Caching.Redis.Core.Tests` was unaffected (it doesn't reference
  `Testcontainers.Redis` directly at all).
- **Diagnostic tell:** if a pre-existing, previously-known-passing Redis integration test
  (e.g. `RedisL2IntegrationTests`) suddenly throws `MissingMethodException` from
  `RedisConfiguration..ctor()`/`RedisBuilder..ctor()`, check this version-pin conflict
  first before assuming a real regression in cache/backplane logic.
- Build-time workaround for the *separate*, long-standing, unrelated `NU1605` package
  downgrade errors from `SharedKernel.Testing`'s transitive floor (documented since Phase
  37): `dotnet build`/`dotnet test -p:NoWarn=NU1605 -p:MSBuildTreatWarningsAsErrors=false`.
  This does NOT fix the Testcontainers.Redis MissingMethodException — that needs the
  actual version bump above, not a warning suppression.

## FusionCache RemoveByTagAsync cross-instance mechanism (confirmed empirically, Phase 39)

Proven via a genuine two-independent-`ServiceProvider` test
(`SharedKernel.Caching.Redis.Tests/Integration/CrossInstanceTagInvalidationTests.cs`) that
**passed against the current, unmodified `AddRedisL2`/`AddSharedKernelCaching` wiring with
zero changes needed**. Confirmed the carrying mechanism by attaching `redis-cli monitor`
to a fixed-port diagnostic Redis container (not from FusionCache source/docs alone):

- `RemoveByTagAsync(tag)` is **not** a per-key sweep. It writes one ordinary FusionCache
  entry to a reserved, tag-scoped key — `{KeyPrefix}v2:__fc:t:{tag}` — whose value is a
  "clear before this timestamp" marker, then publishes that single write over the **same**
  shared backplane channel (`FusionCache.Backplane:v2` for the default/unnamed cache used
  by `AddSharedKernelCaching`) as any other `Set`/`Remove` call.
- Every FusionCache entry (tagged or not) carries its own creation `Timestamp` in its
  serialized envelope. On every subsequent read, FusionCache compares the entry's
  `Timestamp` against its tag's marker value; an entry that predates the marker is treated
  as logically expired (factory re-invoked / fresh L2 read), exactly like a TTL expiry.
- Because the marker write rides the identical L2-write-plus-backplane-broadcast path
  `AddRedisL2` already wires for regular data, **there is no separate tagging-specific
  channel, option, or opt-in to configure**. `FusionCacheEntryOptions.SkipBackplaneNotifications`
  (default `false`) is the only knob that could disable this, and neither `AddRedisL2` nor
  `AddSharedKernelCaching` sets it.
- `CachingCoreOptions.ServiceName` does **not** affect this guarantee — that option only
  drives `.Redis.PubSub`'s own channel-naming convention, an unrelated mechanism. Both test
  instances still pin the same `ServiceName` to keep the topology realistic (two pods of
  one microservice), but it isn't load-bearing for tag propagation itself.
- Full statement now lives in `02.Caching/CLAUDE.md`'s "Core cache rules" bullet — read
  that first before re-deriving this from FusionCache source in a future phase.

## MONITOR-based empirical diagnosis technique (reusable for future Redis-behavior questions)

When FusionCache/Redis wire-level behavior is unclear from docs/XML-doc comments alone:
1. `docker run -d --rm --name diag-redis -p {port}:6379 redis:7-alpine`
2. `(docker exec diag-redis redis-cli monitor > /path/to/log 2>&1 &)` — background,
   redirect to a host file (not `docker exec -d`, which doesn't let you redirect to a host
   file easily).
3. Write a throwaway xUnit `[Fact]` pointing at the fixed port (not Testcontainers'
   ephemeral port) reproducing the exact scenario; run it via `dotnet test --filter`.
4. `grep` the captured log for `PUBLISH`/`SUBSCRIBE`/`HMSET`/`HMGET` lines — this reveals
   actual key names, channel names, and payload structure without needing to decompile the
   FusionCache DLL.
5. **Delete the throwaway test and the diagnostic container/monitor process afterward** —
   it must never ship as part of the permanent test suite (it duplicates the real,
   black-box `CrossInstanceTagInvalidationTests.cs` and couples to internals the real test
   deliberately avoids coupling to).
